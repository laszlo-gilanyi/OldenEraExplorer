#nullable enable
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_28;  // ITexture2D
using AssetRipper.SourceGenerated.Classes.ClassID_89;  // ICubemap
using AssetRipper.SourceGenerated.Classes.ClassID_147; // IResourceManager
using AssetRipper.SourceGenerated.Extensions;          // TryGetAsset extension
using AssetExtractor.Models;
using AssetExtractor.Export;
using AssetExtractor.Pipeline;
using AssetExtractor.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AssetExtractor.Extraction;

public class StandaloneTextureExtractor : IDisposable
{
    private readonly ILogger<StandaloneTextureExtractor> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly string _assetPath;
    private readonly string _outputPath;
    private GameBundle? _gameBundle;
    private readonly bool _externalGameBundle;  // Track if GameBundle was provided externally
    private bool _disposed;

    public Action<ExtractionProgress>? OnProgress { get; set; }

    public StandaloneTextureExtractor(
        string assetPath,
        string outputPath,
        ILogger<StandaloneTextureExtractor>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<StandaloneTextureExtractor>.Instance;
        _loggerFactory = loggerFactory;
        _assetPath = assetPath;
        _outputPath = outputPath;
        _externalGameBundle = false;
    }

    public StandaloneTextureExtractor(
        string assetPath,
        string outputPath,
        GameBundle existingGameBundle,
        ILogger<StandaloneTextureExtractor>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<StandaloneTextureExtractor>.Instance;
        _loggerFactory = loggerFactory;
        _assetPath = assetPath;
        _outputPath = outputPath;
        _gameBundle = existingGameBundle ?? throw new ArgumentNullException(nameof(existingGameBundle));
        _externalGameBundle = true;
    }

    public ExtractionStats ExtractTexturesVersioned(
        string version,
        ManifestManager manifestService)
    {
        var stats = new ExtractionStats();

        try
        {
            _logger.LogInformation(
                "Starting standalone texture extraction for version: {Version}",
                version);

            InitializeGameBundle();

            // AssetRipper requires OriginalPath to be set for proper export paths
            SetOriginalPathsFromResourceManager();

            var textureExporter = new TextureExporter(_outputPath, manifestService, _loggerFactory?.CreateLogger<TextureExporter>());

            _logger.LogInformation("Collecting all Texture2D assets");
            var allTextures = CollectAllTextures();
            stats.TotalCount = allTextures.Count;

            _logger.LogInformation(
                "Found {TextureCount} Texture2D assets",
                allTextures.Count);

            _logger.LogInformation("Filtering textures");
            var texturesToExtract = allTextures
                .Where(t => t.Width_C28 > 0 && t.Height_C28 > 0)
                .Select(t => (Texture: t, RelativePath: ExtractTexturePath(t)))
                .Where(x => IsAllowedTexturePath(x.RelativePath))
                .ToList();

            int skippedCount = allTextures.Count - texturesToExtract.Count;
            _logger.LogInformation(
                "Filtered textures: {ToExtractCount} to extract, {SkippedCount} skipped (filtered)",
                texturesToExtract.Count,
                skippedCount);

            int successCount = 0;
            int failedCount = 0;
            int processedCount = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            _logger.LogInformation(
                "Using {ThreadCount} threads for texture extraction",
                parallelOptions.MaxDegreeOfParallelism);

            Console.WriteLine("Extracting Textures...");
            using var progressBar = new ProgressBar("Textures", texturesToExtract.Count, OnProgress);

            Parallel.For(0, texturesToExtract.Count, parallelOptions, index =>
            {
                var (texture, relativePath) = texturesToExtract[index];

                try
                {
                    int currentProcessed = Interlocked.Increment(ref processedCount);
                    var textureName = texture.OriginalName ?? texture.Name ?? $"Texture_{texture.PathID}";

                    if (currentProcessed % 10 == 0 || currentProcessed == 1 || currentProcessed == texturesToExtract.Count)
                    {
                        progressBar.Update(currentProcessed, textureName);
                    }

                    var exportedPath = textureExporter.ExportTexture(texture, relativePath, version);

                    if (exportedPath != null)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failedCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to extract texture at index {TextureIndex}",
                        index);
                    Interlocked.Increment(ref failedCount);
                }
            });

            progressBar.Complete();

            stats.SuccessCount = successCount;
            stats.FailedCount = failedCount;
            stats.SkippedCount = skippedCount;

            _logger.LogInformation("=== Texture Extraction Complete ===");
            _logger.LogInformation("Total textures found: {TotalCount}", stats.TotalCount);
            _logger.LogInformation("Successfully extracted: {SuccessCount}", stats.SuccessCount);
            _logger.LogInformation("Failed: {FailedCount}", stats.FailedCount);
            _logger.LogInformation("Skipped (filtered + invalid): {SkippedCount}", stats.SkippedCount);
            _logger.LogInformation("  - icons/ (filtered), objects/ (artifact, barracks, interactive, resource)");
            _logger.LogInformation(
                "Success rate: {SuccessRate:F2}%",
                stats.SuccessCount * 100.0 / Math.Max(1, stats.TotalCount - stats.SkippedCount));

            ExtractCubemaps(version, textureExporter, stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Texture extraction failed");
            throw;
        }

        return stats;
    }

    private void InitializeGameBundle()
    {
        if (_gameBundle != null)
        {
            if (_externalGameBundle)
            {
                _logger.LogInformation("Reusing existing GameBundle for texture extraction (skipping ~15s reload)");
            }
            return;
        }

        _logger.LogInformation("Initializing GameBundle for texture extraction");

        var assetFiles = CollectAssetFiles(_assetPath);
        _logger.LogInformation(
            "Found {AssetFileCount} asset files to load",
            assetFiles.Count);

        var assemblyManager = new BaseManager(_ => { });
        var assetFactory = new GameAssetFactory(assemblyManager);

        Console.WriteLine($"Loading game assets ({assetFiles.Count} files)...");

        using (var spinner = new SpinnerDisplay("Loading"))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _gameBundle = GameBundle.FromPaths(
                assetFiles,
                assetFactory,
                LocalFileSystem.Instance,
                null
            );
            sw.Stop();

            _logger.LogInformation(
                "GameBundle loaded in {LoadTimeMs}ms",
                sw.ElapsedMilliseconds);

            spinner.Complete($"Loaded in {sw.ElapsedMilliseconds / 1000.0:F1}s");
        }
        Console.WriteLine();
    }

    private void SetOriginalPathsFromResourceManager()
    {
        if (_gameBundle == null)
            return;

        int pathsSet = 0;

        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is IResourceManager resourceManager)
            {
                foreach (var kvp in resourceManager.Container)
                {
                    var referencedAsset = kvp.Value.TryGetAsset(resourceManager.Collection);
                    if (referencedAsset == null)
                        continue;

                    string resourcePath = $"Assets/Resources/{kvp.Key.String}".Replace('\\', '/');

                    if (referencedAsset.OriginalPath == null)
                    {
                        referencedAsset.OriginalPath = resourcePath;
                        pathsSet++;
                    }
                    else if (referencedAsset.OriginalPath.Length < resourcePath.Length)
                    {
                        referencedAsset.OriginalPath = resourcePath;
                    }
                }
            }
        }

        _logger.LogInformation(
            "Set OriginalPath on {AssetCount} assets from IResourceManager",
            pathsSet);
    }

    private List<ITexture2D> CollectAllTextures()
    {
        if (_gameBundle == null)
            throw new InvalidOperationException("GameBundle not initialized");

        var textures = new List<ITexture2D>();

        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is ITexture2D texture)
            {
                textures.Add(texture);
            }
        }

        return textures;
    }

    private static readonly HashSet<string> AllowedCubemapNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cold Sunset Equirect"
    };

    private void ExtractCubemaps(string version, TextureExporter textureExporter, ExtractionStats stats)
    {
        if (_gameBundle == null)
            return;

        _logger.LogInformation("Extracting cubemaps");

        var cubemaps = new List<ICubemap>();
        foreach (var asset in _gameBundle.FetchAssets())
        {
            if (asset is ICubemap cubemap)
            {
                var name = cubemap.Name ?? "";
                if (AllowedCubemapNames.Contains(name))
                {
                    cubemaps.Add(cubemap);
                }
            }
        }

        if (cubemaps.Count == 0)
        {
            _logger.LogInformation("No matching cubemaps found");
            return;
        }

        _logger.LogInformation("Found {CubemapCount} cubemaps to extract", cubemaps.Count);

        foreach (var cubemap in cubemaps)
        {
            var name = cubemap.Name ?? "UnknownCubemap";
            using (_logger.BeginScope("Cubemap: {CubemapName}", name))
            {
                try
                {
                    _logger.LogInformation("Extracting cubemap: {CubemapName}", name);

                var texture = cubemap as ITexture2D;
                if (texture == null)
                {
                    _logger.LogWarning("Cubemap is not ITexture2D: {CubemapName}", name);
                    continue;
                }

                int faceSize = texture.Width_C28;
                if (faceSize <= 0)
                {
                    _logger.LogWarning(
                        "Invalid cubemap size: {FaceSize} for {CubemapName}",
                        faceSize,
                        name);
                    continue;
                }

                // GetImageData() reads from shared streams - requires synchronization
                byte[] rawData;
                lock (TextureExporter.TextureConversionLock)
                {
                    rawData = texture.GetImageData();
                }
                if (rawData == null || rawData.Length == 0)
                {
                    _logger.LogWarning("No image data for cubemap: {CubemapName}", name);
                    continue;
                }

                var relativePath = $"Assets/Cubemap/{SanitizeFileName(name)}";
                var pngData = ConvertCubemapToPng(rawData, faceSize, texture.Format_C28E);

                if (pngData != null)
                {
                    var textureData = new TextureData
                    {
                        Name = name,
                        Width = faceSize * 2,
                        Height = faceSize,
                        ImageData = pngData,
                        Format = "PNG"
                    };

                    var exportedPath = textureExporter.ExportTexture(textureData, relativePath, version);
                    if (exportedPath != null)
                    {
                        _logger.LogInformation("Exported cubemap: {CubemapPath}", exportedPath);
                        stats.SuccessCount++;
                    }
                    else
                    {
                        stats.FailedCount++;
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to convert cubemap: {CubemapName}", name);
                    stats.FailedCount++;
                }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract cubemap");
                    stats.FailedCount++;
                }
            }
        }
    }

    // Unity uses left-handed coordinates, Three.js uses right-handed - Z axis must be flipped
    private byte[]? ConvertCubemapToPng(byte[] rawData, int faceSize, AssetRipper.SourceGenerated.Enums.TextureFormat format)
    {
        try
        {
            int totalPixels = 6 * faceSize * faceSize;
            int stripSize = totalPixels * 4; // RGBA
            byte[] stripData = new byte[stripSize];

            int bytesDecoded = DecodeTextureData(rawData, faceSize, faceSize * 6, format, stripData);
            if (bytesDecoded < 0)
            {
                _logger.LogWarning("Cubemap decoding failed for format: {TextureFormat}", format);
                return null;
            }

            using var stripImage = Image.LoadPixelData<Rgba32>(stripData, faceSize, faceSize * 6);

            var faces = new Image<Rgba32>[6];
            for (int i = 0; i < 6; i++)
            {
                faces[i] = stripImage.Clone(ctx => ctx.Crop(new Rectangle(0, i * faceSize, faceSize, faceSize)));
            }

            int outWidth = faceSize * 2;
            int outHeight = faceSize;
            using var equirect = new Image<Rgba32>(outWidth, outHeight);

            for (int y = 0; y < outHeight; y++)
            {
                double lat = (0.5 - (double)y / outHeight) * Math.PI;

                for (int x = 0; x < outWidth; x++)
                {
                    double lon = ((double)x / outWidth - 0.5) * 2 * Math.PI;

                    double dx = Math.Cos(lat) * Math.Sin(lon);
                    double dy = Math.Sin(lat);
                    double dz = Math.Cos(lat) * Math.Cos(lon);
                    double dzUnity = -dz;

                    double absx = Math.Abs(dx), absy = Math.Abs(dy), absz = Math.Abs(dzUnity);
                    int faceIdx;
                    double u, v;

                    if (absx >= absy && absx >= absz)
                    {
                        if (dx > 0) { faceIdx = 0; u = -dzUnity / absx; v = dy / absx; }
                        else { faceIdx = 1; u = dzUnity / absx; v = dy / absx; }
                    }
                    else if (absy >= absx && absy >= absz)
                    {
                        if (dy > 0) { faceIdx = 2; u = dx / absy; v = -dzUnity / absy; }
                        else { faceIdx = 3; u = dx / absy; v = dzUnity / absy; }
                    }
                    else
                    {
                        if (dzUnity > 0) { faceIdx = 4; u = dx / absz; v = dy / absz; }
                        else { faceIdx = 5; u = -dx / absz; v = dy / absz; }
                    }

                    int px = (int)(((u + 1) / 2) * (faceSize - 1));
                    int py = (int)(((1 - v) / 2) * (faceSize - 1));
                    px = Math.Clamp(px, 0, faceSize - 1);
                    py = Math.Clamp(py, 0, faceSize - 1);

                    equirect[x, y] = faces[faceIdx][px, py];
                }
            }

            foreach (var face in faces) face.Dispose();

            using var ms = new MemoryStream();
            equirect.SaveAsPng(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cubemap conversion failed");
            return null;
        }
    }

    private int DecodeTextureData(byte[] rawData, int width, int height, AssetRipper.SourceGenerated.Enums.TextureFormat format, byte[] rgbaData)
    {
        switch (format)
        {
            case AssetRipper.SourceGenerated.Enums.TextureFormat.RGBA32:
                return AssetRipper.TextureDecoder.Rgb.RgbConverter.Convert<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte,
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            case AssetRipper.SourceGenerated.Enums.TextureFormat.RGB24:
                return AssetRipper.TextureDecoder.Rgb.RgbConverter.Convert<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGB<byte>, byte,
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            case AssetRipper.SourceGenerated.Enums.TextureFormat.DXT1:
            case AssetRipper.SourceGenerated.Enums.TextureFormat.DXT1Crunched:
                return AssetRipper.TextureDecoder.Dxt.DxtDecoder.DecompressDXT1<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            case AssetRipper.SourceGenerated.Enums.TextureFormat.DXT5:
            case AssetRipper.SourceGenerated.Enums.TextureFormat.DXT5Crunched:
                return AssetRipper.TextureDecoder.Dxt.DxtDecoder.DecompressDXT5<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            case AssetRipper.SourceGenerated.Enums.TextureFormat.BC7:
                return AssetRipper.TextureDecoder.Bc.Bc7.Decompress<
                    AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    rawData, width, height, rgbaData);

            default:
                _logger.LogWarning("Unsupported cubemap format: {TextureFormat}", format);
                return -1;
        }
    }

    private static List<string> CollectAssetFiles(string assetPath)
    {
        var files = new List<string>();

        var resourcesPath = Path.Combine(assetPath, "resources.assets");
        if (File.Exists(resourcesPath))
            files.Add(resourcesPath);

        foreach (var file in Directory.GetFiles(assetPath, "sharedassets*.assets"))
        {
            files.Add(file);
        }

        foreach (var file in Directory.GetFiles(assetPath, "level*.assets"))
        {
            files.Add(file);
        }

        var globalPath = Path.Combine(assetPath, "globalgamemanagers");
        if (File.Exists(globalPath))
            files.Add(globalPath);

        var globalAssetsPath = Path.Combine(assetPath, "globalgamemanagers.assets");
        if (File.Exists(globalAssetsPath))
            files.Add(globalAssetsPath);

        foreach (var file in Directory.GetFiles(assetPath, "*.resS"))
        {
            files.Add(file);
        }

        foreach (var file in Directory.GetFiles(assetPath, "*.resource"))
        {
            files.Add(file);
        }

        return files;
    }

    private static bool IsAllowedTexturePath(string relativePath)
    {
        string lowerPath = relativePath.ToLowerInvariant();

        if (lowerPath.StartsWith("assets/resources/icons/", StringComparison.Ordinal))
        {
            string afterIcons = lowerPath.Substring("assets/resources/icons/".Length);

            if (!afterIcons.Contains('/'))
                return false;

            if (afterIcons.StartsWith("background_buildings/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("buffs/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("campaignicons/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("general_icons/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("guide_icons/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("observer/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("rank/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("rank_insara/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("rewards_icons/", StringComparison.Ordinal) ||
                afterIcons.StartsWith("skins/", StringComparison.Ordinal))
                return false;

            return true;
        }

        if (lowerPath.StartsWith("assets/resources/objects/", StringComparison.Ordinal))
        {
            string afterObjects = lowerPath.Substring("assets/resources/objects/".Length);

            bool isAllowedSubfolder =
                afterObjects.StartsWith("artifact/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("barracks/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("interactive/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("resource/", StringComparison.Ordinal);

            if (!isAllowedSubfolder)
                return false;

            if (afterObjects.StartsWith("artifact/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("interactive/", StringComparison.Ordinal) ||
                afterObjects.StartsWith("resource/", StringComparison.Ordinal))
            {
                if (afterObjects.Contains("/models/"))
                    return false;
            }

            if (afterObjects.StartsWith("barracks/", StringComparison.Ordinal))
            {
                string afterBarracks = afterObjects.Substring("barracks/".Length);
                int slashPos = afterBarracks.IndexOf('/');
                if (slashPos > 0)
                {
                    string subfolder = afterBarracks.Substring(0, slashPos);
                    if (subfolder.EndsWith("_barracks", StringComparison.Ordinal))
                        return false;
                }
            }

            return true;
        }

        if (lowerPath.StartsWith("assets/texture2d/", StringComparison.Ordinal))
        {
            string fileName = Path.GetFileNameWithoutExtension(relativePath).ToLowerInvariant();

            if (fileName == "unit_info_back" || fileName == "icon_lawspoint")
                return true;

            if (fileName.StartsWith("icon_difficulty_") &&
                !fileName.Contains("_mouse_over") &&
                !fileName.Contains("_selected"))
            {
                string suffix = fileName.Substring("icon_difficulty_".Length);
                if (suffix.Length == 1 && char.IsDigit(suffix[0]))
                {
                    int level = suffix[0] - '0';
                    if (level >= 0 && level <= 5)
                        return true;
                }
            }

            return false;
        }

        return false;
    }

    private static string ExtractTexturePath(ITexture2D texture)
    {
        var fileName = texture.OriginalName ?? texture.Name ?? $"UnknownTexture_{texture.PathID}";
        fileName = SanitizeFileName(fileName);

        var originalPath = texture.OriginalPath;
        if (!string.IsNullOrEmpty(originalPath))
        {
            int lastSlash = originalPath.LastIndexOf('/');
            if (lastSlash > 0)
            {
                string directory = originalPath.Substring(0, lastSlash);
                return $"{directory}/{fileName}";
            }
            else
            {
                return $"Assets/Resources/{fileName}";
            }
        }

        var fallbackDirectory = texture.GetBestDirectory();

        if (fallbackDirectory.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{fallbackDirectory}/{fileName}";
        }
        else
        {
            return $"Assets/{fallbackDirectory}/{fileName}";
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "UnknownTexture";

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            name = name.Replace(c, '_');
        }

        if (name.Length > 200)
            name = name.Substring(0, 200);

        return name;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (!_externalGameBundle)
        {
            _gameBundle?.Dispose();
        }

        _gameBundle = null;
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

public class ExtractionStats
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
}
