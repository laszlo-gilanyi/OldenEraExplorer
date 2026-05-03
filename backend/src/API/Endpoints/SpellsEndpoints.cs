using GameData.Indexing;
using Localization.DbAccess;
using Localization.Indexing;
using Localization.Resolution;
using Localization.Services;
using API.Contracts;
using API.Services;
using static API.Helpers.LocalizationHelper;

namespace API.Endpoints;

public static class SpellsEndpoints
{
    public static IEndpointRouteBuilder MapSpellsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/spells")
            .WithTags("Spells")
            ;

        // GET /api/spells - List all spells
        group.MapGet("/", GetSpells)
            .WithName("GetSpells")
            .WithSummary("List all spells")
            .WithDescription("Returns all spells. Supports search filtering by ID, name, or school.")
            .Produces<List<SpellListItemDto>>(200)
            .Produces<ErrorDto>(503);

        // GET /api/spells/{id} - Get spell details
        group.MapGet("/{id}", GetSpellById)
            .WithName("GetSpellById")
            .WithSummary("Get spell details")
            .WithDescription("Returns detailed information about a specific spell including level descriptions and mana costs.")
            .Produces<SpellDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetSpells(
        IGameDataService dataService,
        IGamePathService gamePathService,
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
        var lang = data.Lang;
        var locale = gamePathService.CurrentLocale;

        var seenSpellNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<SpellsIndex.SpellRecord> spells = data.SpellsIndex.Spells.Values
            .Where(s =>
            {
                // Filter 1: Skip spells without localization (e.g., kara_ punishment spells)
                var nameInLang = GetLocalizedSpellName(resolver, s.NameSid, locale);
                if (nameInLang == null)
                    return false;

                // Filter 2: Deduplicate by resolved name (e.g., bonus_magic_astral_summon variants)
                if (!seenSpellNames.Add(nameInLang))
                    return false;

                return true;
            });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            spells = spells.Where(s =>
            {
                if (s.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (GetLocalizedSpellName(resolver, s.NameSid, locale)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                    return true;

                if (s.School.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (GetSchoolDisplay(lang, s.School)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                    return true;

                if (s.Rank.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!string.IsNullOrEmpty(s.School))
                {
                    string schoolCapitalized = char.ToUpper(s.School[0]) + s.School.Substring(1);
                    string schoolTierSid = $"{schoolCapitalized}_magic_{s.Rank}_tier";

                    if (schoolTierSid.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var schoolTierText = lang.ResolveText(schoolTierSid);
                    if (!string.IsNullOrWhiteSpace(schoolTierText) &&
                        schoolTierText != schoolTierSid &&
                        schoolTierText.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                if (DetermineCategory(s, lang, locale).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            });
        }

        var spellsList = spells
            .Select(s => MapToListItem(s, resolver, lang, locale))
            .ToList();

        return Results.Ok(spellsList);
    }

    private static IResult GetSpellById(string id, IGameDataService dataService, IGamePathService gamePathService)
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
        var locale = gamePathService.CurrentLocale;

        var spell = data.SpellsIndex.Spells.Values.FirstOrDefault(s =>
            s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (spell is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Spell '{id}' not found",
                "Check the spell ID and try again. Use GET /api/spells to list available spells."
            ));
        }

        var dto = MapToDetail(spell, resolver, locale, data);
        return Results.Ok(dto);
    }

    private static SpellListItemDto MapToListItem(
        SpellsIndex.SpellRecord spell,
        ITextResolver resolver,
        LangIndex lang,
        string locale)
    {
        var localizedName = GetLocalizedSpellName(resolver, spell.NameSid, locale);
        var category = DetermineCategory(spell, lang, locale);
        var schoolDisplay = GetSchoolDisplay(lang, spell.School);
        var isMasterful = IsMasterfulSpell(spell);

        string? schoolTierText = null;
        if (!string.IsNullOrEmpty(spell.School))
        {
            string schoolCapitalized = char.ToUpper(spell.School[0]) + spell.School.Substring(1);
            string schoolTierSid = $"{schoolCapitalized}_magic_{spell.Rank}_tier";
            schoolTierText = lang.ResolveText(schoolTierSid);
        }

        var name = localizedName ?? spell.Id;
        var baseNameForSort = name;
        if (isMasterful)
        {
            int firstSpaceIndex = name.IndexOf(' ');
            if (firstSpaceIndex > 0 && firstSpaceIndex < name.Length - 1)
            {
                baseNameForSort = name.Substring(firstSpaceIndex + 1);
            }
        }

        return new SpellListItemDto(
            Id: spell.Id,
            Name: name,
            School: string.IsNullOrEmpty(spell.School) ? null : spell.School,
            SchoolDisplay: schoolDisplay,
            SchoolTierText: schoolTierText,
            Rank: spell.Rank,
            Category: category,
            Icon: string.IsNullOrEmpty(spell.Icon) ? null : $"icons/hero_magics/{spell.Icon}",
            IsMasterful: isMasterful,
            BaseNameForSort: baseNameForSort
        );
    }

    private static SpellDetailDto MapToDetail(
        SpellsIndex.SpellRecord spell,
        ITextResolver resolver,
        string locale,
        GameDataLoadResult data)
    {
        var localizedName = GetLocalizedSpellName(resolver, spell.NameSid, locale);
        var category = DetermineCategory(spell, data.Lang, locale);

        // Determine if this is a Bonus spell
        bool isBonusSpell = !spell.IsSpecialMagic
                         && !(spell.UsedOnMap && spell.SettingPerLevelsCount > 1)
                         && !(spell.HasBattleMagic && spell.DealersPerLevelsCount > 1);

        string? schoolTierText = null;
        if (!string.IsNullOrEmpty(spell.School))
        {
            string schoolCapitalized = char.ToUpper(spell.School[0]) + spell.School.Substring(1);
            string schoolTierSid = $"{schoolCapitalized}_magic_{spell.Rank}_tier";
            schoolTierText = TryResolveText(resolver, schoolTierSid, locale);
        }

        var levels = new List<SpellLevelDto>();

        // Try to get mana costs and descriptions from spell JSON via DbAccessor
        int[] manaCosts = new int[4];
        string?[] descriptions = new string?[4];
        string?[] bonusDescriptions = new string?[4];
        int?[] starDustCosts = new int?[4];

        if (data.DbAccessor.TryGetMagic(spell.Id, out var spellJson))
        {
            // learnCost → level 1 starDust cost; upgradeCost array → levels 2/3/4
            if (spellJson.TryGetProperty("learnCost", out var learnCostArr) &&
                learnCostArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var entry in learnCostArr.EnumerateArray())
                {
                    if (entry.TryGetProperty("name", out var resName) &&
                        resName.GetString() == "starDust" &&
                        entry.TryGetProperty("cost", out var resCost))
                    {
                        starDustCosts[0] = resCost.GetInt32();
                        break;
                    }
                }
            }

            if (spellJson.TryGetProperty("upgradeCost", out var upgradeCostArr) &&
                upgradeCostArr.ValueKind == System.Text.Json.JsonValueKind.Array &&
                starDustCosts[0].HasValue)
            {
                int arrLen = upgradeCostArr.GetArrayLength();
                if (arrLen > 0) starDustCosts[1] = upgradeCostArr[0].GetInt32();
                if (arrLen > 1) starDustCosts[2] = upgradeCostArr[1].GetInt32();
                if (arrLen > 2) starDustCosts[3] = upgradeCostArr[2].GetInt32();
            }
            if (spellJson.TryGetProperty("manaCost", out var manaCostArr) &&
                manaCostArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                int arrLen = manaCostArr.GetArrayLength();
                if (arrLen > 0) manaCosts[0] = manaCostArr[0].GetInt32();
                if (arrLen > 1) manaCosts[1] = manaCostArr[1].GetInt32();
                else manaCosts[1] = manaCosts[0];
                if (arrLen > 2) manaCosts[2] = manaCostArr[2].GetInt32();
                else manaCosts[2] = manaCosts[1];
                if (arrLen > 3) manaCosts[3] = manaCostArr[3].GetInt32();
                else manaCosts[3] = manaCosts[2];
            }

            var descSids = new List<string>();
            if (spellJson.TryGetProperty("description", out var descArr) &&
                descArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in descArr.EnumerateArray())
                {
                    descSids.Add(item.GetString() ?? "");
                }
            }

            for (int level = 1; level <= 4; level++)
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
                    descriptions[level - 1] = resolver.Resolve(descSid, ctx, out _);
                }
            }

            if (spellJson.TryGetProperty("bonusDescriptions", out var bonusArr) &&
                bonusArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var bonus in bonusArr.EnumerateArray())
                {
                    if (bonus.TryGetProperty("level", out var lvl) &&
                        bonus.TryGetProperty("description", out var desc))
                    {
                        int bonusLevel = lvl.GetInt32();
                        if (bonusLevel >= 1 && bonusLevel <= 4)
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
                                bonusDescriptions[bonusLevel - 1] = resolver.Resolve(bonusSid, ctx, out _);
                            }
                        }
                    }
                }
            }
        }

        int levelCount = isBonusSpell ? 1 : 4;
        for (int i = 0; i < levelCount; i++)
        {
            levels.Add(new SpellLevelDto(
                Level: i + 1,
                ManaCost: manaCosts[i],
                Description: descriptions[i],
                BonusDescription: bonusDescriptions[i],
                StarDustCost: starDustCosts[i]
            ));
        }

        string? exceptionText = null;
        if (data.DbAccessor.TryGetMagic(spell.Id, out var spellJsonForException))
        {
            if (spellJsonForException.TryGetProperty("excaptionInTooltip", out var exceptionSid) &&
                exceptionSid.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var sid = exceptionSid.GetString();
                if (!string.IsNullOrEmpty(sid))
                {
                    exceptionText = TryResolveText(resolver, sid, locale);
                }
            }
        }

        SkillReferenceDto? relatedSkill = null;
        if (data.SkillsIndex.SpellToSkill.TryGetValue(spell.Id, out var linkedSkillId) &&
            data.SkillsIndex.Skills.TryGetValue(linkedSkillId, out var skillRecord))
        {
            var skillName = TryResolveText(resolver, skillRecord.NameSid, locale) ?? skillRecord.SkillId;
            var skillIcon = skillRecord.LevelParams.Count > 0 && !string.IsNullOrEmpty(skillRecord.LevelParams[0].Icon)
                ? $"icons/hero_skills/{skillRecord.LevelParams[0].Icon}"
                : null;
            relatedSkill = new SkillReferenceDto(linkedSkillId, skillName, skillIcon);
        }

        return new SpellDetailDto(
            Id: spell.Id,
            Name: spell.NameSid,
            LocalizedName: localizedName,
            School: string.IsNullOrEmpty(spell.School) ? null : spell.School,
            Category: category,
            Icon: string.IsNullOrEmpty(spell.Icon) ? null : $"icons/hero_magics/{spell.Icon}",
            SchoolTierText: schoolTierText,
            ExceptionText: exceptionText,
            IsBonusSpell: isBonusSpell,
            Levels: levels.Count > 0 ? levels : null,
            RelatedSkill: relatedSkill
        );
    }

    private static string DetermineCategory(SpellsIndex.SpellRecord spell, LangIndex lang, string locale)
    {
        // Determine base category flags
        bool isSpecial = spell.IsSpecialMagic;
        bool isTrueWorldSpell = spell.UsedOnMap && spell.SettingPerLevelsCount > 1;
        bool isLeveledBattleSpell = spell.HasBattleMagic && spell.DealersPerLevelsCount > 1;
        bool isSkillRelated = !isSpecial && !isTrueWorldSpell && !isLeveledBattleSpell;

        // Localized text for "Combat Magic" (from game SID: tutorial_M_26_name)
        string combatMagicText = lang.ResolveText("tutorial_M_26_name") ?? "Combat Magic";

        // Localized text for "Global Map Magic" (from overlay)
        string globalMapMagicText = OverlayService.Instance.TryResolveFromOverlay("spell_category_global_map_magic", locale)
                                 ?? "Global Map Magic";

        if (isSpecial)
        {
            // Special spells: compose with type suffix
            string baseCategory = lang.ResolveText("Ability_type_special") ?? "Special";

            if (spell.HasBattleMagic)
                return $"{baseCategory} {combatMagicText}";
            if (spell.UsedOnMap)
                return $"{baseCategory} {globalMapMagicText}";
            return baseCategory;  // Fallback (shouldn't happen)
        }

        if (isSkillRelated)
        {
            // Skill Related spells: compose with type suffix (from overlay)
            string baseCategory = OverlayService.Instance.TryResolveFromOverlay("spell_category_skill_related", locale)
                               ?? "Skill Related";

            if (spell.HasBattleMagic)
                return $"{baseCategory} {combatMagicText}";
            if (spell.UsedOnMap)
                return $"{baseCategory} {globalMapMagicText}";
            return baseCategory;  // Fallback (shouldn't happen)
        }

        // True World spells (not Special, not Skill Related)
        if (spell.UsedOnMap)
            return globalMapMagicText;

        // Combat spells (not Special, not Skill Related, not World)
        return combatMagicText;
    }

    private static string? GetSchoolDisplay(LangIndex lang, string? school)
    {
        if (string.IsNullOrEmpty(school))
            return null;

        // School SID patterns - most schools use their name directly as SID
        var schoolSid = school.ToLower() switch
        {
            "neutral" => "world_cheat_dropdown_neutral",
            _ => school // "day", "night", "chaos", "order" use their name as SID
        };

        return lang.ResolveText(schoolSid);
    }

    private static bool IsMasterfulSpell(SpellsIndex.SpellRecord spell)
    {
        return spell.NameSid.EndsWith("_spec", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetLocalizedSpellName(ITextResolver resolver, string nameSid, string locale)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return null;

        return TryResolveText(resolver, nameSid, locale);
    }
}
