#nullable enable
namespace AssetExtractor.Models;

public class UnitData : PrefabData
{
    public SkeletonData? Skeleton { get; set; }
    public List<AnimationData> Animations { get; set; } = new();

    public UnitData()
    {
        Type = PrefabType.Unit;
    }
}

public class HierarchyNode
{
    public string Name { get; set; } = string.Empty;
    public long PathID { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    // Populated at build time so the hierarchy filter does not have to round-trip
    // back to the provider when checking if a node anchors an animator.
    public bool HasAnimator { get; set; }
    public Transform LocalTransform { get; set; } = new();
    public Transform WorldTransform { get; set; } = new();
    public HierarchyNode? Parent { get; set; }
    public List<HierarchyNode> Children { get; set; } = new();
}

public class Transform
{
    public Vector3 Position { get; set; } = new();
    public Quaternion Rotation { get; set; } = new();
    public Vector3 Scale { get; set; } = Vector3.One;

    public static Transform Identity => new()
    {
        Position = Vector3.Zero,
        Rotation = Quaternion.Identity,
        Scale = Vector3.One
    };
}

public class Vector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public Vector3() { }

    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3 Zero => new(0, 0, 0);
    public static Vector3 One => new(1, 1, 1);
}

public class Quaternion
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; } = 1.0f;

    public Quaternion() { }

    public Quaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static Quaternion Identity => new(0, 0, 0, 1);
}

public class SkeletonData
{
    public HierarchyNode? RootBone { get; set; }
    public List<BoneData> Bones { get; set; } = new();
    public Dictionary<string, Matrix4x4> RestPose { get; set; } = new();
}

public class BoneData
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Index { get; set; }
    public int ParentIndex { get; set; } = -1;
    public Transform LocalTransform { get; set; } = new();
    public Matrix4x4 WorldMatrix { get; set; } = new();
    public Matrix4x4 InverseBindMatrix { get; set; } = new();
}

public class Matrix4x4
{
    public float[] Values { get; set; } = new float[16];

    public Matrix4x4()
    {
        Values[0] = 1; Values[5] = 1; Values[10] = 1; Values[15] = 1;
    }

    public Matrix4x4(float[] values)
    {
        if (values.Length != 16)
            throw new ArgumentException("Matrix4x4 requires exactly 16 values");
        Values = values;
    }

    public static Matrix4x4 Identity => new();
}

public class MeshData
{
    public string Name { get; set; } = string.Empty;
    public string HierarchyPath { get; set; } = string.Empty;
    public Transform LocalTransform { get; set; } = new();
    // Pre-flattened from the full parent hierarchy.
    public Transform WorldTransform { get; set; } = new();
    public int[] Triangles { get; set; } = Array.Empty<int>();
    public List<SubMeshData> SubMeshes { get; set; } = new();
    public Vector3[] Vertices { get; set; } = Array.Empty<Vector3>();
    public Vector3[] Normals { get; set; } = Array.Empty<Vector3>();
    public Vector2[] UV0 { get; set; } = Array.Empty<Vector2>();
    public Vector4[] Tangents { get; set; } = Array.Empty<Vector4>();
    public Vector4[] Colors { get; set; } = Array.Empty<Vector4>();
    public BoneWeight[] BoneWeights { get; set; } = Array.Empty<BoneWeight>();
    public int[] BoneIndices { get; set; } = Array.Empty<int>();
    public Matrix4x4[] BindPoses { get; set; } = Array.Empty<Matrix4x4>();
    public string MaterialName { get; set; } = string.Empty;
    // Per-axis sign (-1 if that axis has negative scale, 1 otherwise); applied to bake
    // negative scale into vertices and flip winding order.
    public Vector3 AccumulatedScale { get; set; } = Vector3.One;

    // Map objects need a determinant-based winding flip when their transform has a negative
    // determinant; units' meshes are authored differently and do not.
    public bool IsMapObject { get; set; } = false;
}

public enum MeshTopology
{
    Triangles = 0,
    TriangleStrip = 1,
    Quads = 2,
    Lines = 3,
    LineStrip = 4,
    Points = 5
}

public class SubMeshData
{
    public int[] Triangles { get; set; } = Array.Empty<int>();
    public string MaterialName { get; set; } = string.Empty;
    public MeshTopology Topology { get; set; } = MeshTopology.Triangles;
}

public class Vector2
{
    public float X { get; set; }
    public float Y { get; set; }

    public Vector2() { }

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }
}

public class BoneWeight
{
    public int[] BoneIndex { get; set; } = new int[4];
    public float[] Weight { get; set; } = new float[4];

    public BoneWeight()
    {
        for (int i = 0; i < 4; i++)
        {
            BoneIndex[i] = 0;
            Weight[i] = 0f;
        }
    }
}

public class AnimationData
{
    public string Name { get; set; } = string.Empty;
    public float Length { get; set; }
    public float FrameRate { get; set; } = 30f;
    public List<AnimationChannel> Channels { get; set; } = new();
}

public class AnimationChannel
{
    public string BonePath { get; set; } = string.Empty;
    public int BoneIndex { get; set; } = -1;
    public AnimationProperty Property { get; set; }
    public List<Keyframe> Keyframes { get; set; } = new();
}

public enum AnimationProperty
{
    PositionX, PositionY, PositionZ,
    RotationX, RotationY, RotationZ, RotationW,
    ScaleX, ScaleY, ScaleZ
}

public class Keyframe
{
    public float Time { get; set; }
    public float Value { get; set; }
    public float InTangent { get; set; }
    public float OutTangent { get; set; }
}

public class TextureData
{
    public string Name { get; set; } = string.Empty;
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty; // PNG, JPEG, RAW, etc.
}

public class MaterialData
{
    public string Name { get; set; } = string.Empty;
    public string? MainTextureName { get; set; }
    public string? EmissiveTextureName { get; set; }
    public Vector4 BaseColor { get; set; } = new Vector4(1, 1, 1, 1);
    public float Metallic { get; set; } = 0.0f;
    public float Roughness { get; set; } = 0.5f;
    public Vector4 EmissiveColor { get; set; } = new Vector4(0, 0, 0, 0);
    // False when Unity's _EmissionEnabled is 0; default true for back-compat.
    public bool EmissionEnabled { get; set; } = true;
    // Sourced from Unity's _EmissionMinPower property.
    public float EmissionStrength { get; set; } = 1.0f;
    public int AlphaMode { get; set; } = 0; // 0=Opaque, 1=Cutout, 2=Fade, 3=Transparent
    public float AlphaCutoff { get; set; } = 0.5f;
    // True only when the shader explicitly uses Cull Off; default culls backfaces.
    public bool IsDoubleSided { get; set; } = false;
}

public class Vector4
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; } = 1.0f;

    public Vector4() { }

    public Vector4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }
}
