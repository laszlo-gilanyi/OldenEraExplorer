import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useSubclassesStore, type SubclassSortField } from './subclassesStore';
import { useSubclasses, useSubclass } from './useSubclasses';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import type { SubclassListItemDto, SubclassDetailDto, RequiredSkillDto } from '@/api/types';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import FactionBadge from '@/components/display/FactionBadge';
import ClassBadge from '@/components/display/ClassBadge';
import RichText from '@/components/display/RichText';
import SortableColumnHeader, { type SortDirection } from '@/components/display/SortableColumnHeader';
import EntityChip from '@/components/display/EntityChip';
import DetailContainer, { CARD_WIDTH, FULL_WIDTH_CARD } from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';

/**
 * Multi-level sorting configuration for subclasses.
 *
 * - Faction click: Faction (direction) -> Class (asc)
 * - Name/Class click: Single-level sort
 * - Default (initial): Faction (asc) -> Class (asc)
 */
function compareSubclasses(
  a: SubclassListItemDto,
  b: SubclassListItemDto,
  primaryField: SubclassSortField,
  primaryDirection: SortDirection
): number {
  const dir = primaryDirection === 'asc' ? 1 : -1;

  const compareFaction = () =>
    (a.factionDisplay || a.faction || '').localeCompare(b.factionDisplay || b.faction || '');
  const compareName = () => (a.name || '').localeCompare(b.name || '');
  const compareClass = () =>
    (a.classDisplay || a.classType || '').localeCompare(b.classDisplay || b.classType || '');

  switch (primaryField) {
    case 'faction':
      // Faction (direction) -> Class (inverted to put "might" first)
      return compareFaction() * dir || (compareClass() * -1);

    case 'name':
      // Name only (single-level)
      return compareName() * dir;

    case 'class':
      // Class only (inverted to put "might" first, UI shows opposite)
      return compareClass() * dir * -1;

    default:
      return 0;
  }
}

function SubclassList({
  subclasses,
  selectedSubclassId,
  onSelectSubclass,
  isLoading,
}: {
  subclasses: SubclassListItemDto[];
  selectedSubclassId: string | null;
  onSelectSubclass: (id: string) => void;
  isLoading?: boolean;
}) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedSubclassId && subclasses.length > 0) {
      document.getElementById(`subclass-${selectedSubclassId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedSubclassId, subclasses.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_subclasses'))}
      </div>
    );
  }

  if (subclasses.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_subclasses'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {subclasses.map((subclass) => (
        <button
          key={subclass.id}
          id={`subclass-${subclass.id}`}
          onClick={() => onSelectSubclass(subclass.id)}
          className={cn(
            "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
            selectedSubclassId === subclass.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <ProgressiveIcon iconPath={subclass.icon} alt={subclass.name} size={32} />
          <div className="flex-1 min-w-0">
            <div className="font-medium overflow-hidden text-ellipsis whitespace-nowrap">
              {subclass.name}
            </div>
            <div className={cn(
              "text-xs mt-0.5",
              selectedSubclassId === subclass.id
                ? "text-primary-foreground/70"
                : "text-muted-foreground"
            )}>
              {subclass.factionDisplay || subclass.faction || label('label_unknown')} · {subclass.classDisplay || subclass.classType || label('label_unknown')}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

function RequiredSkillCard({ skill, onNavigate }: { skill: RequiredSkillDto; onNavigate: (skillId: string) => void }) {
  return (
    <EntityChip
      iconPath={skill.icon}
      name={skill.skillName}
      onClick={() => onNavigate(skill.skillId)}
      className="w-[11.5rem]"
    />
  );
}

function SubclassDetailPanel({
  subclass,
  selectedSubclassId,
  error,
  onNavigateToSkill,
}: {
  subclass: SubclassDetailDto | null;
  selectedSubclassId: string | null;
  error?: Error | null;
  onNavigateToSkill: (skillId: string) => void;
}) {
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_subclass'), label(error.message))}
      </div>
    );
  }

  if (!subclass) {
    if (!selectedSubclassId) {
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
            <ProgressiveIcon iconPath={subclass.icon} alt={subclass.name} size={80} className="shrink-0" />
            <div className="flex-1">
              <h2 className="m-0 text-2xl font-semibold text-semantic-gold">
                {subclass.name}
              </h2>
              <div className="flex flex-wrap gap-6 mt-3">
                <FactionBadge
                  factionIcon={subclass.factionIcon}
                  factionDisplay={subclass.factionDisplay}
                  faction={subclass.faction}
                  iconSize={48}
                  textClassName="text-muted-foreground"
                />
                <ClassBadge
                  classIcon={subclass.classIcon}
                  classDisplay={subclass.classDisplay}
                  classType={subclass.classType}
                  iconSize={48}
                  textClassName="text-muted-foreground"
                />
              </div>

              {subclass.description && (
                <RichText
                  text={subclass.description}
                  className="mt-4 text-muted-foreground leading-relaxed whitespace-pre-wrap block"
                />
              )}
            </div>
          </div>
        </div>

        {subclass.requiredSkills.length > 0 && (
          <div className={cn(CARD_WIDTH, "bg-card border border-border rounded-2xl p-5")}>
            <h3 className="m-0 mb-3 text-base font-semibold text-foreground">
              {subclass.statLabels?.requiredSkills || 'Required Skills'}
            </h3>
            <div className="flex flex-wrap justify-center gap-3 max-w-[calc(11.5rem*3+1.5rem)] mx-auto">
              {subclass.requiredSkills.map((skill) => (
                <RequiredSkillCard
                  key={skill.skillId}
                  skill={skill}
                  onNavigate={onNavigateToSkill}
                />
              ))}
            </div>
          </div>
        )}
    </DetailContainer>
  );
}

export default function SubclassesPage() {
  const navigate = useNavigate();
  const { subclassId: urlSubclassId } = useParams<{ subclassId?: string }>();

  useHighlightText();

  const {
    selectedSubclassId,
    setSelectedSubclassId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
  } = useSubclassesStore();

  const columnLabels = useColumnLabels();
  const { label } = useLabels();

  const prevUrlSubclassIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlSubclassId && urlSubclassId !== selectedSubclassId && urlSubclassId !== prevUrlSubclassIdRef.current) {
      setSelectedSubclassId(urlSubclassId);
    }
    prevUrlSubclassIdRef.current = urlSubclassId;
  }, [urlSubclassId, selectedSubclassId, setSelectedSubclassId]);

  const subclassesQuery = useSubclasses(searchQuery || undefined);
  const subclassQuery = useSubclass(selectedSubclassId);

  const sortedSubclasses = useMemo(() => {
    if (!subclassesQuery.data) return [];

    return [...subclassesQuery.data].sort((a, b) =>
      compareSubclasses(a, b, sortField, sortDirection)
    );
  }, [subclassesQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSort(field as SubclassSortField, 'asc');
    }
  };

  const handleSelectSubclass = (id: string) => {
    setSelectedSubclassId(id);
    navigate(`/subclasses/${id}`);
  };

  const handleSearchChange = (query: string) => {
    setSearchQuery(query);
  };

  const handleClearSearch = () => {
    setSearchQuery('');
  };

  const handleNavigateToSkill = (skillId: string) => {
    navigate(`/skills/${skillId}`);
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
            label={columnLabels.class}
            field="class"
            currentField={sortField}
            direction={sortDirection}
            onSort={handleSort}
          />

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={handleSearchChange}
              placeholder={label('search_placeholder_entity', label('nav_subclasses'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {subclassesQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_subclasses'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(subclassesQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <SubclassList
              subclasses={sortedSubclasses}
              selectedSubclassId={selectedSubclassId}
              onSelectSubclass={handleSelectSubclass}
              isLoading={subclassesQuery.isLoading}
            />
          )}
        </div>

        {subclassesQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground flex flex-col gap-2">
            <div className="flex justify-between items-center">
              <span>{label('total_count', subclassesQuery.data.length, label('nav_subclasses'))}</span>
            </div>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <SubclassDetailPanel
            subclass={subclassQuery.data || null}
            selectedSubclassId={selectedSubclassId}
            error={subclassQuery.error as Error | null}
            onNavigateToSkill={handleNavigateToSkill}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
