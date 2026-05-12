import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type FactionLawSortField = 'name' | 'faction' | 'id';

interface FactionLawsState {
  selectedFactionLawId: string | null;
  setSelectedFactionLawId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: FactionLawSortField;
  sortDirection: SortDirection;
  setSort: (field: FactionLawSortField, direction: SortDirection) => void;
}

export const useFactionLawsStore = create<FactionLawsState>()(
  persist(
    (set) => ({
      selectedFactionLawId: null,
      setSelectedFactionLawId: (id) => set({ selectedFactionLawId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'faction',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),
    }),
    {
      name: 'oe-factionlaws-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
