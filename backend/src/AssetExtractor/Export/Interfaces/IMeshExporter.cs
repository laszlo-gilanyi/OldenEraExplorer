#nullable enable
using AssetExtractor.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace AssetExtractor.Export.Interfaces;

/// <summary>
/// Interface for exporting mesh data to glTF format.
/// </summary>
public interface IMeshExporter
{
    /// <summary>
    /// Add meshes to the glTF scene.
    /// </summary>
    void AddMeshes(
        SceneBuilder scene,
        UnitData unitData,
        Dictionary<string, SharpGLTF.Scenes.NodeBuilder> nodeBuilders,
        bool isUnit = false);

    /// <summary>
    /// Create materials from unit data.
    /// </summary>
    Dictionary<string, MaterialBuilder> CreateMaterials(UnitData unitData, bool isUnit = false);
}
