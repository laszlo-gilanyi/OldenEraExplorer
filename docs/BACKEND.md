# Backend Architecture

The backend is a .NET 10 multi-project solution that handles game data loading, asset extraction, and serving processed information to the frontend.

## Project Structure

```
backend/
├── src/
│   ├── Domain/                  # Pure domain entities (10 entity types)
│   ├── Localization/            # Text resolution and scripting system
│   ├── GameData/                # Game data loading and indexing
│   ├── AssetExtractor/          # Asset extraction pipeline; the Unity reader lives under UnityReader/
│   ├── AssetExtractor.CLI/      # CLI executable for subprocess extraction
│   └── API/                     # Web API and hosting
└── Backend.slnx                 # Modern XML solution file
```

All projects target **.NET 10.0** with C# 12 and nullable reference types enabled.

## Core Components

### 1. Domain Project

**Location:** `/backend/src/Domain`

**Purpose:** Pure domain entities with zero dependencies.

**Entities (10 total):**
- `Unit.cs` - Creature/unit definitions
- `Hero.cs` - Hero characters
- `Building.cs` - Town structures
- `Spell.cs` - Magic spells
- `Skill.cs` - Hero skills
- `Ability.cs` - Unit and hero abilities
- `Artifact.cs` - Equipment items
- `MapObject.cs` - Adventure map objects
- `FactionLaw.cs` - Faction mechanics
- `Subclass.cs` - Hero specializations

**Namespace:** `Domain.Entities`

---

### 2. Localization Project

**Location:** `/backend/src/Localization`

**Purpose:** Text resolution, placeholder substitution, and game script execution.

**Structure:**

```
Localization/
├── Scripting/                   # Script interpretation system
│   ├── ScriptInterpreter.cs     # Main execution coordinator
│   ├── ScriptRegistry.cs        # Registry of all game scripts
│   ├── ScriptEnvironment.cs     # Execution context/state
│   ├── ScriptSettings.cs        # Configuration
│   └── Operations/              # 13 operation handler files
│       ├── IScriptOperation.cs
│       ├── OperationRegistry.cs
│       ├── ArithmeticOperations.cs
│       ├── UnitAccessOperations.cs
│       ├── BuffAccessOperations.cs
│       ├── ItemAccessOperations.cs
│       ├── MagicAccessOperations.cs
│       ├── SkillAccessOperations.cs
│       ├── HeroAccessOperations.cs
│       ├── DbOperations.cs
│       ├── ControlFlowOperations.cs
│       ├── MiscAccessOperations.cs
│       └── PatternBasedOperations.cs
│
├── Resolution/                  # Text resolution pipeline
│   ├── TextResolverFacade.cs    # High-level facade
│   ├── BasicResolver.cs         # Simple text lookup
│   ├── PlaceholderResolver.cs   # Placeholder substitution (14KB)
│   ├── ITextResolver.cs
│   └── ResolutionContext.cs
│
├── Indexing/                    # Localization data indexing
│   ├── LangIndex.cs             # Language file index (9.3KB)
│   ├── InfoScriptIndex.cs       # Info scripts index (2.4KB)
│   └── FunctionOverrides.cs
│
├── DbAccess/                    # Low-level data access
│   ├── DbAccessor.cs            # Core.zip JSON reader (19.4KB)
│   └── JsonPathReader.cs
│
├── Services/
│   └── OverlayService.cs        # Overlay resolution
│
└── Resources/
    └── Overlays/                # Multi-language fallback files
        ├── english.json
        └── <lang>.json          # additional languages
```

**Key Features:**

**Script System Refactoring:**
The old monolithic `ScriptEvaluator` has been refactored into:
- **ScriptInterpreter** - Main coordinator
- **Operation Handlers** - 13 specialized operation classes
- **ScriptRegistry** - Loads all `.script` files from Core.zip

**How Scripts Work:**

Game scripts calculate dynamic values (e.g., unit damage, spell costs):

```
modInt current_unit_health
{
    CurrentUnit ( return, "health" )
}
```

The interpreter:
1. Parses script syntax
2. Routes to appropriate operation handler
3. Executes with game data context
4. Returns calculated value

**Namespaces:**
- `Localization.Scripting`
- `Localization.Scripting.Operations`
- `Localization.Resolution`
- `Localization.Indexing`
- `Localization.DbAccess`
- `Localization.Services`

---

### 3. GameData Project

**Location:** `/backend/src/GameData`

**Purpose:** Game data loading, indexing, and entity detail assembly.

**Structure:**

```
GameData/
├── Indexing/                    # 15 index files
│   ├── DbIndex.cs               # Core.zip reader (27.3KB)
│   ├── EntityIndexBase.cs       # Base class for all indexes
│   ├── AbilityIndex.cs          # Ability definitions (21.4KB)
│   ├── AbilityAggregator.cs     # Aggregates ability data (7.2KB)
│   ├── BuildingsIndex.cs        # (19.8KB)
│   ├── HeroesIndex.cs           # (14.6KB)
│   ├── SkillsIndex.cs           # (7.8KB)
│   ├── SpellsIndex.cs           # (4.2KB)
│   ├── SubclassesIndex.cs       # (7.8KB)
│   ├── ArtifactsIndex.cs        # (3.7KB)
│   ├── MapObjectsIndex.cs       # (6.5KB)
│   ├── HeroSpecializationsIndex.cs  # (5.5KB)
│   ├── FactionLawIndex.cs       # (8.0KB)
│   ├── ItemSetsIndex.cs         # (3.8KB)
│   └── OrphanAbilityProcessor.cs  # (15.8KB)
│
├── Loading/
│   ├── DataCatalog.cs           # Unified access to all data
│   └── IDataCatalog.cs
│
├── Services/
│   └── IndexService.cs          # Creates and manages all indexes (5.6KB)
│
└── Details/                     # 12 entity detail builders
    ├── IEntityDetails.cs
    ├── UnitDetails.cs           # (18.9KB)
    ├── HeroDetails.cs           # (11.3KB)
    ├── AbilityDetails.cs        # (6.1KB)
    ├── SkillDetails.cs          # (9.9KB)
    ├── SpellDetails.cs          # (10.3KB)
    ├── BuildingDetails.cs       # (7.3KB)
    ├── ArtifactDetails.cs       # (9.6KB)
    ├── FactionLawDetails.cs     # (5.6KB)
    ├── MapObjectDetails.cs      # (4.6KB)
    ├── SubclassDetails.cs       # (2.9KB)
    └── UnitNavigation.cs        # (4.2KB)
```

**Key Components:**

**DbIndex** - Primary data source:
- Reads all JSON files from `Core.zip`
- Provides lookup by path (e.g., `DB/units/units_logics/humans/crossbowman_l.json`, `DB/units/units_views/humans/crossbowman_v.json`)
- Foundation for all entity indexes

**Entity Indexes:**
Each entity type has a dedicated index extending `EntityIndexBase<T>`:
- Fast lookup by ID
- Search/filter capabilities
- List all entities
- Cross-referencing
- Lang file supplementation for missing faction data (Buildings, FactionLaw, Subclasses)

**Detail Builders:**
Assemble complete entity information:
- Resolve all text via `TextResolverFacade`
- Calculate dynamic values via `ScriptInterpreter`
- Build cross-references to related entities
- Format for API consumption

**Namespaces:**
- `GameData.Indexing`
- `GameData.Loading`
- `GameData.Services`

---

### 4. AssetExtractor Project

**Location:** `/backend/src/AssetExtractor`

**Purpose:** Extract images and 3D models from Unity asset bundles, then encode to PNG / GLB. The Unity SerializedFile / AssetBundle reader lives under `UnityReader/`.

**Structure:**

```
AssetExtractor/
├── UnityReader/                          # In-house Unity asset reader (namespace UnityReader)
│   ├── Api/                              # Surface used by Extraction/ and Export/ (UnityScene, Mesh, Material, GameObject, Animator, ...)
│   ├── Internal/                         # AnimationClipProcessor, PathChecksumCache, MicrosoftLoggerAdapter
│   ├── Texture/                          # Managed BC1/BC3/BC7 + RGB family decoders
│   ├── Vendor/AssetStudio/               # Vendored AssetStudioMod core
│   └── Vendor/AssetRipperTextureDecoder/ # Vendored AssetRipper.TextureDecoder
│
├── Extraction/                  # Asset extraction services
│   ├── AssetExtractor.cs        # Main extractor
│   ├── AssetLoader.cs           # Unity scene loading + resource-path queries
│   ├── MaterialExtractor.cs     # Material processing with texture fallback
│   ├── MeshDataExtractor.cs     # Mesh extraction
│   ├── StandaloneTextureExtractor.cs  # Texture extraction
│   ├── AnimationDataExtractor.cs  # Animation data
│   ├── HierarchyExtractor.cs    # GameObject hierarchy
│   ├── BoneDataExtractor.cs     # Skeleton extraction
│   ├── ThreadSafeTextureCache.cs  # Texture caching
│   └── ShaderProperties.cs      # Shader property name constants
│
├── Export/                      # GLTF/GLB export
│   ├── GlbExporter.cs           # GLB format export
│   ├── GltfMeshExporter.cs      # Mesh to GLTF
│   ├── GltfAnimationExporter.cs # Animation export
│   ├── TextureExporter.cs       # Texture export (streams PNG to disk)
│   ├── NodeHierarchyBuilder.cs  # GLTF node structure
│   └── GlbCoordinateConversion.cs  # Coordinate conversion
│
├── Pipeline/
│   ├── ExtractionOrchestrator.cs  # Main orchestrator
│   ├── ManifestManager.cs       # Version tracking
│   ├── DeduplicationService.cs  # File deduplication
│   └── PromotionService.cs      # Asset promotion
│
├── Models/                      # Data structures
├── Providers/                   # Entity providers
├── Progress/                    # Progress reporting
└── Utilities/                   # Helper functions (incl. WrapperHeuristics)
```

**Key Dependencies:**
- `SharpGLTF.Toolkit` - GLTF/GLB export
- `SixLabors.ImageSharp` - Image processing
- `BCnEncoder.Net` - Texture encoding (used by `UnityReader/Texture/`)
- `K4os.Compression.LZ4` - LZ4 decompression for AssetBundle blocks (used by vendored AssetStudioMod core)

**Namespaces:**
- `AssetExtractor.Extraction`
- `AssetExtractor.Export`
- `AssetExtractor.Pipeline`
- `AssetExtractor.Models`
- `AssetExtractor.Progress`
- `AssetExtractor.Providers`
- `AssetExtractor.Utilities`
- `UnityReader` (and `UnityReader.Internal`) for the reader surface
- `AssetStudio*`, `AssetRipper.TextureDecoder*` for the vendored sources (unmodified upstream namespaces)

---

### 5. AssetExtractor.CLI Project

**Location:** `/backend/src/AssetExtractor.CLI`

**Purpose:** Command-line interface for asset extraction (subprocess mode).

**Files:**
- `Program.cs` - Main CLI entry point (585 lines)
- `CliConfig.cs` - Command-line argument parsing
- `AssetExtractor.CLI.csproj` - Executable project

**GLB Extraction Commands:**
- `extract-glb <name>` - Extract single GLB (auto-detects unit/mapobject/gameobject)
- `extract-glb-units` - Extract all unit models
- `extract-glb-mapobjects` - Extract all map object models (filtered)
- `extract-glb-gameobjects` - Extract all known GameObjects
- `extract-all-glb` - Extract all GLBs (units + mapobjects + gameobjects)

**Texture Extraction:**
- `extract-textures` - Extract all textures (icons + objects)

**Combined:**
- `extract-all` - Extract everything (GLBs + textures)

**List Commands:**
- `list-units` - List all available units (163 units)
- `list-map-objects` - List all available map objects (362 objects)
- `list-game-objects` - List known GameObjects for batch extraction
- `list-all-prefabs [filter]` - List all prefabs (4002 total), optionally filter by name

**Debug/Analysis Commands:**
- `debug-prefab <name>` - Show hierarchy structure of a prefab
- `analyze-assets <search>` - Search assets by name

**Arguments:**
- `--game-path <path>` - Manually specify game installation path
- `--output-path <path>` - Specify output directory (default: `output/` next to the CLI executable)
- `--force, -f` - Force re-extraction even if version is cached
- `--json-progress` - Output progress as JSON lines (for automation)
- `--verbose` - Enable verbose logging (DEBUG level)
- `--help, -h` - Show help message

**Dependencies:**
- AssetExtractor (library reference)

---

### 6. API Project

**Location:** `/backend/src/API`

**Purpose:** ASP.NET Core web API - main application entry point.

**Structure:**

```
API/
├── Program.cs                   # Startup and DI configuration
├── appsettings.json
├── API.csproj                   # Embeds frontend + custom assets
│
├── Services/                    # 14 service files
│   ├── GameDataLoader.cs        # 6-step data loading with ability pre-aggregation
│   ├── GameDataService.cs       # Main game data ops (9.2KB)
│   ├── GamePathService.cs       # Game path resolution (20.5KB)
│   ├── SearchService.cs         # Global search with location-aware highlighting
│   ├── AssetExtractionService.cs  # Extraction orchestration (23.6KB)
│   ├── AssetServingService.cs   # Asset serving (8.4KB)
│   ├── ReferenceIndexService.cs # Relationship tracking (13.4KB)
│   ├── ReferenceIndexBuilder.cs # Index building (11.8KB)
│   ├── LocaleDiscoveryService.cs  # Locale detection (3.2KB)
│   ├── TexturePathResolver.cs   # Texture location (12.1KB)
│   ├── AbilityDtoBuilder.cs     # DTO building (7.2KB)
│   ├── SettingsService.cs       # Settings (2.3KB)
│   ├── IAssetExtractionService.cs
│   └── IAssetServingService.cs
│
├── Endpoints/                   # 20 REST API endpoints
│   ├── UnitsEndpoints.cs        # (22.2KB)
│   ├── HeroesEndpoints.cs       # (16.1KB)
│   ├── ModelsEndpoints.cs       # (51.5KB - largest)
│   ├── SpellsEndpoints.cs       # (18.8KB)
│   ├── BuildingsEndpoints.cs    # (19.1KB)
│   ├── AbilitiesEndpoints.cs    # (21.4KB)
│   ├── SkillsEndpoints.cs       # (12.9KB)
│   ├── ArtifactsEndpoints.cs    # (15.9KB)
│   ├── MapObjectsEndpoints.cs   # (15.9KB)
│   ├── SubclassesEndpoints.cs   # (13.6KB)
│   ├── FactionLawsEndpoints.cs  # (9.9KB)
│   ├── ReferencesEndpoints.cs   # (10KB)
│   ├── SettingsEndpoints.cs     # (9.3KB)
│   ├── GameEndpoints.cs         # (8.4KB)
│   ├── FilesystemEndpoints.cs   # (5.8KB)
│   ├── SearchEndpoints.cs       # (2KB)
│   ├── LabelsEndpoints.cs       # (4.3KB)
│   ├── AssetsEndpoints.cs       # (6.5KB)
│   ├── ExtractionEndpoints.cs   # (5.3KB)
│   └── ViewerEndpoints.cs       # (4.3KB)
│
├── Hubs/
│   └── ExtractionHub.cs         # SignalR for extraction progress
│
├── Hosting/
│   ├── ExtractionMode.cs        # Subprocess extraction mode
│   ├── TrayIcon.cs              # System tray integration
│   ├── ErrorTriggeredFileLogger.cs  # In-memory buffered logging with error-triggered file dumps
│   └── LogFormatter.cs          # Custom log formatting
│
├── Models/
│   └── (DTO models)
│
├── Contracts/
│   └── (API contracts)
│
├── Extensions/
│   └── (DI setup extensions)
│
├── Helpers/
│   └── (Utility helpers)
│
└── Utilities/
    └── (Shared utilities)
```

**Key Features:**

**Single-File Publishing:**
- `PublishSingleFile=true`
- `SelfContained=true`
- Embeds frontend build in `wwwroot/`
- Embeds custom stat icons from `/assets/CustomIcons/`
- Embeds favicon for tray icon

**API Endpoint Categories:**
- Game Data: Units, Heroes, Buildings, Spells, Skills, Abilities, Artifacts, MapObjects, Subclasses, FactionLaws
- References: Cross-entity relationships
- Assets: Texture and model serving
- Search: Global search across all entities
- Models: 3D model export/streaming
- Extraction: Asset extraction control
- Settings: Theme, locale, game path, placeholder resolution, auto-extract, extract PNG/GLB
- Labels: Localization labels

**Dependencies:**
- Domain
- Localization
- GameData
- AssetExtractor
- Plus ASP.NET Core libraries

---

## Data Flow

### 1. Startup Sequence

```
Program.cs
├─> Configure services (DI)
├─> Map endpoints (20 endpoint files)
├─> Configure SignalR (/ws/extraction)
├─> Serve embedded frontend (production)
└─> Start tray icon (production)

GameDataLoader.Initialize()
├─> 1. Load LangIndex (language files)
├─> 2-3. Prepare TextResolvers and DbAccessor
├─> 4. Load DbIndex (Core.zip, units)
├─> 5. Create all entity indexes (11 indexes)
└─> 6. Pre-aggregate abilities (locale-independent)

Detail builders created on-request (not at startup).
```

### 2. Entity Retrieval

```
Request: GET /api/units/crossbowman

UnitsEndpoints.GetById("crossbowman")
├─> UnitsIndex.GetById("crossbowman")
├─> UnitDetails.Build(unit)
│   ├─> TextResolverFacade.Resolve(name, description)
│   │   ├─> BasicResolver (lookup in lang files)
│   │   └─> PlaceholderResolver ({0} → script values)
│   │       └─> ScriptInterpreter.Evaluate(script)
│   ├─> AbilityIndex.GetAbilities(unit.abilityIds)
│   └─> Calculate stats (via scripts)
└─> Return UnitDetailDto
```

### 3. Placeholder Resolution

```
Input: "Deals {0} damage"

PlaceholderResolver.Resolve()
├─> Look up args file for this text key
├─> Find arg[0] = "current_unit_damage"
├─> ScriptRegistry.GetScript("current_unit_damage")
├─> ScriptInterpreter.Evaluate(script, context)
│   └─> UnitAccessOperations.GetDamage(currentUnit)
├─> Replace {0} with calculated value
└─> Output: "Deals 15 damage" (wrapped in <resolved> tag)
```

### 4. Asset Extraction (Subprocess)

```
User clicks "Extract Assets"

ExtractionEndpoints.StartExtraction()
├─> AssetExtractionService.StartExtraction()
│   ├─> Check cache (ManifestManager)
│   │   └─> If cached: return immediately
│   ├─> Spawn subprocess: AssetExtractor.CLI.exe
│   │   └─> Args: --extract --game-path ... --json-progress
│   ├─> Read JSON progress from stdout
│   │   └─> {"type":"progress","current":50,"total":200}
│   ├─> Broadcast via SignalR (ExtractionHub)
│   │   └─> Frontend updates progress bar
│   └─> Wait for completion or cancellation
│       └─> On cancel: Process.Kill(entireProcessTree: true)
└─> Return jobId

CLI Process:
├─> ExtractionOrchestrator.ExtractEverything()
│   ├─> AssetLoader.GameBundle (load Unity assets)
│   ├─> Extract textures (StandaloneTextureExtractor)
│   ├─> Extract GLBs (parallel extraction)
│   │   ├─> Units (Parallel.ForEach)
│   │   └─> Map Objects (Parallel.ForEach)
│   ├─> Save manifest
│   └─> Promote assets
└─> Exit with code 0 (success)
```

## Configuration

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5176"
      }
    }
  },
  "GameDataPath": null
}
```

### Environment Variables

- `ASPNETCORE_ENVIRONMENT` - Development or Production
- `DOTNET_ENVIRONMENT` - Alternative to above

## Building

### Development Build

```bash
dotnet build backend/Backend.slnx
```

### Production Build

Use the release script from the repository root:

```bash
node scripts/build-release.js
```

Builds for win-x64 and linux-x64. Outputs zip packages to `dist/`. Each contains a self-contained executable (`OldenEraExplorer`) with:
- Embedded frontend
- Native dependencies
- System tray icon support

## Dependencies

### Key NuGet Packages

**API:**
- Microsoft.AspNetCore.App 10.0
- Microsoft.AspNetCore.SignalR 10.0
- NotificationIcon.NET 1.2.8

**AssetExtractor:**
- SharpGLTF.Toolkit
- SixLabors.ImageSharp
- BCnEncoder.Net (used by `UnityReader/Texture/`)
- K4os.Compression.LZ4 (used by the vendored AssetStudioMod core)

See [NOTICE.md](../NOTICE.md) for full license information.

## Performance Considerations

### Caching

- Game data loaded once on startup
- Scripts not cached (lightweight evaluation)
- Extracted assets cached on disk with manifest

### Lazy Loading

- Entity details built on-request
- 3D models extracted on-demand
- Language files loaded per-locale

### Asset Extraction

- First extraction: ~1 minute on modern hardware
- Subsequent: skip if manifest matches
- Deduplication prevents re-extraction

## Error Handling

### Game Not Found

- Auto-detection tries common Steam paths
- Fallback to manual path entry
- Path validation before acceptance

### Script Errors

- Placeholder remains unresolved (displayed in red)
- Logs warning with script name
- Application continues (graceful degradation)

### Extraction Failures

- Logged and reported via SignalR
- Partial results still usable
- User can retry

## Troubleshooting

### Issue: "Game data not loaded"
**Solution:** Check game path in Settings. Verify `Core.zip` exists in the game's `*_Data/StreamingAssets/` directory.

### Issue: "Extraction hangs"
**Solution:** Kill subprocess and retry. Check disk space (~1.3 GB needed).

### Issue: "Red placeholders everywhere"
**Solution:** Enable placeholder resolution in Settings. Check logs for script errors.
