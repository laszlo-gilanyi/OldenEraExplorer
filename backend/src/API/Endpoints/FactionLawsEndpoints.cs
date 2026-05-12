using GameData.Indexing;
using GameData.Services;
using Localization.Resolution;
using API.Contracts;
using API.Services;
using static API.Helpers.LocalizationHelper;

namespace API.Endpoints;

public static class FactionLawsEndpoints
{
    public static IEndpointRouteBuilder MapFactionLawsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/faction-laws")
            .WithTags("FactionLaws")
            ;

        // GET /api/faction-laws - List all faction laws
        group.MapGet("/", GetFactionLaws)
            .WithName("GetFactionLaws")
            .WithSummary("List all faction laws")
            .WithDescription("Returns a list of all faction laws. Supports search filtering by ID, name, or faction.")
            .Produces<List<FactionLawListItemDto>>(200)
            .Produces<ErrorDto>(503);

        // GET /api/faction-laws/{id} - Get faction law details
        group.MapGet("/{id}", GetFactionLawById)
            .WithName("GetFactionLawById")
            .WithSummary("Get faction law details")
            .WithDescription("Returns detailed information about a specific faction law including levels and localized text.")
            .Produces<FactionLawDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetFactionLaws(
        IGameDataService dataService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
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
        var resolver = data.ResolverFacade;
        var langIndex = data.Lang;
        var locale = gamePathService.CurrentLocale;

        IEnumerable<FactionLawIndex.FactionLawRecord> factionLaws = data.FactionLawIndex.FactionLaws.Values;

        // Filter laws that have valid localized text
        var lawsWithNames = factionLaws.Select(law =>
        {
            var ctx = new ResolutionContext(locale) { LawId = law.Id };
            var localizedName = TryResolveText(resolver, law.NameSid, ctx) ?? langIndex.ResolveText(law.NameSid);
            var localizedDesc = TryResolveText(resolver, law.DescSid, ctx) ?? langIndex.ResolveText(law.DescSid);

            if (string.IsNullOrWhiteSpace(localizedName) && string.IsNullOrWhiteSpace(localizedDesc))
                return null;

            return new
            {
                Law = law,
                Name = localizedName ?? law.Id,
                Description = localizedDesc
            };
        }).Where(x => x != null).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            lawsWithNames = lawsWithNames.Where(x =>
            {
                if (x!.Law.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (x.Law.Faction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                var factionDisplay = factionMapper.MapFactionDisplay(x.Law.Faction);
                if (factionDisplay?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                    return true;

                return false;
            }).ToList();
        }

        // Map to DTOs
        var lawsList = lawsWithNames
            .Select(x => MapToListItem(x!.Law, x.Name, x.Description, langIndex, factionMapper))
            .ToList();

        return Results.Ok(lawsList);
    }

    private static IResult GetFactionLawById(
        string id,
        IGameDataService dataService,
        IGamePathService gamePathService,
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
        var langIndex = data.Lang;
        var locale = gamePathService.CurrentLocale;

        var factionLaw = data.FactionLawIndex.FactionLaws.Values
            .FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (factionLaw is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Faction law '{id}' not found",
                "Check the faction law ID and try again. Use GET /api/faction-laws to list available faction laws."
            ));
        }

        var dto = MapToDetail(factionLaw, resolver, langIndex, locale, factionMapper, data.FactionLawIndex);
        return Results.Ok(dto);
    }

    private static FactionLawListItemDto MapToListItem(
        FactionLawIndex.FactionLawRecord law,
        string name,
        string? description,
        Localization.Indexing.LangIndex langIndex,
        FactionMapper factionMapper)
    {
        var factionDisplay = factionMapper.MapFactionDisplay(law.Faction);

        return new FactionLawListItemDto(
            Id: law.Id,
            Name: name,
            Faction: string.IsNullOrEmpty(law.Faction) ? null : law.Faction,
            FactionDisplay: factionDisplay,
            Icon: string.IsNullOrEmpty(law.Icon) ? null : $"icons/fraction_laws/{law.Icon}"
        );
    }

    private static FactionLawDetailDto MapToDetail(
        FactionLawIndex.FactionLawRecord law,
        ITextResolver resolver,
        Localization.Indexing.LangIndex langIndex,
        string locale,
        FactionMapper factionMapper,
        FactionLawIndex factionLawIndex)
    {
        // Base context for faction law
        var baseCtx = new ResolutionContext(locale) { LawId = law.Id };

        var localizedName = TryResolveText(resolver, law.NameSid, baseCtx) ?? langIndex.ResolveText(law.NameSid);

        var factionDisplay = factionMapper.MapFactionDisplay(law.Faction);

        var levels = new List<FactionLawLevelDto>();
        for (int i = 0; i < law.ParametersPerLevel.Count; i++)
        {
            var levelParams = law.ParametersPerLevel[i];
            int levelNum = i + 1;

            var ctx = new ResolutionContext(locale)
            {
                LawId = law.Id,
                LawLevel = levelNum
            };

            var levelDescription = TryResolveText(resolver, law.DescSid, ctx);

            levels.Add(new FactionLawLevelDto(
                Level: levelNum,
                Cost: levelParams.Cost,
                Description: levelDescription
            ));
        }

        var statLabels = new FactionLawStatLabelsDto(
            Cost: TryResolveText(resolver, "label_cost", baseCtx) ?? "Cost"
        );

        List<FactionLawLineDto>? layoutDto = null;
        if (!string.IsNullOrEmpty(law.Faction)
            && factionLawIndex.Layouts.TryGetValue(law.Faction, out var lines))
        {
            layoutDto = new List<FactionLawLineDto>(lines.Count);
            foreach (var line in lines)
            {
                var groupDtos = new List<FactionLawGroupDto>(line.Groups.Count);
                foreach (var group in line.Groups)
                {
                    var entries = new List<FactionLawLayoutEntryDto>(group.LawIds.Count);
                    foreach (var lawId in group.LawIds)
                    {
                        string? entryName = null;
                        string? entryIcon = null;
                        int entryLevelCount = 0;
                        if (factionLawIndex.FactionLaws.TryGetValue(lawId, out var entryRec))
                        {
                            var entryCtx = new ResolutionContext(locale) { LawId = entryRec.Id };
                            entryName = TryResolveText(resolver, entryRec.NameSid, entryCtx)
                                ?? langIndex.ResolveText(entryRec.NameSid);
                            if (!string.IsNullOrEmpty(entryRec.Icon))
                                entryIcon = $"icons/fraction_laws/{entryRec.Icon}";
                            entryLevelCount = entryRec.ParametersPerLevel.Count;
                        }
                        entries.Add(new FactionLawLayoutEntryDto(lawId, entryName, entryIcon, entryLevelCount));
                    }
                    groupDtos.Add(new FactionLawGroupDto(entries));
                }
                layoutDto.Add(new FactionLawLineDto(line.CountToUnlock, groupDtos));
            }
        }

        return new FactionLawDetailDto(
            Id: law.Id,
            Name: law.NameSid,
            LocalizedName: localizedName,
            Faction: string.IsNullOrEmpty(law.Faction) ? null : law.Faction,
            FactionDisplay: factionDisplay,
            FactionIcon: factionMapper.GetFactionIconPath(law.Faction),
            Icon: string.IsNullOrEmpty(law.Icon) ? null : $"icons/fraction_laws/{law.Icon}",
            Levels: levels.Count > 0 ? levels : null,
            StatLabels: statLabels,
            Layout: layoutDto
        );
    }
}
