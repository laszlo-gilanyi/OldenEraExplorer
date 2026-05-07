namespace API.Helpers;

public static class IconPaths
{
    public static string UnitHexPortrait(string unitId) => $"icons/units/hex_portraits/{unitId}";
    public static string Artifact(string icon) => $"icons/artifacts/{icon}";

    // Scroll artifacts ship per-spell icons under a category subfolder; everything else lives flat in icons/artifacts.
    public static string ArtifactIcon(string artifactId, string icon)
    {
        var subfolder = ScrollIconSubfolder(artifactId);
        return subfolder is null ? Artifact(icon) : $"icons/artifacts/{subfolder}/{icon}";
    }

    // Every spell variant of a scroll category shares the same GLB, so the viewer represents the bundle with one generic icon.
    public static string? ArtifactGenericScrollIcon(string artifactId)
    {
        var name = ScrollIconSubfolder(artifactId);
        return name is null ? null : $"icons/artifacts/{name}";
    }

    private static string? ScrollIconSubfolder(string artifactId)
    {
        if (artifactId.StartsWith("mythic_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
            return "mythic_scroll_box_artifact";
        if (artifactId.StartsWith("enchanted_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
            return "enchanted_magic_scroll_artifact";
        if (artifactId.StartsWith("magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
            return "magic_scroll_artifact";
        return null;
    }

    public static string HeroMagic(string icon) => $"icons/hero_magics/{icon}";
    public static string HeroLargePortrait(string heroId) => $"icons/hero_large_portraits/{heroId}";

    public static string? MapObjectIcon(string? prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath)) return null;

        var iconPath = prefabPath.Replace('\\', '/');
        if (!iconPath.StartsWith("objects/", StringComparison.OrdinalIgnoreCase))
            iconPath = $"objects/{iconPath}";

        return string.IsNullOrWhiteSpace(iconPath) ? null : iconPath;
    }
}
