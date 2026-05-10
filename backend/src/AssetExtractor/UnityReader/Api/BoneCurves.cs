namespace UnityReader;

// BonePath is the slash-separated transform path relative to the Animator root (e.g.
// "Hips/Spine/Chest/Head"); CRC32 of this string is the binding hash AnimationClip stores.
// Position/Rotation/Scale are null when the clip does not animate that channel for this bone.
public sealed record BoneCurves(
    string BonePath,
    Vector3Curve? Position,
    QuaternionCurve? Rotation,
    Vector3Curve? Scale);
