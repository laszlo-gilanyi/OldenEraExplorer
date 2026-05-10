#nullable enable
namespace AssetExtractor.Extraction;

internal static class ShaderProperties
{
    public const string MainTex = "_MainTex";
    public const string BaseMap = "_BaseMap";

    public const string EmissionTex = "_EmissionTex";
    public const string EmissionMap = "_EmissionMap";
    public const string EmissiveMap = "_EmissiveMap";

    public const string Color = "_Color";
    public const string BaseColor = "_BaseColor";
    public const string EmissionColor = "_EmissionColor";

    public const string Metallic = "_Metallic";
    public const string Glossiness = "_Glossiness";
    public const string Smoothness = "_Smoothness";

    public const string Mode = "_Mode";
    public const string Cutoff = "_Cutoff";
    public const string AlphaClipEnabled = "_AlphaClipEnabled";
    public const string AlphaCutoff = "_AlphaCutoff";

    public const string EmissionEnabled = "_EmissionEnabled";
    public const string EmissionMinPower = "_EmissionMinPower";

    public const string Cull = "_Cull";

    public static readonly string[] MainTexCandidates = { MainTex, BaseMap };
    public static readonly string[] EmissiveTexCandidates = { EmissionTex, EmissionMap, EmissiveMap };
}

// Read from m_ValidKeywords / m_LegacyShaderKeywords on the Material.
internal static class ShaderKeywords
{
    public const string HexEmissionMapEnabled = "HEX_EMISSION_MAP_ENABLED";
    public const string HexAlphaClipEnabled = "HEX_ALPHA_CLIP_ENABLED";
    public const string HexDitherFadeEnabled = "HEX_DITHER_FADE_ENABLED";
}
