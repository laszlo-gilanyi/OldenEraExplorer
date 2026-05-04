import React, { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useUnitsStore, type UnitSortField } from './unitsStore';
import { useUnits, useUnit } from './useUnits';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import type { UnitListItemDto, UnitDetailDto, AbilityDetailDto, UnitCostEntryDto, UsedByHeroDto } from '@/api/types';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import SortableColumnHeader, { type SortDirection } from '@/components/display/SortableColumnHeader';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import FactionBadge from '@/components/display/FactionBadge';
import CurrencyBadge from '@/components/display/CurrencyBadge';
import RichText from '@/components/display/RichText';
import DetailContainer from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';

/**
 * Faction/Tier use multi-level cascading sort to maintain stable grouping.
 * Name/ID use single-level sort for simplicity.
 */
function compareUnits(
  a: UnitListItemDto,
  b: UnitListItemDto,
  primaryField: UnitSortField,
  primaryDirection: SortDirection
): number {
  const dir = primaryDirection === 'asc' ? 1 : -1;

  const compareFaction = () =>
    (a.factionDisplay || a.faction || '').localeCompare(b.factionDisplay || b.faction || '');
  const compareTier = () => (a.tier ?? 0) - (b.tier ?? 0);
  const compareId = () => a.id.localeCompare(b.id, undefined, { numeric: true });
  const compareName = () => (a.name || '').localeCompare(b.name || '');

  switch (primaryField) {
    case 'faction':
      // Faction (direction) -> Tier (asc) -> ID (asc)
      return compareFaction() * dir || compareTier() || compareId();

    case 'tier':
      // Tier (direction) -> Faction (asc) -> ID (asc)
      return compareTier() * dir || compareFaction() || compareId();

    case 'name':
      // Name only (single-level)
      return compareName() * dir;

    case 'id':
      // ID only (single-level)
      return compareId() * dir;

    default:
      return 0;
  }
}

const ICON_STAT_SIZE = 32;
const ICON_RESOURCE_SIZE = 32;

const STAT_ICONS: Record<string, string> = {
  Health: 'icons/unit_stats/unit_health',
  Attack: 'icons/unit_stats/unit_attack',
  Defence: 'icons/unit_stats/unit_defence',
  Damage: 'icons/unit_stats/unit_damage',
  Initiative: 'icons/unit_stats/unit_init',
  Speed: 'icons/unit_stats/unit_speed',
  Luck: 'icons/unit_stats/unit_luck',
  Morale: 'icons/unit_stats/unit_moral',
  SquadValue: 'icons/unit_stats/squadValue',
  ExpBonus: 'icons/unit_stats/expBonus',
  Growth: 'icons/unit_stats/weeklyGrowth',
  Cost: 'icons/unit_stats/cost',
};

interface StatItem {
  label: string;
  value: string | number | null | undefined;
  iconPath: string;
}

function PrimaryStatsGrid({ stats }: { stats: StatItem[] }) {
  const filteredStats = stats.filter(s => s.value !== null && s.value !== undefined);

  return (
    <div
      className="bg-card border border-border"
      style={{
        display: 'grid',
        gridTemplateColumns: '48px 9px min-content 9px auto',
        width: 'fit-content'
      }}
    >
      {filteredStats.map((stat, idx) => {
        const isLast = idx === filteredStats.length - 1;
        const isStaticIcon = stat.iconPath.startsWith('/');
        const borderClass = isLast ? '' : 'border-b border-border';

        return (
          <React.Fragment key={idx}>
            <div className={borderClass} style={{ padding: '8px', display: 'flex', alignItems: 'center' }}>
              {isStaticIcon
                ? <img src={stat.iconPath} alt={stat.label} style={{ width: ICON_STAT_SIZE, height: ICON_STAT_SIZE, objectFit: 'contain' }} />
                : <ProgressiveIcon iconPath={stat.iconPath} alt={stat.label} size={ICON_STAT_SIZE} />
              }
            </div>

            <div className={`bg-border ${borderClass}`} style={{ width: '1px', margin: '8px 8px 8px 0' }} />

            <div className={borderClass} style={{ padding: '8px 8px 8px 0', display: 'flex', alignItems: 'center' }}>
              <span className="text-muted-foreground font-semibold" style={{ textAlign: 'left', display: 'block', overflowWrap: 'break-word', wordBreak: 'keep-all', lineHeight: '1.3' }}>
                {stat.label}
              </span>
            </div>

            <div className={`bg-border ${borderClass}`} style={{ width: '1px', margin: '8px 8px 8px 0' }} />

            <div className={borderClass} style={{ padding: '8px', textAlign: 'right', display: 'flex', alignItems: 'center', justifyContent: 'flex-end' }}>
              <span className="font-semibold text-muted-foreground" style={{ whiteSpace: 'nowrap' }}>
                {stat.value}
              </span>
            </div>
          </React.Fragment>
        );
      })}
    </div>
  );
}

interface SecondaryStatItem {
  label: string;
  value?: string | number | null;
  iconPath: string;
  costEntries?: UnitCostEntryDto[] | null;
}

function SecondaryStatsGrid({ stats }: { stats: SecondaryStatItem[] }) {
  return (
    <div
      className="bg-card border border-border"
      style={{
        display: 'grid',
        gridTemplateColumns: '48px 9px min-content 9px auto',
        width: 'fit-content'
      }}
    >
      {stats.map((stat, idx) => {
        const isLast = idx === stats.length - 1;
        const isStaticIcon = stat.iconPath.startsWith('/');
        const hasCostEntries = stat.costEntries && stat.costEntries.length > 0;
        const borderClass = isLast ? '' : 'border-b border-border';

        return (
          <React.Fragment key={idx}>
            <div className={borderClass} style={{ padding: '8px', display: 'flex', alignItems: 'center' }}>
              {isStaticIcon
                ? <img src={stat.iconPath} alt={stat.label} style={{ width: ICON_STAT_SIZE, height: ICON_STAT_SIZE, objectFit: 'contain' }} />
                : <ProgressiveIcon iconPath={stat.iconPath} alt={stat.label} size={ICON_STAT_SIZE} />
              }
            </div>

            <div className={`bg-border ${borderClass}`} style={{ width: '1px', margin: '8px 8px 8px 0' }} />

            <div className={borderClass} style={{ padding: '8px 8px 8px 0', display: 'flex', alignItems: 'center' }}>
              <span className="text-muted-foreground font-semibold" style={{ textAlign: 'left', display: 'block', overflowWrap: 'break-word', wordBreak: 'keep-all', lineHeight: '1.3' }}>
                {stat.label}
              </span>
            </div>

            <div className={`bg-border ${borderClass}`} style={{ width: '1px', margin: '8px 8px 8px 0' }} />

            <div className={borderClass} style={{ padding: '8px', display: 'flex', alignItems: 'center', justifyContent: 'flex-end' }}>
              {hasCostEntries ? (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', alignItems: 'flex-end' }}>
                  {stat.costEntries!.map((entry, entryIdx) => (
                    <CurrencyBadge
                      key={entryIdx}
                      amount={entry.amount}
                      resourceKey={entry.resourceKey}
                      displayName={entry.displayName}
                      iconSize={ICON_RESOURCE_SIZE}
                      gap="gap-[5px]"
                      amountClassName="text-muted-foreground"
                    />
                  ))}
                </div>
              ) : (
                <span className="font-semibold text-muted-foreground" style={{ minWidth: '50px', textAlign: 'right' }}>
                  {stat.value}
                </span>
              )}
            </div>
          </React.Fragment>
        );
      })}
    </div>
  );
}

function AbilityCard({ ability, onAbilityClick }: { ability: AbilityDetailDto; onAbilityClick?: (abilityId: string) => void }) {
  const { label } = useLabels();
  const navigationId = ability.id || ability.nameSid;
  const isClickable = navigationId && onAbilityClick;
  const isActive = ability.abilityType === 'Active' || ability.abilityType === 'Alternative';

  const nameElement = isClickable ? (
    <button
      onClick={() => onAbilityClick(navigationId!)}
      className="text-lg font-semibold text-semantic-gold bg-transparent border-0 p-0 cursor-pointer hover:opacity-80 transition-opacity text-left"
    >
      {ability.name}
    </button>
  ) : (
    <div className="text-lg font-semibold text-semantic-gold">
      {ability.name}
    </div>
  );

  const descriptionContent = (
    <>
      {ability.immunities && ability.immunities.length > 0 && (
        <p className="text-semantic-red text-sm mb-2 m-0">
          {ability.immunities.join(', ')}
        </p>
      )}

      {ability.description && (
        <RichText
          text={ability.description}
          className="text-muted-foreground text-sm leading-relaxed whitespace-pre-wrap m-0 block"
        />
      )}

      {ability.infoNotes && ability.infoNotes.length > 0 && (
        <div className="mt-2">
          {ability.infoNotes.map((note, index) => (
            <RichText
              key={index}
              text={note}
              className="text-muted-foreground/70 text-sm italic m-0 block"
            />
          ))}
        </div>
      )}
    </>
  );

  return (
    <div className="bg-card border border-border rounded-2xl p-5 mb-3">
      {isActive ? (
        <div className="grid grid-cols-1 md:grid-cols-[80px_140px_1fr] gap-4">
          {ability.icon && (
            <ProgressiveIcon
              iconPath={ability.icon}
              alt={ability.name}
              size={80}
              className="shrink-0"
            />
          )}

          <div className="flex flex-col justify-center">
            {nameElement}
            {ability.abilityTypeSid && (
              <div className="text-muted-foreground text-sm mt-1">
                {ability.abilityTypeSid}
              </div>
            )}
            {ability.rank != null && (
              <div className="text-semantic-gold text-sm mt-1">
                {label('label_ability_tier', ability.rank)}
              </div>
            )}
            {ability.energyCost != null && (
              <div className="text-semantic-green text-sm">
                {label('label_ability_cost', ability.energyCost)}
              </div>
            )}
          </div>

          <div className="flex flex-col justify-center">
            {descriptionContent}
          </div>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-[80px_1fr] gap-4">
          {ability.icon && (
            <ProgressiveIcon
              iconPath={ability.icon}
              alt={ability.name}
              size={80}
              className="shrink-0"
            />
          )}

          <div className="flex flex-col justify-center">
            {nameElement}
            {ability.abilityTypeSid && (
              <div className="text-muted-foreground text-sm mt-1">
                {ability.abilityTypeSid}
              </div>
            )}
            <div className="mt-2">
              {descriptionContent}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function HeroesSection({ heroes, headerLabel, navigate }: { heroes: UsedByHeroDto[] | null; headerLabel: string; navigate: (path: string) => void }) {
  if (!heroes || heroes.length === 0) return null;

  return (
    <div className="bg-card border border-border rounded-2xl p-5">
      <h3 className="m-0 mb-3 text-base font-semibold text-foreground">{headerLabel}</h3>
      <div className="flex flex-wrap gap-2">
        {heroes.map((hero) => (
          <button
            key={hero.heroId}
            onClick={() => navigate(`/heroes/${hero.heroId}`)}
            className="px-3 py-1.5 bg-muted border border-border rounded-md text-sm text-foreground cursor-pointer transition-colors hover:bg-accent hover:text-accent-foreground"
          >
            {hero.heroName}
          </button>
        ))}
      </div>
    </div>
  );
}

interface UnitDetailPanelProps {
  unit: UnitDetailDto | null;
  selectedUnitId: string | null;
  error?: Error | null;
}

function UnitDetailPanel({ unit, selectedUnitId, error }: UnitDetailPanelProps) {
  const { label } = useLabels();
  const navigate = useNavigate();

  const handleAbilityClick = (nameSid: string) => {
    if (nameSid.startsWith('passive_')) {
      return;
    }
    navigate(`/abilities/${encodeURIComponent(nameSid)}`);
  };

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_unit'), label(error.message))}
      </div>
    );
  }

  if (!unit) {
    if (!selectedUnitId) {
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
    <DetailContainer className="flex flex-col gap-7">
        <div className="bg-card border border-border rounded-2xl p-5 flex flex-col md:flex-row gap-6 items-start md:items-center">
          {unit.iconPath && (
            <ProgressiveIcon
              iconPath={unit.iconPath}
              alt={unit.localizedName || unit.name}
              size={180}
              className="shrink-0"
            />
          )}
          <div className="flex-1 flex flex-col gap-2.5">
            <h2 className="m-0 text-[1.75rem] font-semibold text-semantic-gold">
              {unit.localizedName || unit.name}
            </h2>
            <FactionBadge
              factionIcon={unit.factionIcon}
              factionDisplay={unit.factionDisplay}
              faction={unit.faction}
              iconSize={48}
              textClassName="text-muted-foreground text-[0.95rem]"
            />
            <div className="text-foreground text-base font-semibold">
              {(unit.statLabels?.tier || 'Tier: {0}').replace('{0}', String(unit.tier ?? '?'))}
            </div>
            {unit.narrativeDescription && (
              <RichText
                text={unit.narrativeDescription}
                className="text-muted-foreground text-sm leading-relaxed italic block"
              />
            )}
            {unit.description && (
              <RichText
                text={unit.description}
                className="text-muted-foreground text-sm leading-relaxed block"
              />
            )}
          </div>
        </div>

        <div>
          <h3 className="m-0 mb-3 text-lg text-foreground font-semibold">
            {unit.statLabels?.creatureStatsHeader || 'Stats'}
          </h3>
              <div className="flex flex-wrap gap-2 items-start">
                <div className="self-center">
                  <PrimaryStatsGrid stats={[
                    { label: unit.statLabels?.health || 'Health', value: unit.health, iconPath: STAT_ICONS.Health },
                    { label: unit.statLabels?.attack || 'Attack', value: unit.attack, iconPath: STAT_ICONS.Attack },
                    { label: unit.statLabels?.defence || 'Defence', value: unit.defense, iconPath: STAT_ICONS.Defence },
                    { label: unit.statLabels?.damage || 'Damage', value: unit.minDamage && unit.maxDamage ? `${unit.minDamage} - ${unit.maxDamage}` : null, iconPath: STAT_ICONS.Damage },
                    { label: unit.statLabels?.initiative || 'Initiative', value: unit.initiative, iconPath: STAT_ICONS.Initiative },
                    { label: unit.statLabels?.speed || 'Speed', value: unit.speed, iconPath: STAT_ICONS.Speed },
                    { label: unit.statLabels?.luck || 'Luck', value: unit.luck, iconPath: STAT_ICONS.Luck },
                    { label: unit.statLabels?.morale || 'Morale', value: unit.morale, iconPath: STAT_ICONS.Morale },
                  ]} />
                </div>

                <div className="self-start">
                  <SecondaryStatsGrid stats={[
                    { label: unit.statLabels?.squadValue || 'Squad Value', value: unit.squadValue ?? '-', iconPath: STAT_ICONS.SquadValue },
                    { label: unit.statLabels?.expBonus || 'Exp Bonus', value: unit.expBonus ?? '-', iconPath: STAT_ICONS.ExpBonus },
                    { label: unit.statLabels?.weeklyGrowth || 'Weekly Growth', value: unit.growth ?? '-', iconPath: STAT_ICONS.Growth },
                    { label: unit.statLabels?.cost || 'Cost', iconPath: STAT_ICONS.Cost, costEntries: unit.costEntries },
                    ...(unit.upgradeCostEntries && unit.upgradeCostEntries.length > 0
                      ? [{ label: 'Upgrade Cost', iconPath: STAT_ICONS.Cost, costEntries: unit.upgradeCostEntries }]
                      : []),
                  ]} />
                </div>
              </div>
        </div>

          {unit.creatureType && (
            <div>
              <h3 className="m-0 mb-3 text-lg text-foreground font-semibold">
                {unit.statLabels?.creatureTypeHeader || 'Creature Type'}
              </h3>
              <div className="bg-card border border-border rounded-2xl p-5">
                <div className="grid grid-cols-1 md:grid-cols-[80px_1fr] gap-4">
                  {unit.creatureType.icon && (
                    <ProgressiveIcon
                      iconPath={unit.creatureType.icon}
                      alt={unit.creatureType.name}
                      size={80}
                      className="shrink-0"
                    />
                  )}

                  <div className="flex flex-col justify-center">
                    {(() => {
                      const navigationId = unit.creatureType!.id || unit.creatureType!.nameSid;
                      return navigationId ? (
                        <button
                          onClick={() => handleAbilityClick(navigationId)}
                          className="text-lg font-semibold text-semantic-gold bg-transparent border-0 p-0 cursor-pointer hover:opacity-80 transition-opacity text-left"
                        >
                          {unit.creatureType!.name}
                        </button>
                      ) : (
                        <div className="text-lg font-semibold text-semantic-gold">
                          {unit.creatureType!.name}
                        </div>
                      );
                    })()}
                    {unit.creatureType.description && (
                      <RichText
                        text={unit.creatureType.description}
                        className="text-muted-foreground text-sm leading-relaxed whitespace-pre-wrap m-0 mt-2 block"
                      />
                    )}
                  </div>
                </div>
              </div>
            </div>
          )}

          {unit.passiveAbilities && unit.passiveAbilities.length > 0 && (
            <div>
              <h3 className="m-0 mb-3 text-lg text-foreground font-semibold">
                {unit.statLabels?.passiveAbilitiesHeader || 'Passive Abilities'}
              </h3>
              {unit.passiveAbilities.map((ability, index) => (
                <AbilityCard key={index} ability={ability} onAbilityClick={handleAbilityClick} />
              ))}
            </div>
          )}

          {unit.activeAbilities && unit.activeAbilities.length > 0 && (
            <div>
              <h3 className="m-0 mb-3 text-lg text-foreground font-semibold">
                {unit.statLabels?.activeAbilitiesHeader || 'Active Abilities'}
              </h3>
              {unit.activeAbilities.map((ability, index) => (
                <AbilityCard key={index} ability={ability} onAbilityClick={handleAbilityClick} />
              ))}
            </div>
          )}

          <HeroesSection
            heroes={unit.usedByHeroes}
            headerLabel={unit.statLabels?.heroesWithUnitHeader || 'Heroes starting with this Unit'}
            navigate={navigate}
          />
    </DetailContainer>
  );
}

export default function UnitsPage() {
  const navigate = useNavigate();
  const { unitId: urlUnitId } = useParams<{ unitId?: string }>();

  useHighlightText();

  const {
    selectedUnitId,
    setSelectedUnitId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useUnitsStore();

  const columnLabels = useColumnLabels();
  const { label } = useLabels();

  const prevUrlUnitIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlUnitId && urlUnitId !== selectedUnitId && urlUnitId !== prevUrlUnitIdRef.current) {
      setSelectedUnitId(urlUnitId);
    }
    prevUrlUnitIdRef.current = urlUnitId;
  }, [urlUnitId, selectedUnitId, setSelectedUnitId]);

  const unitsQuery = useUnits(searchQuery || undefined);
  const unitQuery = useUnit(selectedUnitId);

  const sortedUnits = useMemo(() => {
    if (!unitsQuery.data) return [];
    return [...unitsQuery.data].sort((a, b) =>
      compareUnits(a, b, sortField, sortDirection)
    );
  }, [unitsQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSort(field as UnitSortField, 'asc');
    }
  };

  const handleSelectUnit = (id: string) => {
    setSelectedUnitId(id);
    navigate(`/units/${id}`);
  };

  const handleSearchChange = (query: string) => {
    setSearchQuery(query);
  };

  const handleClearSearch = () => {
    setSearchQuery('');
  };

  interface UnitListProps {
    units: UnitListItemDto[];
    selectedUnitId: string | null;
    onSelectUnit: (id: string) => void;
    isLoading?: boolean;
  }

  function UnitList({ units, selectedUnitId, onSelectUnit, isLoading }: UnitListProps) {
    useEffect(() => {
      if (selectedUnitId && units.length > 0) {
        document.getElementById(`unit-${selectedUnitId}`)?.scrollIntoView({
          behavior: 'instant',
          block: 'nearest',
        });
      }
    }, [selectedUnitId, units.length]);

    if (isLoading) {
      return (
        <div className="p-5 text-center text-muted-foreground">
          {label('common_loading', ' ' + label('nav_units'))}
        </div>
      );
    }

    if (units.length === 0) {
      return (
        <div className="p-5 text-center text-muted-foreground">
          {label('no_results', label('nav_units'))}
        </div>
      );
    }

    return (
      <div className="flex flex-col gap-1">
        {units.map((unit) => (
          <button
            key={unit.id}
            id={`unit-${unit.id}`}
            onClick={() => onSelectUnit(unit.id)}
            className={cn(
              "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
              selectedUnitId === unit.id
                ? "bg-primary text-primary-foreground"
                : "bg-transparent text-foreground hover:bg-accent"
            )}
          >
            <ProgressiveIcon iconPath={unit.iconPath} alt={unit.name} size={40} />

            <div className="flex-1 min-w-0">
              <div className="font-medium overflow-hidden text-ellipsis whitespace-nowrap">
                {unit.name}
              </div>
              <div className={cn(
                "text-xs mt-0.5",
                selectedUnitId === unit.id ? "text-primary-foreground/70" : "text-muted-foreground"
              )}>
                {label('label_unit_tier')} {unit.tier || '?'} · {unit.factionDisplay || unit.faction || 'Unknown'} · {unit.id}
              </div>
            </div>
          </button>
        ))}
      </div>
    );
  }

  return (
    <div className="h-full overflow-hidden">
      <aside className="absolute left-0 top-0 bottom-0 w-80 lg:w-105 z-10 border-r border-border flex flex-col bg-card">
        <div className="px-2 h-[47px] border-b border-border flex items-center gap-0.5 relative">
          <SortableColumnHeader
            label={columnLabels.name}
            field="name"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />
          <SortableColumnHeader
            label={label('label_unit_tier')}
            field="tier"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />
          <SortableColumnHeader
            label={columnLabels.faction}
            field="faction"
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

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={handleSearchChange}
              placeholder={label('search_placeholder_entity', label('nav_units'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {unitsQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_units'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(unitsQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <UnitList
              units={sortedUnits}
              selectedUnitId={selectedUnitId}
              onSelectUnit={handleSelectUnit}
              isLoading={unitsQuery.isLoading}
            />
          )}
        </div>

        {unitsQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground">
            <span>{label('total_count', unitsQuery.data.length, label('nav_units'))}</span>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <UnitDetailPanel
            unit={unitQuery.data || null}
            selectedUnitId={selectedUnitId}
            error={unitQuery.error as Error | null}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
