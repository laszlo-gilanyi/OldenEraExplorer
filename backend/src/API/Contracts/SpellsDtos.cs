namespace API.Contracts;

/// Masterful spells sort separately if "Masterful" prefix is used. BaseNameForSort groups them with base spells.
public record SpellListItemDto(
    string Id,
    string Name,
    string? School,
    string? SchoolDisplay,
    string? SchoolTierText,
    int Rank,
    string Category,
    string? Icon,
    bool IsMasterful,
    string BaseNameForSort
);

/// Some spells don't have 4 levels. IsBonusSpell prevents showing empty level data.
public record SpellDetailDto(
    string Id,
    string Name,
    string? LocalizedName,
    string? School,
    string Category,
    string? Icon,
    string? SchoolTierText,
    string? ExceptionText,
    bool IsBonusSpell,
    IReadOnlyList<SpellLevelDto>? Levels,
    SkillReferenceDto? RelatedSkill
);

public record SkillReferenceDto(string Id, string Name, string? Icon);

public record SpellLevelDto(
    int Level,
    int ManaCost,
    string? Description,
    string? BonusDescription,
    int? StarDustCost
);
