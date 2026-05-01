import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useBuildingsStore, type BuildingSortField } from './buildingsStore';
import { useBuildings, useBuilding } from './useBuildings';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import RichText from '@/components/display/RichText';
import type { BuildingListItemDto, BuildingDetailDto, BuildingUpgradeOptionDto } from '@/api/types';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import FactionBadge from '@/components/display/FactionBadge';
import CurrencyBadge from '@/components/display/CurrencyBadge';
import SortableColumnHeader from '@/components/display/SortableColumnHeader';
import DetailContainer from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';


// Building List Component
interface BuildingListProps {
  buildings: BuildingListItemDto[];
  selectedBuildingId: string | null;
  onSelectBuilding: (id: string) => void;
  isLoading?: boolean;
}

function BuildingList({ buildings, selectedBuildingId, onSelectBuilding, isLoading }: BuildingListProps) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedBuildingId && buildings.length > 0) {
      document.getElementById(`building-${selectedBuildingId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedBuildingId, buildings.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_buildings'))}
      </div>
    );
  }

  if (buildings.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_buildings'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {buildings.map((building) => (
        <button
          key={building.id}
          id={`building-${building.id}`}
          onClick={() => onSelectBuilding(building.id)}
          className={cn(
            "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
            selectedBuildingId === building.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <div className="overflow-hidden rounded shrink-0" style={{ width: 40, height: 40 }}>
            <ProgressiveIcon
              iconPath={building.iconPath}
              alt={building.name}
              size={40}
              style={{ transform: 'scale(1.35)' }}
            />
          </div>
          <div className="flex-1 min-w-0">
            <div className="font-medium overflow-hidden text-ellipsis whitespace-nowrap">
              {building.name}
            </div>
            <div className="text-xs mt-0.5 text-muted-foreground">
              {building.factionDisplay || building.faction || 'Common'}{building.category ? ` · ${building.category}` : ''} · {label('detail_level', building.level)}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

// Building Detail Panel Component
interface BuildingDetailPanelProps {
  building: BuildingDetailDto | null;
  selectedBuildingId: string | null;
  error?: Error | null;
}

function BuildingDetailPanel({ building, selectedBuildingId, error }: BuildingDetailPanelProps) {
  const navigate = useNavigate();
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_building'), label(error.message))}
      </div>
    );
  }

  if (!building) {
    // Only show "select" message if no building is selected
    if (!selectedBuildingId) {
      return (
        <div className="p-10 text-center text-muted-foreground flex flex-col items-center justify-center h-full">
          <div className="text-5xl mb-4">[ ]</div>
          <div>{label('detail_select')}</div>
        </div>
      );
    }
    // Loading state - prevents flash
    return null;
  }

  return (
    <DetailContainer className="flex flex-col gap-7">
        <div className="bg-card border border-border rounded-2xl p-5">
          <div className="flex flex-col md:flex-row items-start gap-4">
            <div className="overflow-hidden rounded shrink-0" style={{ width: 100, height: 100 }}>
              <ProgressiveIcon
                iconPath={building.iconPath}
                alt={building.name}
                size={100}
                style={{ transform: 'scale(1.35)' }}
              />
            </div>
            <div className="flex-1">
              <h2 className="m-0 text-2xl font-semibold text-semantic-gold">
                {building.name}
              </h2>
              <div className="mt-2">
                <FactionBadge
                  factionIcon={building.factionIcon}
                  factionDisplay={building.factionDisplay}
                  faction={building.faction}
                  iconSize={48}
                  textClassName="text-muted-foreground"
                />
              </div>

              {building.description && (
                <div className="mt-3">
                  <RichText
                    text={building.description}
                    className="text-muted-foreground leading-relaxed block"
                  />
                </div>
              )}

              {building.costs && building.costs.length > 0 && (
                <div className="mt-3 flex items-center gap-2 flex-wrap">
                  <span className="font-semibold text-muted-foreground">
                    {building.costLabel}
                  </span>
                  {building.costs.map((cost, index) => (
                    <CurrencyBadge
                      key={index}
                      amount={cost.amount}
                      resourceKey={cost.resourceName}
                      iconSize={32}
                      gap="gap-[5px]"
                      amountClassName="text-muted-foreground"
                    />
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>

        {building.effects && building.effects.length > 0 && (
          <div className="space-y-3">
            {building.effects.map((effect, idx) => (
              <div
                key={idx}
                className="bg-card border border-border rounded-2xl p-5"
              >
                <div className="flex flex-col md:flex-row items-start gap-4">
                  <ProgressiveIcon
                    iconPath={effect.iconPath}
                    size={80}
                    className="shrink-0"
                  />
                  <RichText
                    text={effect.description}
                    className="text-muted-foreground leading-normal block"
                  />
                </div>
              </div>
            ))}
          </div>
        )}

        {building.upgradeOptions && building.upgradeOptions.length > 0 && (
          <BuildingUpgradeOptions options={building.upgradeOptions} label={building.upgradesLabel} />
        )}

        {building.requirements && building.requirements.length > 0 && (
          <div className="bg-card border border-border rounded-2xl p-5">
            <h3 className="m-0 mb-3 text-base font-semibold text-foreground">
              {building.requirementsLabel}
            </h3>
            <div className="flex flex-wrap gap-2">
              {building.requirements.map((req) => (
                <button
                  key={req.buildingId}
                  onClick={() => navigate(`/buildings/${req.buildingId}`)}
                  className="px-3 py-1.5 bg-muted border border-border rounded-md text-sm text-foreground cursor-pointer transition-colors hover:bg-accent hover:text-accent-foreground"
                >
                  {req.buildingName}
                </button>
              ))}
            </div>
          </div>
        )}

        {building.recruitableUnits && building.recruitableUnits.length > 0 && (
          <div className="bg-card border border-border rounded-2xl p-5">
            <h3 className="m-0 mb-3 text-base font-semibold text-foreground">
              {building.recruitableUnitsLabel}
            </h3>
            <div className="flex flex-wrap gap-2">
              {building.recruitableUnits.map((unit) => (
                <button
                  key={unit.unitId}
                  onClick={() => navigate(`/units/${unit.unitId}`)}
                  className="px-3 py-1.5 bg-muted border border-border rounded-md text-sm text-foreground cursor-pointer transition-colors hover:bg-accent hover:text-accent-foreground"
                >
                  {unit.unitName}
                </button>
              ))}
            </div>
          </div>
        )}
    </DetailContainer>
  );
}

interface BuildingUpgradeOptionsProps {
  options: BuildingUpgradeOptionDto[];
  label: string | null;
}

function BuildingUpgradeOptions({ options, label }: BuildingUpgradeOptionsProps) {
  return (
    <div className="bg-card border border-border rounded-2xl p-5">
      <h3 className="m-0 mb-3 text-base font-semibold text-foreground">
        {label}
      </h3>
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        {options.map((option) => (
          <div
            key={option.sid}
            className="flex flex-col items-center gap-3 p-4 bg-muted/30 border border-border/50 rounded-xl"
          >
            <ProgressiveIcon
              iconPath={option.iconPath}
              alt={option.sid}
              size={64}
              className="shrink-0"
            />
            <RichText
              text={option.description}
              className="text-muted-foreground text-sm leading-relaxed text-center block"
            />
          </div>
        ))}
      </div>
    </div>
  );
}

// Main Buildings Page Component
export default function BuildingsPage() {
  const navigate = useNavigate();
  const { buildingId: urlBuildingId } = useParams<{ buildingId?: string }>();

  useHighlightText();

  const {
    selectedBuildingId,
    setSelectedBuildingId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useBuildingsStore();

  const { label } = useLabels();
  const columnLabels = useColumnLabels();

  const prevUrlBuildingIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlBuildingId && urlBuildingId !== selectedBuildingId && urlBuildingId !== prevUrlBuildingIdRef.current) {
      setSelectedBuildingId(urlBuildingId);
    }
    prevUrlBuildingIdRef.current = urlBuildingId;
  }, [urlBuildingId, selectedBuildingId, setSelectedBuildingId]);

  const buildingsQuery = useBuildings(searchQuery || undefined);
  const buildingQuery = useBuilding(selectedBuildingId);

  const filteredAndSortedBuildings = useMemo(() => {
    if (!buildingsQuery.data) return [];

    // Sort - single level
    const dir = sortDirection === 'asc' ? 1 : -1;
    return [...buildingsQuery.data].sort((a, b) => {
      let cmp = 0;
      switch (sortField) {
        case 'name':
          cmp = (a.name || '').localeCompare(b.name || '');
          break;
        case 'faction':
          cmp = (a.factionDisplay || a.faction || '').localeCompare(b.factionDisplay || b.faction || '');
          break;
        case 'category':
          cmp = (a.category || '').localeCompare(b.category || '');
          break;
        case 'level':
          cmp = (a.level ?? 0) - (b.level ?? 0);
          break;
      }
      return cmp * dir;
    });
  }, [buildingsQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      // Toggle direction if same field
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      // New field, start with ascending
      setSort(field as BuildingSortField, 'asc');
    }
  };

  const handleSelectBuilding = (id: string) => {
    setSelectedBuildingId(id);
    navigate(`/buildings/${id}`);
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
            label={columnLabels.faction}
            field="faction"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />
          <SortableColumnHeader
            label={columnLabels.category}
            field="category"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />
          <SortableColumnHeader
            label={columnLabels.level}
            field="level"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={handleSearchChange}
              placeholder={label('search_placeholder_entity', label('nav_buildings'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {buildingsQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_buildings'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(buildingsQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <BuildingList
              buildings={filteredAndSortedBuildings}
              selectedBuildingId={selectedBuildingId}
              onSelectBuilding={handleSelectBuilding}
              isLoading={buildingsQuery.isLoading}
            />
          )}
        </div>

        {buildingsQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground flex flex-col gap-2">
            <div className="flex justify-between items-center">
              <span>
                {filteredAndSortedBuildings.length === buildingsQuery.data.length
                  ? label('total_count', buildingsQuery.data.length, label('nav_buildings'))
                  : label('filtered_count', filteredAndSortedBuildings.length, buildingsQuery.data.length, label('nav_buildings'))}
              </span>
            </div>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <BuildingDetailPanel
            building={buildingQuery.data || null}
            selectedBuildingId={selectedBuildingId}
            error={buildingQuery.error as Error | null}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
