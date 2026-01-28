using API.Contracts;
using GameData.Indexing;
using Localization.Indexing;
using Localization.Services;
using static API.Helpers.IconPaths;

namespace API.Services;

public static class ArtifactDtoBuilder
{
    public static ArtifactListItemDto BuildListItem(
        ArtifactsIndex.ArtifactRecord artifact,
        LangIndex lang,
        Func<LangIndex, string?, string?, string?>? getRaritySlotText = null)
    {
        var localizedName = lang.ResolveText(artifact.NameSid);
        var raritySlotText = getRaritySlotText?.Invoke(lang, artifact.Rarity, artifact.Slot);

        return new ArtifactListItemDto(
            Id: artifact.Id,
            Name: localizedName ?? artifact.Id,
            Rarity: string.IsNullOrEmpty(artifact.Rarity) ? null : artifact.Rarity,
            Slot: string.IsNullOrEmpty(artifact.Slot) ? null : artifact.Slot,
            RaritySlotText: raritySlotText,
            Icon: string.IsNullOrEmpty(artifact.Icon) ? null : Artifact(artifact.Icon)
        );
    }

    public static ArtifactPoolItemDto? BuildPoolItem(
        ArtifactsIndex.ArtifactRecord artifact,
        LangIndex lang)
    {
        var name = lang.ResolveText(artifact.NameSid);
        if (name == null) return null;

        // Magic scrolls display the contained spell name rather than the generic "Magic Scroll" text
        if (artifact.Id.Contains("magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
        {
            var spellSid = ExtractSpellSidFromScrollId(artifact.Id) + "_name";
            var spellName = lang.ResolveText(spellSid);
            if (spellName != null) name = spellName;
        }

        return new ArtifactPoolItemDto(
            artifact.Id,
            name,
            artifact.Rarity,
            Artifact(artifact.Icon)
        );
    }

    // "mythic_magic_scroll_artifact_fireball" -> "fireball"
    private static string ExtractSpellSidFromScrollId(string scrollId)
    {
        var prefixes = new[]
        {
            "mythic_magic_scroll_artifact_",
            "enchanted_magic_scroll_artifact_",
            "magic_scroll_artifact_"
        };

        foreach (var prefix in prefixes)
        {
            if (scrollId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return scrollId.Substring(prefix.Length);
        }

        return scrollId;
    }

    public static string? GetScrollCategory(string artifactId)
    {
        if (artifactId.StartsWith("mythic_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
            return "mythic";
        if (artifactId.StartsWith("enchanted_magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
            return "enchanted";
        if (artifactId.StartsWith("magic_scroll_artifact", StringComparison.OrdinalIgnoreCase))
            return "normal";
        return null;
    }

    public static List<ArtifactRarityPoolDto>? BuildPoolByRarities(
        List<string> rarityParams,
        ArtifactsIndex artifactsIndex,
        LangIndex lang,
        string locale)
    {
        if (rarityParams.Count == 0) return null;

        var rarityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var rarity in rarityParams)
        {
            var normalized = rarity.ToLowerInvariant();
            rarityCounts[normalized] = rarityCounts.GetValueOrDefault(normalized, 0) + 1;
        }

        var overlay = OverlayService.Instance;
        var result = new List<ArtifactRarityPoolDto>();

        foreach (var (rarity, draws) in rarityCounts)
        {
            var groups = BuildGroupsForRarity(rarity, artifactsIndex, lang, locale, overlay);
            if (groups == null || groups.Count == 0) continue;

            var rarityLabel = overlay.TryResolveFromOverlay($"label_{rarity}_rarity", locale)
                ?? CapitalizeFirst(rarity);
            var artifactLabel = overlay.TryResolveFromOverlay("entity_artifact", locale) ?? "artifact";
            var fullLabel = $"{rarityLabel} {artifactLabel}";

            result.Add(new ArtifactRarityPoolDto(rarity, fullLabel, draws, groups));
        }

        return result.Count > 0 ? result : null;
    }

    private static List<ArtifactPoolGroupDto>? BuildGroupsForRarity(
        string rarity,
        ArtifactsIndex artifactsIndex,
        LangIndex lang,
        string locale,
        OverlayService overlay)
    {
        var normalArtifacts = new List<ArtifactPoolItemDto>();
        var magicScrolls = new List<ArtifactPoolItemDto>();
        var enchantedScrolls = new List<ArtifactPoolItemDto>();
        var mythicScrolls = new List<ArtifactPoolItemDto>();

        var artifacts = artifactsIndex.GetByRarity(rarity);
        foreach (var artifact in artifacts)
        {
            var poolItem = BuildPoolItem(artifact, lang);
            if (poolItem == null) continue;

            var scrollCategory = GetScrollCategory(artifact.Id);
            if (scrollCategory != null && artifact.IsSpecialItem)
            {
                switch (scrollCategory)
                {
                    case "mythic": mythicScrolls.Add(poolItem); break;
                    case "enchanted": enchantedScrolls.Add(poolItem); break;
                    case "normal": magicScrolls.Add(poolItem); break;
                }
            }
            else
            {
                normalArtifacts.Add(poolItem);
            }
        }

        // Scroll groups count as 1 entry each regardless of how many spells they contain
        var totalPoolSize = normalArtifacts.Count
            + (magicScrolls.Count > 0 ? 1 : 0)
            + (enchantedScrolls.Count > 0 ? 1 : 0)
            + (mythicScrolls.Count > 0 ? 1 : 0);

        if (totalPoolSize == 0) return null;

        var groups = new List<ArtifactPoolGroupDto>();

        if (normalArtifacts.Count > 0)
        {
            var percentage = Math.Round((double)normalArtifacts.Count / totalPoolSize * 100, 2);
            var rarityLabel = overlay.TryResolveFromOverlay($"label_{rarity}_rarity", locale) ?? CapitalizeFirst(rarity);
            var artifactLabel = overlay.TryResolveFromOverlay("entity_artifact", locale) ?? "artifact";
            var label = $"{rarityLabel} {artifactLabel}";
            groups.Add(new ArtifactPoolGroupDto(label, rarity, normalArtifacts.Count, percentage, normalArtifacts));
        }

        if (magicScrolls.Count > 0)
        {
            var label = lang.ResolveText("scroll_box_name") ?? "Magic Scroll";
            var scrollRarity = magicScrolls[0].Rarity.ToLowerInvariant();
            groups.Add(new ArtifactPoolGroupDto(label, scrollRarity, magicScrolls.Count,
                Math.Round(1.0 / totalPoolSize * 100, 2), magicScrolls));
        }

        if (enchantedScrolls.Count > 0)
        {
            var label = lang.ResolveText("enchanted_scroll_box_name") ?? "Enchanted Magic Scroll";
            var scrollRarity = enchantedScrolls[0].Rarity.ToLowerInvariant();
            groups.Add(new ArtifactPoolGroupDto(label, scrollRarity, enchantedScrolls.Count,
                Math.Round(1.0 / totalPoolSize * 100, 2), enchantedScrolls));
        }

        if (mythicScrolls.Count > 0)
        {
            var label = lang.ResolveText("mythic_scroll_box_name") ?? "Mythic Magic Scroll";
            var scrollRarity = mythicScrolls[0].Rarity.ToLowerInvariant();
            groups.Add(new ArtifactPoolGroupDto(label, scrollRarity, mythicScrolls.Count,
                Math.Round(1.0 / totalPoolSize * 100, 2), mythicScrolls));
        }

        return groups.Count > 0 ? groups : null;
    }

    private static string CapitalizeFirst(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        return char.ToUpper(str[0]) + str.Substring(1);
    }
}
