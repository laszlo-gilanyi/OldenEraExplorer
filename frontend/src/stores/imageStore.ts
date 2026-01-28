import { create } from 'zustand';

export function normalizeImagePath(path: string | null | undefined): string {
  if (!path) return '';
  let p = path.replace(/\\/g, '/').trim().toLowerCase();
  if (p.endsWith('.png')) p = p.slice(0, -4);
  const idx = p.lastIndexOf('/');
  return idx !== -1 ? p.slice(idx + 1) : p;
}

const normalize = normalizeImagePath;

// Persists for app lifetime (outside React state) to avoid re-fetching already-loaded images
export const globalImageCache = new Set<string>();

interface ImageState {
  version: number;
  retryCount: number;
  loadedImages: Set<string>;

  isLoaded: (path: string | null | undefined) => boolean;
  markLoaded: (path: string | null | undefined) => void;
  triggerRetry: () => void;
  reset: () => void;
}

export const useImageStore = create<ImageState>((set, get) => ({
  version: 0,
  retryCount: 0,
  loadedImages: new Set(),

  isLoaded: (path) => {
    const key = normalize(path);
    return key ? get().loadedImages.has(key) : false;
  },

  markLoaded: (path) => {
    const key = normalize(path);
    if (key && !get().loadedImages.has(key)) {
      set((s) => ({ loadedImages: new Set([...s.loadedImages, key]) }));
    }
  },

  triggerRetry: () => set((s) => ({ retryCount: s.retryCount + 1 })),

  reset: () => {
    globalImageCache.clear();
    return set((s) => ({
      version: s.version + 1,
      retryCount: s.retryCount + 1,
      loadedImages: new Set(),
    }));
  },
}));
