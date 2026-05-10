using System;
using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Dxt;
using AssetRipper.TextureDecoder.Rgb.Formats;

namespace UnityReader;

// Output is top-down RGBA8 (PNG convention); Unity stores pixels bottom-up so the
// last step flips along Y.
public static class TextureDecoder
{
    // Returns a freshly-allocated buffer of length width*height*4. Throws
    // NotSupportedException for any format outside the TextureFormat enum.
    public static byte[] Decode(ReadOnlySpan<byte> data, TextureFormat format, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Invalid dimensions: {width}x{height}");

        var pixelByteCount = checked(width * height * 4);

        var rgba = format switch
        {
            TextureFormat.RGBA32 => DecodeRgba32(data, pixelByteCount),
            TextureFormat.RGB24  => DecodeRgb24(data, width, height),
            TextureFormat.DXT1   => DecodeDxt1(data, width, height),
            TextureFormat.DXT5   => DecodeDxt5(data, width, height),
            TextureFormat.BC7    => DecodeBc7(data, width, height),
            _                    => throw new NotSupportedException(
                $"TextureFormat.{format} is not in the supported set (RGB24, RGBA32, DXT1, DXT5, BC7)."),
        };

        FlipYInPlace(rgba, width, height);
        return rgba;
    }

    private static byte[] DecodeRgba32(ReadOnlySpan<byte> data, int pixelByteCount)
    {
        if (data.Length < pixelByteCount)
            throw new ArgumentException($"RGBA32 buffer too small: have {data.Length}, need {pixelByteCount}");
        var rgba = new byte[pixelByteCount];
        data.Slice(0, pixelByteCount).CopyTo(rgba);
        return rgba;
    }

    private static byte[] DecodeRgb24(ReadOnlySpan<byte> data, int width, int height)
    {
        var pixelCount = width * height;
        var srcByteCount = pixelCount * 3;
        if (data.Length < srcByteCount)
            throw new ArgumentException($"RGB24 buffer too small: have {data.Length}, need {srcByteCount}");

        var rgba = new byte[pixelCount * 4];
        for (var i = 0; i < pixelCount; i++)
        {
            rgba[i * 4 + 0] = data[i * 3 + 0];
            rgba[i * 4 + 1] = data[i * 3 + 1];
            rgba[i * 4 + 2] = data[i * 3 + 2];
            rgba[i * 4 + 3] = 0xFF;
        }
        return rgba;
    }

    private static byte[] DecodeDxt1(ReadOnlySpan<byte> data, int width, int height)
    {
        DxtDecoder.DecompressDXT1<ColorRGBA<byte>, byte>(data, width, height, out var output);
        return output;
    }

    private static byte[] DecodeDxt5(ReadOnlySpan<byte> data, int width, int height)
    {
        DxtDecoder.DecompressDXT5<ColorRGBA<byte>, byte>(data, width, height, out var output);
        return output;
    }

    private static byte[] DecodeBc7(ReadOnlySpan<byte> data, int width, int height)
    {
        Bc7.Decompress<ColorRGBA<byte>, byte>(data, width, height, out var output);
        return output;
    }

    private static void FlipYInPlace(byte[] rgba, int width, int height)
    {
        var rowLen = width * 4;
        Span<byte> tmp = stackalloc byte[1024];
        byte[]? heapTmp = rowLen > tmp.Length ? new byte[rowLen] : null;
        for (var y = 0; y < height / 2; y++)
        {
            var top = y * rowLen;
            var bot = (height - 1 - y) * rowLen;
            var scratch = heapTmp != null ? heapTmp.AsSpan(0, rowLen) : tmp.Slice(0, rowLen);
            rgba.AsSpan(top, rowLen).CopyTo(scratch);
            rgba.AsSpan(bot, rowLen).CopyTo(rgba.AsSpan(top, rowLen));
            scratch.CopyTo(rgba.AsSpan(bot, rowLen));
        }
    }
}
