using AssetExtractor.Models;

namespace AssetExtractor.Utilities;

public enum UnitVariantKind
{
    Base,
    Level
}

public record UnitVariantInfo(string BaseName, UnitVariantKind Kind, string Suffix);

public static class HierarchySelector
{
    public static UnitVariantInfo ParseUnitVariant(string unitName)
    {
        if (string.IsNullOrEmpty(unitName))
            return new UnitVariantInfo(unitName, UnitVariantKind.Base, "");

        int lastUnderscore = unitName.LastIndexOf('_');
        if (lastUnderscore > 0 && lastUnderscore < unitName.Length - 1)
        {
            string suffix = unitName[(lastUnderscore + 1)..];
            string baseName = unitName[..lastUnderscore];

            if (int.TryParse(suffix, out _))
            {
                return new UnitVariantInfo(baseName, UnitVariantKind.Level, suffix);
            }
        }

        return new UnitVariantInfo(unitName, UnitVariantKind.Base, "");
    }
}
