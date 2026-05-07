import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useAbilitiesStore, type AbilitySortField } from './abilitiesStore';
import { useAbilities, useAbility } from './useAbilities';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import RichText from '@/components/display/RichText';
import SortableColumnHeader from '@/components/display/SortableColumnHeader';
import type { AbilityListItemDto, AbilityDetailDto } from '@/api/types';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import EntityChip from '@/components/display/EntityChip';
import DetailContainer, { CARD_WIDTH, FULL_WIDTH_CARD } from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';

interface AbilityListProps {
  abilities: AbilityListItemDto[];
  selectedAbilityId: string | null;
  onSelectAbility: (id: string) => void;
  isLoading?: boolean;
}

function AbilityList({ abilities, selectedAbilityId, onSelectAbility, isLoading }: AbilityListProps) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedAbilityId && abilities.length > 0) {
      document.getElementById(`ability-${selectedAbilityId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedAbilityId, abilities.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_abilities'))}
      </div>
    );
  }

  if (abilities.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_abilities'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {abilities.map((ability) => (
        <button
          key={ability.id}
          id={`ability-${ability.id}`}
          onClick={() => onSelectAbility(ability.id)}
          className={cn(
            "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
            selectedAbilityId === ability.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <ProgressiveIcon iconPath={ability.icon} alt={ability.name} size={32} />
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 overflow-hidden">
              <span className="font-medium overflow-hidden text-ellipsis whitespace-nowrap">
                {ability.name}
              </span>
              {ability.abilityType === 'Orphan' && (
                <span className="text-[10px] px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-400 font-medium uppercase tracking-wide flex-shrink-0">
                  {label('viewer_orphan')}
                </span>
              )}
            </div>
            <div className={cn(
              "text-xs mt-0.5",
              selectedAbilityId === ability.id ? "text-primary-foreground/70" : "text-muted-foreground"
            )}>
              {ability.id}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

interface AbilityDetailPanelProps {
  ability: AbilityDetailDto | null;
  selectedAbilityId: string | null;
  error?: Error | null;
}

function AbilityDetailPanel({ ability, selectedAbilityId, error }: AbilityDetailPanelProps) {
  const { label } = useLabels();
  const navigate = useNavigate();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_ability'), label(error.message))}
      </div>
    );
  }

  if (!ability) {
    if (!selectedAbilityId) {
      return (
        <div className="p-10 text-center text-muted-foreground flex flex-col items-center justify-center h-full">
          <div className="text-5xl mb-4">[ ]</div>
          <div>{label('detail_select')}</div>
        </div>
      );
    }
    return null;
  }

  const isActive = ability.abilityType === 'Active';

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
          className="text-muted-foreground text-sm leading-relaxed whitespace-pre-wrap block"
        />
      )}

      {ability.infoNotes && ability.infoNotes.length > 0 && (
        <div className="mt-2">
          {ability.infoNotes.map((note, index) => (
            <RichText
              key={index}
              text={note}
              className="text-muted-foreground/70 text-sm italic block"
            />
          ))}
        </div>
      )}
    </>
  );

  return (
    <DetailContainer>
        <div className={cn(FULL_WIDTH_CARD, "bg-card border border-border rounded-2xl p-5")}>
          {isActive ? (
            <div className="grid grid-cols-1 md:grid-cols-[100px_140px_1fr] gap-4">
              <ProgressiveIcon iconPath={ability.icon} alt={ability.name} size={100} className="shrink-0" />

              <div className="flex flex-col justify-center">
                <h2 className="m-0 text-xl font-semibold text-semantic-gold">
                  {ability.name}
                </h2>
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
            <div className="grid grid-cols-1 md:grid-cols-[100px_1fr] gap-4">
              <ProgressiveIcon iconPath={ability.icon} alt={ability.name} size={100} className="shrink-0" />

              <div className="flex flex-col justify-center">
                <h2 className="m-0 text-xl font-semibold text-semantic-gold">
                  {ability.name}
                </h2>
                {ability.abilityTypeSid && (
                  <div className="text-muted-foreground text-sm mt-1">
                    {ability.abilityTypeSid}
                  </div>
                )}
                <div className="mt-3">
                  {descriptionContent}
                </div>
              </div>
            </div>
          )}
        </div>
        {ability.sourceUnitIds && ability.sourceUnitIds.length > 0 && (
          <div className={cn(CARD_WIDTH, "bg-card border border-border rounded-2xl p-5")}>
            <h3 className="m-0 mb-3 text-base font-semibold text-foreground">
              {ability.statLabels?.creaturesWithAbility || 'Creatures with this Ability'}
            </h3>
            <div className="grid grid-cols-[repeat(auto-fill,minmax(11rem,11.5rem))] gap-3">
              {ability.sourceUnitIds.map((unitId, index) => {
                const unitName = ability.sourceUnitNames?.[index] || unitId;
                return (
                  <EntityChip
                    key={unitId}
                    iconPath={`icons/units/hex_portraits/${unitId}`}
                    name={unitName}
                    onClick={() => navigate(`/units/${unitId}`)}
                  />
                );
              })}
            </div>
          </div>
        )}
    </DetailContainer>
  );
}

export default function AbilitiesPage() {
  const navigate = useNavigate();
  const { abilityId: urlAbilityId } = useParams<{ abilityId?: string }>();

  useHighlightText();

  const {
    selectedAbilityId,
    setSelectedAbilityId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useAbilitiesStore();

  const columnLabels = useColumnLabels();
  const { label } = useLabels();

  const prevUrlAbilityIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlAbilityId && urlAbilityId !== selectedAbilityId && urlAbilityId !== prevUrlAbilityIdRef.current) {
      setSelectedAbilityId(decodeURIComponent(urlAbilityId));
    }
    prevUrlAbilityIdRef.current = urlAbilityId;
  }, [urlAbilityId, selectedAbilityId, setSelectedAbilityId]);

  const abilitiesQuery = useAbilities(searchQuery || undefined);
  const abilityQuery = useAbility(selectedAbilityId);

  const processedAbilities = useMemo(() => {
    if (!abilitiesQuery.data) return [];
    return [...abilitiesQuery.data].sort((a, b) => {
      const cmp = sortField === 'name'
        ? (a.name || '').localeCompare(b.name || '')
        : a.id.localeCompare(b.id, undefined, { numeric: true });
      return sortDirection === 'asc' ? cmp : -cmp;
    });
  }, [abilitiesQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSort(field as AbilitySortField, 'asc');
    }
  };

  const handleSelectAbility = (id: string) => {
    setSelectedAbilityId(id);
    navigate(`/abilities/${encodeURIComponent(id)}`);
  };

  const handleSearchChange = (query: string) => {
    setSearchQuery(query);
  };

  const handleClearSearch = () => {
    setSearchQuery('');
  };

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
              placeholder={label('search_placeholder_entity', label('nav_abilities'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {abilitiesQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_abilities'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(abilitiesQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <AbilityList
              abilities={processedAbilities}
              selectedAbilityId={selectedAbilityId}
              onSelectAbility={handleSelectAbility}
              isLoading={abilitiesQuery.isLoading}
            />
          )}
        </div>

        {abilitiesQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground">
            <span>
              {processedAbilities.length === abilitiesQuery.data.length
                ? label('total_count', abilitiesQuery.data.length, label('nav_abilities'))
                : label('filtered_count', processedAbilities.length, abilitiesQuery.data.length, label('nav_abilities'))
              }
            </span>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <AbilityDetailPanel
            ability={abilityQuery.data || null}
            selectedAbilityId={selectedAbilityId}
            error={abilityQuery.error as Error | null}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
