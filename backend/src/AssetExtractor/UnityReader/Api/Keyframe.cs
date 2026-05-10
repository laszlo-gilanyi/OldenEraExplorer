namespace UnityReader;

// Time is clip-local seconds. Slopes use Unity's Bezier convention (dValue/dTime),
// not glTF Hermite tangents.
public readonly record struct Keyframe<T>(float Time, T Value, T InSlope, T OutSlope);
