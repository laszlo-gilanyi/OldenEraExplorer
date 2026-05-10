using System.Numerics;
using AsVec2 = AssetStudio.Vector2;
using AsVec3 = AssetStudio.Vector3;
using AsVec4 = AssetStudio.Vector4;
using AsQuat = AssetStudio.Quaternion;
using AsMat4 = AssetStudio.Matrix4x4;

namespace UnityReader.Internal;

// Vendor (AssetStudio) numeric types never leak across the public UnityReader.Api surface.
internal static class VendorConversions
{
    public static Vector2 ToNumerics(this AsVec2 v) => new(v.X, v.Y);
    public static Vector3 ToNumerics(this AsVec3 v) => new(v.X, v.Y, v.Z);
    public static Vector4 ToNumerics(this AsVec4 v) => new(v.X, v.Y, v.Z, v.W);
    public static Quaternion ToNumerics(this AsQuat q) => new(q.X, q.Y, q.Z, q.W);

    // Both sides use row-i column-j semantically; vendor is 0-indexed (M00..M33),
    // System.Numerics is 1-indexed (M11..M44). Constructor takes 16 floats row-by-row.
    public static Matrix4x4 ToNumerics(this AsMat4 m) => new(
        m.M00, m.M01, m.M02, m.M03,
        m.M10, m.M11, m.M12, m.M13,
        m.M20, m.M21, m.M22, m.M23,
        m.M30, m.M31, m.M32, m.M33);
}
