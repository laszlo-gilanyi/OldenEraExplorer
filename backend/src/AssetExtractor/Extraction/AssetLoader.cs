#nullable enable
using AssetExtractor.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Extraction;

public sealed class AssetLoader : IDisposable
{
    private readonly ILogger<AssetLoader> _logger;
    private readonly string _assetPath;
    private readonly UnityReader.UnityScene _unityScene;
    private readonly Dictionary<string, UnityReader.GameObject> _pathIdCache = new();
    private readonly Dictionary<string, UnityReader.GameObject> _prefabCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UnityReader.GameObject> _resourcePathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string Path, UnityReader.GameObject Prefab)>> _resourceNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UnityReader.UnityTexture> _textureResourcePathCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public string AssetPath => _assetPath;
    public IReadOnlyDictionary<string, UnityReader.GameObject> PrefabCache => _prefabCache;
    internal UnityReader.UnityScene Scene => _unityScene;

    public AssetLoader(string assetPath, ILogger<AssetLoader>? logger = null)
    {
        _logger = logger ?? NullLogger<AssetLoader>.Instance;
        _assetPath = assetPath;
        _logger.LogInformation("Initializing AssetLoader; asset path: {AssetPath}", assetPath);

        var assetFiles = CollectAssetFiles(assetPath);
        _logger.LogInformation("Found {AssetFileCount} asset files to load", assetFiles.Count);

        Console.WriteLine($"Loading game assets ({assetFiles.Count} files)...");
        using (var spinner = new SpinnerDisplay("Loading"))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _unityScene = UnityReader.UnityScene.Load(assetFiles);
            sw.Stop();
            spinner.Complete($"Loaded in {sw.ElapsedMilliseconds / 1000.0:F1}s");
        }
        Console.WriteLine();

        BuildCaches();
        _logger.LogInformation("PathID cache: {Count}", _pathIdCache.Count);
        _logger.LogInformation("Prefab cache: {Count}", _prefabCache.Count);
        _logger.LogInformation("Resource path cache: {Count}", _resourcePathCache.Count);
        _logger.LogInformation("Resource name cache: {Count}", _resourceNameCache.Count);

    }

    private void BuildCaches()
    {
        var prefabCandidates = new Dictionary<string, List<UnityReader.GameObject>>(StringComparer.OrdinalIgnoreCase);

        foreach (var go in _unityScene.EnumerateGameObjects())
        {
            if (go.PathId != 0)
            {
                var key = MakePathIdCacheKey(go.SourceFile, go.PathId);
                if (!_pathIdCache.ContainsKey(key)) _pathIdCache[key] = go;
            }

            var transform = go.Transform;
            if (transform == null) continue;
            if (transform.Parent != null) continue;

            var name = go.Name;
            if (string.IsNullOrEmpty(name)) continue;

            if (!prefabCandidates.TryGetValue(name, out var list))
            {
                list = new List<UnityReader.GameObject>();
                prefabCandidates[name] = list;
            }
            list.Add(go);
        }

        foreach (var t in _unityScene.EnumerateTexture2Ds())
        {
            var resourcePath = t.ResourcePath;
            if (!string.IsNullOrEmpty(resourcePath))
            {
                _textureResourcePathCache.TryAdd(resourcePath, t);
            }
        }

        // Mirror _prefabCache's Transform.Parent == null filter: HierarchyExtractor's
        // wrapper / inner-selection logic only works on hierarchy roots, so sub-node
        // ResourceManager entries (mesh leaves, particle anchors, etc.) must be skipped.
        foreach (var go in _unityScene.EnumerateGameObjects())
        {
            var resourcePath = go.ResourcePath;
            if (string.IsNullOrEmpty(resourcePath)) continue;
            if (go.Transform?.Parent != null) continue;

            _resourcePathCache.TryAdd(resourcePath, go);

            int lastSlash = resourcePath.LastIndexOf('/');
            string leafName = lastSlash >= 0 && lastSlash < resourcePath.Length - 1
                ? resourcePath.Substring(lastSlash + 1)
                : resourcePath;
            if (!_resourceNameCache.TryGetValue(leafName, out var bag))
            {
                bag = new List<(string, UnityReader.GameObject)>();
                _resourceNameCache[leafName] = bag;
            }
            bag.Add((resourcePath, go));
        }

        foreach (var (name, candidates) in prefabCandidates)
        {
            if (candidates.Count == 1)
            {
                _prefabCache[name] = candidates[0];
                continue;
            }

            UnityReader.GameObject? best = null;
            int bestScore = -1;
            foreach (var candidate in candidates)
            {
                int score = ScorePrefabCandidate(candidate);
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            if (best != null) _prefabCache[name] = best;
        }
    }

    private static int ScorePrefabCandidate(UnityReader.GameObject gameObject)
    {
        int score = 0;
        var transform = gameObject.Transform;
        if (transform == null) return score;

        foreach (var childTransform in transform.Children)
        {
            if (childTransform == null) continue;
            var childGO = childTransform.GameObject;
            var childName = childGO?.Name ?? string.Empty;

            if (childName.Contains("scale_roll", StringComparison.OrdinalIgnoreCase)) score += 1000;
            else if (childName.EndsWith("_roll", StringComparison.OrdinalIgnoreCase)) score += 1000;
            else if (childName.Contains("wrapper", StringComparison.OrdinalIgnoreCase)) score += 800;
            else if (childName.Contains("scale", StringComparison.OrdinalIgnoreCase)) score += 500;

            if (WrapperHeuristics.IsWrapperRotation(childTransform.LocalRotation))
            {
                score += 600;
            }
        }

        score += transform.Children.Count * 10;
        return score;
    }

    private static List<string> CollectAssetFiles(string assetPath)
    {
        var files = new List<string>();

        var resourcesPath = Path.Combine(assetPath, "resources.assets");
        if (File.Exists(resourcesPath)) files.Add(resourcesPath);

        foreach (var f in Directory.GetFiles(assetPath, "sharedassets*.assets")) files.Add(f);
        foreach (var f in Directory.GetFiles(assetPath, "level*.assets")) files.Add(f);

        var globalPath = Path.Combine(assetPath, "globalgamemanagers");
        if (File.Exists(globalPath)) files.Add(globalPath);
        var globalAssetsPath = Path.Combine(assetPath, "globalgamemanagers.assets");
        if (File.Exists(globalAssetsPath)) files.Add(globalAssetsPath);

        foreach (var f in Directory.GetFiles(assetPath, "*.resS")) files.Add(f);
        foreach (var f in Directory.GetFiles(assetPath, "*.resource")) files.Add(f);

        return files;
    }

    private static string MakePathIdCacheKey(string sourceFile, long pathId) => $"{sourceFile}:{pathId}";

    public List<string> ListResourcePaths(string? pattern = null)
    {
        var paths = _resourcePathCache.Keys.AsEnumerable();
        if (!string.IsNullOrEmpty(pattern))
        {
            paths = paths.Where(p => p.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
        return paths.OrderBy(p => p).ToList();
    }

    public UnityReader.GameObject? FindPrefabByNameWithResourcePaths(string prefabName, string? pathPrefix = null)
    {
        // _prefabCache is already wrapper-scored. The resource-name-cache can hold multiple
        // entries per name (deprecated, alt-path, 1prefabs duplicates), and a FirstOrDefault
        // on those bags would risk picking a non-wrapper variant that breaks downstream
        // wrapper / inner-selection in HierarchyExtractor.
        if (_prefabCache.TryGetValue(prefabName, out var prefabHit))
            return prefabHit;

        if (_resourceNameCache.TryGetValue(prefabName, out var candidates))
        {
            if (!string.IsNullOrEmpty(pathPrefix))
            {
                var match = candidates.FirstOrDefault(c => c.Path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));
                if (match.Prefab != null) return match.Prefab;
            }
            if (candidates.Count > 0) return candidates[0].Prefab;
        }
        return FindPrefabByName(prefabName);
    }

    public UnityReader.GameObject? FindPrefabByResourcePath(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath)) return null;
        resourcePath = resourcePath.Replace('\\', '/');

        if (_resourcePathCache.TryGetValue(resourcePath, out var prefab)) return prefab;

        if (!resourcePath.StartsWith("objects/", StringComparison.OrdinalIgnoreCase))
        {
            if (_resourcePathCache.TryGetValue($"objects/{resourcePath}", out prefab)) return prefab;
        }

        return null;
    }

    public UnityReader.GameObject? FindGameObjectByPathId(string sourceFile, long pathId)
    {
        var cacheKey = MakePathIdCacheKey(sourceFile, pathId);
        return _pathIdCache.TryGetValue(cacheKey, out var go) ? go : null;
    }

    public UnityReader.GameObject? FindPrefabByName(string prefabName)
    {
        if (_prefabCache.TryGetValue(prefabName, out var prefab)) return prefab;

        var variantInfo = HierarchySelector.ParseUnitVariant(prefabName);
        if (variantInfo.Kind != UnitVariantKind.Base)
        {
            if (_prefabCache.TryGetValue(variantInfo.BaseName, out prefab)) return prefab;
        }

        return null;
    }

    public List<string> ListAllPrefabNames() => _prefabCache.Keys.OrderBy(k => k).ToList();

    public void DebugPrefabHierarchy(string prefabName) { /* no-op post-cleanup */ }
    public void AnalyzeAssetStructure(string searchTerm) { /* no-op post-cleanup */ }

    public List<(string Name, UnityReader.UnityTexture Texture)> SearchTextures(string namePattern)
    {
        return _unityScene.EnumerateTexture2Ds()
            .Where(t => !string.IsNullOrEmpty(t.Name) && t.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase))
            .Select(t => (t.Name, t))
            .ToList();
    }

    public UnityReader.UnityTexture? FindTextureByResourcePath(string resourcePathPattern)
    {
        return _textureResourcePathCache.TryGetValue(resourcePathPattern, out var t) ? t : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _unityScene.Dispose();
        _disposed = true;
    }
}
