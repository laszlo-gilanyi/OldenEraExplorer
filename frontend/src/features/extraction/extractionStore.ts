import { create } from 'zustand';

export type ExtractionStatus = 'Idle' | 'Extracting' | 'Completed' | 'Cancelled' | 'Failed';

export interface ExtractionProgress {
  jobId: string;
  phase: string;
  current: number;
  total: number;
  percent: number;
  currentAsset: string;
  elapsed: string;
  estimatedRemaining: string | null;
}

export interface CompletionStats {
  success: number;
  failed: number;
  total: number;
}

export interface ManifestInfo {
  lastExtractedAt: string | null;
  iconCount: number;
  modelCount: number;
  gameVersion: string | null;
  error: string | null;
}

interface ExtractionState {
  status: ExtractionStatus;
  progress: ExtractionProgress | null;
  lastError: string | null;
  isConnected: boolean;
  isPanelOpen: boolean;
  completionStats: CompletionStats | null;
  manifestInfo: ManifestInfo;

  setStatus: (status: ExtractionStatus) => void;
  setProgress: (progress: ExtractionProgress | null) => void;
  setConnected: (connected: boolean) => void;
  setError: (error: string | null) => void;
  setPanelOpen: (open: boolean) => void;
  setCompletionStats: (stats: CompletionStats | null) => void;
  setManifestInfo: (info: ManifestInfo) => void;
  reset: () => void;
}

const defaultManifestInfo: ManifestInfo = {
  lastExtractedAt: null,
  iconCount: 0,
  modelCount: 0,
  gameVersion: null,
  error: null,
};

export const useExtractionStore = create<ExtractionState>((set) => ({
  status: 'Idle',
  progress: null,
  lastError: null,
  isConnected: false,
  isPanelOpen: false,
  completionStats: null,
  manifestInfo: defaultManifestInfo,

  setStatus: (status) => set({ status }),
  setProgress: (progress) => set({ progress }),
  setConnected: (isConnected) => set({ isConnected }),
  setError: (lastError) => set({ lastError }),
  setPanelOpen: (isPanelOpen) => set({ isPanelOpen }),
  setCompletionStats: (completionStats) => set({ completionStats }),
  setManifestInfo: (manifestInfo) => set({ manifestInfo }),
  reset: () => set({ status: 'Idle', progress: null, lastError: null, completionStats: null }),
}));
