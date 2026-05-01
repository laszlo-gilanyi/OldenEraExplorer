import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type HeroSortField = 'name' | 'faction' | 'class' | 'id';

interface HeroesState {
  selectedHeroId: string | null;
  setSelectedHeroId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: HeroSortField;
  sortDirection: SortDirection;
  setSort: (field: HeroSortField, direction: SortDirection) => void;

  showCampaignHeroes: boolean;
  setShowCampaignHeroes: (show: boolean) => void;
}

export const useHeroesStore = create<HeroesState>()(
  persist(
    (set) => ({
      selectedHeroId: null,
      setSelectedHeroId: (id) => set({ selectedHeroId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'faction',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),

      showCampaignHeroes: false,
      setShowCampaignHeroes: (show) => set({ showCampaignHeroes: show }),
    }),
    {
      name: 'oe-heroes-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
