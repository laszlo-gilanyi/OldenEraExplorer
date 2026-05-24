#nullable enable
using AssetExtractor.Models;
using AssetExtractor.Extraction.Interfaces;
using AssetExtractor.Utilities;
using AssetExtractor.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

public class AssetExtractor : IDisposable, IGameObjectProvider
{
    private readonly ILogger<AssetExtractor> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly AssetLoader _assetLoader;
    private readonly MaterialExtractor _materialExtractor;

    private readonly IHierarchyExtractor _hierarchyExtractor;
    private readonly IBoneDataExtractor _boneDataExtractor;
    private readonly IMeshDataExtractor _meshDataExtractor;
    private readonly IAnimationDataExtractor _animationDataExtractor;

    private bool _disposed;
    private const float POSE_TIME_EPSILON = 0.0001f;

    public AssetLoader Loader => _assetLoader;

    public AssetExtractor(string assetPath, ILogger<AssetExtractor>? logger = null, ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<AssetExtractor>.Instance;
        _loggerFactory = loggerFactory;
        _materialExtractor = new MaterialExtractor(_loggerFactory?.CreateLogger<MaterialExtractor>());

        _logger.LogInformation("Initializing AssetExtractor");
        _assetLoader = new AssetLoader(assetPath, _loggerFactory?.CreateLogger<AssetLoader>());
        _hierarchyExtractor = new HierarchyExtractor();
        _boneDataExtractor = new BoneDataExtractor(this, _loggerFactory?.CreateLogger<BoneDataExtractor>());
        _meshDataExtractor = new MeshDataExtractor(this, _boneDataExtractor, _materialExtractor, _loggerFactory?.CreateLogger<MeshDataExtractor>());
        _animationDataExtractor = new AnimationDataExtractor(this, _loggerFactory?.CreateLogger<AnimationDataExtractor>());
    }

    public List<string> ListResourcePaths(string? pattern = null) => _assetLoader.ListResourcePaths(pattern);
    public UnityReader.GameObject? FindPrefabByNameWithResourcePaths(string prefabName, string? pathPrefix = null) => _assetLoader.FindPrefabByNameWithResourcePaths(prefabName, pathPrefix);
    public UnityReader.GameObject? FindPrefabByResourcePath(string resourcePath) => _assetLoader.FindPrefabByResourcePath(resourcePath);
    public UnityReader.GameObject? FindGameObjectByPathId(string sourceFile, long pathId) => _assetLoader.FindGameObjectByPathId(sourceFile, pathId);
    public UnityReader.GameObject? FindPrefabByName(string prefabName) => _assetLoader.FindPrefabByName(prefabName);
    public List<string> ListAllPrefabNames() => _assetLoader.ListAllPrefabNames();
    public void Debug(string nameOrTerm) => _assetLoader.Debug(nameOrTerm);

    public UnitData ExtractUnit(string unitName)
    {
        _logger.LogInformation("Extracting unit: {UnitName}", unitName);
        var unitData = new UnitData { Name = unitName };

        var prefabRoot = FindPrefabByNameWithResourcePaths(unitName, "units/") ?? FindPrefabByName(unitName);
        if (prefabRoot == null) throw new Exception($"Prefab not found for unit '{unitName}'.");

        var rootNode = _hierarchyExtractor.BuildHierarchyNode(prefabRoot)
            ?? throw new Exception($"Failed to build hierarchy for unit: {unitName}");
        unitData.RootNode = rootNode;

        var (wrapperNode, innerNode) = _hierarchyExtractor.SelectWrapperAndInnerNodes(rootNode, unitName);
        if (wrapperNode == null || innerNode == null)
            throw new Exception($"Wrapper/inner selection failed for unit: {unitName}");

        unitData.WrapperNode = wrapperNode;
        unitData.InnerNode = innerNode;

        var animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(innerNode);
        var skinJointPaths = _boneDataExtractor.ExtractSkinJointPaths(innerNode, animatorTransformPathId);

        var skeletonRoots = SkeletonBuilder.FindSkeletonRoots(innerNode, skinJointPaths, _logger);
        if (skeletonRoots.Count == 0)
        {
            var legacyRoot = SkeletonBuilder.FindSkeletonRoot(innerNode);
            if (legacyRoot != null) skeletonRoots.Add(legacyRoot);
        }
        if (skeletonRoots.Count == 0)
            throw new Exception($"Skeleton root not found for unit: {unitName}");

        unitData.Skeleton = SkeletonBuilder.BuildSkeletonData(skeletonRoots, skinJointPaths, HierarchyExtractor.IsVFXNode, _logger);
        unitData.Meshes = _meshDataExtractor.ExtractMeshesFromInner(innerNode, unitData.Skeleton, unitData, animatorTransformPathId);
        unitData.Animations = _animationDataExtractor.ExtractAnimations(innerNode, unitData.Skeleton, skinJointPaths);

        TryApplyIdlePoseAsRestPose(unitData);
        return unitData;
    }

    public MapObjectData ExtractMapObject(string objectName)
    {
        _logger.LogInformation("Extracting map object: {ObjectName}", objectName);
        var objectData = new MapObjectData { Name = objectName };

        string prefabName = objectName;
        if (objectName.Contains('/'))
        {
            var parts = objectName.Split('/');
            objectData.Category = parts[0];
            prefabName = parts[^1];
        }

        var prefabRoot = FindPrefabByResourcePath($"objects/{objectName}") ?? FindPrefabByName(prefabName);
        if (prefabRoot == null) throw new Exception($"Prefab not found for map object '{objectName}'");

        var rootNode = _hierarchyExtractor.BuildHierarchyNode(prefabRoot)
            ?? throw new Exception($"Failed to build hierarchy for map object: {objectName}");
        objectData.RootNode = rootNode;

        var (wrapperNode, innerNode) = _hierarchyExtractor.SelectWrapperAndInnerNodesForMapObject(rootNode);
        objectData.WrapperNode = wrapperNode ?? rootNode;
        objectData.InnerNode = innerNode ?? rootNode;

        var animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(objectData.InnerNode);
        var skinJointPaths = _boneDataExtractor.ExtractSkinJointPathsRecursive(objectData.InnerNode, animatorTransformPathId);

        List<HierarchyNode> skeletonRoots = skinJointPaths.Count > 0
            ? SkeletonBuilder.FindSkeletonRoots(objectData.InnerNode, skinJointPaths, _logger)
            : objectData.InnerNode.Children.Where(c => !HierarchyExtractor.IsVFXNode(c.Name)).ToList();

        if (skeletonRoots.Count > 0)
        {
            objectData.Skeleton = SkeletonBuilder.BuildSkeletonData(skeletonRoots, skinJointPaths, HierarchyExtractor.IsVFXNode, _logger);
            objectData.LookPointPosition = ExtractLookPointPosition(objectData.Skeleton);
        }

        objectData.Meshes = _meshDataExtractor.ExtractMeshesFromMapObject(objectData.InnerNode, objectData);

        var animatorNodes = new List<HierarchyNode>();
        FindAllAnimatorNodesRecursive(rootNode, animatorNodes);

        if (animatorNodes.Count > 0)
        {
            objectData.Animations = new List<AnimationData>();
            foreach (var animatorNode in animatorNodes)
            {
                ExtractPrefabAnimationsCore(objectData.Skeleton, objectData.Animations, animatorNode);
            }
            if (objectData.Animations.Count > 1)
                objectData.Animations = MergeMultipleAnimatorAnimations(objectData.Animations);

            TryApplyIdlePoseAsRestPose(objectData);
        }

        return objectData;
    }

    public GameObjectData ExtractGameObject(string gameObjectName)
    {
        _logger.LogInformation("Extracting GameObject: {GameObjectName}", gameObjectName);
        var prefabData = new GameObjectData { Name = gameObjectName };

        var prefabRoot = FindPrefabByNameWithResourcePaths(gameObjectName) ?? FindPrefabByName(gameObjectName);
        if (prefabRoot == null) throw new Exception($"GameObject not found: '{gameObjectName}'");

        var rootNode = _hierarchyExtractor.BuildHierarchyNode(prefabRoot)
            ?? throw new Exception($"Failed to build hierarchy for GameObject: {gameObjectName}");

        prefabData.RootNode = rootNode;
        prefabData.WrapperNode = rootNode;
        prefabData.InnerNode = rootNode;

        var animatorTransformPathId = _boneDataExtractor.GetAnimatorTransformPathId(rootNode);
        var skinJointPaths = _boneDataExtractor.ExtractSkinJointPathsRecursive(rootNode, animatorTransformPathId);

        List<HierarchyNode> skeletonRoots = skinJointPaths.Count > 0
            ? SkeletonBuilder.FindSkeletonRoots(rootNode, skinJointPaths, _logger)
            : rootNode.Children.Where(c => !HierarchyExtractor.IsVFXNode(c.Name)).ToList();

        if (skeletonRoots.Count > 0)
        {
            prefabData.Skeleton = SkeletonBuilder.BuildSkeletonData(skeletonRoots, skinJointPaths, HierarchyExtractor.IsVFXNode, _logger);
        }

        prefabData.Meshes = _meshDataExtractor.ExtractMeshesFromGameObject(rootNode, prefabData);

        var animatorNodes = new List<HierarchyNode>();
        FindAllAnimatorNodesRecursive(rootNode, animatorNodes);

        if (animatorNodes.Count > 0)
        {
            prefabData.Animations = new List<AnimationData>();
            foreach (var animatorNode in animatorNodes)
            {
                ExtractPrefabAnimationsCore(prefabData.Skeleton, prefabData.Animations, animatorNode);
            }
            if (prefabData.Animations.Count > 1)
                prefabData.Animations = MergeMultipleAnimatorAnimations(prefabData.Animations);

            TryApplyIdlePoseAsRestPose(prefabData);
        }

        return prefabData;
    }

    public PrefabData ExtractPrefab(string prefabName, PrefabType? typeHint = null)
    {
        using (_logger.BeginScope("Prefab: {PrefabName}", prefabName))
        {
            typeHint ??= InferPrefabType(prefabName);
            return typeHint switch
            {
                PrefabType.Unit => ExtractUnit(prefabName),
                PrefabType.MapObject => ExtractMapObject(prefabName),
                _ => throw new ArgumentException($"Unsupported prefab type: {typeHint}")
            };
        }
    }

    private PrefabType InferPrefabType(string name)
    {
        if (name.Contains('/'))
        {
            string lower = name.ToLowerInvariant();
            if (lower.StartsWith("interactive/") || lower.StartsWith("resource/") ||
                lower.StartsWith("barracks/") || lower.StartsWith("objects/") ||
                lower.StartsWith("artifact/"))
            {
                return PrefabType.MapObject;
            }
        }
        return PrefabType.Unit;
    }

    public List<string> ListUnits() => new UnitListProvider(_assetLoader.AssetPath).ListUnits();

    public List<PrefabDescriptor> ListPrefabs(PrefabType type) => type switch
    {
        PrefabType.Unit => new UnitListProvider(_assetLoader.AssetPath).ListPrefabs(),
        PrefabType.MapObject => new MapObjectListProvider(_assetLoader.AssetPath).ListPrefabs(),
        _ => new List<PrefabDescriptor>()
    };

    public List<string> ListMapObjects() => new MapObjectListProvider(_assetLoader.AssetPath).ListMapObjects();

    public void ClearMaterialCaches()
    {
        _materialExtractor.ClearCaches();
    }

    private void FindAllAnimatorNodesRecursive(HierarchyNode node, List<HierarchyNode> results)
    {
        if (!node.IsActive) return;
        if (node.HasAnimator) results.Add(node);
        foreach (var child in node.Children) FindAllAnimatorNodesRecursive(child, results);
    }

    private List<AnimationData> MergeMultipleAnimatorAnimations(List<AnimationData> animations)
    {
        const float DURATION_EPSILON = 0.1f;

        var groups = new List<List<AnimationData>>();
        foreach (var anim in animations)
        {
            var matchingGroup = groups.FirstOrDefault(g => Math.Abs(g[0].Length - anim.Length) < DURATION_EPSILON);
            if (matchingGroup != null) matchingGroup.Add(anim);
            else groups.Add(new List<AnimationData> { anim });
        }

        var merged = new List<AnimationData>();
        foreach (var group in groups)
        {
            if (group.Count == 1) { merged.Add(group[0]); continue; }

            var first = group[0];
            var mergedAnim = new AnimationData
            {
                Name = first.Name.Split('_')[0] + "_combined",
                Length = first.Length,
                FrameRate = first.FrameRate,
                Channels = new List<AnimationChannel>()
            };
            foreach (var anim in group) mergedAnim.Channels.AddRange(anim.Channels);
            merged.Add(mergedAnim);
        }

        return merged;
    }

    private void ExtractPrefabAnimationsCore(SkeletonData? skeleton, List<AnimationData> animationsList, HierarchyNode animatorNode)
    {
        if (skeleton == null || skeleton.Bones.Count == 0) return;

        var animatorBone = skeleton.Bones.FirstOrDefault(b => b.Name == animatorNode.Name);
        string animatorPath = animatorBone?.Path ?? "";

        var animations = _animationDataExtractor.ExtractAnimations(animatorNode, skeleton, new List<string>());
        if (animations.Count == 0) return;

        foreach (var anim in animations)
        {
            foreach (var channel in anim.Channels)
            {
                if (string.IsNullOrEmpty(channel.BonePath)) channel.BonePath = animatorPath;
                else if (!string.IsNullOrEmpty(animatorPath)) channel.BonePath = $"{animatorPath}/{channel.BonePath}";
            }
        }

        animationsList.AddRange(animations);
    }

    private void TryApplyIdlePoseAsRestPose(PrefabData prefabData)
    {
        (SkeletonData? skeleton, IReadOnlyList<AnimationData>? animations) = prefabData switch
        {
            UnitData ud => (ud.Skeleton, ud.Animations),
            MapObjectData mod => (mod.Skeleton, mod.Animations),
            GameObjectData god => (god.Skeleton, god.Animations),
            _ => (null, null)
        };

        if (skeleton == null || animations == null) return;
        TryApplyIdlePoseAsRestPose(skeleton, animations);
    }

    private void TryApplyIdlePoseAsRestPose(SkeletonData? skeleton, IReadOnlyList<AnimationData> animations)
    {
        if (skeleton == null || skeleton.Bones.Count == 0) return;

        var idle = SelectIdleAnimation(animations);
        if (idle == null) return;

        var bonesByPath = skeleton.Bones.ToDictionary(b => b.Path, StringComparer.Ordinal);
        var touchedBones = new HashSet<string>(StringComparer.Ordinal);

        foreach (var channel in idle.Channels)
        {
            if (string.IsNullOrWhiteSpace(channel.BonePath)) continue;
            if (!bonesByPath.TryGetValue(channel.BonePath, out var bone)) continue;

            float value = SampleCurveAtTime(channel.Keyframes, 0f);
            ApplyChannelValue(bone.LocalTransform, channel.Property, value);
            touchedBones.Add(bone.Path);
        }

        foreach (var bone in skeleton.Bones)
        {
            if (touchedBones.Contains(bone.Path)) NormalizeQuaternionInPlace(bone.LocalTransform.Rotation);
        }
    }

    private static AnimationData? SelectIdleAnimation(IReadOnlyList<AnimationData> animations)
    {
        if (animations == null || animations.Count == 0) return null;

        AnimationData? best = null;
        int bestScore = int.MinValue;

        foreach (var anim in animations)
        {
            if (anim == null) continue;
            var name = (anim.Name ?? string.Empty).Trim().ToLowerInvariant();
            if (name.Length == 0) continue;

            int score = 0;
            if (name == "idle") score += 10_000;
            else if (name.StartsWith("idle_", StringComparison.Ordinal)) score += 8_000;
            else if (name.StartsWith("idle", StringComparison.Ordinal)) score += 6_000;
            else if (name.Contains("idle", StringComparison.Ordinal)) score += 3_000;
            else continue;

            score += Math.Min(anim.Channels?.Count ?? 0, 500);
            if (best == null || score > bestScore) { best = anim; bestScore = score; }
        }

        return best;
    }

    private static void ApplyChannelValue(Models.Transform target, AnimationProperty property, float value)
    {
        switch (property)
        {
            case AnimationProperty.PositionX: target.Position.X = value; break;
            case AnimationProperty.PositionY: target.Position.Y = value; break;
            case AnimationProperty.PositionZ: target.Position.Z = value; break;
            case AnimationProperty.RotationX: target.Rotation.X = value; break;
            case AnimationProperty.RotationY: target.Rotation.Y = value; break;
            case AnimationProperty.RotationZ: target.Rotation.Z = value; break;
            case AnimationProperty.RotationW: target.Rotation.W = value; break;
            case AnimationProperty.ScaleX: target.Scale.X = value; break;
            case AnimationProperty.ScaleY: target.Scale.Y = value; break;
            case AnimationProperty.ScaleZ: target.Scale.Z = value; break;
        }
    }

    private static float SampleCurveAtTime(IReadOnlyList<Keyframe> keyframes, float time)
    {
        if (keyframes == null || keyframes.Count == 0 || !float.IsFinite(time)) return 0f;

        var keys = keyframes.Where(k => float.IsFinite(k.Time) && float.IsFinite(k.Value)).OrderBy(k => k.Time).ToList();
        if (keys.Count == 0) return 0f;

        if (time <= keys[0].Time + POSE_TIME_EPSILON) return keys[0].Value;
        if (time >= keys[^1].Time - POSE_TIME_EPSILON) return keys[^1].Value;

        for (int i = 0; i < keys.Count - 1; i++)
        {
            var k0 = keys[i];
            var k1 = keys[i + 1];
            if (time > k1.Time + POSE_TIME_EPSILON) continue;

            float dt = k1.Time - k0.Time;
            if (dt <= POSE_TIME_EPSILON) return k0.Value;

            float u = (time - k0.Time) / dt;
            u = Math.Clamp(u, 0f, 1f);
            float m0 = k0.OutTangent;
            float m1 = k1.InTangent;

            if (!float.IsFinite(m0) || !float.IsFinite(m1)) return k0.Value + (k1.Value - k0.Value) * u;

            float u2 = u * u, u3 = u2 * u;
            float h00 = 2f * u3 - 3f * u2 + 1f;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;
            float value = h00 * k0.Value + h10 * (m0 * dt) + h01 * k1.Value + h11 * (m1 * dt);
            return float.IsFinite(value) ? value : k0.Value;
        }

        return keys[^1].Value;
    }

    private static void NormalizeQuaternionInPlace(Models.Quaternion q)
    {
        if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W))
        { q.X = 0f; q.Y = 0f; q.Z = 0f; q.W = 1f; return; }

        float lenSq = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
        if (lenSq <= 0f || !float.IsFinite(lenSq))
        { q.X = 0f; q.Y = 0f; q.Z = 0f; q.W = 1f; return; }

        float invLen = 1f / MathF.Sqrt(lenSq);
        q.X *= invLen; q.Y *= invLen; q.Z *= invLen; q.W *= invLen;
    }

    private Models.Vector3? ExtractLookPointPosition(SkeletonData skeleton)
    {
        var lookPointBone = skeleton.Bones.FirstOrDefault(b =>
            string.Equals(b.Name, "look_point", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.Name, "Look_point", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.Name, "lookpoint", StringComparison.OrdinalIgnoreCase));

        if (lookPointBone == null) return null;

        var pos = lookPointBone.LocalTransform.Position;
        const float epsilon = 0.05f;
        float xzLengthSq = pos.X * pos.X + pos.Z * pos.Z;
        if (xzLengthSq < epsilon * epsilon) return null;
        return pos;
    }

    public List<(string Name, UnityReader.UnityTexture Texture)> SearchTextures(string namePattern)
        => _assetLoader.SearchTextures(namePattern);

    public void Dispose()
    {
        if (!_disposed)
        {
            _assetLoader?.Dispose();
            _disposed = true;
        }
    }
}
