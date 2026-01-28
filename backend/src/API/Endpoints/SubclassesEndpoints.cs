using GameData.Indexing;
using GameData.Services;
using Localization.Resolution;
using API.Contracts;
using API.Services;
using static API.Helpers.LocalizationHelper;

namespace API.Endpoints;

public static class SubclassesEndpoints
{
    public static IEndpointRouteBuilder MapSubclassesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/subclasses")
            .WithTags("Subclasses")
            ;

        // GET /api/subclasses - List all subclasses
        group.MapGet("/", GetSubclasses)
            .WithName("GetSubclasses")
            .WithSummary("List all subclasses")
            .WithDescription("Returns a list of all hero subclasses. Supports search filtering by ID, name, faction, or class type.")
            .Produces<List<SubclassListItemDto>>(200)
            .Produces<ErrorDto>(503);

        // GET /api/subclasses/{id} - Get subclass details
        group.MapGet("/{id}", GetSubclassById)
            .WithName("GetSubclassById")
            .WithSummary("Get subclass details")
            .WithDescription("Returns detailed information about a specific subclass including required skills.")
            .Produces<SubclassDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetSubclasses(
        IGameDataService dataService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
        ClassMapper classMapper,
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
        var resolver = data.ResolverFacade;
        var locale = gamePathService.CurrentLocale;

        IEnumerable<SubclassesIndex.SubclassRecord> subclasses = data.SubclassesIndex.Subclasses.Values
            .Where(s => HasSubclassLocalization(s, lang));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            subclasses = subclasses.Where(s =>
                s.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (TryResolveText(resolver, s.NameSid, locale) ?? GetLocalizedText(lang, s.NameSid))?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                s.Faction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                factionMapper.MapFactionDisplay(s.Faction)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                s.ClassType.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                classMapper.MapClassDisplay(s.ClassType, s.Faction)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true
            );
        }

        var subclassList = subclasses
            .Select(s => MapToListItem(s, lang, factionMapper, classMapper))
            .ToList();

        return Results.Ok(subclassList);
    }

    private static IResult GetSubclassById(
        string id,
        IGameDataService dataService,
        IGamePathService gamePathService,
        FactionMapper factionMapper,
        ClassMapper classMapper)
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
        var skillsIndex = data.SkillsIndex;
        var resolver = data.ResolverFacade;
        var locale = gamePathService.CurrentLocale;

        var subclass = data.SubclassesIndex.Subclasses.Values.FirstOrDefault(s =>
            s.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
            HasSubclassLocalization(s, lang));

        if (subclass is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Subclass '{id}' not found",
                "Check the subclass ID and try again. Use GET /api/subclasses to list available subclasses."
            ));
        }

        var dto = MapToDetail(subclass, lang, skillsIndex, resolver, locale, factionMapper, classMapper);
        return Results.Ok(dto);
    }

    private static SubclassListItemDto MapToListItem(
        SubclassesIndex.SubclassRecord subclass,
        Localization.Indexing.LangIndex lang,
        FactionMapper factionMapper,
        ClassMapper classMapper)
    {
        var localizedName = GetLocalizedText(lang, subclass.NameSid);

        return new SubclassListItemDto(
            Id: subclass.Id,
            Name: localizedName ?? subclass.Id,
            Faction: string.IsNullOrEmpty(subclass.Faction) ? null : subclass.Faction,
            FactionDisplay: factionMapper.MapFactionDisplay(subclass.Faction),
            ClassType: string.IsNullOrEmpty(subclass.ClassType) ? null : subclass.ClassType,
            ClassDisplay: classMapper.MapClassDisplay(subclass.ClassType, subclass.Faction),
            Icon: string.IsNullOrEmpty(subclass.Icon) ? null : $"icons/hero_sub_classes/{subclass.Icon}"
        );
    }

    private static SubclassDetailDto MapToDetail(
        SubclassesIndex.SubclassRecord subclass,
        Localization.Indexing.LangIndex lang,
        SkillsIndex skillsIndex,
        ITextResolver resolver,
        string locale,
        FactionMapper factionMapper,
        ClassMapper classMapper)
    {
        var localizedName = GetLocalizedText(lang, subclass.NameSid);

        var description = TryResolveText(resolver, subclass.DescSid, locale)
            ?? GetLocalizedText(lang, subclass.DescSid);

        var requiredSkills = ResolveRequiredSkills(subclass.RequiredSkills, lang, skillsIndex);

        var statLabels = new SubclassStatLabelsDto(
            RequiredSkills: TryResolveText(resolver, "label_required_skills", locale) ?? "Required Skills"
        );

        return new SubclassDetailDto(
            Id: subclass.Id,
            Name: localizedName ?? subclass.Id,
            Description: description,
            Faction: string.IsNullOrEmpty(subclass.Faction) ? null : subclass.Faction,
            FactionDisplay: factionMapper.MapFactionDisplay(subclass.Faction),
            FactionIcon: factionMapper.GetFactionIconPath(subclass.Faction),
            ClassType: string.IsNullOrEmpty(subclass.ClassType) ? null : subclass.ClassType,
            ClassDisplay: classMapper.MapClassDisplay(subclass.ClassType, subclass.Faction),
            ClassIcon: classMapper.GetClassIconPath(subclass.ClassType, subclass.Faction),
            Icon: string.IsNullOrEmpty(subclass.Icon) ? null : $"icons/hero_sub_classes/{subclass.Icon}",
            RequiredSkills: requiredSkills,
            StatLabels: statLabels
        );
    }

    private static List<RequiredSkillDto> ResolveRequiredSkills(
        List<SubclassesIndex.ActivationCondition> activationConditions,
        Localization.Indexing.LangIndex lang,
        SkillsIndex skillsIndex)
    {
        var requiredSkills = new List<RequiredSkillDto>();

        if (activationConditions == null || activationConditions.Count == 0)
        {
            return requiredSkills;
        }

        foreach (var condition in activationConditions)
        {
            var skillName = condition.SkillSid;
            string? skillIcon = null;

            if (skillsIndex.Skills.TryGetValue(condition.SkillSid, out var skillRecord))
            {
                // Use Level 3 (Expert) icon and name (index 2, since 0=Basic, 1=Advanced, 2=Expert)
                const int expertLevelIndex = 2;

                if (skillRecord.LevelParams.Count > expertLevelIndex)
                {
                    var expertLevel = skillRecord.LevelParams[expertLevelIndex];
                    skillIcon = expertLevel.Icon;

                    skillName = GetLocalizedText(lang, expertLevel.NameSid) ?? skillName;
                }
                else if (skillRecord.LevelParams.Count > 0)
                {
                    skillIcon = skillRecord.LevelParams[0].Icon;
                    skillName = GetLocalizedText(lang, skillRecord.LevelParams[0].NameSid) ?? skillName;
                }
            }

            requiredSkills.Add(new RequiredSkillDto(
                SkillId: condition.SkillSid,
                SkillName: skillName,
                Icon: string.IsNullOrEmpty(skillIcon) ? null : $"icons/hero_skills/{skillIcon}"
            ));
        }

        return requiredSkills;
    }

    private static string? GetLocalizedText(
        Localization.Indexing.LangIndex lang, string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        try
        {
            var result = lang.ResolveText(sid);
            if (!string.IsNullOrWhiteSpace(result) && result != sid)
            {
                return result;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool HasSubclassLocalization(
        SubclassesIndex.SubclassRecord subclass,
        Localization.Indexing.LangIndex lang)
    {
        var nameInLang = GetLocalizedText(lang, subclass.NameSid);
        var descInLang = GetLocalizedText(lang, subclass.DescSid);

        return nameInLang != null || descInLang != null;
    }
}
