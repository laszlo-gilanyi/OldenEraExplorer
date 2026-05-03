using GameData.Indexing;
using Localization.Resolution;
using API.Contracts;
using API.Services;

namespace API.Endpoints;

public static class SkillsEndpoints
{
    public static IEndpointRouteBuilder MapSkillsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/skills")
            .WithTags("Skills")
            ;

        // GET /api/skills - List all skills
        group.MapGet("/", GetSkills)
            .WithName("GetSkills")
            .WithSummary("List all skills")
            .WithDescription("Returns all hero skills. Supports search filtering by ID, name, or skill type.")
            .Produces<List<SkillListItemDto>>(200)
            .Produces<ErrorDto>(503);

        // GET /api/skills/{id} - Get skill details
        group.MapGet("/{id}", GetSkillById)
            .WithName("GetSkillById")
            .WithSummary("Get skill details")
            .WithDescription("Returns detailed information about a specific skill including levels, sub-skills, and localized text.")
            .Produces<SkillDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetSkills(
        IGameDataService dataService,
        IGamePathService gamePathService,
        string? search = null,
        bool includeArena = false)
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
        var skillsIndex = data.SkillsIndex;
        var langIndex = data.Lang;
        var locale = gamePathService.CurrentLocale;

        // 1. Exclude pseudo-skills (technical skills)
        // 2. Exclude campaign_* skills (campaign-specific)
        // 3. Exclude arena_* skills unless requested
        // 4. Exclude skills without localization (no name AND no description in lang files)
        IEnumerable<SkillsIndex.SkillRecord> skills = skillsIndex.Skills.Values
            .Where(s => !s.IsPseudoSkill)
            .Where(s => !s.SkillId.StartsWith("campaign_", StringComparison.OrdinalIgnoreCase))
            .Where(s => includeArena || !s.SkillId.StartsWith("arena_", StringComparison.OrdinalIgnoreCase))
            .Where(s => HasLocalization(s, langIndex));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            skills = skills.Where(s =>
                s.SkillId.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                GetLocalizedSkillName(resolver, s.NameSid, locale)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true
            );
        }

        var skillsList = skills.OrderBy(s => s.SkillId)
            .Select(s => MapToListItem(s, resolver, locale))
            .ToList();

        return Results.Ok(skillsList);
    }

    private static IResult GetSkillById(string id, IGameDataService dataService, IGamePathService gamePathService)
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
        var skillsIndex = data.SkillsIndex;
        var langIndex = data.Lang;
        var locale = gamePathService.CurrentLocale;

        var skill = skillsIndex.Skills.Values.FirstOrDefault(s =>
            s.SkillId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
            !s.IsPseudoSkill &&
            !s.SkillId.StartsWith("campaign_", StringComparison.OrdinalIgnoreCase) &&
            HasLocalization(s, langIndex));

        if (skill is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Skill '{id}' not found",
                "Check the skill ID and try again. Use GET /api/skills to list available skills."
            ));
        }

        var dto = MapToDetail(skill, skillsIndex, resolver, langIndex, locale, data);
        return Results.Ok(dto);
    }

    private static SkillListItemDto MapToListItem(
        SkillsIndex.SkillRecord skill,
        ITextResolver resolver,
        string locale)
    {
        var localizedName = GetLocalizedSkillName(resolver, skill.NameSid, locale);

        string? icon = null;
        if (skill.LevelParams.Count > 0 && !string.IsNullOrEmpty(skill.LevelParams[0].Icon))
        {
            icon = $"icons/hero_skills/{skill.LevelParams[0].Icon}";
        }

        return new SkillListItemDto(
            Id: skill.SkillId,
            Name: localizedName ?? skill.SkillId,
            Icon: icon
        );
    }

    private static SkillDetailDto MapToDetail(
        SkillsIndex.SkillRecord skill,
        SkillsIndex skillsIndex,
        ITextResolver resolver,
        Localization.Indexing.LangIndex langIndex,
        string locale,
        GameDataLoadResult data)
    {
        SkillLevelDto? level1 = null, level2 = null, level3 = null;

        if (skill.LevelParams.Count >= 1)
        {
            var lvl1 = skill.LevelParams[0];
            var lvl1Ctx = new ResolutionContext(locale) { SkillId = skill.SkillId, SkillLevel = 1 };
            level1 = new SkillLevelDto(
                LevelName: ResolveText(resolver, lvl1.NameSid, lvl1Ctx, langIndex) ?? "Basic",
                Description: ResolveText(resolver, lvl1.DescSid, lvl1Ctx, langIndex),
                Icon: string.IsNullOrEmpty(lvl1.Icon) ? null : $"icons/hero_skills/{lvl1.Icon}",
                SubSkillChoices: new List<SubSkillDto>()
            );
        }

        if (skill.LevelParams.Count >= 2)
        {
            var lvl2 = skill.LevelParams[1];
            var lvl2Ctx = new ResolutionContext(locale) { SkillId = skill.SkillId, SkillLevel = 2 };
            var subSkills2 = BuildSubSkillDtos(lvl2.SubSkills, skill.SkillId, skillsIndex, resolver, langIndex, locale, data);
            level2 = new SkillLevelDto(
                LevelName: ResolveText(resolver, lvl2.NameSid, lvl2Ctx, langIndex) ?? "Advanced",
                Description: ResolveText(resolver, lvl2.DescSid, lvl2Ctx, langIndex),
                Icon: string.IsNullOrEmpty(lvl2.Icon) ? null : $"icons/hero_skills/{lvl2.Icon}",
                SubSkillChoices: subSkills2
            );
        }

        if (skill.LevelParams.Count >= 3)
        {
            var lvl3 = skill.LevelParams[2];
            var lvl3Ctx = new ResolutionContext(locale) { SkillId = skill.SkillId, SkillLevel = 3 };
            var subSkills3 = BuildSubSkillDtos(lvl3.SubSkills, skill.SkillId, skillsIndex, resolver, langIndex, locale, data);
            level3 = new SkillLevelDto(
                LevelName: ResolveText(resolver, lvl3.NameSid, lvl3Ctx, langIndex) ?? "Expert",
                Description: ResolveText(resolver, lvl3.DescSid, lvl3Ctx, langIndex),
                Icon: string.IsNullOrEmpty(lvl3.Icon) ? null : $"icons/hero_skills/{lvl3.Icon}",
                SubSkillChoices: subSkills3
            );
        }

        var statLabels = new SkillStatLabelsDto(
            HeroesStartingWithSkill: ResolveText(resolver, "usedby_heroes_skill", new ResolutionContext(locale), langIndex) ?? "Heroes starting with this Skill"
        );

        return new SkillDetailDto(
            Id: skill.SkillId,
            SkillType: string.IsNullOrEmpty(skill.SkillType) ? null : skill.SkillType,
            Level1: level1,
            Level2: level2,
            Level3: level3,
            StatLabels: statLabels
        );
    }

    private static List<SubSkillDto> BuildSubSkillDtos(
        List<string> subSkillIds,
        string skillId,
        SkillsIndex skillsIndex,
        ITextResolver resolver,
        Localization.Indexing.LangIndex langIndex,
        string locale,
        GameDataLoadResult data)
    {
        var result = new List<SubSkillDto>();

        foreach (var subSkillId in subSkillIds)
        {
            if (!skillsIndex.SubSkills.TryGetValue(subSkillId, out var subSkill))
                continue;

            var ctx = new ResolutionContext(locale)
            {
                SkillId = skillId,
                SubSkillId = subSkillId
            };

            var name = ResolveText(resolver, subSkill.NameSid, ctx, langIndex);
            var desc = ResolveText(resolver, subSkill.DescSid, ctx, langIndex);

            SpellLinkDto? grantedSpell = null;
            if (skillsIndex.SubSkillToMagics.TryGetValue(subSkillId, out var magicIds) && magicIds.Count > 0)
            {
                var spellId = magicIds[0];
                if (data.SpellsIndex.Spells.TryGetValue(spellId, out var spellRecord))
                {
                    var spellCtx = new ResolutionContext(locale);
                    var spellName = ResolveText(resolver, spellRecord.NameSid, spellCtx, langIndex);
                    if (!string.IsNullOrWhiteSpace(spellName))
                    {
                        grantedSpell = new SpellLinkDto(
                            Id: spellId,
                            Name: spellName,
                            Icon: string.IsNullOrEmpty(spellRecord.Icon) ? null : $"icons/hero_magics/{spellRecord.Icon}"
                        );
                    }
                }
            }

            BattleAbilityLinkDto? grantedBattleAbility = null;
            if (!string.IsNullOrEmpty(subSkill.GrantedBattleAbilityId) &&
                skillsIndex.HeroAbilities.TryGetValue(subSkill.GrantedBattleAbilityId, out var abilityRecord))
            {
                var abilityCtx = new ResolutionContext(locale) { SubSkillId = subSkillId, SkillId = skillId, HeroAbilityId = subSkill.GrantedBattleAbilityId };
                var abilityName = ResolveText(resolver, abilityRecord.NameSid, abilityCtx, langIndex);
                if (!string.IsNullOrWhiteSpace(abilityName))
                {
                    var abilityDesc = ResolveText(resolver, abilityRecord.DescSid, abilityCtx, langIndex);
                    grantedBattleAbility = new BattleAbilityLinkDto(
                        Id: subSkill.GrantedBattleAbilityId,
                        Name: abilityName,
                        Icon: $"icons/hero_abilities/{subSkill.GrantedBattleAbilityId}",
                        Description: abilityDesc
                    );
                }
            }

            result.Add(new SubSkillDto(
                Id: subSkillId,
                Name: name ?? subSkillId,
                Description: desc,
                Icon: string.IsNullOrEmpty(subSkill.Icon) ? null : $"icons/hero_sub_skills/{subSkill.Icon}",
                GrantedSpell: grantedSpell,
                GrantedBattleAbility: grantedBattleAbility
            ));
        }

        return result;
    }

    private static string? GetLocalizedSkillName(ITextResolver resolver, string nameSid, string locale)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return null;

        try
        {
            var ctx = new ResolutionContext(locale);
            var result = resolver.Resolve(nameSid, ctx, out _);

            if (!string.IsNullOrWhiteSpace(result) && result != nameSid)
            {
                return result;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? GetLocalizedSkillDescription(ITextResolver resolver, string descSid, string locale)
    {
        if (string.IsNullOrWhiteSpace(descSid))
            return null;

        try
        {
            var ctx = new ResolutionContext(locale);
            var result = resolver.Resolve(descSid, ctx, out _);

            if (!string.IsNullOrWhiteSpace(result) && result != descSid && !result.StartsWith("{"))
            {
                return result;
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool HasLocalization(
        SkillsIndex.SkillRecord skill,
        Localization.Indexing.LangIndex langIndex)
    {
        var nameInLang = !string.IsNullOrWhiteSpace(skill.NameSid)
            ? langIndex.ResolveText(skill.NameSid)
            : null;
        var descInLang = !string.IsNullOrWhiteSpace(skill.DescSid)
            ? langIndex.ResolveText(skill.DescSid)
            : null;

        return nameInLang != null || descInLang != null;
    }

    private static string? ResolveText(
        ITextResolver resolver,
        string sid,
        ResolutionContext ctx,
        Localization.Indexing.LangIndex langIndex)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        try
        {
            var result = resolver.Resolve(sid, ctx, out _);
            if (!string.IsNullOrWhiteSpace(result) && result != sid)
            {
                return result;
            }
        }
        catch
        {
        }

        // Fallback to lang index
        return langIndex.ResolveText(sid);
    }
}
