import { useEffect, useMemo, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useHeroesStore, type HeroSortField } from './heroesStore';
import { useHeroes, useHero } from './useHeroes';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
import { useHighlightText } from '@/hooks/useHighlightText';
import type { HeroListItemDto, HeroDetailDto } from '@/api/types';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import FactionBadge from '@/components/display/FactionBadge';
import ClassBadge from '@/components/display/ClassBadge';
import RichText from '@/components/display/RichText';
import SortableColumnHeader, { type SortDirection } from '@/components/display/SortableColumnHeader';
import UnitHexCard from '@/components/display/UnitHexCard';
import { cn } from '@/lib/utils';

// 1760px is the breakpoint above which the middle row can hold three cards (narrow) vs two (wide).
const WIDE_CARD = 'w-[40rem] max-w-full';
const MIDDLE_NARROW_CARD = 'w-[40rem] min-[1760px]:w-[26rem] max-w-full';
const WIDE_GRID = 'grid grid-cols-1 min-[1760px]:grid-cols-[repeat(auto-fill,minmax(0,40rem))] gap-5 justify-center justify-items-center content-start';
const NARROW_GRID = 'grid grid-cols-1 min-[1760px]:grid-cols-[repeat(auto-fill,minmax(0,26rem))] gap-5 justify-center justify-items-center content-start';

function isNonStandardHero(id: string): boolean {
  return id.startsWith('campaign_') || id.startsWith('tutorial_') || id.startsWith('cm_');
}

function compareHeroes(
  a: HeroListItemDto,
  b: HeroListItemDto,
  primaryField: HeroSortField,
  primaryDirection: SortDirection
): number {
  const aNonStandard = isNonStandardHero(a.id) ? 1 : 0;
  const bNonStandard = isNonStandardHero(b.id) ? 1 : 0;
  if (aNonStandard !== bNonStandard) return aNonStandard - bNonStandard;

  const dir = primaryDirection === 'asc' ? 1 : -1;

  const compareFaction = () =>
    (a.factionDisplay || a.faction || '').localeCompare(b.factionDisplay || b.faction || '');
  const compareId = () => a.id.localeCompare(b.id, undefined, { numeric: true });
  const compareName = () => (a.name || '').localeCompare(b.name || '');
  const compareClass = () =>
    (a.classDisplay || a.classType || '').localeCompare(b.classDisplay || b.classType || '');

  switch (primaryField) {
    case 'faction':
      return compareFaction() * dir || compareId();

    case 'name':
      return compareName() * dir;

    case 'class':
      return compareClass() * dir;

    case 'id':
      return compareId() * dir;

    default:
      return 0;
  }
}

function HeroList({
  heroes,
  selectedHeroId,
  onSelectHero,
  isLoading,
}: {
  heroes: HeroListItemDto[];
  selectedHeroId: string | null;
  onSelectHero: (id: string) => void;
  isLoading?: boolean;
}) {
  const { label } = useLabels();

  useEffect(() => {
    if (selectedHeroId && heroes.length > 0) {
      document.getElementById(`hero-${selectedHeroId}`)?.scrollIntoView({
        behavior: 'instant',
        block: 'nearest',
      });
    }
  }, [selectedHeroId, heroes.length]);

  if (isLoading) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('common_loading', ' ' + label('nav_heroes'))}
      </div>
    );
  }

  if (heroes.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('nav_heroes'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {heroes.map((hero) => (
        <button
          key={hero.id}
          id={`hero-${hero.id}`}
          onClick={() => onSelectHero(hero.id)}
          className={cn(
            "flex items-center gap-3 px-3 py-2.5 rounded-md border-none cursor-pointer text-left transition-colors",
            selectedHeroId === hero.id
              ? "bg-primary text-primary-foreground"
              : "bg-transparent text-foreground hover:bg-accent"
          )}
        >
          <ProgressiveIcon iconPath={hero.iconPath} alt={hero.name} size={40} />
          <div className="flex-1 min-w-0">
            <div className={cn(
              "font-medium overflow-hidden text-ellipsis whitespace-nowrap",
              selectedHeroId !== hero.id && (
                hero.id.startsWith('campaign_') ||
                hero.id.startsWith('tutorial_') ||
                hero.id.startsWith('cm_')
              ) && "text-semantic-gold"
            )}>
              {hero.name}
            </div>
            <div className={cn(
              "text-xs mt-0.5",
              selectedHeroId === hero.id
                ? "text-primary-foreground/70"
                : "text-muted-foreground"
            )}>
              {hero.factionDisplay || hero.faction || label('label_unknown')} · {hero.classDisplay || hero.classType || '?'} · {hero.id}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

const STAT_ICONS: Record<string, string> = {
  Attack: 'icons/hero_stats/offence',
  Defence: 'icons/hero_stats/defence',
  'Spell Power': 'icons/hero_stats/spellpower',
  Knowledge: 'icons/hero_stats/intelligence',
};

const cardClassName = "bg-card border border-border rounded-2xl px-6 py-5";

function HeroDetailPanel({
  hero,
  selectedHeroId,
  error,
}: {
  hero: HeroDetailDto | null;
  selectedHeroId: string | null;
  error?: Error | null;
}) {
  const navigate = useNavigate();
  const { label } = useLabels();

  if (error) {
    return (
      <div className="p-10 text-center text-destructive">
        {label('detail_error', label('entity_hero'), label(error.message))}
      </div>
    );
  }

  if (!hero) {
    if (!selectedHeroId) {
      return (
        <div className="p-10 text-center text-muted-foreground flex flex-col items-center justify-center h-full">
          <div className="text-5xl mb-4">[ ]</div>
          <div>{label('detail_select')}</div>
        </div>
      );
    }
    return null;
  }

  const hasSpells = hero.startingSpells && hero.startingSpells.length > 0;
  const middleGrid = hasSpells ? NARROW_GRID : WIDE_GRID;
  const middleCard = hasSpells ? MIDDLE_NARROW_CARD : WIDE_CARD;

  return (
    <div className="h-full overflow-auto">
      <div className="px-5 py-5 flex flex-col gap-5">
        <div className={WIDE_GRID}>
        <div className={cn(WIDE_CARD, cardClassName)}>
          <div className="flex flex-col gap-4 h-full">
            <h1 className="m-0 p-0 text-center text-2xl font-semibold uppercase tracking-wide text-semantic-gold">
              {hero.name}
            </h1>

            {hero.iconPath && (
              <div className="flex justify-center mt-4">
                <ProgressiveIcon
                  iconPath={hero.iconPath}
                  alt={hero.name}
                  size={256}
                  className="rounded-lg"
                />
              </div>
            )}

            <div className="flex flex-wrap justify-center gap-6 mt-3 mb-2 mx-auto">
              <FactionBadge
                factionIcon={hero.factionIcon}
                factionDisplay={hero.factionDisplay}
                faction={hero.faction}
                iconSize={48}
                textClassName="text-muted-foreground"
              />

              <ClassBadge
                classIcon={hero.classIcon}
                classDisplay={hero.classDisplay}
                classType={hero.classType}
                iconSize={48}
                textClassName="text-muted-foreground"
              />
            </div>
          </div>
        </div>

        <div className={cn(WIDE_CARD, cardClassName)}>
          <div className="flex flex-col gap-4 h-full justify-center">
            <div className="grid grid-cols-[1fr_auto_1fr] grid-rows-[auto_auto] max-w-full md:max-w-100 mx-auto gap-0">
              {hero.specializationIcon && (
                <div className="row-span-2 col-start-2 flex items-center justify-center mx-2 md:mx-4">
                  <ProgressiveIcon
                    iconPath={hero.specializationIcon}
                    alt={hero.specializationName || 'Specialization'}
                    size={128}
                    className="max-[670px]:!w-24 max-[670px]:!h-24 max-w-full h-auto"
                  />
                </div>
              )}

              <div className="row-start-1 col-start-1 flex items-center justify-end gap-2">
                <ProgressiveIcon
                  iconPath={STAT_ICONS.Attack}
                  alt="Attack"
                  size={32}
                />
                <span className="text-lg font-bold text-foreground">
                  {hero.attack ?? ''}
                </span>
              </div>

              <div className="row-start-2 col-start-1 flex items-center justify-end gap-2 mt-2">
                <ProgressiveIcon
                  iconPath={STAT_ICONS.Defence}
                  alt="Defence"
                  size={32}
                />
                <span className="text-lg font-bold text-foreground">
                  {hero.defence ?? ''}
                </span>
              </div>

              <div className="row-start-1 col-start-3 flex items-center justify-start gap-2">
                <span className="text-lg font-bold text-foreground">
                  {hero.spellPower ?? ''}
                </span>
                <ProgressiveIcon
                  iconPath={STAT_ICONS['Spell Power']}
                  alt="Spell Power"
                  size={32}
                />
              </div>

              <div className="row-start-2 col-start-3 flex items-center justify-start gap-2 mt-2">
                <span className="text-lg font-bold text-foreground">
                  {hero.knowledge ?? ''}
                </span>
                <ProgressiveIcon
                  iconPath={STAT_ICONS.Knowledge}
                  alt="Knowledge"
                  size={32}
                />
              </div>
            </div>
            {(hero.specializationName || hero.specializationDescription) && (
              <div className="mt-3 text-center">
                {hero.specializationName && (
                  <h3 className="m-0 mb-2 text-[1.1rem] font-semibold text-semantic-gold">
                    {hero.specializationName}
                  </h3>
                )}
                {hero.specializationDescription && (
                  <RichText
                    text={hero.specializationDescription}
                    className="m-0 text-sm text-muted-foreground leading-relaxed block"
                  />
                )}
              </div>
            )}
          </div>
        </div>

        </div>

        <div className={middleGrid}>
        {hero.startingArmy && hero.startingArmy.length > 0 && (
          <div className={cn(middleCard, cardClassName)}>
            <h3 className="m-0 mb-3 text-center text-[1.1rem] font-semibold text-foreground">
              {hero.statLabels?.startingArmy || 'Starting Army'}
            </h3>
            <div className="flex justify-center flex-wrap gap-1">
              {hero.startingArmy.map((unit, index) => (
                <UnitHexCard
                  key={index}
                  unitId={unit.unitId}
                  unitName={unit.unitName}
                  icon={unit.icon}
                  amount={unit.countInterval}
                  size="lg"
                />
              ))}
            </div>
          </div>
        )}

          {hero.startingSkills && hero.startingSkills.length > 0 && (
            <div className={cn(middleCard, "bg-card border border-border rounded-2xl p-5")}>
              <h3 className="m-0 mb-4 text-center text-[1.1rem] font-semibold text-foreground">
                {hero.statLabels?.startingSkills || 'Starting Skills'}
              </h3>
              <div className="flex justify-center flex-wrap gap-2">
                {hero.startingSkills.map((skill, index) => (
                  <div
                    key={index}
                    className="m-2 flex flex-col items-center max-w-26"
                  >
                    <ProgressiveIcon
                      iconPath={skill.icon}
                      alt={skill.skillName}
                      size={80}
                      className="mb-2"
                    />
                    <button
                      onClick={() => navigate(`/skills/${skill.skillId}`)}
                      className="text-sm text-semantic-gold hover:text-semantic-gold/80 text-center bg-transparent border-none p-0 cursor-pointer font-semibold"
                    >
                      {skill.skillName}
                    </button>
                  </div>
                ))}
              </div>
            </div>
          )}

          {hero.startingSpells && hero.startingSpells.length > 0 && (
            <div className={cn(middleCard, "bg-card border border-border rounded-2xl p-5")}>
              <h3 className="m-0 mb-4 text-center text-[1.1rem] font-semibold text-foreground">
                {hero.statLabels?.startingSpells || 'Starting Spells'}
              </h3>
              <div className="flex justify-center flex-wrap gap-2">
                {hero.startingSpells.map((spell, index) => (
                  <div
                    key={index}
                    className="m-2 flex flex-col items-center max-w-26"
                  >
                    <ProgressiveIcon
                      iconPath={spell.icon}
                      alt={spell.spellName}
                      size={100}
                      className="mb-2"
                    />
                    <button
                      onClick={() => navigate(`/spells/${spell.spellId}`)}
                      className="text-sm text-semantic-gold hover:text-semantic-gold/80 text-center bg-transparent border-none p-0 cursor-pointer font-semibold"
                    >
                      {spell.spellName}
                    </button>
                  </div>
                ))}
              </div>
            </div>
          )}

        </div>

        <div className={WIDE_GRID}>
        {hero.description && (
          <div className={cn(WIDE_CARD, "bg-card border border-border rounded-2xl p-5")}>
            <h3 className="m-0 mb-2 text-center text-[1.1rem] font-semibold text-foreground">
              {hero.statLabels?.biography || 'Biography'}
            </h3>
            <RichText
              text={hero.description}
              className="m-0 text-[0.95rem] text-muted-foreground text-center leading-relaxed block"
            />
          </div>
        )}

        {hero.motto && (
          <div className={cn(WIDE_CARD, "bg-card border border-border rounded-2xl p-5")}>
            <h3 className="m-0 mb-2 text-center text-[1.1rem] font-semibold text-foreground">
              {hero.statLabels?.motto || 'Motto'}
            </h3>
            <RichText
              text={hero.motto}
              className="m-0 text-base italic text-muted-foreground text-center leading-normal block"
            />
          </div>
        )}
        </div>
      </div>
    </div>
  );
}

export default function HeroesPage() {
  const navigate = useNavigate();
  const { heroId: urlHeroId } = useParams<{ heroId?: string }>();

  useHighlightText();

  const {
    selectedHeroId,
    setSelectedHeroId,
    searchQuery,
    setSearchQuery,
    sortField,
    sortDirection,
    setSort,
    showCampaignHeroes,
    setShowCampaignHeroes,
  } = useHeroesStore();

  const columnLabels = useColumnLabels();
  const { label } = useLabels();

  const prevUrlHeroIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (urlHeroId && urlHeroId !== selectedHeroId && urlHeroId !== prevUrlHeroIdRef.current) {
      setSelectedHeroId(urlHeroId);
    }
    prevUrlHeroIdRef.current = urlHeroId;
  }, [urlHeroId, selectedHeroId, setSelectedHeroId]);

  const heroesQuery = useHeroes(searchQuery || undefined, showCampaignHeroes);
  const heroQuery = useHero(selectedHeroId);

  const filteredAndSortedHeroes = useMemo(() => {
    if (!heroesQuery.data) return [];
    return [...heroesQuery.data].sort((a, b) =>
      compareHeroes(a, b, sortField, sortDirection)
    );
  }, [heroesQuery.data, sortField, sortDirection]);

  const handleSort = (field: string) => {
    if (field === sortField) {
      setSort(sortField, sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSort(field as HeroSortField, 'asc');
    }
  };

  const handleSelectHero = (id: string) => {
    setSelectedHeroId(id);
    navigate(`/heroes/${id}`);
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
            label={columnLabels.class}
            field="class"
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
              placeholder={label('search_placeholder_entity', label('nav_heroes'))}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {heroesQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('nav_heroes'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(heroesQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : (
            <HeroList
              heroes={filteredAndSortedHeroes}
              selectedHeroId={selectedHeroId}
              onSelectHero={handleSelectHero}
              isLoading={heroesQuery.isLoading}
            />
          )}
        </div>

        {heroesQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground flex items-center justify-between gap-2">
            <span>
              {filteredAndSortedHeroes.length === heroesQuery.data.length
                ? label('total_count', heroesQuery.data.length, label('nav_heroes'))
                : label('filtered_count', filteredAndSortedHeroes.length, heroesQuery.data.length, label('nav_heroes'))}
            </span>
            <div className="flex items-center gap-2 shrink-0">
              <span>{label('heroes_show_campaign')}</span>
              <button
                type="button"
                role="switch"
                aria-checked={showCampaignHeroes}
                onClick={() => setShowCampaignHeroes(!showCampaignHeroes)}
                className={cn(
                  "relative w-9 h-5 rounded-full transition-colors flex items-center px-0.5 cursor-pointer",
                  showCampaignHeroes ? "bg-primary" : "bg-muted-foreground/40"
                )}
              >
                <span
                  className={cn(
                    "w-3.5 h-3.5 bg-white rounded-full shadow transition-transform",
                    showCampaignHeroes && "translate-x-[18px]"
                  )}
                />
              </button>
            </div>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background ml-80 lg:ml-105">
        <ErrorBoundary>
          <HeroDetailPanel
            hero={heroQuery.data || null}
            selectedHeroId={selectedHeroId}
            error={heroQuery.error as Error | null}
          />
        </ErrorBoundary>
      </main>
    </div>
  );
}
