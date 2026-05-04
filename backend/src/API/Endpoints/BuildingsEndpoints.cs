using GameData.Indexing;
using GameData.Services;
using Localization.Resolution;
using Localization.Services;
using API.Contracts;
using API.Helpers;
using API.Services;

namespace API.Endpoints;

public static class BuildingsEndpoints
{
    public static IEndpointRouteBuilder MapBuildingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/buildings")
            .WithTags("Buildings")
            ;

        group.MapGet("/", GetBuildings)
            .WithName("GetBuildings")
            .WithSummary("List all buildings")
            .WithDescription("Returns a list of all buildings. Supports search filtering by ID, name, faction, or category.")
            .Produces<List<BuildingListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/{id}", GetBuildingById)
            .WithName("GetBuildingById")
            .WithSummary("Get building details")
            .WithDescription("Returns detailed information about a specific building including costs, effects, and localized text.")
            .Produces<BuildingDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetBuildings(
        IGameDataService dataService,
        FactionMapper factionMapper,
        string? search = null,
        string? faction = null,
        string? category = null)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        var data = dataService.Data;
        var resolver = data.ResolverFacade;
        var lang = data.Lang;
        var locale = lang.Locale;

        var buildingItems = new List<(string Key, BuildingsIndex.BuildingRecord Record, int Level)>();

        foreach (var kvp in data.BuildingsIndex.Buildings)
        {
            var building = kvp.Value;
            var maxLevel = Math.Max(building.Names.Length, building.Descriptions.Length);
            if (maxLevel == 0) maxLevel = 1;

            for (int level = 1; level <= maxLevel; level++)
            {
                buildingItems.Add((kvp.Key, building, level));
            }
        }

        IEnumerable<(string Key, BuildingsIndex.BuildingRecord Record, int Level)> filtered = buildingItems
            .Where(b =>
            {
                // Filter: Skip building levels without localization (e.g., Magic Guild L5)
                var nameInLang = GetBuildingName(b.Record, b.Level, resolver, lang, locale);
                return nameInLang != null;
            });

        if (!string.IsNullOrWhiteSpace(faction))
        {
            filtered = filtered.Where(b =>
                b.Record.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            filtered = filtered.Where(b =>
                b.Record.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();

            var levelLabel = OverlayService.Instance.TryResolveFromOverlay("detail_level", locale)
                ?? lang.ResolveText("detail_level")
                ?? "Level {0}";

            filtered = filtered.Where(b =>
            {
                if (b.Record.Sid.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (GetBuildingName(b.Record, b.Level, resolver, lang, locale)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                    return true;

                if (b.Level.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                var levelText = string.Format(levelLabel, b.Level);
                if (levelText.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (b.Record.Faction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (factionMapper.MapFactionDisplay(b.Record.Faction)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                    return true;

                if (b.Record.Category.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            });
        }

        var buildingsList = filtered
            .Select(b => MapToListItem(b.Key, b.Record, b.Level, resolver, lang, locale, factionMapper))
            .ToList();

        return Results.Ok(buildingsList);
    }

    private static IResult GetBuildingById(
        string id,
        IGameDataService dataService,
        FactionMapper factionMapper)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        var data = dataService.Data;
        var resolver = data.ResolverFacade;
        var lang = data.Lang;
        var locale = lang.Locale;

        var parts = id.Split('_');
        if (parts.Length < 3 || !parts[^1].StartsWith("L", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound(new ErrorDto(
                $"Invalid building ID format '{id}'",
                "Building ID should be in format 'faction_sid_Llevel' (e.g., 'human_main_L1')"
            ));
        }

        var levelPart = parts[^1];
        if (!int.TryParse(levelPart.Substring(1), out var level) || level < 1)
        {
            return Results.NotFound(new ErrorDto(
                $"Invalid level in building ID '{id}'",
                "Level must be a positive integer (e.g., L1, L2, L3)"
            ));
        }

        var key = string.Join("_", parts.Take(parts.Length - 1));

        if (!data.BuildingsIndex.Buildings.TryGetValue(key, out var building))
        {
            return Results.NotFound(new ErrorDto(
                $"Building '{key}' not found",
                "Check the building ID and try again. Use GET /api/buildings to list available buildings."
            ));
        }

        var maxLevel = Math.Max(building.Names.Length, building.Descriptions.Length);
        if (maxLevel == 0) maxLevel = 1;
        if (level > maxLevel)
        {
            return Results.NotFound(new ErrorDto(
                $"Level {level} not found for building '{key}'",
                $"Building has {maxLevel} level(s). Valid levels are 1-{maxLevel}."
            ));
        }

        var dto = MapToDetail(key, building, level, resolver, lang, locale, data.BuildingsIndex, factionMapper);
        return Results.Ok(dto);
    }

    private static BuildingListItemDto MapToListItem(
        string key,
        BuildingsIndex.BuildingRecord building,
        int level,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale,
        FactionMapper factionMapper)
    {
        var name = GetBuildingName(building, level, resolver, lang, locale) ?? building.Sid;
        var id = $"{key}_L{level}";

        return new BuildingListItemDto(
            Id: id,
            Name: name,
            Faction: string.IsNullOrEmpty(building.Faction) ? null : building.Faction,
            FactionDisplay: factionMapper.MapFactionDisplay(building.Faction),
            Level: level,
            MaxLevel: Math.Max(building.Names.Length, Math.Max(building.Descriptions.Length, 1)),
            IconPath: GetIconPath(building, level),
            Category: string.IsNullOrEmpty(building.Category) ? null : building.Category
        );
    }

    private static BuildingDetailDto MapToDetail(
        string key,
        BuildingsIndex.BuildingRecord building,
        int level,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale,
        BuildingsIndex buildingsIndex,
        FactionMapper factionMapper)
    {
        var ctx = new ResolutionContext(locale) { FractionId = building.Faction };
        var levelIndex = level - 1;

        var nameSid = levelIndex < building.Names.Length ? building.Names[levelIndex] : "";
        var name = ResolveText(nameSid, resolver, lang, ctx) ?? building.Sid;

        var descSid = levelIndex < building.Descriptions.Length ? building.Descriptions[levelIndex] : "";
        var description = ResolveText(descSid, resolver, lang, ctx);

        List<BuildingCostDto>? costs = null;
        if (building.CostsPerLevel != null && levelIndex < building.CostsPerLevel.Length)
        {
            var levelCosts = building.CostsPerLevel[levelIndex];
            if (levelCosts != null && levelCosts.Length > 0)
            {
                costs = levelCosts
                    .Select(c => new BuildingCostDto(c.ResourceName, c.Amount))
                    .ToList();
            }
        }

        List<BuildingEffectDto>? effects = null;
        if (building.EffectsPerLevel != null && levelIndex < building.EffectsPerLevel.Length)
        {
            var levelEffects = building.EffectsPerLevel[levelIndex];
            if (levelEffects != null && levelEffects.Length > 0)
            {
                effects = levelEffects
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e =>
                    {
                        var effectDesc = ResolveText(e, resolver, lang, ctx) ?? e;
                        // Wall effect icons are in cities_buildings (same as building icons)
                        var iconPath = $"icons/cities_buildings/{e}";
                        return new BuildingEffectDto(effectDesc, iconPath);
                    })
                    .ToList();
            }
        }

        List<BuildingRequirementDto>? requirements = null;
        if (building.RequiredBuildingsPerLevel != null && levelIndex < building.RequiredBuildingsPerLevel.Length)
        {
            var levelReqs = building.RequiredBuildingsPerLevel[levelIndex];
            if (levelReqs != null && levelReqs.Length > 0)
            {
                requirements = levelReqs
                    .Select(r =>
                    {
                        var reqKey = $"{building.Faction}_{r.Sid}";
                        var reqBuildingName = r.Sid;

                        if (buildingsIndex.Buildings.TryGetValue(reqKey, out var reqBuilding))
                        {
                            var reqLevelIndex = r.Level - 1;
                            if (reqLevelIndex >= 0 && reqLevelIndex < reqBuilding.Names.Length)
                            {
                                var reqNameSid = reqBuilding.Names[reqLevelIndex];
                                var resolvedName = ResolveText(reqNameSid, resolver, lang, ctx);
                                if (!string.IsNullOrWhiteSpace(resolvedName))
                                {
                                    reqBuildingName = resolvedName;
                                }
                            }
                        }

                        var reqBuildingId = $"{building.Faction}_{r.Sid}_L{r.Level}";

                        return new BuildingRequirementDto(reqBuildingName, reqBuildingId);
                    })
                    .ToList();
            }
        }

        List<RecruitableUnitDto>? recruitableUnits = null;
        var unitSids = GetRecruitableUnitSids(building, levelIndex, level);
        if (unitSids != null && unitSids.Count > 0)
        {
            recruitableUnits = unitSids
                .Select(unitSid =>
                {
                    var unitName = lang.ResolveText($"{unitSid}_name") ?? unitSid;
                    return new RecruitableUnitDto(unitSid, unitName);
                })
                .ToList();
        }

        List<BuildingUpgradeOptionDto>? upgradeOptions = null;
        if (building.OptionalEffectsPerLevel != null && levelIndex < building.OptionalEffectsPerLevel.Length)
        {
            var levelOptions = building.OptionalEffectsPerLevel[levelIndex];
            if (levelOptions != null && levelOptions.Length > 0)
            {
                upgradeOptions = levelOptions
                    .Select(o =>
                    {
                        var desc = ResolveText(o.DescSid, resolver, lang, ctx) ?? o.Sid;
                        var iconPath = string.IsNullOrWhiteSpace(o.Icon)
                            ? null
                            : $"icons/cities_buildings/{o.Icon}";
                        return new BuildingUpgradeOptionDto(o.Sid, iconPath, desc);
                    })
                    .ToList();
            }
        }

        var maxLevel = Math.Max(building.Names.Length, Math.Max(building.Descriptions.Length, 1));
        var id = $"{key}_L{level}";

        return new BuildingDetailDto(
            Id: id,
            Name: name,
            Faction: string.IsNullOrEmpty(building.Faction) ? null : building.Faction,
            FactionDisplay: factionMapper.MapFactionDisplay(building.Faction),
            FactionIcon: factionMapper.GetFactionIconPath(building.Faction),
            Description: description,
            IconPath: GetIconPath(building, level),
            Costs: costs,
            CostLabel: lang.ResolveText("tooltipBuildingRequireResources"),
            Effects: effects,
            Requirements: requirements,
            RequirementsLabel: lang.ResolveText("tooltipBuildingRequire"),
            RecruitableUnits: recruitableUnits,
            RecruitableUnitsLabel: lang.ResolveText("hire_city_lable"),
            UpgradeOptions: upgradeOptions,
            UpgradesLabel: lang.ResolveText("tooltipBuildingMod")
        );
    }

    private static List<string>? GetRecruitableUnitSids(BuildingsIndex.BuildingRecord building, int levelIndex, int level)
    {
        if (building.RecruitableUnitsPerLevel != null && levelIndex < building.RecruitableUnitsPerLevel.Length)
        {
            var levelUnits = building.RecruitableUnitsPerLevel[levelIndex];
            if (levelUnits != null && levelUnits.Length > 0)
            {
                return levelUnits.ToList();
            }
        }
        else if (level == 1 && building.RecruitableUnits != null && building.RecruitableUnits.Length > 0)
        {
            return building.RecruitableUnits.ToList();
        }
        return null;
    }

    private static string? GetBuildingName(
        BuildingsIndex.BuildingRecord building,
        int level,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale)
    {
        var levelIndex = level - 1;
        if (levelIndex >= building.Names.Length)
        {
            return null;
        }

        var nameSid = building.Names[levelIndex];
        if (string.IsNullOrWhiteSpace(nameSid))
        {
            return null;
        }

        var ctx = new ResolutionContext(locale) { FractionId = building.Faction };
        return ResolveText(nameSid, resolver, lang, ctx);
    }

    private static string? GetBuildingDescription(
        BuildingsIndex.BuildingRecord building,
        int level,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale)
    {
        var levelIndex = level - 1;
        if (levelIndex >= building.Descriptions.Length)
        {
            return null;
        }

        var descSid = building.Descriptions[levelIndex];
        if (string.IsNullOrWhiteSpace(descSid))
        {
            return null;
        }

        var ctx = new ResolutionContext(locale) { FractionId = building.Faction };
        return ResolveText(descSid, resolver, lang, ctx);
    }

    private static string? ResolveText(
        string sid,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        ResolutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return null;
        }

        try
        {
            var result = resolver.Resolve(sid, ctx, out _);
            if (!string.IsNullOrWhiteSpace(result) && result != sid)
            {
                return result;
            }

            var langResult = lang.ResolveText(sid);
            if (!string.IsNullOrWhiteSpace(langResult) && langResult != sid)
            {
                return langResult;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? GetIconPath(BuildingsIndex.BuildingRecord building, int level)
    {
        if (building.Icons == null || building.Icons.Length == 0)
        {
            return null;
        }

        // Use level-specific icon (level is 1-indexed, array is 0-indexed)
        var levelIndex = level - 1;
        var icon = levelIndex >= 0 && levelIndex < building.Icons.Length
            ? building.Icons[levelIndex]
            : building.Icons[0];

        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        // Special case: buildings_wip is a fallback icon at root level
        if (icon == "buildings_wip")
        {
            return $"icons/cities_buildings/{icon}";
        }

        if (!string.IsNullOrWhiteSpace(building.Faction))
        {
            return $"icons/cities_buildings/{building.Faction.ToLowerInvariant()}/{icon}";
        }

        return $"icons/cities_buildings/{icon}";
    }
}
