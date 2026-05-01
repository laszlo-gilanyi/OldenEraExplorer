using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GameData.Services;

/// <summary>
/// Scanning Steam libraries, detecting game roots, normalizing StreamingAssets.
/// </summary>
public static class GameLocator
{
    public sealed record Candidate(
        string GameRoot,
        string StreamingAssets,
        int Score,
        IReadOnlyList<string> Reasons,
        DateTime LastWriteUtc
    );

    public sealed record DetectionResult(Candidate? Selected, IReadOnlyList<Candidate> Candidates);

    public static string NormalizeStreamingAssets(string anyPath, string? preferredLocaleOrNull)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(anyPath)) return "";
            var preferred = string.IsNullOrWhiteSpace(preferredLocaleOrNull) ? "english" : preferredLocaleOrNull!;
            var p = anyPath.Trim().Trim('"');

            try { p = Path.GetFullPath(p); } catch { }
            if (!Directory.Exists(p)) return "";

            var candidates = new List<string>();

            if (Directory.Exists(Path.Combine(p, "Lang")))
                candidates.Add(p);

            if (p.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
            {
                var sa = Path.Combine(p, "StreamingAssets");
                if (Directory.Exists(sa)) candidates.Add(sa);
            }

            var dataDir = Directory.EnumerateDirectories(p, "*_Data", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (dataDir is not null)
            {
                var sa = Path.Combine(dataDir, "StreamingAssets");
                if (Directory.Exists(sa)) candidates.Add(sa);
            }

            foreach (var sa in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (IsValidStreamingAssets(sa, preferred)) return sa;
            }

            return "";
        }
        catch
        {
            return "";
        }
    }

    public static bool IsValidStreamingAssets(string? streamingAssets, string preferredLocale)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(streamingAssets)) return false;
            var root = streamingAssets!;
            if (!Directory.Exists(root)) return false;

            var coreZip = Path.Combine(root, "Core.zip");
            if (!File.Exists(coreZip)) return false;

            // Demo: Lang is on disk
            foreach (var loc in BuildLocaleProbeOrder(preferredLocale))
            {
                var texts = Path.Combine(root, "Lang", loc, "texts");
                if (Directory.Exists(texts) &&
                    Directory.EnumerateFiles(texts, "*.json", SearchOption.TopDirectoryOnly).Any())
                {
                    return true;
                }
            }

            // EA: Lang is inside Core.zip
            using var zip = ZipFile.OpenRead(coreZip);
            foreach (var loc in BuildLocaleProbeOrder(preferredLocale))
            {
                var prefix = $"Lang/{loc}/texts/";
                if (zip.Entries.Any(e =>
                    e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static DetectionResult AutoDetect(string preferredLocale)
    {
        try
        {
            var running = GetRunningGameRoots();
            var candidates = new List<Candidate>();

            foreach (var steamapps in GetSteamAppsRoots())
            {
                var common = Path.Combine(steamapps, "common");
                if (!Directory.Exists(common)) continue;

                foreach (var gameRoot in Directory.EnumerateDirectories(common))
                {
                    var c = BuildCandidate(gameRoot, preferredLocale, running);
                    if (c is not null) candidates.Add(c);
                }
            }

            candidates = candidates
                .GroupBy(c => c.StreamingAssets, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Score).ThenByDescending(x => x.LastWriteUtc).First())
                .ToList();

            var selected = candidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.LastWriteUtc)
                .FirstOrDefault();

            return new DetectionResult(selected, candidates);
        }
        catch
        {
            return new DetectionResult(null, Array.Empty<Candidate>());
        }
    }

    private static Candidate? BuildCandidate(string gameRoot, string preferredLocale, HashSet<string> runningRoots)
    {
        try
        {
            string? streamingAssets = null;

            var dataDir = Directory.EnumerateDirectories(gameRoot, "*_Data", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (dataDir is not null)
            {
                var sa = Path.Combine(dataDir, "StreamingAssets");
                if (Directory.Exists(sa)) streamingAssets = sa;
            }

            if (streamingAssets is null)
            {
                var sa = Path.Combine(gameRoot, "StreamingAssets");
                if (Directory.Exists(sa)) streamingAssets = sa;
            }

            if (streamingAssets is null) return null;

            var reasons = new List<string>();
            var score = 0;
            bool localeOk = false;

            var localeOrder = BuildLocaleProbeOrder(preferredLocale);

            // Demo: Lang on disk
            foreach (var loc in localeOrder)
            {
                var texts = Path.Combine(streamingAssets, "Lang", loc, "texts");
                if (Directory.Exists(texts) &&
                    Directory.EnumerateFiles(texts, "*.json", SearchOption.TopDirectoryOnly).Any())
                {
                    localeOk = true;
                    reasons.Add($"+2 Lang/{loc}/texts ok");
                    score += 2;
                    break;
                }
            }

            // EA: Lang inside Core.zip
            if (!localeOk)
            {
                var coreZip = Path.Combine(streamingAssets, "Core.zip");
                if (File.Exists(coreZip))
                {
                    try
                    {
                        using var zip = ZipFile.OpenRead(coreZip);
                        foreach (var loc in localeOrder)
                        {
                            var prefix = $"Lang/{loc}/texts/";
                            if (zip.Entries.Any(e =>
                                e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                            {
                                localeOk = true;
                                reasons.Add($"+2 Lang/{loc}/texts ok (Core.zip)");
                                score += 2;
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }

            if (!localeOk) return null;

            var name = Path.GetFileName(gameRoot) ?? "";

            if (name.IndexOf("Olden Era", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 3;
                reasons.Add("+3 folder name contains 'Olden Era'");
            }

            if (Directory.EnumerateFiles(gameRoot, "Heroes*.exe", SearchOption.TopDirectoryOnly).Any())
            {
                score += 2;
                reasons.Add("+2 Heroes*.exe found");
            }

            if (Regex.IsMatch(name, @"(?i)(Playtest|Demo|Early\s*Access|EA|Dev)"))
            {
                score += 1;
                reasons.Add("+1 folder name: Playtest/Demo/EA/Dev");
            }

            if (runningRoots.Contains(gameRoot))
            {
                score += 1;
                reasons.Add("+1 HeroesOE.exe is running here");
            }

            if (File.Exists(Path.Combine(gameRoot, "Core.zip")) || Directory.Exists(Path.Combine(gameRoot, "Core")))
            {
                reasons.Add("bonus: Core.zip/Core present");
            }

            var last = SafeGetLastWriteUtc(gameRoot);
            return new Candidate(gameRoot, streamingAssets, score, reasons, last);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime SafeGetLastWriteUtc(string dir)
    {
        try { return Directory.GetLastWriteTimeUtc(dir); } catch { return DateTime.MinValue; }
    }

    private static HashSet<string> GetRunningGameRoots()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var n = p.ProcessName;
                    if (!n.StartsWith("HeroesOE", StringComparison.OrdinalIgnoreCase) &&
                        !n.StartsWith("HeroesOldenEra", StringComparison.OrdinalIgnoreCase)) continue;

                    var exe = p.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(exe)) continue;

                    var dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        set.Add(dir);
                }
                catch { }
            }
        }
        catch { }

        return set;
    }

    private static IEnumerable<string> GetSteamAppsRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var steam in GetSteamInstallRoots())
        {
            var steamapps = Path.Combine(steam, "steamapps");
            if (Directory.Exists(steamapps)) roots.Add(steamapps);

            var vdf = Path.Combine(steamapps, "libraryfolders.vdf");
            foreach (var lib in ReadLibraryFoldersFromVdf(vdf))
            {
                var libApps = Path.Combine(lib, "steamapps");
                if (Directory.Exists(libApps)) roots.Add(libApps);
            }
        }

        return roots;
    }

    private static IEnumerable<string> GetSteamInstallRoots()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? p)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                var full = Path.GetFullPath(p);
                if (Directory.Exists(full))
                    set.Add(full);
            }
            catch { }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                TryAdd(key?.GetValue("SteamPath") as string);
            }
            catch { }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                TryAdd(key?.GetValue("InstallPath") as string);
            }
            catch { }
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                if (OperatingSystem.IsLinux())
                {
                    TryAdd(Path.Combine(home, ".steam", "steam"));
                    TryAdd(Path.Combine(home, ".steam", "root"));
                    TryAdd(Path.Combine(home, ".local", "share", "Steam"));
                    TryAdd(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
                }

                TryAdd(Path.Combine(home, "Library", "Application Support", "Steam"));
            }
        }

        return set;
    }

    private static IEnumerable<string> ReadLibraryFoldersFromVdf(string vdfPath)
    {
        var result = new List<string>();
        if (!File.Exists(vdfPath)) return result;

        try
        {
            foreach (var line in File.ReadLines(vdfPath))
            {
                var m1 = Regex.Match(line, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (m1.Success)
                {
                    var raw = m1.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(raw)) result.Add(raw);
                    continue;
                }

                var m2 = Regex.Match(line, "^\\s*\"\\d+\"\\s*\"([^\"]+)\"");
                if (m2.Success)
                {
                    var raw = m2.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(raw)) result.Add(raw);
                }
            }
        }
        catch { }

        return result;
    }

    private static string[] BuildLocaleProbeOrder(string preferredLocale)
    {
        return new[] { preferredLocale, "english", "hungarian" }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
