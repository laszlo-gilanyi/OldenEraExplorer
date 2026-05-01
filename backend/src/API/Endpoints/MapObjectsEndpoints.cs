using GameData.Indexing;
using Localization.Resolution;
using API.Contracts;
using API.Services;
using static API.Helpers.LocalizationHelper;
using static API.Helpers.IconPaths;

namespace API.Endpoints;

public static class MapObjectsEndpoints
{
    public static IEndpointRouteBuilder MapMapObjectsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/map-objects")
            .WithTags("MapObjects");

        group.MapGet("/", GetMapObjects)
            .WithName("GetMapObjects")
            .WithSummary("List all map objects")
            .WithDescription("Returns a list of all map objects. Supports search filtering by ID or name, and category filtering.")
            .Produces<List<MapObjectListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/categories", GetCategories)
            .WithName("GetMapObjectCategories")
            .WithSummary("List all map object categories")
            .WithDescription("Returns a list of all unique map object categories.")
            .Produces<IReadOnlyList<string>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/{*id}", GetMapObjectById)
            .WithName("GetMapObjectById")
            .WithSummary("Get map object details")
            .WithDescription("Returns detailed information about a specific map object.")
            .Produces<MapObjectDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetMapObjects(
        IGameDataService dataService,
        IGamePathService gamePathService,
        string? search = null,
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
        var locale = gamePathService.CurrentLocale;

        IEnumerable<MapObjectsIndex.MapObjectRecord> mapObjects = FilterDisplayableMapObjects(data.MapObjectsIndex.MapObjects.Values);

        if (!string.IsNullOrWhiteSpace(category))
        {
            mapObjects = mapObjects.Where(mo =>
            {
                var effectiveCategory = mo.BankData != null ? mo.Tag : "unused";
                return effectiveCategory.Equals(category, StringComparison.OrdinalIgnoreCase);
            });
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            mapObjects = mapObjects.Where(mo =>
                mo.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                GetLocalizedText(resolver, lang, mo.NameSid, mo.Id, "name", locale)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                (!string.IsNullOrEmpty(mo.Tag) && mo.Tag.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            );
        }

        var mapObjectsList = mapObjects
            .OrderBy(mo => mo.Id)
            .Select(mo => MapToListItem(mo, resolver, lang, locale))
            .ToList();

        return Results.Ok(mapObjectsList);
    }

    private static IResult GetCategories(IGameDataService dataService)
    {
        if (!dataService.IsLoaded || dataService.Data is null)
        {
            return Results.Json(
                new ErrorDto("Game data not loaded", "Please load game data first using POST /api/game/load"),
                statusCode: 503
            );
        }

        var data = dataService.Data;
        var categories = FilterDisplayableMapObjects(data.MapObjectsIndex.MapObjects.Values)
            .Select(mo => mo.BankData != null ? mo.Tag : "unused")
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        return Results.Ok(categories);
    }

    private static IResult GetMapObjectById(
        string id,
        IGameDataService dataService,
        IGamePathService gamePathService,
        MapObjectDetailsService mapObjectDetailsService)
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
        var locale = gamePathService.CurrentLocale;

        var mapObject = data.MapObjectsIndex.MapObjects.Values
            .FirstOrDefault(mo => mo.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (mapObject is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Map object '{id}' not found",
                "Check the map object ID and try again."
            ));
        }

        var streamingAssetsPath = gamePathService.StreamingAssetsPath;

        var dto = mapObjectDetailsService.GetDetails(
            mapObject,
            resolver,
            lang,
            locale,
            streamingAssetsPath,
            data.Units,
            data.ArtifactsIndex,
            data.SpellsIndex,
            data.DifficultiesIndex,
            data.MapObjectsIndex
        );

        return Results.Ok(dto);
    }

    private static MapObjectListItemDto MapToListItem(
        MapObjectsIndex.MapObjectRecord mapObject,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale)
    {
        var localizedName = GetLocalizedText(resolver, lang, mapObject.NameSid, mapObject.Id, "name", locale);
        var icon = MapObjectIcon(mapObject.PrefabPath);

        string? bankType = null;
        bool? hasGuards = null;
        List<string>? rewardTypes = null;

        if (mapObject.BankData != null)
        {
            bankType = mapObject.Tag;
            hasGuards = mapObject.BankData.Variants.Any(v => v.GuardUnits.Count > 0);
            rewardTypes = ExtractRewardTypes(mapObject.BankData);
        }
        else
        {
            bankType = "unused";
        }

        return new MapObjectListItemDto(
            Id: mapObject.Id,
            Name: localizedName ?? mapObject.Id,
            Category: string.IsNullOrEmpty(mapObject.Tag) ? null : mapObject.Tag,
            Icon: icon,
            BankType: bankType,
            HasGuards: hasGuards,
            RewardTypes: rewardTypes
        );
    }

    private static List<string>? ExtractRewardTypes(BankData bankData)
    {
        var rewardTypes = new HashSet<string>();

        foreach (var variant in bankData.Variants)
        {
            foreach (var reward in variant.RewardSet.Rewards)
            {
                switch (reward.RewardType)
                {
                    case "HeroUnitsReward":
                        rewardTypes.Add("unit");
                        break;
                    case "SideResReward":
                        rewardTypes.Add("resource");
                        break;
                    case "HeroRandomItemsReward":
                        rewardTypes.Add("artifact");
                        break;
                    case "HeroMagicAdditionReward":
                    case "HeroMagicRandomAdditionReward":
                        rewardTypes.Add("spell");
                        break;
                    case "HeroExpReward":
                        rewardTypes.Add("experience");
                        break;
                }
            }
        }

        return rewardTypes.Count > 0 ? rewardTypes.OrderBy(r => r).ToList() : null;
    }

    private static IEnumerable<MapObjectsIndex.MapObjectRecord> FilterDisplayableMapObjects(
        IEnumerable<MapObjectsIndex.MapObjectRecord> mapObjects)
    {
        return mapObjects.Where(mo =>
            mo.IsInteractable &&
            !string.Equals(mo.Tag, "Artifact", StringComparison.OrdinalIgnoreCase) &&
            IsAllowedMapObject(mo));
    }

    private static bool IsAllowedMapObject(MapObjectsIndex.MapObjectRecord mapObject)
    {
        var prefabPath = mapObject.PrefabPath;
        if (string.IsNullOrWhiteSpace(prefabPath)) return false;

        var lowerPath = prefabPath.ToLowerInvariant().Replace('\\', '/');

        var hasValidPath = lowerPath.StartsWith("interactive/", StringComparison.Ordinal) ||
                           lowerPath.StartsWith("resource/", StringComparison.Ordinal) ||
                           lowerPath.StartsWith("barracks/", StringComparison.Ordinal);

        if (!hasValidPath) return false;

        var lowerId = mapObject.Id.ToLowerInvariant();

        if (lowerId.StartsWith("custom_", StringComparison.Ordinal)) return false;
        if (lowerId.StartsWith("campaign_", StringComparison.Ordinal)) return false;
        if (lowerId.EndsWith("_campaign", StringComparison.Ordinal)) return false;
        if (lowerId.StartsWith("pvp_promo_", StringComparison.Ordinal)) return false;
        if (lowerId.EndsWith("_old", StringComparison.Ordinal)) return false;

        return true;
    }
}
