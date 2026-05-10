# Contributing

This document covers development setup, build instructions, and contribution guidelines.

## Prerequisites

- **.NET 10 SDK**
- **Node.js 22+**
- **Heroes of Might and Magic: Olden Era** (Steam installation, for testing)

## Project Structure

```
├── backend/         # .NET API server and game data processing
│   └── src/
│       ├── Domain/             # Core entities
│       ├── Localization/       # Text resolution and scripting
│       ├── GameData/           # Game data loading and indexing
│       ├── AssetExtractor/     # Asset extraction pipeline; the Unity reader lives under UnityReader/
│       ├── AssetExtractor.CLI/ # CLI for subprocess extraction
│       └── API/                # Web API
├── frontend/        # React + Vite web interface
├── scripts/         # Build and release scripts
└── docs/            # Technical documentation
```

## Development Setup

### Quick Start

```bash
npm install
npm run dev
```

This starts:
- **Backend** at `http://localhost:5176`
- **Frontend** at `http://localhost:5173`

Open `http://localhost:5173` in your browser.

### Manual Start

**Backend:**
```bash
cd backend/src/API
dotnet run
```

**Frontend:**
```bash
cd frontend
npm run dev
```

## Building

### Development Build

**Backend:**
```bash
dotnet build backend/Backend.slnx
```

**Frontend:**
```bash
cd frontend && npm run build
```

### Release Build

Run the release script from the repository root:

```bash
node scripts/build-release.js
```

This builds release packages for all platforms:
- `win-x64`
- `linux-x64`

Output goes to `dist/`:

```
dist/
├── OldenEraExplorer-win-x64.zip
└── OldenEraExplorer-linux-x64.zip
```

Each package contains a single self-contained executable with the frontend embedded.

You can optionally pass a version:

```bash
node scripts/build-release.js 1.2.3
```

## Technical Documentation

- [Backend Architecture](docs/BACKEND.md) - .NET project structure, services, data flow
- [Frontend Architecture](docs/FRONTEND.md) - React app structure, state management
- [Placeholder Resolution](docs/PLACEHOLDER_RESOLUTION.md) - How game text is resolved
- [Asset Extraction](docs/ASSET_EXTRACTION.md) - How 3D models and images are extracted

## Code Style

### Backend (.NET)

- C# 12 with nullable reference types
- File-scoped namespaces
- Primary constructors where appropriate

### Frontend (TypeScript)

- TypeScript strict mode
- Functional components with hooks
- Feature-based folder structure

## Submitting Changes

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Make your changes
4. Run builds to verify (`dotnet build backend/Backend.slnx` and `cd frontend && npm run build`)
5. Commit your changes
6. Push to your fork
7. Open a Pull Request

## Reporting Issues

If you encounter bugs or have suggestions, please open an issue on GitHub with:
- Clear description of the problem or suggestion
- Steps to reproduce (for bugs)
- Expected vs actual behavior
- Your platform and game version
