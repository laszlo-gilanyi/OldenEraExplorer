import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { settingsApi, gameApi } from '@/api/client';
import type { UpdateSettingsRequest } from '@/api/types';

export function useSettings() {
  const query = useQuery({
    queryKey: ['settings'],
    queryFn: () => settingsApi.getSettings(),
    staleTime: 60000,
  });

  useEffect(() => {
    if (query.data?.theme) {
      applyTheme(query.data.theme);
    }
  }, [query.data?.theme]);

  return query;
}

export function useUpdateSettings() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (settings: UpdateSettingsRequest) => settingsApi.updateSettings(settings),
    onSuccess: async (data, variables) => {
      console.log('[useSettings] onSuccess called:', { data, variables });

      queryClient.setQueryData(['settings'], data);

      if (variables.locale || variables.usePlaceholderResolver !== undefined) {
        const changeType = variables.locale ? 'Locale' : 'Resolver';
        console.log(`[useSettings] ${changeType} changed, waiting for backend reload...`);

        await new Promise(resolve => setTimeout(resolve, 500));

        queryClient.refetchQueries({
          predicate: (query) => query.state.status === 'error'
        });

        console.log(`[useSettings] Reloading game data after ${changeType} change...`);
        try {
          await gameApi.load();
          console.log('[useSettings] Game data reloaded successfully');

          await queryClient.refetchQueries({ queryKey: ['units'] });
          await queryClient.refetchQueries({ queryKey: ['unit'] });
          await queryClient.refetchQueries({ queryKey: ['heroes'] });
          await queryClient.refetchQueries({ queryKey: ['hero'] });
          await queryClient.refetchQueries({ queryKey: ['spells'] });
          await queryClient.refetchQueries({ queryKey: ['spell'] });
          await queryClient.refetchQueries({ queryKey: ['skills'] });
          await queryClient.refetchQueries({ queryKey: ['skill'] });
          await queryClient.refetchQueries({ queryKey: ['artifacts'] });
          await queryClient.refetchQueries({ queryKey: ['artifact'] });
          await queryClient.refetchQueries({ queryKey: ['buildings'] });
          await queryClient.refetchQueries({ queryKey: ['building'] });
          await queryClient.refetchQueries({ queryKey: ['buffs'] });
          await queryClient.refetchQueries({ queryKey: ['buff'] });
          await queryClient.refetchQueries({ queryKey: ['abilities'] });
          await queryClient.refetchQueries({ queryKey: ['ability'] });
          await queryClient.refetchQueries({ queryKey: ['factionLaws'] });
          await queryClient.refetchQueries({ queryKey: ['factionLaw'] });
          await queryClient.refetchQueries({ queryKey: ['subclasses'] });
          await queryClient.refetchQueries({ queryKey: ['subclass'] });
          await queryClient.refetchQueries({ queryKey: ['map-objects'] });
          await queryClient.refetchQueries({ queryKey: ['map-object'] });
          await queryClient.refetchQueries({ queryKey: ['modSessionEntities'] });
          await queryClient.refetchQueries({ queryKey: ['modSessionEntity'] });
          await queryClient.refetchQueries({ queryKey: ['labels'] });
          await queryClient.refetchQueries({ queryKey: ['game', 'status'] });
        } catch (error) {
          console.error(`[useSettings] Failed to reload game data after ${changeType} change:`, error);
        }
      }
    },
  });
}

export function useLocales() {
  return useQuery({
    queryKey: ['settings', 'locales'],
    queryFn: () => settingsApi.getLocales(),
    staleTime: 300000,
  });
}

function applyTheme(theme: string) {
  const root = document.documentElement;

  root.classList.remove('theme-light', 'theme-dark');

  if (theme === 'Light') {
    root.classList.add('theme-light');
    root.style.colorScheme = 'light';
  } else if (theme === 'Dark') {
    root.classList.add('theme-dark');
    root.style.colorScheme = 'dark';
  } else {
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    root.classList.add(prefersDark ? 'theme-dark' : 'theme-light');
    root.style.colorScheme = prefersDark ? 'dark' : 'light';
  }
}
