#nullable enable
using AssetExtractor.Extraction.Interfaces;
using AssetExtractor.Export;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysVector2 = System.Numerics.Vector2;
using SysVector3 = System.Numerics.Vector3;
using SysMatrix4x4 = System.Numerics.Matrix4x4;
using SysVector4 = System.Numerics.Vector4;
using MeshData = AssetExtractor.Models.MeshData;
using BoneWeight = AssetExtractor.Models.BoneWeight;
using Matrix4x4 = AssetExtractor.Models.Matrix4x4;
using SubMeshData = AssetExtractor.Models.SubMeshData;
using HierarchyNode = AssetExtractor.Models.HierarchyNode;
using SkeletonData = AssetExtractor.Models.SkeletonData;
using UnitData = AssetExtractor.Models.UnitData;
using MapObjectData = AssetExtractor.Models.MapObjectData;
using GameObjectData = AssetExtractor.Models.GameObjectData;
using PrefabData = AssetExtractor.Models.PrefabData;

namespace AssetExtractor.Extraction;

public class MeshDataExtractor : IMeshDataExtractor
{
    private readonly ILogger<MeshDataExtractor> _logger;
    private readonly IGameObjectProvider _gameObjectProvider;
    private readonly IBoneDataExtractor _boneDataExtractor;
    private readonly MaterialExtractor _materialExtractor;

    public MeshDataExtractor(
        IGameObjectProvider gameObjectProvider,
        IBoneDataExtractor boneDataExtractor,
        MaterialExtractor materialExtractor,
        ILogger<MeshDataExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<MeshDataExtractor>.Instance;
        _gameObjectProvider = gameObjectProvider;
        _boneDataExtractor = boneDataExtractor;
        _materialExtractor = materialExtractor;
    }

    private static Dictionary<string, int> BuildSkeletonIndexLookup(SkeletonData? skeleton, ILogger? logger = null)
    {
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        if (skeleton == null) return lookup;
        foreach (var bone in skeleton.Bones)
        {
            if (string.IsNullOrEmpty(bone.Path)) continue;
            lookup.TryAdd(bone.Path, bone.Index);
        }
        return lookup;
    }

    private static Models.Vector3 CalculateAccumulatedScaleSign(HierarchyNode? node)
    {
        float sx = 1f, sy = 1f, sz = 1f;
        var current = node;
        while (current != null)
        {
            var s = current.LocalTransform.Scale;
            sx *= s.X; sy *= s.Y; sz *= s.Z;
            current = current.Parent;
        }
        return new Models.Vector3(sx < 0 ? -1f : 1f, sy < 0 ? -1f : 1f, sz < 0 ? -1f : 1f);
    }

    public List<MeshData> ExtractMeshesFromInner(HierarchyNode inner, SkeletonData skeleton, UnitData unitData, long animatorTransformPathId)
    {
        var meshes = new List<MeshData>();
        var skeletonIndexByPath = BuildSkeletonIndexLookup(skeleton, _logger);

        foreach (var child in inner.Children)
        {
            if (string.Equals(child.Name, "Root", StringComparison.OrdinalIgnoreCase)) continue;
            if (HierarchyExtractor.IsVFXNode(child.Name)) continue;

            var gameObject = _gameObjectProvider.FindGameObjectByPathId(child.SourceFile, child.PathID);
            if (gameObject == null) continue;
            if (!gameObject.TryGetComponent<UnityReader.SkinnedMeshRenderer>(out var smr)) continue;

            var mesh = smr.Mesh;
            if (mesh == null) { _logger.LogWarning("No mesh asset found for child node: {ChildName}", child.Name); continue; }

            var meshData = BuildSkinnedMeshData(mesh, child, smr, unitData, skeletonIndexByPath, animatorTransformPathId);
            if (meshData == null) continue;
            meshes.Add(meshData);
        }

        return meshes;
    }

    public List<MeshData> ExtractMeshesFromMapObject(HierarchyNode inner, MapObjectData objectData)
    {
        var meshes = new List<MeshData>();
        var skeletonIndexByPath = BuildSkeletonIndexLookup(objectData.Skeleton, _logger);
        long animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(inner);

        var innerGO = _gameObjectProvider.FindGameObjectByPathId(inner.SourceFile, inner.PathID);
        if (innerGO != null && inner.IsActive)
        {
            if (innerGO.TryGetComponent<UnityReader.SkinnedMeshRenderer>(out var smr))
            {
                try
                {
                    var mesh = smr.Mesh;
                    if (mesh != null)
                    {
                        var meshData = BuildSkinnedMeshData(mesh, inner, smr, objectData, skeletonIndexByPath, animatorTransformPathId);
                        if (meshData != null) { meshData.HierarchyPath = ""; meshes.Add(meshData); }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed inner SMR mesh from {NodeName}", inner.Name);
                }
            }
            else if (innerGO.TryGetComponent<UnityReader.MeshRenderer>(out var mr) &&
                     innerGO.TryGetComponent<UnityReader.MeshFilter>(out var mf))
            {
                if (!HierarchyExtractor.IsTerrainBlendNode(inner.Name))
                {
                    try
                    {
                        var mesh = mf.Mesh;
                        if (mesh != null)
                        {
                            var meshData = BuildStaticMeshData(mesh, inner, mr, objectData);
                            if (meshData != null) { meshData.HierarchyPath = ""; meshes.Add(meshData); }
                        }
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed inner MR mesh from {NodeName}", inner.Name); }
                }
            }
        }

        foreach (var child in inner.Children)
            ExtractMeshesRecursive(child, "", meshes, objectData, skeletonIndexByPath, animatorTransformPathId);

        foreach (var mesh in meshes) mesh.IsMapObject = true;
        return meshes;
    }

    public List<MeshData> ExtractMeshesFromGameObject(HierarchyNode node, GameObjectData objectData)
    {
        var meshes = new List<MeshData>();
        var skeletonIndexByPath = BuildSkeletonIndexLookup(objectData.Skeleton, _logger);
        long animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(node);

        foreach (var child in node.Children)
            ExtractMeshesRecursive(child, "", meshes, objectData, skeletonIndexByPath, animatorTransformPathId);

        return meshes;
    }

    // Filters Unity's built-in primitive / default-material libraries; prefabs that point
    // here are runtime-dynamic placeholders (e.g. PLATFORM + BACK's background Quad). The
    // AR-baseline GLBs excluded them, so we mirror that to keep output structurally equivalent.
    private static bool IsEngineBuiltinSource(string sourceFile)
        => sourceFile.Equals("unity default resources", StringComparison.OrdinalIgnoreCase)
            || sourceFile.Equals("unity_builtin_extra", StringComparison.OrdinalIgnoreCase);

    private void ExtractMeshesRecursive(HierarchyNode node, string parentPath, List<MeshData> meshes, PrefabData prefabData,
        Dictionary<string, int>? skeletonIndexByPath, long animatorTransformPathId)
    {
        if (!node.IsActive) return;

        string currentPath = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";

        var gameObject = _gameObjectProvider.FindGameObjectByPathId(node.SourceFile, node.PathID);
        if (gameObject == null)
        {
            foreach (var child in node.Children)
                ExtractMeshesRecursive(child, currentPath, meshes, prefabData, skeletonIndexByPath, animatorTransformPathId);
            return;
        }

        if (gameObject.TryGetComponent<UnityReader.SkinnedMeshRenderer>(out var smr))
        {
            try
            {
                var mesh = smr.Mesh;
                if (mesh != null && !IsEngineBuiltinSource(mesh.SourceFile))
                {
                    var meshData = BuildSkinnedMeshData(mesh, node, smr, prefabData, skeletonIndexByPath, animatorTransformPathId);
                    if (meshData != null) { meshData.HierarchyPath = currentPath; meshes.Add(meshData); }
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed SMR mesh from {NodeName}", node.Name); }
        }
        else if (gameObject.TryGetComponent<UnityReader.MeshRenderer>(out var meshRenderer))
        {
            if (HierarchyExtractor.IsTerrainBlendNode(node.Name))
            {
                _logger.LogInformation("Skipping terrain blend mesh: {NodeName}", node.Name);
            }
            else
            {
                try
                {
                    if (gameObject.TryGetComponent<UnityReader.MeshFilter>(out var meshFilter))
                    {
                        var mesh = meshFilter.Mesh;
                        if (mesh != null && !IsEngineBuiltinSource(mesh.SourceFile))
                        {
                            var meshData = BuildStaticMeshData(mesh, node, meshRenderer, prefabData);
                            if (meshData != null) { meshData.HierarchyPath = currentPath; meshes.Add(meshData); }
                        }
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed MR mesh from {NodeName}", node.Name); }
            }
        }

        foreach (var child in node.Children)
            ExtractMeshesRecursive(child, currentPath, meshes, prefabData, skeletonIndexByPath, animatorTransformPathId);
    }

    private MeshData? BuildSkinnedMeshData(UnityReader.Mesh mesh, HierarchyNode node, UnityReader.SkinnedMeshRenderer smr,
        PrefabData prefabData, Dictionary<string, int>? skeletonIndexByPath, long animatorTransformPathId)
    {
        var geom = mesh.ReadGeometry();
        if (geom.Vertices.Length == 0) return null;

        var meshData = BuildMeshDataFromGeometry(node, geom);

        meshData.BoneWeights = (geom.BoneWeights != null && geom.BoneWeights.Length > 0)
            ? ConvertBoneWeights(geom.BoneWeights, node.Name, _logger)
            : CreateDefaultBoneWeights(geom.Vertices.Length);

        meshData.BindPoses = geom.BindPoses != null ? ConvertBindPoses(geom.BindPoses) : Array.Empty<Matrix4x4>();

        meshData.BoneIndices = (skeletonIndexByPath != null && skeletonIndexByPath.Count > 0)
            ? _boneDataExtractor.MapBoneIndicesToSkeleton(smr, skeletonIndexByPath, animatorTransformPathId)
            : Array.Empty<int>();

        ApplyMaterials(meshData, _materialExtractor.ExtractMaterials(smr, prefabData, _gameObjectProvider.Loader));
        return meshData;
    }

    private MeshData? BuildStaticMeshData(UnityReader.Mesh mesh, HierarchyNode node, UnityReader.MeshRenderer meshRenderer, PrefabData prefabData)
    {
        var geom = mesh.ReadGeometry();
        if (geom.Vertices.Length == 0) return null;

        var meshData = BuildMeshDataFromGeometry(node, geom);
        meshData.BoneWeights = Array.Empty<BoneWeight>();
        meshData.BoneIndices = Array.Empty<int>();
        meshData.BindPoses = Array.Empty<Matrix4x4>();

        ApplyMaterials(meshData, _materialExtractor.ExtractMaterials(meshRenderer, prefabData, _gameObjectProvider.Loader));
        return meshData;
    }

    private MeshData BuildMeshDataFromGeometry(HierarchyNode node, UnityReader.MeshGeometry geom)
    {
        var meshData = new MeshData
        {
            Name = node.Name,
            LocalTransform = node.LocalTransform,
            AccumulatedScale = CalculateAccumulatedScaleSign(node),
            Vertices = ConvertVertices(geom.Vertices),
            Normals = geom.Normals != null ? ConvertNormals(geom.Normals) : new Models.Vector3[geom.Vertices.Length],
            UV0 = (geom.UvChannels.Count > 0 && geom.UvChannels[0] != null) ? ConvertUVs(geom.UvChannels[0]!) : new Models.Vector2[geom.Vertices.Length],
            Tangents = geom.Tangents != null ? ConvertTangents(geom.Tangents) : Array.Empty<Models.Vector4>(),
            Colors = geom.Colors != null && geom.Colors.Length > 0 ? ConvertColors(geom.Colors) : Array.Empty<Models.Vector4>(),
            Triangles = geom.Indices.ToArray()
        };
        PopulateSubMeshes(meshData, geom);
        return meshData;
    }

    private static void ApplyMaterials(MeshData meshData, List<Models.MaterialData> materials)
    {
        if (materials.Count == 0) return;
        meshData.MaterialName = materials[0].Name;
        for (int i = 0; i < meshData.SubMeshes.Count && i < materials.Count; i++)
            meshData.SubMeshes[i].MaterialName = materials[i].Name;
    }

    private static void PopulateSubMeshes(MeshData meshData, UnityReader.MeshGeometry geom)
    {
        meshData.SubMeshes.Clear();
        if (geom.SubMeshes.Length == 0) return;

        int cursor = 0;
        for (int i = 0; i < geom.SubMeshes.Length; i++)
        {
            var sm = geom.SubMeshes[i];
            int indexCount = sm.IndexCount;
            if (indexCount <= 0 || cursor + indexCount > geom.Indices.Length)
            {
                cursor += Math.Max(0, indexCount);
                continue;
            }

            var subTris = new int[indexCount];
            for (int j = 0; j < indexCount; j++) subTris[j] = geom.Indices[cursor + j];
            cursor += indexCount;

            if (sm.BaseVertex != 0)
            {
                for (int j = 0; j < subTris.Length; j++) subTris[j] += sm.BaseVertex;
            }

            meshData.SubMeshes.Add(new SubMeshData
            {
                Triangles = subTris,
                MaterialName = string.Empty,
                Topology = (Models.MeshTopology)(int)sm.Topology
            });
        }
    }

    private static Models.Vector3[] ConvertVertices(SysVector3[] vertices)
    {
        var r = new Models.Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) r[i] = new Models.Vector3(vertices[i].X, vertices[i].Y, vertices[i].Z);
        return r;
    }

    private static Models.Vector3[] ConvertNormals(SysVector3[] normals)
    {
        var r = new Models.Vector3[normals.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            var n = normals[i];
            float length = MathF.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
            r[i] = length > 0.0001f
                ? new Models.Vector3(n.X / length, n.Y / length, n.Z / length)
                : new Models.Vector3(0, 1, 0);
        }
        return r;
    }

    private static Models.Vector2[] ConvertUVs(SysVector2[] uvs)
    {
        var r = new Models.Vector2[uvs.Length];
        for (int i = 0; i < uvs.Length; i++) r[i] = new Models.Vector2(uvs[i].X, uvs[i].Y);
        return r;
    }

    private static Models.Vector4[] ConvertTangents(SysVector4[] tangents)
    {
        var r = new Models.Vector4[tangents.Length];
        for (int i = 0; i < tangents.Length; i++)
        { var t = tangents[i]; r[i] = new Models.Vector4(t.X, t.Y, t.Z, t.W); }
        return r;
    }

    private static Models.Vector4[] ConvertColors(SysVector4[] colors)
    {
        var r = new Models.Vector4[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        { var c = colors[i]; r[i] = new Models.Vector4(c.X, c.Y, c.Z, c.W); }
        return r;
    }

    private static BoneWeight[] ConvertBoneWeights(UnityReader.BoneInfluence[] skin, string meshName, ILogger? logger)
    {
        var r = new BoneWeight[skin.Length];
        int zeroWeightCount = 0;

        for (int i = 0; i < skin.Length; i++)
        {
            var bw = skin[i];
            float weightSum = bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3;
            bool hasZeroWeights = weightSum < 0.0001f;

            r[i] = new BoneWeight();
            r[i].BoneIndex[0] = bw.BoneIndex0;
            r[i].BoneIndex[1] = bw.BoneIndex1;
            r[i].BoneIndex[2] = bw.BoneIndex2;
            r[i].BoneIndex[3] = bw.BoneIndex3;
            if (hasZeroWeights)
            {
                r[i].Weight[0] = 1.0f; r[i].Weight[1] = 0f; r[i].Weight[2] = 0f; r[i].Weight[3] = 0f;
                zeroWeightCount++;
            }
            else
            {
                r[i].Weight[0] = bw.Weight0; r[i].Weight[1] = bw.Weight1;
                r[i].Weight[2] = bw.Weight2; r[i].Weight[3] = bw.Weight3;
            }
        }

        return r;
    }

    private static BoneWeight[] CreateDefaultBoneWeights(int vertexCount)
    {
        var r = new BoneWeight[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        { r[i] = new BoneWeight(); r[i].BoneIndex[0] = 0; r[i].Weight[0] = 1.0f; }
        return r;
    }

    private static Matrix4x4[] ConvertBindPoses(SysMatrix4x4[] bindPoses)
    {
        var r = new Matrix4x4[bindPoses.Length];
        for (int i = 0; i < bindPoses.Length; i++)
        {
            var m = bindPoses[i];
            r[i] = new Matrix4x4(new float[]
            {
                m.M11, m.M21, m.M31, m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43,
                m.M14, m.M24, m.M34, m.M44
            });
        }
        return r;
    }
}
