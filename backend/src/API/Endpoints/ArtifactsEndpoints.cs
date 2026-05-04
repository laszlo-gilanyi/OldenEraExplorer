using GameData.Indexing;
using Localization.Resolution;
using Localization.Services;
using API.Contracts;
using API.Helpers;
using API.Services;
using static API.Helpers.LocalizationHelper;
using static API.Helpers.IconPaths;

namespace API.Endpoints;

public static class ArtifactsEndpoints
{
    public static IEndpointRouteBuilder MapArtifactsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/artifacts")
            .WithTags("Artifacts")
            ;

        group.MapGet("/", GetArtifacts)
            .WithName("GetArtifacts")
            .WithSummary("List all artifacts")
            .WithDescription("Returns a list of all artifacts. Supports search filtering by ID, name, rarity, or slot.")
            .Produces<List<ArtifactListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/{id}", GetArtifactById)
            .WithName("GetArtifactById")
            .WithSummary("Get artifact details")
            .WithDescription("Returns detailed information about a specific artifact including stats, set bonuses, and localized text.")
            .Produces<ArtifactDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetArtifacts(
        IGameDataService dataService,
        IGamePathService pathService,
        string? search = null)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        var data = dataService.Data;
        var lang = data.Lang;
        var artifactsIndex = data.ArtifactsIndex;

        IEnumerable<ArtifactsIndex.ArtifactRecord> artifacts = artifactsIndex.Artifacts.Values
            .Where(a => HasArtifactLocalization(a, lang));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            artifacts = artifacts.Where(a =>
                a.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (lang.ResolveText(a.NameSid)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true) ||
                a.Rarity.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                a.Slot.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (GetRaritySlotText(lang, a.Rarity, a.Slot)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
            );
        }

        var artifactsList = artifacts
            .Select(a => ArtifactDtoBuilder.BuildListItem(a, lang, GetRaritySlotText))
            .ToList();

        return Results.Ok(artifactsList);
    }

    private static IResult GetArtifactById(string id, IGameDataService dataService, IGamePathService pathService)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        var data = dataService.Data;
        var lang = data.Lang;
        var resolver = data.ResolverFacade;
        var artifactsIndex = data.ArtifactsIndex;
        var itemSetsIndex = data.ItemSetsIndex;
        var locale = pathService.CurrentLocale;

        var artifact = artifactsIndex.Artifacts.Values.FirstOrDefault(a =>
            a.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
            HasArtifactLocalization(a, lang));

        if (artifact is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Artifact '{id}' not found",
                "Check the artifact ID and try again. Use GET /api/artifacts to list available artifacts."
            ));
        }

        var dto = MapToDetail(artifact, lang, resolver, locale, artifactsIndex, itemSetsIndex);
        return Results.Ok(dto);
    }

    private static ArtifactDetailDto MapToDetail(
        ArtifactsIndex.ArtifactRecord artifact,
        Localization.Indexing.LangIndex lang,
        ITextResolver resolver,
        string locale,
        ArtifactsIndex artifactsIndex,
        ItemSetsIndex itemSetsIndex)
    {
        var ctx = new ResolutionContext(locale)
        {
            ItemId = artifact.Id,
            ItemLevel = 1
        };

        var localizedName = lang.ResolveText(artifact.NameSid);
        var description = TryResolveText(resolver, artifact.DescSid, ctx) ?? lang.ResolveText(artifact.DescSid);
        var raritySlotText = GetRaritySlotText(lang, artifact.Rarity, artifact.Slot);

        string? narrativeDesc = null;
        if (!string.IsNullOrWhiteSpace(artifact.NarrativeDescSid))
            narrativeDesc = TryResolveText(resolver, artifact.NarrativeDescSid, ctx) ?? lang.ResolveText(artifact.NarrativeDescSid);

        string? upgradeDesc = null;
        if (!string.IsNullOrWhiteSpace(artifact.UpgradeDescSid))
            upgradeDesc = TryResolveText(resolver, artifact.UpgradeDescSid, ctx) ?? lang.ResolveText(artifact.UpgradeDescSid);

        var (upgradeCost, upgradeCostNote) = BuildUpgradeCost(artifact, lang, locale);

        ArtifactSetBonusDto? setBonus = null;
        if (!string.IsNullOrWhiteSpace(artifact.ItemSetId))
        {
            setBonus = BuildSetBonus(artifact.ItemSetId, locale, lang, resolver, artifactsIndex, itemSetsIndex);
        }

        var slotIcon = MapSlotToIcon(artifact.Slot);

        return new ArtifactDetailDto(
            Id: artifact.Id,
            Name: artifact.NameSid,
            LocalizedName: localizedName,
            Icon: string.IsNullOrEmpty(artifact.Icon) ? null : Artifact(artifact.Icon),
            Rarity: string.IsNullOrEmpty(artifact.Rarity) ? null : artifact.Rarity,
            Slot: string.IsNullOrEmpty(artifact.Slot) ? null : artifact.Slot,
            SlotIcon: slotIcon,
            RaritySlotText: raritySlotText,
            Description: description,
            NarrativeDescription: narrativeDesc,
            UpgradeDescription: upgradeDesc,
            UpgradeCost: upgradeCost,
            UpgradeCostNote: upgradeCostNote,
            DestroyReward: BuildDestroyReward(artifact, lang),
            SetBonus: setBonus
        );
    }

    private static ArtifactSetBonusDto? BuildSetBonus(
        string itemSetId,
        string locale,
        Localization.Indexing.LangIndex lang,
        ITextResolver resolver,
        ArtifactsIndex artifactsIndex,
        ItemSetsIndex itemSetsIndex)
    {
        if (!itemSetsIndex.ItemSets.TryGetValue(itemSetId, out var itemSet))
            return null;

        var setName = lang.ResolveText(itemSet.NameSid) ?? itemSet.Id;

        var setBonusCtx = new ResolutionContext(locale)
        {
            ItemSetId = itemSet.Id,
            ItemLevel = 1
        };

        var setBonusTemplate = lang.ResolveText("tooltipItemEffectsSet") ?? "When having {0} items:";

        var bonuses = new List<SetBonusEntryDto>();
        foreach (var bonus in itemSet.Bonuses)
        {
            var bonusDesc = TryResolveText(resolver, bonus.DescSid, setBonusCtx) ?? lang.ResolveText(bonus.DescSid) ?? "";
            var header = string.Format(setBonusTemplate, bonus.RequiredItems);

            bonuses.Add(new SetBonusEntryDto(
                Header: header,
                Effect: bonusDesc
            ));
        }

        var setItems = new List<SetItemEntryDto>();
        foreach (var itemId in itemSet.ItemIds)
        {
            if (artifactsIndex.Artifacts.TryGetValue(itemId, out var artifact))
            {
                var itemName = lang.ResolveText(artifact.NameSid) ?? itemId;
                var itemSlotIcon = MapSlotToIcon(artifact.Slot);

                setItems.Add(new SetItemEntryDto(
                    ArtifactId: itemId,
                    Name: itemName,
                    Icon: string.IsNullOrEmpty(artifact.Icon) ? null : Artifact(artifact.Icon),
                    Slot: artifact.Slot
                ));
            }
        }

        return new ArtifactSetBonusDto(
            SetName: setName,
            Bonuses: bonuses,
            SetItems: setItems
        );
    }

    private static (string? UpgradeCost, string? UpgradeCostNote) BuildUpgradeCost(
        ArtifactsIndex.ArtifactRecord artifact,
        Localization.Indexing.LangIndex lang,
        string locale)
    {
        // - MaxLevel <= 1: Don't show (not upgradeable)
        // - MaxLevel == 999: Show cost + note "Each subsequent upgrade costs additional resources" (unlimited)
        // - MaxLevel 2-998: Show cost only (limited, single upgrade)

        if (artifact.MaxLevel <= 1)
            return (null, null);

        var costLabel = OverlayService.Instance.TryResolveFromOverlay("label_upgrade_cost", locale)
                     ?? lang.ResolveText("label_upgrade_cost")
                     ?? "Upgrade Cost: {0}";

        // Number only - dust icon is displayed in frontend
        var firstUpgradeCost = artifact.CostBase + artifact.CostPerLevel;
        var upgradeCost = string.Format(costLabel, firstUpgradeCost);

        string? costNote = null;
        if (artifact.MaxLevel == 999)
        {
            // Unlimited upgrades: add note about subsequent costs (from game localization)
            costNote = lang.ResolveText("tooltipMagicAfter");
        }

        return (upgradeCost, costNote);
    }

    private static string? BuildDestroyReward(
        ArtifactsIndex.ArtifactRecord artifact,
        Localization.Indexing.LangIndex lang)
    {
        if (artifact.RewardForDestroy <= 0)
            return null;

        var label = lang.ResolveText("hero_window_ui_delete_item") ?? "Destroy Artifact";
        return $"{label}: {artifact.RewardForDestroy}";
    }

    private static string NormalizeSlotForSid(string slot)
    {
        return slot.ToLowerInvariant().Replace(" ", "_") switch
        {
            "armour" or "armor" => "ARMOR",
            "back" => "BACK",
            "belt" => "BELT",
            "boots" => "BOOTS",
            "head" => "HEAD",
            "left_hand" or "main_hand" => "LEFT_HAND",
            "right_hand" or "off_hand" => "RIGHT_HAND",
            "ring" => "RING",
            "unique_slot" or "unic_slot" => "UNIQUE_SLOT",  // "unic_slot" is a typo in game data
            _ => slot.ToUpperInvariant().Replace(" ", "_")
        };
    }

    private static string MapSlotToIcon(string slot)
    {
        var normalized = slot.ToLowerInvariant().Replace(" ", "_");
        var iconName = normalized switch
        {
            "armour" or "armor" => "ARMOR",
            "back" or "cape" or "cloak" or "shoulder" or "shoulders" => "BACK",
            "belt" or "waist" => "BELT",
            "boots" or "feet" or "foot" => "BOOTS",
            "head" or "helm" or "helmet" => "HEAD",
            "left_hand" or "main_hand" or "weapon" or "mainhand" => "LEFT_HAND",
            "right_hand" or "off_hand" or "shield" or "offhand" => "RIGHT_HAND",
            "ring" or "finger" => "RING",
            "unique_slot" or "unic_slot" or "trinket" or "relic" or "accessory" => "UNIQUE_SLOT",
            "item" or "item_slot" or "misc" or "consumable" => "ITEM_SLOT",
            _ => "ITEM_SLOT"
        };
        return $"icons/item_slots/{iconName}";
    }

    private static bool HasArtifactLocalization(
        ArtifactsIndex.ArtifactRecord artifact,
        Localization.Indexing.LangIndex lang)
    {
        var nameInLang = !string.IsNullOrWhiteSpace(artifact.NameSid)
            ? lang.ResolveText(artifact.NameSid)
            : null;
        var descInLang = !string.IsNullOrWhiteSpace(artifact.DescSid)
            ? lang.ResolveText(artifact.DescSid)
            : null;

        return nameInLang != null || descInLang != null;
    }

    private static string? GetRaritySlotText(
        Localization.Indexing.LangIndex lang,
        string? rarity,
        string? slot)
    {
        if (string.IsNullOrWhiteSpace(rarity) || string.IsNullOrWhiteSpace(slot))
            return null;

        var rarityLower = rarity.ToLowerInvariant();
        var slotNormalized = NormalizeSlotForSid(slot);
        var combinedSid = $"artifactRarity_{rarityLower}_{slotNormalized}";
        var raritySlotText = lang.ResolveText(combinedSid);

        // Future-proof fallback: try British spelling if American spelling not found
        if (raritySlotText == null && slotNormalized == "ARMOR")
        {
            var combinedSidBritish = $"artifactRarity_{rarityLower}_ARMOUR";
            raritySlotText = lang.ResolveText(combinedSidBritish);
        }

        return raritySlotText ?? $"{rarity} {slot}";
    }
}
