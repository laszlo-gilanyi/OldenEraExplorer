import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useSkillsStore, type SkillSortField } from './skillsStore';
import { useSkills, useSkill } from './useSkills';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import SortableColumnHeader from '@/components/display/SortableColumnHeader';
import UsedBySection from '@/components/display/UsedBySection';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import RichText from '@/components/display/RichText';
import DetailContainer, { FULL_WIDTH_CARD } from '@/components/display/DetailContainer';
import { cn } from '@/lib/utils';
import type { SkillListItemDto, SkillDetailDto, SkillLevelDto, SubSkillDto, SpellLinkDto, BattleAbilityLinkDto } from '@/api/types';

// Skill type background classes for visual distinction (theme-aware)
const skillTypeBgClasses: Record<string, string> = {
  main: 'bg-blue-600',
  combat: 'bg-red-600',
  magic: 'bg-purple-600',
  support: 'bg-green-600',
  economy: 'bg-amber-600',
};

function getSkillTypeBgClass(skillType: string | null): string {
  if (!skillType) return 'bg-muted';
  const lowerType = skillType.toLowerCase();
  return skillTypeBgClasses[lowerType] || 'bg-muted';
}

function SkillList({
  skills,
  selectedSkillId,
  onSelectSkill,
  isLoading,
}: {
  skills: SkillListItemDto[];
  selectedSkillId: string | null;
  onSelectSkill: (id: string) => void;
  isLoading?: boolean;
}) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedSkillId && skills.length > 0) {
      document.getElementById(`skill-${selectedSkillId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedSkillId, skills.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_skills'))}
      </div>
    );
  }

  if (skills.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_skills'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {skills.map((skill) => (
        <button
          key={skill.id}
          id={`skill-${skill.id}`}
          onClick={() => onSelectSkill(skill.id)}
          className={cn(
            "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
            selectedSkillId === skill.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <ProgressiveIcon iconPath={skill.icon} alt={skill.name} size={32} />
          <div className="flex-1 min-w-0">
            <div className={cn(
              "font-medium overflow-hidden text-ellipsis whitespace-nowrap",
              selectedSkillId !== skill.id && skill.id.startsWith('arena_') && "text-semantic-gold"
            )}>
              {skill.name}
            </div>
            <div className={cn(
              "text-xs mt-0.5",
              selectedSkillId === skill.id ? "text-primary-foreground/70" : "text-muted-foreground"
            )}>
              {skill.id}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

function SubSkillCard({ subSkill }: { subSkill: SubSkillDto }) {
  const navigate = useNavigate();
  return (
    <div className="p-4 bg-card rounded-lg border border-border flex flex-col md:flex-row gap-3">
      {subSkill.icon ? (
        <ProgressiveIcon
          iconPath={subSkill.icon}
          alt={subSkill.name}
          size={48}
          className="shrink-0 self-start"
        />
      ) : (
        <div className="w-12 h-12 rounded-md bg-muted shrink-0 self-start" />
      )}
      <div className="flex-1 min-w-0">
        <div className="font-semibold text-foreground mb-1.5 text-[0.95rem]">
          {subSkill.name}
        </div>
        {subSkill.description && (
          <RichText
            text={subSkill.description}
            className="text-muted-foreground text-[0.85rem] leading-relaxed whitespace-pre-wrap block"
          />
        )}
        {subSkill.grantedSpell && (
          <GrantedSpellLink spell={subSkill.grantedSpell} onNavigate={(id) => navigate(`/spells/${id}`)} />
        )}
        {subSkill.grantedBattleAbility && (
          <GrantedBattleAbilityDisplay ability={subSkill.grantedBattleAbility} />
        )}
      </div>
    </div>
  );
}

function GrantedSpellLink({ spell, onNavigate }: { spell: SpellLinkDto; onNavigate: (id: string) => void }) {
  return (
    <button
      onClick={() => onNavigate(spell.id)}
      className="mt-2 flex items-center gap-2 px-2 py-1 -ml-2 rounded-md cursor-pointer hover:bg-accent transition-colors text-left"
    >
      {spell.icon && (
        <ProgressiveIcon iconPath={spell.icon} alt={spell.name} size={24} className="shrink-0" />
      )}
      <span className="text-foreground text-sm font-medium">{spell.name}</span>
    </button>
  );
}

function GrantedBattleAbilityDisplay({ ability }: { ability: BattleAbilityLinkDto }) {
  return (
    <div className="mt-2 px-2 py-1 -ml-2">
      <div className="flex items-center gap-2">
        {ability.icon && (
          <ProgressiveIcon iconPath={ability.icon} alt={ability.name} size={24} className="shrink-0" />
        )}
        <span className="text-foreground text-sm font-medium">{ability.name}</span>
      </div>
      {ability.description && (
        <RichText
          text={ability.description}
          className="mt-1 text-muted-foreground text-[0.85rem] leading-relaxed whitespace-pre-wrap block"
        />
      )}
    </div>
  );
}

function SkillLevelSection({
  level,
  levelNumber,
  skillTypeBgClass,
  className,
}: {
  level: SkillLevelDto;
  levelNumber: number;
  skillTypeBgClass: string;
  className?: string;
}) {
  const levelNames: Record<number, string> = { 1: 'Basic', 2: 'Advanced', 3: 'Expert' };
  const defaultLevelName = levelNames[levelNumber] || `Level ${levelNumber}`;

  return (
    <div className={cn("py-5 px-6 bg-card rounded-xl border border-border", className)}>
      <div className={cn(
        "flex flex-col md:flex-row gap-4",
        (level.description || level.subSkillChoices.length > 0) && "mb-4"
      )}>
        {level.icon ? (
          <ProgressiveIcon
            iconPath={level.icon}
            alt={level.levelName || defaultLevelName}
            size={80}
            className="shrink-0 self-start"
          />
        ) : (
          <div
            className={cn(
              "w-20 h-20 rounded-lg shrink-0 self-start flex items-center justify-center text-2xl font-bold text-white opacity-60",
              skillTypeBgClass
            )}
          >
            {levelNumber}
          </div>
        )}

        <div className="flex-1 min-w-0">
          <div className="font-semibold text-foreground text-lg mb-2">
            {level.levelName || defaultLevelName}
          </div>

          {level.description && (
            <RichText
              text={level.description}
              className="text-muted-foreground text-sm leading-relaxed whitespace-pre-wrap block"
            />
          )}
        </div>
      </div>

      {level.subSkillChoices.length > 0 && (
        <div className="mt-4 flex flex-col gap-2.5">
          {level.subSkillChoices.map((subSkill) => (
            <SubSkillCard key={subSkill.id} subSkill={subSkill} />
          ))}
        </div>
      )}
    </div>
  );
}

function SkillDetailPanel({
  skill,
  selectedSkillId,
  error,
}: {
  skill: SkillDetailDto | null;
  selectedSkillId: string | null;
  error?: Error | null;
}) {
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_skill'), label(error.message))}
      </div>
    );
  }

  if (!skill) {
    // Only show "select" message if no skill is selected
    if (!selectedSkillId) {
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

  const skillTypeBgClass = getSkillTypeBgClass(skill.skillType);

  return (
    <DetailContainer>
        {skill.level1 && (
          <SkillLevelSection
            level={skill.level1}
            levelNumber={1}
            skillTypeBgClass={skillTypeBgClass}
            className={FULL_WIDTH_CARD}
          />
        )}
        {skill.level2 && (
          <SkillLevelSection level={skill.level2} levelNumber={2} skillTypeBgClass={skillTypeBgClass} />
        )}
        {skill.level3 && (
          <SkillLevelSection level={skill.level3} levelNumber={3} skillTypeBgClass={skillTypeBgClass} />
        )}

        <UsedBySection
          entityType="skill"
          entityId={skill.id}
          title={skill.statLabels?.heroesStartingWithSkill}
          className={FULL_WIDTH_CARD}
        />
    </DetailContainer>
  );
}

export default function SkillsPage() {
  const navigate = useNavigate();
  const { skillId: urlSkillId } = useParams<{ skillId?: string }>();

  useHighlightText();

  const {
    selectedSkillId,
    setSelectedSkillId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
    showArenaSkills,
    setShowArenaSkills,
  } = useSkillsStore();

  const { label } = useLabels();
  const columnLabels = useColumnLabels();

  const prevUrlSkillIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlSkillId && urlSkillId !== selectedSkillId && urlSkillId !== prevUrlSkillIdRef.current) {
      setSelectedSkillId(urlSkillId);
    }
    prevUrlSkillIdRef.current = urlSkillId;
  }, [urlSkillId, selectedSkillId, setSelectedSkillId]);

  const skillsQuery = useSkills(searchQuery || undefined, showArenaSkills);
  const skillQuery = useSkill(selectedSkillId);

  const sortedSkills = useMemo(() => {
    if (!skillsQuery.data) return [];
    return [...skillsQuery.data].sort((a, b) => {
      const aArena = a.id.startsWith('arena_') ? 1 : 0;
      const bArena = b.id.startsWith('arena_') ? 1 : 0;
      if (aArena !== bArena) return aArena - bArena;

      const cmp = sortField === 'name'
        ? (a.name || '').localeCompare(b.name || '')
        : a.id.localeCompare(b.id, undefined, { numeric: true });
      return sortDirection === 'asc' ? cmp : -cmp;
    });
  }, [skillsQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      // Toggle direction if same field
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      // New field, start with ascending
      setSort(field as SkillSortField, 'asc');
    }
  };

  const handleSelectSkill = (id: string) => {
    setSelectedSkillId(id);
    navigate(`/skills/${id}`);
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
              placeholder={label('search_placeholder_entity', label('nav_skills'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {skillsQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_skills'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(skillsQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <SkillList
              skills={sortedSkills}
              selectedSkillId={selectedSkillId}
              onSelectSkill={handleSelectSkill}
              isLoading={skillsQuery.isLoading}
            />
          )}
        </div>

        {skillsQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground flex items-center justify-between gap-2">
            <span>{label('total_count', skillsQuery.data.length, label('nav_skills'))}</span>
            <div className="flex items-center gap-2 shrink-0">
              <span>{label('skills_show_arena')}</span>
              <button
                type="button"
                role="switch"
                aria-checked={showArenaSkills}
                onClick={() => setShowArenaSkills(!showArenaSkills)}
                className={cn(
                  "relative w-9 h-5 rounded-full transition-colors flex items-center px-0.5 cursor-pointer",
                  showArenaSkills ? "bg-primary" : "bg-muted-foreground/40"
                )}
              >
                <span
                  className={cn(
                    "w-3.5 h-3.5 bg-white rounded-full shadow transition-transform",
                    showArenaSkills && "translate-x-[18px]"
                  )}
                />
              </button>
            </div>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <SkillDetailPanel
            skill={skillQuery.data || null}
            selectedSkillId={selectedSkillId}
            error={skillQuery.error as Error | null}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
