#nullable enable
using System.Text.RegularExpressions;
using AssetExtractor.Models;
using AssetExtractor.Export;
using AssetExtractor.Pipeline;
using AssetExtractor.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

public class StandaloneTextureExtractor : IDisposable
{
    // Remove a name from here if any page starts referencing it.
    private static readonly Regex IconsEngineInternalPrefix = new Regex(
        @"^(arenas|background_buildings|buffs|campaignicons|dialogue|dialogue_unit_portraits|general_icons|guide_icons|observer|rank|rank_insara|rewards_icons|skins)/",
        RegexOptions.Compiled);

    // Names that resolve to the flat Texture2D/ dump via TexturePathResolver fallback.
    // Add here when a new literal reference appears in a frontend or backend source.
    private static readonly Regex Texture2DKeepPattern = new Regex(
        @"^(" +
            @"icon_difficulty_\d+" +
            @"|Icon_Stats_Mana" +
            @"|Icon_LawsPoint|Frame_Law_Back|LevelPoint \(1\)" +
            @"|Frame_LawLevel(_Loced)? \(1\)" +
            @"|Scroll_(Center|Left|Right)" +
            @"|Button_(Item_Delete|LevelUp)" +
        @")$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<StandaloneTextureExtractor> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly string _assetPath;
    private readonly string _outputPath;
    private UnityReader.UnityScene? _scene;
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
    }

    public ExtractionStats ExtractTexturesVersioned(string version, ManifestManager manifestService)
    {
        var stats = new ExtractionStats();
        try
        {
            _logger.LogInformation("Starting standalone texture extraction for version: {Version}", version);

            _scene = UnityReader.UnityScene.Load(new[] { _assetPath });
            _logger.LogInformation("Loaded {FileCount} files / {ObjectCount:N0} objects", _scene.FileCount, _scene.ObjectCount);

            var allTextures = _scene.EnumerateTexture2Ds()
                .Where(t => t.Width > 0 && t.Height > 0)
                .Select(t => (Texture: t, RelativePath: ComputeRelativePath(t)))
                .Where(x => IsAllowedTexturePath(x.RelativePath))
                .ToList();

            int totalCandidates = _scene.EnumerateTexture2Ds().Count(t => t.Width > 0 && t.Height > 0);
            stats.TotalCount = totalCandidates;
            stats.SkippedCount = totalCandidates - allTextures.Count;
            _logger.LogInformation("Filtered textures: {ToExtract} to extract, {Skipped} filtered out",
                allTextures.Count, stats.SkippedCount);

            var textureExporter = new TextureExporter(_outputPath, manifestService, _loggerFactory?.CreateLogger<TextureExporter>());
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            int successCount = 0, failedCount = 0, processedCount = 0;
            Console.WriteLine("Extracting Textures...");
            using var progressBar = new ProgressBar("Textures", allTextures.Count, OnProgress);

            Parallel.For(0, allTextures.Count, parallelOptions, index =>
            {
                var (texture, relativePath) = allTextures[index];
                try
                {
                    int processed = Interlocked.Increment(ref processedCount);
                    if (processed % 10 == 0 || processed == 1 || processed == allTextures.Count)
                        progressBar.Update(processed, texture.Name);

                    var exportedPath = textureExporter.ExportTexture(texture, relativePath, version);
                    if (exportedPath != null) Interlocked.Increment(ref successCount);
                    else Interlocked.Increment(ref failedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract texture at index {Index}", index);
                    Interlocked.Increment(ref failedCount);
                }
            });
            progressBar.Complete();

            stats.SuccessCount = successCount;
            stats.FailedCount = failedCount;

            _logger.LogInformation("=== Texture Extraction Complete ===");
            _logger.LogInformation("  total: {Total}, success: {Success}, failed: {Failed}, filtered: {Skipped}",
                stats.TotalCount, stats.SuccessCount, stats.FailedCount, stats.SkippedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Texture extraction failed");
            throw;
        }
        return stats;
    }

    private static string ComputeRelativePath(UnityReader.UnityTexture texture)
    {
        var resourcePath = texture.ResourcePath;

        // Resource-key wins over m_Name because EA Texture2Ds sometimes use camelCase
        // m_Name (e.g. "starDust_64") while the resource-key is lowercase
        // ("icons/resources/stardust_64"), and we need layout case-stable across builds.
        if (!string.IsNullOrEmpty(resourcePath))
        {
            return $"Assets/Resources/{SanitizeRelativePath(resourcePath)}";
        }

        return $"Assets/Texture2D/{SanitizeFileName(texture.Name)}";
    }

    private static string SanitizeRelativePath(string resourcePath)
    {
        // Per-segment so '/' delimiters survive.
        var segments = resourcePath.Split('/');
        for (int i = 0; i < segments.Length; i++)
            segments[i] = SanitizeFileName(segments[i]);
        return string.Join("/", segments);
    }

    // Allow-list mirrors the historical AR-pipeline output layout (v1.0.3 baseline, 0.80.12).
    // Anything outside the three branches below is engine-internal and was not part of the
    // baseline standalone export.
    private static bool IsAllowedTexturePath(string relativePath)
    {
        string lower = relativePath.ToLowerInvariant();

        if (lower.StartsWith("assets/resources/icons/", StringComparison.Ordinal))
        {
            string after = lower.Substring("assets/resources/icons/".Length);
            // Direct icons/foo.png entries (no subdirectory) were not part of the baseline.
            if (!after.Contains('/')) return false;

            if (IconsEngineInternalPrefix.IsMatch(after)) return false;

            return true;
        }

        if (lower.StartsWith("assets/resources/objects/", StringComparison.Ordinal))
        {
            string after = lower.Substring("assets/resources/objects/".Length);

            bool isAllowedTopLevel =
                after.StartsWith("artifact/", StringComparison.Ordinal) ||
                after.StartsWith("barracks/", StringComparison.Ordinal) ||
                after.StartsWith("interactive/", StringComparison.Ordinal) ||
                after.StartsWith("resource/", StringComparison.Ordinal);
            if (!isAllowedTopLevel) return false;

            // /models/ carries mesh-pack textures already covered by per-prefab GLB extraction.
            if (after.StartsWith("artifact/", StringComparison.Ordinal) ||
                after.StartsWith("interactive/", StringComparison.Ordinal) ||
                after.StartsWith("resource/", StringComparison.Ordinal))
            {
                if (after.Contains("/models/")) return false;
            }

            // barracks/{faction}_barracks/ duplicates icons already in the building-tier subdirs.
            if (after.StartsWith("barracks/", StringComparison.Ordinal))
            {
                string afterBarracks = after.Substring("barracks/".Length);
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

        if (lower.StartsWith("assets/texture2d/", StringComparison.Ordinal))
        {
            string fileName = Path.GetFileNameWithoutExtension(relativePath);
            return Texture2DKeepPattern.IsMatch(fileName);
        }

        return false;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "UnknownTexture";

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid) name = name.Replace(c, '_');
        if (name.Length > 200) name = name.Substring(0, 200);
        return name;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _scene?.Dispose();
        _scene = null;
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
