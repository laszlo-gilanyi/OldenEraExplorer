#nullable enable
using AssetExtractor.Models;

namespace AssetExtractor.Extraction.Interfaces;

public interface IHierarchyExtractor
{
    HierarchyNode? BuildHierarchyNode(UnityReader.GameObject gameObject);

    (HierarchyNode? wrapper, HierarchyNode? inner) SelectWrapperAndInnerNodes(
        HierarchyNode prefabRoot,
        string unitName);

    (HierarchyNode? wrapper, HierarchyNode? inner) SelectWrapperAndInnerNodesForMapObject(
        HierarchyNode root);
}
