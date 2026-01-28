using GameData.Indexing;
using Localization.Indexing;
using Localization.Resolution;
using API.Contracts;
using API.Services;
using Microsoft.Extensions.Logging;
using GameData.Shared.Utils;
using static API.Helpers.LocalizationHelper;

namespace API.Endpoints;

public static class AbilitiesEndpoints
{
    public static IEndpointRouteBuilder MapAbilitiesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/abilities")
            .WithTags("Abilities")
            ;

        // GET /api/abilities - List all abilities
        group.MapGet("/", GetAbilities)
            .WithName("GetAbilities")
            .WithSummary("List all abilities")
            .WithDescription("Returns a list of all abilities. Supports search filtering by name, type, or source unit.")
            .Produces<List<AbilityListItemDto>>(200)
            .Produces<ErrorDto>(503);

        // GET /api/abilities/{id} - Get ability details
        group.MapGet("/{id}", GetAbilityById)
            .WithName("GetAbilityById")
            .WithSummary("Get ability details")
            .WithDescription("Returns detailed information about a specific ability including localized text, immunities, and info notes.")
            .Produces<AbilityDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetAbilities(
        IGameDataService dataService,
        IGamePathService gamePathService,
        IAssetServingService assetService,
        ILoggerFactory loggerFactory,
        string? search = null,
        string? type = null)
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
        var ctx = new ResolutionContext(locale);
        var units = data.Units;

        var aggregates = data.AggregatedAbilities;

        IEnumerable<AbilityAggregateResult> filteredItems = aggregates;
        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filteredItems = filteredItems.Where(item =>
                item.Key.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();

            // Special case: "orphan"/"unused" search shows all orphan abilities regardless of name
            var isOrphanSearch = searchTerm.Equals("orphan", StringComparison.OrdinalIgnoreCase) ||
                                 searchTerm.Equals("unused", StringComparison.OrdinalIgnoreCase);

            if (isOrphanSearch)
            {
                filteredItems = filteredItems.Where(item =>
                    item.Key.Type.Equals("Orphan", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                filteredItems = filteredItems.Where(item =>
                {
                    var resolvedName = resolver.Resolve(item.Key.NameSid, ctx, out _) ?? item.Key.NameSid;
                    return item.VariantId.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                           resolvedName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
                });
            }
        }

        var abilitiesList = filteredItems
            .Select(item => MapAggregateToListItem(item, resolver, lang, locale, units))
            .ToList();

        return Results.Ok(abilitiesList);
    }

    private static IResult GetAbilityById(
        string id,
        IGameDataService dataService,
        IGamePathService gamePathService,
        IAssetServingService assetService,
        ILoggerFactory loggerFactory)
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
        var units = data.Units;
        var abilityIndex = data.AbilityIndex;

        var aggregates = data.AggregatedAbilities;
        var matchingAggregate = aggregates.FirstOrDefault(a =>
            a.VariantId.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (matchingAggregate is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Ability '{id}' not found",
                "Check the ability ID and try again. Use GET /api/abilities to list available abilities."
            ));
        }

        var dto = MapAggregateToDetail(matchingAggregate, units, abilityIndex, resolver, lang, locale);
        return Results.Ok(dto);
    }

    private static AbilityListItemDto MapAggregateToListItem(
        AbilityAggregateResult aggregate,
        ITextResolver resolver,
        LangIndex lang,
        string locale,
        List<DbIndex.UnitRecord> units)
    {
        var ctx = new ResolutionContext(locale);
        var resolvedName = resolver.Resolve(aggregate.Key.NameSid, ctx, out _) ?? aggregate.Key.NameSid;

        var primarySid = aggregate.Key.NameSid;

        string? iconPath = null;
        if (!string.IsNullOrWhiteSpace(primarySid))
        {
            var iconName = AbilityIconHelper.GetIconFileName(primarySid);
            iconPath = $"icons/abilities/{iconName}";
        }

        return new AbilityListItemDto(
            Id: aggregate.VariantId,  // Use pre-computed variant ID
            Name: resolvedName,
            AbilityType: aggregate.Key.Type,
            Icon: iconPath
        );
    }

    private static string? ResolveUnitName(
        ITextResolver resolver,
        LangIndex lang,
        string unitId,
        string locale)
    {
        if (string.IsNullOrWhiteSpace(unitId))
            return null;

        var patterns = new[]
        {
            $"{unitId}_name",
            $"unit.{unitId}.name",
            $"unit_{unitId}_name"
        };

        var ctx = new ResolutionContext(locale);
        foreach (var pattern in patterns)
        {
            var result = resolver.Resolve(pattern, ctx, out _);
            if (!string.IsNullOrWhiteSpace(result) && result != pattern)
                return result;

            var langResult = lang.ResolveText(pattern);
            if (!string.IsNullOrWhiteSpace(langResult))
                return langResult;
        }

        return unitId;
    }

    private static AbilityDetailDto MapAggregateToDetail(
        AbilityAggregateResult aggregate,
        List<DbIndex.UnitRecord> units,
        AbilityIndex abilityIndex,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale)
    {
        var primarySid = aggregate.AbilitySids.FirstOrDefault() ?? "";

        string? iconPath = null;
        if (!string.IsNullOrWhiteSpace(primarySid))
        {
            var iconName = AbilityIconHelper.GetIconFileName(primarySid);
            iconPath = $"icons/abilities/{iconName}";
        }

        var sourceUnitIds = aggregate.SourceUnitIds.Where(id => !string.IsNullOrEmpty(id)).ToList();

        int? rank = null;
        int? energyCost = null;
        if (!string.IsNullOrWhiteSpace(aggregate.Key.Rank) && int.TryParse(aggregate.Key.Rank, out var r))
            rank = r;
        if (!string.IsNullOrWhiteSpace(aggregate.Key.Energy) && int.TryParse(aggregate.Key.Energy, out var e))
            energyCost = e;

        var firstUnitId = sourceUnitIds.FirstOrDefault();
        var description = aggregate.Key.ResolvedDescription ?? "";

        var ctx = new ResolutionContext(locale);

        List<string>? immunities = null;
        List<string>? infoNotes = null;
        string? abilityTypeSid = null;

        if (!string.IsNullOrEmpty(primarySid))
        {
            var ability = abilityIndex.Abilities.Values.FirstOrDefault(a =>
                a.NameSid != null && a.NameSid.Equals(primarySid, StringComparison.OrdinalIgnoreCase));

            if (ability != null)
            {
                if (!string.IsNullOrWhiteSpace(ability.AbilityTypeSid))
                {
                    abilityTypeSid = TryResolveText(resolver, ability.AbilityTypeSid, locale) ?? ability.AbilityTypeSid;
                }

                var immunitiesList = new List<string>();
                foreach (var immSid in ability.ImmunitySids)
                {
                    var resolved = TryResolveText(resolver, immSid, locale);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        immunitiesList.Add(resolved);
                    }
                    else
                    {
                        immunitiesList.Add(immSid);
                    }
                }
                if (immunitiesList.Count > 0)
                    immunities = immunitiesList;

                var infoNotesList = new List<string>();
                foreach (var infoSid in ability.InfoDescriptionSids)
                {
                    var resolved = TryResolveText(resolver, infoSid, locale);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        infoNotesList.Add(resolved);
                    }
                }
                if (infoNotesList.Count > 0)
                    infoNotes = infoNotesList;
            }
        }

        var resolvedName = resolver.Resolve(aggregate.Key.NameSid, ctx, out _) ?? aggregate.Key.NameSid;

        var sourceUnitNames = sourceUnitIds
            .Select(unitId => ResolveUnitName(resolver, lang, unitId, locale) ?? unitId)
            .ToList();

        // Stat labels
        var statLabels = new AbilityStatLabelsDto(
            CreaturesWithAbility: TryResolveText(resolver, "usedby_creatures_ability", locale) ?? "Creatures with this Ability"
        );

        return new AbilityDetailDto(
            Id: primarySid,
            Name: resolvedName,
            NameSid: primarySid,
            AbilityType: aggregate.Key.Type,
            Description: description,
            Rank: rank,
            EnergyCost: energyCost,
            AbilityTypeSid: abilityTypeSid,
            Immunities: immunities,
            InfoNotes: infoNotes,
            Icon: iconPath,
            SourceUnitIds: sourceUnitIds.Count > 0 ? sourceUnitIds : null,
            SourceUnitNames: sourceUnitNames.Count > 0 ? sourceUnitNames : null,
            StatLabels: statLabels
        );
    }

    private static AbilityDetailDto MapToDetail(
        AbilityIndex.AbilityRecord ability,
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string locale)
    {
        var name = ResolveAbilityName(resolver, lang, ability.NameSid, locale) ?? ability.AbilityId;
        var description = ResolveAbilityDescription(resolver, lang, ability.DescriptionSid, ability.SourceUnitId, locale);

        string? abilityTypeSid = null;
        if (!string.IsNullOrWhiteSpace(ability.AbilityTypeSid))
        {
            abilityTypeSid = TryResolveText(resolver, ability.AbilityTypeSid, locale) ?? ability.AbilityTypeSid;
        }

        var immunities = new List<string>();
        foreach (var immSid in ability.ImmunitySids)
        {
            var resolved = TryResolveText(resolver, immSid, locale);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                immunities.Add(resolved);
            }
            else
            {
                immunities.Add(immSid);
            }
        }

        var infoNotes = new List<string>();
        foreach (var infoSid in ability.InfoDescriptionSids)
        {
            var resolved = TryResolveText(resolver, infoSid, locale);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                infoNotes.Add(resolved);
            }
        }

        string? iconPath = null;
        if (!string.IsNullOrWhiteSpace(ability.NameSid))
        {
            var iconName = AbilityIconHelper.GetIconFileName(ability.NameSid);
            iconPath = $"icons/abilities/{iconName}";
        }

        // Stat labels
        var statLabels = new AbilityStatLabelsDto(
            CreaturesWithAbility: TryResolveText(resolver, "usedby_creatures_ability", locale) ?? "Creatures with this Ability"
        );

        return new AbilityDetailDto(
            Id: ability.AbilityId,
            Name: name,
            NameSid: ability.NameSid,
            AbilityType: ability.AbilityType,
            Description: description,
            Rank: ability.Rank,
            EnergyCost: ability.EnergyCost,
            AbilityTypeSid: abilityTypeSid,
            Immunities: immunities.Count > 0 ? immunities : null,
            InfoNotes: infoNotes.Count > 0 ? infoNotes : null,
            Icon: iconPath,
            StatLabels: statLabels
        );
    }

    private static string? ResolveAbilityName(
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string? nameSid,
        string locale)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return null;

        var result = TryResolveText(resolver, nameSid, locale);
        if (!string.IsNullOrWhiteSpace(result))
            return result;

        // Fallback to LangIndex
        return lang.ResolveText(nameSid);
    }

    private static string? ResolveAbilityDescription(
        ITextResolver resolver,
        Localization.Indexing.LangIndex lang,
        string? descriptionSid,
        string? unitId,
        string locale)
    {
        if (string.IsNullOrWhiteSpace(descriptionSid))
            return null;

        var ctx = SearchService.CreateAbilityResolutionContext(locale, unitId);

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
}
