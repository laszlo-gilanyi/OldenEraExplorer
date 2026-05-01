# Backend

The backend is a .NET 10 multi-project solution that handles game data loading, asset extraction, and serving processed information to the frontend.

## Architecture

```
backend/
├── src/
│   ├── Domain/                  # Pure domain entities (10 entity types)
│   ├── Localization/            # Text resolution and scripting system
│   ├── GameData/                # Game data loading and indexing
│   ├── AssetExtractor/          # Unity asset extraction library
│   │   └── AssetRipper/         # Vendored AssetRipper (52 projects)
│   ├── AssetExtractor.CLI/      # CLI executable for subprocess extraction
│   └── API/                     # Web API and hosting
│       ├── Endpoints/           # REST API endpoints (20 files)
│       ├── Services/            # API-level services (14 files)
│       ├── Hubs/                # SignalR hubs for real-time updates
│       └── Program.cs           # Application entry point
└── Backend.slnx                 # Modern XML solution file
```

## API Endpoints

| Endpoint Group | Description |
|----------------|-------------|
| `/api/game` | Game path detection, data loading |
| `/api/units` | Unit stats, abilities, models |
| `/api/heroes` | Hero specializations, skills |
| `/api/spells` | Magic spells and effects |
| `/api/artifacts` | Items and equipment |
| `/api/buildings` | Town buildings |
| `/api/skills` | Hero skills and perks |
| `/api/abilities` | Unit and spell abilities |
| `/api/subclasses` | Hero subclasses |
| `/api/map-objects` | Map objects and resources |
| `/api/faction-laws` | Faction-specific rules |
| `/api/search` | Full-text search across all entities |
| `/api/models` | 3D model streaming (GLB) |
| `/api/assets` | Texture and icon serving |
| `/api/settings` | User preferences |
| `/api/extraction` | Asset extraction control |
| `/api/references` | Cross-entity relationships |
| `/api/labels` | Localization labels |
| `/api/filesystem` | Folder picker operations |
| `/api/viewer` | 3D viewer configuration |

## Key Services

### Text Resolution
The placeholder resolution system (`Localization/Resolution/`) dynamically resolves game text placeholders like `{0}`, `{1}` with actual values. See [docs/PLACEHOLDER_RESOLUTION.md](../docs/PLACEHOLDER_RESOLUTION.md) for details.

### Texture Extraction
`StandaloneTextureExtractor.cs` extracts textures from Unity assets with caching. Supports icons, portraits, and custom stat icons.

### Model Conversion
`ModelsEndpoints.cs` serves GLB 3D models extracted from Unity asset bundles.

### Game Detection
`GameLocator.cs` auto-detects Steam installations across Windows/Linux/macOS.

## Development

### Prerequisites
- .NET 10 SDK

### Run Locally
```bash
cd backend/src/API
dotnet run
```

The API starts at `http://localhost:5176`.

### Build
```bash
# From repository root
dotnet build backend/Backend.slnx
```

### Publish (Release)

Use the release script from the repository root:

```bash
node scripts/build-release.js
```

Builds for win-x64 and linux-x64. Creates zip packages in `dist/`. Each contains a single self-contained executable with embedded frontend.

## API Documentation

When running in development mode, OpenAPI documentation is available at `/openapi/v1.json`.
