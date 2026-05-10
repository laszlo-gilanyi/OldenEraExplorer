namespace UnityReader;

public sealed class MeshFilter
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.MeshFilter _vendor;

    internal MeshFilter(UnityScene scene, AssetStudio.MeshFilter vendor)
    {
        _scene = scene;
        _vendor = vendor;
    }

    public Mesh? Mesh =>
        _vendor.m_Mesh != null && _vendor.m_Mesh.TryGet(out var m) ? _scene.GetOrCreateMesh(m) : null;
}
