using GameData.Indexing;
using GameData.Services;
using Localization.Indexing;
using Localization.Resolution;
using API.Contracts;
using API.Helpers;
using API.Models;
using API.Services;
using static API.Helpers.LocalizationHelper;
using static API.Helpers.IconPaths;

namespace API.Endpoints;

public static class UnitsEndpoints
{
    public static IEndpointRouteBuilder MapUnitsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/units")
            .WithTags("Units")
            ;

        group.MapGet("/", GetUnits)
            .WithName("GetUnits")
            .WithSummary("List all units")
            .WithDescription("Returns all units. Supports search filtering by ID, name, or faction.")
            .Produces<List<UnitListItemDto>>(200)
            .Produces<ErrorDto>(503);

        group.MapGet("/{id}", GetUnitById)
            .WithName("GetUnitById")
            .WithSummary("Get unit details")
            .WithDescription("Returns detailed information about a specific unit including stats, abilities, and localized text.")
            .Produces<UnitDetailDto>(200)
            .Produces<ErrorDto>(404)
            .Produces<ErrorDto>(503);

        return endpoints;
    }

    private static IResult GetUnits(
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
        var locale = gamePathService.CurrentLocale;
        var lang = data.Lang;

        IEnumerable<GameData.Indexing.DbIndex.UnitRecord> units = data.Units;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            var tierLabel = lang.ResolveText("label_unit_tier");

            units = units.Where(u =>
            {
                var tierText = !string.IsNullOrWhiteSpace(tierLabel) && tierLabel != "label_unit_tier"
                    ? $"{tierLabel} {u.Tier}"
                    : $"Tier {u.Tier}";

                return u.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    u.Fraction.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    factionMapper.MapFactionDisplay(u.Fraction)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                    u.Tier.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    tierText.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    GetLocalizedUnitName(resolver, u.Id, locale)?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            });
        }

        var unitsList = units
            .Select(u => UnitDtoBuilder.BuildListItem(u, resolver, locale, lang, factionMapper))
            .ToList();

        return Results.Ok(unitsList);
    }

    private static IResult GetUnitById(
        string id,
        IGameDataService dataService,
        FactionMapper factionMapper,
        IGamePathService gamePathService,
        IReferenceIndexService referenceService)
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
        var lang = data.Lang;

        var unit = data.Units.FirstOrDefault(u =>
            u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (unit is null)
        {
            return Results.NotFound(new ErrorDto(
                $"Unit '{id}' not found",
                "Check the unit ID and try again. Use GET /api/units to list available units."
            ));
        }

        var dto = MapToDetail(unit, resolver, locale, lang, referenceService, dataService, factionMapper);
        return Results.Ok(dto);
    }

    private static UnitDetailDto MapToDetail(
        GameData.Indexing.DbIndex.UnitRecord unit,
        ITextResolver resolver,
        string locale,
        LangIndex lang,
        IReferenceIndexService referenceService,
        IGameDataService dataService,
        FactionMapper factionMapper)
    {
        var localizedName = GetLocalizedUnitName(resolver, unit.Id, locale);
        var description = GetLocalizedUnitDescription(resolver, unit.Id, locale);
        var narrativeDescriptionSid = $"{unit.Id}_narrativeDescription";
        var narrativeDescription = TryResolveText(resolver, narrativeDescriptionSid, locale) ?? narrativeDescriptionSid;
        var factionDisplay = factionMapper.MapFactionDisplay(unit.Fraction);

        var stats = unit.Stats;

        AbilityDetailDto? creatureType = null;
        if (!string.IsNullOrWhiteSpace(unit.BaseClassNameSid))
        {
            var baseClassRef = new DbIndex.AbilityRef(
                NameSid: unit.BaseClassNameSid,
                DescriptionSid: unit.BaseClassDescSid,
                Icon: unit.BaseClassNameSid,
                AbilityTypeSid: "",
                IsActiveAbility: false,
                IsAlternativeAttack: false,
                Rank: null,
                Energy: null,
                ImmunitySids: Array.Empty<string>(),
                InfoDescriptionSids: Array.Empty<string>()
            );
            // Lookup variant ID from pre-aggregated abilities
            var baseClassVariantId = FindVariantIdForAbility(
                dataService.Data!.AggregatedAbilities,
                unit.BaseClassNameSid,
                unit.BaseClassDescSid,
                unit.Id,
                0,
                false,
                resolver,
                locale);
            var baseClassDto = AbilityDtoBuilder.BuildFromAbilityRef(baseClassRef, unit.Id, 0, resolver, lang, locale, baseClassVariantId);
            if (!string.IsNullOrWhiteSpace(baseClassDto.Name) && baseClassDto.Name != baseClassDto.Id)
            {
                creatureType = baseClassDto with { AbilityType = "BaseClass" };
            }
        }

        var passiveAbilities = new List<AbilityDetailDto>();
        for (int i = 0; i < unit.Passives.Count; i++)
        {
            var passive = unit.Passives[i];
            var passiveVariantId = FindVariantIdForAbility(
                dataService.Data!.AggregatedAbilities,
                passive.NameSid,
                passive.DescriptionSid,
                unit.Id,
                i,
                false,
                resolver,
                locale);
            var dto = AbilityDtoBuilder.BuildFromAbilityRef(passive, unit.Id, i, resolver, lang, locale, passiveVariantId);
            if (!string.IsNullOrWhiteSpace(dto.Name) && dto.Name != dto.NameSid)
            {
                passiveAbilities.Add(dto);
            }
        }

        var activeAbilities = new List<AbilityDetailDto>();
        for (int i = 0; i < unit.Abilities.Count; i++)
        {
            var ability = unit.Abilities[i];
            var abilityVariantId = FindVariantIdForAbility(
                dataService.Data!.AggregatedAbilities,
                ability.NameSid,
                ability.DescriptionSid,
                unit.Id,
                i,
                true,
                resolver,
                locale);
            var dto = AbilityDtoBuilder.BuildFromAbilityRef(ability, unit.Id, i, resolver, lang, locale, abilityVariantId);
            if (!string.IsNullOrWhiteSpace(dto.Name) && dto.Name != dto.NameSid)
            {
                activeAbilities.Add(dto);
            }
        }

        var costEntries = unit.Cost
            .Select(c => new UnitCostEntryDto(
                ResourceKey: c.ResourceKey,
                DisplayName: TryResolveText(resolver, $"{c.ResourceKey}_name", locale) ?? c.ResourceKey,
                Amount: c.Amount
            ))
            .ToList();

        var usedByHeroes = referenceService
            .GetReferencedBy(unit.Id, EntityType.Unit)
            .Where(r => r.EntityType == EntityType.Hero)
            .Where(r => {
                if (dataService.Data?.HeroesIndex.Heroes.TryGetValue(r.EntityId, out var hero) == true)
                {
                    return !hero.IsTutorialOrCampaignHero;
                }
                return false;
            })
            .Select(r => new UsedByHeroDto(
                HeroId: r.EntityId,
                HeroName: r.DisplayName ?? r.EntityId,
                IconPath: HeroLargePortrait(r.EntityId)
            ))
            .ToList();

        var statLabels = new UnitStatLabelsDto(
            Health: TryResolveText(resolver, "unit_health", locale) ?? "Health",
            Attack: TryResolveText(resolver, "unit_attack", locale) ?? "Attack",
            Defence: TryResolveText(resolver, "unit_defence", locale) ?? "Defence",
            Damage: TryResolveText(resolver, "unit_damage", locale) ?? "Damage",
            Initiative: TryResolveText(resolver, "unit_init", locale) ?? "Initiative",
            Speed: TryResolveText(resolver, "unit_speed", locale) ?? "Speed",
            Luck: TryResolveText(resolver, "unit_luck", locale) ?? "Luck",
            Morale: TryResolveText(resolver, "unit_moral", locale) ?? "Morale",
            SquadValue: TryResolveText(resolver, "label_squad_value", locale) ?? "Squad Value",
            ExpBonus: TryResolveText(resolver, "label_exp_bonus", locale) ?? "Exp Bonus",
            WeeklyGrowth: TryResolveText(resolver, "tutorial_C3_name", locale) ?? "Weekly Growth",
            Cost: TryResolveText(resolver, "label_cost", locale) ?? "Cost",
            Tier: TryResolveText(resolver, "unitWindowTier", locale) ?? "Tier: {0}",
            Faction: TryResolveText(resolver, "oe_label_faction", locale) ?? "Faction",
            CreatureStatsHeader: TryResolveText(resolver, "tutorial_M_30_name", locale) ?? "Stats",
            CreatureTypeHeader: TryResolveText(resolver, "label_creature_type", locale) ?? "Creature Type",
            PassiveAbilitiesHeader: TryResolveText(resolver, "passive_abilities", locale) ?? "Passive Abilities",
            ActiveAbilitiesHeader: TryResolveText(resolver, "active_abilities", locale) ?? "Active Abilities",
            HeroesWithUnitHeader: TryResolveText(resolver, "usedby_heroes_unit", locale) ?? "Heroes starting with this Unit"
        );

        return new UnitDetailDto(
            Id: unit.Id,
            Name: unit.BaseClassNameSid,
            LocalizedName: localizedName,
            Faction: string.IsNullOrEmpty(unit.Fraction) ? null : unit.Fraction,
            FactionDisplay: factionDisplay,
            Tier: unit.Tier > 0 ? unit.Tier : null,
            IconPath: UnitHexPortrait(unit.Id),
            FactionIcon: factionMapper.GetFactionIconPath(unit.Fraction),
            Attack: TryGetStatInt(stats, "attack"),
            Defense: TryGetStatInt(stats, "defence", "defense"),
            MinDamage: TryGetStatInt(stats, "damageMin", "min", "damage_min"),
            MaxDamage: TryGetStatInt(stats, "damageMax", "max", "damage_max"),
            Health: TryGetStatInt(stats, "health", "hp"),
            Speed: TryGetStatInt(stats, "speed"),
            Initiative: TryGetStatInt(stats, "initiative"),
            Growth: unit.Growth,
            Luck: TryGetStatInt(stats, "luck"),
            Morale: TryGetStatInt(stats, "morale"),
            SquadValue: TryGetStatInt(stats, "squadvalue", "squadValue"),
            ExpBonus: TryGetStatInt(stats, "expbonus", "expBonus"),
            Description: description,
            NarrativeDescription: narrativeDescription,
            CreatureType: creatureType,
            PassiveAbilities: passiveAbilities.Count > 0 ? passiveAbilities : null,
            ActiveAbilities: activeAbilities.Count > 0 ? activeAbilities : null,
            CostEntries: costEntries.Count > 0 ? costEntries : null,
            UsedByHeroes: usedByHeroes.Count > 0 ? usedByHeroes : null,
            StatLabels: statLabels
        );
    }

    private static string? GetLocalizedUnitDescription(ITextResolver resolver, string unitId, string locale)
    {
        var patterns = new[]
        {
            $"unit.{unitId}.description",
            $"unit_{unitId}_description",
            $"{unitId}_description",
            $"units.{unitId}.description"
        };

        foreach (var pattern in patterns)
        {
            var result = TryResolveText(resolver, pattern, locale);
            if (!string.IsNullOrWhiteSpace(result) && !result.StartsWith("{") && result != pattern)
                return result;
        }

        return null;
    }

    private static int? TryGetStatInt(IReadOnlyDictionary<string, string> stats, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (stats.TryGetValue(key, out var value) && int.TryParse(value, out var intValue))
            {
                return intValue;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the variant ID for an ability by matching its resolved description with pre-aggregated abilities.
    /// </summary>
    private static string? FindVariantIdForAbility(
        List<GameData.Indexing.AbilityAggregateResult> aggregatedAbilities,
        string? nameSid,
        string? descriptionSid,
        string currentUnitId,
        int abilityIndex,
        bool isActiveAbility,
        ITextResolver resolver,
        string locale)
    {
        if (string.IsNullOrWhiteSpace(nameSid))
            return nameSid;

        // Resolve the description for this specific ability
        var currentUnitCtx = new ResolutionContext(locale)
        {
            UnitId = currentUnitId,
            AbilityIndex = abilityIndex,
            IsActiveAbility = isActiveAbility
        };
        var resolvedDescToMatch = string.IsNullOrWhiteSpace(descriptionSid)
            ? ""
            : (resolver.Resolve(descriptionSid, currentUnitCtx, out _) ?? descriptionSid);

        // Find matching aggregate by NameSid and ResolvedDescription
        var matchingAggregate = aggregatedAbilities.FirstOrDefault(a =>
            a.Key.NameSid.Equals(nameSid, StringComparison.OrdinalIgnoreCase) &&
            a.Key.ResolvedDescription == resolvedDescToMatch &&
            a.SourceUnitIds.Contains(currentUnitId, StringComparer.OrdinalIgnoreCase));

        return matchingAggregate?.VariantId ?? nameSid;
    }

}
