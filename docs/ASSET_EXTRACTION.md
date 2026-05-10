# Asset Extraction

The asset extraction system processes Unity asset bundles from Heroes of Might and Magic: Olden Era to extract PNG images and GLB 3D models.

## Overview

Unity games store assets in proprietary binary formats (`.assets`, `.resS`). The reader under `backend/src/AssetExtractor/UnityReader/` parses them; the surrounding pipeline then:
- Extracts **Textures** → PNG images
- Extracts **3D Models** → GLB files (GLTF binary format)

The reader vendors curated subsets of [AssetStudioMod](https://github.com/aelurum/AssetStudio) and [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) for the low-level parsing primitives. The animation processor and texture-format glue are clean-room own code.

## Architecture

### Project Structure

```
backend/src/
├── AssetExtractor/              # Pipeline library + Unity reader
│   ├── UnityReader/             # In-house Unity SerializedFile / AssetBundle reader
│   │   ├── Api/                 # Surface used by Extraction/ and Export/ (UnityScene, Mesh, Material, ...)
│   │   ├── Internal/            # AnimationClipProcessor, PathChecksumCache, MicrosoftLoggerAdapter
│   │   ├── Texture/             # Managed BC1/BC3/BC7 + RGB family decoders
│   │   ├── Vendor/AssetStudio/  # Vendored AssetStudioMod core
│   │   └── Vendor/AssetRipperTextureDecoder/  # Vendored AssetRipper.TextureDecoder
│   │
│   ├── Extraction/              # Asset extraction
│   │   ├── AssetExtractor.cs
│   │   ├── AssetLoader.cs       # Unity scene loading + resource-path queries
│   │   ├── MaterialExtractor.cs
│   │   ├── MeshDataExtractor.cs
│   │   ├── StandaloneTextureExtractor.cs
│   │   ├── AnimationDataExtractor.cs
│   │   ├── HierarchyExtractor.cs
│   │   ├── BoneDataExtractor.cs
│   │   ├── ThreadSafeTextureCache.cs
│   │   └── ShaderProperties.cs
│   │
│   ├── Export/                  # GLTF/GLB export
│   │   ├── GlbExporter.cs
│   │   ├── GltfMeshExporter.cs
│   │   ├── GltfAnimationExporter.cs
│   │   ├── TextureExporter.cs   # Streams PNG to disk + inline hash
│   │   ├── NodeHierarchyBuilder.cs
│   │   └── GlbCoordinateConversion.cs
│   │
│   ├── Pipeline/
│   │   ├── ExtractionOrchestrator.cs
│   │   ├── ManifestManager.cs
│   │   ├── DeduplicationService.cs
│   │   └── PromotionService.cs
│   │
│   ├── Models/
│   ├── Providers/
│   ├── Progress/
│   └── Utilities/
│
└── AssetExtractor.CLI/          # CLI executable
    ├── Program.cs               # Main CLI entry
    ├── CliConfig.cs             # Argument parsing
    └── AssetExtractor.CLI.csproj
```

### Subprocess Model

Asset extraction runs as a **separate CLI process** spawned by the main API.

This keeps extraction cancellable, isolated from the main API process, and able to report progress in real time.

**How it works:**
```
API Process                     CLI Process
    |                               |
    |--spawn AssetExtractor.CLI---->|
    |                               |
    |<--JSON progress updates-------|
    |                               |
    |--SIGTERM (if cancelled)------>|
    |                               |
    |<--exit code 0 (success)-------|
```

---

## Unity Reader

Lives under `backend/src/AssetExtractor/UnityReader/`. Top-level namespace: `UnityReader`.

**Public surface (`Api/`):** `UnityScene`, `GameObject`, `Transform`, `Mesh`, `Material`, `Shader`, `MeshRenderer`, `SkinnedMeshRenderer`, `MeshFilter`, `Animator`, `AnimatorController`, `AnimatorOverrideController`, `AnimationClip`, `MeshGeometry`, `BoneCurves`, `Vector3Curve`, `QuaternionCurve`, `UnityTexture`.

**Vendored sources:**
- `Vendor/AssetStudio/` - curated AssetStudioMod subset (SerializedFile reader, BundleFile reader, vertex/index parsing, texture-format enum, animation-clip primitives).
- `Vendor/AssetRipperTextureDecoder/` - managed BC1/BC3/BC7 decoders.

**Clean-room own code (`Internal/`):**
- `AnimationClipProcessor` - decodes streamed/dense/constant clip data into per-bone `BoneCurves`.
- `PathChecksumCache` - reverse-lookup from CRC32 path-hash to slash-separated transform path, per-Animator.

**Relevant NuGet dependencies:**
- `K4os.Compression.LZ4` - LZ4 for AssetBundle blocks
- `BCnEncoder.Net` - BC1/BC3/BC7 managed decode
- `SharpGLTF.Toolkit` - GLB export
- `SixLabors.ImageSharp` - PNG encode + image processing

---

## CLI Commands

### Extraction Commands

**GLB Extraction:**
- `extract-glb <name>` - Single asset (auto-detects unit/map-object)
- `extract-glb-units` - All unit models
- `extract-glb-mapobjects` - All map object models
- `extract-glb-gameobjects` - Known GameObjects ("PLATFORM + BACK")
- `extract-all-glb` - All GLBs

**Texture Extraction:**
- `extract-textures` - PNG icons/textures only

**Combined:**
- `extract-all` - Everything (GLBs + textures)

### List Commands

- `list-units` - List all available units (163 units)
- `list-map-objects` - List all available map objects (362 objects in categories: ARTIFACT, BARRACKS, INTERACTIVE, RESOURCE)
- `list-game-objects` - List known GameObjects for batch extraction (currently only "PLATFORM + BACK")
- `list-all-prefabs [filter]` - List all prefabs (4002 total), optionally filter by name

### Debug/Analysis Commands

- `debug-prefab <name>` - Show hierarchy structure of a prefab (displays GameObject tree with transforms)
- `analyze-assets <search>` - Search assets by name across all asset files

### Arguments

- `--game-path <path>` - Manual game path override
- `--output-path <path>` - Output directory (default: `output/` next to the CLI executable)
- `--force, -f` - Force re-extraction (ignore cache)
- `--json-progress` - JSON line output for subprocess integration
- `--verbose` - DEBUG level logging
- `--help, -h` - Help text

---

## Extraction Pipeline

### Full Extraction Flow (`extract-all`)

```
ExtractionOrchestrator.ExtractEverything()
├─> 1. ResolveGamePath()
│   └─> Validates path or auto-detects
│
├─> 2. InitializeVersionManagement()
│   ├─> Detect game version from build info
│   └─> Compute assets hash
│
├─> 3. Create AssetExtractorService
│   └─> AssetLoader.GameBundle (loads all .assets files)
│       └─> Parses all Unity asset files
│
├─> 4. Extract Textures (StandaloneTextureExtractor)
│   ├─> Iterate all ITexture2D objects
│   ├─> Convert to PNG format
│   ├─> Save versioned: `Assets-{version}/Assets/Resources/icons/{category}/{name}.png`
│   └─> Update manifest
│
├─> 5. Extract GLBs (3 phases - parallel with progress)
│   ├─> Phase 1: GameObjects (serial - only "PLATFORM + BACK")
│   ├─> Phase 2: Units (parallel - Parallel.ForEach with CPU count threads)
│   └─> Phase 3: Map Objects (parallel)
│
├─> 6. Save Manifest
│   └─> Write `cache_manifest.json` with version tracking
│
└─> 7. Run PromotionService
    └─> Consolidate multi-version assets
```

### Detailed Data Flow

```
Unity Assets (.assets files)
  └─> AssetLoader.GameBundle (IAsset lookup)
      └─> AssetExtractor (extraction facade)
          ├─> HierarchyExtractor (GameObject tree)
          ├─> MeshDataExtractor (SkinnedMesh/Mesh → vertices/normals/UVs)
          ├─> BoneDataExtractor (Armature/Bones)
          ├─> AnimationDataExtractor (AnimationClip)
          └─> MaterialExtractor (Materials → textures)
              └─> TextureData (ITexture2D → PNG bytes)

Result: PrefabData (UnitData/MapObjectData/GameObjectData)
  └─> GlbExporter (SharpGLTF)
      ├─> NodeHierarchyBuilder (hierarchy tree)
      ├─> GltfMeshExporter (glTF mesh nodes)
      └─> GltfAnimationExporter (glTF animation tracks)
          └─> GLB file (binary glTF 2.0 + embedded textures)

TextureExporter → PNG files (versioned paths)
ManifestManager → cache_manifest.json (tracking)
```

---

## Extraction Details

### Texture Extraction

**Process:**
1. Load asset files via `UnityScene.Load(...)`
2. Enumerate every supported `Texture2D`
3. Decode to RGBA8, encode to PNG
4. Save with versioned path: `Assets-{version}/Assets/Resources/icons/{category}/{name}.png`
5. Record in manifest

### 3D Model Extraction

#### 1. Hierarchy Extraction

```csharp
var hierarchy = HierarchyExtractor.Extract(gameObject);
```

Builds a tree structure. Example from `debug-prefab "PLATFORM + BACK"`:
```
- PLATFORM + BACK
  - GameObject (1)
    - unit_plarform [Animator] S(0.70,0.70,0.70) R(-0.00,-1.00,-0.00,0.00)
      - Armature S(45.80,45.80,45.80) R(-0.71,0.00,-0.00,0.71)
        - Bone R(0.71,0.00,0.00,0.71)
        - Bone.001, Bone.002, ...
        - Bricks_LP.002 [MeshRenderer, MeshFilter]
      - Bricks_LP.001 [SkinnedMeshRenderer]
    - unit_info_back (1)
    - Quad [MeshRenderer, MeshFilter]
```

Legend: `[Component]` = Unity component, `S(x,y,z)` = scale, `R(x,y,z,w)` = rotation quaternion

#### 2. Mesh Extraction

```csharp
var meshData = MeshDataExtractor.Extract(mesh);
```

Extracts:
- **Vertices** - 3D positions
- **Normals** - Surface directions (for lighting)
- **UVs** - Texture coordinates
- **Triangles** - Face indices
- **Bone weights** - For skeletal animation

#### 3. Material Extraction

```csharp
var material = MaterialExtractor.Extract(material);
```

Extracts:
- **Textures** - MainTexture (diffuse), normal maps
- **Colors** - BaseColor, EmissionColor
- **Properties** - AlphaMode (0=Opaque, 1=Mask, 2=Blend), AlphaCutoff, DoubleSided
- **Emission** - EmissionBlinkPower, EmissionBlinkSpeed, EmissionMinPower

Example verbose output:
```
Extracting material: crossbowman_weapon_MT
EmissionColor: (0.00, 0.00, 0.00, 1.00)
Material properties: BaseColor=(1.00, 1.00, 1.00, 1.00)
AlphaMode: 1, AlphaCutoff: 0.60
DoubleSided: False
MainTexture: crossbowman_weaopon_color
```

#### 4. Animation Extraction

```csharp
var animation = AnimationDataExtractor.Extract(clip);
```

Extracts per animation clip:
- **Curves** - Position, rotation, scale curves for each bone
- **Sample rate** - Typically 30 FPS
- **Timing** - StartTime, StopTime, Duration

Example verbose output for crossbowman:
```
Found Animator component on node: crossbowman
Controller type: AnimatorOverrideController_2018_3
Found AnimatorOverrideController with 9 clip overrides
Found 9 animation clips
Processing clip: idle, sample rate=30
Clip timing: StartTime=0.000000, StopTime=5.333333, Duration=5.333333s
Extracted 133 position curves
Extracted 133 rotation curves
Extracted 133 scale curves
  Animation: idle (5.33s)
Successfully extracted 9 animations
[POSE] Default pose set from 'idle' (t=0) for 133 bones
```

**Note:** The model's default pose is automatically set from the first frame of the 'idle' animation.

#### 5. Coordinate Conversion

Unity uses left-handed Y-up coordinates. GLTF uses right-handed Y-up.

**Conversion:**
```
Unity: X-right, Y-up, Z-forward (left-handed)
GLTF:  X-right, Y-up, Z-backward (right-handed)
```

The `GlbCoordinateConversion` class handles this transformation, flipping the Z axis.

#### 6. GLB Export

The `GlbExporter` class builds and writes the final GLB file using SharpGLTF:

```csharp
// Simplified - actual implementation in GlbExporter.cs
var model = BuildGltfModel(hierarchy, meshes, materials, animations, bones);
model.WriteGLB(memoryStream);
```

**CLI Output Example** (`extract-glb crossbowman --verbose`):
```
Extracting meshes
  Mesh: Crossbowman_mesh (8134 vertices, 8703 triangles) - Material: crossbowman_weapon_MT
  Mesh: CrossbowmanBird_mesh (810 vertices, 1052 triangles) - Material: crossbowman_weapon_MT
Total meshes: 2

Extracting animations
Found 9 animation clips
  Animation: attack (1.93s)
  Animation: idle (5.33s)
  Animation: walk (1.60s)
  ...
Total animations: 9

Success! Exported to: output/Assets-0.46.10-demo/Assets/Resources/units/humans/crossbowman.glb
```

**Output Structure:**
```
output/
├── Assets-{version}/Assets/
│   ├── Resources/
│   │   ├── units/{faction}/{name}.glb
│   │   ├── objects/{category}/{name}.glb
│   │   └── icons/{category}/{name}.png
│   ├── GameObject/{name}.glb
│   └── Texture2D/{name}.png
└── cache_manifest.json
```

After promotion (API's ExtractedAssets):
```
ExtractedAssets/
├── Assets-0.45.02-cb/...        (versioned)
├── Assets-0.46.10-demo/...      (versioned)
├── Assets-shared/Assets/Resources/
│   ├── units/humans/crossbowman.glb
│   ├── units/nature/druid.glb
│   ├── objects/interactive/arena.glb
│   └── icons/...
└── cache_manifest.json
```

---

## Subprocess Integration

### API Side (AssetExtractionService)

```csharp
// backend/src/API/Services/AssetExtractionService.cs

public string StartExtraction(StartExtractionRequest request)
{
    // 1. Check cache IN-PROCESS (instant)
    if (!request.ForceReExtract)
    {
        var version = ManifestManager.DetectGameVersion(gamePath);
        if (_manifestService.IsVersionExtracted(version))
        {
            // Cache hit - return immediately
            return jobId;
        }
    }

    // 2. Start CLI subprocess
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,  // Self or CLI.exe
            ArgumentList = ["--extract", "--game-path", path, "--output-path", output, "--json-progress"],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }
    };
    process.Start();

    // 3. Stream JSON progress from stdout
    var stdoutTask = ReadStdoutAsync(process.StandardOutput, jobId, cancellationToken);
    var stderrTask = ReadStderrAsync(process.StandardError, cancellationToken);

    // 4. Wait for completion or cancellation
    await process.WaitForExitAsync(cancellationToken);

    // 5. Handle cancellation
    if (cancellationToken.IsCancellationRequested)
    {
        process.Kill(entireProcessTree: true);  // True subprocess kill
    }

    // 6. Reload manifest and invalidate caches
    _manifestService.ReloadManifest();
    _assetServingService.InvalidateCache();

    return jobId;
}
```

### CLI Side (Program.cs)

```csharp
// backend/src/AssetExtractor.CLI/Program.cs

// Parse arguments
var config = CliConfig.Parse(args);

// Set JSON progress mode if requested
if (config.JsonProgress)
{
    ProgressBar.JsonOutputMode = true;
}

// Run extraction (UnityScene.LoggerFactory must be configured before this point)
var orchestrator = new ExtractionOrchestrator(config.OutputPath, logger, loggerFactory);
var result = orchestrator.ExtractEverything(config.GamePath, config.Force);

// Return exit code
return result.FailedCount > 0 ? 1 : 0;
```

### Progress JSON Format

```json
{"type":"status","status":"starting","message":"Initializing extraction..."}
{"type":"progress","phase":"Textures","current":50,"total":2000,"percent":2.5,"item":"crossbowman.png"}
{"type":"progress","phase":"Units","current":10,"total":163,"percent":6.1,"item":"crossbowman"}
{"type":"status","status":"completed","success":2500,"failed":10}
```

### SignalR Broadcasting

```csharp
// API reads stdout and broadcasts
process.OutputDataReceived += (sender, e) => {
    var json = JsonSerializer.Deserialize<ProgressUpdate>(e.Data);
    await hub.Clients.All.SendAsync("ProgressUpdate", json);
};
```

### Frontend Display

```typescript
connection.on('ProgressUpdate', (data) => {
  setProgress({
    percentage: (data.current / data.total) * 100,
    message: data.message,
  });
});
```

---

## Manifest Management

### ManifestManager

**Location:** `/backend/src/AssetExtractor/Pipeline/ManifestManager.cs`

**Functionality:**
- Load/Save `cache_manifest.json`
- Track extracted versions
- Version detection: `DetectGameVersion(gamePath)` → parses build info
- Path generation: `GetVersionOutputPath(version)` → versioned directory
- Thread-safe (lock-based)

**Manifest Structure:**
```json
{
  "version": 3,
  "lastPromotionRun": "2026-01-15T15:47:21Z",
  "builds": {
    "0.46.10-demo": {
      "gameRootPath": "/path/to/<game>_Data",
      "gameVersion": "0.46.10-demo",
      "extractedAt": "2026-01-15T06:37:34Z",
      "assetsHash": "b169e3366d732fd2"
    },
    "0.45.02-cb": {
      "gameRootPath": "/path/to/<game>_Data",
      "gameVersion": "0.45.02-cb",
      "extractedAt": "2026-01-15T15:46:16Z",
      "assetsHash": "b1da178b33b68c15"
    }
  },
  "assets": {
    "Assets/Resources/icons/units/hex_portraits/crossbowman": {
      "type": "Texture2D",
      "status": "shared",
      "sharedHash": "abc123...",
      "size": 12345,
      "versions": ["0.46.10-demo", "0.45.02-cb"],
      "extension": ".png"
    },
    "Assets/Resources/units/humans/crossbowman": {
      "type": "GLB",
      "status": "shared",
      "sharedHash": "def456...",
      "size": 543210,
      "versions": ["0.46.10-demo"],
      "extension": ".glb"
    }
  }
}
```

### Deduplication

**Service:** `DeduplicationService`

**How it works:**
- Compute XXHash64 of file content
- Compare with manifest records
- Skip if hash matches (file unchanged)

**Benefits:**
- Faster re-extraction after game updates
- Only processes changed assets

### Promotion

**Service:** `PromotionService`

**What is promotion?**

Assets are extracted to versioned directories:
```
Assets-0.45.02-cb/Assets/Resources/icons/units/hex_portraits/crossbowman.png
Assets-0.46.10-demo/Assets/Resources/icons/units/hex_portraits/crossbowman.png  (updated)
```

Promotion copies the latest version to a non-versioned directory for serving:
```
Assets-shared/Assets/Resources/icons/units/hex_portraits/crossbowman.png  (promoted)
```

**Why?**
- Frontend always requests `/api/assets/png/units/hex_portraits/crossbowman`
- No need to update frontend when game version changes
- Old versions kept for rollback

---

## Progress Reporting

### Two Implementations

**JsonProgress** - Outputs JSON lines (for subprocess/automation):
```json
{"type":"progress","phase":"Units","current":42,"total":163,"percent":25.8,"item":"esquire"}
```

**ConsoleProgress** - Animated spinner with elapsed time:
```
Loading game assets (177 files)...
⠋ Loading... 0.0s
⠙ Loading... 0.1s
...
✓ Loaded in 17.2s

Extracting units...
[████████████░░░░░░░░░░░░░░░░░░] 42/163 (25.8%) - crossbowman
```

**NullProgress** - No-op implementation

### Integration

- `ProgressBar` utility class (throttled updates)
- `ExtractionProgress` data model
- Thread-safe: `OnProgress` callback from parallel threads

### CLI Logging

**CleanFormatter** - Custom console logger with ANSI colors:
- Structured scoping: `[PrefabName: name]` for unit/object extraction
- Color coding (ANSI escape codes):
  - Cyan (`[96m`) - Skeleton/bone extraction
  - Green (`[92m`) - Mesh extraction
  - Blue (`[94m`) - Material/texture extraction
  - Magenta (`[95m`) - Animation extraction
  - Red - Errors

---

## Performance

### Threading Model

- `Parallel.ForEach` with `Environment.ProcessorCount` threads
- `ConcurrentBag` for thread-safe result collection
- `Interlocked` operations for counters
- Lock-based manifest synchronization

### Memory & Performance

- GameBundle loaded ONCE (reused across phases)
- Asset caching in AssetLoader (prefabs, pathIds, resource paths)
- Texture decode lock (vendor file-stream reads not thread-safe)
- Deduplication prevents duplicate GLB files

### Timing

**First Extraction (measured on modern hardware):**
- Game bundle loading: ~17-20s (177 asset files)
- Building asset caches: ~1s (4002 prefabs, 541880 PathIDs)
- Textures: ~9s
- Models: ~35s (~525 total: 163 units + 362 map objects)
- **Total: ~1 minute**

**Subsequent Extractions (with cache):**
- Unchanged files: Instant skip
- Changed files: Only re-extract those
- **Typical: seconds**

### Optimization Techniques

**Parallel Processing:**
```csharp
Parallel.ForEach(units, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, unit => {
    ExtractUnit(unit);
});
```

**Lazy Loading:**
```csharp
// Don't load entire asset bundle into memory
using var reader = new AssetBundleReader(path);
foreach (var asset in reader.StreamAssets()) {
    ProcessAsset(asset);
}
```

**Caching:**
```csharp
var cache = new ThreadSafeCache<string, Texture2D>();
if (cache.TryGet(textureName, out var cached)) {
    return cached;
}
```

---

## Error Handling

### Error Types

**Game Not Found:**
- Auto-detection tries common Steam paths
- Fallback to manual path entry
- Path validation before starting

**Asset Loading Errors:**
- Try-catch per asset with graceful failure
- Individual failures don't stop batch
- Failed items tracked in `BatchExtractionResult`

**Export Errors:**
- Logged with asset name and error
- Partial results still usable
- Exit code non-zero if any failures

### Exit Codes

- `0` - All success
- `1` - Some failures (check logs)

---

## Troubleshooting

### Issue: "Extraction hangs at X%"

**Causes:**
- Large asset taking time
- Corrupt asset bundle
- Out of memory

**Solutions:**
1. Wait (some assets are large)
2. Check logs for errors
3. Cancel and retry with `--force`

### Issue: "GLB files are huge"

**Cause:** GLTF stores data uncompressed in binary.

**Solutions:**
- Use Draco compression (future enhancement)
- Serve with gzip at HTTP level
- Current sizes are acceptable (~1-5MB per model)

### Issue: "Models look wrong in viewer"

**Causes:**
- Coordinate conversion error
- Missing materials/textures
- Incorrect bone weights

**Solutions:**
1. Check console for material/texture errors
2. Verify textures were extracted
3. Inspect GLB in external viewer (Blender)
4. Report issue with specific model ID

### Issue: "Out of memory"

**Causes:**
- Too many parallel tasks
- Very large asset bundles

**Solutions:**
1. Reduce `MaxDegreeOfParallelism`
2. Restart extraction process
3. Extract in batches (`--png` then `--glb`)

---

## Future Enhancements

### Planned Features

1. **Particle Systems** - Extract VFX effects (very complex, low priority)
2. **Audio** - Extract sound files as OGG/WAV
3. **Draco Compression** - Smaller GLB files
4. **Progressive Loading** - Stream models chunk-by-chunk
5. **LOD Support** - Extract multiple detail levels

### Known Limitations

1. **Particle systems and VFX not supported** - Unity particle systems, visual effects, and complex shader-based effects are not included in GLB exports. This is an inherent limitation of the GLB format and the extraction process. Particle systems are runtime-dependent, require complex Unity-specific data structures, and don't have direct equivalents in the GLTF/GLB specification. This means exported 3D models will closely resemble their in-game appearance but will lack visual effects such as:
   - Particle effects (fire, smoke, sparks, glowing embers, magical auras)
   - Shader-based VFX (glowing effects, magical trails, pulsing lights, environmental effects)
   - Complex post-processing effects
   - **This is a very difficult technical challenge and is planned for a future enhancement, but represents significant development effort.**

2. **Some shaders don't convert** - Custom Unity shaders may not map perfectly to GLTF materials. The exporter attempts to convert common shader properties (base color, emission, alpha mode) but advanced shader features may not translate correctly.

3. **No physics data** - Colliders, rigid bodies, and physics simulation data are not extracted. Only visual mesh data is included.

---

## Technical References

- [AssetStudioMod](https://github.com/aelurum/AssetStudio) - Upstream of vendored Unity reader code
- [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) - Upstream of vendored BC1/BC3/BC7 decoder
- [GLTF Specification](https://www.khronos.org/gltf/)
- [Unity Asset Bundle Format](https://docs.unity3d.com/Manual/AssetBundlesIntro.html)
- [SharpGLTF Library](https://github.com/vpenades/SharpGLTF)
