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

    // Process-wide so the install task and progress-polling endpoint share state even though
    // AddHttpClient<UpdateService>() registers the service as transient.
    public static volatile UpdateProgress? InstallProgress;

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
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Update check failed: network unreachable ({Reason})", ex.Message);
            throw;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Update check timed out");
            throw;
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
            InstallProgress = new UpdateProgress("error", "Update installation is disabled in development mode.");
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

            using (var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
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
            }

            _logger.LogInformation("Download complete. Extracting...");
            InstallProgress = new UpdateProgress("installing", "Installing update...");

            // Subdirectory keeps update.zip outside the copy source so the helper's robocopy/cp
            // does not carry it into the app directory.
            var extractDir = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var binaryName = GetBinaryName();
            var newBinary = Directory.GetFiles(extractDir, binaryName, SearchOption.AllDirectories)
                .FirstOrDefault();

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

        var persistLogPath = Path.Combine(appDir, "logs", $"update-helper-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        // PowerShell handles UTF-8 paths and arbitrary Unicode cleanly via -EncodedCommand,
        // unlike cmd.exe which would mangle characters outside the system OEM codepage.
        // Transcript goes to %TEMP% during the run and is only promoted to logs/ on failure,
        // so the logs/ directory stays empty on success — matches DiagnosticFileLogger's "log = something went wrong" convention.
        var psScript = $@"
$ErrorActionPreference = 'Continue'
$tempLog = [System.IO.Path]::Combine($env:TEMP, ""oee-update-helper-$([guid]::NewGuid().ToString('N')).log"")
$persistLog = '{persistLogPath.Replace("'", "''")}'
Start-Transcript -Path $tempLog -Force | Out-Null
Start-Sleep -Seconds 3
$rc = robocopy '{packageRoot.Replace("'", "''")}' '{appDir.Replace("'", "''")}' /E /R:3 /W:1 /XD '{appDir.Replace("'", "''")}\ExtractedAssets' '{appDir.Replace("'", "''")}\CustomAssets'
$rcExit = $LASTEXITCODE
if ($rcExit -ge 8) {{
    Write-Error ""robocopy failed with exit code $rcExit""
    Stop-Transcript | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $persistLog) | Out-Null
    Move-Item $tempLog $persistLog -Force -ErrorAction SilentlyContinue
    exit 1
}}
Remove-Item -Recurse -Force '{tempDir.Replace("'", "''")}' -ErrorAction SilentlyContinue
Start-Process '{currentPath.Replace("'", "''")}' -ArgumentList '--from-update'
Stop-Transcript | Out-Null
Remove-Item $tempLog -Force -ErrorAction SilentlyContinue
";

        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        Environment.Exit(0);
    }

    private void InstallUnix(string packageRoot, string currentPath, string tempDir)
    {
        try
        {
            var appDir = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(appDir)) return;

            var persistLogPath = Path.Combine(appDir, "logs", $"update-helper-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var scriptPath = Path.Combine(Path.GetTempPath(), $"oee-update-{Guid.NewGuid():N}.sh");
            // Helper waits for parent to exit (avoids ETXTBSY on the running binary), then copies,
            // relaunches, and cleans up. Output goes to a temp log during the run and is only promoted
            // to logs/ on failure, so the logs/ dir stays empty on success — matches DiagnosticFileLogger's
            // "log = something went wrong" convention.
            var script = $"""
                #!/bin/bash
                TEMP_LOG=$(mktemp /tmp/oee-update-helper-XXXXXX.log)
                PERSIST_LOG="{persistLogPath}"
                exec > "$TEMP_LOG" 2>&1
                echo "[update-helper] Started at $(date)"
                sleep 3
                echo "[update-helper] Copying from {packageRoot} to {appDir}"
                cp -rf "{packageRoot}/." "{appDir}/"
                CP_EXIT=$?
                if [ $CP_EXIT -ne 0 ]; then
                    echo "[update-helper] ERROR: cp failed with exit code $CP_EXIT"
                    mkdir -p "$(dirname "$PERSIST_LOG")"
                    mv "$TEMP_LOG" "$PERSIST_LOG"
                    exit 1
                fi
                chmod +x "{currentPath}"
                rm -rf "{tempDir}"
                echo "[update-helper] Launching {currentPath} --from-update"
                nohup "{currentPath}" --from-update > /dev/null 2>&1 &
                disown
                rm -f "{scriptPath}"
                echo "[update-helper] Done"
                rm -f "$TEMP_LOG"
                """;

            File.WriteAllText(scriptPath, script);

            var chmod = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{scriptPath}\"",
                UseShellExecute = false,
            });
            chmod?.WaitForExit();

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unix install failed");
            InstallProgress = new UpdateProgress("error", $"Install failed: {ex.Message}");
        }
    }

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
