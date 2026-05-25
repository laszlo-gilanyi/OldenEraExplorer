import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import type {
  FactionLawDetailDto,
  FactionLawLayoutEntryDto,
  FactionLawLineDto,
  FactionLawListItemDto,
} from '@/api/types';
import CurrencyBadge from '@/components/display/CurrencyBadge';
import DetailContainer, { FULL_WIDTH_CARD } from '@/components/display/DetailContainer';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import RichText from '@/components/display/RichText';
import SortableColumnHeader from '@/components/display/SortableColumnHeader';
import SearchBox from '@/features/search/SearchBox';
import { useHighlightText } from '@/hooks/useHighlightText';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useImageStore } from '@/stores/imageStore';
import { cn } from '@/lib/utils';
import { useFactionLaw, useFactionLaws } from './useFactionLaws';
import { useFactionLawsStore, type FactionLawSortField } from './factionLawsStore';

// Native Scroll_Center.png aspect; the whole panel inherits it so the
// background image never needs squashing.
const PARCHMENT_ASPECT_RATIO = '2048 / 1432';

// At <=1759 px the DetailContainer drops to a single 40rem column, so the
// scroll panel needs the wider scaleNarrow value to stay legible.
const WIDE_BREAKPOINT = '(min-width: 1760px)';

const layout = {
  panel: {
    scaleWide: '60%',
    scaleNarrow: '95%',
  },

  crest: {
    width: '15%',
    height: '18.5%',
    top: '-0.6%',
  },

  scrollEnds: {
    width: '10%',
    height: '110%',
    top: '-5%',
    leftOffset: '-3%',
    rightOffset: '-3%',
  },

  rows: {
    firstTopPct: 22,
    spacingPct: 14,
    horizontalPadding: '10%',
    columnGap: '15%',
  },

  cell: {
    size: '7cqw',
    horizontalGap: '1cqw',
    innerIconRatio: '72%',
  },

  divider: {
    firstRow: {
      width: '5%',
      numberTop: '50%',
      numberSize: '40cqw',
    },
    otherRows: {
      width: '6%',
      numberTop: '34%',
      numberSize: '32cqw',
      labelTop: '79%',
      labelSize: '22cqw',
    },
    textColor: '#363632',
  },

  // `inset` must match the inset on Frame_Law_Back.png so the ring lands
  // exactly on the frame's visible edge.
  selectedRing: {
    inset: '12%',
    thickness: '5cqw',
    radius: '12%',
    color: '#D4AF37',
  },

  header: {
    minHeight: '10rem',
    nameMinWidth: '20%',
  },
} as const;

// Without the version/retryCount query suffix the browser keeps a 404 cached
// from before extraction finished, and the image never recovers without a
// hard reload. ProgressiveIcon handles this internally; raw <img> and
// backgroundImage need this helper.
function useAssetUrl(): (path: string) => string {
  const version = useImageStore((s) => s.version);
  const retryCount = useImageStore((s) => s.retryCount);
  return (path: string) => `/api/assets/png/${path}?v=${version}_${retryCount}`;
}

function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(
    () => typeof window !== 'undefined' && window.matchMedia(query).matches,
  );
  useEffect(() => {
    const mql = window.matchMedia(query);
    const handler = () => setMatches(mql.matches);
    mql.addEventListener('change', handler);
    return () => mql.removeEventListener('change', handler);
  }, [query]);
  return matches;
}

// Renders the panel's contents at "natural" size (1/scale) and then visually
// shrinks via CSS transform. Percentage and cqw values inside the panel stay
// proportional after scaling, including pixel-defined details like the
// selected-cell ring.
function usePanelScale(): { width: string; innerSize: string; transform: string } {
  const isWide = useMediaQuery(WIDE_BREAKPOINT);
  const width = isWide ? layout.panel.scaleWide : layout.panel.scaleNarrow;
  const scale = parseFloat(width) / 100;
  return {
    width,
    innerSize: `${100 / scale}%`,
    transform: `scale(${scale})`,
  };
}

function rowCenterTop(idx: number): string {
  return `${layout.rows.firstTopPct + idx * layout.rows.spacingPct}%`;
}

function FactionLawList({
  factionLaws,
  selectedLawId,
  onSelectLaw,
  isLoading,
}: {
  factionLaws: FactionLawListItemDto[];
  selectedLawId: string | null;
  onSelectLaw: (id: string) => void;
  isLoading?: boolean;
}) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedLawId && factionLaws.length > 0) {
      document.getElementById(`factionlaw-${selectedLawId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedLawId, factionLaws.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_faction_laws'))}
      </div>
    );
  }

  if (factionLaws.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_faction_laws'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {factionLaws.map((law) => (
        <button
          key={law.id}
          id={`factionlaw-${law.id}`}
          onClick={() => onSelectLaw(law.id)}
          className={cn(
            'flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors',
            selectedLawId === law.id
              ? 'bg-primary text-primary-foreground'
              : 'bg-transparent text-foreground hover:bg-accent',
          )}
        >
          <ProgressiveIcon iconPath={law.icon} alt={law.name} size={32} />
          <div className="flex-1 min-w-0">
            <div className="font-medium overflow-hidden text-ellipsis whitespace-nowrap">
              {law.name}
            </div>
            <div
              className={cn(
                'text-xs mt-0.5',
                selectedLawId === law.id ? 'text-primary-foreground/70' : 'text-muted-foreground',
              )}
            >
              {law.factionDisplay || law.faction || label('label_unknown')}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

function FactionLawCostRow({ cost, costLabel }: { cost: number; costLabel: string }) {
  return (
    <div className="flex items-center gap-1.5">
      <span className="font-semibold text-foreground text-sm">{costLabel}:</span>
      <CurrencyBadge
        amount={cost < 0 ? '?' : cost}
        iconPath="Icon_LawsPoint"
        displayName="Laws Point"
        iconSize={28}
        gap="gap-[5px]"
        amountClassName="text-muted-foreground"
      />
    </div>
  );
}

function FactionLawHeaderContent({ factionLaw }: { factionLaw: FactionLawDetailDto }) {
  const { label } = useLabels();
  const levels = factionLaw.levels ?? [];
  const isMultiLevel = levels.length > 1;
  const costLabel = factionLaw.statLabels?.cost || 'Cost';
  const displayName = factionLaw.localizedName || factionLaw.name;
  // Single-level laws still need one card (description + cost) but with no
  // "Level 1" badge.
  const levelsToRender = isMultiLevel ? levels : levels.slice(0, 1);

  return (
    <div className="flex flex-col min-[1760px]:flex-row items-stretch gap-4 h-full">
      <div
        className="flex flex-row items-center gap-3 shrink-0 min-[1760px]:self-center"
        style={{ minWidth: layout.header.nameMinWidth }}
      >
        <ProgressiveIcon iconPath={factionLaw.icon} alt={displayName} size={80} />
        <h2 className="text-xl font-semibold text-semantic-gold m-0 max-w-[14ch] text-balance leading-tight">
          {displayName}
        </h2>
      </div>

      <div className="flex-1 flex flex-col min-[1760px]:flex-row items-stretch gap-3 min-w-0">
        {levelsToRender.map((l) => (
          <div
            key={l.level}
            className="flex-1 border-l-2 border-semantic-gold/40 pl-3 flex flex-col min-w-0"
          >
            {isMultiLevel && (
              <div className="font-semibold text-semantic-gold text-base mb-2">
                {label('detail_level', l.level)}
              </div>
            )}
            {l.description && (
              <RichText
                text={l.description}
                className="text-muted-foreground text-sm leading-relaxed whitespace-pre-wrap block"
              />
            )}
            <div className="mt-auto pt-3">
              <FactionLawCostRow cost={l.cost} costLabel={costLabel} />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function FactionLawCell({
  entry,
  isCurrent,
  onSelect,
}: {
  entry: FactionLawLayoutEntryDto;
  isCurrent: boolean;
  onSelect: (lawId: string) => void;
}) {
  const assetUrl = useAssetUrl();
  const frameBackUrl = assetUrl('Frame_Law_Back.png');
  return (
    <div className="relative w-full h-full" style={{ containerType: 'inline-size' }}>
      <button
        onClick={() => onSelect(entry.id)}
        title={entry.name || entry.id}
        className="w-full h-full p-0 flex items-center justify-center cursor-pointer transition-all bg-transparent border-0"
        style={{
          backgroundImage: `url(${frameBackUrl})`,
          backgroundSize: '100% 100%',
          backgroundRepeat: 'no-repeat',
        }}
      >
        <div
          className="flex items-center justify-center"
          style={{ width: layout.cell.innerIconRatio, height: layout.cell.innerIconRatio }}
        >
          <ProgressiveIcon
            iconPath={entry.icon}
            alt={entry.name || entry.id}
            size={72}
            style={{ width: '100%', height: '100%' }}
          />
        </div>
      </button>

      {isCurrent && (
        <div
          className="pointer-events-none absolute"
          style={{
            top: layout.selectedRing.inset,
            left: layout.selectedRing.inset,
            right: layout.selectedRing.inset,
            bottom: layout.selectedRing.inset,
            border: `${layout.selectedRing.thickness} solid ${layout.selectedRing.color}`,
            borderRadius: layout.selectedRing.radius,
          }}
        />
      )}

      {entry.levelCount > 0 && (
        <div
          className="pointer-events-none absolute left-0 right-0 flex justify-center z-10"
          style={{ top: '-4cqw', gap: '2cqw' }}
        >
          {Array.from({ length: entry.levelCount }).map((_, i) => (
            <ProgressiveIcon
              key={i}
              iconPath="LevelPoint (1)"
              alt=""
              style={{ width: '15cqw', height: '15cqw' }}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function FactionLawGroupRow({
  entries,
  currentLawId,
  onSelect,
}: {
  entries: FactionLawLayoutEntryDto[];
  currentLawId: string;
  onSelect: (lawId: string) => void;
}) {
  return (
    <div className="flex justify-center items-center" style={{ gap: layout.cell.horizontalGap }}>
      {entries.map((entry) => (
        <div key={entry.id} style={{ width: layout.cell.size, aspectRatio: '1 / 1' }}>
          <FactionLawCell
            entry={entry}
            isCurrent={entry.id === currentLawId}
            onSelect={onSelect}
          />
        </div>
      ))}
    </div>
  );
}

function FactionLawRowDivider({
  rowIndex,
  countToUnlock,
}: {
  rowIndex: number;
  countToUnlock: number;
}) {
  const isFirst = rowIndex === 0;
  const cfg = isFirst ? layout.divider.firstRow : layout.divider.otherRows;
  const imageName = isFirst ? 'Frame_LawLevel (1)' : 'Frame_LawLevel_Loced (1)';

  return (
    <div
      className="absolute pointer-events-none"
      style={{
        top: rowCenterTop(rowIndex),
        left: '50%',
        transform: 'translate(-50%, -50%)',
        width: cfg.width,
        containerType: 'inline-size',
      }}
    >
      <div className="relative">
        <ProgressiveIcon iconPath={imageName} alt="" className="w-full block" style={{ width: '100%', height: 'auto' }} />
        <span
          className="absolute left-1/2 -translate-x-1/2 -translate-y-1/2 font-bold leading-none"
          style={{
            top: cfg.numberTop,
            fontSize: cfg.numberSize,
            color: layout.divider.textColor,
          }}
        >
          {rowIndex + 1}
        </span>
        {!isFirst && (
          <div
            className="absolute left-1/2 -translate-x-1/2 -translate-y-1/2 flex items-center justify-center gap-[0.15em] font-semibold leading-none whitespace-nowrap"
            style={{
              top: layout.divider.otherRows.labelTop,
              fontSize: layout.divider.otherRows.labelSize,
              color: layout.divider.textColor,
            }}
          >
            <span>{countToUnlock}</span>
            <ProgressiveIcon
              iconPath="Icon_LawsPoint"
              alt=""
              style={{ width: '1.2em', height: '1.2em' }}
            />
          </div>
        )}
      </div>
    </div>
  );
}

function FactionLawScrollPanel({
  faction,
  lines,
  currentLawId,
  onSelect,
}: {
  faction: string | null;
  lines: FactionLawLineDto[];
  currentLawId: string;
  onSelect: (lawId: string) => void;
}) {
  const assetUrl = useAssetUrl();
  const scale = usePanelScale();

  return (
    <div
      className="relative mx-auto"
      style={{ width: scale.width, aspectRatio: PARCHMENT_ASPECT_RATIO }}
    >
      <div
        className="absolute top-0 left-0"
        style={{
          width: scale.innerSize,
          height: scale.innerSize,
          transform: scale.transform,
          transformOrigin: 'top left',
          aspectRatio: PARCHMENT_ASPECT_RATIO,
          backgroundImage: `url(${assetUrl('Scroll_Center.png')})`,
          backgroundSize: '100% 100%',
          backgroundRepeat: 'no-repeat',
          containerType: 'inline-size',
        }}
      >
        <ProgressiveIcon
          iconPath="Scroll_Left"
          alt=""
          className="absolute pointer-events-none"
          style={{
            top: layout.scrollEnds.top,
            left: layout.scrollEnds.leftOffset,
            width: layout.scrollEnds.width,
            height: layout.scrollEnds.height,
          }}
        />
        <ProgressiveIcon
          iconPath="Scroll_Right"
          alt=""
          className="absolute pointer-events-none"
          style={{
            top: layout.scrollEnds.top,
            right: layout.scrollEnds.rightOffset,
            width: layout.scrollEnds.width,
            height: layout.scrollEnds.height,
          }}
        />

        {faction && (
          <ProgressiveIcon
            iconPath={`icons/fraction_laws_main_icons/scroll_faction_${faction}`}
            alt=""
            className="absolute pointer-events-none"
            style={{
              top: layout.crest.top,
              left: '50%',
              transform: 'translateX(-50%)',
              width: layout.crest.width,
              height: layout.crest.height,
            }}
          />
        )}

        {lines.map((line, idx) => (
          <FactionLawRowDivider
            key={`divider-${idx}`}
            rowIndex={idx}
            countToUnlock={line.countToUnlock}
          />
        ))}

        {lines.map((line, idx) => (
          <div
            key={`row-${idx}`}
            className="absolute grid items-center"
            style={{
              top: rowCenterTop(idx),
              left: layout.rows.horizontalPadding,
              right: layout.rows.horizontalPadding,
              transform: 'translateY(-50%)',
              gridTemplateColumns: '1fr 1fr',
              columnGap: layout.rows.columnGap,
            }}
          >
            <FactionLawGroupRow
              entries={line.groups[0]?.laws ?? []}
              currentLawId={currentLawId}
              onSelect={onSelect}
            />
            <FactionLawGroupRow
              entries={line.groups[1]?.laws ?? []}
              currentLawId={currentLawId}
              onSelect={onSelect}
            />
          </div>
        ))}
      </div>
    </div>
  );
}

function FactionLawDetailPanel({
  factionLaw,
  selectedFactionLawId,
  error,
  onSelectLaw,
}: {
  factionLaw: FactionLawDetailDto | null;
  selectedFactionLawId: string | null;
  error?: Error | null;
  onSelectLaw: (id: string) => void;
}) {
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_faction_law'), label(error.message))}
      </div>
    );
  }

  if (!factionLaw) {
    if (!selectedFactionLawId) {
      return (
        <div className="p-10 text-center text-muted-foreground flex flex-col items-center justify-center h-full">
          <div className="text-5xl mb-4">[ ]</div>
          <div>{label('detail_select')}</div>
        </div>
      );
    }
    // Render nothing while the detail query is in flight: showing the empty
    // "select a law" state would flash between selections.
    return null;
  }

  const lines = factionLaw.layout;

  return (
    <DetailContainer>
      <div className={cn(FULL_WIDTH_CARD, 'flex flex-col items-center gap-10')}>
        <div
          className="bg-card border border-border rounded-xl p-3 w-full"
          style={{ minHeight: layout.header.minHeight }}
        >
          <FactionLawHeaderContent factionLaw={factionLaw} />
        </div>

        {lines && lines.length > 0 && (
          <div className="w-full">
            <FactionLawScrollPanel
              faction={factionLaw.faction}
              lines={lines}
              currentLawId={factionLaw.id}
              onSelect={onSelectLaw}
            />
          </div>
        )}
      </div>
    </DetailContainer>
  );
}

export default function FactionLawsPage() {
  const navigate = useNavigate();
  const { factionLawId: urlFactionLawId } = useParams<{ factionLawId?: string }>();

  useHighlightText();

  const {
    selectedFactionLawId,
    setSelectedFactionLawId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useFactionLawsStore();

  const columnLabels = useColumnLabels();
  const { label } = useLabels();

  // The ref guard makes the URL → store sync fire only on actual URL
  // transitions. Without it, every store update would re-trigger the effect
  // and overwrite the user's freshly-typed selection.
  const prevUrlFactionLawIdRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    if (
      urlFactionLawId &&
      urlFactionLawId !== selectedFactionLawId &&
      urlFactionLawId !== prevUrlFactionLawIdRef.current
    ) {
      setSelectedFactionLawId(urlFactionLawId);
    }
    prevUrlFactionLawIdRef.current = urlFactionLawId;
  }, [urlFactionLawId, selectedFactionLawId, setSelectedFactionLawId]);

  const factionLawsQuery = useFactionLaws(searchQuery || undefined);
  const factionLawQuery = useFactionLaw(selectedFactionLawId);

  const sortedFactionLaws = useMemo(() => {
    if (!factionLawsQuery.data) return [];
    return [...factionLawsQuery.data].sort((a, b) => {
      const cmp =
        sortField === 'name'
          ? (a.name || '').localeCompare(b.name || '')
          : (a.factionDisplay || a.faction || '').localeCompare(
              b.factionDisplay || b.faction || '',
            );
      return sortDirection === 'asc' ? cmp : -cmp;
    });
  }, [factionLawsQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSort(field as FactionLawSortField, 'asc');
    }
  };

  const handleSelectFactionLaw = (id: string) => {
    setSelectedFactionLawId(id);
    navigate(`/faction-laws/${id}`);
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

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={setSearchQuery}
              placeholder={label('search_placeholder_entity', label('nav_faction_laws'))}
              collapsible={true}
              onClear={() => setSearchQuery('')}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {factionLawsQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_faction_laws'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(factionLawsQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <FactionLawList
              factionLaws={sortedFactionLaws}
              selectedLawId={selectedFactionLawId}
              onSelectLaw={handleSelectFactionLaw}
              isLoading={factionLawsQuery.isLoading}
            />
          )}
        </div>

        {factionLawsQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground flex flex-col gap-2">
            <div className="flex justify-between items-center">
              <span>
                {label('total_count', factionLawsQuery.data.length, label('nav_faction_laws'))}
              </span>
            </div>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <FactionLawDetailPanel
            factionLaw={factionLawQuery.data || null}
            selectedFactionLawId={selectedFactionLawId}
            error={factionLawQuery.error as Error | null}
            onSelectLaw={handleSelectFactionLaw}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
