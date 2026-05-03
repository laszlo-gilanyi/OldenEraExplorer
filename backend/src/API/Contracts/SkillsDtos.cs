namespace API.Contracts;

public record SkillListItemDto(
    string Id,
    string Name,
    string? Icon
);

public record SkillDetailDto(
    string Id,
    string? SkillType,
    SkillLevelDto? Level1,
    SkillLevelDto? Level2,
    SkillLevelDto? Level3,
    SkillStatLabelsDto? StatLabels = null
);

public record SkillStatLabelsDto(
    string HeroesStartingWithSkill
);

public record SkillLevelDto(
    string LevelName,
    string? Description,
    string? Icon,
    IReadOnlyList<SubSkillDto> SubSkillChoices
);

public record SubSkillDto(
    string Id,
    string Name,
    string? Description,
    string? Icon,
    SpellLinkDto? GrantedSpell = null,
    BattleAbilityLinkDto? GrantedBattleAbility = null
);

public record SpellLinkDto(string Id, string Name, string? Icon);

public record BattleAbilityLinkDto(string Id, string Name, string? Icon, string? Description = null);
