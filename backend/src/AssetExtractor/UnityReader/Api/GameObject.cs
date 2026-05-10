using System.Diagnostics.CodeAnalysis;

namespace UnityReader;

// TryGetComponent supports Transform, MeshFilter, MeshRenderer, SkinnedMeshRenderer, Animator.
public sealed class GameObject
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.GameObject _vendor;

    internal GameObject(UnityScene scene, AssetStudio.GameObject vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    public string Name => _vendor.m_Name ?? string.Empty;
    public long PathId => _vendor.m_PathID;

    public bool IsActive => _vendor.m_IsActive;

    // SerializedFile name (no directory). Pair with PathId to form cross-file dedup keys.
    public string SourceFile =>
        System.IO.Path.GetFileName(_vendor.assetsFile?.fileName ?? string.Empty);

    // ResourceManager-registered path (e.g. "objects/artifact/book_artifact") or null
    // if not bound to a runtime resource slot.
    public string? ResourcePath => _scene.ResolveResourcePath(_vendor);

    public Transform? Transform => _vendor.m_Transform != null ? _scene.GetOrCreateTransform(_vendor.m_Transform) : null;

    public bool TryGetComponent<T>([NotNullWhen(true)] out T? component) where T : class
    {
        component = ResolveComponent<T>();
        return component != null;
    }

    private T? ResolveComponent<T>() where T : class
    {
        if (typeof(T) == typeof(Transform))
            return _vendor.m_Transform != null
                ? (T)(object)_scene.GetOrCreateTransform(_vendor.m_Transform)
                : null;
        if (typeof(T) == typeof(MeshFilter))
            return _vendor.m_MeshFilter != null
                ? (T)(object)_scene.GetOrCreateMeshFilter(_vendor.m_MeshFilter)
                : null;
        if (typeof(T) == typeof(MeshRenderer))
            return _vendor.m_MeshRenderer != null
                ? (T)(object)_scene.GetOrCreateMeshRenderer(_vendor.m_MeshRenderer)
                : null;
        if (typeof(T) == typeof(SkinnedMeshRenderer))
            return _vendor.m_SkinnedMeshRenderer != null
                ? (T)(object)_scene.GetOrCreateSkinnedMeshRenderer(_vendor.m_SkinnedMeshRenderer)
                : null;
        if (typeof(T) == typeof(Animator))
            return _vendor.m_Animator != null
                ? (T)(object)_scene.GetOrCreateAnimator(_vendor.m_Animator)
                : null;
        return null;
    }
}
