using System.Collections.Generic;
using System.Threading;

namespace UnityReader;

// Bones is position-preserving (null for broken refs) because mesh skin-weight bone
// indices reference bone slots by position.
public sealed class SkinnedMeshRenderer
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.SkinnedMeshRenderer _vendor;
    private Transform?[]? _cachedBones;
    private Material?[]? _cachedMaterials;

    internal SkinnedMeshRenderer(UnityScene scene, AssetStudio.SkinnedMeshRenderer vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    // Disabled renderers are runtime-hidden placeholders that the OEE prefab pipeline skips.
    public bool IsEnabled => _vendor.m_Enabled;

    public Mesh? Mesh =>
        _vendor.m_Mesh != null && _vendor.m_Mesh.TryGet(out var m) ? _scene.GetOrCreateMesh(m) : null;

    public IReadOnlyList<Transform?> Bones
    {
        get
        {
            if (_cachedBones is { } cached) return cached;
            var list = _vendor.m_Bones ?? new List<AssetStudio.PPtr<AssetStudio.Transform>>();
            var result = new Transform?[list.Count];
            for (var i = 0; i < list.Count; i++)
                result[i] = list[i].TryGet(out var t) ? _scene.GetOrCreateTransform(t) : null;
            Interlocked.CompareExchange(ref _cachedBones, result, null);
            return _cachedBones!;
        }
    }

    public IReadOnlyList<Material?> Materials
    {
        get
        {
            if (_cachedMaterials is { } cached) return cached;
            var list = _vendor.m_Materials ?? new List<AssetStudio.PPtr<AssetStudio.Material>>();
            var result = new Material?[list.Count];
            for (var i = 0; i < list.Count; i++)
                result[i] = list[i].TryGet(out var m) ? _scene.GetOrCreateMaterial(m) : null;
            Interlocked.CompareExchange(ref _cachedMaterials, result, null);
            return _cachedMaterials!;
        }
    }
}
