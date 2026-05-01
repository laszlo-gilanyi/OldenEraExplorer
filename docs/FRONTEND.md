# Frontend Architecture

The frontend is a React 19 + Vite + TypeScript single-page application with a feature-based architecture.

## Tech Stack

- **React 19.2.3** - UI framework
- **TypeScript ~5.9.3** - Type safety
- **Vite 7.3.0** - Build tool and dev server
- **React Router DOM 7.12.0** - Client-side routing
- **TanStack React Query 5.90.16** - Server state management
- **Zustand** - Client state management
- **SignalR** - Real-time WebSocket communication
- **Three.js 0.176.0** - 3D rendering
- **Tailwind CSS 4.1.18** - Styling

## Project Structure

```
frontend/src/
├── main.tsx                     # Entry point
├── App.tsx                      # Root component
├── router.tsx                   # Route configuration
│
├── api/                         # API client (2 files)
│   ├── client.ts                # Axios client + all endpoints
│   └── types.ts                 # TypeScript DTOs
│
├── components/                  # Shared components (20 files)
│   ├── layout/                  # Layout components (2)
│   │   ├── Navigation.tsx
│   │   └── index.ts
│   ├── display/                 # Display components (10)
│   │   ├── RichText.tsx
│   │   ├── FactionBadge.tsx
│   │   ├── ClassBadge.tsx
│   │   ├── CurrencyBadge.tsx
│   │   ├── HexagonFrame.tsx
│   │   ├── ProgressiveIcon.tsx
│   │   ├── SortableColumnHeader.tsx
│   │   ├── UsedBySection.tsx
│   │   ├── DetailContainer.tsx
│   │   └── index.ts
│   ├── feedback/                # Feedback components (5)
│   │   ├── LoadingScreen.tsx
│   │   ├── ErrorBoundary.tsx
│   │   ├── ErrorPage.tsx
│   │   ├── DropZoneOverlay.tsx + useDroppedFile
│   │   └── index.ts
│   └── ui/                      # Base UI (3)
│       ├── input.tsx
│       ├── FolderPickerModal.tsx
│       └── index.ts
│
├── features/                    # Feature modules (14 modules, 57 files)
│   ├── abilities/               # Standard pattern (4 files each)
│   │   ├── AbilitiesPage.tsx
│   │   ├── useAbilities.ts
│   │   ├── abilitiesStore.ts
│   │   └── index.ts
│   ├── artifacts/
│   ├── buildings/
│   ├── faction-laws/
│   ├── heroes/
│   ├── map-objects/
│   ├── skills/
│   ├── spells/
│   ├── subclasses/
│   ├── units/
│   ├── extraction/              # Special: extraction panel
│   │   ├── ExtractionPanel.tsx
│   │   ├── useExtractionProgress.ts (SignalR)
│   │   ├── extractionStore.ts
│   │   └── index.ts
│   ├── search/                  # Special: search components
│   │   ├── GlobalSearch.tsx
│   │   ├── SearchBox.tsx
│   │   └── index.ts
│   ├── settings/                # Special: settings
│   │   ├── SettingsPanel.tsx
│   │   ├── useSettings.ts
│   │   └── index.ts
│   └── viewer/                  # Special: 3D viewer (7 files)
│       ├── ViewerPage.tsx
│       ├── ModelViewer.tsx (Three.js)
│       ├── useModels.ts
│       ├── useViewerGui.ts
│       ├── useSceneHierarchyGui.ts
│       ├── viewerStore.ts (150+ lines, 40+ properties)
│       └── index.ts
│
├── hooks/                       # Shared hooks (5 files)
│   ├── useGameStatus.ts
│   ├── useLabels.ts
│   ├── useHighlightText.ts
│   ├── useOnClickOutside.ts
│   └── index.ts
│
├── stores/                      # Global Zustand stores
│   ├── gameStore.ts             # Minimal (isGameReady only)
│   ├── imageStore.ts
│   └── index.ts
│
├── lib/                         # Utilities (5 files)
│   ├── queryClient.ts           # React Query config
│   ├── utils.ts                 # Utility functions
│   ├── colorPicker.ts
│   ├── localeMapping.ts
│   └── index.ts
│
└── styles/
    └── globals.css              # Global styles + Tailwind
```

The frontend mixes global stores under `src/stores/` with feature-local stores inside each feature folder.

## Core Concepts

### 1. Feature-Based Organization

Each entity type (units, heroes, spells, etc.) follows the **same pattern**:

```
features/[entity]/
├── [Entity]Page.tsx        # Main page component
├── use[Entity].ts          # React Query hooks (list + detail)
├── [entity]Store.ts        # Zustand store (UI state)
└── index.ts                # Barrel export
```

**Standard Features (10):**
1. abilities
2. artifacts
3. buildings
4. faction-laws
5. heroes
6. map-objects
7. skills
8. spells
9. subclasses
10. units

**Special Features (4):**
11. extraction - Asset extraction panel
12. search - Global and entity-specific search
13. settings - Settings panel
14. viewer - 3D model viewer (most complex)

### 2. State Management

#### Server State (React Query)

All backend data uses TanStack Query:

```typescript
// features/units/useUnits.ts
export function useUnits(search?: string) {
  return useQuery<UnitListItemDto[]>({
    queryKey: ['units', search],
    queryFn: () => unitsApi.list(search),
    retry: false,
  });
}

export function useUnit(id: string | null) {
  return useQuery<UnitDetailDto>({
    queryKey: ['unit', id],
    queryFn: () => unitsApi.getById(id!),
    enabled: !!id,
    retry: false,
  });
}
```

**Features:**
- Automatic caching
- Background refetching
- Loading/error states
- Request deduplication

#### Client State (Zustand)

UI state (selected items, search, sort) uses Zustand:

```typescript
// features/units/unitsStore.ts
export const useUnitsStore = create<UnitsState>((set) => ({
  selectedUnitId: null,
  searchQuery: '',
  sortField: 'faction',
  sortDirection: 'asc',

  setSelectedUnitId: (id) => set({ selectedUnitId: id }),
  setSearchQuery: (query) => set({ searchQuery: query }),
  setSort: (field, direction) => set({ sortField: field, sortDirection: direction }),
}));
```

**Persistence:**
All feature stores persist to `sessionStorage` automatically.

**Global Store:**

Only `gameStore` is truly global:

```typescript
// stores/gameStore.ts
interface GameState {
  isGameReady: boolean;
  setGameReady: (ready: boolean) => void;
}
```

Used by the API client to block requests until game data loads.

### 3. API Client

#### Organization

All API endpoints in one file: `api/client.ts`

```typescript
// Axios instance
const client = axios.create({
  baseURL: import.meta.env.DEV ? 'http://localhost:5176/api' : '/api',
});

// Request interceptor (blocks calls if game not ready)
client.interceptors.request.use((config) => {
  const { isGameReady } = useGameStore.getState();
  const allowedPaths = ['/game/', '/settings', '/labels', '/extraction/', '/assets/', '/filesystem/'];

  if (!isGameReady && !allowedPaths.some(p => config.url?.startsWith(p))) {
    return Promise.reject(new Error('Game not ready'));
  }

  return config;
});

// API endpoint groups
export const gameApi = { ... };
export const unitsApi = { ... };
export const abilitiesApi = { ... };
// ... 17 total API groups
```

**Endpoint Groups:**
- gameApi - Game detection, path, loading
- unitsApi - Units list and detail
- abilitiesApi - Abilities
- buildingsApi - Buildings
- spellsApi - Spells
- skillsApi - Skills
- factionLawsApi - Faction laws
- heroesApi - Heroes
- mapObjectsApi - Map objects
- subclassesApi - Subclasses
- artifactsApi - Artifacts
- modelsApi - 3D models (GLB URLs)
- extractionApi - Asset extraction jobs
- searchApi - Global search
- settingsApi - User settings
- labelsApi - UI labels (translations)
- filesystemApi - Folder picker

### 4. Routing

**11 main pages** with lazy loading:

```typescript
// router.tsx
const router = createBrowserRouter([
  {
    path: '/',
    element: <App />,
    errorElement: <ErrorPage />,
    children: [
      { index: true, element: <Navigate to="/units" /> },
      { path: 'units', element: <UnitsPage /> },
      { path: 'units/:unitId', element: <UnitsPage /> },
      { path: 'buildings', element: <BuildingsPage /> },
      { path: 'buildings/:buildingId', element: <BuildingsPage /> },
      { path: 'heroes', element: <HeroesPage /> },
      { path: 'heroes/:heroId', element: <HeroesPage /> },
      { path: 'spells', element: <SpellsPage /> },
      { path: 'spells/:spellId', element: <SpellsPage /> },
      { path: 'skills', element: <SkillsPage /> },
      { path: 'skills/:skillId', element: <SkillsPage /> },
      { path: 'subclasses', element: <SubclassesPage /> },
      { path: 'subclasses/:subclassId', element: <SubclassesPage /> },
      { path: 'faction-laws', element: <FactionLawsPage /> },
      { path: 'faction-laws/:factionLawId', element: <FactionLawsPage /> },
      { path: 'map-objects', element: <MapObjectsPage /> },
      { path: 'map-objects/*', element: <MapObjectsPage /> },
      { path: 'artifacts', element: <ArtifactsPage /> },
      { path: 'artifacts/:artifactId', element: <ArtifactsPage /> },
      { path: 'abilities', element: <AbilitiesPage /> },
      { path: 'abilities/:abilityId', element: <AbilitiesPage /> },
      { path: 'viewer', element: <ViewerPage /> },
      { path: 'viewer/units', element: <ViewerPage /> },
      { path: 'viewer/units/:modelId', element: <ViewerPage /> },
      { path: 'viewer/map-objects', element: <ViewerPage /> },
      { path: 'viewer/map-objects/*', element: <ViewerPage /> },
      { path: 'viewer/artifacts', element: <ViewerPage /> },
      { path: 'viewer/artifacts/*', element: <ViewerPage /> },
    ],
  },
]);
```

**All pages use lazy loading:**
```typescript
const UnitsPage = lazy(() => import('./features/units'));
```

### 5. Data Flow

#### App Lifecycle

```
App.tsx (root)
├─> useGameStatus() → checks if game ready
├─> useSettings() → user preferences
├─> Auto-detect game (if not set)
├─> Auto-load game data (if path exists)
├─> Auto-extract assets (if enabled)
└─> Render: LoadingScreen | Navigation + Outlet
```

#### Page Pattern

```
UnitsPage
├─> useParams() → get :unitId from URL
├─> useUnitsStore() → get searchQuery, sortField
├─> useUnits(searchQuery) → list query
├─> useUnit(unitId) → detail query (if selected)
├─> Render: List + Detail panels
└─> On interaction: update store → triggers re-render
```

#### Store Pattern

```typescript
useUnitsStore = create((set) => ({
  selectedUnitId: string | null,
  searchQuery: string,
  sortField: 'faction' | 'tier' | ...,
  sortDirection: 'asc' | 'desc',

  // Setters (persist to sessionStorage)
}))
```

### 6. Component Organization

#### Layout Components (`components/layout/`)
- **Navigation** - Main nav tabs (11 links)

#### Display Components (`components/display/`)
Reusable display pieces:
- **RichText** - Renders HTML/rich text (handles `<resolved>`, `<unresolved>` tags)
- **FactionBadge** - Visual faction indicator
- **ClassBadge** - Character class badge
- **CurrencyBadge** - Currency/resource display
- **HexagonFrame** - Hexagon-shaped container
- **ProgressiveIcon** - Lazy-loading image with fallback
- **SortableColumnHeader** - Table header with sort arrows
- **UsedBySection** - Shows cross-references
- **DetailContainer** - Detail panel wrapper

#### Feedback Components (`components/feedback/`)
- **LoadingScreen** - Full-screen loading
- **ErrorBoundary** - React error boundary
- **ErrorPage** - Error route
- **DropZoneOverlay** + `useDroppedFile` - Drag-drop file input

#### UI Components (`components/ui/`)
- **Input** - Styled text input
- **FolderPickerModal** - File browser modal

### 7. Rich Text Rendering

Game text contains special markup:

```html
<resolved>15</resolved> damage
<unresolved>{funcName}</unresolved>
```

The `RichText` component:
- Parses HTML with `<resolved>` and `<unresolved>` tags
- Colors resolved values **orange**
- Colors unresolved values **red**

### 8. Real-Time Updates (SignalR)

Asset extraction progress uses SignalR:

```typescript
// features/extraction/useExtractionProgress.ts
export function useExtractionProgress() {
  const [progress, setProgress] = useState(0);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/ws/extraction')
      .build();

    connection.on('ProgressUpdate', (data) => {
      setProgress(data.percentage);
    });

    connection.start();
    return () => connection.stop();
  }, []);

  return { progress };
}
```

### 9. 3D Model Viewer

The viewer uses Three.js for 3D rendering.

**Important Limitation:** The 3D models do not include particle systems, visual effects (VFX), or complex shader effects due to GLB format limitations. Models exported from Unity asset bundles only contain mesh, texture, material, and animation data. Visual effects like glowing auras, particle trails, fire/smoke effects, or magical glows are not supported. This is noted in the UI and documentation as a known limitation. See `docs/ASSET_EXTRACTION.md` for technical details.

**Key Components:**
- `ViewerPage.tsx` - Main layout with UI controls
- `ModelViewer.tsx` - Three.js canvas wrapper
- `useModels.ts` - Model data fetching
- `useViewerGui.ts` - GUI controls (lil-gui)
- `useSceneHierarchyGui.ts` - Scene inspector
- `viewerStore.ts` - Massive store (319 lines, 40+ state properties)

**Store State (viewerStore):**
```typescript
{
  // Camera (7 properties)
  cameraType, activeCamera, nearClip, farClip, autoRotate, autoRotateSpeed

  // Display Mode (2)
  displayMode: 'game-preview' | 'studio', showPlatform

  // Environment (3)
  showBackground, backgroundColor, showGrid

  // Lighting (5)
  usePunctualLights, ambientIntensity, ambientColor, directionalIntensity, directionalColor

  // Renderer (3)
  toneMapping, exposure, pixelRatioLimit

  // Animation (4)
  animations[], playbackSpeed, loopMode

  // Materials (4)
  materials[], selectedMaterialUuid, globalWireframe

  // Debug (3)
  showStats, showSkeleton, pointSize

  // Scene Inspector (7)
  sceneGraph, platformSceneGraph, selectedNodeUuid, boundingBoxNodes[], wireframeNodes[], hiddenNodes[], soloNode

  // Persisted to sessionStorage (selective fields)
}
```

**Libraries:**
- `three` - Core 3D engine (vanilla)
- `lil-gui` - GUI controls for debugging
- `stats.js` - Performance monitoring

### 10. Search

Global search across all entity types:

```typescript
// features/search/GlobalSearch.tsx
export function GlobalSearch() {
  const [query, setQuery] = useState('');
  const { data: results } = useQuery({
    queryKey: ['search', query],
    queryFn: () => searchApi.query(query),
    enabled: query.length > 2,
  });

  return (
    <div>
      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search all entities..."
      />
      <SearchResults results={results} />
    </div>
  );
}
```

### 11. Settings

User preferences stored on backend in `settings.json`:

```typescript
// features/settings/useSettings.ts
export function useSettings() {
  const { data: settings } = useQuery({
    queryKey: ['settings'],
    queryFn: () => settingsApi.get(),
  });

  return settings;
}

export function useUpdateSettings() {
  return useMutation({
    mutationFn: (settings) => settingsApi.update(settings),
    onSuccess: () => {
      queryClient.invalidateQueries(['settings']);
    },
  });
}
```

**Settings (SettingsPanel.tsx):**
- Theme (light, dark, system)
- Locale (language)
- Placeholder resolution enabled/disabled
- Auto-extraction on startup enabled/disabled
- Game path (with change/browse UI)

**Extraction Settings (ExtractionPanel.tsx):**
- Extract PNG icons enabled/disabled (persisted)
- Extract GLB models enabled/disabled (persisted)
- Force re-extract (one-time, local state, resets after extraction)

## Styling

### Tailwind CSS

Utility-first CSS framework:

```tsx
<div className="flex items-center gap-4 p-6 bg-gray-800 rounded-lg">
  <img className="w-16 h-16 rounded" src={icon} />
  <h2 className="text-xl font-bold text-white">{name}</h2>
</div>
```

### Dark Mode

Theme switching via CSS class on `<html>`:

```typescript
useEffect(() => {
  if (theme === 'dark') {
    document.documentElement.classList.add('dark');
  } else {
    document.documentElement.classList.remove('dark');
  }
}, [theme]);
```

## Performance

### Code Splitting

React.lazy for route-based splitting:

```typescript
const UnitsPage = lazy(() => import('./features/units/UnitsPage'));
```

### Image Optimization

Progressive image loading:

```tsx
// components/ProgressiveIcon.tsx
export function ProgressiveIcon({ src }: { src: string }) {
  const [loaded, setLoaded] = useState(false);

  return (
    <div className={loaded ? 'opacity-100' : 'opacity-0 transition-opacity'}>
      <img
        src={src}
        onLoad={() => setLoaded(true)}
        loading="lazy"
      />
    </div>
  );
}
```

### Memoization

Prevent unnecessary re-renders:

```tsx
const UnitCard = memo(function UnitCard({ unit }: { unit: Unit }) {
  // Component implementation
}, (prev, next) => prev.unit.id === next.unit.id);
```

## Error Handling

### Error Boundaries

Catch rendering errors:

```tsx
// components/feedback/ErrorBoundary.tsx
export class ErrorBoundary extends Component {
  componentDidCatch(error: Error) {
    console.error('UI Error:', error);
  }

  render() {
    if (this.state.hasError) {
      return <ErrorPage />;
    }
    return this.props.children;
  }
}
```

### API Error Handling

React Query handles errors automatically:

```tsx
const { data, error, isLoading } = useUnits();

if (isLoading) return <LoadingScreen />;
if (error) return <ErrorMessage error={error} />;
```

## Development

### Dev Server

```bash
cd frontend
npm run dev
```

Runs at `http://localhost:5173` (Vite default) with:
- Hot module replacement
- Fast refresh
- TypeScript checking
- Proxy to backend at `http://localhost:5176`

### Build

```bash
npm run build
```

Produces optimized static files in `dist/`:
- Minified JavaScript
- Optimized CSS
- Asset hashing
- Source maps
- Type checking via `tsc -b`

### Linting

```bash
npm run lint
```

## Deployment

### Embedded in Backend

Production builds are embedded into the .NET API:

1. `npm run build` creates `dist/` folder
2. .NET publish copies `dist/` to embedded resources
3. Backend serves files from embedded resources
4. Single executable contains both backend and frontend

## Browser Support

- Chrome 90+
- Firefox 88+
- Safari 14+
- Edge 90+

## Troubleshooting

### Issue: "Cannot connect to backend"
**Solution:** Ensure backend is running at `http://localhost:5176`. Check CORS settings.

### Issue: "Images not loading"
**Solution:** Check asset extraction completed. Verify `/api/assets/` endpoint accessible.

### Issue: "3D models not rendering"
**Solution:** Check browser WebGL support. Update graphics drivers. Try different browser.

### Issue: "White screen on production build"
**Solution:** Check browser console. Verify embedded resources bundled correctly. Ensure paths are relative.
