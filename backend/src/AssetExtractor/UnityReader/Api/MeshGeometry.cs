using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace UnityReader;

// Position/normal/tangent/UV/color arrays are aligned to Vertices.Length; null entries
// indicate the mesh did not store that channel.
public sealed record MeshGeometry(
    Vector3[] Vertices,
    Vector3[]? Normals,
    Vector4[]? Tangents,
    Vector4[]? Colors,
    IReadOnlyList<Vector2[]?> UvChannels,
    BoneInfluence[]? BoneWeights,
    Matrix4x4[]? BindPoses,
    int[] Indices,
    SubMeshInfo[] SubMeshes);

// 8 slots; OEE only reads UV0 in practice, so the other seven decode lazily on first
// access and stay null otherwise.
internal sealed class UvChannelView : IReadOnlyList<Vector2[]?>
{
    private readonly float[]?[] _sources;
    private readonly int _vertexCount;
    private readonly Vector2[]?[] _cache;
    private readonly bool[] _decoded;

    public UvChannelView(float[]?[] sources, int vertexCount)
    {
        _sources = sources;
        _vertexCount = vertexCount;
        _cache = new Vector2[]?[sources.Length];
        _decoded = new bool[sources.Length];
    }

    public int Count => _sources.Length;

    public Vector2[]? this[int index]
    {
        get
        {
            if (Volatile.Read(ref _decoded[index])) return _cache[index];
            var src = _sources[index];
            Vector2[]? value = null;
            if (src != null && src.Length > 0)
            {
                value = new Vector2[_vertexCount];
                for (var i = 0; i < _vertexCount; i++)
                    value[i] = new Vector2(src[i * 2 + 0], src[i * 2 + 1]);
            }
            _cache[index] = value;
            Volatile.Write(ref _decoded[index], true);
            return value;
        }
    }

    public IEnumerator<Vector2[]?> GetEnumerator()
    {
        for (var i = 0; i < _sources.Length; i++) yield return this[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Up to 4 bones with normalized weights; unused slots are BoneIndex=0, Weight=0.
public readonly record struct BoneInfluence(
    int BoneIndex0, int BoneIndex1, int BoneIndex2, int BoneIndex3,
    float Weight0, float Weight1, float Weight2, float Weight3);

// MeshGeometry.Indices is already triangulated (TriangleStrip / Quads expanded), so the
// per-submesh slice is contiguous starting at this submesh's cumulative index offset.
public readonly record struct SubMeshInfo(
    int IndexCount,
    int VertexCount,
    int FirstVertex,
    int BaseVertex,
    MeshTopology Topology);

public enum MeshTopology
{
    Triangles = 0,
    TriangleStrip = 1,
    Quads = 2,
    Lines = 3,
    LineStrip = 4,
    Points = 5,
}
