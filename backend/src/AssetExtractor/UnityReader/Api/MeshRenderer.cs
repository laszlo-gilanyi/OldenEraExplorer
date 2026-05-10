using System.Collections.Generic;
using System.Threading;

namespace UnityReader;

// Materials slots stay positional (null for broken refs) so they align with submesh
// indices on iteration.
public sealed class MeshRenderer
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.MeshRenderer _vendor;
    private Material?[]? _cachedMaterials;

    internal MeshRenderer(UnityScene scene, AssetStudio.MeshRenderer vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    // Disabled renderers are runtime-hidden placeholders (e.g. PLATFORM + BACK's
    // background quad) and are skipped by the prefab pipeline.
    public bool IsEnabled => _vendor.m_Enabled;

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
