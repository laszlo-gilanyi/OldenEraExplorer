using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnityReader;

/// <summary>
/// Loads Unity SerializedFiles / AssetBundles into an in-memory representation.
/// Owns the underlying <c>AssetsManager</c> and open file handles; dispose when done.
/// </summary>
public sealed class UnityScene : IDisposable
{
    // Set once before the first Load() call. Bridges AssetStudio.Logger output into
    // Microsoft.Extensions.Logging; null factory silently drops vendor diagnostics.
    public static ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    private static readonly HashSet<int> SupportedFormatIds = new()
    {
        (int)TextureFormat.RGB24,
        (int)TextureFormat.RGBA32,
        (int)TextureFormat.DXT1,
        (int)TextureFormat.DXT5,
        (int)TextureFormat.BC7,
    };

    private readonly AssetStudio.AssetsManager _manager;
    private readonly IReadOnlyList<AssetStudio.SerializedFile> _files;
    private bool _disposed;

    // Reference-equality so the same vendor instance always maps to the same wrapper
    // (Material.TextureProperties and EnumerateTexture2Ds yield identical refs for the
    // same Texture2D). Concurrent so Parallel.ForEach extraction can resolve cross-refs
    // concurrently.
    private readonly ConcurrentDictionary<object, object> _wrapperCache = new(ReferenceEqualityComparer.Instance);

    private readonly List<UnityTexture> _supportedTextures = new();
    private readonly List<GameObject> _allGameObjects = new();
    private readonly List<Mesh> _allMeshes = new();
    private readonly List<Material> _allMaterials = new();
    private readonly List<AnimationClip> _allAnimationClips = new();

    // Keyed by vendor reference (not PathID) so cross-file ID collisions cannot alias.
    private readonly Dictionary<AssetStudio.Object, string> _resourcePathByVendor = new();

    public string UnityVersion { get; }
    public int FileCount => _files.Count;
    public long ObjectCount { get; }
    public IReadOnlyDictionary<int, int> ClassCounts { get; }
    public IReadOnlyList<string> UnsupportedTextureFormats { get; }

    private UnityScene(AssetStudio.AssetsManager manager)
    {
        _manager = manager;
        _files = manager.AssetsFileList;

        string? unityVersion = null;
        long objectCount = 0;
        var counts = new Dictionary<int, int>();
        var unsupportedFormatNames = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var sf in manager.AssetsFileList)
        {
            unityVersion ??= sf.version?.ToString();
            foreach (var obj in sf.Objects)
            {
                objectCount++;
                var id = (int)obj.type;
                counts.TryGetValue(id, out var c);
                counts[id] = c + 1;

                switch (obj)
                {
                    case AssetStudio.Texture2D tex:
                    {
                        var fmtId = (int)tex.m_TextureFormat;
                        if (SupportedFormatIds.Contains(fmtId))
                            _supportedTextures.Add(GetOrCreateTexture(tex));
                        else
                            unsupportedFormatNames.Add(tex.m_TextureFormat.ToString());
                        break;
                    }
                    case AssetStudio.GameObject go:
                        _allGameObjects.Add(GetOrCreateGameObject(go));
                        break;
                    case AssetStudio.Mesh mesh:
                        _allMeshes.Add(GetOrCreateMesh(mesh));
                        break;
                    case AssetStudio.Material mat:
                        _allMaterials.Add(GetOrCreateMaterial(mat));
                        break;
                    case AssetStudio.AnimationClip ac:
                        _allAnimationClips.Add(GetOrCreateAnimationClip(ac));
                        break;
                    case AssetStudio.ResourceManager rm:
                        IndexResourceManager(rm);
                        break;
                }
            }
        }

        UnityVersion = unityVersion ?? "unknown";
        ObjectCount = objectCount;
        ClassCounts = counts;
        UnsupportedTextureFormats = unsupportedFormatNames.ToArray();
    }

    // Hard-coded readers, no classdata.tpk / TypeTree fallback. Adjacent referenced files
    // are auto-loaded by the underlying parser.
    public static UnityScene Load(IEnumerable<string> paths)
    {
        var manager = new AssetStudio.AssetsManager
        {
            LoadViaTypeTree = false,
            MeshLazyLoad = false,
        };
        manager.LoadFilesAndFolders(paths.ToArray());
        return new UnityScene(manager);
    }

    // Accessing any wrapper returned from this scene is undefined behavior post-dispose.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager.Clear();
    }

    // Texture2D (ClassID 28) only. Cubemap, Texture2DArray, Texture3D are excluded;
    // formats outside TextureFormat surface in UnsupportedTextureFormats instead.
    public IEnumerable<UnityTexture> EnumerateTexture2Ds()
    {
        foreach (var t in _supportedTextures) yield return t;
    }

    public IEnumerable<GameObject> EnumerateGameObjects()
    {
        foreach (var g in _allGameObjects) yield return g;
    }

    public IEnumerable<Mesh> EnumerateMeshes()
    {
        foreach (var m in _allMeshes) yield return m;
    }

    public IEnumerable<Material> EnumerateMaterials()
    {
        foreach (var m in _allMaterials) yield return m;
    }

    public IEnumerable<AnimationClip> EnumerateAnimationClips()
    {
        foreach (var c in _allAnimationClips) yield return c;
    }

    private void IndexResourceManager(AssetStudio.ResourceManager rm)
    {
        if (rm.m_Container == null) return;
        foreach (var kvp in rm.m_Container)
        {
            if (!kvp.Value.TryGet(out var asset) || asset == null) continue;

            // Prefer the longer key when multiple aliases point to one asset: the short
            // leaf-only variant routes into Assets/Resources/{leaf}.png and gets dropped
            // by the OEE allow-list filter, hiding the resource entirely.
            if (!_resourcePathByVendor.TryGetValue(asset, out var existingKey)
                || kvp.Key.Length > existingKey.Length)
            {
                _resourcePathByVendor[asset] = kvp.Key;
            }
        }
    }

    // Returns the ResourceManager.Container key (e.g. "units/dungeon/1prefabs/blade_dancer")
    // or null if the asset is not a registered runtime resource.
    internal string? ResolveResourcePath(AssetStudio.Object vendor)
        => _resourcePathByVendor.TryGetValue(vendor, out var path) ? path : null;

    private TWrapper GetOrCreate<TVendor, TWrapper>(TVendor vendor, Func<TVendor, TWrapper> factory)
        where TVendor : class
        where TWrapper : class
        => (TWrapper)_wrapperCache.GetOrAdd(vendor, _ => factory(vendor));

    internal UnityTexture GetOrCreateTexture(AssetStudio.Texture2D vendor)
        => GetOrCreate(vendor, v => new UnityTexture(this, v, (TextureFormat)(int)v.m_TextureFormat));

    internal GameObject GetOrCreateGameObject(AssetStudio.GameObject vendor)
        => GetOrCreate(vendor, v => new GameObject(this, v));

    internal Mesh GetOrCreateMesh(AssetStudio.Mesh vendor)
        => GetOrCreate(vendor, v => new Mesh(v));

    internal Material GetOrCreateMaterial(AssetStudio.Material vendor)
        => GetOrCreate(vendor, v => new Material(this, v));

    internal Shader GetOrCreateShader(AssetStudio.Shader vendor)
        => GetOrCreate(vendor, v => new Shader(v));

    internal Transform GetOrCreateTransform(AssetStudio.Transform vendor)
        => GetOrCreate(vendor, v => new Transform(this, v));

    internal MeshFilter GetOrCreateMeshFilter(AssetStudio.MeshFilter vendor)
        => GetOrCreate(vendor, v => new MeshFilter(this, v));

    internal MeshRenderer GetOrCreateMeshRenderer(AssetStudio.MeshRenderer vendor)
        => GetOrCreate(vendor, v => new MeshRenderer(this, v));

    internal SkinnedMeshRenderer GetOrCreateSkinnedMeshRenderer(AssetStudio.SkinnedMeshRenderer vendor)
        => GetOrCreate(vendor, v => new SkinnedMeshRenderer(this, v));

    internal Animator GetOrCreateAnimator(AssetStudio.Animator vendor)
        => GetOrCreate(vendor, v => new Animator(this, v));

    internal AnimatorController GetOrCreateAnimatorController(AssetStudio.AnimatorController vendor)
        => GetOrCreate(vendor, v => new AnimatorController(this, v));

    internal AnimatorOverrideController GetOrCreateAnimatorOverrideController(AssetStudio.AnimatorOverrideController vendor)
        => GetOrCreate(vendor, v => new AnimatorOverrideController(this, v));

    internal AnimationClip GetOrCreateAnimationClip(AssetStudio.AnimationClip vendor)
        => GetOrCreate(vendor, v => new AnimationClip(v));
}
