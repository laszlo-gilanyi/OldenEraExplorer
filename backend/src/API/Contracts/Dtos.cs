namespace API.Contracts;

/// Missing keys fall back to raw key to prevent UI breakage when localization is incomplete.
public record GameStatusDto(
    bool PathSet,
    bool DataLoaded,
    string? GameRoot,
    string? HeroesOeDataPath,
    string? StreamingAssetsPath,
    string CurrentLocale,
    string? Error,
    Dictionary<string, string>? UiLabels = null
);

public record DetectionResultDto(
    bool Success,
    string? SelectedGameRoot,
    string? HeroesOeDataPath,
    string? StreamingAssetsPath,
    IReadOnlyList<CandidateDto> Candidates
);

public record CandidateDto(
    string GameRoot,
    string? HeroesOeDataPath,
    string? StreamingAssetsPath,
    int Score,
    string DisplayName
);

public record SetPathRequest(string Path, string? Locale = null);

public record SetPathResultDto(
    bool Success,
    string? Error,
    string? GameRoot,
    string? HeroesOeDataPath,
    string? StreamingAssetsPath
);

public record BrowseResultDto(string Path);

public record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages
);

/// Game bundles contain models not in JSON files. Viewer-only fields enable 3D display without game data.
public record UnitListItemDto(
    string Id,
    string Name,
    string? Faction,
    string? FactionDisplay,
    int? Tier,
    string? IconPath,
    bool IsOrphan = false,
    float? Scale = null,
    string? PrefabPath = null
);

public record UnitDetailDto(
    string Id,
    string Name,
    string? LocalizedName,
    string? Faction,
    string? FactionDisplay,
    int? Tier,
    string? IconPath,
    string? FactionIcon,
    int? Attack,
    int? Defense,
    int? MinDamage,
    int? MaxDamage,
    int? Health,
    int? Speed,
    int? Initiative,
    int? Growth,
    int? Luck,
    int? Morale,
    int? SquadValue,
    int? ExpBonus,
    string? Description,
    string? NarrativeDescription,
    AbilityDetailDto? CreatureType,
    IReadOnlyList<AbilityDetailDto>? PassiveAbilities,
    IReadOnlyList<AbilityDetailDto>? ActiveAbilities,
    IReadOnlyList<UnitCostEntryDto>? CostEntries,
    IReadOnlyList<UsedByHeroDto>? UsedByHeroes,
    UnitStatLabelsDto? StatLabels
);

public record UnitCostEntryDto(
    string ResourceKey,
    string DisplayName,
    int Amount
);

public record UsedByHeroDto(
    string HeroId,
    string HeroName,
    string? IconPath
);

public record UnitStatLabelsDto(
    string Health,
    string Attack,
    string Defence,
    string Damage,
    string Initiative,
    string Speed,
    string Luck,
    string Morale,
    string SquadValue,
    string ExpBonus,
    string WeeklyGrowth,
    string Cost,
    string Tier,
    string Faction,
    string CreatureStatsHeader,
    string CreatureTypeHeader,
    string PassiveAbilitiesHeader,
    string ActiveAbilitiesHeader,
    string HeroesWithUnitHeader
);

public record ErrorDto(string Error, string? Detail = null);

public record MessageDto(string Message);

public record ExtractedModelDto(
    string Id,
    string Type,
    long SizeBytes,
    DateTime LastExtractedUtc
);

public record ModelStatusDto(
    bool ExistsOnDisk,
    long? SizeBytes,
    DateTime? LastExtractedUtc
);

/// Game doesn't provide icon paths for map objects. Derived from PrefabPath by convention.
/// Filter metadata (BankType, HasGuards, RewardTypes) enables frontend filtering without detail queries.
public record MapObjectListItemDto(
    string Id,
    string Name,
    string? Category,
    string? Icon,
    bool IsOrphan = false,
    string? PrefabPath = null,
    string? BankType = null,
    bool? HasGuards = null,
    IReadOnlyList<string>? RewardTypes = null
);

public record MapObjectDetailDto(
    string Id,
    string Name,
    string? Description,
    string? NarrativeDescription,
    string? Icon,
    CreatureBankInfoDto? CreatureBankInfo
);

public record CreatureBankInfoDto(
    bool HasGuards,
    string VisitType,
    List<CreatureBankVariantInfoDto> Variants,
    bool IsBarracks = false,
    List<DifficultyLevelDto>? DifficultyLevels = null,
    string? DifficultyLabel = null,
    string? GuardsLabel = null,
    string? BankType = null
);

public record CreatureBankVariantInfoDto(
    double RollChance,
    int Value,
    int? CustomGuardValue,
    List<GuardUnitInfoDto> Guards,
    CategorizedRewardsDto Rewards,
    string? RewardApplyType = null,
    List<CategorizedRewardsDto>? RewardOptions = null
);

public record GuardUnitInfoDto(
    string UnitId,
    string UnitName,
    int Amount,
    string Icon,
    int? MinAmount = null,
    int? MaxAmount = null
);

/// <summary>
/// Pre-categorized rewards for a creature bank variant.
/// All parsing is done server-side - frontend just displays.
/// </summary>
public record CategorizedRewardsDto(
    List<ResourceRewardEntryDto> Resources,
    List<ArtifactRarityPoolDto> ArtifactPools,
    List<SpellPoolOptionDto> SpellPools,
    List<GuardUnitInfoDto> Units,
    int? Experience
);

public record ResourceRewardEntryDto(
    string ResourceKey,
    string DisplayName,
    int Amount
);

public record ArtifactRarityPoolDto(
    string Rarity,
    string RarityLabel,
    int Draws,
    List<ArtifactPoolGroupDto> Groups
);

public record ArtifactPoolGroupDto(
    string GroupName,
    string Rarity,
    int Count,
    double Percentage,
    List<ArtifactPoolItemDto> Artifacts
);

public record ArtifactPoolItemDto(
    string Id,
    string Name,
    string Rarity,
    string Icon
);

/// <summary>
/// A spell pool option that the player can choose.
/// Each option contains multiple tier groups with weighted chances.
/// Example: Option 1 might have Tier 1 (60%) and Tier 2 (40%).
/// </summary>
public record SpellPoolOptionDto(
    List<SpellPoolGroupDto> Groups
);

public record SpellPoolGroupDto(
    string TierName,
    int Tier,
    double Weight,
    int Count,
    List<SpellPoolItemDto> Spells
);

public record SpellPoolItemDto(
    string Id,
    string Name,
    int Rank,
    string Icon
);

public record DifficultyLevelDto(
    string Name,
    double Power,
    string Icon,
    string? Tooltip = null
);

public record ArtifactListItemDto(
    string Id,
    string Name,
    string? Rarity,
    string? Slot,
    string? RaritySlotText,
    string? Icon,
    bool IsOrphan = false,
    string? PrefabPath = null
);

public record ArtifactDetailDto(
    string Id,
    string Name,
    string? LocalizedName,
    string? Icon,
    string? Rarity,
    string? Slot,
    string? SlotIcon,
    string? RaritySlotText,
    string? Description,
    string? NarrativeDescription,
    string? UpgradeDescription,
    string? UpgradeCost,
    string? UpgradeCostNote,
    ArtifactSetBonusDto? SetBonus
);

public record ArtifactSetBonusDto(
    string SetName,
    IReadOnlyList<SetBonusEntryDto> Bonuses,
    IReadOnlyList<SetItemEntryDto> SetItems
);

public record SetBonusEntryDto(
    string Header,
    string Effect
);

public record SetItemEntryDto(
    string ArtifactId,
    string Name,
    string? Icon,
    string Slot
);

/// Building levels are separate list items. Compound ID enables distinct URLs per level.
public record BuildingListItemDto(
    string Id,
    string Name,
    string? Faction,
    string? FactionDisplay,
    int Level,
    int MaxLevel,
    string? IconPath,
    string? Category
);

public record BuildingDetailDto(
    string Id,
    string Name,
    string? Faction,
    string? FactionDisplay,
    string? FactionIcon,
    string? Description,
    string? IconPath,
    IReadOnlyList<BuildingCostDto>? Costs,
    IReadOnlyList<BuildingEffectDto>? Effects,
    IReadOnlyList<BuildingRequirementDto>? Requirements,
    IReadOnlyList<RecruitableUnitDto>? RecruitableUnits
);

public record BuildingCostDto(
    string ResourceName,
    int Amount
);

public record BuildingEffectDto(
    string Description,
    string? IconPath
);

public record BuildingRequirementDto(
    string BuildingName,
    string BuildingId
);

public record RecruitableUnitDto(
    string UnitId,
    string UnitName
);
