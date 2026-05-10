using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using UnityReader.Internal;

namespace UnityReader;

// Local TRS only; world-space and Animator-driven transforms are not exposed.
public sealed class Transform
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.Transform _vendor;
    private Transform?[]? _cachedChildren;

    internal Transform(UnityScene scene, AssetStudio.Transform vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    // Used by the bone-path builder to detect when a walk-up reaches the animator transform.
    public long PathId => _vendor.m_PathID;

    public Vector3 LocalPosition => _vendor.m_LocalPosition.ToNumerics();
    public Quaternion LocalRotation => _vendor.m_LocalRotation.ToNumerics();
    public Vector3 LocalScale => _vendor.m_LocalScale.ToNumerics();

    public GameObject? GameObject =>
        _vendor.m_GameObject.TryGet(out var go) ? _scene.GetOrCreateGameObject(go) : null;

    public Transform? Parent =>
        _vendor.m_Father != null && _vendor.m_Father.TryGet(out var p) ? _scene.GetOrCreateTransform(p) : null;

    // Position-preserving; null entries indicate a broken cross-bundle reference, so
    // iterate by index when positions matter.
    public IReadOnlyList<Transform?> Children
    {
        get
        {
            if (_cachedChildren is { } cached) return cached;
            var list = new Transform?[_vendor.m_Children.Count];
            for (var i = 0; i < _vendor.m_Children.Count; i++)
                list[i] = _vendor.m_Children[i].TryGet(out var c) ? _scene.GetOrCreateTransform(c) : null;
            Interlocked.CompareExchange(ref _cachedChildren, list, null);
            return _cachedChildren!;
        }
    }
}
