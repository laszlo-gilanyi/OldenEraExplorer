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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _unityScene = UnityReader.UnityScene.Load(assetFiles);
        sw.Stop();
        Console.WriteLine($"Loaded in {sw.ElapsedMilliseconds / 1000.0:F1}s");

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

    private void PrintHierarchyRecursive(UnityReader.GameObject gameObject, UnityReader.Transform transform, int depth)
    {
        var indent = new string(' ', depth * 2);

        var components = new List<string>();
        if (gameObject.TryGetComponent<UnityReader.SkinnedMeshRenderer>(out _)) components.Add("SkinnedMeshRenderer");
        if (gameObject.TryGetComponent<UnityReader.MeshRenderer>(out _)) components.Add("MeshRenderer");
        if (gameObject.TryGetComponent<UnityReader.MeshFilter>(out _)) components.Add("MeshFilter");
        if (gameObject.TryGetComponent<UnityReader.Animator>(out _)) components.Add("Animator");

        var componentStr = components.Count > 0 ? $" [{string.Join(", ", components)}]" : "";

        var scale = transform.LocalScale;
        var rot = transform.LocalRotation;
        var transformStr = "";

        if (Math.Abs(scale.X - 1) > 0.001f || Math.Abs(scale.Y - 1) > 0.001f || Math.Abs(scale.Z - 1) > 0.001f)
            transformStr += $" S({scale.X:F2},{scale.Y:F2},{scale.Z:F2})";

        if (Math.Abs(rot.X) > 0.001f || Math.Abs(rot.Y) > 0.001f || Math.Abs(rot.Z) > 0.001f || Math.Abs(rot.W - 1) > 0.001f)
            transformStr += $" R({rot.X:F2},{rot.Y:F2},{rot.Z:F2},{rot.W:F2})";

        _logger.LogInformation("{Indent}- {Name}{Components}{Transform}", indent, gameObject.Name, componentStr, transformStr);

        foreach (var childTransform in transform.Children)
        {
            if (childTransform == null) continue;
            var childGo = childTransform.GameObject;
            if (childGo == null) continue;
            PrintHierarchyRecursive(childGo, childTransform, depth + 1);
        }
    }

    public void Debug(string nameOrTerm)
    {
        if (nameOrTerm.StartsWith("#"))
        {
            _logger.LogWarning("Direct PathID lookup not supported. Use a name or search term.");
            return;
        }

        UnityReader.GameObject? prefab = null;
        string? foundBy = null;

        if (nameOrTerm.Contains('/'))
        {
            prefab = FindPrefabByResourcePath(nameOrTerm);
            if (prefab != null) foundBy = "resource path";
        }
        if (prefab == null)
        {
            prefab = FindPrefabByNameWithResourcePaths(nameOrTerm);
            if (prefab != null) foundBy = "resource name cache";
        }
        if (prefab == null)
        {
            prefab = FindPrefabByName(nameOrTerm);
            if (prefab != null) foundBy = "name cache";
        }

        if (prefab != null)
        {
            _logger.LogInformation($"Found '{prefab.Name}' via {foundBy} (PathID: {prefab.PathId})");
            InspectPrefab(prefab);
        }
        else
        {
            _logger.LogInformation($"No prefab match for '{nameOrTerm}'. Searching across asset types.");
            SearchAcrossTypes(nameOrTerm);
        }
    }

    private void InspectPrefab(UnityReader.GameObject prefab)
    {
        _logger.LogInformation($"=== {prefab.Name} ===");
        _logger.LogInformation($"PathID:        {prefab.PathId}");
        _logger.LogInformation($"Source file:   {prefab.SourceFile}");
        if (!string.IsNullOrEmpty(prefab.ResourcePath))
            _logger.LogInformation($"Resource path: {prefab.ResourcePath}");
        _logger.LogInformation($"Active:        {prefab.IsActive}");

        var rootTransform = prefab.Transform;
        if (rootTransform == null)
        {
            _logger.LogError("No transform on root.");
            return;
        }

        var allNodes = new List<(UnityReader.GameObject Go, UnityReader.Transform Tr)>();
        CollectHierarchyNodes(prefab, rootTransform, allNodes);

        PrintRenderersSection(allNodes);
        PrintAnimatorsSection(allNodes);

        _logger.LogInformation("");
        _logger.LogInformation("Hierarchy:");
        PrintHierarchyRecursive(prefab, rootTransform, 0);
    }

    private static void CollectHierarchyNodes(
        UnityReader.GameObject go,
        UnityReader.Transform tr,
        List<(UnityReader.GameObject, UnityReader.Transform)> result)
    {
        result.Add((go, tr));
        foreach (var child in tr.Children)
        {
            if (child == null) continue;
            var childGo = child.GameObject;
            if (childGo == null) continue;
            CollectHierarchyNodes(childGo, child, result);
        }
    }

    private void PrintRenderersSection(List<(UnityReader.GameObject Go, UnityReader.Transform Tr)> allNodes)
    {
        var renderers = new List<(UnityReader.GameObject Node, UnityReader.SkinnedMeshRenderer? Smr, UnityReader.MeshRenderer? Mr, UnityReader.MeshFilter? Mf)>();
        foreach (var (go, _) in allNodes)
        {
            go.TryGetComponent<UnityReader.SkinnedMeshRenderer>(out var smr);
            go.TryGetComponent<UnityReader.MeshRenderer>(out var mr);
            go.TryGetComponent<UnityReader.MeshFilter>(out var mf);
            if (smr != null || mr != null)
                renderers.Add((go, smr, mr, mf));
        }

        _logger.LogInformation("");
        _logger.LogInformation($"Renderers ({renderers.Count}):");
        if (renderers.Count == 0)
        {
            _logger.LogInformation("  (none)");
            return;
        }

        foreach (var (node, smr, mr, mf) in renderers)
        {
            if (smr != null)
            {
                var mesh = smr.Mesh;
                _logger.LogInformation($"  - {node.Name} (SkinnedMeshRenderer)  enabled={smr.IsEnabled}");
                if (mesh != null)
                    _logger.LogInformation($"      Mesh: {mesh.Name}  vertices={mesh.VertexCount}  submeshes={mesh.SubMeshCount}  bones={smr.Bones.Count}  bindPoses={mesh.BindPoseCount}");
                else
                    _logger.LogInformation("      Mesh: (missing)");
                PrintMaterialList(smr.Materials);
            }
            else if (mr != null)
            {
                _logger.LogInformation($"  - {node.Name} (MeshRenderer)  enabled={mr.IsEnabled}");
                var mesh = mf?.Mesh;
                if (mesh != null)
                    _logger.LogInformation($"      Mesh: {mesh.Name}  vertices={mesh.VertexCount}  submeshes={mesh.SubMeshCount}");
                else
                    _logger.LogInformation("      Mesh: (no MeshFilter)");
                PrintMaterialList(mr.Materials);
            }
        }
    }

    private void PrintMaterialList(IReadOnlyList<UnityReader.Material?> materials)
    {
        if (materials.Count == 0)
        {
            _logger.LogInformation("      Materials: (none)");
            return;
        }
        _logger.LogInformation($"      Materials ({materials.Count}):");
        foreach (var m in materials)
        {
            if (m == null) { _logger.LogInformation("        - (missing)"); continue; }
            var shader = m.Shader?.Name ?? "(unresolved)";
            _logger.LogInformation($"        - {m.Name}  shader: {shader}");
        }
    }

    private void PrintAnimatorsSection(List<(UnityReader.GameObject Go, UnityReader.Transform Tr)> allNodes)
    {
        var animators = new List<(UnityReader.GameObject Node, UnityReader.Animator Anim)>();
        foreach (var (go, _) in allNodes)
        {
            if (go.TryGetComponent<UnityReader.Animator>(out var anim))
                animators.Add((go, anim));
        }

        _logger.LogInformation("");
        _logger.LogInformation($"Animators ({animators.Count}):");
        if (animators.Count == 0)
        {
            _logger.LogInformation("  (none)");
            return;
        }

        foreach (var (node, anim) in animators)
        {
            _logger.LogInformation($"  - {node.Name}");
            _logger.LogInformation($"      Controller: {anim.Controller?.Name ?? "(none)"}");
            var clips = anim.AnimationClips;
            _logger.LogInformation($"      Clips ({clips.Count}):");
            foreach (var clip in clips)
            {
                if (clip == null) { _logger.LogInformation("        - (missing)"); continue; }
                _logger.LogInformation($"        - {clip.Name}  {clip.Length:F2}s @ {clip.SampleRate:F0} FPS");
            }
        }
    }

    private void SearchAcrossTypes(string searchTerm)
    {
        var textures = new List<UnityReader.UnityTexture>();
        var gameObjects = new List<UnityReader.GameObject>();
        var meshes = new List<UnityReader.Mesh>();
        var materials = new List<UnityReader.Material>();
        var animClips = new List<UnityReader.AnimationClip>();

        foreach (var t in _unityScene.EnumerateTexture2Ds())
            if (!string.IsNullOrEmpty(t.Name) && t.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                textures.Add(t);
        foreach (var g in _unityScene.EnumerateGameObjects())
            if (!string.IsNullOrEmpty(g.Name) && g.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                gameObjects.Add(g);
        foreach (var m in _unityScene.EnumerateMeshes())
            if (!string.IsNullOrEmpty(m.Name) && m.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                meshes.Add(m);
        foreach (var m in _unityScene.EnumerateMaterials())
            if (!string.IsNullOrEmpty(m.Name) && m.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                materials.Add(m);
        foreach (var c in _unityScene.EnumerateAnimationClips())
            if (!string.IsNullOrEmpty(c.Name) && c.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                animClips.Add(c);

        _logger.LogInformation($"=== Search results for '{searchTerm}' ===");

        _logger.LogInformation("");
        _logger.LogInformation($"Texture2D ({textures.Count}):");
        if (textures.Count == 0) _logger.LogInformation("  (no matches)");
        foreach (var t in textures)
        {
            var rp = string.IsNullOrEmpty(t.ResourcePath) ? "" : $"  resource: {t.ResourcePath}";
            _logger.LogInformation($"  - {t.Name}  {t.Width}x{t.Height} {t.Format}  PathID: {t.PathId}  file: {t.SourceFile}{rp}");
        }

        _logger.LogInformation("");
        _logger.LogInformation($"GameObject ({gameObjects.Count}):");
        if (gameObjects.Count == 0) _logger.LogInformation("  (no matches)");
        foreach (var g in gameObjects)
        {
            var rp = string.IsNullOrEmpty(g.ResourcePath) ? "" : $"  resource: {g.ResourcePath}";
            var active = g.IsActive ? "" : "  (inactive)";
            _logger.LogInformation($"  - {g.Name}  PathID: {g.PathId}  file: {g.SourceFile}{rp}{active}");
        }

        _logger.LogInformation("");
        _logger.LogInformation($"Mesh ({meshes.Count}):");
        if (meshes.Count == 0) _logger.LogInformation("  (no matches)");
        foreach (var m in meshes)
        {
            var skin = m.HasSkinning ? "  skinned" : "";
            _logger.LogInformation($"  - {m.Name}  vertices={m.VertexCount}  submeshes={m.SubMeshCount}{skin}  PathID: {m.PathId}  file: {m.SourceFile}");
        }

        _logger.LogInformation("");
        _logger.LogInformation($"Material ({materials.Count}):");
        if (materials.Count == 0) _logger.LogInformation("  (no matches)");
        foreach (var m in materials)
        {
            var shader = m.Shader?.Name ?? "(unresolved)";
            _logger.LogInformation($"  - {m.Name}  shader: {shader}  PathID: {m.PathId}  file: {m.SourceFile}");
        }

        _logger.LogInformation("");
        _logger.LogInformation($"AnimationClip ({animClips.Count}):");
        if (animClips.Count == 0) _logger.LogInformation("  (no matches)");
        foreach (var c in animClips)
        {
            var legacy = c.IsLegacy ? "  legacy" : "";
            _logger.LogInformation($"  - {c.Name}  {c.Length:F2}s @ {c.SampleRate:F0} FPS{legacy}  PathID: {c.PathId}  file: {c.SourceFile}");
        }

        var total = textures.Count + gameObjects.Count + meshes.Count + materials.Count + animClips.Count;
        _logger.LogInformation("");
        _logger.LogInformation($"Total: {total} matches ({textures.Count} Texture2D, {gameObjects.Count} GameObject, {meshes.Count} Mesh, {materials.Count} Material, {animClips.Count} AnimationClip)");
    }

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
