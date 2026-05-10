using System.Collections.Generic;
using System.Numerics;

namespace UnityReader;

/// <summary>Sequence of position / scale keyframes (per-channel float triples lifted into Vector3).</summary>
public sealed record Vector3Curve(IReadOnlyList<Keyframe<Vector3>> Keyframes);

/// <summary>Sequence of rotation keyframes encoded as Quaternions.</summary>
public sealed record QuaternionCurve(IReadOnlyList<Keyframe<Quaternion>> Keyframes);
