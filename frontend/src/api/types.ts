// Game status types
export interface GameStatusDto {
  pathSet: boolean;
  dataLoaded: boolean;
  gameRoot: string | null;
  heroesOeDataPath: string | null;
  streamingAssetsPath: string | null;
  currentLocale: string;
  error: string | null;
  uiLabels: Record<string, string> | null;
}

// Unit types
export interface UnitListItemDto {
  id: string;
  name: string;
  faction: string | null;
  factionDisplay: string | null;
  tier: number | null;
  iconPath: string | null;
  isOrphan?: boolean;
  scale?: number | null;
  prefabPath?: string | null;
}

export interface UnitDetailDto {
  id: string;
  name: string;
  localizedName: string | null;
  faction: string | null;
  factionDisplay: string | null;
  tier: number | null;
  iconPath: string | null;
  factionIcon: string | null;
  attack: number | null;
  defense: number | null;
  minDamage: number | null;
  maxDamage: number | null;
  health: number | null;
  speed: number | null;
  initiative: number | null;
  growth: number | null;
  luck: number | null;
  morale: number | null;
  squadValue: number | null;
  expBonus: number | null;
  description: string | null;
  narrativeDescription: string | null;
  creatureType: AbilityDetailDto | null;
  passiveAbilities: AbilityDetailDto[] | null;
  activeAbilities: AbilityDetailDto[] | null;
  costEntries: UnitCostEntryDto[] | null;
  usedByHeroes: UsedByHeroDto[] | null;
  statLabels: UnitStatLabelsDto | null;
}

export interface UnitCostEntryDto {
  resourceKey: string;
  displayName: string;
  amount: number;
}

export interface UsedByHeroDto {
  heroId: string;
  heroName: string;
  iconPath: string | null;
}

export interface UnitStatLabelsDto {
  health: string;
  attack: string;
  defence: string;
  damage: string;
  initiative: string;
  speed: string;
  luck: string;
  morale: string;
  squadValue: string;
  expBonus: string;
  weeklyGrowth: string;
  cost: string;
  tier: string;
  faction: string;
  creatureStatsHeader: string;
  creatureTypeHeader: string;
  passiveAbilitiesHeader: string;
  activeAbilitiesHeader: string;
  heroesWithUnitHeader: string;
}

// Pagination types
export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

// Model Catalog types
export interface ExtractedModelDto {
  id: string;
  type: 'unit' | 'map-object';
  sizeBytes: number;
  lastExtractedUtc: string;
}

export interface ModelStatusDto {
  existsOnDisk: boolean;
  sizeBytes: number | null;
  lastExtractedUtc: string | null;
}

// Game Detection types
export interface DetectionResultDto {
  success: boolean;
  selectedGameRoot: string | null;
  heroesOeDataPath: string | null;
  streamingAssetsPath: string | null;
  candidates: CandidateDto[];
}

export interface CandidateDto {
  gameRoot: string;
  heroesOeDataPath: string | null;
  streamingAssetsPath: string | null;
  score: number;
  displayName: string;
}

export interface SetPathResultDto {
  success: boolean;
  error: string | null;
  gameRoot: string | null;
  heroesOeDataPath: string | null;
  streamingAssetsPath: string | null;
}

// Filesystem types (folder picker)
export interface FilesystemRootsDto {
  roots: FilesystemRootDto[];
}

export interface FilesystemRootDto {
  id: string;
  name: string;
  icon: string;
}

export interface DirectoryListingDto {
  current: string;
  parent: string | null;
  entries: DirectoryEntryDto[];
}

export interface DirectoryEntryDto {
  id: string;
  name: string;
  type: string;
  date: string;
}

// Spell types
export interface SpellListItemDto {
  id: string;
  name: string;
  school: string | null;
  schoolDisplay: string | null;
  schoolTierText: string | null;
  rank: number;
  category: string;
  icon: string | null;
  isMasterful: boolean;
  baseNameForSort: string;
}

export interface SpellDetailDto {
  id: string;
  name: string;
  localizedName: string | null;
  school: string | null;
  category: string;
  icon: string | null;
  schoolTierText: string | null;
  exceptionText: string | null;
  isBonusSpell: boolean;
  levels: SpellLevelDto[] | null;
}

export interface SpellLevelDto {
  level: number;
  manaCost: number;
  description: string | null;
  bonusDescription: string | null;
}

// Skill types
export interface SkillListItemDto {
  id: string;
  name: string;
  icon: string | null;
}

export interface SkillDetailDto {
  id: string;
  skillType: string | null;
  level1: SkillLevelDto | null;
  level2: SkillLevelDto | null;
  level3: SkillLevelDto | null;
  statLabels?: SkillStatLabelsDto;
}

export interface SkillStatLabelsDto {
  heroesStartingWithSkill: string;
}

export interface SkillLevelDto {
  levelName: string;
  description: string | null;
  icon: string | null;
  subSkillChoices: SubSkillDto[];
}

export interface SubSkillDto {
  id: string;
  name: string;
  description: string | null;
  icon: string | null;
}

// Map Object types
export interface MapObjectListItemDto {
  id: string;
  name: string;
  category: string | null;
  icon: string | null;
  isOrphan?: boolean;
  prefabPath?: string | null;
  bankType?: string | null;
  hasGuards?: boolean | null;
  rewardTypes?: string[] | null;
}

export interface MapObjectDetailDto {
  id: string;
  name: string;
  description: string | null;
  narrativeDescription: string | null;
  icon: string | null;
  creatureBankInfo: CreatureBankInfoDto | null;
}

export interface CreatureBankInfoDto {
  hasGuards: boolean;
  visitType: string;
  variants: CreatureBankVariantInfoDto[];
  isBarracks?: boolean;
  difficultyLevels?: DifficultyLevelDto[] | null;
  difficultyLabel?: string | null;
  guardsLabel?: string | null;
  bankType?: string | null;
}

export interface CreatureBankVariantInfoDto {
  rollChance: number; // Relative probability (0-100%)
  value: number;
  customGuardValue: number | null;
  guards: GuardUnitInfoDto[];
  rewards: CategorizedRewardsDto;
  rewardApplyType?: string | null;
  rewardOptions?: CategorizedRewardsDto[] | null;
}

export interface GuardUnitInfoDto {
  unitId: string;
  unitName: string;
  amount: number;
  icon: string;
  minAmount?: number | null;
  maxAmount?: number | null;
}

// All parsing is done server-side - frontend just displays.
export interface CategorizedRewardsDto {
  resources: ResourceRewardEntryDto[];
  artifactPools: ArtifactRarityPoolDto[];
  spellPools: SpellPoolOptionDto[];
  units: GuardUnitInfoDto[];
  experience: number | null;
  cursePools?: CursePoolDto[] | null;
}

export interface ResourceRewardEntryDto {
  resourceKey: string;
  displayName: string;
  amount: number;
}

export interface ArtifactRarityPoolDto {
  rarity: string;
  rarityLabel: string;
  draws: number;
  groups: ArtifactPoolGroupDto[];
}

export interface ArtifactPoolGroupDto {
  groupName: string;
  rarity: string;
  count: number;
  percentage: number;
  artifacts: ArtifactPoolItemDto[];
}

export interface ArtifactPoolItemDto {
  id: string;
  name: string;
  rarity: string;
  icon: string;
}

export interface SpellPoolOptionDto {
  groups: SpellPoolGroupDto[];
}

export interface SpellPoolGroupDto {
  tierName: string;
  tier: number;
  weight: number;
  count: number;
  spells: SpellPoolItemDto[];
}

export interface SpellPoolItemDto {
  id: string;
  name: string;
  rank: number;
  icon: string;
}

export interface DifficultyLevelDto {
  name: string;
  power: number;
  icon: string;
  tooltip?: string | null;
}

// Used for map objects like Hero's Crypt that apply debuffs along with rewards.
export interface CursePoolDto {
  title: string;
  curses: CurseInfoDto[];
  durationDays: number;
}

// Name and description may be null if not localized (e.g., for invisible curses).
export interface CurseInfoDto {
  id: string;
  name: string | null;
  description: string | null;
  effects: CurseEffectDto[];
}

export interface CurseEffectDto {
  stat: string;
  statDisplayName: string;
  modifier: string;
}

// Ability types
export interface AbilityListItemDto {
  id: string;
  name: string;
  abilityType: string;
  icon: string | null;
}

export interface AbilityDetailDto {
  id: string;
  name: string;
  nameSid: string | null;
  abilityType: string;
  description: string | null;
  rank: number | null;
  energyCost: number | null;
  abilityTypeSid: string | null;
  immunities: string[] | null;
  infoNotes: string[] | null;
  icon: string | null;
  sourceUnitIds?: string[];
  sourceUnitNames?: string[];
  statLabels?: AbilityStatLabelsDto;
}

export interface AbilityStatLabelsDto {
  creaturesWithAbility: string;
}

// Faction Law types
export interface FactionLawListItemDto {
  id: string;
  name: string;
  faction: string | null;
  factionDisplay: string | null;
  icon: string | null;
}

export interface FactionLawDetailDto {
  id: string;
  name: string;
  localizedName: string | null;
  faction: string | null;
  factionDisplay: string | null;
  factionIcon: string | null;
  icon: string | null;
  levels: FactionLawLevelDto[] | null;
  statLabels?: FactionLawStatLabelsDto;
}

export interface FactionLawStatLabelsDto {
  cost: string;
}

export interface FactionLawLevelDto {
  level: number;
  cost: number;
  description: string | null;
}

// Hero types
export interface HeroListItemDto {
  id: string;
  name: string;
  faction: string | null;
  factionDisplay: string | null;
  classType: string | null;
  classDisplay: string | null;
  iconPath: string | null;
}

export interface HeroDetailDto {
  id: string;
  name: string;
  faction: string | null;
  factionDisplay: string | null;
  classType: string | null;
  classDisplay: string | null;
  iconPath: string | null;
  classIcon: string | null;
  specializationIcon: string | null;
  factionIcon: string | null;
  // Stats (only 4 main stats - Luck and Morale are never displayed for heroes)
  attack: string | null;
  defence: string | null;
  spellPower: string | null;
  knowledge: string | null;
  // Specialization
  specializationName: string | null;
  specializationDescription: string | null;
  // Starting loadout
  startingArmy: StartingArmyDto[] | null;
  startingSkills: StartingSkillDto[] | null;
  startingSpells: StartingSpellDto[] | null;
  // Flavor
  description: string | null;
  motto: string | null;
  // Localized UI labels
  statLabels: HeroStatLabelsDto | null;
}

export interface StartingArmyDto {
  unitId: string;
  unitName: string;
  countInterval: string;
  icon: string | null;
}

export interface StartingSkillDto {
  skillId: string;
  skillName: string;
  icon: string | null;
}

export interface StartingSpellDto {
  spellId: string;
  spellName: string;
  icon: string | null;
  isMasterful: boolean;
}

export interface HeroStatLabelsDto {
  startingArmy: string;
  startingSkills: string;
  startingSpells: string;
  biography: string;
  motto: string;
}

// Subclass types
export interface SubclassListItemDto {
  id: string;
  name: string;
  faction: string | null;
  factionDisplay: string | null;
  classType: string | null;
  classDisplay: string | null;
  icon: string | null;
}

export interface SubclassDetailDto {
  id: string;
  name: string;
  description: string | null;
  faction: string | null;
  factionDisplay: string | null;
  factionIcon: string | null;
  classType: string | null;
  classDisplay: string | null;
  classIcon: string | null;
  icon: string | null;
  requiredSkills: RequiredSkillDto[];
  statLabels?: SubclassStatLabelsDto;
}

export interface SubclassStatLabelsDto {
  requiredSkills: string;
}

export interface RequiredSkillDto {
  skillId: string;
  skillName: string;
  icon: string | null;
}

// Error type
export interface ErrorDto {
  error: string;
  detail?: string;
}

// Artifact types
export interface ArtifactListItemDto {
  id: string;
  name: string;
  rarity: string | null;
  slot: string | null;
  raritySlotText: string | null;
  icon: string | null;
  isOrphan?: boolean;
  prefabPath?: string | null;
}

export interface ArtifactDetailDto {
  id: string;
  name: string;
  localizedName: string | null;
  icon: string | null;
  rarity: string | null;
  slot: string | null;
  slotIcon: string | null;
  raritySlotText: string | null;
  description: string | null;
  narrativeDescription: string | null;
  upgradeDescription: string | null;
  upgradeCost: string | null;
  upgradeCostNote: string | null;
  setBonus: ArtifactSetBonusDto | null;
}

export interface ArtifactSetBonusDto {
  setName: string;
  bonuses: SetBonusEntryDto[];
  setItems: SetItemEntryDto[];
}

export interface SetBonusEntryDto {
  header: string;
  effect: string;
}

export interface SetItemEntryDto {
  artifactId: string;
  name: string;
  icon: string | null;
  slot: string;
}

// Building types
export interface BuildingListItemDto {
  id: string;
  name: string;
  faction: string | null;
  factionDisplay: string | null;
  level: number;
  maxLevel: number;
  iconPath: string | null;
  category: string | null;
}

export interface BuildingDetailDto {
  id: string;
  name: string;
  faction: string | null;
  factionDisplay: string | null;
  factionIcon: string | null;
  description: string | null;
  iconPath: string | null;
  costs: BuildingCostDto[] | null;
  effects: BuildingEffectDto[] | null;
  requirements: BuildingRequirementDto[] | null;
  recruitableUnits: RecruitableUnitDto[] | null;
}

export interface BuildingCostDto {
  resourceName: string;
  amount: number;
}

export interface BuildingEffectDto {
  description: string;
  iconPath: string | null;
}

export interface BuildingRequirementDto {
  buildingName: string;
  buildingId: string;
}

export interface RecruitableUnitDto {
  unitId: string;
  unitName: string;
}

// Search types
export type EntityType =
  | 'Unit'
  | 'Hero'
  | 'Skill'
  | 'SubSkill'
  | 'Spell'
  | 'Artifact'
  | 'Building'
  | 'MapObject'
  | 'FactionLaw'
  | 'Ability'
  | 'Subclass';

export interface SearchResultDto {
  id: string;
  type: EntityType;
  name: string;
  matchedText: string | null;
  iconPath: string | null;
  matchLocation: 'Sidebar' | 'Detail';
}

export interface SearchResponse {
  query: string;
  results: SearchResultDto[];
  totalResults: number;
}

// Settings types
export interface SettingsDto {
  theme: string;
  locale: string;
  usePlaceholderResolver: boolean;
  showResolverOutput: boolean;
  autoExtractEnabled: boolean;
  extractPng: boolean;
  extractGlb: boolean;
}

export interface UpdateSettingsRequest {
  theme?: string;
  locale?: string;
  usePlaceholderResolver?: boolean;
  showResolverOutput?: boolean;
  autoExtractEnabled?: boolean;
  extractPng?: boolean;
  extractGlb?: boolean;
}

export interface LocalesDto {
  locales: string[];
  currentLocale: string;
}

// Cross-entity reference types
export interface EntityReferenceDto {
  entityId: string;
  entityType: string;
  displayName: string | null;
  propertyPath: string;
}

export interface EntityReferencesResponse {
  entityId: string;
  entityType: string;
  referencedBy: EntityReferenceDto[];
}

// Labels types
export interface LabelsDto {
  columns: ColumnLabelsDto;
  overlay: Record<string, string>;
}

export interface ColumnLabelsDto {
  name: string;
  tier: string;
  faction: string;
  id: string;
  category: string;
  school: string;
  level: string;
  class: string;
}
