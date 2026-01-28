using API.Contracts;
using GameData.Indexing;
using GameData.Services;
using Localization.Indexing;
using Localization.Resolution;
using static API.Helpers.LocalizationHelper;
using static API.Helpers.IconPaths;

namespace API.Services;

public static class UnitDtoBuilder
{
    public static UnitListItemDto BuildListItem(
        DbIndex.UnitRecord unit,
        ITextResolver resolver,
        string locale,
        LangIndex lang,
        FactionMapper factionMapper)
    {
        var localizedName = GetLocalizedUnitName(resolver, unit.Id, locale);

        return new UnitListItemDto(
            Id: unit.Id,
            Name: localizedName ?? unit.Id,
            Faction: string.IsNullOrEmpty(unit.Fraction) ? null : unit.Fraction,
            FactionDisplay: factionMapper.MapFactionDisplay(unit.Fraction),
            Tier: unit.Tier > 0 ? unit.Tier : null,
            IconPath: UnitHexPortrait(unit.Id)
        );
    }

    public static GuardUnitInfoDto BuildGuardInfo(
        string unitId,
        int amount,
        List<DbIndex.UnitRecord>? units,
        ITextResolver resolver,
        string locale,
        bool applyDifficultyModifier = false)
    {
        var unit = units?.FirstOrDefault(u =>
            u.Id.Equals(unitId, StringComparison.OrdinalIgnoreCase));

        var unitName = unit != null
            ? GetLocalizedUnitName(resolver, unitId, locale) ?? unitId
            : resolver.Resolve($"{unitId}_name", new ResolutionContext(locale), out _) ?? unitId;

        int? minAmount = null;
        int? maxAmount = null;
        if (applyDifficultyModifier)
        {
            // Min: 0.50x (easy), Max: 2.00x (hell) - from difficulties_lobby.json
            minAmount = Math.Max(1, (int)(amount * 0.50));
            maxAmount = Math.Max(1, (int)(amount * 2.00));
        }

        return new GuardUnitInfoDto(
            unitId,
            unitName,
            amount,
            UnitHexPortrait(unitId),
            minAmount,
            maxAmount
        );
    }

    public static GuardUnitInfoDto? BuildUnitReward(
        List<string> parameters,
        List<DbIndex.UnitRecord>? units,
        ITextResolver resolver,
        string locale)
    {
        if (parameters.Count < 2) return null;

        var unitId = parameters[0];
        if (!int.TryParse(parameters[1], out var amount)) return null;

        return BuildGuardInfo(unitId, amount, units, resolver, locale, applyDifficultyModifier: false);
    }
}
