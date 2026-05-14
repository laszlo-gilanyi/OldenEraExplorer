#nullable enable
using System.Text.RegularExpressions;
using AssetExtractor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Providers;

public class UnitListProvider : IPrefabListProvider
{
    private readonly ILogger<UnitListProvider> _logger;
    private readonly string _assetPath;

    public UnitListProvider(string assetPath, ILogger<UnitListProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<UnitListProvider>.Instance;
        _assetPath = assetPath;
    }

    public PrefabType PrefabType => PrefabType.Unit;

    public List<PrefabDescriptor> ListPrefabs()
    {
        var prefabs = new List<PrefabDescriptor>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var globalGameManagersPath = Path.Combine(_assetPath, "globalgamemanagers");
        if (!File.Exists(globalGameManagersPath))
            globalGameManagersPath = Path.Combine(_assetPath, "globalgamemanagers.assets");

        if (!File.Exists(globalGameManagersPath))
            throw new FileNotFoundException($"globalgamemanagers not found under asset path: {_assetPath}");

        foreach (var token in EnumeratePrintableStrings(globalGameManagersPath, minLength: 8))
        {
            if (TryParseUnitPrefabDescriptor(token, out var descriptor))
            {
                if (!seenNames.Contains(descriptor.Name))
                {
                    seenNames.Add(descriptor.Name);
                    prefabs.Add(descriptor);
                }
            }
        }

        if (prefabs.Count == 0)
        {
            throw new Exception(
                "No unit prefab paths found in globalgamemanagers. Expected strings like " +
                "'units/<faction>/1prefabs/<unit>' in the Unity data files.");
        }

        _logger.LogInformation(
            "Loaded {UnitCount} units from {GlobalGameManagersPath}",
            prefabs.Count,
            globalGameManagersPath);

        return prefabs
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    public List<string> ListUnits()
    {
        return ListPrefabs()
            .Select(p => p.Name)
            .ToList();
    }

    private static IEnumerable<string> EnumeratePrintableStrings(string filePath, int minLength)
    {
        using var stream = File.OpenRead(filePath);

        byte[] buffer = new byte[64 * 1024];
        var currentBytes = new List<byte>();

        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < bytesRead; i++)
            {
                byte b = buffer[i];
                if ((b >= 0x20 && b <= 0x7E) || (b >= 0x80 && b <= 0xF7))
                {
                    currentBytes.Add(b);
                    continue;
                }

                if (currentBytes.Count >= minLength)
                {
                    var str = TryDecodeUtf8(currentBytes);
                    if (str != null && str.Length >= minLength)
                        yield return str;
                }

                currentBytes.Clear();
            }
        }

        if (currentBytes.Count >= minLength)
        {
            var str = TryDecodeUtf8(currentBytes);
            if (str != null && str.Length >= minLength)
                yield return str;
        }
    }

    private static string? TryDecodeUtf8(List<byte> bytes)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseUnitPrefabDescriptor(string assetPath, out PrefabDescriptor descriptor)
    {
        descriptor = new PrefabDescriptor();

        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        string normalized = assetPath.Trim().Replace('\\', '/');
        string lower = normalized.ToLowerInvariant();

        const string marker = "/1prefabs/";
        int markerIndex = lower.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return false;

        if (!lower.StartsWith("units/", StringComparison.Ordinal) &&
            !lower.StartsWith("assets/resources/units/", StringComparison.Ordinal))
        {
            return false;
        }

        string after = normalized[(markerIndex + marker.Length)..].TrimStart('/');
        string name = after.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            name = name[..^".prefab".Length];

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                continue;
            return false;
        }

        var lowerName = name.ToLowerInvariant();

        // Skip map/material variants
        if (Regex.IsMatch(lowerName, @"_(map|mt|mat)(_\d+)?$"))
        {
            return false;
        }

        string faction = string.Empty;
        int unitsIndex = lower.IndexOf("units/", StringComparison.Ordinal);
        if (unitsIndex >= 0)
        {
            int factionStart = unitsIndex + "units/".Length;
            int factionEnd = lower.IndexOf("/", factionStart, StringComparison.Ordinal);
            if (factionEnd > factionStart)
            {
                faction = normalized[factionStart..factionEnd];
            }
        }

        descriptor = new PrefabDescriptor
        {
            Name = lowerName,
            Type = PrefabType.Unit,
            Category = faction,
            ResourcePath = normalized
        };

        return true;
    }

}
