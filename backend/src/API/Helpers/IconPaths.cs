namespace API.Helpers;

public static class IconPaths
{
    public static string UnitHexPortrait(string unitId) => $"icons/units/hex_portraits/{unitId}";
    public static string Artifact(string icon) => $"icons/artifacts/{icon}";
    public static string HeroMagic(string icon) => $"icons/hero_magics/{icon}";
    public static string HeroLargePortrait(string heroId) => $"icons/hero_large_portraits/{heroId}";
}
