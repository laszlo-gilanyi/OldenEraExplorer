namespace API.Helpers;

public static class IconPaths
{
    public static string UnitHexPortrait(string unitId) => $"icons/units/hex_portraits/{unitId}";
    public static string Artifact(string icon) => $"icons/artifacts/{icon}";
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
