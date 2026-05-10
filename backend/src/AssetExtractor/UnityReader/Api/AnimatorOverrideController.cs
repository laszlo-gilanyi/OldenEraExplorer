using System.Collections.Generic;
using System.Threading;

namespace UnityReader;

// Every EA unit prefab uses one of these to swap unit-specific idle / move / attack
// clips into the shared base humanoid controller.
public sealed class AnimatorOverrideController
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.AnimatorOverrideController _vendor;
    private (AnimationClip? Original, AnimationClip? Override)[]? _cachedClipOverrides;

    internal AnimatorOverrideController(UnityScene scene, AssetStudio.AnimatorOverrideController vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    public string Name => _vendor.m_Name ?? string.Empty;
    public long PathId => _vendor.m_PathID;

    // Resolved transitively when a layer points at another override layer.
    public AnimatorController? BaseController
    {
        get
        {
            if (_vendor.m_Controller == null || !_vendor.m_Controller.TryGet(out var rt)) return null;
            return rt switch
            {
                AssetStudio.AnimatorController ac => _scene.GetOrCreateAnimatorController(ac),
                AssetStudio.AnimatorOverrideController aoc => _scene.GetOrCreateAnimatorOverrideController(aoc).BaseController,
                _ => null,
            };
        }
    }

    // Null Override means the clip was not overridden, fall back to Original.
    public IReadOnlyList<(AnimationClip? Original, AnimationClip? Override)> ClipOverrides
    {
        get
        {
            if (_cachedClipOverrides is { } cached) return cached;
            var src = _vendor.m_Clips ?? new List<AssetStudio.AnimationClipOverride>();
            var result = new (AnimationClip?, AnimationClip?)[src.Count];
            for (var i = 0; i < src.Count; i++)
            {
                var originalRef = src[i].m_OriginalClip;
                var overrideRef = src[i].m_OverrideClip;
                AnimationClip? original = null, ov = null;
                if (originalRef != null && originalRef.TryGet(out var oVendor)) original = _scene.GetOrCreateAnimationClip(oVendor);
                if (overrideRef != null && overrideRef.TryGet(out var ovVendor)) ov = _scene.GetOrCreateAnimationClip(ovVendor);
                result[i] = (original, ov);
            }
            Interlocked.CompareExchange(ref _cachedClipOverrides, result, null);
            return _cachedClipOverrides!;
        }
    }

    // Position-preserving: each slot is either the override (when mapped) or the original;
    // null entries indicate a broken reference.
    public IReadOnlyList<AnimationClip?> EffectiveClips
    {
        get
        {
            var baseClips = BaseController?.AnimationClips ?? new AnimationClip?[0];
            var overrideMap = new Dictionary<long, AnimationClip?>();
            foreach (var (orig, ov) in ClipOverrides)
            {
                if (orig != null) overrideMap[orig.PathId] = ov;
            }
            var result = new AnimationClip?[baseClips.Count];
            for (var i = 0; i < baseClips.Count; i++)
            {
                var c = baseClips[i];
                if (c != null && overrideMap.TryGetValue(c.PathId, out var ov))
                    result[i] = ov ?? c;
                else
                    result[i] = c;
            }
            return result;
        }
    }
}
