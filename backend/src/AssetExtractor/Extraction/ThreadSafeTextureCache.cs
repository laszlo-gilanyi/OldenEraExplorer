#nullable enable
using System.Collections.Concurrent;
using AssetExtractor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AssetExtractor.Extraction;

public class ThreadSafeTextureCache
{
    private readonly ILogger<ThreadSafeTextureCache> _logger;
    private readonly ConcurrentDictionary<long, Lazy<TextureData?>> _cache = new();

    public ThreadSafeTextureCache(ILogger<ThreadSafeTextureCache>? logger = null)
    {
        _logger = logger ?? NullLogger<ThreadSafeTextureCache>.Instance;
    }

    public TextureData? GetOrLoad(UnityReader.UnityTexture texture)
    {
        var lazy = _cache.GetOrAdd(texture.PathId, _ => new Lazy<TextureData?>(() => LoadTextureWithLock(texture)));
        return lazy.Value;
    }

    public TextureData? TryGetCached(long pathId)
    {
        if (_cache.TryGetValue(pathId, out var lazy) && lazy.IsValueCreated) return lazy.Value;
        return null;
    }

    public bool IsCached(long pathId) => _cache.TryGetValue(pathId, out var lazy) && lazy.IsValueCreated;

    public IEnumerable<TextureData> GetAllCached()
    {
        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsValueCreated && kvp.Value.Value != null)
                yield return kvp.Value.Value;
        }
    }

    public void Clear() => _cache.Clear();

    public int Count => _cache.Count(kvp => kvp.Value.IsValueCreated && kvp.Value.Value != null);

    private TextureData? LoadTextureWithLock(UnityReader.UnityTexture texture)
    {
        var textureData = new TextureData
        {
            Name = texture.Name,
            Width = texture.Width,
            Height = texture.Height
        };

        try
        {
            var rgba8 = texture.DecodeRgba8();
            using var bitmap = SixLabors.ImageSharp.Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                rgba8, texture.Width, texture.Height);

            // GLB embedding wants Unity-native bottom-up; DecodeRgba8 returns top-down
            // (PNG convention), so flip back. Standalone PNG export bypasses this cache.
            bitmap.Mutate(c => c.Flip(FlipMode.Vertical));

            using var ms = new MemoryStream();
            bitmap.SaveAsPng(ms);
            // GetBuffer + length skips the ToArray() copy; the MemoryStream is local.
            var buffer = ms.GetBuffer();
            var len = (int)ms.Length;
            var pngBytes = new byte[len];
            Buffer.BlockCopy(buffer, 0, pngBytes, 0, len);
            textureData.ImageData = pngBytes;
            textureData.Format = "PNG";

            return textureData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract texture '{TextureName}'", textureData.Name);
            return null;
        }
    }
}
