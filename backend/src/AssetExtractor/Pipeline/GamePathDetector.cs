#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetExtractor.Pipeline;

/// <summary>
/// Detection result containing selected path and all candidates found.
/// </summary>
public sealed class DetectionResult
{
    public string? SelectedPath { get; set; }
    public List<Candidate> AllCandidates { get; set; } = new();
    public DetectionMethod DetectionMethod { get; set; }
}

/// <summary>
/// Candidate game installation with score and metadata.
/// </summary>
public sealed class Candidate
{
    public string GameRoot { get; set; } = string.Empty;
    public string HeroesOEDataPath { get; set; } = string.Empty;
    public int Score { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Method used to detect the game path.
/// </summary>
public enum DetectionMethod
{
    AutoDetected,
    FromSettings,
    ManuallySpecified
}

/// <summary>
/// Detects Heroes of Might & Magic Olden Era game installation path.
/// Supports Windows, Linux, and macOS with Steam library detection.
/// </summary>
public sealed class GamePathDetector
{
    private readonly ILogger<GamePathDetector> _logger;

    public GamePathDetector(ILogger<GamePathDetector>? logger = null)
    {
        _logger = logger ?? NullLogger<GamePathDetector>.Instance;
    }

    /// <summary>
    /// Detect game path automatically by scanning Steam libraries.
    /// </summary>
    public DetectionResult DetectGamePath()
    {
        var result = new DetectionResult
        {
            DetectionMethod = DetectionMethod.AutoDetected
        };

        // Find Steam root directories
        var steamRoots = FindSteamRoots();

        // Find all Steam library folders
        var libraryFolders = new List<string>();
        foreach (var steamRoot in steamRoots)
        {
            libraryFolders.Add(steamRoot);
            libraryFolders.AddRange(ParseLibraryFoldersVdf(steamRoot));
        }

        // Scan each library for game installations
        foreach (var library in libraryFolders)
        {
            var candidates = ScanLibraryForGame(library);
            result.AllCandidates.AddRange(candidates);
        }

        // Select best candidate
        if (result.AllCandidates.Count > 0)
        {
            var best = result.AllCandidates.OrderByDescending(c => c.Score).First();
            result.SelectedPath = best.HeroesOEDataPath;
        }

        return result;
    }

    /// <summary>
    /// Validate and normalize a manually specified path.
    /// Accepts game root or HeroesOE_Data directory.
    /// </summary>
    public string? ValidateAndNormalizePath(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return null;

            // If path itself is a *_Data directory, use it directly
            if (Path.GetFileName(path).EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
            {
                return ValidateDataDirectory(path) ? path : null;
            }

            // Check for any *_Data subdirectory
            var dataDir = Directory.EnumerateDirectories(path, "*_Data", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(d => ValidateDataDirectory(d));
            return dataDir;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Find Steam root directories based on platform.
    /// </summary>
    private List<string> FindSteamRoots()
    {
        var roots = new List<string>();

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                roots.AddRange(FindSteamRootsWindows());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                roots.AddRange(FindSteamRootsLinux());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                roots.AddRange(FindSteamRootsMacOS());
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to find Steam roots");
        }

        return roots.Where(Directory.Exists).Distinct().ToList();
    }

    /// <summary>
    /// Find Steam root on Windows via registry.
    /// </summary>
    private List<string> FindSteamRootsWindows()
    {
        var roots = new List<string>();

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Try HKEY_CURRENT_USER
                var installPath = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam",
                    "SteamPath",
                    null
                ) as string;

                if (!string.IsNullOrEmpty(installPath))
                    roots.Add(installPath);

                // Try HKEY_LOCAL_MACHINE (32-bit registry on 64-bit Windows)
                installPath = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                    "InstallPath",
                    null
                ) as string;

                if (!string.IsNullOrEmpty(installPath))
                    roots.Add(installPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read Windows registry for Steam path");
        }

        return roots;
    }

    /// <summary>
    /// Find Steam root on Linux.
    /// </summary>
    private List<string> FindSteamRootsLinux()
    {
        var roots = new List<string>();

        if (IsWSL())
        {
            // WSL-specific: Try reading Windows registry
            var steamPath = ReadWindowsRegistryFromWSL(@"HKCU\Software\Valve\Steam", "SteamPath");
            var convertedSteamPath = ConvertWindowsPathToWSL(steamPath);
            if (!string.IsNullOrEmpty(convertedSteamPath) && Directory.Exists(convertedSteamPath))
                roots.Add(convertedSteamPath);

            var installPath = ReadWindowsRegistryFromWSL(@"HKLM\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            var convertedInstallPath = ConvertWindowsPathToWSL(installPath);
            if (!string.IsNullOrEmpty(convertedInstallPath) && Directory.Exists(convertedInstallPath))
                roots.Add(convertedInstallPath);

            // Fallback: Scan common Windows Steam locations via /mnt/*
            foreach (var drive in new[] { "c", "d", "e", "f" })
            {
                var candidates = new[]
                {
                    $"/mnt/{drive}/Program Files (x86)/Steam",
                    $"/mnt/{drive}/Program Files/Steam",
                    $"/mnt/{drive}/Steam",
                    $"/mnt/{drive}/SteamLibrary"
                };

                roots.AddRange(candidates.Where(Directory.Exists));
            }
        }
        else
        {
            // Native Linux: Standard Steam locations
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var candidates = new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam") // Flatpak
            };

            roots.AddRange(candidates.Where(Directory.Exists));
        }

        return roots;
    }

    /// <summary>
    /// Find Steam root on macOS.
    /// </summary>
    private List<string> FindSteamRootsMacOS()
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var steamPath = Path.Combine(home, "Library", "Application Support", "Steam");
        if (Directory.Exists(steamPath))
            roots.Add(steamPath);

        return roots;
    }

    /// <summary>
    /// Parse libraryfolders.vdf to find additional Steam library locations.
    /// </summary>
    private List<string> ParseLibraryFoldersVdf(string steamRoot)
    {
        var libraries = new List<string>();

        try
        {
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                return libraries;

            var content = File.ReadAllText(vdfPath);

            // Simple VDF parser - look for "path" entries
            var pathPattern = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            var matches = pathPattern.Matches(content);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var path = match.Groups[1].Value;
                    // VDF paths may use escaped backslashes on Windows
                    path = path.Replace("\\\\", "\\");

                    if (Directory.Exists(path))
                        libraries.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse libraryfolders.vdf in Steam root {SteamRoot}", steamRoot);
        }

        return libraries;
    }

    /// <summary>
    /// Scan a Steam library folder for Heroes of Might & Magic Olden Era installations.
    /// </summary>
    private List<Candidate> ScanLibraryForGame(string libraryPath)
    {
        var candidates = new List<Candidate>();

        try
        {
            var steamappsPath = Path.Combine(libraryPath, "steamapps", "common");
            if (!Directory.Exists(steamappsPath))
                return candidates;

            // Scan all directories in steamapps/common
            foreach (var dir in Directory.GetDirectories(steamappsPath))
            {
                var candidate = AnalyzeGameDirectory(dir);
                if (candidate != null)
                    candidates.Add(candidate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to scan Steam library at {LibraryPath}", libraryPath);
        }

        return candidates;
    }

    /// <summary>
    /// Analyze a directory to determine if it's a valid Heroes of Might & Magic Olden Era installation.
    /// </summary>
    private Candidate? AnalyzeGameDirectory(string directory)
    {
        try
        {
            var dirName = Path.GetFileName(directory);
            var dirNameLower = dirName.ToLowerInvariant();

            // Must contain "olden" or "heroes" to be considered
            if (!dirNameLower.Contains("olden") && !dirNameLower.Contains("heroes"))
                return null;

            var dataPath = Directory.EnumerateDirectories(directory, "*_Data", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (dataPath is null)
                return null;

            // Calculate score
            int score = 0;

            // +3: Directory name contains "Olden Era"
            if (dirNameLower.Contains("olden") && dirNameLower.Contains("era"))
                score += 3;

            // +2: Directory name contains variant indicators
            if (dirNameLower.Contains("playtest") || dirNameLower.Contains("demo") ||
                dirNameLower.Contains("early access"))
                score += 2;

            // +2: Executable found
            var exePaths = new[]
            {
                Path.Combine(directory, "HeroesOldenEra.exe"),
                Path.Combine(directory, "HeroesOldenEra"),
                Path.Combine(directory, "HeroesOE.exe"),
                Path.Combine(directory, "HeroesOE"),
            };

            if (exePaths.Any(File.Exists))
                score += 2;

            // +2: Valid sharedassets files found
            if (ValidateDataDirectory(dataPath))
                score += 2;

            // +1: Process is running (check process list)
            if (IsProcessRunning("HeroesOE"))
                score += 1;

            return new Candidate
            {
                GameRoot = directory,
                HeroesOEDataPath = dataPath,
                Score = score,
                DisplayName = dirName
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validate that a directory contains required Unity asset files.
    /// </summary>
    private bool ValidateDataDirectory(string dataPath)
    {
        try
        {
            if (!Directory.Exists(dataPath))
                return false;

            // Check for sharedassets*.assets files
            var sharedAssets = Directory.GetFiles(dataPath, "sharedassets*.assets");
            if (sharedAssets.Length == 0)
                return false;

            // Optionally check for resources.assets
            var resourcesPath = Path.Combine(dataPath, "resources.assets");
            return File.Exists(resourcesPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if a process with the given name is currently running.
    /// </summary>
    private bool IsProcessRunning(string processName)
    {
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processName);
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detect if running in WSL (Windows Subsystem for Linux).
    /// </summary>
    private static bool IsWSL()
    {
        try
        {
            // Strategy 1: Check environment variable
            var wslDistro = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
            if (!string.IsNullOrEmpty(wslDistro))
                return true;

            // Strategy 2: Check /proc/version for "microsoft" or "WSL"
            if (File.Exists("/proc/version"))
            {
                var version = File.ReadAllText("/proc/version").ToLowerInvariant();
                if (version.Contains("microsoft") || version.Contains("wsl"))
                    return true;
            }

            // Strategy 3: Check if /mnt/c exists (Windows C: drive mount)
            if (Directory.Exists("/mnt/c"))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Read a Windows registry value from WSL using reg.exe.
    /// </summary>
    /// <param name="keyPath">Registry key path (e.g., "HKCU\Software\Valve\Steam")</param>
    /// <param name="valueName">Value name to read (e.g., "SteamPath")</param>
    /// <returns>Registry value string, or null if not found or on error</returns>
    private static string? ReadWindowsRegistryFromWSL(string keyPath, string valueName)
    {
        try
        {
            var regPath = "/mnt/c/Windows/System32/reg.exe";
            if (!File.Exists(regPath))
                return null;

            var psi = new ProcessStartInfo
            {
                FileName = regPath,
                Arguments = $"query \"{keyPath}\" /v {valueName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                return null;

            // Parse output format:
            // SteamPath    REG_SZ    C:\Program Files (x86)\Steam
            var match = Regex.Match(output, $@"{Regex.Escape(valueName)}\s+REG_\w+\s+(.+)");
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim();
                return NormalizePathSeparators(value);
            }

            return null;
        }
        catch
        {
            return null; // Silent failure
        }
    }

    /// <summary>
    /// Convert a Windows path to WSL path format.
    /// Example: "C:\Program Files\Steam" → "/mnt/c/Program Files/Steam"
    /// </summary>
    private static string? ConvertWindowsPathToWSL(string? windowsPath)
    {
        if (string.IsNullOrEmpty(windowsPath))
            return null;

        try
        {
            // Normalize path separators first
            var normalized = NormalizePathSeparators(windowsPath);

            // Check for drive letter pattern (e.g., "C:/" or "C:\")
            var driveMatch = Regex.Match(normalized, @"^([a-zA-Z]):[/\\](.*)$");
            if (driveMatch.Success)
            {
                var driveLetter = driveMatch.Groups[1].Value.ToLowerInvariant();
                var pathRemainder = driveMatch.Groups[2].Value;

                // Convert to /mnt/<drive>/<path>
                return $"/mnt/{driveLetter}/{pathRemainder}";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Normalize path separators to forward slashes for Linux/WSL compatibility.
    /// </summary>
    private static string NormalizePathSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        return path.Replace('\\', '/');
    }
}
