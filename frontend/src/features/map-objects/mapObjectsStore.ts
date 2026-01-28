import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type MapObjectSortField = 'name' | 'category' | 'id';

interface MapObjectsState {
  selectedMapObjectId: string | null;
  setSelectedMapObjectId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: MapObjectSortField;
  sortDirection: SortDirection;
  setSort: (field: MapObjectSortField, direction: SortDirection) => void;

  selectedDifficultyIndex: number | null;
  setSelectedDifficultyIndex: (index: number) => void;
}

export const useMapObjectsStore = create<MapObjectsState>()(
  persist(
    (set) => ({
      selectedMapObjectId: null,
      setSelectedMapObjectId: (id) => set({ selectedMapObjectId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'name',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),

      selectedDifficultyIndex: null,
      setSelectedDifficultyIndex: (index) => set({ selectedDifficultyIndex: index }),
    }),
    {
      name: 'oe-mapobjects-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
