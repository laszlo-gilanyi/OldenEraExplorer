# Olden Era Explorer

[![Downloads](https://img.shields.io/github/downloads/laszlo-gilanyi/OldenEraExplorer/total.svg?color=blue)](https://github.com/laszlo-gilanyi/OldenEraExplorer/releases)
[![Downloads@Latest](https://img.shields.io/github/downloads/laszlo-gilanyi/OldenEraExplorer/latest/total.svg?color=brightgreen)](https://github.com/laszlo-gilanyi/OldenEraExplorer/releases/latest)
[![Release](https://img.shields.io/github/v/release/laszlo-gilanyi/OldenEraExplorer?color=orange)](https://github.com/laszlo-gilanyi/OldenEraExplorer/releases/latest)

Fan-made browser for Heroes of Might and Magic: Olden Era game data.
Browse units, heroes, spells, artifacts, buildings, and 3D models without opening the game.

> Builds are released only on this project's [Releases page](https://github.com/laszlo-gilanyi/OldenEraExplorer/releases). The in-app updater (since v1.0.2) pulls from the same place. If you have a build from anywhere else, it's not mine, and I don't recommend running it.

## What's the point?

In-game, you can only see what's currently available to you - units in your army, heroes you own, skills offered at level-up, spells you've unlocked.

This shows you **everything**. All units, all heroes, all skills at all levels, all artifacts, all faction laws, all buildings. Browse the complete game database with no restrictions.

## How does it work?

The tool doesn't ship with any game content. When you run it:

1. Auto-detects your Steam installation
2. Reads JSON game data from `<game>/*_Data/StreamingAssets/Core.zip`
3. Loads localization and script arguments from the game files
4. Extracts textures and 3D models from the game's Unity asset bundles
5. Runs the game's own script system to resolve placeholder text (stuff like `{0}` → actual damage values)
6. Shows everything through a local web server

You need to own the game - this just makes your own copy easier to browse.

## What you can browse

### 10 data categories

- **Units (Creatures)** - stats, abilities, all attributes
- **Heroes** - portraits, starting attributes, specializations
- **Creature Abilities** - passive and active with full descriptions, tier and cost
- **Hero Skills** - skill trees, all levels, progression paths
- **Hero Subclasses** - specialized hero classes, bonuses, unlock requirements
- **Spells** - combat spells and world map spells, all levels
- **Artifacts** - equipment bonuses, set effects
- **Map Objects** - adventure map objects (resources, dwellings, etc.)
- **Buildings** - town structures, costs, requirements
- **Faction Laws** - unique faction mechanics and unlockable bonuses

### 3D viewer

View units and map objects in 3D with animation support.

**Heads up:** GLB models exported from Unity don't include particle systems, VFX, or complex shader effects. Models look close to in-game, but things like glowing auras, fire/smoke particles, and similar effects won't be present.

### Smart text resolution

Game descriptions use placeholders (`{0}`, `{1}`, etc.) that get calculated at runtime. This tool:
- Finds the game's argument files for each text
- Runs the actual game scripts to calculate exact values
- Color-codes the results: **orange** for successfully resolved, **red** if something couldn't be calculated

Some values can't be resolved because they depend on some runtime state or missing data files in certain builds. You'll see those in red.

### Multi-language support

All 14 languages the game supports. Full translations for each locale.

### Auto game detection

First launch scans your Steam library folders and finds the game automatically. If it doesn't find it, you can browse to the folder manually in settings.

### Settings

- **Theme** - Light, Dark, or match your system (default: Dark)
- **Language** - all 14 supported languages
- **Placeholder Resolution** - toggle the dynamic text calculation (default: on)
- **Auto-Extract on Startup** - automatically extract assets when game path is set (default: on)
- **Game Path** - current install location, switch between detected installs, or browse manually

Tested with the Early Access `0.80.07` build. As the game continues to evolve, future patches may still require updates to the tool.

### Asset extraction panel

Separate panel in the navbar:

- **Extract Icons / Extract Models** - choose PNG images, GLB models or both when you start extraction
- **Replace Existing** - force re-extraction even if files exist (resets after each extraction)
- **Progress Display** - real-time progress with phase info, time elapsed

## Installation

### Requirements

- **Heroes of Might and Magic: Olden Era** (Steam version)

### How to run

1. Download the latest release for your platform from [Releases](https://github.com/laszlo-gilanyi/OldenEraExplorer/releases)
2. Extract the zip
3. Run the executable (`OldenEraExplorer.exe` on Windows, `OldenEraExplorer` on Linux)
4. System tray icon appears, browser opens automatically to `http://localhost:5176`
5. App auto-detects your game and you're ready. Asset extraction runs in background (icons show placeholders while extracting)

## Usage

- **Navigation** - navbar switches between entity types
- **Search** - type in the search box to filter across all categories
- **Browse** - sidebar lists all entities, click one to see details in the main panel
- **3D Viewer** - three tabs (Units, Map Objects, Artifacts), select any to view with animations
- **Settings** - gear icon to change theme, language, game path, or toggle features

## FAQ

**Is this legal?**
Yes. Doesn't distribute any game files. Just reads from your own legally purchased copy, like modding tools do.

**Does it modify my game?**
No. Read-only. Won't touch your game installation.

**Why are icons missing?**
Asset extraction runs in background on first launch. Icons and models appear as they're extracted (about 1 minute on modern hardware, uses ~1.3 GB disk space). Gets saved to an `ExtractedAssets` folder next to the executable, so subsequent launches are instant.

**Can I use this for modding?**
It's a viewer, not a mod tool. But it might help you understand game data structure. Official modding tools from the developers don't exist yet.

**Why is some text red?**
Red means the placeholder couldn't be resolved. This happens when scripts reference runtime-only data (like current battle state) or when required JSON files are missing from that build. Orange means it resolved successfully.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and build instructions.

## License

GNU General Public License v3.0 - see [LICENSE](LICENSE).

Game assets and data are copyrighted by the developers. This is for personal use with legally owned game files only.

## Credits

- Built with [.NET](https://dotnet.microsoft.com/), [React](https://react.dev/), [Vite](https://vitejs.dev/), and [React Router](https://reactrouter.com/)
- Uses [AssetRipper](https://github.com/AssetRipper/AssetRipper) for Unity asset extraction
- 3D rendering powered by [Three.js](https://threejs.org/)
- Game data © [Heroes of Might and Magic: Olden Era](https://store.steampowered.com/app/3105440/Heroes_of_Might_and_Magic_Olden_Era/), developed by [Unfrozen](https://store.steampowered.com/developer/unfrozen), published by [Hooded Horse](https://store.steampowered.com/publisher/HoodedHorse)

See [NOTICE.md](NOTICE.md) for third-party licenses.
