import { useState, useMemo, useCallback, useRef, useEffect } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { useUnitModels, useMapObjectModels, useArtifactModels } from './useModels';
import { modelsApi } from '@/api/client';
import type { UnitListItemDto, MapObjectListItemDto, ArtifactListItemDto } from '@/api/types';
import ModelViewer from './ModelViewer';
import type { ModelViewerHandle, CameraPreset } from './ModelViewer';
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
  const viewerContainerRef = useRef<HTMLDivElement>(null);

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

  const [searchQuery, setSearchQuery] = useState('');
  const columnLabels = useColumnLabels();

  const [unitSortField, setUnitSortField] = useState<UnitSortField>('faction');
  const [unitSortDirection, setUnitSortDirection] = useState<SortDirection>('asc');
  const [artifactSortField, setArtifactSortField] = useState<ArtifactSortField>('name');
  const [artifactSortDirection, setArtifactSortDirection] = useState<SortDirection>('asc');
  const [mapObjectSortField, setMapObjectSortField] = useState<MapObjectSortField>('name');
  const [mapObjectSortDirection, setMapObjectSortDirection] = useState<SortDirection>('asc');

  const { droppedFile, handleFileDropped: onFileDropped, clearDroppedFile } = useDroppedFile();

  const handleFileDropped = useCallback((file: File, blobUrl: string) => {
    onFileDropped(file, blobUrl);
    navigate(`/viewer/${modelType}`);
  }, [onFileDropped, navigate, modelType]);

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
    navigate(`/viewer/${type}`);
  }, [navigate]);

  const handleSelectModel = useCallback((model: ViewerItem) => {
    clearDroppedFile();

    if (isUnitListItem(model)) {
      navigate(`/viewer/units/${model.id}`);
    } else if (isArtifactListItem(model)) {
      navigate(`/viewer/artifacts/${model.id}`);
    } else if (isMapObjectListItem(model)) {
      navigate(`/viewer/map-objects/${model.prefabPath || model.id}`);
    }
  }, [navigate, clearDroppedFile]);

  const handleSearchChange = useCallback((query: string) => {
    setSearchQuery(query);
  }, []);

  const handleClearSearch = useCallback(() => {
    setSearchQuery('');
  }, []);

  const handleResetCamera = useCallback(() => {
    modelViewerRef.current?.resetCamera();
  }, []);

  const handleCameraPreset = useCallback((preset: string) => {
    modelViewerRef.current?.setCameraPreset(preset as CameraPreset);
  }, []);

  const handleScreenshot = useCallback(() => {
    const dataUrl = modelViewerRef.current?.takeScreenshot();
    if (dataUrl) {
      const link = document.createElement('a');
      link.href = dataUrl;

      const modelName = selectedModel?.name || 'model';
      const sanitizedName = modelName.replace(/[^a-z0-9]/gi, '_').toLowerCase();
      const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
      link.download = `${sanitizedName}_${timestamp}.png`;

      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
    }
  }, [selectedModel]);

  const isOrphan = selectedModel?.isOrphan || false;
  const isArtifactOrMapObject = modelType === 'artifacts' || modelType === 'map-objects';

  useViewerGui({
    containerRef: viewerContainerRef,
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
          <div className="px-4 py-3 border-b border-border bg-card flex items-center justify-between">
            <div>
              <h2 className="m-0 text-lg text-foreground flex items-center gap-2">
                <span className="text-primary">{label('viewer_dropped')}</span>
                {droppedFile.file.name}
              </h2>
              <span className="text-xs text-muted-foreground">
                {label('viewer_size', (droppedFile.file.size / 1024 / 1024).toFixed(2))}
              </span>
            </div>
            <div className="flex items-center gap-3">
              <div className="group relative flex items-center">
                <svg className="w-5 h-5 text-amber-500/70 cursor-help" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <div className="invisible group-hover:visible absolute right-0 top-7 w-72 p-3 bg-card border border-border rounded-lg shadow-xl z-[103] text-xs text-muted-foreground leading-relaxed">
                  {label('viewer_vfx_disclaimer')}
                </div>
              </div>
              <button
                onClick={clearDroppedFile}
                className="px-3 py-1.5 text-sm bg-muted hover:bg-muted/80 text-muted-foreground rounded-md border-0 cursor-pointer transition-colors"
              >
                {label('viewer_close')}
              </button>
            </div>
          </div>
        ) : selectedModel ? (
          <div className="px-4 py-3 border-b border-border bg-card flex items-start justify-between">
            <div>
              <h2 className="m-0 text-lg text-foreground">
                {selectedModel.name}
              </h2>
              <span className="text-xs text-muted-foreground">
                {getModelSubtitle(selectedModel, label)}
              </span>
            </div>
            <div className="group relative flex items-center">
              <svg className="w-5 h-5 text-amber-500/70 cursor-help" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <div className="invisible group-hover:visible absolute right-0 top-7 w-72 p-3 bg-card border border-border rounded-lg shadow-xl z-[103] text-xs text-muted-foreground leading-relaxed">
                {label('viewer_vfx_disclaimer')}
              </div>
            </div>
          </div>
        ) : null}

        <div className="flex-1 flex p-4 overflow-hidden">
          <DropZoneOverlay onFileDropped={handleFileDropped}>
            <div ref={viewerContainerRef} className="flex-1 relative rounded-lg overflow-hidden bg-card shadow-2xl h-full">
              <ErrorBoundary>
                {glbUrl ? (
                  /* eslint-disable-next-line react-hooks/refs -- Ref access for Three.js stats container */
                  <ModelViewer ref={modelViewerRef} glbUrl={glbUrl} statsContainer={viewerContainerRef.current} unitScale={selectedModel ? getScale(selectedModel) : null} faction={isUnitListItem(selectedModel) ? selectedModel.faction : null} />
                ) : (
                  <div className="w-full h-full flex items-center justify-center text-muted-foreground flex-col gap-3">
                    <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                      <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
                      <polyline points="3.27 6.96 12 12.01 20.73 6.96" />
                      <line x1="12" y1="22.08" x2="12" y2="12" />
                    </svg>
                    <p>{label('viewer_select')}</p>
                    <p className="text-sm opacity-60">{label('dropzone_hint')}</p>
                  </div>
                )}
              </ErrorBoundary>
              {glbUrl && (
                <button
                  onClick={handleScreenshot}
                  className="absolute bottom-4 left-4 p-0 m-0 bg-transparent text-muted-foreground border-0 cursor-pointer z-10"
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
