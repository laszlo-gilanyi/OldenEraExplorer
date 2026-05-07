import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useFactionLawsStore, type FactionLawSortField } from './factionLawsStore';
import { useFactionLaws, useFactionLaw } from './useFactionLaws';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import RichText from '@/components/display/RichText';
import SortableColumnHeader from '@/components/display/SortableColumnHeader';
import type { FactionLawListItemDto, FactionLawDetailDto } from '@/api/types';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import FactionBadge from '@/components/display/FactionBadge';
import CurrencyBadge from '@/components/display/CurrencyBadge';
import DetailContainer, { CARD_WIDTH, FULL_WIDTH_CARD } from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';

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
            "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
            selectedLawId === law.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <ProgressiveIcon iconPath={law.icon} alt={law.name} size={32} />
          <div className="flex-1 min-w-0">
            <div className="font-medium overflow-hidden text-ellipsis whitespace-nowrap">
              {law.name}
            </div>
            <div className={cn(
              "text-xs mt-0.5",
              selectedLawId === law.id ? "text-primary-foreground/70" : "text-muted-foreground"
            )}>
              {law.factionDisplay || law.faction || label('label_unknown')}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

function FactionLawDetailPanel({
  factionLaw,
  selectedFactionLawId,
  error,
}: {
  factionLaw: FactionLawDetailDto | null;
  selectedFactionLawId: string | null;
  error?: Error | null;
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
    // Only show "select" message if no faction law is selected
    if (!selectedFactionLawId) {
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
    <DetailContainer>
        <div className={cn(FULL_WIDTH_CARD, "bg-card border border-border rounded-2xl p-5")}>
          <div className="flex flex-col md:flex-row items-start md:items-center gap-4">
            <ProgressiveIcon iconPath={factionLaw.icon} alt={factionLaw.localizedName || factionLaw.name} size={80} className="shrink-0" />
            <div>
              <h2 className="m-0 text-2xl font-semibold text-semantic-gold">
                {factionLaw.localizedName || factionLaw.name}
              </h2>
              <div className="mt-2">
                <FactionBadge
                  factionIcon={factionLaw.factionIcon}
                  factionDisplay={factionLaw.factionDisplay}
                  faction={factionLaw.faction}
                  iconSize={48}
                  textClassName="text-muted-foreground"
                />
              </div>
            </div>
          </div>
        </div>

        {factionLaw.levels && factionLaw.levels.length > 0 && (
          factionLaw.levels.map((level) => (
            <div
              key={level.level}
              className={cn(CARD_WIDTH, "p-5 bg-card rounded-xl border border-border flex flex-col")}
            >
              <div className="font-semibold text-semantic-gold text-lg mb-2">
                {label('detail_level', level.level)}
              </div>

              {level.description && (
                <RichText
                  text={level.description}
                  className="text-muted-foreground text-sm leading-relaxed whitespace-pre-wrap mb-3 block"
                />
              )}

              <div className="mt-auto pt-3 flex items-center gap-1.5">
                <span className="font-semibold text-foreground">
                  {factionLaw.statLabels?.cost || 'Cost'}:
                </span>
                <CurrencyBadge
                  amount={level.cost < 0 ? '?' : level.cost}
                  iconPath="Icon_LawsPoint"
                  displayName="Laws Point"
                  iconSize={32}
                  gap="gap-[5px]"
                  amountClassName="text-muted-foreground"
                />
              </div>
            </div>
          ))
        )}
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

  const prevUrlFactionLawIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlFactionLawId && urlFactionLawId !== selectedFactionLawId && urlFactionLawId !== prevUrlFactionLawIdRef.current) {
      setSelectedFactionLawId(urlFactionLawId);
    }
    prevUrlFactionLawIdRef.current = urlFactionLawId;
  }, [urlFactionLawId, selectedFactionLawId, setSelectedFactionLawId]);

  const factionLawsQuery = useFactionLaws(searchQuery || undefined);
  const factionLawQuery = useFactionLaw(selectedFactionLawId);

  const sortedFactionLaws = useMemo(() => {
    if (!factionLawsQuery.data) return [];
    return [...factionLawsQuery.data].sort((a, b) => {
      const cmp = sortField === 'name'
        ? (a.name || '').localeCompare(b.name || '')
        : (a.factionDisplay || a.faction || '').localeCompare(b.factionDisplay || b.faction || '');
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

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={handleSearchChange}
              placeholder={label('search_placeholder_entity', label('nav_faction_laws'))}
              collapsible={true}
              onClear={handleClearSearch}
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
              <span>{label('total_count', factionLawsQuery.data.length, label('nav_faction_laws'))}</span>
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
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
