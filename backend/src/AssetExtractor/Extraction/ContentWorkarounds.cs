namespace AssetExtractor.Extraction;

// Re-binds textures for prefabs whose materials were swapped to runtime-VFX placeholders in the EA build.
// Those placeholders carry no texture and have alpha=0, but the original texture still exists in the bundle
// as an orphan, so the mesh would otherwise render invisible.
public static class ContentWorkarounds
{
    public record MaterialOverride(
        string TextureName,
        float BaseColorR,
        float BaseColorG,
        float BaseColorB,
        float BaseColorA);

    public static readonly Dictionary<string, Dictionary<string, MaterialOverride>> ByPrefab = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anesthesis_flask_of_oblivion_artifact"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["flask_01_vfx"] = new("flask_of_oblivion_glass", 0.54f, 0.74f, 1.0f, 0.40f),
            ["flask_02_vfx"] = new("flask_of_oblivion", 1.0f, 1.0f, 1.0f, 0.71f),
        },
    };

    public static MaterialOverride? Lookup(string prefabName, string materialName)
    {
        if (string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(materialName))
            return null;

        // Bulk map-object extraction stores names as "category/name" (e.g. "artifact/foo"),
        // while single-prefab extraction uses the bare name. Match either form.
        var bareName = prefabName.Contains('/')
            ? prefabName[(prefabName.LastIndexOf('/') + 1)..]
            : prefabName;

        foreach (var key in new[] { prefabName, bareName })
        {
            if (ByPrefab.TryGetValue(key, out var perMaterial) &&
                perMaterial.TryGetValue(materialName, out var ovr))
            {
                return ovr;
            }
        }
        return null;
    }
}
