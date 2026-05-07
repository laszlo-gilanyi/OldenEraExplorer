import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useMapObjectsStore, type MapObjectSortField } from './mapObjectsStore';
import { useMapObjects, useMapObject } from './useMapObjects';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import { useOnClickOutside } from '@/hooks/useOnClickOutside';
import RichText from '@/components/display/RichText';
import type { MapObjectListItemDto, MapObjectDetailDto, CreatureBankInfoDto, DifficultyLevelDto, CategorizedRewardsDto, CreatureBankVariantInfoDto, ArtifactRarityPoolDto, ArtifactPoolGroupDto } from '@/api/types';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import SortableColumnHeader, { type SortDirection } from '@/components/display/SortableColumnHeader';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import CurrencyBadge from '@/components/display/CurrencyBadge';
import UnitHexCard from '@/components/display/UnitHexCard';
import DetailContainer, { FULL_WIDTH_CARD } from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';

const DROPDOWN_CONFIG = {
  artifact: { width: 400, className: 'w-100 max-h-75 grid grid-cols-4 gap-2' },
  spell: { width: 500, className: 'w-125 max-h-100 space-y-3' },
} as const;

const rarityColorClasses: Record<string, string> = {
  common: 'text-rarity-common',
  uncommon: 'text-rarity-uncommon',
  rare: 'text-rarity-rare',
  epic: 'text-rarity-epic',
  legendary: 'text-rarity-legendary',
};

const getRarityColorClass = (rarity: string | null) =>
  rarity ? rarityColorClasses[rarity.toLowerCase()] ?? 'text-muted-foreground' : 'text-muted-foreground';

const getCurseRarityFromTitle = (title: string): string | null => {
  const titleLower = title.toLowerCase();
  if (titleLower.includes('common')) return 'common';
  if (titleLower.includes('powerful')) return 'rare';
  if (titleLower.includes('overwhelming')) return 'epic';
  if (titleLower.includes('unbearable')) return 'legendary';
  return null;
};

const isPercentageStat = (statId: string): boolean => {
  return statId.includes('Percent') ||
         statId.includes('Bonus') ||
         statId === 'MovementPerBonus' ||
         statId === 'ManaRestoreBonusPercent';
};

const formatCurseModifier = (modifier: string, statId: string): string => {
  if (!isPercentageStat(statId)) {
    return modifier;
  }

  const numValue = parseFloat(modifier);
  if (isNaN(numValue)) {
    return modifier;
  }

  const percentage = numValue * 100;
  return `${percentage > 0 ? '+' : ''}${percentage.toFixed(0)}%`;
};

const getCurseLocalizationKey = (statId: string): string | null => {
  const statToCurseKey: Record<string, string> = {
    'viewRadius': 'viewRadius',
    'movementPerBonus': 'movementBonus',
    'manaRestoreBonusPercent': 'manaRestore',
  };
  return statToCurseKey[statId] || null;
};

const DEFAULT_DIFFICULTY_POWER = 1.0;
const DEFAULT_DIFFICULTY_INDEX = 2;

const formatPercentage = (value: number) =>
  value % 1 === 0 ? value.toFixed(0) : value.toFixed(1);

function calculateGuardAmount(baseAmount: number, difficultyPower: number): number {
  return Math.max(1, Math.floor(baseAmount * difficultyPower));
}

function hasAnyRewards(rewards: CategorizedRewardsDto): boolean {
  return (
    rewards.resources.length > 0 ||
    rewards.artifactPools.length > 0 ||
    rewards.spellPools.length > 0 ||
    rewards.units.length > 0 ||
    rewards.experience !== null ||
    (rewards.cursePools?.length ?? 0) > 0
  );
}

function hasDisplayableOptions(v: CreatureBankVariantInfoDto): boolean {
  return !!(v.rewardOptions && v.rewardOptions.some(hasAnyRewards));
}

function isSimpleVariant(v: CreatureBankVariantInfoDto): boolean {
  return (
    v.guards.length === 0 &&
    v.rewards.artifactPools.length === 0 &&
    v.rewards.spellPools.length === 0 &&
    v.rewards.units.length === 0 &&
    (!v.rewards.cursePools || v.rewards.cursePools.length === 0) &&
    !hasDisplayableOptions(v)
  );
}

function compareMapObjects(
  a: MapObjectListItemDto,
  b: MapObjectListItemDto,
  field: MapObjectSortField,
  direction: SortDirection
): number {
  const dir = direction === 'asc' ? 1 : -1;

  switch (field) {
    case 'name':
      return (a.name || '').localeCompare(b.name || '') * dir;
    case 'id':
      return a.id.localeCompare(b.id, undefined, { numeric: true }) * dir;
    case 'category':
      return (a.category || '').localeCompare(b.category || '') * dir;
    default:
      return 0;
  }
}

export default function MapObjectsPage() {
  const navigate = useNavigate();
  const { '*': urlPath } = useParams<{ '*'?: string }>();

  useHighlightText();

  const {
    selectedMapObjectId,
    setSelectedMapObjectId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useMapObjectsStore();

  const columnLabels = useColumnLabels();
  const { label } = useLabels();

  const urlMapObjectId = urlPath || null;

  const prevUrlMapObjectIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (urlMapObjectId && urlMapObjectId !== selectedMapObjectId && urlMapObjectId !== prevUrlMapObjectIdRef.current) {
      setSelectedMapObjectId(urlMapObjectId);
    }
    prevUrlMapObjectIdRef.current = urlMapObjectId;
  }, [urlMapObjectId, selectedMapObjectId, setSelectedMapObjectId]);

  const mapObjectsQuery = useMapObjects(searchQuery || undefined);
  const mapObjectQuery = useMapObject(selectedMapObjectId);

  const sortedMapObjects = useMemo(() => {
    if (!mapObjectsQuery.data) return [];
    return [...mapObjectsQuery.data].sort((a, b) =>
      compareMapObjects(a, b, sortField, sortDirection)
    );
  }, [mapObjectsQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSort(field as MapObjectSortField, 'asc');
    }
  };

  const handleSelectMapObject = (id: string) => {
    setSelectedMapObjectId(id);
    navigate(`/map-objects/${id}`);
  };

  return (
    <div className="h-full overflow-hidden">
      <aside className="absolute left-0 top-0 bottom-0 w-80 lg:w-105 z-10 border-r border-border flex flex-col bg-card">
        <div className="px-2 h-11.75 border-b border-border flex items-center gap-0.5 relative">
          <SortableColumnHeader
            label={columnLabels.name}
            field="name"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />
          <SortableColumnHeader
            label={columnLabels.id}
            field="id"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />
          <SortableColumnHeader
            label={label('label_category')}
            field="category"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={setSearchQuery}
              placeholder={label('search_placeholder_entity', label('nav_map_objects'))}
              collapsible={true}
              onClear={() => setSearchQuery('')}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {mapObjectsQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_map_objects'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(mapObjectsQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <MapObjectList
              mapObjects={sortedMapObjects}
              selectedMapObjectId={selectedMapObjectId}
              onSelectMapObject={handleSelectMapObject}
              isLoading={mapObjectsQuery.isLoading}
            />
          )}
        </div>

        {mapObjectsQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground">
            <span>{label('total_count', mapObjectsQuery.data.length, label('nav_map_objects'))}</span>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <MapObjectDetailPanel
            mapObject={mapObjectQuery.data || null}
            selectedMapObjectId={selectedMapObjectId}
            error={mapObjectQuery.error as Error | null}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}

interface MapObjectListProps {
  mapObjects: MapObjectListItemDto[];
  selectedMapObjectId: string | null;
  onSelectMapObject: (id: string) => void;
  isLoading?: boolean;
}

function MapObjectList({ mapObjects, selectedMapObjectId, onSelectMapObject, isLoading }: MapObjectListProps) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedMapObjectId && mapObjects.length > 0) {
      document.getElementById(`mapobject-${selectedMapObjectId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedMapObjectId, mapObjects.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_map_objects'))}
      </div>
    );
  }

  if (mapObjects.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_map_objects'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {mapObjects.map((mapObject) => (
        <button
          key={mapObject.id}
          id={`mapobject-${mapObject.id}`}
          onClick={() => onSelectMapObject(mapObject.id)}
          className={cn(
            "flex items-center gap-3 px-3 py-2.5 rounded-md cursor-pointer text-left transition-colors",
            selectedMapObjectId === mapObject.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <ProgressiveIcon
            iconPath={mapObject.icon}
            alt={mapObject.name}
            size={40}
          />
          <div className="flex-1 min-w-0">
            <div className="font-medium overflow-hidden text-ellipsis whitespace-nowrap">
              {mapObject.name}
            </div>
            <div className={cn(
              "text-xs mt-0.5",
              selectedMapObjectId === mapObject.id ? "text-primary-foreground/70" : "text-muted-foreground"
            )}>
              {mapObject.id}{mapObject.bankType && ` · ${mapObject.bankType}`}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

interface MapObjectDetailPanelProps {
  mapObject: MapObjectDetailDto | null;
  selectedMapObjectId: string | null;
  error?: Error | null;
}

function MapObjectDetailPanel({ mapObject, selectedMapObjectId, error }: MapObjectDetailPanelProps) {
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('nav_map_objects'), label(error.message))}
      </div>
    );
  }

  if (!mapObject) {
    if (!selectedMapObjectId) {
      return (
        <div className="p-10 text-center text-muted-foreground flex flex-col items-center justify-center h-full">
          <div className="text-5xl mb-4">[ ]</div>
          <div>{label('detail_select')}</div>
        </div>
      );
    }
    return null;
  }

  return (
    <DetailContainer>
        <div className={cn(FULL_WIDTH_CARD, "bg-card border border-border rounded-lg p-4 grid grid-cols-1 md:grid-cols-[112px_1fr] gap-4")}>
        <div>
          <ProgressiveIcon
            iconPath={mapObject.icon}
            alt={mapObject.name}
            size={112}
            className="rounded-md shrink-0"
          />
        </div>

        <div className="flex flex-col gap-2">
          <h2 className="text-xl font-semibold text-foreground m-0">
            {mapObject.name}
          </h2>

          {mapObject.description && (
            <RichText
              text={mapObject.description}
              className="text-muted-foreground text-sm leading-relaxed block"
            />
          )}

          {mapObject.narrativeDescription && (
            <RichText
              text={mapObject.narrativeDescription}
              className="text-muted-foreground text-sm leading-relaxed italic m-0 block"
            />
          )}
        </div>
      </div>

      {mapObject.creatureBankInfo && (
        <div className={FULL_WIDTH_CARD}>
          <RewardDetails bankInfo={mapObject.creatureBankInfo} />
        </div>
      )}
    </DetailContainer>
  );
}

interface RewardDetailsProps {
  bankInfo: CreatureBankInfoDto;
}

function RewardDetails({ bankInfo }: RewardDetailsProps) {
  const { selectedDifficultyIndex: storedDifficultyIndex, setSelectedDifficultyIndex } = useMapObjectsStore();

  const defaultDifficultyIndex = bankInfo.difficultyLevels?.findIndex(d => d.power === DEFAULT_DIFFICULTY_POWER) ?? DEFAULT_DIFFICULTY_INDEX;
  const selectedDifficultyIndex = storedDifficultyIndex ?? defaultDifficultyIndex;

  const hasDifficultyLevels = bankInfo.difficultyLevels && bankInfo.difficultyLevels.length > 0;

  const { simpleVariants, complexVariants, optionVariants, hasRenderableVariants } = useMemo(() => {
    const hasRenderable = bankInfo.variants.some(v =>
      v.rollChance > 0 && (
        v.guards.length > 0 ||
        hasAnyRewards(v.rewards) ||
        hasDisplayableOptions(v)
      )
    );

    return {
      hasRenderableVariants: hasRenderable,
      optionVariants: bankInfo.variants.filter(hasDisplayableOptions),
      simpleVariants: bankInfo.variants.filter(v => isSimpleVariant(v) && !hasDisplayableOptions(v)),
      complexVariants: bankInfo.variants.filter(v => !isSimpleVariant(v) && !hasDisplayableOptions(v)),
    };
  }, [bankInfo.variants]);

  if (!hasRenderableVariants) {
    return null;
  }

  const actuallyHasGuards = bankInfo.variants.some(v => v.guards.length > 0);
  const difficultyPower = hasDifficultyLevels ? bankInfo.difficultyLevels![selectedDifficultyIndex].power : undefined;

  return (
    <div className="space-y-4">
      <div className="bg-card border border-border rounded-lg overflow-visible p-4">
        {actuallyHasGuards && hasDifficultyLevels && (
          <div className="flex items-center justify-end mb-3">
            <DifficultySelector
              difficulties={bankInfo.difficultyLevels!}
              selectedIndex={selectedDifficultyIndex}
              onSelect={setSelectedDifficultyIndex}
              difficultyLabel={bankInfo.difficultyLabel}
            />
          </div>
        )}

        <div className="space-y-3">
          {simpleVariants.length > 0 && (
            <div className="grid grid-cols-[repeat(auto-fill,minmax(120px,1fr))] gap-2">
              {simpleVariants.map((variant, idx) => (
                <RewardType
                  key={`simple-${idx}`}
                  variant={variant}
                  difficultyPower={difficultyPower}
                  guardsLabel={bankInfo.guardsLabel}
                />
              ))}
            </div>
          )}
          {complexVariants.length > 0 && (
            <div className="grid grid-cols-[repeat(auto-fill,minmax(280px,1fr))] gap-3">
              {complexVariants.map((variant, idx) => (
                <RewardType
                  key={`complex-${idx}`}
                  variant={variant}
                  difficultyPower={difficultyPower}
                  guardsLabel={bankInfo.guardsLabel}
                />
              ))}
            </div>
          )}
          {optionVariants.length > 0 && (
            <div className="space-y-3">
              {optionVariants.map((variant, idx) => (
                <RewardType
                  key={`option-${idx}`}
                  variant={variant}
                  difficultyPower={difficultyPower}
                  guardsLabel={bankInfo.guardsLabel}
                />
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

interface RewardTypeProps {
  variant: CreatureBankVariantInfoDto;
  difficultyPower?: number;
  guardsLabel?: string | null;
}

function RewardType({ variant, difficultyPower, guardsLabel }: RewardTypeProps) {
  const [expandedPool, setExpandedPool] = useState<number | null>(null);
  const [expandedSpellPool, setExpandedSpellPool] = useState<number | null>(null);
  const navigate = useNavigate();
  const { label } = useLabels();

  const { guards, rewards, rewardOptions } = variant;
  const hasRewards = hasAnyRewards(rewards);
  const hasDisplayableRewardOptions = rewardOptions && rewardOptions.some(hasAnyRewards);
  const hasGuards = guards.length > 0;
  const showBothSections = hasGuards && (hasRewards || hasDisplayableRewardOptions);

  if (variant.rollChance === 0 || (!hasRewards && !hasDisplayableRewardOptions && guards.length === 0)) {
    return null;
  }

  return (
    <div className="bg-muted/30 border border-border/50 rounded-lg p-4 flex flex-col items-center gap-2 relative">
      {variant.rollChance < 100 && (
        <div className="absolute top-2 left-2">
          <ChanceBadge chance={variant.rollChance} />
        </div>
      )}

      {showBothSections && (
        <div className="text-xs text-muted-foreground font-medium">{label('label_rewards')}</div>
      )}
      {!showBothSections && variant.rollChance < 100 && (
        <div className="h-3" />
      )}

      {hasDisplayableRewardOptions && (
        <div className="flex items-stretch gap-2 w-full overflow-visible">
          {rewardOptions!.filter(hasAnyRewards).map((option, optionIdx) => (
            <RewardOption
              key={optionIdx}
              option={option}
              optionNumber={optionIdx + 1}
              expandedPool={expandedPool}
              setExpandedPool={setExpandedPool}
              expandedSpellPool={expandedSpellPool}
              setExpandedSpellPool={setExpandedSpellPool}
              navigate={navigate}
              optionOffset={optionIdx * 100}
            />
          ))}
        </div>
      )}

      {rewards.resources.length > 0 && (
        <div className="flex flex-wrap justify-center gap-1">
          {rewards.resources.map((res, idx) => (
            <CurrencyBadge
              key={idx}
              amount={res.amount}
              resourceKey={res.resourceKey}
              iconSize={18}
              gap="gap-1"
            />
          ))}
        </div>
      )}

      {rewards.artifactPools.length > 0 && (
        <div className="w-full space-y-2">
          {rewards.artifactPools.map((rarityPool, rarityIdx) => (
            <ArtifactRarityPoolSection
              key={rarityIdx}
              rarityPool={rarityPool}
              expandedPool={expandedPool}
              setExpandedPool={setExpandedPool}
              navigate={navigate}
              poolIndexOffset={rewards.artifactPools.slice(0, rarityIdx).reduce((sum, p) => sum + p.groups.length, 0)}
            />
          ))}
        </div>
      )}

      {rewards.spellPools.length > 0 && (
        <div className={
          rewards.spellPools.length > 1
            ? "flex items-stretch gap-2 w-full"
            : "w-[calc(50%-4px)] mx-auto min-h-15"
        }>
          {rewards.spellPools.map((pool, poolIdx) => {
            const spellLabel = rewards.spellPools.length > 1
              ? `${label('nav_spells')} ${poolIdx + 1}`
              : label('nav_spells');
            return (
              <div key={poolIdx} className={rewards.spellPools.length > 1 ? "flex-1 min-h-15" : "h-full"}>
                <SpellPoolButton
                  groups={pool.groups}
                  buttonLabel={spellLabel}
                  isExpanded={expandedSpellPool === poolIdx}
                  onClick={() => setExpandedSpellPool(expandedSpellPool === poolIdx ? null : poolIdx)}
                  navigate={navigate}
                />
              </div>
            );
          })}
        </div>
      )}

      {rewards.units.length > 0 && (
        <div className="flex flex-wrap justify-center gap-2">
          {rewards.units.map((unit, idx) => (
            <UnitHexCard
              key={idx}
              unitId={unit.unitId}
              unitName={unit.unitName}
              icon={unit.icon}
              amount={unit.amount}
              size="md"
            />
          ))}
        </div>
      )}

      {rewards.experience !== null && (
        <div className="text-foreground font-semibold">
          +{rewards.experience} XP
        </div>
      )}

      {rewards.cursePools && rewards.cursePools.length > 0 && (
        <div className="w-full space-y-2">
          {rewards.cursePools.map((cursePool, poolIdx) => {
            const curseRarity = getCurseRarityFromTitle(cursePool.title);
            const curseTitleColor = getRarityColorClass(curseRarity);

            return (
              <div key={poolIdx} className="w-full">
                <div className={`text-xs mb-1.5 text-center font-medium pt-2 border-t border-border/50 ${curseTitleColor}`}>
                  {cursePool.title}
                </div>
                <div className="text-xs text-amber-300/70 mb-1.5 text-center">
                  {label('duration', cursePool.durationDays)}
                </div>
                <div className="flex flex-wrap justify-center gap-1.5">
                  {cursePool.curses.map((curse, curseIdx) => {
                    if (curse.effects.length > 0) {
                      return (
                        <div
                          key={curseIdx}
                          className="px-2 py-1 bg-muted/30 border border-border/50 rounded text-xs text-foreground/80 w-30 grid place-items-center text-center"
                          title={curse.description || undefined}
                        >
                          {curse.effects.map((e, effectIdx) => {
                            const formattedModifier = formatCurseModifier(e.modifier, e.stat);
                            const localizationKey = getCurseLocalizationKey(e.stat);
                            const effectText = localizationKey
                              ? label(localizationKey, formattedModifier)
                              : `${e.statDisplayName} ${formattedModifier}`;

                            return (
                              <span key={effectIdx}>
                                {effectText}
                                {effectIdx < curse.effects.length - 1 && <>,<br /></>}
                              </span>
                            );
                          })}
                        </div>
                      );
                    } else if (curse.id === 'heros_crypt_debuff_none') {
                      return (
                        <div
                          key={curseIdx}
                          className="px-2 py-1 bg-muted/30 border border-border/50 rounded text-xs text-foreground/80 w-30 grid place-items-center text-center"
                          title={curse.description || undefined}
                        >
                          {label('none')}
                        </div>
                      );
                    } else {
                      return (
                        <div
                          key={curseIdx}
                          className="px-2 py-1 bg-muted/30 border border-border/50 rounded text-xs text-foreground/80 w-30 grid place-items-center text-center"
                          title={curse.description || undefined}
                        >
                          {curse.id}
                        </div>
                      );
                    }
                  })}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {hasGuards && (
        <div className={showBothSections ? "border-t border-border/50 pt-2 mt-1 w-full" : "w-full"}>
          <div className="text-xs text-muted-foreground mb-2 text-center font-medium">{guardsLabel || 'Guards'}</div>
          <div className="flex flex-wrap justify-center gap-2">
            {guards.map((guard, guardIdx) => {
              const amount = difficultyPower !== undefined
                ? calculateGuardAmount(guard.amount, difficultyPower)
                : guard.amount;
              return (
                <UnitHexCard
                  key={guardIdx}
                  unitId={guard.unitId}
                  unitName={guard.unitName}
                  icon={guard.icon}
                  amount={amount}
                  size={guards.length > 6 ? 'compact' : 'md'}
                />
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}

interface RewardOptionProps {
  option: CategorizedRewardsDto;
  optionNumber: number;
  expandedPool: number | null;
  setExpandedPool: (idx: number | null) => void;
  expandedSpellPool: number | null;
  setExpandedSpellPool: (idx: number | null) => void;
  navigate: (path: string) => void;
  optionOffset: number;
}

function RewardOption({
  option,
  optionNumber,
  expandedPool,
  setExpandedPool,
  expandedSpellPool,
  setExpandedSpellPool,
  navigate,
  optionOffset
}: RewardOptionProps) {
  const { label } = useLabels();

  return (
    <div className="flex-1 bg-muted/20 border border-border/30 rounded-md p-2 flex flex-col items-center gap-1.5 min-w-0 overflow-visible">
      <div className="text-xs text-muted-foreground font-medium">
        {label('label_option')} {optionNumber}
      </div>

      {option.resources.length > 0 && (
        <div className="flex flex-wrap justify-center gap-1">
          {option.resources.map((res, idx) => (
            <CurrencyBadge
              key={idx}
              amount={res.amount}
              resourceKey={res.resourceKey}
              iconSize={16}
              gap="gap-0.5"
            />
          ))}
        </div>
      )}

      {option.artifactPools.length > 0 && (
        <div className="w-full space-y-1">
          {option.artifactPools.map((rarityPool, rarityIdx) => (
            <ArtifactRarityPoolSection
              key={rarityIdx}
              rarityPool={rarityPool}
              expandedPool={expandedPool}
              setExpandedPool={setExpandedPool}
              navigate={navigate}
              poolIndexOffset={optionOffset + option.artifactPools.slice(0, rarityIdx).reduce((sum, p) => sum + p.groups.length, 0)}
              compact
            />
          ))}
        </div>
      )}

      {option.spellPools.length > 0 && (
        <div className="w-full space-y-1">
          {option.spellPools.map((pool, poolIdx) => {
            const globalIdx = optionOffset + poolIdx;
            const spellLabel = label('nav_spells');
            return (
              <SpellPoolButton
                key={poolIdx}
                groups={pool.groups}
                buttonLabel={spellLabel}
                isExpanded={expandedSpellPool === globalIdx}
                onClick={() => setExpandedSpellPool(expandedSpellPool === globalIdx ? null : globalIdx)}
                navigate={navigate}
                stretch={false}
                centered
              />
            );
          })}
        </div>
      )}

      {option.units.length > 0 && (
        <div className="flex flex-wrap justify-center gap-1">
          {option.units.map((unit, idx) => (
            <UnitHexCard
              key={idx}
              unitId={unit.unitId}
              unitName={unit.unitName}
              icon={unit.icon}
              amount={unit.amount}
              size="sm"
            />
          ))}
        </div>
      )}

      {option.experience !== null && (
        <div className="text-foreground font-semibold">
          +{option.experience} XP
        </div>
      )}

      {option.cursePools && option.cursePools.length > 0 && (
        <div className="w-full space-y-1">
          {option.cursePools.map((cursePool, poolIdx) => {
            const curseRarity = getCurseRarityFromTitle(cursePool.title);
            const curseTitleColor = getRarityColorClass(curseRarity);

            return (
              <div key={poolIdx} className="w-full">
                <div className={`text-xs mb-1 text-center font-medium pt-1.5 border-t border-border/50 ${curseTitleColor}`}>
                  {cursePool.title}
                </div>
                <div className="text-xs text-amber-300/70 mb-1 text-center">
                  {label('duration', cursePool.durationDays)}
                </div>
                <div className="flex flex-wrap justify-center gap-1">
                  {cursePool.curses.map((curse, curseIdx) => {
                    let effectText: string;
                    if (curse.effects.length > 0) {
                      effectText = curse.effects.map(e => {
                        const formattedModifier = formatCurseModifier(e.modifier, e.stat);
                        const localizationKey = getCurseLocalizationKey(e.stat);

                        if (localizationKey) {
                          return label(localizationKey, formattedModifier);
                        } else {
                          return `${e.statDisplayName} ${formattedModifier}`;
                        }
                      }).join(', ');
                    } else if (curse.id === 'heros_crypt_debuff_none') {
                      effectText = label('none');
                    } else {
                      effectText = curse.id;
                    }

                    return (
                      <div
                        key={curseIdx}
                        className="px-1.5 py-0.5 bg-muted/30 border border-border/50 rounded text-xs text-foreground/80"
                        title={curse.description || undefined}
                      >
                        {effectText}
                      </div>
                    );
                  })}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

interface DifficultySelectorProps {
  difficulties: DifficultyLevelDto[];
  selectedIndex: number;
  onSelect: (index: number) => void;
  difficultyLabel?: string | null;
}

function DifficultySelector({ difficulties, selectedIndex, onSelect, difficultyLabel }: DifficultySelectorProps) {
  return (
    <div className="flex items-center justify-end gap-1">
      {difficultyLabel && (
        <span className="text-xs text-muted-foreground font-medium mr-1">{difficultyLabel}:</span>
      )}
      {difficulties.map((difficulty, idx) => (
        <button
          key={idx}
          onClick={() => onSelect(idx)}
          title={difficulty.tooltip || undefined}
          className="relative transition-all group"
        >
          <ProgressiveIcon
            iconPath={difficulty.icon}
            alt={difficulty.name}
            size={36}
            imgClassName="object-contain rounded-full"
          />
          <div
            className={cn(
              "absolute inset-0 rounded-full pointer-events-none transition-all z-10",
              selectedIndex === idx
                ? "ring-2 ring-inset ring-primary"
                : "group-hover:ring-2 group-hover:ring-inset group-hover:ring-primary/50"
            )}
          />
        </button>
      ))}
    </div>
  );
}

function ChanceBadge({ chance }: { chance: number }) {
  return (
    <span className="inline-flex items-center rounded font-bold bg-semantic-green/20 text-semantic-green px-1.5 py-0.5 text-[10px]">
      {formatPercentage(chance)}%
    </span>
  );
}

interface PoolDropdownProps {
  label: React.ReactNode;
  isExpanded: boolean;
  onClick: () => void;
  dropdownWidth: number;
  dropdownClassName: string;
  children: React.ReactNode;
  stretch?: boolean;
  centered?: boolean;
}

function PoolDropdown({ label, isExpanded, onClick, dropdownWidth, dropdownClassName, children, stretch = true, centered = false }: PoolDropdownProps) {
  const buttonRef = useRef<HTMLButtonElement>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);

  useOnClickOutside(dropdownRef, onClick, [buttonRef], isExpanded);

  useEffect(() => {
    if (isExpanded && dropdownRef.current) {
      dropdownRef.current.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
  }, [isExpanded]);

  useLayoutEffect(() => {
    if (!isExpanded || !buttonRef.current || !dropdownRef.current) return;

    const buttonRect = buttonRef.current.getBoundingClientRect();
    const detailContainer = buttonRef.current.closest('[data-detail-container]');

    dropdownRef.current.style.left = '';
    dropdownRef.current.style.right = '';
    dropdownRef.current.style.transform = '';

    if (!detailContainer) {
      dropdownRef.current.style.left = '50%';
      dropdownRef.current.style.transform = 'translateX(-50%)';
      return;
    }

    const containerRect = detailContainer.getBoundingClientRect();
    const buttonCenterX = buttonRect.left + buttonRect.width / 2;
    const centeredDropdownLeft = buttonCenterX - dropdownWidth / 2;
    const centeredDropdownRight = buttonCenterX + dropdownWidth / 2;

    if (centeredDropdownRight > containerRect.right) {
      dropdownRef.current.style.right = '0';
    } else if (centeredDropdownLeft < containerRect.left) {
      dropdownRef.current.style.left = '0';
    } else {
      dropdownRef.current.style.left = '50%';
      dropdownRef.current.style.transform = 'translateX(-50%)';
    }
  }, [isExpanded, dropdownWidth]);

  return (
    <div className={stretch ? "relative h-full" : "relative"}>
      <button
        ref={buttonRef}
        onClick={onClick}
        className={cn(
          "w-full px-3 py-1.5 text-xs font-medium rounded-md transition-colors flex items-center gap-1.5 cursor-pointer",
          centered ? "justify-center text-center" : "justify-between text-left",
          stretch && "h-full",
          isExpanded
            ? "bg-primary text-primary-foreground"
            : "bg-muted text-muted-foreground hover:bg-muted/80"
        )}
      >
        <span>{label}</span>
        <svg
          className={cn("w-3 h-3 transition-transform shrink-0", isExpanded && "rotate-180")}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      <div
        ref={dropdownRef}
        className={cn(
          "absolute top-full mt-1 z-50 max-w-[90vw] overflow-y-auto",
          "p-3 bg-card rounded-lg border border-border shadow-lg",
          "transition-opacity duration-200 ease-out",
          dropdownClassName,
          isExpanded ? "opacity-100 visible" : "opacity-0 invisible pointer-events-none"
        )}
      >
        {children}
      </div>
    </div>
  );
}

interface PoolItemButtonProps {
  icon: string;
  name: string;
  onClick: () => void;
}

function PoolItemButton({ icon, name, onClick }: PoolItemButtonProps) {
  return (
    <button
      onClick={onClick}
      className="flex flex-col items-center p-2 hover:bg-muted/40 rounded transition-colors group cursor-pointer"
    >
      <div className="w-10 h-10 mb-1 relative">
        <ProgressiveIcon
          iconPath={icon}
          alt={name}
          size={40}
          imgClassName="object-contain"
        />
      </div>
      <span className="text-[10px] text-center text-foreground group-hover:text-semantic-gold line-clamp-2">
        {name}
      </span>
    </button>
  );
}

interface ArtifactRarityPoolSectionProps {
  rarityPool: ArtifactRarityPoolDto;
  expandedPool: number | null;
  setExpandedPool: (idx: number | null) => void;
  navigate: (path: string) => void;
  poolIndexOffset: number;
  compact?: boolean;
}

function ArtifactRarityPoolSection({
  rarityPool,
  expandedPool,
  setExpandedPool,
  navigate,
  poolIndexOffset,
  compact = false
}: ArtifactRarityPoolSectionProps) {
  const colorClass = getRarityColorClass(rarityPool.rarity);
  const drawsLabel = `${rarityPool.draws}× `;

  return (
    <div className={compact ? "space-y-1" : "space-y-1.5"}>
      <div className={`text-xs font-semibold text-center pt-2 border-t border-border/50 ${colorClass} ${compact ? '' : 'mb-1'}`}>
        {drawsLabel}{rarityPool.rarityLabel}
      </div>

      <div className={
        rarityPool.groups.length >= 2
          ? "grid grid-cols-2 gap-1.5 items-stretch"
          : "flex justify-center"
      }>
        {rarityPool.groups.map((group, groupIdx) => {
          const globalIdx = poolIndexOffset + groupIdx;
          return (
            <ArtifactPoolButton
              key={groupIdx}
              pool={group}
              isExpanded={expandedPool === globalIdx}
              onClick={() => setExpandedPool(expandedPool === globalIdx ? null : globalIdx)}
              navigate={navigate}
              stretch={rarityPool.groups.length > 1}
            />
          );
        })}
      </div>
    </div>
  );
}

interface ArtifactPoolButtonProps {
  pool: ArtifactPoolGroupDto;
  isExpanded: boolean;
  onClick: () => void;
  navigate: (path: string) => void;
  stretch?: boolean;
}

function ArtifactPoolButton({ pool, isExpanded, onClick, navigate, stretch = true }: ArtifactPoolButtonProps) {
  const colorClass = getRarityColorClass(pool.rarity);
  return (
    <PoolDropdown
      label={<><span className="font-bold">{pool.percentage}%</span><br /><span className={colorClass}>{pool.groupName}</span><br />({pool.count})</>}
      isExpanded={isExpanded}
      onClick={onClick}
      dropdownWidth={DROPDOWN_CONFIG.artifact.width}
      dropdownClassName={DROPDOWN_CONFIG.artifact.className}
      stretch={stretch}
    >
      {pool.artifacts.map((artifact, idx) => (
        <PoolItemButton
          key={idx}
          icon={artifact.icon}
          name={artifact.name}
          onClick={() => navigate(`/artifacts/${artifact.id}`)}
        />
      ))}
    </PoolDropdown>
  );
}

interface SpellPoolButtonProps {
  groups: Array<{
    tierName: string;
    tier: number;
    weight: number;
    count: number;
    spells: Array<{
      id: string;
      name: string;
      rank: number;
      icon: string;
    }>;
  }>;
  buttonLabel: string;
  isExpanded: boolean;
  onClick: () => void;
  navigate: (path: string) => void;
  stretch?: boolean;
  centered?: boolean;
}

function SpellPoolButton({ groups, buttonLabel, isExpanded, onClick, navigate, stretch = true, centered = false }: SpellPoolButtonProps) {
  const { label } = useLabels();

  return (
    <PoolDropdown
      label={buttonLabel}
      isExpanded={isExpanded}
      onClick={onClick}
      stretch={stretch}
      centered={centered}
      dropdownWidth={DROPDOWN_CONFIG.spell.width}
      dropdownClassName={DROPDOWN_CONFIG.spell.className}
    >
      {groups.map((group, idx) => (
        <div key={idx}>
          <h6 className="text-xs font-semibold text-muted-foreground mb-2">
            {group.tierName} - {group.weight}% ({group.count} {label('entity_spell')})
          </h6>
          <div className="grid grid-cols-4 sm:grid-cols-5 gap-2">
            {group.spells.map((spell, spellIdx) => (
              <PoolItemButton
                key={spellIdx}
                icon={spell.icon}
                name={spell.name}
                onClick={() => navigate(`/spells/${spell.id}`)}
              />
            ))}
          </div>
        </div>
      ))}
    </PoolDropdown>
  );
}
