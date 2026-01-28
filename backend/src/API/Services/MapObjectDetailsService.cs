using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using GameData.Indexing;
using GameData.Services;
using Localization.Indexing;
using Localization.Resolution;
using API.Contracts;
using static API.Helpers.LocalizationHelper;
using static API.Helpers.IconPaths;
using static API.Helpers.StringHelpers;

namespace API.Services;

public class MapObjectDetailsService
{
    private readonly ILogger<MapObjectDetailsService> _logger;

    public MapObjectDetailsService(ILogger<MapObjectDetailsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MapObjectDetailDto GetDetails(
        MapObjectsIndex.MapObjectRecord mapObject,
        ITextResolver resolver,
        LangIndex lang,
        string locale,
        string? streamingAssetsPath,
        List<DbIndex.UnitRecord>? units,
        ArtifactsIndex? artifactsIndex,
        SpellsIndex? spellsIndex,
        DifficultiesIndex? difficultiesIndex)
    {
        if (mapObject == null) throw new ArgumentNullException(nameof(mapObject));
        if (resolver == null) throw new ArgumentNullException(nameof(resolver));
        if (lang == null) throw new ArgumentNullException(nameof(lang));
        if (string.IsNullOrWhiteSpace(locale)) throw new ArgumentException("Locale cannot be empty", nameof(locale));

        var localizedName = GetLocalizedText(resolver, lang, mapObject.NameSid, mapObject.Id, "name", locale);
        var description = GetLocalizedText(resolver, lang, mapObject.DescriptionSid, mapObject.Id, "description", locale);
        var narrativeDescription = GetLocalizedText(resolver, lang, mapObject.NarrativeDescriptionSid, mapObject.Id, "narrativeDescription", locale);
        var icon = MapObjectIcon(mapObject.PrefabPath);

        CreatureBankInfoDto? creatureBankInfo = null;
        if (mapObject.BankData != null)
        {
            creatureBankInfo = BuildCreatureBankInfo(
                mapObject,
                resolver,
                lang,
                streamingAssetsPath ?? "",
                units,
                locale,
                difficultiesIndex,
                artifactsIndex,
                spellsIndex
            );
        }

        return new MapObjectDetailDto(
            Id: mapObject.Id,
            Name: localizedName ?? mapObject.Id,
            Description: description,
            NarrativeDescription: narrativeDescription,
            Icon: icon,
            CreatureBankInfo: creatureBankInfo
        );
    }

    private CreatureBankInfoDto BuildCreatureBankInfo(
        MapObjectsIndex.MapObjectRecord mapObject,
        ITextResolver resolver,
        LangIndex lang,
        string streamingAssetsPath,
        List<DbIndex.UnitRecord>? units,
        string locale,
        DifficultiesIndex? difficultiesIndex,
        ArtifactsIndex? artifactsIndex,
        SpellsIndex? spellsIndex)
    {
        var bankData = mapObject.BankData!;
        var sourceFolder = bankData.SourceFolder;
        var isBarracksOrOutpost = sourceFolder == "barracks" || sourceFolder == "outposts";

        var hasGuards = bankData.Variants.Any(v => v.GuardUnits.Count > 0);

        var totalRollChance = bankData.Variants.Sum(v => v.RollChance);
        var variantDtos = new List<CreatureBankVariantInfoDto>();

        foreach (var variant in bankData.Variants)
        {
            var guardDtos = variant.GuardUnits.Select(guard =>
                UnitDtoBuilder.BuildGuardInfo(guard.Sid, guard.Amount, units, resolver, locale, bankData.ApplyDifficultyModifier)
            ).ToList();

            var rewardApplyType = string.IsNullOrEmpty(variant.RewardSet.RewardSetApplyType)
                ? null
                : variant.RewardSet.RewardSetApplyType;

            var relativeChance = totalRollChance > 0
                ? Math.Round((double)variant.RollChance / totalRollChance * 100, 1)
                : 100.0;

            CategorizedRewardsDto categorizedRewards;
            List<CategorizedRewardsDto>? rewardOptions = null;

            // For "OnlySelected" type, each reward is a separate option to choose from
            if (rewardApplyType == "OnlySelected" && variant.RewardSet.Rewards.Count > 1)
            {
                rewardOptions = variant.RewardSet.Rewards.Select(reward =>
                    BuildCategorizedRewards(
                        new List<Reward> { reward },
                        resolver,
                        artifactsIndex,
                        spellsIndex,
                        units,
                        locale,
                        lang
                    )
                ).ToList();

                // Empty rewards since they're in options
                categorizedRewards = new CategorizedRewardsDto(
                    new List<ResourceRewardEntryDto>(),
                    new List<ArtifactRarityPoolDto>(),
                    new List<SpellPoolOptionDto>(),
                    new List<GuardUnitInfoDto>(),
                    null
                );
            }
            else
            {
                categorizedRewards = BuildCategorizedRewards(
                    variant.RewardSet.Rewards,
                    resolver,
                    artifactsIndex,
                    spellsIndex,
                    units,
                    locale,
                    lang
                );
            }

            variantDtos.Add(new CreatureBankVariantInfoDto(
                relativeChance,
                variant.Value,
                variant.CustomGuardValue,
                guardDtos,
                categorizedRewards,
                rewardApplyType,
                rewardOptions
            ));
        }

        List<DifficultyLevelDto>? difficultyLevels = null;
        string? difficultyLabel = null;
        if (bankData.ApplyDifficultyModifier && difficultiesIndex != null)
        {
            difficultyLevels = difficultiesIndex.GuardDifficulties
                .Select(d =>
                {
                    var tooltipSid = $"difficulty_{d.Name.ToLowerInvariant()}";
                    var tooltip = TryResolveText(resolver, tooltipSid, locale);
                    return new DifficultyLevelDto(d.Name, d.Power, d.Icon, tooltip);
                })
                .ToList();
            difficultyLabel = TryResolveText(resolver, "difficulties", locale) ?? "Difficulty";
        }

        var guardsLabel = LoadGuardsLabel(resolver, locale);

        return new CreatureBankInfoDto(
            hasGuards,
            bankData.VisitType,
            variantDtos,
            IsBarracks: isBarracksOrOutpost,
            DifficultyLevels: difficultyLevels,
            DifficultyLabel: difficultyLabel,
            GuardsLabel: guardsLabel,
            BankType: mapObject.Tag
        );
    }

    private string LoadGuardsLabel(ITextResolver resolver, string locale)
    {
        return (TryResolveText(resolver, "windowGuardName", locale) ?? "Guards").TrimEnd('!');
    }

    private CategorizedRewardsDto BuildCategorizedRewards(
        IEnumerable<Reward> rewards,
        ITextResolver resolver,
        ArtifactsIndex? artifactsIndex,
        SpellsIndex? spellsIndex,
        List<DbIndex.UnitRecord>? units,
        string locale,
        LangIndex lang)
    {
        var resources = new List<ResourceRewardEntryDto>();
        var artifactPools = new List<ArtifactRarityPoolDto>();
        var spellPools = new List<SpellPoolOptionDto>();
        var unitRewards = new List<GuardUnitInfoDto>();
        int? experience = null;

        foreach (var reward in rewards)
        {
            var parameters = reward.Parameters;

            switch (reward.RewardType)
            {
                case "SideResReward":
                    for (int i = 0; i < parameters.Count; i += 2)
                    {
                        if (i + 1 < parameters.Count && int.TryParse(parameters[i + 1], out var amount))
                        {
                            var resourceKey = parameters[i];
                            var displayName = TryResolveText(resolver, $"{resourceKey}_name", locale)
                                ?? CapitalizeFirst(resourceKey);
                            resources.Add(new ResourceRewardEntryDto(resourceKey, displayName, amount));
                        }
                    }
                    break;

                case "HeroExpReward":
                    if (parameters.Count > 0 && int.TryParse(parameters[0], out var exp))
                    {
                        experience = (experience ?? 0) + exp;
                    }
                    break;

                case "HeroUnitsReward":
                    var unitReward = UnitDtoBuilder.BuildUnitReward(parameters, units, resolver, locale);
                    if (unitReward != null)
                    {
                        unitRewards.Add(unitReward);
                    }
                    break;

                case "HeroRandomItemsReward":
                    if (artifactsIndex != null)
                    {
                        var pools = ArtifactDtoBuilder.BuildPoolByRarities(parameters, artifactsIndex, lang, locale);
                        if (pools != null)
                        {
                            artifactPools.AddRange(pools);
                        }
                    }
                    break;

                case "HeroMagicRandomAdditionReward":
                    if (spellsIndex != null)
                    {
                        var groups = SpellDtoBuilder.BuildPoolBySchoolAndTiers(parameters, spellsIndex, resolver, locale);
                        if (groups != null && groups.Count > 0)
                        {
                            spellPools.Add(new SpellPoolOptionDto(groups));
                        }
                    }
                    break;
            }
        }

        return new CategorizedRewardsDto(resources, artifactPools, spellPools, unitRewards, experience);
    }
}
