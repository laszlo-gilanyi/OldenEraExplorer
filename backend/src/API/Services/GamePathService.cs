using GameData.Services;
using ExtractorDetector = AssetExtractor.Pipeline.GamePathDetector;

namespace API.Services;

public record DetectionResult(
    bool Success,
    string? SelectedGameRoot,
    string? HeroesOeDataPath,
    string? StreamingAssetsPath,
    IReadOnlyList<CandidateInfo> AllCandidates
);

public record CandidateInfo(
    string GameRoot,
    string? HeroesOeDataPath,
    string? StreamingAssetsPath,
    int Score,
    string DisplayName
);

public record SetPathResult(
    bool Success,
    string? Error,
    string? GameRoot,
    string? HeroesOeDataPath,
    string? StreamingAssetsPath,
    bool HasAssetBundles = true,
    string? Warning = null
);

public interface IGamePathService
{
    bool IsPathSet { get; }
    string? GameRoot { get; }
    string? HeroesOeDataPath { get; }
    string? StreamingAssetsPath { get; }
    string CurrentLocale { get; }
    DetectionResult AutoDetect();
    SetPathResult SetPath(string gameRoot, string? locale = null);
    void Clear();
    event EventHandler<GamePathChangedEventArgs>? PathChanged;
}

public class GamePathChangedEventArgs : EventArgs
{
    public string? NewGameRoot { get; init; }
    public string? PreviousGameRoot { get; init; }
    public string? HeroesOeDataPath { get; init; }
    public bool IsCleared => NewGameRoot == null;
}

public class GamePathService : IGamePathService
{
    private readonly SettingsService _settings;
    private readonly ILogger<GamePathService> _logger;
    private readonly object _lock = new();

    private string? _gameRoot;
    private string? _heroesOeDataPath;
    private string? _streamingAssetsPath;
    private string _currentLocale = "english";

    public bool IsPathSet => !string.IsNullOrEmpty(_streamingAssetsPath);
    public string? GameRoot => _gameRoot;
    public string? HeroesOeDataPath => _heroesOeDataPath;
    public string? StreamingAssetsPath => _streamingAssetsPath;
    public string CurrentLocale => _currentLocale;
    public event EventHandler<GamePathChangedEventArgs>? PathChanged;

    public GamePathService(SettingsService settings, ILogger<GamePathService> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        _logger.LogDebug("Loading game path settings...");

        _currentLocale = _settings.LastLocale ?? "english";

        if (!string.IsNullOrEmpty(_settings.LastRootPath))
        {
            _logger.LogInformation("Found persisted game path: {Path}", _settings.LastRootPath);

            var result = SetPathInternal(_settings.LastRootPath, _currentLocale, persistToSettings: false);

            if (result.Success)
            {
                _logger.LogInformation("Successfully restored game path from settings");
            }
            else
            {
                _logger.LogWarning("Failed to restore persisted game path: {Error}", result.Error);
            }
        }
        else
        {
            _logger.LogDebug("No persisted game path found");
        }
    }

    public DetectionResult AutoDetect()
    {
        lock (_lock)
        {
            _logger.LogInformation("Starting auto-detection of game installations...");

            var candidates = new List<CandidateInfo>();

            var locatorResult = GameLocator.AutoDetect(_currentLocale);

            foreach (var candidate in locatorResult.Candidates)
            {
                var heroesDataPath = FindHeroesOeDataPath(candidate.GameRoot);

                candidates.Add(new CandidateInfo(
                    GameRoot: candidate.GameRoot,
                    HeroesOeDataPath: heroesDataPath,
                    StreamingAssetsPath: candidate.StreamingAssets,
                    Score: candidate.Score,
                    DisplayName: Path.GetFileName(candidate.GameRoot)
                ));
            }

            var assetExtractorDetector = new ExtractorDetector();
            var assetExtractorResult = assetExtractorDetector.DetectGamePath();

            foreach (var candidate in assetExtractorResult.AllCandidates)
            {
                if (candidates.Any(c => c.GameRoot.Equals(candidate.GameRoot, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var streamingAssets = GameLocator.NormalizeStreamingAssets(candidate.GameRoot, _currentLocale);

                candidates.Add(new CandidateInfo(
                    GameRoot: candidate.GameRoot,
                    HeroesOeDataPath: candidate.HeroesOEDataPath,
                    StreamingAssetsPath: string.IsNullOrEmpty(streamingAssets) ? null : streamingAssets,
                    Score: candidate.Score,
                    DisplayName: candidate.DisplayName
                ));
            }

            candidates = candidates
                .GroupBy(c => GetCanonicalPath(c.GameRoot), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(c => c.Score)
                .ToList();

            var selected = candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c.StreamingAssetsPath));

            if (selected != null)
            {
                _logger.LogInformation("Auto-detected game installation: {Path} (score: {Score})",
                    selected.GameRoot, selected.Score);

                var setResult = SetPathInternal(selected.GameRoot, _currentLocale, persistToSettings: true);

                return new DetectionResult(
                    Success: setResult.Success,
                    SelectedGameRoot: setResult.GameRoot,
                    HeroesOeDataPath: setResult.HeroesOeDataPath,
                    StreamingAssetsPath: setResult.StreamingAssetsPath,
                    AllCandidates: candidates
                );
            }

            _logger.LogWarning("Auto-detection found {Count} candidates but none had valid StreamingAssets",
                candidates.Count);

            return new DetectionResult(
                Success: false,
                SelectedGameRoot: null,
                HeroesOeDataPath: null,
                StreamingAssetsPath: null,
                AllCandidates: candidates
            );
        }
    }

    public SetPathResult SetPath(string gameRoot, string? locale = null)
    {
        return SetPathInternal(gameRoot, locale ?? _currentLocale, persistToSettings: true);
    }

    private SetPathResult SetPathInternal(string gameRoot, string locale, bool persistToSettings)
    {
        lock (_lock)
        {
            _logger.LogInformation("Setting game path: {Path} (locale: {Locale})", gameRoot, locale);

            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                return new SetPathResult(
                    Success: false,
                    Error: "Game root path cannot be empty",
                    GameRoot: null,
                    HeroesOeDataPath: null,
                    StreamingAssetsPath: null
                );
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(gameRoot.Trim().Trim('"'));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to normalize game path: {Path}", gameRoot);
                return new SetPathResult(
                    Success: false,
                    Error: $"Invalid path format: {ex.Message}",
                    GameRoot: null,
                    HeroesOeDataPath: null,
                    StreamingAssetsPath: null
                );
            }

            if (!Directory.Exists(normalizedPath))
            {
                return new SetPathResult(
                    Success: false,
                    Error: $"Directory does not exist: {normalizedPath}",
                    GameRoot: null,
                    HeroesOeDataPath: null,
                    StreamingAssetsPath: null
                );
            }

            var streamingAssets = GameLocator.NormalizeStreamingAssets(normalizedPath, locale);
            var heroesOeData = FindHeroesOeDataPath(normalizedPath);

            if (string.IsNullOrEmpty(streamingAssets))
            {
                var errorMessage = BuildPathRejectionMessage(normalizedPath, locale);
                _logger.LogWarning("Path validation failed: {Reason} (path: {Path})", errorMessage, normalizedPath);
                return new SetPathResult(
                    Success: false,
                    Error: errorMessage,
                    GameRoot: null,
                    HeroesOeDataPath: null,
                    StreamingAssetsPath: null
                );
            }

            // No-op when path and locale are unchanged: firing PathChanged would unload
            // currently-loaded data and leave the app in a broken state until a manual reload.
            if (string.Equals(_gameRoot, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_currentLocale, locale, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("SetPath called with unchanged path and locale; skipping reconfiguration");
                return new SetPathResult(
                    Success: true,
                    Error: null,
                    GameRoot: _gameRoot,
                    HeroesOeDataPath: _heroesOeDataPath,
                    StreamingAssetsPath: _streamingAssetsPath
                );
            }

            var previousGameRoot = _gameRoot;

            // Persist exactly what the caller picked so a manual browse to a *_Data or
            // StreamingAssets dir is visibly reflected in the UI (acts as override confirmation).
            // Auto-detect already passes a game root, so its display is unchanged.
            _gameRoot = normalizedPath;
            _heroesOeDataPath = heroesOeData;
            _streamingAssetsPath = streamingAssets;
            _currentLocale = locale;

            _logger.LogInformation("Game path configured successfully:");
            _logger.LogInformation("  GameRoot: {GameRoot}", _gameRoot);
            _logger.LogInformation("  HeroesOE_Data: {HeroesOeData}", _heroesOeDataPath ?? "(not found)");
            _logger.LogInformation("  StreamingAssets: {StreamingAssets}", _streamingAssetsPath);
            _logger.LogInformation("  Locale: {Locale}", _currentLocale);

            if (persistToSettings)
            {
                _settings.LastRootPath = _gameRoot;
                _settings.LastLocale = _currentLocale;
                _settings.Save();
                _logger.LogDebug("Settings persisted");
            }

            OnPathChanged(previousGameRoot, _gameRoot);

            bool hasBundles = !string.IsNullOrEmpty(_heroesOeDataPath) &&
                              GameLocator.IsValidDataDirectory(_heroesOeDataPath);
            string? warning = hasBundles
                ? null
                : "This install has no Unity asset bundles (sharedassets*.assets / resources.assets), " +
                  "so JSON data browsing works but image and 3D model extraction will not. " +
                  "This usually means an incomplete install or a leftover folder.";

            if (warning != null)
            {
                _logger.LogWarning("Path accepted but asset bundles missing in {DataPath}", _heroesOeDataPath);
            }

            return new SetPathResult(
                Success: true,
                Error: null,
                GameRoot: _gameRoot,
                HeroesOeDataPath: _heroesOeDataPath,
                StreamingAssetsPath: _streamingAssetsPath,
                HasAssetBundles: hasBundles,
                Warning: warning
            );
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _logger.LogInformation("Clearing game path configuration");

            var previousGameRoot = _gameRoot;

            _gameRoot = null;
            _heroesOeDataPath = null;
            _streamingAssetsPath = null;

            _settings.LastRootPath = null;
            _settings.Save();

            OnPathChanged(previousGameRoot, null);
        }
    }

    private static string BuildPathRejectionMessage(string gameRoot, string locale)
    {
        try
        {
            if (!Directory.Exists(gameRoot))
                return $"Directory does not exist: {gameRoot}";

            var dataDirs = GameLocator.EnumerateDataDirsPreferringEA(gameRoot);
            bool isDataDirItself = Path.GetFileName(gameRoot).EndsWith("_Data", StringComparison.OrdinalIgnoreCase);

            if (dataDirs.Count == 0 && !isDataDirItself)
            {
                return "No *_Data directory found. Point this at the game's install root (the folder containing HeroesOldenEra.exe).";
            }

            return "No Core.zip or Lang folder found. The selected install looks empty or corrupted; reinstall the game or point Browse at the correct folder.";
        }
        catch
        {
            return "Could not validate the selected folder as a Heroes Olden Era install.";
        }
    }

    private static string? FindHeroesOeDataPath(string gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            return null;

        try
        {
            if (Path.GetFileName(gameRoot).EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
            {
                return gameRoot;
            }

            var ordered = GameLocator.EnumerateDataDirsPreferringEA(gameRoot);
            return ordered.FirstOrDefault(GameLocator.IsValidDataDirectory) ?? ordered.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string GetCanonicalPath(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (!OperatingSystem.IsWindows())
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "realpath",
                    Arguments = $"\"{path}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    var result = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(result))
                        return result;
                }
            }

            if (Directory.Exists(path))
            {
                var resolved = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
                return resolved?.FullName ?? Path.GetFullPath(path);
            }

            return Path.GetFullPath(path);
        }
        catch
        {
            return Path.GetFullPath(path);
        }
    }

    private void OnPathChanged(string? previousGameRoot, string? newGameRoot)
    {
        try
        {
            PathChanged?.Invoke(this, new GamePathChangedEventArgs
            {
                PreviousGameRoot = previousGameRoot,
                NewGameRoot = newGameRoot,
                HeroesOeDataPath = _heroesOeDataPath
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PathChanged event handler");
        }
    }
}
