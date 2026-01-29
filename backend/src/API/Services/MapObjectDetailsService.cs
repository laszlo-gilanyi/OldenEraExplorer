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
        DifficultiesIndex? difficultiesIndex,
        MapObjectsIndex? mapObjectsIndex)
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
                spellsIndex,
                mapObjectsIndex
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
        SpellsIndex? spellsIndex,
        MapObjectsIndex? mapObjectsIndex)
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

            if (rewardApplyType == "OnlySelected" && variant.RewardSet.Rewards.Count > 1)
            {
                rewardOptions = variant.RewardSet.Rewards.Select(reward =>
                    BuildCategorizedRewards(
                        new List<Reward> { reward },
                        resolver,
                        artifactsIndex,
                        spellsIndex,
                        mapObjectsIndex,
                        units,
                        locale,
                        lang
                    )
                ).ToList();

                categorizedRewards = new CategorizedRewardsDto(
                    new List<ResourceRewardEntryDto>(),
                    new List<ArtifactRarityPoolDto>(),
                    new List<SpellPoolOptionDto>(),
                    new List<GuardUnitInfoDto>(),
                    null,
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
                    mapObjectsIndex,
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
        MapObjectsIndex? mapObjectsIndex,
        List<DbIndex.UnitRecord>? units,
        string locale,
        LangIndex lang)
    {
        var resources = new List<ResourceRewardEntryDto>();
        var artifactPools = new List<ArtifactRarityPoolDto>();
        var spellPools = new List<SpellPoolOptionDto>();
        var unitRewards = new List<GuardUnitInfoDto>();
        var cursePools = new List<CursePoolDto>();
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

                case "SideRandomBuffReward":
                    if (mapObjectsIndex != null)
                    {
                        var cursePool = BuildCursePool(parameters, mapObjectsIndex, resolver, locale);
                        if (cursePool != null)
                        {
                            cursePools.Add(cursePool);
                        }
                    }
                    break;
            }
        }

        return new CategorizedRewardsDto(resources, artifactPools, spellPools, unitRewards, experience, cursePools.Count > 0 ? cursePools : null);
    }

    private CursePoolDto? BuildCursePool(
        List<string> parameters,
        MapObjectsIndex mapObjectsIndex,
        ITextResolver resolver,
        string locale)
    {
        if (parameters.Count < 2) return null;

        var curseInfos = new List<CurseInfoDto>();
        int durationDays = 0;
        string? curseRarity = null;

        var durationTypeIndex = parameters.FindIndex(p => p.Equals("ForSeveralDays", StringComparison.OrdinalIgnoreCase));
        if (durationTypeIndex >= 0 && durationTypeIndex + 1 < parameters.Count)
        {
            if (int.TryParse(parameters[durationTypeIndex + 1], out var days))
            {
                durationDays = days;
            }
        }

        var curseIds = durationTypeIndex > 0 ? parameters.Take(durationTypeIndex).ToList() : parameters;

        foreach (var curseId in curseIds)
        {
            if (mapObjectsIndex.SideBuffs.TryGetValue(curseId, out var buffRecord))
            {
                if (curseRarity == null && curseId.Contains("_debuff_"))
                {
                    var parts = curseId.Split('_');
                    var debuffIndex = Array.IndexOf(parts, "debuff");
                    if (debuffIndex >= 0 && debuffIndex + 1 < parts.Length)
                    {
                        var rarityPart = parts[debuffIndex + 1];
                        if (rarityPart.StartsWith("common")) curseRarity = "common";
                        else if (rarityPart.StartsWith("rare")) curseRarity = "rare";
                        else if (rarityPart.StartsWith("epic")) curseRarity = "epic";
                        else if (rarityPart.StartsWith("legendary")) curseRarity = "legendary";
                        else if (rarityPart.Equals("none", StringComparison.OrdinalIgnoreCase)) curseRarity = "none";
                    }
                }

                var name = TryResolveText(resolver, buffRecord.NameSid, locale);
                var description = TryResolveText(resolver, buffRecord.DescriptionSid, locale);

                var effects = new List<CurseEffectDto>();
                foreach (var bonus in buffRecord.Bonuses)
                {
                    if (bonus.Parameters.Count >= 2)
                    {
                        var stat = bonus.Parameters[0];
                        var modifier = bonus.Parameters[1];

                        var normalizedStat = stat switch
                        {
                            "moral" => "morale",
                            _ => stat
                        };

                        var statDisplayName = TryResolveText(resolver, $"{normalizedStat}_name", locale) ?? CapitalizeFirst(normalizedStat);

                        effects.Add(new CurseEffectDto(stat, statDisplayName, modifier));
                    }
                }

                curseInfos.Add(new CurseInfoDto(
                    buffRecord.Id,
                    name,
                    description,
                    effects
                ));
            }
        }

        if (curseInfos.Count == 0) return null;

        var curseTitleKey = curseRarity != null ? $"{curseRarity}Curse" : "commonCurse";
        var curseTitle = TryResolveText(resolver, curseTitleKey, locale) ?? "Curse";

        return new CursePoolDto(curseTitle, curseInfos, durationDays);
    }
}
