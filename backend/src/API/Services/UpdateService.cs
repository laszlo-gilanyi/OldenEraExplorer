using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace API.Services;

public class UpdateService
{
    public static string CurrentVersion =>
        NormalizeVersion(
            typeof(UpdateService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
            ?? typeof(UpdateService).Assembly.GetName().Version?.ToString()
            ?? "0.0.0");

    private const string RepoOwner = "laszlo-gilanyi";
    private const string RepoName = "OldenEraExplorer";

    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private readonly IWebHostEnvironment _env;

    public volatile UpdateProgress? InstallProgress;

    public UpdateService(HttpClient httpClient, ILogger<UpdateService> logger, IWebHostEnvironment env)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "OldenEraExplorer-Updater");
        _logger = logger;
        _env = env;
    }

    public async Task<ReleaseInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub API returned {StatusCode} for update check", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var gh = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (gh == null) return null;

            _logger.LogInformation("Latest release: {Tag}, current: {Current}", gh.TagName, CurrentVersion);

            if (!IsNewer(gh.TagName, CurrentVersion)) return null;

            return new ReleaseInfo
            {
                TagName = gh.TagName,
                HtmlUrl = gh.HtmlUrl,
                Assets = gh.Assets.Select(a => new ReleaseAsset
                {
                    Name = a.Name,
                    BrowserDownloadUrl = a.BrowserDownloadUrl
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            return null;
        }
    }

    public async Task DownloadAndInstallUpdateAsync(ReleaseInfo release)
    {
        if (_env.IsDevelopment())
        {
            _logger.LogWarning("Update installation is disabled in development mode.");
            return;
        }

        var asset = FindMatchingAsset(release);
        if (asset == null)
        {
            _logger.LogWarning("No matching asset found for current platform in release {Tag}", release.TagName);
            InstallProgress = new UpdateProgress("error", "No matching download found for this platform.");
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"OldenEraExplorer-Update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, "update.zip");

        try
        {
            _logger.LogInformation("Downloading {Url}", asset.BrowserDownloadUrl);
            InstallProgress = new UpdateProgress("downloading", "Downloading update...");

            using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;
            await using var fs = File.Create(zipPath);
            await using var stream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                {
                    var pct = (int)(downloaded * 100 / total);
                    InstallProgress = new UpdateProgress("downloading", $"Downloading... {pct}%");
                }
            }

            _logger.LogInformation("Download complete. Extracting...");
            InstallProgress = new UpdateProgress("installing", "Installing update...");

            ZipFile.ExtractToDirectory(zipPath, tempDir);

            var binaryName = GetBinaryName();
            var newBinary = Directory.GetFiles(tempDir, binaryName, SearchOption.AllDirectories)
                .FirstOrDefault(p => !string.Equals(p, zipPath, StringComparison.OrdinalIgnoreCase));

            if (newBinary == null)
            {
                _logger.LogError("Binary '{Name}' not found in update package", binaryName);
                InstallProgress = new UpdateProgress("error", "Update package is missing the application binary.");
                return;
            }

            var packageRoot = Path.GetDirectoryName(newBinary)!;
            _logger.LogInformation("Installing from {PackageRoot}", packageRoot);
            InstallUpdate(packageRoot, tempDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download or install update");
            InstallProgress = new UpdateProgress("error", $"Update failed: {ex.Message}");

            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static void CleanupOldFiles()
    {
        try
        {
            var currentPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentPath)) return;

            var oldPath = currentPath + ".old";
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
                Console.WriteLine("Cleaned up leftover .old binary.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup of old binary failed: {ex.Message}");
        }
    }

    private void InstallUpdate(string packageRoot, string tempDir)
    {
        var currentPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(currentPath))
        {
            _logger.LogError("Cannot determine current executable path — update aborted.");
            InstallProgress = new UpdateProgress("error", "Cannot determine executable path.");
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            InstallWindows(packageRoot, currentPath, tempDir);
        else
            InstallUnix(packageRoot, currentPath, tempDir);
    }

    private void InstallWindows(string packageRoot, string currentPath, string tempDir)
    {
        var appDir = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrEmpty(appDir)) return;

        var batchPath = Path.Combine(Path.GetTempPath(), $"oee-update-{Guid.NewGuid():N}.bat");
        var batch = $"""
            @echo off
            setlocal
            timeout /t 2 /nobreak >nul
            robocopy "{packageRoot}" "{appDir}" /E /R:3 /W:1 /XD "{appDir}\ExtractedAssets" "{appDir}\CustomAssets"
            if %ERRORLEVEL% GEQ 8 (echo robocopy failed & exit /b 1)
            rd /s /q "{tempDir}"
            start "" "{currentPath}"
            del "%~f0"
            """;

        File.WriteAllText(batchPath, batch);
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batchPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        Environment.Exit(0);
    }

    private void InstallUnix(string packageRoot, string currentPath, string tempDir)
    {
        try
        {
            var appDir = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(appDir)) return;

            CopyDirectory(packageRoot, appDir);

            var chmod = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{currentPath}\"",
                UseShellExecute = false,
            });
            chmod?.WaitForExit();

            try { Directory.Delete(tempDir, recursive: true); } catch { }

            Process.Start(new ProcessStartInfo
            {
                FileName = currentPath,
                UseShellExecute = true,
            });

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unix install failed");
            InstallProgress = new UpdateProgress("error", $"Install failed: {ex.Message}");
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExtractedAssets", "CustomAssets" };

        Directory.CreateDirectory(dst);

        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            if (HasExcludedSegment(rel, excluded)) continue;
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }

        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            if (HasExcludedSegment(rel, excluded)) continue;

            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool HasExcludedSegment(string rel, HashSet<string> excluded) =>
        rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(excluded.Contains);

    private static ReleaseAsset? FindMatchingAsset(ReleaseInfo release)
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "";
        if (string.IsNullOrEmpty(os)) return null;

        return release.Assets.FirstOrDefault(a =>
            a.Name.Contains(os, StringComparison.OrdinalIgnoreCase) &&
            a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNewer(string latest, string current)
    {
        var l = NormalizeVersion(latest);
        var c = NormalizeVersion(current);
        return Version.TryParse(l, out var vl) && Version.TryParse(c, out var vc)
            ? vl > vc
            : string.Compare(l, c, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static string NormalizeVersion(string v) =>
        v.Trim().TrimStart('v', 'V').Split('+')[0];

    private static string GetBinaryName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "OldenEraExplorer.exe" : "OldenEraExplorer";
}

public class UpdateProgress(string stage, string message)
{
    public string Stage { get; } = stage;    // "downloading" | "installing" | "error"
    public string Message { get; } = message;
}

// Outward-facing DTO — no [JsonPropertyName], so ASP.NET Core serializes with camelCase
public class ReleaseInfo
{
    public string TagName { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public List<ReleaseAsset> Assets { get; set; } = [];
}

public class ReleaseAsset
{
    public string Name { get; set; } = "";
    public string BrowserDownloadUrl { get; set; } = "";
}

// Internal type for deserializing GitHub API response (snake_case)
file class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    public List<GitHubAsset> Assets { get; set; } = [];
}

file class GitHubAsset
{
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
