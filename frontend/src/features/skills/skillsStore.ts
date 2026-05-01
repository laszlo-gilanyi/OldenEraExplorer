import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import type { SortDirection } from '@/components/display/SortableColumnHeader';

export type SkillSortField = 'name' | 'type' | 'id';

interface SkillsState {
  selectedSkillId: string | null;
  setSelectedSkillId: (id: string | null) => void;

  searchQuery: string;
  setSearchQuery: (query: string) => void;

  sortField: SkillSortField;
  sortDirection: SortDirection;
  setSort: (field: SkillSortField, direction: SortDirection) => void;

  showArenaSkills: boolean;
  setShowArenaSkills: (show: boolean) => void;
}

export const useSkillsStore = create<SkillsState>()(
  persist(
    (set) => ({
      selectedSkillId: null,
      setSelectedSkillId: (id) => set({ selectedSkillId: id }),

      searchQuery: '',
      setSearchQuery: (query) => set({ searchQuery: query }),

      sortField: 'name',
      sortDirection: 'asc',
      setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),

      showArenaSkills: false,
      setShowArenaSkills: (show) => set({ showArenaSkills: show }),
    }),
    {
      name: 'oe-skills-store',
      storage: createJSONStorage(() => sessionStorage),
    }
  )
);
