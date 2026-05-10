using System.Collections.Generic;
using UnityReader.Internal;

namespace UnityReader;

// Path-checksum cache (lazy) maps the bone-path CRC32s stored in clips back to slash-
// separated transform paths in this Animator's skeleton.
public sealed class Animator
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.Animator _vendor;

    private PathChecksumCache? _pathCache;
    private readonly Dictionary<AnimationClip, IReadOnlyList<BoneCurves>> _processCache = new();

    internal Animator(UnityScene scene, AssetStudio.Animator vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    // Resolves through any override layer. Null if the controller reference is broken.
    public AnimatorController? Controller
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

    public AnimatorOverrideController? OverrideController
    {
        get
        {
            if (_vendor.m_Controller == null || !_vendor.m_Controller.TryGet(out var rt)) return null;
            return rt is AssetStudio.AnimatorOverrideController aoc
                ? _scene.GetOrCreateAnimatorOverrideController(aoc)
                : null;
        }
    }

    // When an override controller is present (every EA unit prefab) returns only the
    // per-prefab override variants, matching AR's RuntimeAnimatorController enumeration
    // (walks overrideController.Clips[i].OverrideClip). Otherwise falls back to the
    // base controller's clip list.
    public IReadOnlyList<AnimationClip?> AnimationClips
    {
        get
        {
            var ov = OverrideController;
            if (ov != null)
            {
                var list = new List<AnimationClip?>();
                foreach (var (_, overrideClip) in ov.ClipOverrides)
                {
                    if (overrideClip != null)
                        list.Add(overrideClip);
                }
                return list;
            }
            return Controller?.AnimationClips ?? new AnimationClip?[0];
        }
    }

    public GameObject? GameObject =>
        _vendor.m_GameObject.TryGet(out var go) ? _scene.GetOrCreateGameObject(go) : null;

    // Cached by clip reference. Throws NotSupportedException for legacy clips or any clip
    // missing the muscle-clip + binding-constant generic-skeletal layout.
    public IReadOnlyList<BoneCurves> ProcessClip(AnimationClip clip)
    {
        // Shared base controllers can be reached from multiple Parallel.ForEach prefab
        // workers concurrently, and both caches below use non-thread-safe Dictionaries.
        lock (_processCache)
        {
            if (_processCache.TryGetValue(clip, out var cached)) return cached;
            _pathCache ??= PathChecksumCache.BuildForAnimator(this);
            var result = AnimationClipProcessor.Decode(clip.Vendor, _pathCache);
            _processCache[clip] = result;
            return result;
        }
    }

    internal AssetStudio.Animator Vendor => _vendor;
}
