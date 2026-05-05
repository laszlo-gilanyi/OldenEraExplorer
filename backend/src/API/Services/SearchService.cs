using System.Text.RegularExpressions;
using GameData.Indexing;
using GameData.Loading;
using Localization.Indexing;
using Localization.Resolution;
using Localization.Services;
using API.Contracts;
using API.Utilities;

namespace API.Services;

public sealed class SearchService
{
    private const int MinQueryLength = 2;
    private const string PlaceholderSearchKeyword = "_unresolved";

    /// <summary>
    /// Detects unresolved placeholders by checking for &lt;unresolved&gt; tags.
    /// This works with the current resolution annotation system where unresolved values
    /// are wrapped in &lt;unresolved&gt;...&lt;/unresolved&gt; tags (rendered in dark red).
    /// </summary>
    private static readonly Regex UnresolvedMarkerRx = new(
        @"<unresolved>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ResolutionTagRx = new(
        @"</?(?:resolved|unresolved)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IGameDataService _dataService;

    public SearchService(IGameDataService dataService)
    {
        _dataService = dataService;
    }

    public SearchServiceResult Search(string? query, string locale)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < MinQueryLength)
        {
            return SearchServiceResult.Error($"Search query must be at least {MinQueryLength} characters.");
        }

        if (!_dataService.IsLoaded || _dataService.Data is null)
        {
            return SearchServiceResult.NotReady("Game data not loaded. Please load game data first using POST /api/game/load");
        }

        var data = _dataService.Data;
        var resolver = data.ResolverFacade;
        var lang = data.Lang;
        var normalizedQuery = query.Trim();
        var lowerQuery = normalizedQuery.ToLowerInvariant();

        // Single method handles both text search and "_unresolved" keyword (architectural: avoid duplicating entity iteration)
        bool isUnresolvedSearch = lowerQuery == PlaceholderSearchKeyword;

        return SearchAllEntities(data, resolver, lang, normalizedQuery, lowerQuery, locale, isUnresolvedSearch);
    }

    private SearchServiceResult SearchAllEntities(
        GameDataLoadResult data,
        ITextResolver resolver,
        LangIndex lang,
        string query,
        string lowerQuery,
        string locale,
        bool isUnresolvedSearch)
    {
        var results = new List<(SearchResultDto Result, int Relevance)>();

        foreach (var unit in data.Units)
        {
            var name = ResolveText(resolver, $"{unit.Id}_name", locale) ?? unit.Id;

            var narrativeDesc = ResolveText(resolver, $"{unit.Id}_narrative", locale)
                ?? ResolveText(resolver, $"{unit.Id}_narrativeDescription", locale);

            string? factionDisplay = null;
            if (!string.IsNullOrWhiteSpace(unit.Fraction))
            {
                var result = lang.ResolveText($"{unit.Fraction}_name");
                if (!string.IsNullOrWhiteSpace(result) && result != $"{unit.Fraction}_name")
                {
                    factionDisplay = result;
                }
                else
                {
                    result = lang.ResolveText(unit.Fraction);
                    if (!string.IsNullOrWhiteSpace(result) && result != unit.Fraction)
                    {
                        factionDisplay = result;
                    }
                    else
                    {
                        factionDisplay = char.ToUpper(unit.Fraction[0]) + unit.Fraction[1..];
                    }
                }
            }

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery,
                name,
                unit.Id,
                sidebarFields: new[] { unit.Fraction, factionDisplay },
                detailFields: new[] { narrativeDesc }
            );

            if (matches)
            {
                results.Add((new SearchResultDto(
                    unit.Id,
                    "Unit",
                    name,
                    matchedField,
                    $"icons/units/hex_portraits/{unit.Id}",
                    matchLocation
                ), relevance));
            }
        }

        foreach (var hero in data.HeroesIndex.Heroes.Values)
        {
            if (hero.IsTutorialOrCampaignHero) continue;

            var name = ResolveText(resolver, $"{hero.HeroId}_name", locale)
                ?? ResolveText(resolver, hero.HeroId, locale)
                ?? hero.HeroId;

            var factionDisplay = !string.IsNullOrEmpty(hero.Fraction)
                ? ResolveText(resolver, $"{hero.Fraction}_name", locale)
                : null;

            var classDisplay = !string.IsNullOrEmpty(hero.ClassType) && !string.IsNullOrEmpty(hero.Fraction)
                ? ResolveText(resolver, $"{hero.ClassType}_{hero.Fraction}_name", locale)
                : null;

            var heroCtx = CreateHeroResolutionContext(locale, hero.SpecializationSid);
            var specName = ResolveText(resolver, $"{hero.HeroId}_spec_name", locale);
            var specDescSid = data.HeroSpecializationsIndex.Specializations.TryGetValue(hero.SpecializationSid ?? "", out var heroSpecRecord)
                && !string.IsNullOrWhiteSpace(heroSpecRecord.DescSid)
                ? heroSpecRecord.DescSid
                : $"{hero.HeroId}_spec_description";
            var specDesc = ResolveTextWithContext(resolver, specDescSid, heroCtx);
            var description = ResolveTextWithContext(resolver, $"{hero.HeroId}_description", heroCtx);
            var motto = ResolveTextWithContext(resolver, $"{hero.HeroId}_motto", heroCtx);

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery,
                name,
                hero.HeroId,
                sidebarFields: new[] { hero.Fraction, factionDisplay, hero.ClassType, classDisplay },
                detailFields: new[] { specName, specDesc, description, motto }
            );

            if (matches)
            {
                results.Add((new SearchResultDto(
                    hero.HeroId,
                    "Hero",
                    name,
                    matchedField,
                    string.IsNullOrEmpty(hero.Icon) ? null : $"icons/hero_large_portraits/{hero.Icon}",
                    matchLocation
                ), relevance));
            }
        }

        foreach (var skill in data.SkillsIndex.Skills.Values)
        {
            if (skill.IsPseudoSkill) continue;
            if (skill.SkillId.StartsWith("campaign_", StringComparison.OrdinalIgnoreCase)) continue;

            var name = ResolveText(resolver, skill.NameSid, locale) ?? skill.SkillId;

            var levelTexts = new List<string?>();
            for (int levelIndex = 0; levelIndex < skill.LevelParams.Count; levelIndex++)
            {
                var levelParam = skill.LevelParams[levelIndex];
                var skillLevel = levelIndex + 1;
                var skillCtx = new ResolutionContext(locale) { SkillId = skill.SkillId, SkillLevel = skillLevel };

                if (!string.IsNullOrEmpty(levelParam.NameSid))
                {
                    var levelName = ResolveTextWithContext(resolver, levelParam.NameSid, skillCtx);
                    if (levelName != null) levelTexts.Add(levelName);
                }

                if (!string.IsNullOrEmpty(levelParam.DescSid))
                {
                    var levelDesc = ResolveTextWithContext(resolver, levelParam.DescSid, skillCtx);
                    if (levelDesc != null) levelTexts.Add(levelDesc);
                }
            }

            var subSkillTexts = new List<string?>();
            foreach (var subSkillId in skill.AllSubSkills)
            {
                if (data.SkillsIndex.SubSkills.TryGetValue(subSkillId, out var subSkill))
                {
                    var subSkillCtx = new ResolutionContext(locale)
                    {
                        SkillId = skill.SkillId,
                        SubSkillId = subSkillId
                    };

                    var subName = ResolveTextWithContext(resolver, subSkill.NameSid, subSkillCtx);
                    var subDesc = ResolveTextWithContext(resolver, subSkill.DescSid, subSkillCtx);
                    if (subName != null) subSkillTexts.Add(subName);
                    if (subDesc != null) subSkillTexts.Add(subDesc);
                }
            }

            var secondaryFields = levelTexts.Concat(subSkillTexts).ToArray();

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery,
                name,
                skill.SkillId,
                sidebarFields: null,
                detailFields: secondaryFields
            );

            if (matches)
            {
                var icon = skill.LevelParams.Count > 0 && !string.IsNullOrEmpty(skill.LevelParams[0].Icon)
                    ? $"icons/hero_skills/{skill.LevelParams[0].Icon}"
                    : null;
                results.Add((new SearchResultDto(
                    skill.SkillId,
                    "Skill",
                    name,
                    matchedField,
                    icon,
                    matchLocation
                ), relevance));
            }
        }

        foreach (var spell in data.SpellsIndex.Spells.Values)
        {
            var name = ResolveText(resolver, spell.NameSid, locale) ?? spell.Id;
            var searchableTexts = new List<string?>();

            bool isBonusSpell = !spell.IsSpecialMagic
                && !(spell.UsedOnMap && spell.SettingPerLevelsCount > 1)
                && !(spell.HasBattleMagic && spell.DealersPerLevelsCount > 1);

            if (data.DbAccessor.TryGetMagic(spell.Id, out var spellJson))
            {
                var descSids = new List<string>();
                if (spellJson.TryGetProperty("description", out var descArr) &&
                    descArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in descArr.EnumerateArray())
                    {
                        descSids.Add(item.GetString() ?? "");
                    }
                }

                int levelCount = isBonusSpell ? 1 : Math.Min(4, Math.Max(descSids.Count, 1));

                for (int level = 1; level <= levelCount; level++)
                {
                    string descSid = descSids.Count >= level ? descSids[level - 1] :
                                     descSids.Count > 0 ? descSids[^1] : "";

                    if (!string.IsNullOrEmpty(descSid))
                    {
                        var ctx = new ResolutionContext(locale)
                        {
                            MagicId = spell.Id,
                            MagicLevel = level,
                            BuffSpellPower = 0
                        };
                        var resolved = resolver.Resolve(descSid, ctx, out _);
                        if (!string.IsNullOrEmpty(resolved))
                            searchableTexts.Add(resolved);
                    }
                }

                if (!isBonusSpell && spellJson.TryGetProperty("bonusDescriptions", out var bonusArr) &&
                    bonusArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var bonus in bonusArr.EnumerateArray())
                    {
                        if (bonus.TryGetProperty("level", out var lvl) &&
                            bonus.TryGetProperty("description", out var desc))
                        {
                            int bonusLevel = lvl.GetInt32();
                            if (bonusLevel >= 1 && bonusLevel <= levelCount)
                            {
                                var bonusSid = desc.GetString() ?? "";
                                if (!string.IsNullOrEmpty(bonusSid))
                                {
                                    var ctx = new ResolutionContext(locale)
                                    {
                                        MagicId = spell.Id,
                                        MagicLevel = bonusLevel,
                                        BuffSpellPower = 0
                                    };
                                    var resolved = resolver.Resolve(bonusSid, ctx, out _);
                                    if (!string.IsNullOrEmpty(resolved))
                                        searchableTexts.Add(resolved);
                                }
                            }
                        }
                    }
                }
            }

            var schoolDisplay = !string.IsNullOrEmpty(spell.School)
                ? ResolveText(resolver, $"{spell.School}_name", locale)
                : null;

            string? schoolTierText = null;
            if (!string.IsNullOrEmpty(spell.School) && spell.Rank > 0)
            {
                string schoolCapitalized = char.ToUpper(spell.School[0]) + spell.School.Substring(1);
                string schoolTierSid = $"{schoolCapitalized}_magic_{spell.Rank}_tier";
                schoolTierText = lang.ResolveText(schoolTierSid);
            }

            var category = DetermineSpellCategory(spell, lang, locale);

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery,
                name,
                spell.Id,
                sidebarFields: new[] { schoolDisplay, schoolTierText, category, spell.School },
                detailFields: searchableTexts.ToArray()
            );

            if (matches)
            {
                results.Add((new SearchResultDto(
                    spell.Id,
                    "Spell",
                    name,
                    matchedField,
                    string.IsNullOrEmpty(spell.Icon) ? null : $"icons/hero_magics/{spell.Icon}",
                    matchLocation
                ), relevance));
            }
        }

        foreach (var artifact in data.ArtifactsIndex.Artifacts.Values)
        {
            var artifactCtx = new ResolutionContext(locale) { ItemId = artifact.Id, ItemLevel = 1 };

            var name = ResolveTextWithContext(resolver, artifact.NameSid, artifactCtx);
            var description = !string.IsNullOrEmpty(artifact.DescSid)
                ? ResolveTextWithContext(resolver, artifact.DescSid, artifactCtx)
                : null;

            // Skip artifacts without localized text (consistency with ArtifactsEndpoints filtering)
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(description))
                continue;

            name = name ?? artifact.Id;

            var narrativeDesc = !string.IsNullOrEmpty(artifact.NarrativeDescSid)
                ? ResolveTextWithContext(resolver, artifact.NarrativeDescSid, artifactCtx)
                : null;

            var upgradeDesc = !string.IsNullOrEmpty(artifact.UpgradeDescSid)
                ? ResolveTextWithContext(resolver, artifact.UpgradeDescSid, artifactCtx)
                : null;

            var searchableTexts = new List<string?> { description, narrativeDesc, upgradeDesc };

            string? setName = null;
            if (!string.IsNullOrEmpty(artifact.ItemSetId) &&
                data.ItemSetsIndex.ItemSets.TryGetValue(artifact.ItemSetId, out var itemSet))
            {
                var setCtx = new ResolutionContext(locale) { ItemSetId = itemSet.Id, ItemLevel = 1 };
                setName = ResolveTextWithContext(resolver, itemSet.NameSid, setCtx);

                foreach (var bonus in itemSet.Bonuses)
                {
                    var bonusEffect = ResolveTextWithContext(resolver, bonus.DescSid, setCtx);
                    if (!string.IsNullOrEmpty(bonusEffect))
                        searchableTexts.Add(bonusEffect);
                }
            }

            var raritySlotText = GetArtifactRaritySlotText(lang, artifact.Rarity, artifact.Slot);

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery,
                name,
                artifact.Id,
                sidebarFields: new[] { artifact.Rarity, artifact.Slot, raritySlotText },
                detailFields: searchableTexts.Concat(new[] { setName }).ToArray()
            );

            if (matches)
            {
                results.Add((new SearchResultDto(
                    artifact.Id,
                    "Artifact",
                    name,
                    matchedField,
                    string.IsNullOrEmpty(artifact.Icon) ? null : $"icons/artifacts/{artifact.Icon}",
                    matchLocation
                ), relevance));
            }
        }

        foreach (var kvp in data.BuildingsIndex.Buildings)
        {
            var key = kvp.Key;
            var building = kvp.Value;

            var buildingCtx = new ResolutionContext(locale) { FractionId = building.Faction };

            var factionDisplay = !string.IsNullOrEmpty(building.Faction)
                ? ResolveText(resolver, $"{building.Faction}_name", locale)
                : null;

            var maxLevel = Math.Max(building.Names.Length, Math.Max(building.Descriptions.Length, 1));
            for (int level = 1; level <= maxLevel; level++)
            {
                var levelIndex = level - 1;

                var nameSid = levelIndex < building.Names.Length ? building.Names[levelIndex] :
                              building.Names.Length > 0 ? building.Names[^1] : building.Sid;
                var name = ResolveTextWithContext(resolver, nameSid, buildingCtx) ?? building.Sid;

                var descSid = levelIndex < building.Descriptions.Length ? building.Descriptions[levelIndex] :
                              building.Descriptions.Length > 0 ? building.Descriptions[^1] : null;
                var description = !string.IsNullOrEmpty(descSid)
                    ? ResolveTextWithContext(resolver, descSid, buildingCtx)
                    : null;

                var searchableTexts = new List<string?> { description };

                if (building.EffectsPerLevel != null && levelIndex < building.EffectsPerLevel.Length)
                {
                    var levelEffects = building.EffectsPerLevel[levelIndex];
                    if (levelEffects != null)
                    {
                        foreach (var effectSid in levelEffects.Where(e => !string.IsNullOrWhiteSpace(e)))
                        {
                            var effectDesc = ResolveTextWithContext(resolver, effectSid, buildingCtx);
                            if (!string.IsNullOrEmpty(effectDesc))
                                searchableTexts.Add(effectDesc);
                        }
                    }
                }

                var levelId = $"{key}_L{level}";

                var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                    isUnresolvedSearch,
                    lowerQuery,
                    name,
                    levelId,
                    sidebarFields: new[] { building.Faction, factionDisplay, building.Sid },
                    detailFields: searchableTexts.ToArray()
                );

                if (matches)
                {
                    string? iconPath = null;
                    if (building.Icons != null && building.Icons.Length > 0)
                    {
                        // Use level-specific icon (level is 1-indexed, array is 0-indexed)
                        var iconLevelIndex = level - 1;
                        var icon = iconLevelIndex >= 0 && iconLevelIndex < building.Icons.Length
                            ? building.Icons[iconLevelIndex]
                            : building.Icons[0]; // Fallback to first icon if level out of range

                        if (!string.IsNullOrWhiteSpace(icon))
                        {
                            // Special case: buildings_wip is a fallback icon at root level
                            if (icon == "buildings_wip")
                            {
                                iconPath = $"icons/cities_buildings/{icon}";
                            }
                            else if (!string.IsNullOrWhiteSpace(building.Faction))
                            {
                                iconPath = $"icons/cities_buildings/{building.Faction.ToLowerInvariant()}/{icon}";
                            }
                            else
                            {
                                iconPath = $"icons/cities_buildings/{icon}";
                            }
                        }
                    }

                    results.Add((new SearchResultDto(
                        levelId,
                        "Building",
                        name,
                        matchedField,
                        iconPath,
                        matchLocation
                    ), relevance));
                }
            }
        }

        foreach (var mapObj in data.MapObjectsIndex.MapObjects.Values)
        {
            if (!mapObj.IsInteractable) continue;
            if (string.Equals(mapObj.Tag, "Artifact", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsAllowedMapObject(mapObj)) continue;

            var name = GetMapObjectName(resolver, lang, mapObj, locale) ?? mapObj.Id;
            var description = GetMapObjectDescription(resolver, lang, mapObj, locale);
            var narrativeDesc = GetMapObjectNarrativeDescription(resolver, lang, mapObj, locale);

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery,
                name,
                mapObj.Id,
                sidebarFields: null,
                detailFields: new[] { description, narrativeDesc }
            );

            if (matches)
            {
                string? iconPath = null;
                if (!string.IsNullOrWhiteSpace(mapObj.PrefabPath))
                {
                    iconPath = mapObj.PrefabPath.Replace('\\', '/');
                    if (!iconPath.StartsWith("objects/", StringComparison.OrdinalIgnoreCase))
                        iconPath = $"objects/{iconPath}";
                }

                results.Add((new SearchResultDto(
                    mapObj.Id,
                    "MapObject",
                    name,
                    matchedField,
                    iconPath,
                    matchLocation
                ), relevance));
            }
        }

        foreach (var law in data.FactionLawIndex.FactionLaws.Values)
        {
            var baseCtx = new ResolutionContext(locale) { LawId = law.Id };
            var name = ResolveTextWithContext(resolver, law.NameSid, baseCtx);
            if (string.IsNullOrEmpty(name)) continue;

            var description = !string.IsNullOrEmpty(law.DescSid)
                ? ResolveTextWithContext(resolver, law.DescSid, baseCtx)
                : null;
            if (string.IsNullOrEmpty(description)) continue;

            var levelDescriptions = new List<string?>();
            for (int i = 0; i < law.ParametersPerLevel.Count; i++)
            {
                var levelNum = i + 1;
                var lawCtx = new ResolutionContext(locale) { LawId = law.Id, LawLevel = levelNum };

                var levelDesc = ResolveTextWithContext(resolver, law.DescSid, lawCtx);
                if (levelDesc != null) levelDescriptions.Add(levelDesc);
            }

            var factionDisplay = !string.IsNullOrEmpty(law.Faction)
                ? ResolveText(resolver, $"{law.Faction}_name", locale)
                : null;

            var detailFields = new List<string?> { description };
            detailFields.AddRange(levelDescriptions);

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery,
                name,
                law.Id,
                sidebarFields: new[] { law.Faction, factionDisplay },
                detailFields: detailFields.ToArray()
            );

            if (matches)
            {
                results.Add((new SearchResultDto(
                    law.Id,
                    "FactionLaw",
                    name,
                    matchedField,
                    !string.IsNullOrEmpty(law.Icon) ? $"icons/fraction_laws/{law.Icon}" : null,
                    matchLocation
                ), relevance));
            }
        }

        // Use pre-aggregated abilities with pre-computed variant IDs
        var aggregates = data.AggregatedAbilities;

        foreach (var aggregate in aggregates)
        {
            var nameSid = aggregate.Key.NameSid;
            var name = ResolveText(resolver, nameSid, locale) ?? nameSid;
            var description = aggregate.Key.ResolvedDescription;

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery, name, aggregate.VariantId,
                sidebarFields: null,
                detailFields: new[] { description, aggregate.Key.Type });

            if (matches)
            {
                results.Add((new SearchResultDto(
                    aggregate.VariantId, "Ability", name, matchedField,
                    $"icons/abilities/{nameSid}",
                    matchLocation
                ), relevance));
            }
        }

        foreach (var subclass in data.SubclassesIndex.Subclasses.Values)
        {
            var name = ResolveText(resolver, subclass.NameSid, locale);
            var description = !string.IsNullOrEmpty(subclass.DescSid)
                ? ResolveText(resolver, subclass.DescSid, locale)
                : null;

            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(description))
                continue;

            name = name ?? subclass.Id;

            var factionDisplay = !string.IsNullOrEmpty(subclass.Faction)
                ? ResolveText(resolver, $"{subclass.Faction}_name", locale)
                : null;
            var classDisplay = !string.IsNullOrEmpty(subclass.ClassType) && !string.IsNullOrEmpty(subclass.Faction)
                ? ResolveText(resolver, $"{subclass.ClassType}_{subclass.Faction}_name", locale)
                : null;

            var (matches, relevance, matchedField, matchLocation) = CheckEntityMatch(
                isUnresolvedSearch,
                lowerQuery,
                name,
                subclass.Id,
                sidebarFields: new[] { subclass.Faction, factionDisplay, subclass.ClassType, classDisplay },
                detailFields: new[] { description }
            );

            if (matches)
            {
                results.Add((new SearchResultDto(
                    subclass.Id,
                    "Subclass",
                    name,
                    matchedField,
                    string.IsNullOrEmpty(subclass.Icon) ? null : $"icons/hero_sub_classes/{subclass.Icon}",
                    matchLocation
                ), relevance));
            }
        }

        var sortedResults = results
            .OrderBy(r => r.Result.Type)
            .ThenBy(r => r.Result.Id, new NaturalStringComparer())
            .Select(r => r.Result)
            .ToList();

        return SearchServiceResult.Success(query, sortedResults, sortedResults.Count);
    }

    #region Helper Methods

    /// <summary>
    /// Creates a resolution context for ability descriptions.
    /// Uses UnitId context if sourceUnitId is valid (not null, "(standalone)", or "(hero)").
    /// </summary>
    public static ResolutionContext CreateAbilityResolutionContext(string locale, string? sourceUnitId)
    {
        return (!string.IsNullOrWhiteSpace(sourceUnitId)
            && sourceUnitId != "(standalone)"
            && sourceUnitId != "(hero)")
            ? new ResolutionContext(locale) { UnitId = sourceUnitId }
            : new ResolutionContext(locale);
    }

    /// <summary>
    /// Creates a resolution context for hero descriptions.
    /// Uses HeroSpecializationId context if specializationSid is provided.
    /// </summary>
    public static ResolutionContext CreateHeroResolutionContext(string locale, string? specializationSid)
    {
        return new ResolutionContext(locale) { HeroSpecializationId = specializationSid };
    }

    /// <summary>
    /// Determines if an entity matches the search criteria.
    /// For regular search: checks if the query is found in any field.
    /// For unresolved search: checks if any field contains &lt;unresolved&gt; tags.
    /// </summary>
    /// <returns>
    /// (matches, relevance, matchedField, matchLocation) for regular search.
    /// (matches, 0, unresolvedField, "Detail") for unresolved search.
    /// </returns>
    private static (bool Matches, int Relevance, string? MatchedField, string MatchLocation) CheckEntityMatch(
        bool isUnresolvedSearch,
        string lowerQuery,
        string? name,
        string? id,
        string?[]? sidebarFields,
        string?[]? detailFields)
    {
        if (isUnresolvedSearch)
        {
            var allFields = new[] { name, id }
                .Concat(sidebarFields ?? Array.Empty<string?>())
                .Concat(detailFields ?? Array.Empty<string?>());

            foreach (var field in allFields)
            {
                if (!string.IsNullOrWhiteSpace(field) && HasUnresolvedMarkers(field))
                {
                    return (true, 0, field, "Detail");
                }
            }

            return (false, 0, null, "Sidebar");
        }
        else
        {
            var (relevance, matchedField, matchLocation) = CalculateRelevanceMultiple(
                lowerQuery, name, id, sidebarFields, detailFields);

            return (relevance > 0, relevance, matchedField, matchLocation);
        }
    }

    /// <summary>
    /// Calculate search relevance across multiple fields.
    /// Primary fields (name, id) get higher scores; secondary fields (descriptions) get lower scores.
    /// Also returns match location: "Sidebar" for fields visible in left sidebar, "Detail" for fields only in detail panel.
    /// </summary>
    private static (int Relevance, string? MatchedField, string MatchLocation) CalculateRelevanceMultiple(
        string lowerQuery,
        string? name,
        string? id,
        string?[]? sidebarFields = null,
        string?[]? detailFields = null)
    {
        var nameRelevance = CalculateSingleFieldRelevance(name, lowerQuery);
        var idRelevance = CalculateSingleFieldRelevance(id, lowerQuery);

        if (nameRelevance > 0)
            return (nameRelevance, name, "Sidebar");
        if (idRelevance > 0)
            return (idRelevance, id, "Sidebar");

        if (sidebarFields != null)
        {
            foreach (var field in sidebarFields)
            {
                if (string.IsNullOrWhiteSpace(field)) continue;
                var relevance = CalculateSingleFieldRelevance(field, lowerQuery);
                if (relevance > 0)
                {
                    return (relevance, field, "Sidebar");
                }
            }
        }

        // Detail fields deprioritized (capped at 30) because they're not visible in search results list
        if (detailFields != null)
        {
            foreach (var field in detailFields)
            {
                if (string.IsNullOrWhiteSpace(field)) continue;
                var relevance = CalculateSingleFieldRelevance(field, lowerQuery);
                if (relevance > 0)
                {
                    return (Math.Min(relevance, 30), field, "Detail");
                }
            }
        }

        return (0, null, "Sidebar");
    }

    private static int CalculateSingleFieldRelevance(string? field, string lowerQuery)
    {
        if (string.IsNullOrWhiteSpace(field)) return 0;

        var cleanField = StripResolutionTags(field);
        var fieldLower = cleanField.ToLowerInvariant();

        if (fieldLower == lowerQuery) return 100;
        if (fieldLower.StartsWith(lowerQuery)) return 80;
        if (fieldLower.Contains(lowerQuery)) return 50;

        return 0;
    }

    private static string? ResolveText(ITextResolver resolver, string sid, string locale)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        try
        {
            var ctx = new ResolutionContext(locale);
            var result = resolver.Resolve(sid, ctx, out _);

            if (!string.IsNullOrWhiteSpace(result) && result != sid)
                return result;
        }
        catch
        {
        }

        return null;
    }

    private static string? ResolveTextWithContext(ITextResolver resolver, string sid, ResolutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        try
        {
            var result = resolver.Resolve(sid, ctx, out _);

            if (!string.IsNullOrWhiteSpace(result) && result != sid)
                return result;
        }
        catch
        {
        }

        return null;
    }

    private static (string? Text, bool HasUnresolved) ResolveTextForPlaceholderSearch(
        ITextResolver resolver, string sid, ResolutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return (null, false);

        try
        {
            var result = resolver.Resolve(sid, ctx, out _);

            if (!string.IsNullOrWhiteSpace(result) && result != sid)
            {
                var hasUnresolved = HasUnresolvedMarkers(result);
                return (result, hasUnresolved);
            }
        }
        catch
        {
        }

        return (null, false);
    }

    private static bool HasUnresolvedMarkers(string text)
        => UnresolvedMarkerRx.IsMatch(text);

    private static string? ResolveAbilityDescriptionForSearch(
        ITextResolver resolver,
        LangIndex lang,
        string? descriptionSid,
        string? unitId,
        int abilityIndex,
        bool isActiveAbility,
        string locale)
    {
        if (string.IsNullOrWhiteSpace(descriptionSid))
            return null;

        var ctx = (!string.IsNullOrWhiteSpace(unitId))
            ? new ResolutionContext(locale)
            {
                UnitId = unitId,
                AbilityIndex = abilityIndex,
                IsActiveAbility = isActiveAbility
            }
            : new ResolutionContext(locale);

        try
        {
            var result = resolver.Resolve(descriptionSid, ctx, out _);
            if (!string.IsNullOrWhiteSpace(result) && result != descriptionSid)
                return result;
        }
        catch
        {
        }

        return lang.ResolveText(descriptionSid);
    }

    private static bool IsAllowedMapObject(MapObjectsIndex.MapObjectRecord mapObj)
    {
        var prefabPath = mapObj.PrefabPath?.ToLowerInvariant() ?? "";
        var hasAllowedPath = prefabPath.StartsWith("interactive/") ||
                             prefabPath.StartsWith("resource/") ||
                             prefabPath.StartsWith("barracks/");

        if (!hasAllowedPath)
            return false;

        var id = mapObj.Id?.ToLowerInvariant() ?? "";
        if (id.StartsWith("custom_") ||
            id.StartsWith("campaign_") ||
            id.StartsWith("pvp_promo_"))
            return false;

        if (id.EndsWith("_campaign") ||
            id.EndsWith("_old"))
            return false;

        return true;
    }

    private static string? GetMapObjectName(
        ITextResolver resolver,
        LangIndex lang,
        MapObjectsIndex.MapObjectRecord mapObject,
        string locale)
    {
        if (!string.IsNullOrWhiteSpace(mapObject.NameSid))
        {
            var result = ResolveText(resolver, mapObject.NameSid, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(mapObject.NameSid);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        var patterns = new[]
        {
            $"{mapObject.Id}_name",
            $"mapobject.{mapObject.Id}.name",
            $"object.{mapObject.Id}.name"
        };

        foreach (var pattern in patterns)
        {
            var result = ResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        return null;
    }

    private static string? GetMapObjectDescription(
        ITextResolver resolver,
        LangIndex lang,
        MapObjectsIndex.MapObjectRecord mapObject,
        string locale)
    {
        if (!string.IsNullOrWhiteSpace(mapObject.DescriptionSid))
        {
            var result = ResolveText(resolver, mapObject.DescriptionSid, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(mapObject.DescriptionSid);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        var patterns = new[]
        {
            $"{mapObject.Id}_description",
            $"mapobject.{mapObject.Id}.description",
            $"object.{mapObject.Id}.description"
        };

        foreach (var pattern in patterns)
        {
            var result = ResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        return null;
    }

    private static string? GetMapObjectNarrativeDescription(
        ITextResolver resolver,
        LangIndex lang,
        MapObjectsIndex.MapObjectRecord mapObject,
        string locale)
    {
        if (!string.IsNullOrWhiteSpace(mapObject.NarrativeDescriptionSid))
        {
            var result = ResolveText(resolver, mapObject.NarrativeDescriptionSid, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(mapObject.NarrativeDescriptionSid);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        var patterns = new[]
        {
            $"{mapObject.Id}_narrativeDescription",
            $"mapobject.{mapObject.Id}.narrativeDescription",
            $"object.{mapObject.Id}.narrativeDescription"
        };

        foreach (var pattern in patterns)
        {
            var result = ResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result))
                return result;

            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        return null;
    }

    private static string DetermineSpellCategory(SpellsIndex.SpellRecord spell, LangIndex lang, string locale)
    {
        bool isSpecial = spell.IsSpecialMagic;
        bool isTrueWorldSpell = spell.UsedOnMap && spell.SettingPerLevelsCount > 1;
        bool isLeveledBattleSpell = spell.HasBattleMagic && spell.DealersPerLevelsCount > 1;
        bool isSkillRelated = !isSpecial && !isTrueWorldSpell && !isLeveledBattleSpell;

        string combatMagicText = lang.ResolveText("tutorial_M_26_name") ?? "Combat Magic";

        string globalMapMagicText = OverlayService.Instance.TryResolveFromOverlay("spell_category_global_map_magic", locale)
                                 ?? "Global Map Magic";

        if (isSpecial)
        {
            string baseCategory = lang.ResolveText("Ability_type_special") ?? "Special";
            if (spell.HasBattleMagic)
                return $"{baseCategory} {combatMagicText}";
            if (spell.UsedOnMap)
                return $"{baseCategory} {globalMapMagicText}";
            return baseCategory;
        }

        if (isSkillRelated)
        {
            string baseCategory = OverlayService.Instance.TryResolveFromOverlay("spell_category_skill_related", locale)
                               ?? "Skill Related";
            if (spell.HasBattleMagic)
                return $"{baseCategory} {combatMagicText}";
            if (spell.UsedOnMap)
                return $"{baseCategory} {globalMapMagicText}";
            return baseCategory;
        }

        if (spell.UsedOnMap)
            return globalMapMagicText;

        return combatMagicText;
    }

    private static string? GetArtifactRaritySlotText(
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

        if (raritySlotText == null && slotNormalized == "ARMOR")
        {
            var combinedSidBritish = $"artifactRarity_{rarityLower}_ARMOUR";
            raritySlotText = lang.ResolveText(combinedSidBritish);
        }

        return raritySlotText ?? $"{rarity} {slot}";
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
            "unique_slot" or "unic_slot" => "UNIQUE_SLOT",
            _ => slot.ToUpperInvariant().Replace(" ", "_")
        };
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Strips resolution annotation tags (&lt;resolved&gt;, &lt;unresolved&gt;) from text for search matching.
    /// This allows users to search for resolved values without including the markup tags.
    /// Example: "&lt;resolved&gt;250&lt;/resolved&gt; Gold" becomes "250 Gold" for matching.
    /// </summary>
    private static string StripResolutionTags(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return ResolutionTagRx.Replace(text, string.Empty);
    }

    #endregion
}

/// <summary>
/// Result from SearchService.Search operation.
/// </summary>
public sealed class SearchServiceResult
{
    public bool IsSuccess { get; private init; }
    public bool IsNotReady { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? Query { get; private init; }
    public List<SearchResultDto> Results { get; private init; } = new();
    public int TotalResults { get; private init; }

    private SearchServiceResult() { }

    public static SearchServiceResult Success(string query, List<SearchResultDto> results, int totalResults) =>
        new()
        {
            IsSuccess = true,
            Query = query,
            Results = results,
            TotalResults = totalResults
        };

    public static SearchServiceResult Error(string message) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = message
        };

    public static SearchServiceResult NotReady(string message) =>
        new()
        {
            IsSuccess = false,
            IsNotReady = true,
            ErrorMessage = message
        };
}
