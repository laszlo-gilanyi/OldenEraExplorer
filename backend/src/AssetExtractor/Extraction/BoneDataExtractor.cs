#nullable enable
using AssetExtractor.Models;
using AssetExtractor.Extraction.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

public class BoneDataExtractor : IBoneDataExtractor
{
    private readonly ILogger<BoneDataExtractor> _logger;
    private readonly IGameObjectProvider _gameObjectProvider;

    public BoneDataExtractor(
        IGameObjectProvider gameObjectProvider,
        ILogger<BoneDataExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<BoneDataExtractor>.Instance;
        _gameObjectProvider = gameObjectProvider;
    }

    public List<string> ExtractSkinJointPaths(HierarchyNode inner, long animatorTransformPathId)
    {
        var unique = new HashSet<string>();
        var ordered = new List<string>();

        foreach (var child in inner.Children)
        {
            if (string.Equals(child.Name, "Root", StringComparison.OrdinalIgnoreCase)) continue;

            var gameObject = _gameObjectProvider.FindGameObjectByPathId(child.SourceFile, child.PathID);
            if (gameObject == null) continue;
            if (!gameObject.TryGetComponent<UnityReader.SkinnedMeshRenderer>(out var smr)) continue;

            _logger.LogInformation("Collecting bones from mesh '{MeshName}'", child.Name);

            foreach (var boneTransform in smr.Bones)
            {
                if (boneTransform == null) continue;
                string path = BuildTransformPath(boneTransform, animatorTransformPathId);
                if (unique.Add(path)) ordered.Add(path);
            }
        }

        _logger.LogInformation("Collected {BoneCount} unique bones from all meshes", ordered.Count);
        return ordered;
    }

    public List<string> ExtractSkinJointPathsRecursive(HierarchyNode inner, long animatorTransformPathId)
    {
        var unique = new HashSet<string>();
        var ordered = new List<string>();
        Recurse(inner, animatorTransformPathId, unique, ordered);
        _logger.LogInformation("Collected {BoneCount} unique bones from all meshes (recursive)", ordered.Count);
        return ordered;
    }

    private void Recurse(
        HierarchyNode node,
        long animatorTransformPathId,
        HashSet<string> unique,
        List<string> ordered)
    {
        var gameObject = _gameObjectProvider.FindGameObjectByPathId(node.SourceFile, node.PathID);
        if (gameObject != null && gameObject.TryGetComponent<UnityReader.SkinnedMeshRenderer>(out var smr))
        {
            _logger.LogInformation("Collecting bones from mesh '{MeshName}' (recursive)", node.Name);

            foreach (var boneTransform in smr.Bones)
            {
                if (boneTransform == null) continue;
                string path = BuildTransformPath(boneTransform, animatorTransformPathId);
                if (unique.Add(path)) ordered.Add(path);
            }
        }

        foreach (var child in node.Children)
        {
            Recurse(child, animatorTransformPathId, unique, ordered);
        }
    }

    public int[] MapBoneIndicesToSkeleton(
        UnityReader.SkinnedMeshRenderer smr,
        Dictionary<string, int> skeletonIndexByPath,
        long animatorTransformPathId)
    {
        var boneIndices = new List<int>();

        foreach (var boneTransform in smr.Bones)
        {
            if (boneTransform == null)
            {
                boneIndices.Add(-1);
                continue;
            }

            string bonePath = BuildTransformPath(boneTransform, animatorTransformPathId);
            var boneName = boneTransform.GameObject?.Name ?? "";

            if (skeletonIndexByPath.TryGetValue(bonePath, out int skelIndex))
            {
                boneIndices.Add(skelIndex);
                continue;
            }

            int nameMatch = -1;
            foreach (var kvp in skeletonIndexByPath)
            {
                if (kvp.Key.EndsWith("/" + boneName) || kvp.Key == boneName)
                {
                    nameMatch = kvp.Value;
                    break;
                }
            }

            if (nameMatch >= 0)
            {
                boneIndices.Add(nameMatch);
            }
            else
            {
                _logger.LogWarning(
                    "Bone '{BoneName}' not found in skeleton (path: {BonePath})",
                    boneName, bonePath);
                boneIndices.Add(-1);
            }
        }

        return boneIndices.ToArray();
    }

    public string BuildTransformPath(UnityReader.Transform transform, long animatorTransformPathId)
    {
        var pathParts = new List<string>();
        UnityReader.Transform? current = transform;

        while (current != null)
        {
            var gameObject = current.GameObject;
            var name = gameObject?.Name ?? "Unnamed";
            pathParts.Insert(0, name);

            var parent = current.Parent;
            if (parent != null && parent.PathId == animatorTransformPathId)
                break;

            current = parent;
        }

        return string.Join("/", pathParts);
    }

    public long GetAnimatorTransformPathId(HierarchyNode innerNode)
    {
        var gameObject = _gameObjectProvider.FindGameObjectByPathId(innerNode.SourceFile, innerNode.PathID);
        if (gameObject == null) return 0;
        return gameObject.Transform?.PathId ?? 0;
    }
}
