#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using AssetExtractor.Models;
using AssetExtractor.Extraction;
using AssetExtractor.Export;
using AssetExtractor.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AssetExtractorService = AssetExtractor.Extraction.AssetExtractor;

namespace AssetExtractor.Pipeline;

// Subprocess entry point spawned by the API for cancellable extraction. Coordinates
// path resolution, cache management, parallel texture/GLB extraction, and promotion.
public class ExtractionOrchestrator
{
    private readonly ILogger<ExtractionOrchestrator> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly string _outputPath;

    private ManifestManager? _manifestService;
    private DeduplicationService? _deduplicationService;
    private PromotionService? _promotionService;

    private string? _currentVersion;

    public ExtractionOrchestrator(ILogger<ExtractionOrchestrator>? logger = null, ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<ExtractionOrchestrator>.Instance;
        _loggerFactory = loggerFactory;
        _outputPath = Path.Combine(AppContext.BaseDirectory, "output");
    }

    public ExtractionOrchestrator(string outputPath, ILogger<ExtractionOrchestrator>? logger = null, ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<ExtractionOrchestrator>.Instance;
        _loggerFactory = loggerFactory;
        _outputPath = outputPath;
    }

    public string? CurrentVersion => _currentVersion;

    private ILogger<T>? CreateLogger<T>() => _loggerFactory?.CreateLogger<T>();

    public ManifestManager ManifestService
    {
        get
        {
            _manifestService ??= new ManifestManager(_outputPath);
            return _manifestService;
        }
    }

    public DeduplicationService DeduplicationService
    {
        get
        {
            _deduplicationService ??= new DeduplicationService();
            return _deduplicationService;
        }
    }

    public PromotionService PromotionService
    {
        get
        {
            _promotionService ??= new PromotionService(ManifestService, _outputPath);
            return _promotionService;
        }
    }

    // API broadcasts to SignalR clients via this hook.
    public Action<ExtractionProgress>? OnProgress { get; set; }

    public string InitializeVersionManagement(string gamePath)
    {
        _currentVersion = ManifestManager.DetectGameVersion(gamePath);
        _logger.LogInformation(
            "Initialized version management for: {Version}",
            _currentVersion);

        var sharedAssets = Path.Combine(gamePath, "sharedassets0.assets");
        var assetsHash = File.Exists(sharedAssets)
            ? DeduplicationService.ComputeFileHash(sharedAssets)
            : string.Empty;

        ManifestService.UpdateBuildInfo(_currentVersion, gamePath, assetsHash);

        return _currentVersion;
    }

    // Resolution order: manual path, saved settings, auto-detection. Manual paths are
    // not persisted here; the API owns persistence in the subprocess pattern.
    public string? ResolveGamePath(string? manualPath)
    {
        var settings = new SettingsService();
        var detector = new GamePathDetector();

        if (!string.IsNullOrEmpty(manualPath))
        {
            var normalized = detector.ValidateAndNormalizePath(manualPath);
            return normalized;
        }

        var savedPath = settings.GetLastGamePath();
        if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
        {
            return savedPath;
        }

        var result = detector.DetectGamePath();

        if (result.SelectedPath != null)
        {
            settings.SetLastGamePath(result.SelectedPath);
            return result.SelectedPath;
        }

        return null;
    }

    public string GenerateOutputPath(PrefabDescriptor descriptor, bool includeExtension = true)
    {
        var ext = includeExtension ? ".glb" : "";
        return descriptor.Type switch
        {
            PrefabType.Unit => $"Assets/Resources/units/{descriptor.Category}/{descriptor.Name}{ext}",
            PrefabType.MapObject => $"Assets/Resources/objects/{descriptor.Category}/{descriptor.Name}{ext}",
            PrefabType.GameObject => $"Assets/GameObject/{descriptor.Name}{ext}",
            _ => $"Assets/{descriptor.Name.Replace("/", "_")}{ext}"
        };
    }

    public string GenerateTexturePath(string textureName, bool includeExtension = true)
    {
        var ext = includeExtension ? ".png" : "";
        return $"Assets/Texture2D/{textureName}{ext}";
    }

    public ExtractionResult ExtractPrefab(string name, string? manualGamePath = null)
    {
        var result = new ExtractionResult
        {
            PrefabName = name,
            Success = false
        };

        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                result.ErrorMessage = "Could not resolve game path. Use --game-path to specify manually.";
                return result;
            }

            var version = InitializeVersionManagement(gamePath);

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            var glbExporter = new GlbExporter(_outputPath, DeduplicationService, ManifestService);

            var prefabData = extractor.ExtractPrefab(name);

            var unitPrefabs = extractor.ListPrefabs(PrefabType.Unit);
            var descriptor = unitPrefabs.FirstOrDefault(p => p.Name == name);

            if (descriptor == null)
            {
                var mapObjectPrefabs = extractor.ListPrefabs(PrefabType.MapObject);
                descriptor = mapObjectPrefabs.FirstOrDefault(p => $"{p.Category}/{p.Name}" == name || p.Name == name);
            }

            string relativePath;
            if (descriptor != null)
            {
                relativePath = GenerateOutputPath(descriptor, includeExtension: false);
            }
            else
            {
                relativePath = $"Assets/{name.Replace("/", "_")}";
            }

            var glbPath = glbExporter.Export(prefabData, relativePath, version);

            ManifestService.SaveManifest();

            result.Success = true;
            result.OutputPath = glbPath ?? Path.Combine(_outputPath, relativePath + ".glb");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
            {
                result.ErrorMessage += $" (Inner: {ex.InnerException.Message})";
            }
        }

        return result;
    }

    public BatchExtractionResult ExtractAllUnits(string? manualGamePath = null, bool force = false)
    {
        return ExtractPrefabBatch(
            manualGamePath,
            PrefabType.Unit,
            (extractor, descriptor) => descriptor.Name,
            triggerPromotion: true,
            force: force
        );
    }

    public BatchExtractionResult ExtractAllMapObjects(string? manualGamePath = null, bool force = false)
    {
        return ExtractPrefabBatch(
            manualGamePath,
            PrefabType.MapObject,
            (extractor, descriptor) => $"{descriptor.Category}/{descriptor.Name}",
            triggerPromotion: true,
            force: force
        );
    }

    public BatchExtractionResult ExtractAllGameObjects(string? manualGamePath = null, bool force = false)
    {
        var batchResult = new BatchExtractionResult();

        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                batchResult.Results.Add(new ExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Could not resolve game path"
                });
                return batchResult;
            }

            var version = ManifestManager.DetectGameVersion(gamePath);
            if (!force && ManifestService.IsVersionExtracted(version))
            {
                _logger.LogInformation(
                    "Version {Version} already extracted. Use --force to re-extract",
                    version);
                return batchResult;
            }

            InitializeVersionManagement(gamePath);
            _logger.LogInformation("Version: {Version}", version);

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            var glbExporter = new GlbExporter(_outputPath, DeduplicationService, ManifestService);

            batchResult.TotalCount = KnownGameObjects.Length;

            Console.WriteLine("Extracting GameObjects...");
            foreach (var gameObjectName in KnownGameObjects)
            {
                try
                {
                    var prefabData = extractor.ExtractGameObject(gameObjectName);
                    var descriptor = new PrefabDescriptor
                    {
                        Name = gameObjectName,
                        Type = PrefabType.GameObject,
                        Category = string.Empty
                    };

                    var glbPath = glbExporter.Export(prefabData,
                        GenerateOutputPath(descriptor, includeExtension: false),
                        version);

                    batchResult.Results.Add(new ExtractionResult
                    {
                        PrefabName = gameObjectName,
                        Success = true,
                        OutputPath = glbPath ?? Path.Combine(_outputPath,
                            GenerateOutputPath(descriptor, includeExtension: false) + ".glb")
                    });
                    batchResult.SuccessCount++;
                }
                catch (Exception ex)
                {
                    batchResult.Results.Add(new ExtractionResult
                    {
                        PrefabName = gameObjectName,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                    batchResult.FailedCount++;
                    batchResult.FailedItems.Add(gameObjectName);
                }
            }

            ManifestService.SaveManifest();

            if (PromotionService.IsPromotionNeeded())
            {
                try
                {
                    _logger.LogInformation("Running asset promotion");
                    PromotionService.PromoteAssetsAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Asset promotion failed");
                }
            }
        }
        catch (Exception ex)
        {
            batchResult.Results.Add(new ExtractionResult
            {
                Success = false,
                ErrorMessage = $"Batch extraction failed: {ex.GetType().Name}: {ex.Message}"
            });
        }

        return batchResult;
    }

    private static readonly string[] KnownGameObjects = new[]
    {
        "PLATFORM + BACK"
    };

    public static bool IsKnownGameObject(string name)
    {
        return KnownGameObjects.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private void ExtractAllGlbCore(
        AssetExtractorService extractor,
        GlbExporter glbExporter,
        string version,
        BatchExtractionResult batchResult)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        var gameObjectsStopwatch = Stopwatch.StartNew();
        batchResult.TotalCount += KnownGameObjects.Length;

        Console.WriteLine("Extracting GameObjects...");
        foreach (var gameObjectName in KnownGameObjects)
        {
            try
            {
                var prefabData = extractor.ExtractGameObject(gameObjectName);
                var descriptor = new PrefabDescriptor
                {
                    Name = gameObjectName,
                    Type = PrefabType.GameObject,
                    Category = string.Empty
                };

                var glbPath = glbExporter.Export(prefabData,
                    GenerateOutputPath(descriptor, includeExtension: false),
                    version);

                batchResult.Results.Add(new ExtractionResult
                {
                    PrefabName = gameObjectName,
                    Success = true,
                    OutputPath = glbPath ?? Path.Combine(_outputPath,
                        GenerateOutputPath(descriptor, includeExtension: false) + ".glb")
                });
                batchResult.SuccessCount++;
            }
            catch (Exception ex)
            {
                batchResult.Results.Add(new ExtractionResult
                {
                    PrefabName = gameObjectName,
                    Success = false,
                    ErrorMessage = ex.Message
                });
                batchResult.FailedCount++;
                batchResult.FailedItems.Add(gameObjectName);
            }
        }
        gameObjectsStopwatch.Stop();
        Console.WriteLine($"GameObjects Extraction finished in {gameObjectsStopwatch.Elapsed.TotalSeconds:F1}s");

        extractor.ClearMaterialCaches();

        var unitsStopwatch = Stopwatch.StartNew();
        var unitDescriptors = extractor.ListPrefabs(PrefabType.Unit);
        batchResult.TotalCount += unitDescriptors.Count;

        int unitSuccessCount = 0;
        int unitFailedCount = 0;
        var unitResults = new ConcurrentBag<ExtractionResult>();
        var unitFailedItems = new ConcurrentBag<string>();

        Console.WriteLine("Extracting Units...");
        int unitProcessedCount = 0;
        using (var unitProgressBar = new ProgressBar("Units", unitDescriptors.Count, OnProgress))
        {
            Parallel.ForEach(unitDescriptors, parallelOptions, descriptor =>
            {
                try
                {
                    var prefabData = extractor.ExtractPrefab(descriptor.Name);
                    var glbPath = glbExporter.Export(prefabData,
                        GenerateOutputPath(descriptor, includeExtension: false),
                        version);

                    var itemResult = new ExtractionResult
                    {
                        PrefabName = descriptor.Name,
                        Success = true,
                        OutputPath = glbPath ?? Path.Combine(_outputPath,
                            GenerateOutputPath(descriptor, includeExtension: false) + ".glb")
                    };

                    unitResults.Add(itemResult);
                    Interlocked.Increment(ref unitSuccessCount);
                    unitProgressBar.Update(Interlocked.Increment(ref unitProcessedCount), descriptor.Name);
                }
                catch (Exception ex)
                {
                    unitResults.Add(new ExtractionResult
                    {
                        PrefabName = descriptor.Name,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                    Interlocked.Increment(ref unitFailedCount);
                    unitFailedItems.Add(descriptor.Name);
                    unitProgressBar.Update(Interlocked.Increment(ref unitProcessedCount), descriptor.Name);
                }
            });
            unitProgressBar.Complete();
        }

        batchResult.Results.AddRange(unitResults);
        batchResult.SuccessCount += unitSuccessCount;
        batchResult.FailedCount += unitFailedCount;
        batchResult.FailedItems.AddRange(unitFailedItems);
        unitsStopwatch.Stop();
        Console.WriteLine($"Units Extraction finished in {unitsStopwatch.Elapsed.TotalSeconds:F1}s");

        var mapObjectsStopwatch = Stopwatch.StartNew();
        var mapObjectDescriptors = extractor.ListPrefabs(PrefabType.MapObject);
        batchResult.TotalCount += mapObjectDescriptors.Count;

        int mapObjectSuccessCount = 0;
        int mapObjectFailedCount = 0;
        var mapObjectResults = new ConcurrentBag<ExtractionResult>();
        var mapObjectFailedItems = new ConcurrentBag<string>();

        Console.WriteLine("Extracting Map Objects...");
        int mapObjectProcessedCount = 0;
        using (var mapObjectProgressBar = new ProgressBar("Map Objects", mapObjectDescriptors.Count, OnProgress))
        {
            Parallel.ForEach(mapObjectDescriptors, parallelOptions, descriptor =>
            {
                string prefabName = $"{descriptor.Category}/{descriptor.Name}";
                try
                {
                    var prefabData = extractor.ExtractPrefab(prefabName);
                    var glbPath = glbExporter.Export(prefabData,
                        GenerateOutputPath(descriptor, includeExtension: false),
                        version);

                    var itemResult = new ExtractionResult
                    {
                        PrefabName = prefabName,
                        Success = true,
                        OutputPath = glbPath ?? Path.Combine(_outputPath,
                            GenerateOutputPath(descriptor, includeExtension: false) + ".glb")
                    };

                    mapObjectResults.Add(itemResult);
                    Interlocked.Increment(ref mapObjectSuccessCount);
                    mapObjectProgressBar.Update(Interlocked.Increment(ref mapObjectProcessedCount), prefabName);
                }
                catch (Exception ex)
                {
                    mapObjectResults.Add(new ExtractionResult
                    {
                        PrefabName = prefabName,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                    Interlocked.Increment(ref mapObjectFailedCount);
                    mapObjectFailedItems.Add(prefabName);
                    mapObjectProgressBar.Update(Interlocked.Increment(ref mapObjectProcessedCount), prefabName);
                }
            });
            mapObjectProgressBar.Complete();
        }

        batchResult.Results.AddRange(mapObjectResults);
        batchResult.SuccessCount += mapObjectSuccessCount;
        batchResult.FailedCount += mapObjectFailedCount;
        batchResult.FailedItems.AddRange(mapObjectFailedItems);
        mapObjectsStopwatch.Stop();
        Console.WriteLine($"Map Objects Extraction finished in {mapObjectsStopwatch.Elapsed.TotalSeconds:F1}s");
    }

    public BatchExtractionResult ExtractAllGlb(string? manualGamePath = null, bool force = false)
    {
        var batchResult = new BatchExtractionResult();

        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                batchResult.Results.Add(new ExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Could not resolve game path"
                });
                return batchResult;
            }

            var version = ManifestManager.DetectGameVersion(gamePath);
            if (!force && ManifestService.IsVersionExtracted(version))
            {
                _logger.LogInformation(
                    "Version {Version} already extracted. Use --force to re-extract",
                    version);
                return batchResult;
            }

            InitializeVersionManagement(gamePath);
            _logger.LogInformation("Version: {Version}", version);

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            var glbExporter = new GlbExporter(_outputPath, DeduplicationService, ManifestService);

            ExtractAllGlbCore(extractor, glbExporter, version, batchResult);

            ManifestService.SaveManifest();

            if (PromotionService.IsPromotionNeeded())
            {
                try
                {
                    _logger.LogInformation("Running asset promotion");
                    PromotionService.PromoteAssetsAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Asset promotion failed");
                }
            }
        }
        catch (Exception ex)
        {
            batchResult.Results.Add(new ExtractionResult
            {
                Success = false,
                ErrorMessage = $"Batch extraction failed: {ex.GetType().Name}: {ex.Message}"
            });
        }

        return batchResult;
    }

    public BatchExtractionResult ExtractEverything(string? manualGamePath = null, bool force = false)
    {
        var batchResult = new BatchExtractionResult();

        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                batchResult.Results.Add(new ExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Could not resolve game path"
                });
                return batchResult;
            }

            var version = ManifestManager.DetectGameVersion(gamePath);
            if (!force && ManifestService.IsVersionExtracted(version))
            {
                _logger.LogInformation(
                    "Version {Version} already extracted. Use --force to re-extract",
                    version);
                return batchResult;
            }

            InitializeVersionManagement(gamePath);
            _logger.LogInformation("Version: {Version}", version);

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            var glbExporter = new GlbExporter(_outputPath, DeduplicationService, ManifestService);

            var texturesStopwatch = Stopwatch.StartNew();
            _logger.LogInformation(
                "Starting standalone texture extraction for version: {Version}",
                version);
            using var textureExtractor = new StandaloneTextureExtractor(gamePath, _outputPath, CreateLogger<StandaloneTextureExtractor>(), _loggerFactory);
            textureExtractor.OnProgress = OnProgress;
            textureExtractor.ExtractTexturesVersioned(version, ManifestService);
            texturesStopwatch.Stop();
            Console.WriteLine($"Image Extraction finished in {texturesStopwatch.Elapsed.TotalSeconds:F1}s");

            ExtractAllGlbCore(extractor, glbExporter, version, batchResult);

            ManifestService.SaveManifest();

            if (PromotionService.IsPromotionNeeded())
            {
                try
                {
                    var promotionStopwatch = Stopwatch.StartNew();
                    _logger.LogInformation("=== Running asset promotion ===");
                    PromotionService.PromoteAssetsAsync().GetAwaiter().GetResult();
                    promotionStopwatch.Stop();
                    Console.WriteLine($"Version Promotion finished in {promotionStopwatch.Elapsed.TotalSeconds:F1}s");
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Asset promotion failed");
                }
            }
        }
        catch (Exception ex)
        {
            batchResult.Results.Add(new ExtractionResult
            {
                Success = false,
                ErrorMessage = $"Batch extraction failed: {ex.GetType().Name}: {ex.Message}"
            });
        }

        return batchResult;
    }

    public BatchExtractionResult ExtractTexturesOnly(string? manualGamePath = null, bool force = false)
    {
        var batchResult = new BatchExtractionResult();

        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                batchResult.Results.Add(new ExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Could not resolve game path"
                });
                return batchResult;
            }

            var version = ManifestManager.DetectGameVersion(gamePath);
            if (!force && ManifestService.IsVersionExtracted(version))
            {
                _logger.LogInformation(
                    "Version {Version} already extracted. Use --force to re-extract",
                    version);
                return batchResult;
            }

            InitializeVersionManagement(gamePath);
            _logger.LogInformation(
                "Extracting textures for version: {Version}",
                version);

            var texturesStopwatch = Stopwatch.StartNew();
            using var textureExtractor = new StandaloneTextureExtractor(gamePath, _outputPath, CreateLogger<StandaloneTextureExtractor>(), _loggerFactory);
            textureExtractor.OnProgress = OnProgress;
            var stats = textureExtractor.ExtractTexturesVersioned(version, ManifestService);
            texturesStopwatch.Stop();
            Console.WriteLine($"Image Extraction finished in {texturesStopwatch.Elapsed.TotalSeconds:F1}s");

            batchResult.TotalCount = stats.TotalCount;
            batchResult.SuccessCount = stats.SuccessCount;
            batchResult.FailedCount = stats.FailedCount;

            ManifestService.SaveManifest();

            if (PromotionService.IsPromotionNeeded())
            {
                try
                {
                    var promotionStopwatch = Stopwatch.StartNew();
                    _logger.LogInformation("=== Running asset promotion ===");
                    PromotionService.PromoteAssetsAsync().GetAwaiter().GetResult();
                    promotionStopwatch.Stop();
                    Console.WriteLine($"Version Promotion finished in {promotionStopwatch.Elapsed.TotalSeconds:F1}s");
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Asset promotion failed");
                }
            }
        }
        catch (Exception ex)
        {
            batchResult.Results.Add(new ExtractionResult
            {
                Success = false,
                ErrorMessage = $"Texture extraction failed: {ex.GetType().Name}: {ex.Message}"
            });
            if (ex.InnerException != null)
            {
                _logger.LogError(
                    ex.InnerException,
                    "Inner exception details");
            }
        }

        return batchResult;
    }

    public async Task<PromotionService.PromotionResult> PromoteAssetsAsync()
    {
        return await PromotionService.PromoteAssetsAsync();
    }

    public List<string> ListUnits(string? manualGamePath = null)
    {
        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                return new List<string>();
            }

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            return extractor.ListUnits();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list units");
            return new List<string>();
        }
    }

    public List<string> ListMapObjects(string? manualGamePath = null)
    {
        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                return new List<string>();
            }

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            return extractor.ListMapObjects();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list map objects");
            return new List<string>();
        }
    }

    public List<string> ListAllPrefabs(string? manualGamePath = null)
    {
        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                return new List<string>();
            }

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            return extractor.ListAllPrefabNames();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list prefabs");
            return new List<string>();
        }
    }

    public List<string> ListResourcePaths(string? manualGamePath = null, string? pattern = null)
    {
        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                return new List<string>();
            }

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            return extractor.ListResourcePaths(pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list resource paths");
            return new List<string>();
        }
    }

    public ExtractionResult ExtractGameObject(string name, string? manualGamePath = null)
    {
        var result = new ExtractionResult
        {
            PrefabName = name,
            Success = false
        };

        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                result.ErrorMessage = "Could not resolve game path. Use --game-path to specify manually.";
                return result;
            }

            var version = InitializeVersionManagement(gamePath);

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            var prefabData = extractor.ExtractGameObject(name);

            var descriptor = new PrefabDescriptor
            {
                Name = name,
                Type = PrefabType.GameObject,
                Category = string.Empty
            };

            var glbExporter = new GlbExporter(_outputPath, DeduplicationService, ManifestService);
            var textureExporter = new TextureExporter(_outputPath, ManifestService);

            string relativePath = GenerateOutputPath(descriptor, includeExtension: false);

            var glbPath = glbExporter.Export(prefabData, relativePath, version);
            result.OutputPath = glbPath ?? Path.Combine(_outputPath, relativePath + ".glb");

            foreach (var textureData in prefabData.Textures)
            {
                if (result.TexturePaths.Any(p => p.Contains(textureData.Name)))
                    continue;

                var texturePath = GenerateTexturePath(textureData.Name, includeExtension: false);
                var exportedPath = textureExporter.ExportTexture(textureData, texturePath, version);
                if (exportedPath != null)
                {
                    result.TexturePaths.Add(exportedPath);
                }
            }

            ManifestService.SaveManifest();

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
            {
                result.ErrorMessage += $" (Inner: {ex.InnerException.Message})";
            }
        }

        return result;
    }

    public void Debug(string nameOrTerm, string? manualGamePath = null)
    {
        var gamePath = ResolveGamePath(manualGamePath);
        if (gamePath == null)
        {
            _logger.LogError("Could not resolve game path");
            return;
        }

        using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
        extractor.Debug(nameOrTerm);
    }

    private BatchExtractionResult ExtractPrefabBatch(
        string? manualGamePath,
        PrefabType type,
        Func<AssetExtractorService, PrefabDescriptor, string> getPrefabName,
        bool triggerPromotion,
        bool force = false)
    {
        var batchResult = new BatchExtractionResult();

        try
        {
            var gamePath = ResolveGamePath(manualGamePath);
            if (gamePath == null)
            {
                _logger.LogError("Could not resolve game path. Use --game-path to specify manually");
                batchResult.Results.Add(new ExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Could not resolve game path. Use --game-path to specify manually."
                });
                return batchResult;
            }

            var version = ManifestManager.DetectGameVersion(gamePath);
            if (!force && ManifestService.IsVersionExtracted(version))
            {
                _logger.LogInformation(
                    "Version {Version} already extracted. Use --force to re-extract",
                    version);
                return batchResult;
            }

            InitializeVersionManagement(gamePath);
            _logger.LogInformation("Version: {Version}", version);

            using var extractor = new AssetExtractorService(gamePath, CreateLogger<AssetExtractorService>(), _loggerFactory);
            var glbExporter = new GlbExporter(_outputPath, DeduplicationService, ManifestService);

            var prefabs = extractor.ListPrefabs(type);
            batchResult.TotalCount = prefabs.Count;

            var stopwatch = Stopwatch.StartNew();
            int successCount = 0;
            int failedCount = 0;
            var results = new ConcurrentBag<ExtractionResult>();
            var failedItems = new ConcurrentBag<string>();

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            string typeName = type == PrefabType.Unit ? "Units" : "Map Objects";
            _logger.LogInformation("Extracting {TypeName}", typeName);
            int processedCount = 0;
            using var progressBar = new ProgressBar(typeName, prefabs.Count, OnProgress);

            Parallel.ForEach(prefabs, parallelOptions, descriptor =>
            {
                try
                {
                    string prefabName = getPrefabName(extractor, descriptor);
                    var prefabData = extractor.ExtractPrefab(prefabName);

                    var glbPath = glbExporter.Export(prefabData,
                        GenerateOutputPath(descriptor, includeExtension: false),
                        version);

                    var itemResult = new ExtractionResult
                    {
                        PrefabName = prefabName,
                        Success = true,
                        OutputPath = glbPath ?? Path.Combine(_outputPath,
                            GenerateOutputPath(descriptor, includeExtension: false) + ".glb")
                    };

                    results.Add(itemResult);
                    Interlocked.Increment(ref successCount);

                    int processed = Interlocked.Increment(ref processedCount);
                    progressBar.Update(processed, prefabName);
                }
                catch (Exception ex)
                {
                    string prefabName = getPrefabName(extractor, descriptor);
                    var errorResult = new ExtractionResult
                    {
                        PrefabName = prefabName,
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                    results.Add(errorResult);
                    Interlocked.Increment(ref failedCount);
                    failedItems.Add(prefabName);

                    int processed = Interlocked.Increment(ref processedCount);
                    progressBar.Update(processed, prefabName);
                }
            });

            progressBar.Complete();

            batchResult.Results.AddRange(results);
            batchResult.SuccessCount = successCount;
            batchResult.FailedCount = failedCount;
            batchResult.FailedItems.AddRange(failedItems);

            stopwatch.Stop();
            Console.WriteLine($"[TIMING] {typeName} Extraction: {stopwatch.Elapsed.TotalSeconds:F1}s");

            ManifestService.SaveManifest();

            if (triggerPromotion && PromotionService.IsPromotionNeeded())
            {
                try
                {
                    _logger.LogInformation("=== Running asset promotion ===");
                    PromotionService.PromoteAssetsAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Asset promotion failed");
                }
            }
        }
        catch (Exception ex)
        {
            batchResult.Results.Add(new ExtractionResult
            {
                Success = false,
                ErrorMessage = $"Batch extraction failed: {ex.GetType().Name}: {ex.Message}"
            });
        }

        return batchResult;
    }
}
