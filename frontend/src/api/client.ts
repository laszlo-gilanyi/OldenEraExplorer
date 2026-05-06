import axios, { AxiosError } from 'axios';
import { useGameStore } from '../stores/gameStore';
import type {
  GameStatusDto,
  DetectionResultDto,
  SetPathResultDto,
  FilesystemRootsDto,
  DirectoryListingDto,
  UnitListItemDto,
  UnitDetailDto,
  AbilityListItemDto,
  AbilityDetailDto,
  BuildingListItemDto,
  BuildingDetailDto,
  SpellListItemDto,
  SpellDetailDto,
  SkillListItemDto,
  SkillDetailDto,
  MapObjectListItemDto,
  MapObjectDetailDto,
  FactionLawListItemDto,
  FactionLawDetailDto,
  HeroListItemDto,
  HeroDetailDto,
  SubclassListItemDto,
  SubclassDetailDto,
  ArtifactListItemDto,
  ArtifactDetailDto,
  ErrorDto,
  ExtractedModelDto,
  ModelStatusDto,
  SearchResponse,
  SettingsDto,
  UpdateSettingsRequest,
  ReleaseInfo,
  UpdateProgress,
  LocalesDto,
  LabelsDto,
} from './types';

const api = axios.create({
  baseURL: '/api',
  timeout: 30000,
});

const ALLOWED_WITHOUT_GAME = [
  '/game/',
  '/settings',
  '/labels',
  '/extraction/',
  '/assets/',
  '/filesystem/',
];

api.interceptors.request.use((config) => {
  const url = config.url || '';
  const isAllowed = ALLOWED_WITHOUT_GAME.some(path => url.startsWith(path));

  if (!isAllowed && !useGameStore.getState().isGameReady) {
    return Promise.reject(new Error('error_no_game_data'));
  }

  return config;
});

function handleApiError(error: unknown): never {
  if (axios.isAxiosError(error)) {
    const axiosError = error as AxiosError<ErrorDto>;
    const message = axiosError.response?.data?.error || axiosError.message;
    throw new Error(message);
  }
  throw error;
}

export const gameApi = {
  getStatus: async (): Promise<GameStatusDto> => {
    try {
      const response = await api.get<GameStatusDto>('/game/status');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  detect: async (): Promise<DetectionResultDto> => {
    try {
      const response = await api.get<DetectionResultDto>('/game/detect');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  setPath: async (path: string, locale?: string): Promise<SetPathResultDto> => {
    try {
      const response = await api.post<SetPathResultDto>('/game/path', { path, locale });
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  load: async (): Promise<GameStatusDto> => {
    try {
      const response = await api.post<GameStatusDto>('/game/load');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  clearPath: async (): Promise<void> => {
    try {
      await api.delete('/game/path');
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const unitsApi = {
  list: async (search?: string): Promise<UnitListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      const response = await api.get<UnitListItemDto[]>(`/units?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<UnitDetailDto> => {
    try {
      const response = await api.get<UnitDetailDto>(`/units/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getGlbUrl: (id: string): string => `/api/units/${id}/glb`,
};

export const abilitiesApi = {
  list: async (search?: string, type?: string): Promise<AbilityListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      if (type) {
        params.set('type', type);
      }
      const response = await api.get<AbilityListItemDto[]>(`/abilities?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<AbilityDetailDto> => {
    try {
      const response = await api.get<AbilityDetailDto>(`/abilities/${encodeURIComponent(id)}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const buildingsApi = {
  list: async (search?: string, faction?: string, category?: string): Promise<BuildingListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      if (faction) {
        params.set('faction', faction);
      }
      if (category) {
        params.set('category', category);
      }
      const response = await api.get<BuildingListItemDto[]>(`/buildings?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<BuildingDetailDto> => {
    try {
      const response = await api.get<BuildingDetailDto>(`/buildings/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const spellsApi = {
  list: async (search?: string): Promise<SpellListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      const response = await api.get<SpellListItemDto[]>(`/spells?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<SpellDetailDto> => {
    try {
      const response = await api.get<SpellDetailDto>(`/spells/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const skillsApi = {
  list: async (search?: string, includeArena?: boolean): Promise<SkillListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      if (includeArena) {
        params.set('includeArena', 'true');
      }
      const response = await api.get<SkillListItemDto[]>(`/skills?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<SkillDetailDto> => {
    try {
      const response = await api.get<SkillDetailDto>(`/skills/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const factionLawsApi = {
  list: async (search?: string): Promise<FactionLawListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      const response = await api.get<FactionLawListItemDto[]>(`/faction-laws?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<FactionLawDetailDto> => {
    try {
      const response = await api.get<FactionLawDetailDto>(`/faction-laws/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const heroesApi = {
  list: async (search?: string, includeCampaignAndTutorial?: boolean): Promise<HeroListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      if (includeCampaignAndTutorial) {
        params.set('includeCampaignAndTutorial', 'true');
      }
      const response = await api.get<HeroListItemDto[]>(`/heroes?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<HeroDetailDto> => {
    try {
      const response = await api.get<HeroDetailDto>(`/heroes/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const mapObjectsApi = {
  list: async (search?: string, category?: string): Promise<MapObjectListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      if (category) {
        params.set('category', category);
      }
      const response = await api.get<MapObjectListItemDto[]>(`/map-objects?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<MapObjectDetailDto> => {
    try {
      const response = await api.get<MapObjectDetailDto>(`/map-objects/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getCategories: async (): Promise<string[]> => {
    try {
      const response = await api.get<string[]>('/map-objects/categories');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const subclassesApi = {
  list: async (search?: string): Promise<SubclassListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      const response = await api.get<SubclassListItemDto[]>(`/subclasses?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<SubclassDetailDto> => {
    try {
      const response = await api.get<SubclassDetailDto>(`/subclasses/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const artifactsApi = {
  list: async (search?: string): Promise<ArtifactListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      const response = await api.get<ArtifactListItemDto[]>(`/artifacts?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getById: async (id: string): Promise<ArtifactDetailDto> => {
    try {
      const response = await api.get<ArtifactDetailDto>(`/artifacts/${id}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const modelsApi = {
  listUnits: async (search?: string): Promise<UnitListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      const response = await api.get<UnitListItemDto[]>(`/models/units?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  listMapObjects: async (search?: string, category?: string): Promise<MapObjectListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      if (category) {
        params.set('category', category);
      }
      const response = await api.get<MapObjectListItemDto[]>(`/models/map-objects?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  listArtifacts: async (search?: string): Promise<ArtifactListItemDto[]> => {
    try {
      const params = new URLSearchParams();
      if (search) {
        params.set('search', search);
      }
      const response = await api.get<ArtifactListItemDto[]>(`/models/artifacts?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  listExtracted: async (): Promise<ExtractedModelDto[]> => {
    try {
      const response = await api.get<ExtractedModelDto[]>(`/models/extracted`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getUnitGlbUrl: (id: string): string => `/api/models/unit/${id}/glb`,

  getMapObjectGlbUrl: (category: string, name: string): string => `/api/models/map-object/${category}/${name}/glb`,

  getUnitStatus: async (id: string): Promise<ModelStatusDto> => {
    try {
      const response = await api.get<ModelStatusDto>(`/models/unit/${id}/status`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getMapObjectStatus: async (category: string, name: string): Promise<ModelStatusDto> => {
    try {
      const response = await api.get<ModelStatusDto>(`/models/map-object/${category}/${name}/status`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const extractionApi = {
  start: async (options?: {
    extractPng?: boolean;
    extractGlb?: boolean;
    forceReExtract?: boolean;
  }): Promise<{ jobId: string }> => {
    try {
      const response = await api.post<{ jobId: string }>('/extraction/start', options || {});
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  cancel: async (): Promise<void> => {
    try {
      await api.post('/extraction/cancel');
    } catch (error) {
      handleApiError(error);
    }
  },

  getStatus: async (): Promise<{
    status: string;
    progress: {
      jobId: string;
      current: number;
      total: number;
      percent: number;
      currentAsset: string;
      elapsed: string;
      estimatedRemaining: string | null;
    } | null;
  }> => {
    try {
      const response = await api.get('/extraction/status');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getAutoExtract: async (): Promise<boolean> => {
    try {
      const response = await api.get<{ enabled: boolean }>('/extraction/auto-extract');
      return response.data.enabled;
    } catch (error) {
      handleApiError(error);
    }
  },

  setAutoExtract: async (enabled: boolean): Promise<void> => {
    try {
      await api.put('/extraction/auto-extract', { enabled });
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const searchApi = {
  search: async (query: string, limit = 20): Promise<SearchResponse> => {
    try {
      const params = new URLSearchParams({
        q: query,
        limit: String(limit),
      });
      const response = await api.get<SearchResponse>(`/search?${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const settingsApi = {
  getSettings: async (): Promise<SettingsDto> => {
    try {
      const response = await api.get<SettingsDto>('/settings');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  updateSettings: async (settings: UpdateSettingsRequest): Promise<SettingsDto> => {
    try {
      const response = await api.put<SettingsDto>('/settings', settings);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  getLocales: async (): Promise<LocalesDto> => {
    try {
      const response = await api.get<LocalesDto>('/settings/locales');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  checkUpdate: async (): Promise<ReleaseInfo | null> => {
    try {
      const response = await api.get<ReleaseInfo | null>('/settings/check-update');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  installUpdate: async (release: ReleaseInfo): Promise<void> => {
    try {
      await api.post('/settings/install-update', release);
    } catch (error) {
      handleApiError(error);
    }
  },

  getUpdateProgress: async (): Promise<UpdateProgress | null> => {
    try {
      const response = await api.get<UpdateProgress | null>('/settings/update-progress');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const labelsApi = {
  getLabels: async (): Promise<LabelsDto> => {
    try {
      const response = await api.get<LabelsDto>('/labels');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export const filesystemApi = {
  getRoots: async (): Promise<FilesystemRootsDto> => {
    try {
      const response = await api.get<FilesystemRootsDto>('/filesystem/roots');
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },

  listDirectory: async (path?: string): Promise<DirectoryListingDto> => {
    try {
      const params = path ? `?path=${encodeURIComponent(path)}` : '';
      const response = await api.get<DirectoryListingDto>(`/filesystem/list${params}`);
      return response.data;
    } catch (error) {
      handleApiError(error);
    }
  },
};

export default api;
