using System.Collections.Generic;
using System.Threading;

namespace UnityReader;

// Always the base controller. AnimatorOverrideController per-unit overrides are resolved
// upstream at Animator.Controller retrieval time. AnimationClips is the position-preserving
// union of state-machine clips (null signals a broken cross-bundle ref).
public sealed class AnimatorController
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.AnimatorController _vendor;
    private AnimationClip?[]? _cachedAnimationClips;

    internal AnimatorController(UnityScene scene, AssetStudio.AnimatorController vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    public string Name => _vendor.m_Name ?? string.Empty;
    public long PathId => _vendor.m_PathID;

    public IReadOnlyList<AnimationClip?> AnimationClips
    {
        get
        {
            if (_cachedAnimationClips is { } cached) return cached;
            var src = _vendor.m_AnimationClips;
            var result = new AnimationClip?[src?.Count ?? 0];
            for (var i = 0; i < result.Length; i++)
                result[i] = src![i].TryGet(out var c) ? _scene.GetOrCreateAnimationClip(c) : null;
            Interlocked.CompareExchange(ref _cachedAnimationClips, result, null);
            return _cachedAnimationClips!;
        }
    }
}
