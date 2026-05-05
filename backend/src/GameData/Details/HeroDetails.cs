using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GameData.Indexing;
using Localization.DbAccess;
using Localization.Indexing;
using Localization.Resolution;
using Localization.Scripting;

namespace GameData.Details;

public class HeroDetailsService
{
    private readonly DbAccessor _dbAccessor;
    private readonly ITextResolver _textResolver;
    private readonly ILogger<HeroDetailsService> _logger;

    public HeroDetailsService(
        DbAccessor dbAccessor,
        ITextResolver textResolver,
        ILogger<HeroDetailsService> logger)
    {
        _dbAccessor = dbAccessor ?? throw new ArgumentNullException(nameof(dbAccessor));
        _textResolver = textResolver ?? throw new ArgumentNullException(nameof(textResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<HeroDetailsResult> GetDetailsAsync(
        HeroesIndex.HeroRecord heroRecord,
        LangIndex lang,
        SkillsIndex? skillsIndex,
        SpellsIndex? spellsIndex,
        HeroSpecializationsIndex? heroSpecializationsIndex,
        string factionDisplay,
        string locale,
        bool placeholderResolverEnabled,
        CancellationToken cancellationToken = default)
    {
        if (heroRecord == null) throw new ArgumentNullException(nameof(heroRecord));
        if (lang == null) throw new ArgumentNullException(nameof(lang));
        if (string.IsNullOrWhiteSpace(locale)) throw new ArgumentException("Locale cannot be empty", nameof(locale));

        cancellationToken.ThrowIfCancellationRequested();

        var ctx = new ResolutionContext(locale)
        {
            HeroSpecializationId = heroRecord.SpecializationSid
        };

        var heroName = lang.ResolveText($"{heroRecord.HeroId}")
                       ?? lang.ResolveText($"{heroRecord.HeroId}_name")
                       ?? heroRecord.HeroId;

        cancellationToken.ThrowIfCancellationRequested();

        var classDisplay = ResolveClassDisplay(heroRecord, lang);
        var (attack, defence, spellPower, knowledge, luck, morale) = FormatStats(heroRecord);

        cancellationToken.ThrowIfCancellationRequested();

        var (specializationName, specializationDescription) = ResolveSpecialization(
            heroRecord, lang, ctx, placeholderResolverEnabled, heroSpecializationsIndex);

        cancellationToken.ThrowIfCancellationRequested();

        var startingArmy = BuildStartingArmy(heroRecord, lang);

        cancellationToken.ThrowIfCancellationRequested();

        var startingSkills = BuildStartingSkills(heroRecord, lang, skillsIndex, ctx);

        cancellationToken.ThrowIfCancellationRequested();

        var startingSpells = BuildStartingSpells(
            heroRecord, lang, spellsIndex, heroSpecializationsIndex, ctx);

        var description = lang.ResolveText($"{heroRecord.HeroId}_description") ?? "";
        var motto = lang.ResolveText($"{heroRecord.HeroId}_motto") ?? "";

        cancellationToken.ThrowIfCancellationRequested();

        var result = new HeroDetailsResult(
            HeroName: heroName,
            Icon: heroRecord.Icon,
            ClassType: heroRecord.ClassType,
            ClassDisplay: classDisplay,
            ClassIcon: heroRecord.ClassIcon,
            Faction: heroRecord.Fraction,
            FactionDisplay: factionDisplay,
            Attack: attack,
            Defence: defence,
            SpellPower: spellPower,
            Knowledge: knowledge,
            Luck: luck,
            Morale: morale,
            SpecializationName: specializationName,
            SpecializationDescription: specializationDescription,
            SpecializationIcon: heroRecord.SpecializationIcon,
            StartingArmy: startingArmy,
            StartingSkills: startingSkills,
            StartingSpells: startingSpells,
            Description: description,
            Motto: motto
        );

        return Task.FromResult(result);
    }

    private string ResolveClassDisplay(HeroesIndex.HeroRecord hero, LangIndex lang)
    {
        if (!string.IsNullOrWhiteSpace(hero.ClassType) && !string.IsNullOrWhiteSpace(hero.Fraction))
        {
            var classNameSid = $"{hero.ClassType}_{hero.Fraction}_name";
            var resolved = lang.ResolveText(classNameSid);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return hero.ClassType switch
        {
            "might" => "Might",
            "magic" => "Magic",
            _ => hero.ClassType
        };
    }

    private (string? Attack, string? Defence, string? SpellPower, string? Knowledge, string? Luck, string? Morale)
        FormatStats(HeroesIndex.HeroRecord hero)
    {
        var attack = hero.BaseStats.TryGetValue("offence", out var att) ? att.ToString() : null;
        var defence = hero.BaseStats.TryGetValue("defence", out var def) ? def.ToString() : null;
        var spellPower = hero.BaseStats.TryGetValue("spellPower", out var sp) ? sp.ToString() : null;
        var knowledge = hero.BaseStats.TryGetValue("intelligence", out var intel) ? intel.ToString() : null;
        var luck = hero.BaseStats.TryGetValue("luck", out var lck) ? lck.ToString() : null;
        var morale = hero.BaseStats.TryGetValue("moral", out var mor) ? mor.ToString() : null;

        return (attack, defence, spellPower, knowledge, luck, morale);
    }

    private (string Name, string Description) ResolveSpecialization(
        HeroesIndex.HeroRecord hero,
        LangIndex lang,
        ResolutionContext ctx,
        bool placeholderResolverEnabled,
        HeroSpecializationsIndex? heroSpecializationsIndex)
    {
        var specializationName = lang.ResolveText($"{hero.HeroId}_spec_name") ?? "";
        var descriptionSid = $"{hero.HeroId}_spec_description";

        if (heroSpecializationsIndex?.Specializations.TryGetValue(hero.SpecializationSid ?? "", out var specRecord) == true
            && !string.IsNullOrWhiteSpace(specRecord.DescSid))
        {
            descriptionSid = specRecord.DescSid;
        }

        var specializationDescription = placeholderResolverEnabled
            ? _textResolver.Resolve(descriptionSid, ctx, out _)
            : lang.ResolveText(descriptionSid) ?? "";

        return (specializationName, specializationDescription);
    }

    private List<StartingArmyEntry> BuildStartingArmy(
        HeroesIndex.HeroRecord hero,
        LangIndex lang)
    {
        var startingArmy = new List<StartingArmyEntry>();

        foreach (var unit in hero.StartSquad)
        {
            var unitName = lang.ResolveText($"{unit.Sid}_name") ?? unit.Sid;
            var countInterval = $"{unit.Min} - {unit.Max}";
            var unitIcon = unit.Sid;

            startingArmy.Add(new StartingArmyEntry(unit.Sid, unitName, countInterval, unitIcon));
        }

        return startingArmy;
    }

    private List<StartingSkillEntry> BuildStartingSkills(
        HeroesIndex.HeroRecord hero,
        LangIndex lang,
        SkillsIndex? skillsIndex,
        ResolutionContext ctx)
    {
        var startingSkills = new List<StartingSkillEntry>();

        foreach (var skill in hero.StartSkills)
        {
            string skillName = skill.Sid;
            string skillIcon = "";

            if (skillsIndex?.Skills.TryGetValue(skill.Sid, out var skillRecord) == true)
            {
                var levelIndex = skill.Level - 1;
                if (levelIndex >= 0 && levelIndex < skillRecord.LevelParams.Count)
                {
                    var levelParam = skillRecord.LevelParams[levelIndex];

                    skillName = _textResolver?.Resolve(levelParam.NameSid, ctx, out _)
                                ?? lang.ResolveText(levelParam.NameSid)
                                ?? skill.Sid;

                    skillIcon = levelParam.Icon ?? "";
                }
            }

            startingSkills.Add(new StartingSkillEntry(skill.Sid, skillName, skillIcon));
        }

        return startingSkills;
    }

    private List<StartingSpellEntry> BuildStartingSpells(
        HeroesIndex.HeroRecord hero,
        LangIndex lang,
        SpellsIndex? spellsIndex,
        HeroSpecializationsIndex? heroSpecializationsIndex,
        ResolutionContext ctx)
    {
        var effectiveSpells = hero.StartMagics.ToList();
        if (heroSpecializationsIndex != null && !string.IsNullOrWhiteSpace(hero.SpecializationSid))
        {
            effectiveSpells = heroSpecializationsIndex.ApplyReplacements(hero.SpecializationSid, effectiveSpells);
        }

        var startingSpells = new List<StartingSpellEntry>();

        foreach (var spellSid in effectiveSpells)
        {
            string spellName = spellSid;
            string spellIcon = "";
            bool isMasterful = spellSid.EndsWith("_special", StringComparison.OrdinalIgnoreCase);

            if (spellsIndex?.Spells.TryGetValue(spellSid, out var spellRecord) == true)
            {
                var spellCtx = new ResolutionContext(ctx.Locale) { MagicId = spellRecord.Id };
                spellName = _textResolver?.Resolve(spellRecord.NameSid, spellCtx, out _)
                            ?? lang.ResolveText(spellRecord.NameSid)
                            ?? spellSid;

                spellIcon = spellRecord.Icon;
            }

            startingSpells.Add(new StartingSpellEntry(spellSid, spellName, spellIcon, isMasterful));
        }

        return startingSpells;
    }
}

public sealed record HeroDetailsResult(
    string HeroName,
    string Icon,
    string ClassType,
    string ClassDisplay,
    string ClassIcon,
    string Faction,
    string FactionDisplay,
    string? Attack,
    string? Defence,
    string? SpellPower,
    string? Knowledge,
    string? Luck,
    string? Morale,
    string SpecializationName,
    string SpecializationDescription,
    string SpecializationIcon,
    List<StartingArmyEntry> StartingArmy,
    List<StartingSkillEntry> StartingSkills,
    List<StartingSpellEntry> StartingSpells,
    string Description,
    string Motto
);

public sealed record StartingArmyEntry(
    string UnitId,
    string UnitName,
    string CountInterval,
    string Icon);

public sealed record StartingSkillEntry(
    string SkillId,
    string SkillName,
    string Icon);

public sealed record StartingSpellEntry(
    string SpellId,
    string SpellName,
    string Icon,
    bool IsMasterful);
