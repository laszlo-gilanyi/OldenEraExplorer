import { useQuery } from '@tanstack/react-query';
import { skillsApi } from '@/api/client';
import type { SkillListItemDto, SkillDetailDto } from '@/api/types';

export function useSkills(search?: string, includeArena?: boolean) {
  return useQuery<SkillListItemDto[]>({
    queryKey: ['skills', search, includeArena],
    queryFn: () => skillsApi.list(search, includeArena),
    retry: false,
  });
}

export function useSkill(id: string | null) {
  return useQuery<SkillDetailDto>({
    queryKey: ['skill', id],
    queryFn: () => skillsApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
