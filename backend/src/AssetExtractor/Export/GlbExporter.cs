#nullable enable
using AssetExtractor.Models;
using AssetExtractor.Export.Interfaces;
using AssetExtractor.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssetExtractor.Export;

/// <summary>
/// Coordinates GLB export using SharpGLTF, delegating to specialized exporters.
/// Manages versioning and manifest tracking.
/// </summary>
public class GlbExporter
{
    private readonly ILogger<GlbExporter> _logger;
    private readonly string _outputPath;
    private readonly DeduplicationService _deduplicationService;
    private readonly ManifestManager _manifestService;

    private readonly INodeHierarchyBuilder _nodeHierarchyBuilder;
    private readonly IMeshExporter _meshExporter;
    private readonly IAnimationExporter _animationExporter;

    public GlbExporter(
        string outputPath,
        DeduplicationService deduplicationService,
        ManifestManager manifestService,
        ILogger<GlbExporter>? logger = null)
    {
        _logger = logger ?? NullLogger<GlbExporter>.Instance;
        _outputPath = outputPath;
        _deduplicationService = deduplicationService;
        _manifestService = manifestService;

        if (!Directory.Exists(_outputPath))
        {
            Directory.CreateDirectory(_outputPath);
        }

        _nodeHierarchyBuilder = new NodeHierarchyBuilder();
        _meshExporter = new GltfMeshExporter();
        _animationExporter = new GltfAnimationExporter();
    }

    private static UnitData ConvertToUnitData(PrefabData prefabData)
    {
        if (prefabData is UnitData ud)
        {
            return ud;
        }

        if (prefabData is MapObjectData mapData)
        {
            return new UnitData
            {
                Name = mapData.Name,
                Meshes = mapData.Meshes,
                Materials = mapData.Materials,
                Textures = mapData.Textures,
                RootNode = mapData.RootNode,
                WrapperNode = mapData.WrapperNode,
                InnerNode = mapData.InnerNode,
                Skeleton = mapData.Skeleton,
                Animations = mapData.Animations,
                LookPointPosition = mapData.LookPointPosition
            };
        }

        if (prefabData is GameObjectData gameData)
        {
            return new UnitData
            {
                Name = gameData.Name,
                Meshes = gameData.Meshes,
                Materials = gameData.Materials,
                Textures = gameData.Textures,
                RootNode = gameData.RootNode,
                WrapperNode = gameData.WrapperNode,
                InnerNode = gameData.InnerNode,
                Skeleton = gameData.Skeleton,
                Animations = gameData.Animations,
                LookPointPosition = gameData.LookPointPosition
            };
        }

        throw new ArgumentException($"Unsupported prefab type: {prefabData.GetType().Name}");
    }

    public string? Export(PrefabData prefabData, string relativePath, string version)
    {
        try
        {
            var versionOutputPath = _manifestService.GetVersionOutputPath(version);
            var filename = relativePath + ".glb";
            var fullPath = Path.Combine(versionOutputPath, filename);

            _logger.LogInformation("Exporting to GLB (version {Version}): {Filename}", version, filename);

            var (model, _) = BuildGltfModel(prefabData);

            using var memoryStream = new MemoryStream();
            model.WriteGLB(memoryStream);

            var buffer = memoryStream.GetBuffer();
            var actualLength = (int)memoryStream.Length;
            var hash = DeduplicationService.ComputeHash(buffer, 0, actualLength);

            var reservationResult = _deduplicationService.TryReservePath(fullPath, hash, out var actualPath);

            string manifestRelativePath = relativePath;

            switch (reservationResult)
            {
                case PathReservationResult.Duplicate:
                    _logger.LogInformation("Skipped duplicate GLB: {RelativePath}", relativePath);
                    break;

                case PathReservationResult.ConflictResolved:
                    _logger.LogInformation("Resolved conflict for GLB: {OriginalPath} -> {ResolvedFilename}", relativePath, Path.GetFileName(actualPath));
                    WriteGlbFile(actualPath!, buffer, actualLength);
                    manifestRelativePath = actualPath!
                        .Substring(versionOutputPath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (manifestRelativePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    {
                        manifestRelativePath = manifestRelativePath.Substring(0, manifestRelativePath.Length - 4);
                    }
                    break;

                case PathReservationResult.Success:
                    WriteGlbFile(actualPath!, buffer, actualLength);
                    _logger.LogInformation("GLB export complete: {ActualPath}", actualPath);
                    break;
            }

            _manifestService.AddOrUpdateVariant(
                manifestRelativePath,
                version,
                hash,
                actualLength,
                "Model",
                ".glb");
            return actualPath ?? fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export GLB '{PrefabName}'", prefabData.Name);
            return null;
        }
    }

    public (ModelRoot Model, List<string> Warnings) BuildGltfModel(PrefabData prefabData)
    {
        var warnings = new List<string>();
        var scene = new SceneBuilder(prefabData.Name);
        var usedNodeNames = new HashSet<string>(StringComparer.Ordinal);

        _logger.LogInformation("Building glTF model for: {PrefabName}", prefabData.Name);

        var unitData = ConvertToUnitData(prefabData);

        if (prefabData is UnitData)
        {
            BuildUnitModel(scene, unitData, usedNodeNames, warnings);
        }
        else if (prefabData is MapObjectData)
        {
            BuildMapObjectModel(scene, unitData, usedNodeNames, warnings);
        }
        else if (prefabData is GameObjectData)
        {
            BuildGameObjectModel(scene, unitData, usedNodeNames, warnings);
        }
        else
        {
            throw new ArgumentException($"Unsupported prefab type: {prefabData.GetType().Name}");
        }

        var model = scene.ToGltf2();

        FixMeshNodeHierarchy(model, unitData);

        ApplyPostProcessing(model, prefabData);

        return (model, warnings);
    }

    private void BuildUnitModel(
        SceneBuilder scene,
        UnitData unitData,
        HashSet<string> usedNodeNames,
        List<string> warnings)
    {
        var nodeBuilders = _nodeHierarchyBuilder.BuildNodeHierarchy(scene, unitData, usedNodeNames);

        if (unitData.Skeleton != null && unitData.Skeleton.Bones.Count > 0)
        {
            _logger.LogInformation("Adding skeleton with {BoneCount} bones", unitData.Skeleton.Bones.Count);
            _nodeHierarchyBuilder.AddSkeleton(scene, unitData, nodeBuilders, usedNodeNames);
        }

        _logger.LogInformation("Adding {MeshCount} meshes", unitData.Meshes.Count);
        _meshExporter.AddMeshes(scene, unitData, nodeBuilders, isUnit: true);

        if (unitData.Animations.Count > 0 && unitData.Skeleton != null)
        {
            _logger.LogInformation("Adding {AnimationCount} animations", unitData.Animations.Count);
            _animationExporter.AddAnimations(scene, unitData.Animations, unitData.Skeleton, nodeBuilders);
        }
    }

    private void BuildMapObjectModel(
        SceneBuilder scene,
        UnitData unitData,
        HashSet<string> usedNodeNames,
        List<string> warnings)
    {
        // Unity→glTF (90°) + isometric (45°) for map objects, OR look_point-based rotation if present
        var nodeBuilders = _nodeHierarchyBuilder.BuildNodeHierarchy(scene, unitData, usedNodeNames, applyGltfForward: true, isometricExtra: true);

        if (unitData.Skeleton != null && unitData.Skeleton.Bones.Count > 0)
        {
            _logger.LogInformation("Adding skeleton with {BoneCount} nodes for map object", unitData.Skeleton.Bones.Count);
            _nodeHierarchyBuilder.AddSkeleton(scene, unitData, nodeBuilders, usedNodeNames);
        }

        _meshExporter.AddMeshes(scene, unitData, nodeBuilders);

        if (unitData.Animations.Count > 0 && unitData.Skeleton != null)
        {
            _logger.LogInformation("Adding {AnimationCount} animations for map object", unitData.Animations.Count);
            _animationExporter.AddAnimations(scene, unitData.Animations, unitData.Skeleton, nodeBuilders);
        }
    }

    private void BuildGameObjectModel(
        SceneBuilder scene,
        UnitData unitData,
        HashSet<string> usedNodeNames,
        List<string> warnings)
    {
        // Unity→glTF (90°) only - no isometric correction for GameObjects
        var nodeBuilders = _nodeHierarchyBuilder.BuildNodeHierarchy(scene, unitData, usedNodeNames, applyGltfForward: true);

        if (unitData.Skeleton != null && unitData.Skeleton.Bones.Count > 0)
        {
            _logger.LogInformation("Adding skeleton with {BoneCount} nodes for GameObject", unitData.Skeleton.Bones.Count);
            _nodeHierarchyBuilder.AddSkeleton(scene, unitData, nodeBuilders, usedNodeNames);
        }

        _meshExporter.AddMeshes(scene, unitData, nodeBuilders);

        if (unitData.Animations.Count > 0 && unitData.Skeleton != null)
        {
            _logger.LogInformation("Adding {AnimationCount} animations for GameObject", unitData.Animations.Count);
            _animationExporter.AddAnimations(scene, unitData.Animations, unitData.Skeleton, nodeBuilders);
        }
    }

    private static void ApplyPostProcessing(ModelRoot model, PrefabData prefabData)
    {
        model.Asset.Generator = "unity-asset-to-glb";

        // Omit timestamp for deterministic hashing (deduplication)
        var extras = new JsonObject
        {
            ["prefabName"] = prefabData.Name
        };

        if (prefabData is UnitData unitData)
        {
            extras["type"] = "unit";
            extras["meshCount"] = unitData.Meshes.Count;
            extras["animationCount"] = unitData.Animations.Count;
            extras["boneCount"] = unitData.Skeleton?.Bones.Count ?? 0;
        }
        else if (prefabData is MapObjectData mapObjectData)
        {
            extras["type"] = "mapObject";
            extras["category"] = mapObjectData.Category ?? "";
            extras["meshCount"] = mapObjectData.Meshes.Count;
        }
        else if (prefabData is GameObjectData gameObjectData)
        {
            extras["type"] = "gameObject";
            extras["meshCount"] = gameObjectData.Meshes.Count;
            extras["animationCount"] = gameObjectData.Animations.Count;
            extras["boneCount"] = gameObjectData.Skeleton?.Bones.Count ?? 0;
        }

        model.Asset.Extras = extras;
    }

    private void WriteGlbFile(string fullPath, byte[] buffer, int length)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(fullPath, buffer.AsSpan(0, length).ToArray());
    }

    #region Mesh Hierarchy Post-Processing

    /// <summary>
    /// Fix mesh node names and hierarchy. SharpGLTF's SceneBuilder doesn't support named mesh nodes,
    /// so this post-processes the model to restore Unity hierarchy structure.
    /// </summary>
    private void FixMeshNodeHierarchy(ModelRoot model, UnitData unitData)
    {
        _logger.LogInformation("Post-processing: Fixing mesh node names and hierarchy");

        SharpGLTF.Schema2.Node? innerNode = null;

        if (unitData.InnerNode != null)
        {
            string token = $"go{unitData.InnerNode.PathID}";
            innerNode = FindNodeByName(model, unitData.InnerNode.Name, token);
        }

        if (innerNode == null && unitData.RootNode != null && unitData.InnerNode != null &&
            unitData.RootNode.PathID == unitData.InnerNode.PathID)
        {
            string rootBaseName = unitData.RootNode.Name;
            if (string.Equals(rootBaseName, unitData.InnerNode.Name, StringComparison.Ordinal))
            {
                rootBaseName = $"{rootBaseName}_root";
            }
            string token = $"go{unitData.RootNode.PathID}";
            innerNode = FindNodeByName(model, rootBaseName, token);
        }

        if (innerNode == null)
        {
            _logger.LogWarning("Could not find inner node '{InnerNodeName}' for mesh reparenting", unitData.InnerNode?.Name);
            return;
        }

        var meshNodes = model.LogicalNodes.Where(n => n.Mesh != null).ToList();

        // Match by name, not index - SharpGLTF may reorder meshes during export
        foreach (var meshNode in meshNodes)
        {
            var meshAssetName = meshNode.Mesh?.Name;
            var meshData = unitData.Meshes.FirstOrDefault(m => m.Name == meshAssetName);
            if (meshData == null)
            {
                _logger.LogWarning("No matching mesh data for mesh asset '{MeshAssetName}'", meshAssetName);
                continue;
            }

            meshNode.Name = meshData.Name;

            // Bone-attached meshes already have transform - skip reparenting
            if (!string.IsNullOrEmpty(meshData.HierarchyPath))
            {
                var skeletonHasMeshPath = unitData.Skeleton?.Bones.Any(b => b.Path == meshData.HierarchyPath) == true;
                if (skeletonHasMeshPath)
                {
                    continue;
                }
            }

            if (IsDescendantOf(meshNode, innerNode, model))
            {
                continue;
            }

            // GameObjects: flattened hierarchy → WorldTransform. Units: proper hierarchy → LocalTransform
            var t = HasNonIdentityWorldTransform(meshData.WorldTransform)
                ? meshData.WorldTransform
                : meshData.LocalTransform;

            var meshLocalXform = new SharpGLTF.Transforms.AffineTransform(
                new System.Numerics.Vector3(t.Scale.X, t.Scale.Y, t.Scale.Z),
                new System.Numerics.Quaternion(t.Rotation.X, -t.Rotation.Y, -t.Rotation.Z, t.Rotation.W),
                new System.Numerics.Vector3(-t.Position.X, t.Position.Y, t.Position.Z)
            );

            SharpGLTF.Schema2.Node targetParent = innerNode;
            // Unity SkinnedMeshRenderer transform is in joints - applying here duplicates. Rigid meshes only.
            if (meshNode.Skin == null)
            {
                meshNode.LocalTransform = meshLocalXform;
            }

            var currentParent = model.LogicalNodes.FirstOrDefault(n => n.VisualChildren.Contains(meshNode));
            int meshNodeIndex = meshNode.LogicalIndex;

            if (currentParent != null && currentParent != targetParent)
            {
                SetNodeChildren(currentParent, GetChildIndicesExcluding(currentParent.VisualChildren, meshNodeIndex));
                AddChildToNode(targetParent, meshNodeIndex);
            }
            else if (currentParent == null)
            {
                foreach (var scene in model.LogicalScenes)
                {
                    SetSceneNodes(scene, GetChildIndicesExcluding(scene.VisualChildren, meshNodeIndex));
                }
                AddChildToNode(targetParent, meshNodeIndex);
            }
        }

        _logger.LogInformation("Post-processing complete");
    }

    private static bool IsDescendantOf(SharpGLTF.Schema2.Node node, SharpGLTF.Schema2.Node ancestor, ModelRoot model)
    {
        if (node == ancestor) return true;

        var current = node;
        var visited = new HashSet<int>();

        while (current != null)
        {
            if (!visited.Add(current.LogicalIndex))
                return false;

            if (current == ancestor) return true;

            current = model.LogicalNodes.FirstOrDefault(n => n.VisualChildren.Contains(current));
        }

        return false;
    }

    private static bool IsIdentityTransform(Models.Transform t)
    {
        const float eps = 1e-6f;
        return MathF.Abs(t.Position.X) < eps &&
               MathF.Abs(t.Position.Y) < eps &&
               MathF.Abs(t.Position.Z) < eps &&
               MathF.Abs(t.Scale.X - 1f) < eps &&
               MathF.Abs(t.Scale.Y - 1f) < eps &&
               MathF.Abs(t.Scale.Z - 1f) < eps &&
               MathF.Abs(t.Rotation.X) < eps &&
               MathF.Abs(t.Rotation.Y) < eps &&
               MathF.Abs(t.Rotation.Z) < eps &&
               MathF.Abs(MathF.Abs(t.Rotation.W) - 1f) < eps;
    }

    private static bool HasNonIdentityWorldTransform(Models.Transform t)
    {
        return !IsIdentityTransform(t);
    }

    /// <summary>
    /// WORKAROUND: SharpGLTF has no public API for post-construction hierarchy modification.
    /// Uses reflection on _children field (SharpGLTF 1.0.0-alpha0042).
    /// Breaking: Library updates may invalidate this (meshes won't animate).
    /// </summary>
    private static void SetNodeChildren(SharpGLTF.Schema2.Node node, List<int> childIndices)
    {
        var field = node.GetType().GetField("_children", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(node, childIndices);
    }

    /// <summary>
    /// WORKAROUND: Reflection-based scene root modification (_nodes field). See SetNodeChildren.
    /// </summary>
    private static void SetSceneNodes(SharpGLTF.Schema2.Scene scene, List<int> nodeIndices)
    {
        var field = scene.GetType().GetField("_nodes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(scene, nodeIndices);
    }

    private static List<int> GetChildIndicesExcluding(IEnumerable<SharpGLTF.Schema2.Node> children, int excludeIndex)
    {
        return children.Where(c => c.LogicalIndex != excludeIndex).Select(c => c.LogicalIndex).ToList();
    }

    private static void AddChildToNode(SharpGLTF.Schema2.Node parent, int childIndex)
    {
        var indices = parent.VisualChildren.Select(c => c.LogicalIndex).ToList();
        if (!indices.Contains(childIndex))
        {
            indices.Add(childIndex);
            SetNodeChildren(parent, indices);
        }
    }

    private static SharpGLTF.Schema2.Node? FindNodeByName(ModelRoot model, string baseName, string? pathIdToken)
    {
        var node = model.LogicalNodes.FirstOrDefault(n => n.Name == baseName);
        if (node != null || pathIdToken == null) return node;

        string expected = $"{baseName}__{pathIdToken}";
        return model.LogicalNodes.FirstOrDefault(n => n.Name == expected)
            ?? model.LogicalNodes.FirstOrDefault(n => n.Name?.StartsWith(expected, StringComparison.Ordinal) == true);
    }

    #endregion
}
