using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UnityReader.Internal;

namespace UnityReader;

// Metadata is eager; geometry is fetched on demand via ReadGeometry so iterating
// thousands of meshes does not pin every vertex buffer in memory at once.
public sealed class Mesh
{
    private readonly AssetStudio.Mesh _vendor;

    internal Mesh(AssetStudio.Mesh vendor) => _vendor = vendor;

    public string Name => _vendor.m_Name ?? string.Empty;
    public long PathId => _vendor.m_PathID;

    // Engine-builtin primitives (Quad, Cube, ...) live in Unity's resource library and
    // surface here with that library's filename rather than a game-bundle filename.
    public string SourceFile =>
        System.IO.Path.GetFileName(_vendor.assetsFile?.fileName ?? string.Empty);
    public int VertexCount => _vendor.m_VertexCount;
    public int SubMeshCount => _vendor.m_SubMeshes?.Count ?? 0;
    public bool HasSkinning => _vendor.m_Skin != null && _vendor.m_Skin.Length > 0;
    public bool HasBlendShapes => _vendor.m_Shapes?.shapes != null && _vendor.m_Shapes.shapes.Count > 0;
    public int BindPoseCount => _vendor.m_BindPose?.Length ?? 0;

    // Allocates fresh arrays on every call; cache the result if you need it more than once.
    public MeshGeometry ReadGeometry()
    {
        var vc = _vendor.m_VertexCount;
        var vertices = UnpackVec3FromStride(_vendor.m_Vertices, vc);
        var normals = _vendor.m_Normals?.Length > 0 ? UnpackVec3FromStride(_vendor.m_Normals, vc) : null;
        var tangents = _vendor.m_Tangents?.Length > 0 ? UnpackVec4FromStride(_vendor.m_Tangents, vc) : null;
        var colors = _vendor.m_Colors?.Length > 0 ? UnpackVec4FromStride(_vendor.m_Colors, vc) : null;
        var uv = new UvChannelView(
            new[]
            {
                _vendor.m_UV0, _vendor.m_UV1, _vendor.m_UV2, _vendor.m_UV3,
                _vendor.m_UV4, _vendor.m_UV5, _vendor.m_UV6, _vendor.m_UV7,
            },
            vc);

        BoneInfluence[]? skin = null;
        if (_vendor.m_Skin != null && _vendor.m_Skin.Length > 0)
        {
            skin = new BoneInfluence[_vendor.m_Skin.Length];
            for (var i = 0; i < skin.Length; i++)
            {
                var s = _vendor.m_Skin[i];
                skin[i] = new BoneInfluence(
                    s.boneIndex[0], s.boneIndex[1], s.boneIndex[2], s.boneIndex[3],
                    s.weight[0], s.weight[1], s.weight[2], s.weight[3]);
            }
        }

        Matrix4x4[]? bindPoses = null;
        if (_vendor.m_BindPose != null && _vendor.m_BindPose.Length > 0)
        {
            bindPoses = new Matrix4x4[_vendor.m_BindPose.Length];
            for (var i = 0; i < bindPoses.Length; i++)
                bindPoses[i] = _vendor.m_BindPose[i].ToNumerics();
        }

        var indices = _vendor.m_Indices != null
            ? _vendor.m_Indices.Select(i => (int)i).ToArray()
            : System.Array.Empty<int>();

        var submeshes = (_vendor.m_SubMeshes ?? new List<AssetStudio.SubMesh>())
            .Select(s => new SubMeshInfo(
                IndexCount: (int)s.indexCount,
                VertexCount: (int)s.vertexCount,
                FirstVertex: (int)s.firstVertex,
                BaseVertex: (int)s.baseVertex,
                Topology: (MeshTopology)(int)s.topology))
            .ToArray();

        return new MeshGeometry(vertices, normals, tangents, colors, uv, skin, bindPoses, indices, submeshes);
    }

    // Unity 6000 stores normals as VEC4 (W unused or sign); detect stride from buffer
    // length so VEC3- and VEC4-strided channels both decode correctly.
    private static Vector3[] UnpackVec3FromStride(float[] flat, int vertexCount)
    {
        if (vertexCount == 0) return System.Array.Empty<Vector3>();
        var stride = flat.Length / vertexCount;
        var arr = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            arr[i] = new Vector3(flat[i * stride + 0], flat[i * stride + 1], flat[i * stride + 2]);
        return arr;
    }

    private static Vector4[] UnpackVec4FromStride(float[] flat, int vertexCount)
    {
        if (vertexCount == 0) return System.Array.Empty<Vector4>();
        var stride = flat.Length / vertexCount;
        var arr = new Vector4[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            arr[i] = new Vector4(flat[i * stride + 0], flat[i * stride + 1], flat[i * stride + 2], stride >= 4 ? flat[i * stride + 3] : 1f);
        return arr;
    }
}
