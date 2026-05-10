#nullable enable
using AssetExtractor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Utilities;

public static class SkeletonBuilder
{
    // Unity animation paths are relative to the Animator GameObject, so skeleton roots
    // are the first segment of each bone path (direct children of the Animator).
    public static List<HierarchyNode> FindSkeletonRoots(
        HierarchyNode inner,
        List<string> skinJointPaths,
        ILogger? logger = null)
    {
        var orderedRootNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in skinJointPaths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            var firstSlash = path.IndexOf('/');
            string rootName = firstSlash >= 0 ? path.Substring(0, firstSlash) : path;
            if (string.IsNullOrEmpty(rootName))
                continue;

            if (seen.Add(rootName))
            {
                orderedRootNames.Add(rootName);
            }
        }

        var roots = new List<HierarchyNode>();
        foreach (var rootName in orderedRootNames)
        {
            var matches = inner.Children
                .Where(c => string.Equals(c.Name, rootName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                (logger ?? NullLogger.Instance).LogWarning(
                    "Skeleton root candidate '{RootName}' not found as a direct child of '{InnerNodeName}'",
                    rootName,
                    inner.Name);
                continue;
            }

            var best = matches
                .OrderByDescending(m => m.Children.Count)
                .First();

            roots.Add(best);
        }

        return roots;
    }

    // Legacy fallback: child of inner named "Root".
    public static HierarchyNode? FindSkeletonRoot(HierarchyNode inner)
    {
        foreach (var child in inner.Children)
        {
            if (string.Equals(child.Name, "Root", StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    public static SkeletonData BuildSkeletonData(
        HierarchyNode skeletonRoot,
        List<string> skinJointPaths,
        Func<string, bool> isVFXNode,
        ILogger? logger = null)
    {
        return BuildSkeletonData(new List<HierarchyNode> { skeletonRoot }, skinJointPaths, isVFXNode, logger);
    }

    // Multi-root supports composite rigs (horse + rider + weapon). Paths are relative
    // to the Animator GameObject, matching Unity AnimationClip binding paths.
    public static SkeletonData BuildSkeletonData(
        List<HierarchyNode> skeletonRoots,
        List<string> skinJointPaths,
        Func<string, bool> isVFXNode,
        ILogger? logger = null)
    {
        if (skeletonRoots.Count == 0)
        {
            throw new ArgumentException("skeletonRoots cannot be empty", nameof(skeletonRoots));
        }

        var skeleton = new SkeletonData
        {
            RootBone = skeletonRoots[0]
        };

        var nodeByPath = new Dictionary<string, HierarchyNode>(StringComparer.Ordinal);
        foreach (var root in skeletonRoots)
        {
            BuildNodeLookup(root, "", nodeByPath, isVFXNode);
        }

        var log = logger ?? NullLogger.Instance;
        log.LogDebug(
            "Built node lookup with {NodeCount} nodes",
            nodeByPath.Count);
        log.LogDebug(
            "Skin joint paths count: {SkinJointPathCount}",
            skinJointPaths.Count);

        var addedPaths = new HashSet<string>(StringComparer.Ordinal);

        // Roots first so subsequent bones can resolve their parent path (e.g. Root/Hips
        // requires Root to already be present).
        foreach (var root in skeletonRoots)
        {
            if (addedPaths.Contains(root.Name))
            {
                continue;
            }

            var rootBoneData = new BoneData
            {
                Name = root.Name,
                Path = root.Name,
                Index = skeleton.Bones.Count,
                ParentIndex = -1,
                LocalTransform = root.LocalTransform,
                WorldMatrix = Models.Matrix4x4.Identity,
                InverseBindMatrix = Models.Matrix4x4.Identity
            };

            skeleton.Bones.Add(rootBoneData);
            skeleton.RestPose[root.Name] = rootBoneData.WorldMatrix;
            addedPaths.Add(root.Name);
            log.LogDebug(
                "Added skeleton root bone: {RootBoneName}",
                root.Name);
        }

        // SMR bone order matters for skinning, so we must add bones used by meshes first.
        for (int i = 0; i < skinJointPaths.Count; i++)
        {
            string path = skinJointPaths[i];

            if (string.IsNullOrEmpty(path))
            {
                log.LogWarning(
                    "Skin joint {JointIndex} has empty path, skipping",
                    i);
                continue;
            }

            // Unity bones can be shared across meshes; deduplicate.
            if (addedPaths.Contains(path))
            {
                continue;
            }

            if (!nodeByPath.TryGetValue(path, out var node))
            {
                log.LogWarning(
                    "Skin joint path '{JointPath}' not found in hierarchy",
                    path);
                continue;
            }

            // SMR bone order isn't topologically sorted, so the parent may not exist yet;
            // NormalizeSkeletonParentIndices fixes any -1 entries afterwards.
            int parentIndex = -1;
            string parentPath = GetParentPath(path);
            if (!string.IsNullOrEmpty(parentPath))
            {
                parentIndex = skeleton.Bones.FindIndex(b => b.Path == parentPath);
            }

            var boneData = new BoneData
            {
                Name = node.Name,
                Path = path,
                Index = skeleton.Bones.Count,
                ParentIndex = parentIndex,
                LocalTransform = node.LocalTransform,
                WorldMatrix = Models.Matrix4x4.Identity,
                InverseBindMatrix = Models.Matrix4x4.Identity
            };

            skeleton.Bones.Add(boneData);
            skeleton.RestPose[path] = boneData.WorldMatrix;
            addedPaths.Add(path);
        }

        log.LogDebug(
            "Added {BoneCount} bones from SMR in order",
            skeleton.Bones.Count);

        foreach (var root in skeletonRoots)
        {
            AddRemainingBones(root, "", skeleton, nodeByPath, addedPaths, isVFXNode, logger);
        }

        NormalizeSkeletonParentIndices(skeleton, logger);

        return skeleton;
    }

    // SMR bone order is preserved for skinning, but parent links must match the transform
    // hierarchy regardless of insertion order, so fix them up after the fact.
    public static void NormalizeSkeletonParentIndices(SkeletonData skeleton, ILogger? logger = null)
    {
        var indexByPath = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            var bone = skeleton.Bones[i];
            bone.Index = i;
            indexByPath[bone.Path] = i;
        }

        int missingParents = 0;

        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            var bone = skeleton.Bones[i];
            string parentPath = GetParentPath(bone.Path);

            if (string.IsNullOrEmpty(parentPath))
            {
                bone.ParentIndex = -1;
                continue;
            }

            if (indexByPath.TryGetValue(parentPath, out int parentIndex))
            {
                bone.ParentIndex = parentIndex;
            }
            else
            {
                bone.ParentIndex = -1;
                missingParents++;
                if (missingParents <= 5)
                {
                    (logger ?? NullLogger.Instance).LogWarning(
                        "Missing parent '{ParentPath}' for bone '{BonePath}'",
                        parentPath,
                        bone.Path);
                }
            }
        }

        if (missingParents > 0)
        {
            (logger ?? NullLogger.Instance).LogWarning(
                "{MissingParentCount} bones have missing parents after normalization (armature may be invalid)",
                missingParents);
        }
    }

    public static void AddRemainingBones(
        HierarchyNode node,
        string parentPath,
        SkeletonData skeleton,
        Dictionary<string, HierarchyNode> nodeByPath,
        HashSet<string> addedPaths,
        Func<string, bool> isVFXNode,
        ILogger? logger = null)
    {
        string path = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";

        if (!addedPaths.Contains(path))
        {
            int parentIndex = -1;
            if (!string.IsNullOrEmpty(parentPath))
            {
                parentIndex = skeleton.Bones.FindIndex(b => b.Path == parentPath);
            }

            var boneData = new BoneData
            {
                Name = node.Name,
                Path = path,
                Index = skeleton.Bones.Count,
                ParentIndex = parentIndex,
                LocalTransform = node.LocalTransform,
                WorldMatrix = Models.Matrix4x4.Identity,
                InverseBindMatrix = Models.Matrix4x4.Identity
            };

            skeleton.Bones.Add(boneData);
            skeleton.RestPose[path] = boneData.WorldMatrix;
            addedPaths.Add(path);

            (logger ?? NullLogger.Instance).LogDebug(
                "Added non-SMR bone: {BonePath}",
                path);
        }

        foreach (var child in node.Children)
        {
            if (!isVFXNode(child.Name))
            {
                AddRemainingBones(child, path, skeleton, nodeByPath, addedPaths, isVFXNode, logger);
            }
        }
    }

    private static void BuildNodeLookup(HierarchyNode node, string parentPath, Dictionary<string, HierarchyNode> lookup, Func<string, bool> isVFXNode)
    {
        string path = string.IsNullOrEmpty(parentPath) ? node.Name : $"{parentPath}/{node.Name}";
        lookup[path] = node;

        foreach (var child in node.Children)
        {
            if (!isVFXNode(child.Name))
            {
                BuildNodeLookup(child, path, lookup, isVFXNode);
            }
        }
    }

    private static string GetParentPath(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0)
            return "";
        return path.Substring(0, lastSlash);
    }
}
