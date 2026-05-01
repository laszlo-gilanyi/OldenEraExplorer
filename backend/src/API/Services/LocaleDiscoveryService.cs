using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using API.Utilities;

namespace API.Services;

public class LocaleDiscoveryService
{
    private readonly ILogger<LocaleDiscoveryService> _logger;

    public LocaleDiscoveryService(ILogger<LocaleDiscoveryService>? logger = null)
    {
        _logger = logger ?? NullLogger<LocaleDiscoveryService>.Instance;
    }

    public Task<List<string>> DiscoverLocalesAsync(
        string streamingAssetsRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(streamingAssetsRoot))
        {
            _logger.LogWarning("StreamingAssets root path is empty, returning empty locale list");
            return Task.FromResult(new List<string>());
        }

        return Task.Run(() => DiscoverLocales(streamingAssetsRoot, cancellationToken), cancellationToken);
    }

    private List<string> DiscoverLocales(string streamingAssetsRoot, CancellationToken cancellationToken)
    {
        var locales = new List<string>();
        var langDir = Path.Combine(streamingAssetsRoot, "Lang");

        if (Directory.Exists(langDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(langDir))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var localeName = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(localeName))
                    continue;

                if (Directory.Exists(Path.Combine(dir, "texts")))
                    locales.Add(localeName);
                else
                    _logger.LogDebug("Skipping incomplete locale (no texts/ folder): {Locale}", localeName);
            }
        }
        else
        {
            // EA build: Lang is inside Core.zip
            var coreZipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
            if (!File.Exists(coreZipPath))
            {
                _logger.LogWarning("Neither Lang directory nor Core.zip found in: {Root}", streamingAssetsRoot);
                return locales;
            }

            using var zip = ZipFile.OpenRead(coreZipPath);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Match: Lang/<locale>/texts/<file>.json
                var parts = entry.FullName.Split('/');
                if (parts.Length >= 4 &&
                    parts[0].Equals("Lang", StringComparison.OrdinalIgnoreCase) &&
                    parts[2].Equals("texts", StringComparison.OrdinalIgnoreCase) &&
                    entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    seen.Add(parts[1]);
                }
            }

            locales.AddRange(seen);
        }

        var comparer = new NaturalStringComparer();
        var sorted = locales
            .OrderBy(x => x.Equals("english", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x, comparer)
            .ToList();

        _logger.LogInformation("Discovered {Count} valid locales: {Locales}",
            sorted.Count,
            string.Join(", ", sorted));

        return sorted;
    }
}
