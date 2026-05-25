import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useSpellsStore, type SpellSortField } from './spellsStore';
import { useSpells, useSpell } from './useSpells';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import UsedBySection from '@/components/display/UsedBySection';
import SortableColumnHeader, { type SortDirection } from '@/components/display/SortableColumnHeader';
import type { SpellListItemDto, SpellDetailDto, SkillReferenceDto } from '@/api/types';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import RichText from '@/components/display/RichText';
import DetailContainer, { CARD_WIDTH, FULL_WIDTH_CARD } from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';

/**
 * Multi-level sorting configuration.
 *
 * Uses BaseNameForSort to keep masterful spells grouped with their base spell:
 * - "Early Start" and "Masterful Early Start" both have baseNameForSort = "Early Start"
 *
 * - School click: School (direction) -> Rank (asc) -> BaseNameForSort (asc) -> IsMasterful (asc)
 * - Rank click: Rank (direction) -> School (asc) -> BaseNameForSort (asc) -> IsMasterful (asc)
 * - Category click: Category (direction) -> Rank (asc) -> BaseNameForSort (asc) -> IsMasterful (asc)
 * - Name click: BaseNameForSort (direction) -> IsMasterful (asc)
 * - Default (initial): School (asc) -> Rank (asc) -> BaseNameForSort (asc) -> IsMasterful (asc)
 */
function compareSpells(
  a: SpellListItemDto,
  b: SpellListItemDto,
  primaryField: SpellSortField,
  primaryDirection: SortDirection
): number {
  const dir = primaryDirection === 'asc' ? 1 : -1;

  const compareSchool = () =>
    (a.schoolDisplay || a.school || '').localeCompare(b.schoolDisplay || b.school || '');
  const compareRank = () => (a.rank ?? 0) - (b.rank ?? 0);
  // Use baseNameForSort to keep masterful spells grouped with their base spell
  const compareBaseName = () => (a.baseNameForSort || a.name || '').localeCompare(b.baseNameForSort || b.name || '');
  const compareCategory = () => (a.category || '').localeCompare(b.category || '');
  // IsMasterful: masterful spells should appear after their base spell
  const compareMasterful = () => (a.isMasterful ? 1 : 0) - (b.isMasterful ? 1 : 0);

  switch (primaryField) {
    case 'school':
      // School (dir) -> Rank (asc) -> BaseNameForSort (asc) -> IsMasterful (asc)
      return compareSchool() * dir || compareRank() || compareBaseName() || compareMasterful();

    case 'rank':
      // Rank (dir) -> School (asc) -> BaseNameForSort (asc) -> IsMasterful (asc)
      return compareRank() * dir || compareSchool() || compareBaseName() || compareMasterful();

    case 'category':
      // Category (dir) -> Rank (asc) -> BaseNameForSort (asc) -> IsMasterful (asc)
      return compareCategory() * dir || compareRank() || compareBaseName() || compareMasterful();

    case 'name':
      // BaseNameForSort (dir) -> IsMasterful (asc)
      return compareBaseName() * dir || compareMasterful();

    default:
      return 0;
  }
}

// School color classes using theme-aware CSS variables
// Keys match the raw school values from game data
const schoolColorClasses: Record<string, string> = {
  day: 'text-school-day',
  night: 'text-school-night',
  primal: 'text-school-primal',
  space: 'text-school-space',
  neutral: 'text-school-neutral',
};

function getSchoolColorClass(school: string | null | undefined): string {
  if (!school) return 'text-foreground';
  return schoolColorClasses[school] || 'text-foreground';
}

function SpellList({
  spells,
  selectedSpellId,
  onSelectSpell,
  isLoading,
}: {
  spells: SpellListItemDto[];
  selectedSpellId: string | null;
  onSelectSpell: (id: string) => void;
  isLoading?: boolean;
}) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedSpellId && spells.length > 0) {
      document.getElementById(`spell-${selectedSpellId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedSpellId, spells.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_spells'))}
      </div>
    );
  }

  if (spells.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_spells'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {spells.map((spell) => (
        <button
          key={spell.id}
          id={`spell-${spell.id}`}
          onClick={() => onSelectSpell(spell.id)}
          className={cn(
            "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
            selectedSpellId === spell.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <ProgressiveIcon iconPath={spell.icon} alt={spell.name} size={32} />
          <div className="flex-1 min-w-0">
            <div
              className={cn(
                "font-medium overflow-hidden text-ellipsis whitespace-nowrap",
                selectedSpellId === spell.id
                  ? "text-primary-foreground"
                  : getSchoolColorClass(spell.school)
              )}
            >
              {spell.name}
            </div>
            <div className={cn(
              "text-xs mt-0.5 flex gap-2",
              selectedSpellId === spell.id
                ? "text-primary-foreground/70"
                : "text-muted-foreground"
            )}>
              <span>{spell.schoolTierText || `${spell.schoolDisplay || spell.school || label('label_unknown')} ${spell.rank}`}</span>
              <span>·</span>
              <span>{spell.category}</span>
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

function SpellDetailPanel({
  spell,
  selectedSpellId,
  error,
}: {
  spell: SpellDetailDto | null;
  selectedSpellId: string | null;
  error?: Error | null;
}) {
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_spell'), label(error.message))}
      </div>
    );
  }

  if (!spell) {
    // Only show "select" message if no spell is selected
    if (!selectedSpellId) {
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

  const schoolColorClass = getSchoolColorClass(spell.school);

  return (
    <DetailContainer>
        <div className={cn(FULL_WIDTH_CARD, "bg-card border border-border rounded-2xl p-5")}>
          <div className="flex flex-col md:flex-row items-start gap-4">
            <ProgressiveIcon
              iconPath={spell.icon}
              alt={spell.localizedName || spell.name}
              size={100}
              className="shrink-0"
            />
            <div>
              <h2 className={cn("m-0 text-2xl font-semibold", schoolColorClass)}>
                {spell.localizedName || spell.name}
              </h2>
              {spell.schoolTierText && (
                <div className={cn("mt-2 text-base", schoolColorClass)}>
                  {spell.schoolTierText}
                </div>
              )}
              <div className="mt-1 text-muted-foreground text-sm">
                {spell.category}
              </div>
              {spell.relatedSkill && (
                <RelatedSkillLink skill={spell.relatedSkill} />
              )}
            </div>
          </div>
        </div>

        {spell.levels && spell.levels.length > 0 && (
          spell.levels.map((level) => (
            <div
              key={level.level}
              className={cn(CARD_WIDTH, "p-4 bg-card rounded-lg border border-border flex flex-col")}
            >
                {!spell.isBonusSpell && (
                  <div className="font-semibold text-semantic-gold text-base mb-2">
                    {label('detail_level', level.level)}
                  </div>
                )}

                {level.bonusDescription && (
                  <RichText
                    text={level.bonusDescription}
                    className="mb-2 font-semibold text-semantic-orange text-sm leading-relaxed block"
                  />
                )}

                {spell.exceptionText && (
                  <RichText
                    text={spell.exceptionText}
                    className="mb-2 text-semantic-red text-sm block"
                  />
                )}

                {level.description && (
                  <RichText
                    text={level.description}
                    className="text-muted-foreground text-sm leading-relaxed whitespace-pre-wrap block"
                  />
                )}

                <div className="mt-auto pt-3 flex items-center gap-4 text-sm">
                  {level.starDustCost != null && (
                    <span className="flex items-center gap-1 text-semantic-gold font-semibold">
                      <ProgressiveIcon iconPath="icons/resources/stardust" alt="astrology" size={32} />
                      <strong>{level.starDustCost}</strong>
                    </span>
                  )}
                  <span className="flex items-center gap-1 text-semantic-blue font-semibold">
                    <ProgressiveIcon iconPath="Icon_Stats_Mana" alt="mana" size={32} />
                    <strong>{label('spell_mana', level.manaCost)}</strong>
                  </span>
                </div>
            </div>
          ))
        )}

        <UsedBySection
          entityType="spell"
          entityId={spell.id}
          className={FULL_WIDTH_CARD}
        />
    </DetailContainer>
  );
}

function RelatedSkillLink({ skill }: { skill: SkillReferenceDto }) {
  const navigate = useNavigate();
  return (
    <button
      onClick={() => navigate(`/skills/${skill.id}`)}
      className="mt-2 flex items-center gap-2 px-2 py-1 -ml-2 rounded-md cursor-pointer hover:bg-accent transition-colors text-left"
    >
      {skill.icon && (
        <ProgressiveIcon iconPath={skill.icon} alt={skill.name} size={24} className="shrink-0" />
      )}
      <span className="text-foreground text-sm font-medium">{skill.name}</span>
    </button>
  );
}

export default function SpellsPage() {
  const navigate = useNavigate();
  const { spellId: urlSpellId } = useParams<{ spellId?: string }>();

  useHighlightText();

  const {
    selectedSpellId,
    setSelectedSpellId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useSpellsStore();

  const { label } = useLabels();
  const columnLabels = useColumnLabels();

  const prevUrlSpellIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlSpellId && urlSpellId !== selectedSpellId && urlSpellId !== prevUrlSpellIdRef.current) {
      setSelectedSpellId(urlSpellId);
    }
    prevUrlSpellIdRef.current = urlSpellId;
  }, [urlSpellId, selectedSpellId, setSelectedSpellId]);

  const spellsQuery = useSpells(searchQuery || undefined);
  const spellQuery = useSpell(selectedSpellId);

  const sortedSpells = useMemo(() => {
    if (!spellsQuery.data) return [];
    return [...spellsQuery.data].sort((a, b) =>
      compareSpells(a, b, sortField, sortDirection)
    );
  }, [spellsQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      // Toggle direction if same field
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      // New field, start with ascending
      setSort(field as SpellSortField, 'asc');
    }
  };


  const handleSelectSpell = (id: string) => {
    setSelectedSpellId(id);
    navigate(`/spells/${id}`);
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
          <div className="hidden lg:block">
            <SortableColumnHeader
              label={label('label_spell_tier')}
              field="rank"
              currentField={sortField}
              direction={sortDirection}
              onSort={handleSort}
            />
          </div>
          <SortableColumnHeader
            label={columnLabels.school}
            field="school"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />
          <div className="hidden lg:block">
            <SortableColumnHeader
              label={columnLabels.category}
              field="category"
              currentField={sortField}
              direction={sortDirection}
              onSort={handleSort}
            />
          </div>

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={handleSearchChange}
              placeholder={label('search_placeholder_entity', label('nav_spells'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {spellsQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_spells'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(spellsQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <SpellList
              spells={sortedSpells}
              selectedSpellId={selectedSpellId}
              onSelectSpell={handleSelectSpell}
              isLoading={spellsQuery.isLoading}
            />
          )}
        </div>

        {spellsQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground">
            <span>{label('total_count', spellsQuery.data.length, label('nav_spells'))}</span>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <SpellDetailPanel
            spell={spellQuery.data || null}
            selectedSpellId={selectedSpellId}
            error={spellQuery.error as Error | null}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
