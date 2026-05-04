#nullable enable
using AssetExtractor.Models;
using AssetExtractor.Export.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetExtractor.Export;

public class GltfMeshExporter : IMeshExporter
{
    private readonly ILogger<GltfMeshExporter> _logger;
    private int _outOfBoundsJoints;
    private int _duplicateJoints;
    private int _nonNormalizedWeights;
    private int _invalidWeights;

    public GltfMeshExporter(ILogger<GltfMeshExporter>? logger = null)
    {
        _logger = logger ?? NullLogger<GltfMeshExporter>.Instance;
    }

    public void AddMeshes(
        SceneBuilder scene,
        UnitData unitData,
        Dictionary<string, NodeBuilder> nodeBuilders)
    {
        var materialBuilders = CreateMaterials(unitData);

        var defaultMaterial = new MaterialBuilder("DefaultMaterial")
            .WithDoubleSide(true)
            .WithMetallicRoughnessShader()
            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1.0f));

        materialBuilders["DefaultMaterial"] = defaultMaterial;

        foreach (var meshData in unitData.Meshes)
        {
            int totalIndexCount = meshData.SubMeshes.Count > 0
                ? meshData.SubMeshes.Sum(sm => sm.Triangles.Length)
                : meshData.Triangles.Length;
            int triangleCount = totalIndexCount / 3;
            int primitiveCount = meshData.SubMeshes.Count > 0 ? meshData.SubMeshes.Count : 1;

            _logger.LogInformation("  Processing mesh: {MeshName} ({VertexCount} vertices, {TriangleCount} triangles, {PrimitiveCount} primitives)",
                meshData.Name, meshData.Vertices.Length, triangleCount, primitiveCount);

            if (meshData.Vertices.Length == 0 || totalIndexCount == 0)
            {
                _logger.LogWarning("  Skipping empty mesh: {MeshName}", meshData.Name);
                continue;
            }

            // Skip shadow receivers - Unity colors these at runtime, textureless in GLB causes artifacts
            if (ShouldSkipDefaultMaterialMesh(meshData, unitData))
            {
                _logger.LogInformation("  Skipping mesh with textureless DefaultMaterial: {MeshName}", meshData.Name);
                continue;
            }

            bool isSkinned = unitData.Skeleton != null &&
                           meshData.BoneWeights.Length > 0 &&
                           meshData.BoneIndices.Length > 0 &&
                           unitData.Skeleton.Bones.Count > 0;

            if (isSkinned)
            {
                _logger.LogInformation("  Exporting as SKINNED mesh");
                AddSkinnedMesh(scene, meshData, unitData.Skeleton!, nodeBuilders, materialBuilders, defaultMaterial);
            }
            else
            {
                _logger.LogInformation("  Exporting as RIGID mesh (no skeleton/weights)");
                AddRigidMesh(scene, meshData, nodeBuilders, materialBuilders, defaultMaterial);
            }
        }
    }

    public Dictionary<string, MaterialBuilder> CreateMaterials(UnitData unitData)
    {
        var materialBuilders = new Dictionary<string, MaterialBuilder>();

        foreach (var materialData in unitData.Materials)
        {
            var material = CreateMaterialFromData(materialData, unitData.Textures);
            materialBuilders[materialData.Name] = material;
        }

        return materialBuilders;
    }

    private void AddRigidMesh(
        SceneBuilder scene,
        MeshData meshData,
        Dictionary<string, NodeBuilder> nodeBuilders,
        IReadOnlyDictionary<string, MaterialBuilder> materialBuilders,
        MaterialBuilder defaultMaterial)
    {
        var meshBuilder = new MeshBuilder<VertexPositionNormalTangent, VertexTexture1, VertexEmpty>(meshData.Name);

        var subMeshes = meshData.SubMeshes.Count > 0
            ? meshData.SubMeshes
            : new List<SubMeshData> { new() { Triangles = meshData.Triangles, MaterialName = meshData.MaterialName } };

        var vertexCache = BuildVertexCache(meshData);

        // Unity CW → glTF CCW via reversed winding (v2,v1,v0). Also compensates X-axis flip.
        foreach (var subMesh in subMeshes)
        {
            if (subMesh.Triangles.Length == 0) continue;

            var material = !string.IsNullOrEmpty(subMesh.MaterialName) && materialBuilders.TryGetValue(subMesh.MaterialName, out var found)
                ? found : defaultMaterial;
            var prim = meshBuilder.UsePrimitive(material);

            switch (subMesh.Topology)
            {
                case Models.MeshTopology.Triangles:
                    for (int i = 0; i + 2 < subMesh.Triangles.Length; i += 3)
                    {
                        int idx0 = subMesh.Triangles[i], idx1 = subMesh.Triangles[i + 1], idx2 = subMesh.Triangles[i + 2];
                        if (idx0 >= vertexCache.Length || idx1 >= vertexCache.Length || idx2 >= vertexCache.Length) continue;
                        prim.AddTriangle(vertexCache[idx2], vertexCache[idx1], vertexCache[idx0]);
                    }
                    break;

                case Models.MeshTopology.TriangleStrip:
                    for (int i = 0; i < subMesh.Triangles.Length - 2; i++)
                    {
                        int idx0 = subMesh.Triangles[i], idx1 = subMesh.Triangles[i + 1], idx2 = subMesh.Triangles[i + 2];
                        if (idx0 == idx1 || idx0 == idx2 || idx1 == idx2) continue;
                        if (idx0 >= vertexCache.Length || idx1 >= vertexCache.Length || idx2 >= vertexCache.Length) continue;
                        // TriangleStrip: alternate winding every triangle
                        if ((i & 1) == 1) prim.AddTriangle(vertexCache[idx0], vertexCache[idx1], vertexCache[idx2]);
                        else prim.AddTriangle(vertexCache[idx2], vertexCache[idx1], vertexCache[idx0]);
                    }
                    break;

                case Models.MeshTopology.Quads:
                    for (int q = 0; q + 3 < subMesh.Triangles.Length; q += 4)
                    {
                        int idx0 = subMesh.Triangles[q], idx1 = subMesh.Triangles[q + 1];
                        int idx2 = subMesh.Triangles[q + 2], idx3 = subMesh.Triangles[q + 3];
                        if (idx0 >= vertexCache.Length || idx1 >= vertexCache.Length ||
                            idx2 >= vertexCache.Length || idx3 >= vertexCache.Length) continue;
                        prim.AddTriangle(vertexCache[idx2], vertexCache[idx1], vertexCache[idx0]);
                        prim.AddTriangle(vertexCache[idx3], vertexCache[idx2], vertexCache[idx0]);
                    }
                    break;

                default:
                    _logger.LogWarning("Unsupported mesh topology: {Topology}", subMesh.Topology);
                    break;
            }
        }

        // Animated map objects: attach to skeleton node (prefer HierarchyPath, fallback to Name)
        NodeBuilder? targetNode = null;

        if (!string.IsNullOrEmpty(meshData.HierarchyPath) && nodeBuilders.TryGetValue(meshData.HierarchyPath, out var nodeByPath))
        {
            targetNode = nodeByPath;
            _logger.LogInformation("  Attached mesh '{MeshName}' to skeleton node by path: {HierarchyPath}", meshData.Name, meshData.HierarchyPath);
        }
        else if (nodeBuilders.TryGetValue(meshData.Name, out var nodeByName))
        {
            targetNode = nodeByName;
            _logger.LogInformation("  Attached mesh '{MeshName}' to skeleton node by name", meshData.Name);
        }

        if (targetNode != null)
        {
            scene.AddRigidMesh(meshBuilder, targetNode);
        }
        else
        {
            scene.AddRigidMesh(meshBuilder, System.Numerics.Matrix4x4.Identity);
        }
    }

    private void AddSkinnedMesh(
        SceneBuilder scene,
        MeshData meshData,
        SkeletonData skeleton,
        Dictionary<string, NodeBuilder> nodeBuilders,
        IReadOnlyDictionary<string, MaterialBuilder> materialBuilders,
        MaterialBuilder defaultMaterial)
    {
        if (meshData.BindPoses.Length == 0)
        {
            _logger.LogWarning("No bind poses for mesh {MeshName}", meshData.Name);
        }

        _outOfBoundsJoints = 0;
        _duplicateJoints = 0;
        _nonNormalizedWeights = 0;
        _invalidWeights = 0;

        var meshBuilder = new MeshBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>(meshData.Name);
        var subMeshes = meshData.SubMeshes.Count > 0
            ? meshData.SubMeshes
            : new List<SubMeshData> { new() { Triangles = meshData.Triangles, MaterialName = meshData.MaterialName } };

        // Bone transforms with negative scale (reflection) flip winding at runtime. Pre-flip here.
        bool hasInvertedWinding = false;
        if (meshData.IsMapObject)
        {
            var scale = meshData.AccumulatedScale;
            float determinant = scale.X * scale.Y * scale.Z;
            hasInvertedWinding = determinant < 0;

            if (hasInvertedWinding)
            {
                _logger.LogInformation("Flipping winding for skinned map object: {MeshName} (det={Determinant:F1})", meshData.Name, determinant);
            }
        }

        NodeBuilder[] boneNodes = new NodeBuilder[meshData.BoneIndices.Length];
        var jointsWithBindPoses = new List<(NodeBuilder Joint, System.Numerics.Matrix4x4 InverseBindMatrix)>();

        for (int i = 0; i < meshData.BoneIndices.Length; i++)
        {
            int skeletonBoneIndex = meshData.BoneIndices[i];

            if (skeletonBoneIndex < 0 || skeletonBoneIndex >= skeleton.Bones.Count)
            {
                throw new Exception($"Invalid bone index {skeletonBoneIndex} for mesh '{meshData.Name}'");
            }

            var bone = skeleton.Bones[skeletonBoneIndex];
            if (!nodeBuilders.TryGetValue(bone.Path, out var boneNode))
            {
                throw new Exception($"Bone mapping failure for '{bone.Path}'");
            }

            boneNodes[i] = boneNode;

            if (i >= meshData.BindPoses.Length)
            {
                throw new Exception($"Bind pose missing for bone {i} in mesh '{meshData.Name}'");
            }

            var inverseBindMatrix = ConvertUnityMatrixToNumerics(meshData.BindPoses[i]);
            jointsWithBindPoses.Add((boneNode, inverseBindMatrix));
        }

        int invalidTriangleCount = 0;
        int totalIndexCount = 0;

        foreach (var subMesh in subMeshes)
        {
            if (subMesh.Triangles.Length == 0)
            {
                continue;
            }

            totalIndexCount += subMesh.Triangles.Length;

            MaterialBuilder material = defaultMaterial;
            if (!string.IsNullOrEmpty(subMesh.MaterialName) && materialBuilders.TryGetValue(subMesh.MaterialName, out var found))
            {
                material = found;
            }

            var prim = meshBuilder.UsePrimitive(material);

            switch (subMesh.Topology)
            {
                case Models.MeshTopology.Triangles:
                    for (int i = 0; i + 2 < subMesh.Triangles.Length; i += 3)
                    {
                        int idx0 = subMesh.Triangles[i];
                        int idx1 = subMesh.Triangles[i + 1];
                        int idx2 = subMesh.Triangles[i + 2];

                        if (idx0 >= meshData.Vertices.Length || idx1 >= meshData.Vertices.Length || idx2 >= meshData.Vertices.Length)
                        {
                            invalidTriangleCount++;
                            continue;
                        }

                        var v0 = CreateSkinnedVertex(meshData, idx0, boneNodes);
                        var v1 = CreateSkinnedVertex(meshData, idx1, boneNodes);
                        var v2 = CreateSkinnedVertex(meshData, idx2, boneNodes);

                        if (hasInvertedWinding)
                            prim.AddTriangle(v0, v1, v2);
                        else
                            prim.AddTriangle(v2, v1, v0);
                    }
                    break;

                case Models.MeshTopology.TriangleStrip:
                    for (int i = 0; i < subMesh.Triangles.Length - 2; i++)
                    {
                        int idx0 = subMesh.Triangles[i];
                        int idx1 = subMesh.Triangles[i + 1];
                        int idx2 = subMesh.Triangles[i + 2];

                        // Skip degenerate triangles
                        if (idx0 == idx1 || idx0 == idx2 || idx1 == idx2)
                            continue;

                        if (idx0 >= meshData.Vertices.Length || idx1 >= meshData.Vertices.Length || idx2 >= meshData.Vertices.Length)
                        {
                            invalidTriangleCount++;
                            continue;
                        }

                        var v0 = CreateSkinnedVertex(meshData, idx0, boneNodes);
                        var v1 = CreateSkinnedVertex(meshData, idx1, boneNodes);
                        var v2 = CreateSkinnedVertex(meshData, idx2, boneNodes);

                        // TriangleStrip: alternate winding every triangle, compensate for inverted scale
                        bool oddTriangle = (i & 1) == 1;
                        if (oddTriangle != hasInvertedWinding)
                            prim.AddTriangle(v0, v1, v2);
                        else
                            prim.AddTriangle(v2, v1, v0);
                    }
                    break;

                case Models.MeshTopology.Quads:
                    for (int q = 0; q + 3 < subMesh.Triangles.Length; q += 4)
                    {
                        int idx0 = subMesh.Triangles[q];
                        int idx1 = subMesh.Triangles[q + 1];
                        int idx2 = subMesh.Triangles[q + 2];
                        int idx3 = subMesh.Triangles[q + 3];

                        if (idx0 >= meshData.Vertices.Length || idx1 >= meshData.Vertices.Length ||
                            idx2 >= meshData.Vertices.Length || idx3 >= meshData.Vertices.Length)
                        {
                            invalidTriangleCount++;
                            continue;
                        }

                        var v0 = CreateSkinnedVertex(meshData, idx0, boneNodes);
                        var v1 = CreateSkinnedVertex(meshData, idx1, boneNodes);
                        var v2 = CreateSkinnedVertex(meshData, idx2, boneNodes);
                        var v3 = CreateSkinnedVertex(meshData, idx3, boneNodes);

                        if (hasInvertedWinding)
                        {
                            prim.AddTriangle(v0, v1, v2);
                            prim.AddTriangle(v0, v2, v3);
                        }
                        else
                        {
                            prim.AddTriangle(v2, v1, v0);
                            prim.AddTriangle(v3, v2, v0);
                        }
                    }
                    break;

                default:
                    _logger.LogWarning("Unsupported mesh topology for skinned mesh: {Topology}", subMesh.Topology);
                    break;
            }
        }

        if (invalidTriangleCount > totalIndexCount / 30)
        {
            throw new Exception($"Too many invalid triangles ({invalidTriangleCount}) in mesh '{meshData.Name}'");
        }

        scene.AddSkinnedMesh(meshBuilder, jointsWithBindPoses.ToArray());

        if (_outOfBoundsJoints > 0 || _duplicateJoints > 0 || _nonNormalizedWeights > 0 || _invalidWeights > 0)
        {
            _logger.LogInformation("Bone weight fixes for '{MeshName}': bounds={OutOfBoundsCount}, invalid={InvalidCount}, duplicates={DuplicateCount}, normalized={NormalizedCount}",
                meshData.Name, _outOfBoundsJoints, _invalidWeights, _duplicateJoints, _nonNormalizedWeights);
        }
    }

    private static (VertexPositionNormalTangent, VertexTexture1)[] BuildVertexCache(MeshData meshData)
    {
        var cache = new (VertexPositionNormalTangent, VertexTexture1)[meshData.Vertices.Length];

        for (int i = 0; i < meshData.Vertices.Length; i++)
        {
            var p = meshData.Vertices[i];
            var pos = p != null ? GlbCoordinateConversion.ToGltfVector3Convert(new(p.X, p.Y, p.Z)) : System.Numerics.Vector3.Zero;

            var normal = System.Numerics.Vector3.UnitY;
            if (i < meshData.Normals.Length && meshData.Normals[i] is { } n &&
                float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z))
            {
                var candidate = new System.Numerics.Vector3(n.X, n.Y, n.Z);
                if (candidate.LengthSquared() > 0.0001f)
                    normal = System.Numerics.Vector3.Normalize(candidate);
            }
            var gltfNormal = GlbCoordinateConversion.ToGltfVector3Convert(normal);

            var tangent = new System.Numerics.Vector4(1, 0, 0, 1);
            if (i < meshData.Tangents.Length && meshData.Tangents[i] is { } t &&
                float.IsFinite(t.X) && float.IsFinite(t.Y) && float.IsFinite(t.Z) && float.IsFinite(t.W))
            {
                tangent = GlbCoordinateConversion.ToGltfTangentConvert(new(t.X, t.Y, t.Z, t.W));
            }

            var uv = i < meshData.UV0.Length && meshData.UV0[i] is { } u
                ? new System.Numerics.Vector2(u.X, u.Y)
                : System.Numerics.Vector2.Zero;

            cache[i] = (new VertexPositionNormalTangent(pos, gltfNormal, tangent), new VertexTexture1(uv));
        }

        return cache;
    }

    private static (System.Numerics.Vector3 Pos, System.Numerics.Vector3 Normal, System.Numerics.Vector4 Tangent, VertexTexture1 TexCoord) GetVertexData(MeshData meshData, int i)
    {
        var p = meshData.Vertices[i];
        var pos = p != null ? GlbCoordinateConversion.ToGltfVector3Convert(new(p.X, p.Y, p.Z)) : System.Numerics.Vector3.Zero;

        var normal = System.Numerics.Vector3.UnitY;
        if (i < meshData.Normals.Length && meshData.Normals[i] is { } n &&
            float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z))
        {
            var candidate = new System.Numerics.Vector3(n.X, n.Y, n.Z);
            if (candidate.LengthSquared() > 0.0001f)
                normal = System.Numerics.Vector3.Normalize(candidate);
        }
        var gltfNormal = GlbCoordinateConversion.ToGltfVector3Convert(normal);

        var tangent = new System.Numerics.Vector4(1, 0, 0, 1);
        if (i < meshData.Tangents.Length && meshData.Tangents[i] is { } t &&
            float.IsFinite(t.X) && float.IsFinite(t.Y) && float.IsFinite(t.Z) && float.IsFinite(t.W))
        {
            tangent = GlbCoordinateConversion.ToGltfTangentConvert(new(t.X, t.Y, t.Z, t.W));
        }

        var uv = i < meshData.UV0.Length && meshData.UV0[i] is { } u
            ? new System.Numerics.Vector2(u.X, u.Y)
            : System.Numerics.Vector2.Zero;

        return (pos, gltfNormal, tangent, new VertexTexture1(uv));
    }

    private (VertexPositionNormalTangent, VertexTexture1, VertexJoints4) CreateSkinnedVertex(MeshData meshData, int index, NodeBuilder[] boneNodes)
    {
        var (pos, normal, tangent, texCoord) = GetVertexData(meshData, index);
        var position = new VertexPositionNormalTangent(pos, normal, tangent);

        var bindings = new List<(int, float)>();

        if (index < meshData.BoneWeights.Length)
        {
            var bw = meshData.BoneWeights[index];
            var bones = new (int index, float weight)[4];
            bool hasOutOfBoundsJoint = false;
            bool hasInvalidWeight = false;
            bool hasDuplicates = false;
            float weightSum = 0f;

            for (int i = 0; i < 4; i++)
            {
                int meshBoneIndex = bw.BoneIndex[i];
                float weight = bw.Weight[i];

                if (meshBoneIndex < 0 || meshBoneIndex >= boneNodes.Length)
                {
                    if (weight > 0)
                    {
                        hasOutOfBoundsJoint = true;
                    }
                    meshBoneIndex = 0;
                }

                if (!float.IsFinite(weight))
                {
                    hasInvalidWeight = true;
                    weight = 0f;
                }
                else
                {
                    float originalWeight = weight;
                    weight = Math.Clamp(weight, 0f, 1f);
                    if (originalWeight != weight && originalWeight < 0)
                    {
                        hasInvalidWeight = true;
                    }
                }

                bones[i] = (meshBoneIndex, weight);
                weightSum += weight;

                for (int j = 0; j < i; j++)
                {
                    if (bones[j].index == meshBoneIndex)
                    {
                        hasDuplicates = true;
                        break;
                    }
                }
            }

            if (hasOutOfBoundsJoint)
            {
                _outOfBoundsJoints++;
            }
            if (hasInvalidWeight)
            {
                _invalidWeights++;
            }
            if (hasDuplicates)
            {
                _duplicateJoints++;
            }

            if (weightSum > 0.0001f)
            {
                if (Math.Abs(weightSum - 1.0f) > 0.001f)
                {
                    _nonNormalizedWeights++;
                }

                for (int i = 0; i < 4; i++)
                {
                    bindings.Add((bones[i].index, bones[i].weight / weightSum));
                }
            }
            else if (boneNodes.Length > 0)
            {
                bindings.Add((0, 1.0f));
            }
        }

        var skinning = bindings.Count > 0
            ? new VertexJoints4(bindings.ToArray())
            : new VertexJoints4(0);

        return (position, texCoord, skinning);
    }

    private static System.Numerics.Matrix4x4 ConvertUnityMatrixToNumerics(Matrix4x4 unityMatrix)
    {
        var v = unityMatrix.Values;

        for (int i = 0; i < 16; i++)
        {
            if (!float.IsFinite(v[i]))
            {
                throw new Exception($"Invalid matrix value at index {i}: {v[i]}");
            }
        }

        var result = new System.Numerics.Matrix4x4(
            v[0], v[4], v[8], v[12],
            v[1], v[5], v[9], v[13],
            v[2], v[6], v[10], v[14],
            v[3], v[7], v[11], v[15]
        );

        result.M14 = 0;
        result.M24 = 0;
        result.M34 = 0;
        result.M44 = 1;

        // Unity left-handed → glTF right-handed: mirror X-axis
        var mirrorX = new System.Numerics.Matrix4x4(
            -1, 0, 0, 0,
             0, 1, 0, 0,
             0, 0, 1, 0,
             0, 0, 0, 1
        );
        result = mirrorX * result * mirrorX;

        return result;
    }

    private MaterialBuilder CreateMaterialFromData(MaterialData materialData, List<TextureData> textures)
    {
        static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

        var material = new MaterialBuilder(materialData.Name)
            .WithDoubleSide(materialData.IsDoubleSided)
            .WithMetallicRoughnessShader();

        var baseColorVec = new System.Numerics.Vector4(
            Clamp01(materialData.BaseColor.X),
            Clamp01(materialData.BaseColor.Y),
            Clamp01(materialData.BaseColor.Z),
            Clamp01(materialData.BaseColor.W)
        );
        material.WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, baseColorVec);

        SharpGLTF.Materials.AlphaMode alphaMode = materialData.AlphaMode switch
        {
            1 => SharpGLTF.Materials.AlphaMode.MASK,
            2 or 3 => SharpGLTF.Materials.AlphaMode.BLEND,
            _ => SharpGLTF.Materials.AlphaMode.OPAQUE
        };

        if (alphaMode != SharpGLTF.Materials.AlphaMode.OPAQUE)
        {
            material.WithAlpha(alphaMode, materialData.AlphaCutoff);
        }

        if (!string.IsNullOrEmpty(materialData.MainTextureName))
        {
            var texture = textures.FirstOrDefault(t => t.Name == materialData.MainTextureName);
            if (texture != null && texture.ImageData.Length > 0)
            {
                try
                {
                    byte[] imageData = texture.ImageData;

                    if (alphaMode == SharpGLTF.Materials.AlphaMode.BLEND)
                    {
                        imageData = ProcessTextureForBlendMode(imageData, texture.Name);
                    }

                    var memoryImage = new SharpGLTF.Memory.MemoryImage(imageData);
                    var imageBuilder = ImageBuilder.From(memoryImage, texture.Name);
                    material.WithChannelImage(KnownChannel.BaseColor, imageBuilder);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load texture {TextureName}", texture.Name);
                }
            }
        }

        if (!string.IsNullOrEmpty(materialData.EmissiveTextureName))
        {
            if (!materialData.EmissionEnabled)
            {
                _logger.LogInformation("Skipping emissive for '{MaterialName}' - emission is disabled (_EmissionEnabled=0)", materialData.Name);
            }
            else
            {
                var matNameLower = materialData.Name.ToLowerInvariant();
                var emissiveNameLower = materialData.EmissiveTextureName.ToLowerInvariant();

                // Unity lets artists assign any emissive to any material. When a base unit's emissive
                // lands on an upgrade variant, the UVs don't match — different mesh, different UV layout —
                // so the emissive renders as stripes or noise instead of the intended subtle glow.
                // Asset naming convention mirrors material naming, so prefix mismatch reliably catches this.
                var matBase = matNameLower.EndsWith("_mt") ? matNameLower[..^3] : matNameLower;
                bool emissiveMismatchesMaterial = !emissiveNameLower.StartsWith(matBase);

                // Some textures named like emissives are actually alpha/transparency masks (e.g. olgoi_transparent).
                bool isTransparencyTexture = emissiveNameLower.Contains("_transparent") &&
                                             !emissiveNameLower.Contains("emissive");

                if (emissiveMismatchesMaterial)
                {
                    _logger.LogInformation("Skipping emissive '{EmissiveTextureName}' - does not match material base name '{MatBase}'",
                        materialData.EmissiveTextureName, matBase);
                }
                else if (isTransparencyTexture)
                {
                    _logger.LogInformation("Skipping emissive '{EmissiveTextureName}' - appears to be transparency texture, not emissive", materialData.EmissiveTextureName);
                }
                else
                {
                var emissiveTexture = textures.FirstOrDefault(t => t.Name == materialData.EmissiveTextureName);
                if (emissiveTexture != null && emissiveTexture.ImageData.Length > 0)
                {
                    try
                    {
                        var memoryImage = new SharpGLTF.Memory.MemoryImage(emissiveTexture.ImageData);
                        var imageBuilder = ImageBuilder.From(memoryImage, emissiveTexture.Name);

                        var hasEmissiveColor = materialData.EmissiveColor.X > 0 ||
                                              materialData.EmissiveColor.Y > 0 ||
                                              materialData.EmissiveColor.Z > 0;

                        float strength = materialData.EmissionStrength;

                        var emissiveColorVec = hasEmissiveColor
                            ? new System.Numerics.Vector3(
                                materialData.EmissiveColor.X * strength,
                                materialData.EmissiveColor.Y * strength,
                                materialData.EmissiveColor.Z * strength)
                            : new System.Numerics.Vector3(strength, strength, strength);

                        material.WithEmissive(imageBuilder, emissiveColorVec);
                        _logger.LogInformation("Applied emissive texture: {EmissiveTextureName} (strength={Strength:F2})", emissiveTexture.Name, strength);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load emissive texture {EmissiveTextureName}", emissiveTexture.Name);
                    }
                }
                }
            }
        }

        return material;
    }

    private byte[] ProcessTextureForBlendMode(byte[] pngData, string textureName)
    {
        try
        {
            using var inputStream = new MemoryStream(pngData);
            using var image = Image.Load<Rgba32>(inputStream);

            bool hasRealAlpha = false;
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height && !hasRealAlpha; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        if (row[x].A != 255)
                        {
                            hasRealAlpha = true;
                            break;
                        }
                    }
                }
            });

            if (hasRealAlpha)
            {
                return pngData;
            }

            _logger.LogInformation("Deriving alpha from luminance for BLEND texture: {TextureName}", textureName);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        ref var pixel = ref row[x];
                        int luminance = (int)(0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B);
                        pixel.A = (byte)Math.Clamp(luminance, 0, 255);
                    }
                }
            });

            using var outputStream = new MemoryStream();
            image.SaveAsPng(outputStream);
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process texture for BLEND mode");
            return pngData;
        }
    }

    private static bool ShouldSkipDefaultMaterialMesh(MeshData meshData, UnitData unitData)
    {
        var materialNames = new HashSet<string>();

        if (!string.IsNullOrEmpty(meshData.MaterialName))
        {
            materialNames.Add(meshData.MaterialName);
        }

        foreach (var subMesh in meshData.SubMeshes)
        {
            if (!string.IsNullOrEmpty(subMesh.MaterialName))
            {
                materialNames.Add(subMesh.MaterialName);
            }
        }

        if (materialNames.Count == 0)
            return false;

        foreach (var matName in materialNames)
        {
            if (matName != "DefaultMaterial")
                return false;

            var material = unitData.Materials.FirstOrDefault(m => m.Name == matName);
            if (material != null && !string.IsNullOrEmpty(material.MainTextureName))
                return false;
        }

        return true;
    }
}
