using GameData.Indexing;
using GameData.Loading;
using GameData.Services;
using Localization.Indexing;
using Localization.Resolution;
using API.Contracts;
using API.Services;
using static API.Helpers.IconPaths;
using static API.Helpers.LocalizationHelper;

namespace API.Endpoints;

public static class HeroesEndpoints
{
    public static IEndpointRouteBuilder MapHeroesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/heroes")
            .WithTags("Heroes")
            ;

        group.MapGet("/", GetHeroes)
            .WithName("GetHeroes")
            .WithSummary("List all heroes")
            .WithDescription("Returns all heroes. Supports search filtering by ID, name, faction, or class.")
            .Produces<List<HeroListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/{id}", GetHeroById)
            .WithName("GetHeroById")
            .WithSummary("Get hero details")
            .WithDescription("Returns detailed information about a specific hero including stats, specialization, starting army, skills, and spells.")
            .Produces<HeroDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetHeroes(
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
        var heroesIndex = data.HeroesIndex;
        var lang = data.Lang;
        var resolver = data.ResolverFacade;
        var locale = gamePathService.CurrentLocale;

        IEnumerable<HeroesIndex.HeroRecord> heroes = heroesIndex.Heroes.Values
            .Where(h => !h.IsTutorialOrCampaignHero);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            heroes = heroes.Where(h =>
                h.HeroId.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                GetHeroName(lang, h.HeroId)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                h.Fraction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                factionMapper.MapFactionDisplay(h.Fraction)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                h.ClassType.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                classMapper.MapClassDisplay(h.ClassType, h.Fraction)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true
            );
        }

        var heroesList = heroes
            .OrderBy(h => h.Fraction)
            .ThenBy(h => h.HeroId)
            .Select(h => MapToListItem(h, lang, factionMapper, classMapper))
            .ToList();

        return Results.Ok(heroesList);
    }

    private static IResult GetHeroById(
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
        var heroesIndex = data.HeroesIndex;
        var lang = data.Lang;
        var resolver = data.ResolverFacade;
        var locale = gamePathService.CurrentLocale;

        var heroRecord = heroesIndex.Heroes.Values.FirstOrDefault(h =>
            h.HeroId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
            !h.IsTutorialOrCampaignHero);

        if (heroRecord is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Hero '{id}' not found",
                "Check the hero ID and try again. Use GET /api/heroes to list available heroes."
            ));
        }

        var dto = MapToDetail(heroRecord, lang, resolver, locale, data.SpellsIndex, data.SkillsIndex, data.HeroSpecializationsIndex, factionMapper, classMapper);
        return Results.Ok(dto);
    }

    private static HeroListItemDto MapToListItem(
        HeroesIndex.HeroRecord hero,
        LangIndex lang,
        FactionMapper factionMapper,
        ClassMapper classMapper)
    {
        var heroName = GetHeroName(lang, hero.HeroId) ?? hero.HeroId;
        var classDisplay = classMapper.MapClassDisplay(hero.ClassType, hero.Fraction);
        var factionDisplay = factionMapper.MapFactionDisplay(hero.Fraction);

        return new HeroListItemDto(
            Id: hero.HeroId,
            Name: heroName,
            Faction: string.IsNullOrEmpty(hero.Fraction) ? null : hero.Fraction,
            FactionDisplay: factionDisplay,
            ClassType: string.IsNullOrEmpty(hero.ClassType) ? null : hero.ClassType,
            ClassDisplay: classDisplay,
            IconPath: string.IsNullOrEmpty(hero.Icon) ? null : HeroLargePortrait(hero.Icon)
        );
    }

    private static HeroDetailDto MapToDetail(
        HeroesIndex.HeroRecord hero,
        LangIndex lang,
        ITextResolver resolver,
        string locale,
        SpellsIndex spellsIndex,
        SkillsIndex skillsIndex,
        HeroSpecializationsIndex heroSpecializationsIndex,
        FactionMapper factionMapper,
        ClassMapper classMapper)
    {
        var heroName = GetHeroName(lang, hero.HeroId) ?? hero.HeroId;
        var classDisplay = classMapper.MapClassDisplay(hero.ClassType, hero.Fraction);
        var factionDisplay = factionMapper.MapFactionDisplay(hero.Fraction);

        var attack = hero.BaseStats.TryGetValue("offence", out var att) ? att.ToString() : null;
        var defence = hero.BaseStats.TryGetValue("defence", out var def) ? def.ToString() : null;
        var spellPower = hero.BaseStats.TryGetValue("spellPower", out var sp) ? sp.ToString() : null;
        var knowledge = hero.BaseStats.TryGetValue("intelligence", out var intel) ? intel.ToString() : null;

        string descSid = $"{hero.HeroId}_spec_description";

        if (heroSpecializationsIndex?.Specializations.TryGetValue(hero.SpecializationSid ?? "", out var specRecord) == true)
        {
            if (!string.IsNullOrWhiteSpace(specRecord.NameSid))
                nameSid = specRecord.NameSid;
            if (!string.IsNullOrWhiteSpace(specRecord.DescSid))
                descSid = specRecord.DescSid;
        }

        var specializationName = lang.ResolveText(nameSid) ?? "";
        var specializationDescription = TryResolveTextWithHeroContext(resolver, descSid, locale, hero.SpecializationSid)
            ?? lang.ResolveText(descSid) ?? "";

        var startingArmy = hero.StartSquad
            .Select(unit => new StartingArmyDto(
                UnitId: unit.Sid,
                UnitName: lang.ResolveText($"{unit.Sid}_name") ?? unit.Sid,
                CountInterval: $"{unit.Min} - {unit.Max}",
                Icon: UnitHexPortrait(unit.Sid)
            ))
            .ToList();

        var startingSkills = hero.StartSkills
            .Select(skill =>
            {
                string skillName = skill.Sid;
                string? skillIcon = null;

                if (skillsIndex?.Skills.TryGetValue(skill.Sid, out var skillRecord) == true)
                {
                    var levelIndex = skill.Level - 1;
                    if (levelIndex >= 0 && levelIndex < skillRecord.LevelParams.Count)
                    {
                        var levelParam = skillRecord.LevelParams[levelIndex];
                        skillName = lang.ResolveText(levelParam.NameSid) ?? skill.Sid;
                        skillIcon = levelParam.Icon;
                    }
                }

                return new StartingSkillDto(
                    SkillId: skill.Sid,
                    SkillName: skillName,
                    Icon: string.IsNullOrEmpty(skillIcon) ? null : $"icons/hero_skills/{skillIcon}"
                );
            })
            .ToList();

        var effectiveSpells = hero.StartMagics.ToList();
        if (heroSpecializationsIndex != null && !string.IsNullOrWhiteSpace(hero.SpecializationSid))
        {
            effectiveSpells = heroSpecializationsIndex.ApplyReplacements(hero.SpecializationSid, effectiveSpells);
        }

        var startingSpells = effectiveSpells
            .Select(spellSid =>
            {
                string spellName = spellSid;
                string? spellIcon = null;
                bool isMasterful = spellSid.EndsWith("_special", StringComparison.OrdinalIgnoreCase);

                if (spellsIndex?.Spells.TryGetValue(spellSid, out var spellRecord) == true)
                {
                    spellName = lang.ResolveText(spellRecord.NameSid) ?? spellSid;
                    spellIcon = spellRecord.Icon;
                }

                return new StartingSpellDto(
                    SpellId: spellSid,
                    SpellName: spellName,
                    Icon: string.IsNullOrEmpty(spellIcon) ? null : $"icons/hero_magics/{spellIcon}",
                    IsMasterful: isMasterful
                );
            })
            .ToList();

        var description = lang.ResolveText($"{hero.HeroId}_description") ?? "";
        var motto = lang.ResolveText($"{hero.HeroId}_motto") ?? "";

        var statLabels = new HeroStatLabelsDto(
            StartingArmy: TryResolveText(resolver, "lobby_units", locale) ?? "Starting Army",
            StartingSkills: TryResolveText(resolver, "lobby_skills", locale) ?? "Starting Skills",
            StartingSpells: TryResolveText(resolver, "lobby_spells", locale) ?? "Starting Spells",
            Biography: TryResolveText(resolver, "label_description", locale) ?? "Description",
            Motto: TryResolveText(resolver, "label_hero_motto", locale) ?? "Motto"
        );

        return new HeroDetailDto(
            Id: hero.HeroId,
            Name: heroName,
            Faction: string.IsNullOrEmpty(hero.Fraction) ? null : hero.Fraction,
            FactionDisplay: factionDisplay,
            ClassType: string.IsNullOrEmpty(hero.ClassType) ? null : hero.ClassType,
            ClassDisplay: classDisplay,
            IconPath: string.IsNullOrEmpty(hero.Icon) ? null : HeroLargePortrait(hero.Icon),
            ClassIcon: classMapper.GetClassIconPath(hero.ClassType, hero.Fraction),
            SpecializationIcon: string.IsNullOrEmpty(hero.SpecializationIcon) ? null : $"icons/hero_specializations/{hero.SpecializationIcon}",
            FactionIcon: factionMapper.GetFactionIconPath(hero.Fraction),
            Attack: attack,
            Defence: defence,
            SpellPower: spellPower,
            Knowledge: knowledge,
            SpecializationName: string.IsNullOrEmpty(specializationName) ? null : specializationName,
            SpecializationDescription: string.IsNullOrEmpty(specializationDescription) ? null : specializationDescription,
            StartingArmy: startingArmy.Count > 0 ? startingArmy : null,
            StartingSkills: startingSkills.Count > 0 ? startingSkills : null,
            StartingSpells: startingSpells.Count > 0 ? startingSpells : null,
            Description: string.IsNullOrEmpty(description) ? null : description,
            Motto: string.IsNullOrEmpty(motto) ? null : motto,
            StatLabels: statLabels
        );
    }

    private static string? GetHeroName(LangIndex lang, string heroId)
    {
        var result = lang.ResolveText(heroId);
        if (!string.IsNullOrWhiteSpace(result) && result != heroId)
            return result;

        result = lang.ResolveText($"{heroId}_name");
        if (!string.IsNullOrWhiteSpace(result) && result != $"{heroId}_name")
            return result;

        return null;
    }

    private static string? TryResolveTextWithHeroContext(
        ITextResolver resolver,
        string sid,
        string locale,
        string? specializationSid)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return null;
        }

        try
        {
            var ctx = SearchService.CreateHeroResolutionContext(locale, specializationSid);

            var result = resolver.Resolve(sid, ctx, out _);

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
}
