import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type BuildingSortField = 'name' | 'faction' | 'category' | 'level' | 'id';

interface BuildingsState {
  selectedBuildingId: string | null;
  setSelectedBuildingId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: BuildingSortField;
  sortDirection: SortDirection;
  setSort: (field: BuildingSortField, direction: SortDirection) => void;
}

export const useBuildingsStore = create<BuildingsState>()(
  persist(
    (set) => ({
      selectedBuildingId: null,
      setSelectedBuildingId: (id) => set({ selectedBuildingId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'faction',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),
    }),
    {
      name: 'oe-buildings-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
