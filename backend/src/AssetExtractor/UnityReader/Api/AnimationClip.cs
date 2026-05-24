namespace UnityReader;

// Metadata only; per-bone curve resolution lives on Animator.ProcessClip because the
// bone-path checksums are anchor-Animator-relative.
public sealed class AnimationClip
{
    private readonly AssetStudio.AnimationClip _vendor;

    internal AnimationClip(AssetStudio.AnimationClip vendor) => _vendor = vendor;

    public string Name => _vendor.m_Name ?? string.Empty;
    public long PathId => _vendor.m_PathID;

    public string SourceFile =>
        System.IO.Path.GetFileName(_vendor.assetsFile?.fileName ?? string.Empty);

    public float SampleRate => _vendor.m_SampleRate;
    public bool IsLegacy => _vendor.m_Legacy;
    public bool HasMuscleClip => _vendor.m_MuscleClip != null;
    public bool HasClipBindingConstant => _vendor.m_ClipBindingConstant != null;

    // Returns 0 for legacy / non-skeletal clips that lack the MuscleClip layout.
    public float Length =>
        _vendor.m_MuscleClip != null
            ? _vendor.m_MuscleClip.m_StopTime - _vendor.m_MuscleClip.m_StartTime
            : 0f;

    internal AssetStudio.AnimationClip Vendor => _vendor;
}
