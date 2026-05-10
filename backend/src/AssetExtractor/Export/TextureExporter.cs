#nullable enable
using System.IO.Hashing;
using AssetExtractor.Models;
using AssetExtractor.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetExtractor.Export;

public class TextureExporter
{
    private readonly ILogger<TextureExporter> _logger;
    private readonly string _outputPath;
    private readonly ManifestManager _manifestService;

    // Vendor texture-data fetch reads shared file streams that are not thread-safe.
    // Encoding and disk I/O still run in parallel; only the raw read is serialized.
    internal static readonly object TextureConversionLock = new();

    public TextureExporter(
        string outputPath,
        ManifestManager manifestService,
        ILogger<TextureExporter>? logger = null)
    {
        _logger = logger ?? NullLogger<TextureExporter>.Instance;
        _outputPath = outputPath;
        _manifestService = manifestService;
    }

    private bool ValidateTextureData(TextureData? textureData)
    {
        if (textureData == null || textureData.ImageData == null || textureData.ImageData.Length == 0)
        {
            _logger.LogWarning("Cannot export texture: no image data");
            return false;
        }
        return true;
    }

    public string? ExportTexture(TextureData textureData, string relativePath, string version)
    {
        if (!ValidateTextureData(textureData))
            return null;

        try
        {
            var versionOutputPath = _manifestService.GetVersionOutputPath(version);
            var fullPath = Path.Combine(versionOutputPath, relativePath + ".png");

            WriteTextureFile(fullPath, textureData.ImageData);
            _logger.LogInformation("Exported texture: {FullPath}", fullPath);

            var hash = ComputeHash(textureData.ImageData);
            _manifestService.AddOrUpdateVariant(
                relativePath,
                version,
                hash,
                textureData.ImageData.Length,
                "Texture2D",
                ".png");

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export texture '{TextureName}'", textureData.Name);
            return null;
        }
    }

    private static string ComputeHash(byte[] data)
    {
        var hash = XxHash64.HashToUInt64(data);
        return hash.ToString("x16");
    }

    // Streams PNG bytes directly to disk while hashing inline; the encoded image is
    // never buffered in memory. UnityTexture.DecodeRgba8 already returns top-down pixels.
    public string? ExportTexture(UnityReader.UnityTexture texture, string relativePath, string version)
    {
        using (_logger.BeginScope("Texture: {TexturePath}", relativePath))
        {
            try
            {
                var name = texture.Name;
                var width = texture.Width;
                var height = texture.Height;
                _logger.LogInformation("Exporting texture (new reader): {TextureName} ({Width}x{Height})", name, width, height);

                var rgba8 = texture.DecodeRgba8();
                using var bitmap = Image.LoadPixelData<Rgba32>(rgba8, width, height);

                var versionOutputPath = _manifestService.GetVersionOutputPath(version);
                var fullPath = Path.Combine(versionOutputPath, relativePath + ".png");
                EnsureDirectory(fullPath);

                using var fileStream = File.Create(fullPath);
                using var hashingStream = new HashingStream(fileStream);
                bitmap.SaveAsPng(hashingStream);
                hashingStream.Flush();

                var hash = hashingStream.GetHashHex();
                var byteCount = hashingStream.BytesWritten;
                _manifestService.AddOrUpdateVariant(
                    relativePath, version, hash, byteCount, "Texture2D", ".png");

                _logger.LogInformation("Exported texture: {FullPath}", fullPath);
                return fullPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export texture (new reader) at '{RelativePath}'", relativePath);
                return null;
            }
        }
    }

    private static void EnsureDirectory(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private void WriteTextureFile(string fullPath, byte[] data)
    {
        EnsureDirectory(fullPath);
        File.WriteAllBytes(fullPath, data);
    }
}

// Pass-through write stream that feeds every byte into XxHash64 before forwarding,
// letting encode-write-hash run as a single pass without buffering.
internal sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly XxHash64 _hash = new();
    private long _bytesWritten;

    public HashingStream(Stream inner) { _inner = inner; }

    public long BytesWritten => _bytesWritten;
    public string GetHashHex() => _hash.GetCurrentHashAsUInt64().ToString("x16");

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _bytesWritten;
    public override long Position { get => _bytesWritten; set => throw new NotSupportedException(); }

    public override void Flush() => _inner.Flush();

    public override void Write(byte[] buffer, int offset, int count)
    {
        _hash.Append(new ReadOnlySpan<byte>(buffer, offset, count));
        _inner.Write(buffer, offset, count);
        _bytesWritten += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _hash.Append(buffer);
        _inner.Write(buffer);
        _bytesWritten += buffer.Length;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
