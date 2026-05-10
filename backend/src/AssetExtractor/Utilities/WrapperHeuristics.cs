#nullable enable
namespace AssetExtractor.Utilities;

// Detects the empty parent nodes EA inserts above mesh roots to host scale tweens or
// 90-degree rotations. Used by AssetLoader (load-time scoring) and HierarchyExtractor
// (runtime filtering).
internal static class WrapperHeuristics
{
    // sqrt(2)/2 is the magnitude Y and W take for a 90-degree Y-axis rotation; geometric
    // children typically carry identity, so this value reliably distinguishes wrappers.
    public const float WrapperYRotation = 0.707107f;
    public const float Tolerance = 0.0001f;

    public static bool IsWrapperRotation(float x, float y, float z, float w)
    {
        return System.MathF.Abs(x) < Tolerance
            && System.MathF.Abs(z) < Tolerance
            && System.MathF.Abs(System.MathF.Abs(y) - WrapperYRotation) < Tolerance
            && System.MathF.Abs(System.MathF.Abs(w) - WrapperYRotation) < Tolerance;
    }

    public static bool IsWrapperRotation(System.Numerics.Quaternion q)
        => IsWrapperRotation(q.X, q.Y, q.Z, q.W);

    public static bool IsWrapperRotation(Models.Quaternion q)
        => IsWrapperRotation(q.X, q.Y, q.Z, q.W);
}
