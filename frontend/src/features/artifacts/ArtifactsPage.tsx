import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useArtifactsStore, type ArtifactSortField } from './artifactsStore';
import { useArtifacts, useArtifact } from './useArtifacts';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import RichText from '@/components/display/RichText';
import type { ArtifactListItemDto, ArtifactDetailDto } from '@/api/types';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import CurrencyBadge from '@/components/display/CurrencyBadge';
import SortableColumnHeader, { type SortDirection } from '@/components/display/SortableColumnHeader';
import DetailContainer from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';

/**
 * Single-level sorting for artifacts
 * Default: Name (asc)
 */
function compareArtifacts(
  a: ArtifactListItemDto,
  b: ArtifactListItemDto,
  primaryField: ArtifactSortField,
  primaryDirection: SortDirection
): number {
  const dir = primaryDirection === 'asc' ? 1 : -1;

  const compareName = () => (a.name || '').localeCompare(b.name || '');
  const compareId = () => a.id.localeCompare(b.id, undefined, { numeric: true });

  switch (primaryField) {
    case 'name':
      return compareName() * dir;
    case 'id':
      return compareId() * dir;
    default:
      return 0;
  }
}

// Rarity color classes using theme-aware CSS variables
const rarityColorClasses: Record<string, string> = {
  common: 'text-rarity-common',
  uncommon: 'text-rarity-uncommon',
  rare: 'text-rarity-rare',
  epic: 'text-rarity-epic',
  legendary: 'text-rarity-legendary',
  mythic: 'text-rarity-mythic',
};

function getRarityColorClass(rarity: string | null): string {
  if (!rarity) return 'text-muted-foreground';
  return rarityColorClasses[rarity.toLowerCase()] || 'text-muted-foreground';
}

// Rarity background classes for overlays
const rarityBgClasses: Record<string, string> = {
  common: 'bg-rarity-common',
  uncommon: 'bg-rarity-uncommon',
  rare: 'bg-rarity-rare',
  epic: 'bg-rarity-epic',
  legendary: 'bg-rarity-legendary',
  mythic: 'bg-rarity-mythic',
};

function getRarityBgClass(rarity: string | null): string {
  if (!rarity) return 'bg-muted';
  return rarityBgClasses[rarity.toLowerCase()] || 'bg-muted';
}

/**
 * Grid layout configuration for equipment slots.
 * Each position: [row, col] (0-indexed)
 *
 * Layout:
 * Row 0: Relic, Head, Back
 * Row 1: Ring1, Armor, Ring2
 * Row 2: LeftHand, Belt, RightHand
 * Row 3: Item1, (empty), Item2
 * Row 4: Item3, Boots, Item4
 */
const GRID_LAYOUT: Array<{ slot: string; slotIcon: string } | null> = [
  // Row 0
  { slot: 'unique_slot', slotIcon: 'UNIQUE_SLOT' },
  { slot: 'head', slotIcon: 'HEAD' },
  { slot: 'back', slotIcon: 'BACK' },
  // Row 1
  { slot: 'ring', slotIcon: 'RING' },       // Ring1
  { slot: 'armor', slotIcon: 'ARMOR' },
  { slot: 'ring2', slotIcon: 'RING' },      // Ring2
  // Row 2
  { slot: 'left_hand', slotIcon: 'LEFT_HAND' },
  { slot: 'belt', slotIcon: 'BELT' },
  { slot: 'right_hand', slotIcon: 'RIGHT_HAND' },
  // Row 3
  { slot: 'item_slot', slotIcon: 'ITEM_SLOT' },   // Item1
  null,  // Empty center
  { slot: 'item_slot2', slotIcon: 'ITEM_SLOT' },  // Item2
  // Row 4
  { slot: 'item_slot3', slotIcon: 'ITEM_SLOT' },  // Item3
  { slot: 'boots', slotIcon: 'BOOTS' },
  { slot: 'item_slot4', slotIcon: 'ITEM_SLOT' },  // Item4
];

/**
 * Normalizes slot name for comparison.
 */
function normalizeSlot(slot: string): string {
  return slot.toLowerCase().replace(/\s+/g, '_');
}

interface SetItemsGridProps {
  setItems: { artifactId: string; name: string; slot: string; icon: string | null }[];
  currentArtifactId: string;
  onNavigateToArtifact?: (id: string) => void;
}

/**
 * 3x5 grid component for displaying set items.
 * Shows item icons in their slot positions with empty slots as faded placeholders.
 * Uses indexed lookup for slots that can have multiple items (ring, item_slot).
 */
function SetItemsGrid({ setItems, currentArtifactId, onNavigateToArtifact }: SetItemsGridProps) {
  // Group items by normalized slot type for indexed lookup
  const ringItems = setItems.filter(i => normalizeSlot(i.slot) === 'ring');
  const itemSlotItems = setItems.filter(i => normalizeSlot(i.slot) === 'item_slot');

  // Create lookup map for single-slot items
  const singleSlotItems = new Map<string, typeof setItems[0]>();
  for (const item of setItems) {
    const normalized = normalizeSlot(item.slot);
    if (normalized !== 'ring' && normalized !== 'item_slot') {
      // Map common slot variations
      const key = normalized === 'armour' ? 'armor'
               : normalized === 'unic_slot' ? 'unique_slot'
               : normalized;
      singleSlotItems.set(key, item);
    }
  }

  // Get item for a grid position
  const getItemForPosition = (gridSlot: string): typeof setItems[0] | undefined => {
    switch (gridSlot) {
      case 'ring': return ringItems[0];      // Ring1
      case 'ring2': return ringItems[1];     // Ring2
      case 'item_slot': return itemSlotItems[0];   // Item1
      case 'item_slot2': return itemSlotItems[1];  // Item2
      case 'item_slot3': return itemSlotItems[2];  // Item3
      case 'item_slot4': return itemSlotItems[3];  // Item4
      default: return singleSlotItems.get(gridSlot);
    }
  };

  return (
    <div
      className="bg-muted/50 rounded-lg p-2"
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(3, 56px)',
        gridTemplateRows: 'repeat(5, 56px)',
        gap: '0px',
      }}
    >
      {GRID_LAYOUT.map((cell, idx) => {
        if (cell === null) {
          // Empty cell (no slot)
          return <div key={idx} style={{ width: 56, height: 56 }} />;
        }

        const item = getItemForPosition(cell.slot);

        if (item) {
          const isCurrent = item.artifactId === currentArtifactId;
          return (
            <div key={idx} className="transition-all rounded-md" style={{ width: 56, height: 56, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <button
                onClick={() => onNavigateToArtifact?.(item.artifactId)}
                title={item.name}
                className={cn(
                  "w-12 h-12 bg-muted dark:bg-black/40 rounded-md cursor-pointer p-0 flex items-center justify-center transition-all dark:hover:bg-accent",
                  isCurrent && "border-2 border-semantic-gold"
                )}
                onMouseEnter={(e) => {
                  if (!document.documentElement.classList.contains('theme-dark')) {
                    e.currentTarget.style.backgroundColor = '#505050';
                  }
                }}
                onMouseLeave={(e) => {
                  if (!document.documentElement.classList.contains('theme-dark')) {
                    e.currentTarget.style.backgroundColor = '';
                  }
                }}
              >
                <ProgressiveIcon iconPath={item.icon} alt={item.name} size={44} />
              </button>
            </div>
          );
        }

        // Empty slot placeholder with faded slot icon
        return (
          <div
            key={idx}
            style={{
              width: 56,
              height: 56,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <div className="w-12 h-12 bg-muted dark:bg-black/40 rounded-md flex items-center justify-center opacity-50">
              <ProgressiveIcon
                iconPath={`icons/item_slots/${cell.slotIcon}`}
                alt={cell.slot}
                size={44}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}

interface ArtifactListProps {
  artifacts: ArtifactListItemDto[];
  selectedArtifactId: string | null;
  onSelectArtifact: (id: string) => void;
  isLoading?: boolean;
}

function ArtifactList({ artifacts, selectedArtifactId, onSelectArtifact, isLoading }: ArtifactListProps) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedArtifactId && artifacts.length > 0) {
      document.getElementById(`artifact-${selectedArtifactId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedArtifactId, artifacts.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_artifacts'))}
      </div>
    );
  }

  if (artifacts.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_artifacts'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {artifacts.map((artifact) => (
        <button
          key={artifact.id}
          id={`artifact-${artifact.id}`}
          onClick={() => onSelectArtifact(artifact.id)}
          className={cn(
            "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
            selectedArtifactId === artifact.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <ProgressiveIcon iconPath={artifact.icon} alt={artifact.name} size={40} />
          <div className="flex-1 min-w-0">
            <div className={cn(
              "font-medium overflow-hidden text-ellipsis whitespace-nowrap",
              selectedArtifactId === artifact.id
                ? "text-primary-foreground"
                : getRarityColorClass(artifact.rarity)
            )}>
              {artifact.name}
            </div>
            <div className={cn(
              "text-xs mt-0.5",
              selectedArtifactId === artifact.id ? "text-primary-foreground/80" : "text-muted-foreground"
            )}>
              {artifact.raritySlotText || `${artifact.rarity || label('label_unknown')} ${artifact.slot || ''}`} · {artifact.id}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

interface ArtifactDetailPanelProps {
  artifact: ArtifactDetailDto | null;
  selectedArtifactId: string | null;
  error?: Error | null;
  onNavigateToArtifact?: (id: string) => void;
}

function ArtifactDetailPanel({ artifact, selectedArtifactId, error, onNavigateToArtifact }: ArtifactDetailPanelProps) {
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_artifact'), label(error.message))}
      </div>
    );
  }

  if (!artifact) {
    // Only show "select" message if no artifact is selected
    if (!selectedArtifactId) {
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
    <DetailContainer className="flex flex-col gap-5">
        <div className="bg-card border border-border rounded-2xl p-5">
          <div className="grid grid-cols-1 md:grid-cols-[auto_1fr] gap-4">
            <ProgressiveIcon
              iconPath={artifact.icon}
              alt={artifact.localizedName || artifact.name}
              size={112}
              className="shrink-0"
            />

            <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
              <h2 className={cn("m-0 text-2xl font-semibold", getRarityColorClass(artifact.rarity))}>
                {artifact.localizedName || artifact.name}
              </h2>

              <div className={cn("flex items-center gap-2 text-sm", getRarityColorClass(artifact.rarity))}>
                <span>{artifact.raritySlotText || `${artifact.rarity || label('label_unknown')} ${artifact.slot || 'Item'}`}</span>
                {artifact.slotIcon && (
                  <div
                    className={cn("w-6 h-6 shrink-0", getRarityBgClass(artifact.rarity))}
                    style={{
                      maskImage: `url(/api/assets/png/${artifact.slotIcon})`,
                      maskSize: 'contain',
                      maskRepeat: 'no-repeat',
                      maskPosition: 'center',
                      WebkitMaskImage: `url(/api/assets/png/${artifact.slotIcon})`,
                      WebkitMaskSize: 'contain',
                      WebkitMaskRepeat: 'no-repeat',
                      WebkitMaskPosition: 'center',
                    }}
                  />
                )}
              </div>

              {artifact.description && (
                <RichText
                  text={artifact.description}
                  className="text-muted-foreground leading-relaxed block"
                />
              )}

              {artifact.upgradeCost && (
                <div>
                  <div className="flex items-center gap-2">
                    <ProgressiveIcon iconPath="Button_LevelUp" size={32} alt="Upgrade" />
                    <CurrencyBadge
                      amount={artifact.upgradeCost}
                      resourceKey="dust"
                      displayName="Dust"
                      iconSize={32}
                      gap="gap-[5px]"
                      amountClassName="text-semantic-gold"
                    />
                  </div>
                  {artifact.upgradeCostNote && (
                    <div className="mt-1 text-xs italic text-muted-foreground">
                      {artifact.upgradeCostNote}
                    </div>
                  )}
                </div>
              )}

              {artifact.upgradeDescription && (
                <RichText
                  text={artifact.upgradeDescription}
                  className="m-0 text-semantic-gold font-semibold leading-relaxed block"
                />
              )}

              {artifact.destroyReward != null && (
                <div className="flex items-center gap-2">
                  <ProgressiveIcon iconPath="Button_Item_Delete" size={32} alt="Destroy" />
                  <CurrencyBadge
                    amount={artifact.destroyReward}
                    resourceKey="dust"
                    displayName="Dust"
                    iconSize={32}
                    gap="gap-[5px]"
                    amountClassName="text-foreground"
                  />
                </div>
              )}

              {artifact.narrativeDescription && (
                <RichText
                  text={artifact.narrativeDescription}
                  className="m-0 text-muted-foreground italic leading-relaxed block"
                />
              )}
            </div>
          </div>
        </div>

        {artifact.setBonus && (
          <div className="bg-card border border-border rounded-2xl p-5">
            <div className="grid grid-cols-1 md:grid-cols-[auto_1fr] gap-5">
              <SetItemsGrid
                setItems={artifact.setBonus.setItems}
                currentArtifactId={artifact.id}
                onNavigateToArtifact={onNavigateToArtifact}
              />

              <div>
                <h3 className="m-0 mb-2 text-base font-semibold text-semantic-gold">
                  {artifact.setBonus.setName}
                </h3>
                {artifact.setBonus.bonuses.map((bonus, index) => (
                  <div key={index} className="mt-2">
                    <div className="text-foreground text-sm font-semibold">
                      {bonus.header}
                    </div>
                    <RichText
                      text={bonus.effect}
                      className="text-muted-foreground text-sm"
                    />
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}
    </DetailContainer>
  );
}

export default function ArtifactsPage() {
  const navigate = useNavigate();
  const { artifactId: urlArtifactId } = useParams<{ artifactId?: string }>();

  useHighlightText();

  const {
    selectedArtifactId,
    setSelectedArtifactId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useArtifactsStore();

  const { label } = useLabels();
  const columnLabels = useColumnLabels();

  const prevUrlArtifactIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlArtifactId && urlArtifactId !== selectedArtifactId && urlArtifactId !== prevUrlArtifactIdRef.current) {
      setSelectedArtifactId(urlArtifactId);
    }
    prevUrlArtifactIdRef.current = urlArtifactId;
  }, [urlArtifactId, selectedArtifactId, setSelectedArtifactId]);

  const artifactsQuery = useArtifacts(searchQuery || undefined);
  const artifactQuery = useArtifact(selectedArtifactId);

  const sortedArtifacts = useMemo(() => {
    if (!artifactsQuery.data) return [];
    return [...artifactsQuery.data].sort((a, b) =>
      compareArtifacts(a, b, sortField, sortDirection)
    );
  }, [artifactsQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      // Toggle direction if same field
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      // New field, start with ascending
      setSort(field as ArtifactSortField, 'asc');
    }
  };

  const handleSelectArtifact = (id: string) => {
    setSelectedArtifactId(id);
    navigate(`/artifacts/${id}`);
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
              placeholder={label('search_placeholder_entity', label('nav_artifacts'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {artifactsQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_artifacts'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(artifactsQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <ArtifactList
              artifacts={sortedArtifacts}
              selectedArtifactId={selectedArtifactId}
              onSelectArtifact={handleSelectArtifact}
              isLoading={artifactsQuery.isLoading}
            />
          )}
        </div>

        {artifactsQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground flex flex-col gap-2">
            <div className="flex justify-between items-center">
              <span>{label('total_count', artifactsQuery.data.length, label('nav_artifacts'))}</span>
            </div>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <ArtifactDetailPanel
            artifact={artifactQuery.data || null}
            selectedArtifactId={selectedArtifactId}
            error={artifactQuery.error as Error | null}
            onNavigateToArtifact={handleSelectArtifact}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
