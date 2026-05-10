using AssetExtractor.Models;

namespace AssetExtractor.Extraction.Interfaces;

public interface IBoneDataExtractor
{
    List<string> ExtractSkinJointPaths(HierarchyNode inner, long animatorTransformPathId);
    List<string> ExtractSkinJointPathsRecursive(HierarchyNode inner, long animatorTransformPathId);

    int[] MapBoneIndicesToSkeleton(
        UnityReader.SkinnedMeshRenderer smr,
        Dictionary<string, int> skeletonIndexByPath,
        long animatorTransformPathId);

    string BuildTransformPath(
        UnityReader.Transform transform,
        long animatorTransformPathId);

    long GetAnimatorTransformPathId(HierarchyNode innerNode);
}
