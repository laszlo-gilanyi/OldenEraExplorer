import { useState, useMemo, useCallback, useRef, useEffect, type ChangeEvent } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { useUnitModels, useMapObjectModels, useArtifactModels } from './useModels';
import { modelsApi } from '@/api/client';
import type { UnitListItemDto, MapObjectListItemDto, ArtifactListItemDto } from '@/api/types';
import ModelViewer from './ModelViewer';
import type { ModelViewerHandle, CameraPreset, CameraSyncState, TextureSlot } from './ModelViewer';
import { useViewerStore } from './viewerStore';
import { useViewerGui } from './useViewerGui';
import SearchBox from '@/features/search/SearchBox';
import ErrorBoundary from '@/components/feedback/ErrorBoundary';
import ProgressiveIcon from '@/components/display/ProgressiveIcon';
import SortableColumnHeader, { type SortDirection } from '@/components/display/SortableColumnHeader';
import { useColumnLabels, useLabels } from '@/hooks/useLabels';
// DRAG-AND-DROP FEATURE - Comment out the next line to disable
import DropZoneOverlay, { useDroppedFile } from '@/components/feedback/DropZoneOverlay';

type ModelType = 'units' | 'map-objects' | 'artifacts';
type ViewerItem = UnitListItemDto | MapObjectListItemDto | ArtifactListItemDto;

function isUnitListItem(item: ViewerItem): item is UnitListItemDto {
  return 'faction' in item || 'tier' in item;
}

function isMapObjectListItem(item: ViewerItem): item is MapObjectListItemDto {
  return 'category' in item && !('tier' in item) && !('rarity' in item);
}

function isArtifactListItem(item: ViewerItem): item is ArtifactListItemDto {
  return 'rarity' in item || 'slot' in item;
}

const rarityColorClasses: Record<string, string> = {
  common: 'text-rarity-common',
  uncommon: 'text-rarity-uncommon',
  rare: 'text-rarity-rare',
  epic: 'text-rarity-epic',
  legendary: 'text-rarity-legendary',
  mythic: 'text-rarity-mythic',
};

function getRarityColorClass(rarity: string | null): string {
  if (!rarity) return 'text-muted-foreground';
  return rarityColorClasses[rarity.toLowerCase()] || 'text-muted-foreground';
}

function getIcon(item: ViewerItem): string | null | undefined {
  if (isUnitListItem(item)) return item.iconPath;
  if (isMapObjectListItem(item)) return item.icon;
  if (isArtifactListItem(item)) return item.icon;
  return null;
}

function getScale(item: ViewerItem): number | null | undefined {
  if (isUnitListItem(item)) return item.scale;
  return null;
}

function getModelNameColorClass(item: ViewerItem, isSelected: boolean): string {
  if (isSelected) return '';
  if (isArtifactListItem(item)) {
    return getRarityColorClass(item.rarity);
  }
  return '';
}

type UnitSortField = 'name' | 'tier' | 'faction' | 'id';
type MapObjectSortField = 'name' | 'id';
type ArtifactSortField = 'name' | 'id';

function compareUnits(
  a: ViewerItem,
  b: ViewerItem,
  primaryField: UnitSortField,
  primaryDirection: SortDirection
): number {
  const dir = primaryDirection === 'asc' ? 1 : -1;

  const getFaction = (item: ViewerItem) =>
    isUnitListItem(item) ? (item.factionDisplay || '') : '';
  const getTier = (item: ViewerItem) =>
    isUnitListItem(item) ? (item.tier ?? 999) : 999;

  const compareFaction = () => getFaction(a).localeCompare(getFaction(b));
  const compareTier = () => getTier(a) - getTier(b);
  const compareId = () => a.id.localeCompare(b.id, undefined, { numeric: true });
  const compareName = () => (a.name || '').localeCompare(b.name || '');

  switch (primaryField) {
    case 'faction':
      return compareFaction() * dir || compareTier() || compareId();
    case 'tier':
      return compareTier() * dir || compareFaction() || compareId();
    case 'name':
      return compareName() * dir;
    case 'id':
      return compareId() * dir;
    default:
      return 0;
  }
}

function compareByField(
  a: ViewerItem,
  b: ViewerItem,
  field: 'name' | 'id',
  direction: SortDirection
): number {
  const dir = direction === 'asc' ? 1 : -1;
  if (field === 'name') {
    return (a.name || '').localeCompare(b.name || '') * dir;
  }
  return a.id.localeCompare(b.id, undefined, { numeric: true }) * dir;
}

export default function ViewerPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const params = useParams<{ modelId?: string; '*'?: string }>();
  const { label } = useLabels();

  const modelViewerRef = useRef<ModelViewerHandle>(null);
  const comparisonViewerRef = useRef<ModelViewerHandle>(null);
  const viewerContainerRef = useRef<HTMLDivElement>(null);
  // The primary viewer's framing element. Stats overlay attaches here so it stays anchored to the
  // primary canvas even when viewerContainerRef becomes the grid that hosts both viewers.
  const primaryViewerFrameRef = useRef<HTMLDivElement>(null);
  const textureInputRef = useRef<HTMLInputElement>(null);

  const modelType: ModelType = useMemo(() => {
    if (location.pathname.includes('/viewer/artifacts')) {
      return 'artifacts';
    }
    if (location.pathname.includes('/viewer/map-objects')) {
      return 'map-objects';
    }
    return 'units';
  }, [location.pathname]);

  const setDisplayMode = useViewerStore((s) => s.setDisplayMode);

  const [isPortrait, setIsPortrait] = useState(true);
  const [compareMode, setCompareMode] = useState(false);
  // Sticky flag: once the user has engaged compare in this ViewerPage session, keep the secondary
  // viewer mounted (hidden via CSS when compare is off) so subsequent toggles do not show the
  // loading spinner again. Resets only when ViewerPage itself remounts (modelType route change).
  const [compareEverActive, setCompareEverActive] = useState(false);
  useEffect(() => {
    if (compareMode) setCompareEverActive(true);
  }, [compareMode]);
  // In single view, decides whether the primary canvas applies the user's custom textures or
  // stays on the original baseline. Default is "original" - uploading a texture pops compare on
  // so the user can see both side by side, and "Keep / Show custom" via the caret is the only
  // way to flip the single-view preference to custom.
  const [applyCustomToPrimary, setApplyCustomToPrimary] = useState(false);
  const [compareMenuOpen, setCompareMenuOpen] = useState(false);
  const [cameraSyncState, setCameraSyncState] = useState<CameraSyncState | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [textureSlots, setTextureSlots] = useState<TextureSlot[]>([]);
  const [showOptionalEmissionSlots, setShowOptionalEmissionSlots] = useState(false);
  const [activeTextureSlotId, setActiveTextureSlotId] = useState<string | null>(null);
  const [texturePanelOpen, setTexturePanelOpen] = useState(false);
  const [customTextures, setCustomTextures] = useState<
    Record<string, { file: File; blobUrl: string }>
  >({});
  // Snapshot used by the unmount-time cleanup effect (which cannot read state directly).
  const customTexturesRef = useRef(customTextures);
  const texturePanelRef = useRef<HTMLDivElement>(null);
  const textureButtonRef = useRef<HTMLButtonElement>(null);
  const compareMenuRef = useRef<HTMLDivElement>(null);
  const compareMenuButtonRef = useRef<HTMLButtonElement>(null);
  const columnLabels = useColumnLabels();

  const [unitSortField, setUnitSortField] = useState<UnitSortField>('faction');
  const [unitSortDirection, setUnitSortDirection] = useState<SortDirection>('asc');
  const [artifactSortField, setArtifactSortField] = useState<ArtifactSortField>('name');
  const [artifactSortDirection, setArtifactSortDirection] = useState<SortDirection>('asc');
  const [mapObjectSortField, setMapObjectSortField] = useState<MapObjectSortField>('name');
  const [mapObjectSortDirection, setMapObjectSortDirection] = useState<SortDirection>('asc');

  const { droppedFile, handleFileDropped: onFileDropped, clearDroppedFile } = useDroppedFile();

  useEffect(() => {
    customTexturesRef.current = customTextures;
  }, [customTextures]);

  // Revoke any leftover object URLs on unmount so we do not leak the uploaded images.
  useEffect(() => {
    return () => {
      Object.values(customTexturesRef.current).forEach(({ blobUrl }) => {
        URL.revokeObjectURL(blobUrl);
      });
    };
  }, []);

  const clearCustomTexture = useCallback((slotId?: string) => {
    setCustomTextures((current) => {
      if (slotId) {
        const texture = current[slotId];
        if (!texture) return current;
        URL.revokeObjectURL(texture.blobUrl);
        const next = { ...current };
        delete next[slotId];
        return next;
      }

      Object.values(current).forEach(({ blobUrl }) => URL.revokeObjectURL(blobUrl));
      return {};
    });

    if (!slotId) {
      // After a full reset, drop back to the default preference (original).
      setApplyCustomToPrimary(false);
      if (textureInputRef.current) textureInputRef.current.value = '';
    }
  }, []);

  const handleCameraSyncStateChange = useCallback((state: CameraSyncState) => {
    setCameraSyncState(state);
  }, []);

  useEffect(() => {
    if (!compareMode) setCameraSyncState(null);
  }, [compareMode]);

  // Close the texture popover when the user clicks anywhere outside it (or its trigger button).
  useEffect(() => {
    if (!texturePanelOpen) return;
    const onDocPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      if (texturePanelRef.current?.contains(target)) return;
      if (textureButtonRef.current?.contains(target)) return;
      setTexturePanelOpen(false);
    };
    document.addEventListener('pointerdown', onDocPointerDown);
    return () => document.removeEventListener('pointerdown', onDocPointerDown);
  }, [texturePanelOpen]);

  // Same click-outside behavior for the compare split-button dropdown.
  useEffect(() => {
    if (!compareMenuOpen) return;
    const onDocPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      if (compareMenuRef.current?.contains(target)) return;
      if (compareMenuButtonRef.current?.contains(target)) return;
      setCompareMenuOpen(false);
    };
    document.addEventListener('pointerdown', onDocPointerDown);
    return () => document.removeEventListener('pointerdown', onDocPointerDown);
  }, [compareMenuOpen]);


  // Toggling emission slots off drops any uploads pinned to the (now-hidden) optional slots so they
  // do not silently keep affecting materials.
  const handleOptionalEmissionToggle = useCallback(() => {
    if (showOptionalEmissionSlots) {
      setCustomTextures((current) => {
        const next = { ...current };
        Object.keys(next).forEach((slotId) => {
          if (slotId.startsWith('optionalEmissive:')) {
            URL.revokeObjectURL(next[slotId].blobUrl);
            delete next[slotId];
          }
        });
        return next;
      });

      if (activeTextureSlotId?.startsWith('optionalEmissive:')) {
        setActiveTextureSlotId(null);
      }
    }

    setShowOptionalEmissionSlots((value) => !value);
  }, [activeTextureSlotId, showOptionalEmissionSlots]);

  const handleTextureSlotSelect = useCallback((slotId: string) => {
    setActiveTextureSlotId(slotId);
    textureInputRef.current?.click();
  }, []);

  const handleCustomTextureChange = useCallback(
    (event: ChangeEvent<HTMLInputElement>) => {
      const file = event.currentTarget.files?.[0];
      const slotId = activeTextureSlotId ?? textureSlots[0]?.id;
      event.currentTarget.value = '';

      if (!file || !slotId) return;
      if (!file.type.startsWith('image/')) return;

      const blobUrl = URL.createObjectURL(file);
      setActiveTextureSlotId(null);

      // First-ever upload in this session pops compare on automatically so the user can see the
      // original next to their new texture. Subsequent uploads do not change the view mode; the
      // user's prior preference (Show original / Show custom) is preserved.
      const wasEmpty = Object.keys(customTexturesRef.current).length === 0;
      if (wasEmpty) setCompareMode(true);

      setCustomTextures((current) => {
        const existing = current[slotId];
        if (existing) URL.revokeObjectURL(existing.blobUrl);
        return { ...current, [slotId]: { file, blobUrl } };
      });
    },
    [activeTextureSlotId, textureSlots],
  );

  const handleFileDropped = useCallback((file: File, blobUrl: string) => {
    clearCustomTexture();
    onFileDropped(file, blobUrl);
    navigate(`/viewer/${modelType}`);
  }, [clearCustomTexture, onFileDropped, navigate, modelType]);

  const unitsQuery = useUnitModels(
    modelType === 'units' ? searchQuery || undefined : undefined
  );

  const mapObjectsQuery = useMapObjectModels(
    modelType === 'map-objects' ? searchQuery || undefined : undefined
  );

  const artifactsQuery = useArtifactModels(
    modelType === 'artifacts' ? searchQuery || undefined : undefined
  );

  const currentQuery = modelType === 'units'
    ? unitsQuery
    : modelType === 'map-objects'
      ? mapObjectsQuery
      : artifactsQuery;

  const sortedModels = useMemo(() => {
    const data = currentQuery.data;
    if (!data) return [];

    if (modelType === 'units') {
      return [...data].sort((a, b) => compareUnits(a, b, unitSortField, unitSortDirection));
    } else if (modelType === 'artifacts') {
      return [...data].sort((a, b) => compareByField(a, b, artifactSortField, artifactSortDirection));
    } else {
      return [...data].sort((a, b) => compareByField(a, b, mapObjectSortField, mapObjectSortDirection));
    }
  }, [currentQuery.data, modelType, unitSortField, unitSortDirection, artifactSortField, artifactSortDirection, mapObjectSortField, mapObjectSortDirection]);

  const handleUnitSort = useCallback((field: string) => {
    const f = field as UnitSortField;
    if (f === unitSortField) {
      setUnitSortDirection(prev => prev === 'asc' ? 'desc' : 'asc');
    } else {
      setUnitSortField(f);
      setUnitSortDirection('asc');
    }
  }, [unitSortField]);

  const handleArtifactSort = useCallback((field: string) => {
    const f = field as ArtifactSortField;
    if (f === artifactSortField) {
      setArtifactSortDirection(prev => prev === 'asc' ? 'desc' : 'asc');
    } else {
      setArtifactSortField(f);
      setArtifactSortDirection('asc');
    }
  }, [artifactSortField]);

  const handleMapObjectSort = useCallback((field: string) => {
    const f = field as MapObjectSortField;
    if (f === mapObjectSortField) {
      setMapObjectSortDirection(prev => prev === 'asc' ? 'desc' : 'asc');
    } else {
      setMapObjectSortField(f);
      setMapObjectSortDirection('asc');
    }
  }, [mapObjectSortField]);

  const lastKnownSelectedModelRef = useRef<ViewerItem | null>(null);

  // Helper: Find model by ID or prefabPath (for artifacts/map-objects with full path in URL)
  const findModel = useCallback((modelId: string): ViewerItem | undefined => {
    return currentQuery.data?.find((m: ViewerItem) => {
      // Match by ID first
      if (m.id === modelId) return true;

      // For map objects and artifacts, also try matching by prefabPath
      if (isMapObjectListItem(m) || isArtifactListItem(m)) {
        return m.prefabPath === modelId;
      }

      return false;
    });
  }, [currentQuery.data]);

  /* eslint-disable react-hooks/refs -- Ref caching pattern for filtered model persistence */
  const selectedModel: ViewerItem | null = useMemo(() => {
    const catchAllParam = params['*'];

    if (modelType === 'units' && params.modelId) {
      const found = findModel(params.modelId);
      if (found) {
        lastKnownSelectedModelRef.current = found;
        return found;
      }
      if (lastKnownSelectedModelRef.current?.id === params.modelId) {
        return lastKnownSelectedModelRef.current;
      }
      return {
        id: params.modelId,
        name: params.modelId,
        faction: null,
        factionDisplay: null,
        tier: null,
        iconPath: null,
      } as UnitListItemDto;
    } else if ((modelType === 'map-objects' || modelType === 'artifacts') && catchAllParam) {
      const modelId = catchAllParam;
      const found = findModel(modelId);
      if (found) {
        lastKnownSelectedModelRef.current = found;
        return found;
      }
      if (lastKnownSelectedModelRef.current?.id === modelId) {
        return lastKnownSelectedModelRef.current;
      }
      if (modelType === 'map-objects') {
        const firstSlash = modelId.indexOf('/');
        const category = firstSlash > 0 ? modelId.substring(0, firstSlash) : null;
        const name = firstSlash > 0 ? modelId.substring(firstSlash + 1) : modelId;

        return {
          id: name,
          name: modelId,
          category: category,
          icon: null,
          prefabPath: modelId,
        } as MapObjectListItemDto;
      } else {
        return {
          id: modelId,
          name: modelId,
          rarity: null,
          slot: null,
          raritySlotText: null,
          icon: null,
          prefabPath: `artifact/${modelId}`,
        } as ArtifactListItemDto;
      }
    }
    return null;
  }, [modelType, params, findModel]);
  /* eslint-enable react-hooks/refs */

  useEffect(() => {
    const isOrphanModel = selectedModel?.isOrphan || false;
    const defaultMode = modelType === 'units' && !isOrphanModel ? 'game-preview' : 'studio';
    setDisplayMode(defaultMode);
  }, [modelType, setDisplayMode, selectedModel]);

  const handleModelTypeChange = useCallback((type: ModelType) => {
    setSearchQuery('');
    clearCustomTexture();
    navigate(`/viewer/${type}`);
  }, [clearCustomTexture, navigate]);

  const handleSelectModel = useCallback((model: ViewerItem) => {
    clearDroppedFile();
    clearCustomTexture();

    if (isUnitListItem(model)) {
      navigate(`/viewer/units/${model.id}`);
    } else if (isArtifactListItem(model)) {
      navigate(`/viewer/artifacts/${model.id}`);
    } else if (isMapObjectListItem(model)) {
      navigate(`/viewer/map-objects/${model.prefabPath || model.id}`);
    }
  }, [navigate, clearDroppedFile, clearCustomTexture]);

  const handleSearchChange = useCallback((query: string) => {
    setSearchQuery(query);
  }, []);

  const handleClearSearch = useCallback(() => {
    setSearchQuery('');
  }, []);

  const handleResetCamera = useCallback(() => {
    modelViewerRef.current?.resetCamera();
    comparisonViewerRef.current?.resetCamera();
  }, []);

  const handleCameraPreset = useCallback((preset: string) => {
    const typedPreset = preset as CameraPreset;
    modelViewerRef.current?.setCameraPreset(typedPreset);
    comparisonViewerRef.current?.setCameraPreset(typedPreset);
  }, []);

  // Pulls from whichever viewer is currently displaying the customs: secondary in compare mode,
  // primary otherwise. In single mode with "Show original" active the primary is on the baseline,
  // and the export then reflects that current view - the file always matches what is on screen.
  const handleExportGlb = useCallback(async () => {
    const ref = compareMode ? comparisonViewerRef : modelViewerRef;
    try {
      const buffer = await ref.current?.exportGlb();
      if (!buffer) return;
      const blob = new Blob([buffer], { type: 'model/gltf-binary' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      const modelName = selectedModel?.name || 'model';
      const sanitized = modelName.replace(/[^a-z0-9]/gi, '_').toLowerCase();
      const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
      link.download = `${sanitized}_custom_${timestamp}.glb`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('GLB export failed:', err);
    }
  }, [compareMode, selectedModel]);

  const handleScreenshot = useCallback(
    (target: 'primary' | 'secondary' = 'primary') => {
      const ref = target === 'secondary' ? comparisonViewerRef : modelViewerRef;
      const dataUrl = ref.current?.takeScreenshot();
      if (!dataUrl) return;

      const link = document.createElement('a');
      link.href = dataUrl;

      const modelName = selectedModel?.name || 'model';
      const sanitizedName = modelName.replace(/[^a-z0-9]/gi, '_').toLowerCase();
      const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
      // Include the side in the filename so original vs custom screenshots do not silently overwrite.
      const sideTag = target === 'secondary' ? '_custom' : '';
      link.download = `${sanitizedName}${sideTag}_${timestamp}.png`;

      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
    },
    [selectedModel],
  );

  const isOrphan = selectedModel?.isOrphan || false;
  const isArtifactOrMapObject = modelType === 'artifacts' || modelType === 'map-objects';

  useViewerGui({
    containerRef: primaryViewerFrameRef,
    onResetCamera: handleResetCamera,
    onCameraPreset: handleCameraPreset,
    isOrphan: isOrphan || isArtifactOrMapObject,
  });

  /* eslint-disable react-hooks/refs -- selectedModel is derived from ref-cached value */
  const glbUrl = useMemo((): string | null => {
    if (droppedFile) {
      return droppedFile.blobUrl;
    }

    if (!selectedModel) return null;

    if (isUnitListItem(selectedModel)) {
      return modelsApi.getUnitGlbUrl(selectedModel.id);
    } else {
      const prefabPath = (isMapObjectListItem(selectedModel) || isArtifactListItem(selectedModel))
        ? selectedModel.prefabPath
        : null;

      if (!prefabPath) return null;

      const firstSlash = prefabPath.indexOf('/');
      if (firstSlash < 0) return null;
      const category = prefabPath.substring(0, firstSlash);
      const name = prefabPath.substring(firstSlash + 1);
      return modelsApi.getMapObjectGlbUrl(category, name);
    }
  }, [selectedModel, droppedFile]);
  /* eslint-enable react-hooks/refs */

  // Reset slot detection and any open popover when the loaded model changes; new slots come from onTextureSlotsReady.
  // Also clear customTextures here: slot IDs are positional (baseColor:0 etc.) and would otherwise
  // leak onto whatever model the user navigates to via browser back/forward or any path that does
  // not go through handleSelectModel/handleModelTypeChange/handleFileDropped.
  useEffect(() => {
    setTextureSlots([]);
    setShowOptionalEmissionSlots(false);
    setActiveTextureSlotId(null);
    setTexturePanelOpen(false);
    clearCustomTexture();
  }, [glbUrl, clearCustomTexture]);

  const customTextureUrls = useMemo(
    () =>
      Object.fromEntries(
        Object.entries(customTextures).map(([slotId, texture]) => [slotId, texture.blobUrl]),
      ),
    [customTextures],
  );

  const hasCustomTextures = Object.keys(customTextures).length > 0;
  const hasDiffuseTextureSlots = textureSlots.some((slot) => slot.channel === 'map');

  // When the user opts in, synthesize "optional emission" rows for every diffuse slot that does
  // not already have a real emission slot. The viewer pre-registered targets under the matching
  // optionalEmissive:* ids so the uploads will hit the right materials.
  const displayTextureSlots = useMemo(() => {
    if (!showOptionalEmissionSlots) return textureSlots;

    const detectedEmissionBaseSlotIds = new Set(
      textureSlots
        .filter((slot) => slot.channel === 'emissiveMap' && slot.baseSlotId)
        .map((slot) => slot.baseSlotId),
    );

    const optionalEmissionSlots = textureSlots
      .filter((slot) => slot.channel === 'map' && !detectedEmissionBaseSlotIds.has(slot.id))
      .map((slot) => {
        const baseName = slot.name.replace(/\s*Diffuse$/i, '').trim();
        return {
          id: `optionalEmissive:${slot.id}`,
          name: baseName ? `${baseName} Emission` : 'Emission',
          materialNames: slot.materialNames,
          channel: 'emissiveMap' as const,
          baseSlotId: slot.id,
          optional: true,
        };
      });

    return [...textureSlots, ...optionalEmissionSlots];
  }, [showOptionalEmissionSlots, textureSlots]);

  // Slots grouped by their owning diffuse: one group per base-color texture, plus an orphan group
  // at the end for any standalone emission slots that have no diffuse parent. Group names are the
  // raw first material name attached to the base slot - no stripping, no heuristics.
  type SlotGroup = { groupName: string; slots: TextureSlot[] };
  const groupedTextureSlots: SlotGroup[] = useMemo(() => {
    if (displayTextureSlots.length === 0) return [];

    const baseSlots = displayTextureSlots.filter((s) => s.channel === 'map');
    const childrenByBase = new Map<string, TextureSlot[]>();
    displayTextureSlots.forEach((s) => {
      if (s.baseSlotId) {
        const list = childrenByBase.get(s.baseSlotId) ?? [];
        list.push(s);
        childrenByBase.set(s.baseSlotId, list);
      }
    });

    const groups: SlotGroup[] = baseSlots.map((base) => ({
      groupName: base.materialNames[0] ?? base.name,
      slots: [base, ...(childrenByBase.get(base.id) ?? [])],
    }));

    const orphans = displayTextureSlots.filter(
      (s) => s.channel === 'emissiveMap' && !s.baseSlotId,
    );
    if (orphans.length > 0) {
      groups.push({ groupName: 'Emission', slots: orphans });
    }

    return groups;
  }, [displayTextureSlots]);

  const renderTexturePanel = () => (
    <div
      ref={texturePanelRef}
      className="absolute right-0 top-full mt-1 z-[110] w-80 rounded-lg border border-border bg-card shadow-xl"
      role="dialog"
      aria-label="Textures"
    >
      <input
        ref={textureInputRef}
        type="file"
        accept="image/png,image/jpeg,image/webp,.png,.jpg,.jpeg,.webp"
        className="hidden"
        onChange={handleCustomTextureChange}
      />

      <div className="px-3 py-2 border-b border-border flex items-center justify-between">
        <h3 className="m-0 text-sm font-medium text-foreground">Textures</h3>
        {hasCustomTextures && (
          <button
            type="button"
            onClick={() => clearCustomTexture()}
            className="px-2 py-0.5 text-xs bg-transparent hover:bg-muted text-muted-foreground hover:text-foreground rounded border border-border cursor-pointer transition-colors"
          >
            Reset all
          </button>
        )}
      </div>

      <div className="max-h-72 overflow-y-auto px-2 py-2 flex flex-col gap-2">
        {groupedTextureSlots.length === 0 ? (
          <div className="px-2 py-3 text-xs text-muted-foreground text-center">
            Detecting texture slots...
          </div>
        ) : (
          groupedTextureSlots.map((group, groupIdx) => (
            <div key={`${group.groupName}_${groupIdx}`} className="flex flex-col">
              <div className="px-2 pt-1 pb-1 text-xs text-muted-foreground font-medium">
                {group.groupName}
              </div>
              {group.slots.map((slot) => {
                const selectedTexture = customTextures[slot.id];
                const channelLabel = slot.channel === 'emissiveMap' ? 'Emission' : 'Diffuse';
                return (
                  <div
                    key={slot.id}
                    className="flex items-center gap-2 px-2 py-1.5 rounded hover:bg-muted/50 transition-colors"
                    title={slot.materialNames.join(', ')}
                  >
                    <button
                      type="button"
                      disabled={!glbUrl}
                      onClick={() => handleTextureSlotSelect(slot.id)}
                      className="flex-1 min-w-0 flex items-center gap-2 bg-transparent border-0 text-left cursor-pointer disabled:cursor-not-allowed disabled:opacity-50"
                    >
                      <span
                        className={`w-2 h-2 rounded-full shrink-0 ${
                          selectedTexture ? 'bg-primary' : 'bg-border'
                        }`}
                      />
                      <span className="flex-1 min-w-0 flex flex-col gap-0.5">
                        <span className="text-xs text-foreground">{channelLabel}</span>
                        {selectedTexture ? (
                          <span className="text-[10px] text-muted-foreground truncate">
                            {selectedTexture.file.name}
                          </span>
                        ) : slot.sourceTextureName ? (
                          <span className="text-[10px] text-muted-foreground/60 italic truncate">
                            {slot.sourceTextureName}
                          </span>
                        ) : (
                          <span className="text-[10px] text-muted-foreground/60 italic truncate">
                            Not set
                          </span>
                        )}
                      </span>
                    </button>
                    {selectedTexture && (
                      <button
                        type="button"
                        onClick={() => clearCustomTexture(slot.id)}
                        aria-label="Reset texture"
                        className="shrink-0 w-6 h-6 flex items-center justify-center bg-transparent hover:bg-muted text-muted-foreground hover:text-foreground rounded border-0 cursor-pointer transition-colors"
                      >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                          <line x1="18" y1="6" x2="6" y2="18" />
                          <line x1="6" y1="6" x2="18" y2="18" />
                        </svg>
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
          ))
        )}
      </div>

      {hasDiffuseTextureSlots && (
        <div className="px-3 py-2 border-t border-border">
          <label className="flex items-center gap-2 cursor-pointer text-xs text-muted-foreground hover:text-foreground transition-colors">
            <input
              type="checkbox"
              checked={showOptionalEmissionSlots}
              onChange={handleOptionalEmissionToggle}
              className="cursor-pointer"
            />
            Show emission slots
          </label>
        </div>
      )}

      {glbUrl && (
        <div className="px-3 py-2 border-t border-border">
          <button
            type="button"
            onClick={handleExportGlb}
            className="w-full px-2 py-1.5 text-xs bg-muted hover:bg-muted/80 text-foreground rounded border border-border cursor-pointer transition-colors"
          >
            Export GLB
          </button>
        </div>
      )}
    </div>
  );

  const iconButtonClass =
    'p-1 bg-transparent text-muted-foreground hover:text-foreground border-0 cursor-pointer transition-colors';

  // Buttons anchored to the corners of the primary canvas. These must stay on the canvas itself
  // (not the outer compare container) so they keep their original placement when compare turns on.
  const renderCompareMenu = () => {
    type MenuItem = { label: string; onClick: () => void };
    const items: MenuItem[] = compareMode
      ? [
          {
            label: 'Keep original',
            onClick: () => {
              setCompareMode(false);
              setApplyCustomToPrimary(false);
              setCompareMenuOpen(false);
            },
          },
          {
            label: 'Keep custom',
            onClick: () => {
              setCompareMode(false);
              setApplyCustomToPrimary(true);
              setCompareMenuOpen(false);
            },
          },
        ]
      : applyCustomToPrimary
        ? [
            {
              label: 'Show original',
              onClick: () => {
                setApplyCustomToPrimary(false);
                setCompareMenuOpen(false);
              },
            },
          ]
        : [
            {
              label: 'Show custom',
              onClick: () => {
                setApplyCustomToPrimary(true);
                setCompareMenuOpen(false);
              },
            },
          ];

    return (
      <div
        ref={compareMenuRef}
        className="absolute right-0 top-full mt-1 z-[110] min-w-44 rounded-lg border border-border bg-card shadow-xl py-1"
        role="menu"
      >
        {items.map((item) => (
          <button
            key={item.label}
            role="menuitem"
            onClick={item.onClick}
            className="w-full text-left px-3 py-1.5 text-xs bg-transparent hover:bg-muted text-foreground border-0 cursor-pointer transition-colors"
          >
            {item.label}
          </button>
        ))}
      </div>
    );
  };

  const renderPrimaryCanvasButtons = () => (
    <>
      <button
        onClick={() => handleScreenshot('primary')}
        className={`absolute bottom-3 left-3 z-10 ${iconButtonClass}`}
        title={label('viewer_screenshot')}
      >
        <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M4 8v-2a2 2 0 0 1 2 -2h2" />
          <path d="M4 16v2a2 2 0 0 0 2 2h2" />
          <path d="M16 4h2a2 2 0 0 1 2 2v2" />
          <path d="M16 20h2a2 2 0 0 0 2 -2v-2" />
          <path d="M12 12m-3 0a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" />
        </svg>
      </button>
      {!compareMode && (
        <button
          onClick={() => setIsPortrait((p) => !p)}
          className={`absolute bottom-3 right-3 z-10 ${iconButtonClass}`}
          title="Toggle aspect"
        >
          {isPortrait ? (
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <polyline points="15 3 21 3 21 9" />
              <polyline points="9 21 3 21 3 15" />
              <line x1="21" y1="3" x2="14" y2="10" />
              <line x1="3" y1="21" x2="10" y2="14" />
            </svg>
          ) : (
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <polyline points="4 14 10 14 10 20" />
              <polyline points="20 10 14 10 14 4" />
              <line x1="10" y1="14" x2="3" y2="21" />
              <line x1="21" y1="3" x2="14" y2="10" />
            </svg>
          )}
        </button>
      )}
    </>
  );

  const renderSecondaryCanvasButtons = () => (
    <button
      onClick={() => handleScreenshot('secondary')}
      className={`absolute bottom-3 left-3 z-10 ${iconButtonClass}`}
      title={label('viewer_screenshot')}
    >
      <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M4 8v-2a2 2 0 0 1 2 -2h2" />
        <path d="M4 16v2a2 2 0 0 0 2 2h2" />
        <path d="M16 4h2a2 2 0 0 1 2 2v2" />
        <path d="M16 20h2a2 2 0 0 0 2 -2v-2" />
        <path d="M12 12m-3 0a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" />
      </svg>
    </button>
  );

  // Compare and texture controls sit in the header bar for now. They will move later per user direction.
  const renderHeaderIconButtons = () => (
    <div className="flex items-center gap-1">
      <div className="relative">
        <button
          ref={textureButtonRef}
          onClick={() => setTexturePanelOpen((v) => !v)}
          disabled={!glbUrl}
          className={`relative ${iconButtonClass} disabled:opacity-50 disabled:cursor-not-allowed ${
            texturePanelOpen || hasCustomTextures ? 'text-foreground' : ''
          }`}
          title="Textures"
          aria-expanded={texturePanelOpen}
        >
          <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="13.5" cy="6.5" r="0.5" fill="currentColor" />
            <circle cx="17.5" cy="10.5" r="0.5" fill="currentColor" />
            <circle cx="8.5" cy="7.5" r="0.5" fill="currentColor" />
            <circle cx="6.5" cy="12.5" r="0.5" fill="currentColor" />
            <path d="M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.555-2.503 5.555-5.554C21.965 6.012 17.461 2 12 2z" />
          </svg>
          {hasCustomTextures && (
            <span className="absolute -top-0.5 -right-0.5 w-2 h-2 rounded-full bg-primary" />
          )}
        </button>
        {texturePanelOpen && renderTexturePanel()}
      </div>
      <div className="relative flex items-center">
        <button
          onClick={() => {
            // Main click is a simple compare on/off toggle. Whatever the user previously chose for
            // single-view via the caret menu (Original vs Custom) is kept; the explicit "Keep ..."
            // / "Show ..." items remain the only way to flip that preference.
            setCompareMode((v) => !v);
            setCompareMenuOpen(false);
          }}
          disabled={!glbUrl}
          className={`${iconButtonClass} disabled:opacity-50 disabled:cursor-not-allowed ${
            compareMode ? 'text-foreground' : ''
          }`}
          title={compareMode ? 'Exit compare' : 'Compare'}
          aria-pressed={compareMode}
        >
          <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <rect x="3" y="4" width="8" height="16" rx="1" />
            <rect x="13" y="4" width="8" height="16" rx="1" />
          </svg>
        </button>
        {/* Caret reveals exit-options while in compare, or a quick toggle to switch the primary
            back/forth between original and custom in single view. Always rendered (with
            visibility:hidden when not useful) so the compare button does not slide horizontally
            when the caret appears or disappears. */}
        {(() => {
          const caretVisible = compareMode || hasCustomTextures;
          return (
            <button
              ref={compareMenuButtonRef}
              onClick={() => setCompareMenuOpen((v) => !v)}
              disabled={!glbUrl || !caretVisible}
              aria-hidden={!caretVisible}
              tabIndex={caretVisible ? 0 : -1}
              className={`px-0.5 ${iconButtonClass} disabled:opacity-50 disabled:cursor-not-allowed ${
                compareMenuOpen ? 'text-foreground' : ''
              } ${!caretVisible ? 'invisible pointer-events-none' : ''}`}
              title="More"
              aria-haspopup="menu"
              aria-expanded={compareMenuOpen}
            >
              <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                <polyline points="6 9 12 15 18 9" />
              </svg>
            </button>
          );
        })()}
        {compareMenuOpen && renderCompareMenu()}
      </div>
    </div>
  );

  return (
    <div className="h-full overflow-hidden">
      <aside className="absolute left-0 top-0 bottom-0 w-105 z-10 border-r border-border flex flex-col bg-card">
        <div className="h-[47px] px-2 border-b border-border flex items-center">
          <div className="flex gap-1 w-full">
            <button
              onClick={() => handleModelTypeChange('units')}
              className={`flex-1 px-2 py-1 cursor-pointer text-sm rounded-md transition-colors ${
                modelType === 'units'
                  ? 'bg-muted text-foreground font-medium border border-border'
                  : 'bg-transparent text-muted-foreground hover:text-foreground border border-transparent'
              }`}
            >
              {label('nav_units')}
            </button>
            <button
              onClick={() => handleModelTypeChange('artifacts')}
              className={`flex-1 px-2 py-1 cursor-pointer text-sm rounded-md transition-colors ${
                modelType === 'artifacts'
                  ? 'bg-muted text-foreground font-medium border border-border'
                  : 'bg-transparent text-muted-foreground hover:text-foreground border border-transparent'
              }`}
            >
              {label('nav_artifacts')}
            </button>
            <button
              onClick={() => handleModelTypeChange('map-objects')}
              className={`flex-1 px-2 py-1 cursor-pointer text-sm rounded-md transition-colors ${
                modelType === 'map-objects'
                  ? 'bg-muted text-foreground font-medium border border-border'
                  : 'bg-transparent text-muted-foreground hover:text-foreground border border-transparent'
              }`}
            >
              {label('nav_map_objects')}
            </button>
          </div>
        </div>

        <div className="px-2 h-[47px] border-b border-border flex items-center gap-0.5 relative">
          {modelType === 'units' && (
            <>
              <SortableColumnHeader
                label={columnLabels.name}
                field="name"
                currentField={unitSortField}
                direction={unitSortDirection}
                onSort={handleUnitSort}
              />
              <SortableColumnHeader
                label={label('label_unit_tier')}
                field="tier"
                currentField={unitSortField}
                direction={unitSortDirection}
                onSort={handleUnitSort}
              />
              <SortableColumnHeader
                label={columnLabels.faction}
                field="faction"
                currentField={unitSortField}
                direction={unitSortDirection}
                onSort={handleUnitSort}
              />
              <SortableColumnHeader
                label={columnLabels.id}
                field="id"
                currentField={unitSortField}
                direction={unitSortDirection}
                onSort={handleUnitSort}
              />
            </>
          )}
          {modelType === 'artifacts' && (
            <>
              <SortableColumnHeader
                label={columnLabels.name}
                field="name"
                currentField={artifactSortField}
                direction={artifactSortDirection}
                onSort={handleArtifactSort}
              />
              <SortableColumnHeader
                label={columnLabels.id}
                field="id"
                currentField={artifactSortField}
                direction={artifactSortDirection}
                onSort={handleArtifactSort}
              />
            </>
          )}
          {modelType === 'map-objects' && (
            <>
              <SortableColumnHeader
                label={columnLabels.name}
                field="name"
                currentField={mapObjectSortField}
                direction={mapObjectSortDirection}
                onSort={handleMapObjectSort}
              />
              <SortableColumnHeader
                label={columnLabels.id}
                field="id"
                currentField={mapObjectSortField}
                direction={mapObjectSortDirection}
                onSort={handleMapObjectSort}
              />
            </>
          )}

          <div className="ml-auto flex items-center gap-1">
            <SearchBox
              value={searchQuery}
              onChange={handleSearchChange}
              placeholder={label('search_placeholder_entity', modelType === 'units' ? 'units' : modelType === 'artifacts' ? 'artefacts' : 'map objects')}
              collapsible={true}
              onClear={handleClearSearch}
            />
          </div>
        </div>

        <div className="flex-1 overflow-auto p-2">
          {currentQuery.isError ? (
            <div className="p-5 text-center text-destructive">
              <p>{label('load_failed', label('entity_models'))}</p>
              <p className="text-xs text-muted-foreground">
                {label(currentQuery.error?.message || 'error_unknown')}
              </p>
            </div>
          ) : currentQuery.isLoading ? (
            <div className="p-5 text-center text-muted-foreground">
              {label('common_loading', ' ' + label('entity_models'))}
            </div>
          ) : (
            <ModelList
              key={modelType}
              models={sortedModels}
              selectedModelId={selectedModel?.id || null}
              onSelectModel={handleSelectModel}
            />
          )}
        </div>

        {currentQuery.data && (
          <div className="px-3 py-2 border-t border-border text-xs text-muted-foreground">
            <span>{label('total_count', currentQuery.data.length, modelType === 'units' ? label('nav_units') : modelType === 'artifacts' ? label('nav_artifacts') : label('nav_map_objects'))}</span>
          </div>
        )}
      </aside>

      <main className="h-full overflow-hidden bg-background flex flex-col ml-105">
        {droppedFile ? (
          <div className="px-4 py-3 border-b border-border bg-card flex items-center justify-between gap-4">
            <div className="min-w-0">
              <h2 className="m-0 text-lg text-foreground flex items-center gap-2 truncate">
                <span className="text-primary">{label('viewer_dropped')}</span>
                {droppedFile.file.name}
              </h2>
              <span className="text-xs text-muted-foreground">
                {label('viewer_size', (droppedFile.file.size / 1024 / 1024).toFixed(2))}
              </span>
            </div>
            <div className="flex items-center gap-3 shrink-0">
              {renderHeaderIconButtons()}
              <div className="group relative flex items-center">
                <svg className="w-5 h-5 text-amber-500/70 cursor-help" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <div className="invisible group-hover:visible absolute right-0 top-7 w-72 p-3 bg-card border border-border rounded-lg shadow-xl z-[103] text-xs text-muted-foreground leading-relaxed">
                  {label('viewer_vfx_disclaimer')}
                </div>
              </div>
              <button
                onClick={() => {
                  clearCustomTexture();
                  clearDroppedFile();
                }}
                className="px-3 py-1.5 text-sm bg-muted hover:bg-muted/80 text-muted-foreground rounded-md border-0 cursor-pointer transition-colors"
              >
                {label('viewer_close')}
              </button>
            </div>
          </div>
        ) : selectedModel ? (
          <div className="px-4 py-3 border-b border-border bg-card flex items-start justify-between gap-4">
            <div className="min-w-0">
              <h2 className="m-0 text-lg text-foreground truncate">
                {selectedModel.name}
              </h2>
              <span className="text-xs text-muted-foreground">
                {getModelSubtitle(selectedModel, label)}
              </span>
            </div>
            <div className="flex items-center gap-3 shrink-0">
              {renderHeaderIconButtons()}
              <div className="group relative flex items-center">
                <svg className="w-5 h-5 text-amber-500/70 cursor-help" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <div className="invisible group-hover:visible absolute right-0 top-7 w-72 p-3 bg-card border border-border rounded-lg shadow-xl z-[103] text-xs text-muted-foreground leading-relaxed">
                  {label('viewer_vfx_disclaimer')}
                </div>
              </div>
            </div>
          </div>
        ) : null}

        <div className="flex-1 flex p-4 overflow-hidden">
          <DropZoneOverlay onFileDropped={handleFileDropped}>
            <div
              ref={viewerContainerRef}
              className="relative w-full h-full min-h-0 flex justify-center gap-3"
            >
              <div
                ref={primaryViewerFrameRef}
                className={`relative rounded-lg overflow-hidden bg-card shadow-2xl h-full ${
                  !glbUrl
                    ? 'w-full'
                    : compareMode || isPortrait
                      ? 'aspect-[1250/1760]'
                      : 'flex-1'
                }`}
              >
                {!glbUrl ? (
                  <div className="w-full h-full flex items-center justify-center text-muted-foreground flex-col gap-3">
                    <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                      <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
                      <polyline points="3.27 6.96 12 12.01 20.73 6.96" />
                      <line x1="12" y1="22.08" x2="12" y2="12" />
                    </svg>
                    <p>{label('viewer_select')}</p>
                    <p className="text-sm opacity-60">{label('dropzone_hint')}</p>
                  </div>
                ) : (
                  <>
                  {compareMode && (
                    <div
                      className="absolute top-0 left-0 z-10 pointer-events-none"
                      style={{
                        width: '80px',
                        height: '80px',
                        clipPath: 'polygon(0 0, 100% 0, 0 100%)',
                        background:
                          'linear-gradient(135deg, hsl(var(--muted-foreground) / 0.55) 0%, hsl(var(--border)) 60%, hsl(var(--card)) 100%)',
                        filter: 'drop-shadow(2px 2px 3px rgba(0, 0, 0, 0.55))',
                      }}
                    >
                      <span
                        style={{
                          position: 'absolute',
                          top: '20px',
                          left: '-20px',
                          width: '95px',
                          textAlign: 'center',
                          fontSize: '10px',
                          fontWeight: 700,
                          letterSpacing: '0.12em',
                          textTransform: 'uppercase',
                          color: 'hsl(var(--foreground))',
                          transform: 'rotate(-45deg)',
                          whiteSpace: 'nowrap',
                        }}
                      >
                        Original
                      </span>
                    </div>
                  )}
                  {!compareMode && hasCustomTextures && applyCustomToPrimary && (
                    <div
                      className="absolute top-0 left-0 z-10 pointer-events-none"
                      style={{
                        width: '80px',
                        height: '80px',
                        clipPath: 'polygon(0 0, 100% 0, 0 100%)',
                        background:
                          'linear-gradient(135deg, hsl(var(--primary)) 0%, hsl(var(--primary) / 0.55) 55%, hsl(var(--card)) 100%)',
                        filter: 'drop-shadow(2px 2px 3px rgba(0, 0, 0, 0.55))',
                      }}
                    >
                      <span
                        style={{
                          position: 'absolute',
                          top: '20px',
                          left: '-20px',
                          width: '95px',
                          textAlign: 'center',
                          fontSize: '10px',
                          fontWeight: 700,
                          letterSpacing: '0.12em',
                          textTransform: 'uppercase',
                          color: 'hsl(var(--primary-foreground))',
                          transform: 'rotate(-45deg)',
                          whiteSpace: 'nowrap',
                        }}
                      >
                        Custom
                      </span>
                    </div>
                  )}
                  <ErrorBoundary>
                    <ModelViewer
                      ref={modelViewerRef}
                      glbUrl={glbUrl}
                      // eslint-disable-next-line react-hooks/refs -- Ref access for Three.js stats container
                      statsContainer={primaryViewerFrameRef.current}
                      unitScale={selectedModel ? getScale(selectedModel) : null}
                      faction={selectedModel && isUnitListItem(selectedModel) ? selectedModel.faction : null}
                      customTextureUrls={compareMode || !applyCustomToPrimary ? undefined : customTextureUrls}
                      onTextureSlotsReady={setTextureSlots}
                      syncStore={true}
                      cameraSyncId="primary"
                      cameraSyncState={compareMode ? cameraSyncState : null}
                      onCameraSyncStateChange={compareMode ? handleCameraSyncStateChange : undefined}
                    />
                  </ErrorBoundary>
                  {renderPrimaryCanvasButtons()}
                  </>
                )}
              </div>

              {(compareMode || compareEverActive) && glbUrl && (
                  <div className={`relative rounded-lg overflow-hidden bg-card shadow-2xl h-full aspect-[1250/1760] ${!compareMode ? 'hidden' : ''}`}>
                    <div
                      className="absolute top-0 right-0 z-10 pointer-events-none"
                      style={{
                        width: '80px',
                        height: '80px',
                        clipPath: 'polygon(0 0, 100% 0, 100% 100%)',
                        background: hasCustomTextures
                          ? 'linear-gradient(225deg, hsl(var(--primary)) 0%, hsl(var(--primary) / 0.55) 55%, hsl(var(--card)) 100%)'
                          : 'linear-gradient(225deg, hsl(var(--muted-foreground) / 0.55) 0%, hsl(var(--border)) 60%, hsl(var(--card)) 100%)',
                        filter: 'drop-shadow(-2px 2px 3px rgba(0, 0, 0, 0.55))',
                      }}
                    >
                      <span
                        style={{
                          position: 'absolute',
                          top: '20px',
                          right: '-20px',
                          width: '95px',
                          textAlign: 'center',
                          fontSize: '10px',
                          fontWeight: 700,
                          letterSpacing: '0.12em',
                          textTransform: 'uppercase',
                          color: hasCustomTextures
                            ? 'hsl(var(--primary-foreground))'
                            : 'hsl(var(--foreground))',
                          transform: 'rotate(45deg)',
                          whiteSpace: 'nowrap',
                        }}
                      >
                        Custom
                      </span>
                    </div>
                    <ErrorBoundary>
                      <ModelViewer
                        ref={comparisonViewerRef}
                        glbUrl={glbUrl}
                        unitScale={selectedModel ? getScale(selectedModel) : null}
                        faction={selectedModel && isUnitListItem(selectedModel) ? selectedModel.faction : null}
                        customTextureUrls={customTextureUrls}
                        syncStore={false}
                        cameraSyncId="secondary"
                        cameraSyncState={cameraSyncState}
                        onCameraSyncStateChange={handleCameraSyncStateChange}
                      />
                    </ErrorBoundary>
                    {renderSecondaryCanvasButtons()}
                  </div>
              )}
            </div>
          </DropZoneOverlay>
        </div>
      </main>
    </div>
  );
}

interface ModelListProps {
  models: ViewerItem[];
  selectedModelId: string | null;
  onSelectModel: (model: ViewerItem) => void;
}

function getDisplayId(model: ViewerItem): string {
  return model.id;
}

function getModelSubtitle(model: ViewerItem, label: (key: string, ...args: (string | number)[]) => string): string {
  const parts: string[] = [];

  if (isUnitListItem(model)) {
    parts.push(model.tier ? label('viewer_tier', model.tier) : label('viewer_tier_na'));
    if (model.factionDisplay) {
      parts.push(model.factionDisplay);
    }
  }
  else if (isArtifactListItem(model) && model.raritySlotText) {
    parts.push(model.raritySlotText);
  }
  else if (isMapObjectListItem(model) && model.category) {
    parts.push(model.category);
  }

  if (!model.isOrphan) {
    parts.push(getDisplayId(model));
  }

  return parts.join(' · ');
}

function ModelList({ models, selectedModelId, onSelectModel }: ModelListProps) {
  const { label } = useLabels();

  if (models.length === 0) {
    return (
      <div className="p-5 text-center text-muted-foreground">
        {label('no_results', label('entity_models'))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1">
      {models.map((model) => (
        <button
          key={model.id}
          onClick={() => onSelectModel(model)}
          className={`flex items-center gap-3 px-3 py-2.5 border-0 rounded-md cursor-pointer text-left w-full transition-colors ${
            selectedModelId === model.id
              ? 'bg-primary text-primary-foreground'
              : 'bg-transparent text-foreground hover:bg-accent'
          } ${model.isOrphan ? 'opacity-70' : ''}`}
        >
          <ProgressiveIcon iconPath={getIcon(model)} alt={model.name} size={40} />

          <div className="flex-1 min-w-0">
            <div className={`overflow-hidden text-ellipsis whitespace-nowrap flex items-center gap-1.5 ${
              selectedModelId === model.id ? 'font-medium' : 'font-normal'
            } ${getModelNameColorClass(model, selectedModelId === model.id)}`}>
              {model.name}
              {model.isOrphan && (
                <span className="text-[10px] px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-400 font-medium uppercase tracking-wide">
                  {label('viewer_orphan')}
                </span>
              )}
            </div>
            <div className={`text-xs mt-0.5 ${
              selectedModelId === model.id ? 'text-primary-foreground/70' : 'text-muted-foreground'
            }`}>
              {getModelSubtitle(model, label)}
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}
