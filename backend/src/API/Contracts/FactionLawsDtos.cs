namespace API.Contracts;

public record FactionLawListItemDto(
    string Id,
    string Name,
    string? Faction,
    string? FactionDisplay,
    string? Icon
);

public record FactionLawDetailDto(
    string Id,
    string Name,
    string? LocalizedName,
    string? Faction,
    string? FactionDisplay,
    string? FactionIcon,
    string? Icon,
    IReadOnlyList<FactionLawLevelDto>? Levels,
    FactionLawStatLabelsDto? StatLabels = null,
    IReadOnlyList<FactionLawLineDto>? Layout = null
);

public record FactionLawLineDto(
    int CountToUnlock,
    IReadOnlyList<FactionLawGroupDto> Groups
);

public record FactionLawGroupDto(
    IReadOnlyList<FactionLawLayoutEntryDto> Laws
);

public record FactionLawLayoutEntryDto(
    string Id,
    string? Name,
    string? Icon,
    int LevelCount
);

public record FactionLawStatLabelsDto(
    string Cost
);

public record FactionLawLevelDto(
    int Level,
    int Cost,
    string? Description
);
