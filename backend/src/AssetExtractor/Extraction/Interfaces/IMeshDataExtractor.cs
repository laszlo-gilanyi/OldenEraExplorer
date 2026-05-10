using AssetExtractor.Models;

namespace AssetExtractor.Extraction.Interfaces;

public interface IMeshDataExtractor
{
    List<MeshData> ExtractMeshesFromInner(
        HierarchyNode inner,
        SkeletonData skeleton,
        UnitData unitData,
        long animatorTransformPathId);

    List<MeshData> ExtractMeshesFromMapObject(
        HierarchyNode inner,
        MapObjectData objectData);

    List<MeshData> ExtractMeshesFromGameObject(
        HierarchyNode node,
        GameObjectData objectData);
}
