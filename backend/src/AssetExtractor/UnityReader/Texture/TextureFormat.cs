namespace UnityReader;

// Values match Unity's canonical UnityEngine.TextureFormat IDs so they can be cast
// directly from raw m_TextureFormat readings. Anything outside this enum reaches
// TextureDecoder.Decode as NotSupportedException rather than silent miss-decode.
public enum TextureFormat
{
    RGB24 = 3,    // R8 G8 B8, alpha implicit 255
    RGBA32 = 4,   // R8 G8 B8 A8
    DXT1 = 10,    // BC1 / S3TC, 4bpp, opaque or 1-bit alpha
    DXT5 = 12,    // BC3 / S3TC, 8bpp, full alpha
    BC7 = 25,     // 8bpp, high-quality compressed RGBA
}
