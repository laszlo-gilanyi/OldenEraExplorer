using API.Contracts;
using GameData.Indexing;
using Localization.Resolution;
using Localization.Services;
using static API.Helpers.IconPaths;
using static API.Helpers.LocalizationHelper;

namespace API.Services;

public static class SpellDtoBuilder
{
    /// <summary>
    /// Builds spell pool groups from HeroMagicRandomAdditionReward parameters.
    /// Parameters format: ["school", "tier: weight", ...] e.g. ["night", "1: 60", "2: 40"]
    /// Groups spells by tier with weight percentages.
    /// Used by: MapObjectsEndpoints.BuildBankInfoDto
    /// </summary>
    public static List<SpellPoolGroupDto>? BuildPoolBySchoolAndTiers(
        List<string> parameters,
        SpellsIndex spellsIndex,
        ITextResolver resolver,
        string locale)
    {
        if (parameters.Count < 2) return null;

        var school = parameters[0];  // "night", "day", "space", "primal"
        var groups = new List<SpellPoolGroupDto>();
        var overlay = OverlayService.Instance;
        var tierLabel = overlay.TryResolveFromOverlay("label_spell_tier", locale) ?? "Tier";

        for (int i = 1; i < parameters.Count; i++)
        {
            var tierWeight = ParseTierWeight(parameters[i]);
            if (tierWeight == null) continue;

            var (tier, weight) = tierWeight.Value;

            var matchingSpells = spellsIndex.Spells.Values
                .Where(s => s.School.Equals(school, StringComparison.OrdinalIgnoreCase)
                         && s.Rank == tier
                         && !s.IsSpecialMagic)
                .OrderBy(s => s.Id)
                .ToList();

            if (matchingSpells.Count == 0) continue;

            var spellItems = matchingSpells.Select(spell => new SpellPoolItemDto(
                spell.Id,
                TryResolveText(resolver, spell.NameSid, locale) ?? spell.Id,
                spell.Rank,
                HeroMagic(spell.Icon)
            )).ToList();

            groups.Add(new SpellPoolGroupDto(
                $"{tierLabel} {tier}",
                tier,
                weight,
                spellItems.Count,
                spellItems
            ));
        }

        return groups.Count > 0 ? groups : null;
    }

    /// <summary>
    /// Parses "tier: weight" format from parameters.
    /// Example: "1: 60" -> (1, 60.0)
    /// </summary>
    private static (int Tier, double Weight)? ParseTierWeight(string param)
    {
        var parts = param.Split(':');
        if (parts.Length != 2) return null;

        if (!int.TryParse(parts[0].Trim(), out var tier)) return null;
        if (!double.TryParse(parts[1].Trim(), out var weight)) return null;

        return (tier, weight);
    }
}
