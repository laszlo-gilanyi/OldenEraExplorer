using System;

namespace UnityReader;

// Metadata is eager; raw pixel data is fetched on demand via LoadRawData / DecodeRgba8
// so iterating thousands of textures does not pull all of them into memory at once.
public sealed class UnityTexture
{
    private readonly UnityScene _scene;
    private readonly AssetStudio.Texture2D _vendor;

    internal UnityTexture(UnityScene scene, AssetStudio.Texture2D vendor, TextureFormat format)
    {
        _scene = scene;
        _vendor = vendor;
        Format = format;
    }

    public string Name => _vendor.m_Name ?? string.Empty;

    public long PathId => _vendor.m_PathID;

    public string SourceFile =>
        System.IO.Path.GetFileName(_vendor.assetsFile?.fileName ?? string.Empty);

    // ResourceManager-registered path or null if the texture is not bound to a runtime
    // resource slot. The OEE pipeline uses this to place textures under
    // Assets/Resources/{category}/... instead of a flat Texture2D/ folder.
    public string? ResourcePath => _scene.ResolveResourcePath(_vendor);

    public int Width => _vendor.m_Width;
    public int Height => _vendor.m_Height;
    public TextureFormat Format { get; }

    // Streamed textures (Unity StreamingInfo) reopen the referenced .resS file every call,
    // so callers should cache the result if they need it more than once.
    public byte[] LoadRawData()
    {
        return _vendor.image_data?.GetData() ?? Array.Empty<byte>();
    }

    // Top-down RGBA8 buffer of length Width * Height * 4.
    public byte[] DecodeRgba8()
    {
        var raw = LoadRawData();
        return TextureDecoder.Decode(raw, Format, Width, Height);
    }
}
