#nullable enable
using System.Collections.Concurrent;
using System.IO.Hashing;

namespace AssetExtractor.Pipeline;

// XXHash64-based deduplication, safe for parallel extraction. Returns 16-char lowercase
// hex digests.
public class DeduplicationService
{
    private readonly ConcurrentDictionary<string, string> _savedFiles = new();

    public static string ComputeHash(byte[] data)
    {
        if (data == null || data.Length == 0)
            return string.Empty;

        var xxHash = new XxHash64();
        xxHash.Append(data);
        var hashBytes = xxHash.GetHashAndReset();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string ComputeHash(byte[] data, int offset, int length)
    {
        if (data == null || length == 0)
            return string.Empty;

        var xxHash = new XxHash64();
        xxHash.Append(data.AsSpan(offset, length));
        var hashBytes = xxHash.GetHashAndReset();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string ComputeFileHash(string filePath)
    {
        if (!File.Exists(filePath))
            return string.Empty;

        var xxHash = new XxHash64();
        using var stream = File.OpenRead(filePath);

        byte[] buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            xxHash.Append(buffer.AsSpan(0, bytesRead));
        }

        var hashBytes = xxHash.GetHashAndReset();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    // Returns Success on first write, Duplicate when the same hash already occupies the
    // path, or ConflictResolved when actualPath got rerouted to a numeric-suffixed slot
    // because a different hash held the original path.
    public PathReservationResult TryReservePath(string targetPath, string hash, out string actualPath)
    {
        targetPath = Path.GetFullPath(targetPath);

        if (_savedFiles.TryAdd(targetPath, hash))
        {
            actualPath = targetPath;
            return PathReservationResult.Success;
        }

        if (_savedFiles.TryGetValue(targetPath, out var existingHash) && existingHash == hash)
        {
            actualPath = targetPath;
            return PathReservationResult.Duplicate;
        }

        actualPath = FindConflictPath(targetPath, hash);
        return PathReservationResult.ConflictResolved;
    }

    private string FindConflictPath(string basePath, string hash)
    {
        var dir = Path.GetDirectoryName(basePath) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);

        int suffix = 0;
        const int maxAttempts = 1000;

        while (suffix < maxAttempts)
        {
            var newPath = Path.Combine(dir, $"{nameWithoutExt}_{suffix}{ext}");

            if (_savedFiles.TryAdd(newPath, hash))
            {
                return newPath;
            }

            if (_savedFiles.TryGetValue(newPath, out var existingHash) && existingHash == hash)
            {
                return newPath;
            }

            suffix++;
        }

        throw new InvalidOperationException($"Too many conflicts for path: {basePath}");
    }

    public bool IsPathReserved(string path)
    {
        return _savedFiles.ContainsKey(Path.GetFullPath(path));
    }

    public string? GetPathHash(string path)
    {
        return _savedFiles.TryGetValue(Path.GetFullPath(path), out var hash) ? hash : null;
    }

    public void RegisterExistingFile(string path, string hash)
    {
        _savedFiles.TryAdd(Path.GetFullPath(path), hash);
    }

    public void ClearReservations()
    {
        _savedFiles.Clear();
    }

    public int ReservedPathCount => _savedFiles.Count;

    public IReadOnlyDictionary<string, string> GetAllReservations()
    {
        return _savedFiles;
    }
}

public enum PathReservationResult
{
    Success,
    Duplicate,
    ConflictResolved
}
