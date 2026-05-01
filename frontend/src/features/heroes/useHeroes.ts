import { useQuery } from '@tanstack/react-query';
import { heroesApi } from '@/api/client';
import type { HeroListItemDto, HeroDetailDto } from '@/api/types';

export function useHeroes(search?: string, includeCampaignAndTutorial?: boolean) {
  return useQuery<HeroListItemDto[]>({
    queryKey: ['heroes', search, includeCampaignAndTutorial],
    queryFn: () => heroesApi.list(search, includeCampaignAndTutorial),
    retry: false,
  });
}

export function useHero(id: string | null) {
  return useQuery<HeroDetailDto>({
    queryKey: ['hero', id],
    queryFn: () => heroesApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
