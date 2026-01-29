using GameData.Loading;
using GameData.Services;

namespace API.Services;

public enum GameDataState
{
    NotLoaded,
    Loading,
    Loaded,
    Error
}

public interface IGameDataService
{
    GameDataState State { get; }
    string? Error { get; }
    GameDataLoadResult? Data { get; }
    bool IsLoaded { get; }
    Task<bool> LoadAsync(CancellationToken cancellationToken = default);
    void Unload();
}

public class GameDataService : IGameDataService, IDisposable
{
    private readonly IGamePathService _pathService;
    private readonly IndexService _indexService;
    private readonly SettingsService _settings;
    private readonly ILogger<GameDataService> _logger;
    private readonly IReferenceIndexService _referenceIndexService;
    private readonly IAssetServingService _assetService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly FactionMapper _factionMapper;
    private readonly ClassMapper _classMapper;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private GameDataState _state = GameDataState.NotLoaded;
    private string? _error;
    private GameDataLoadResult? _data;
    private bool _disposed;

    public GameDataState State => _state;
    public string? Error => _error;
    public GameDataLoadResult? Data => _data;
    public bool IsLoaded => _state == GameDataState.Loaded && _data != null;

    public GameDataService(
        IGamePathService pathService,
        IndexService indexService,
        SettingsService settings,
        ILogger<GameDataService> logger,
        IReferenceIndexService referenceIndexService,
        IAssetServingService assetService,
        ILoggerFactory loggerFactory,
        FactionMapper factionMapper,
        ClassMapper classMapper)
    {
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _referenceIndexService = referenceIndexService ?? throw new ArgumentNullException(nameof(referenceIndexService));
        _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _factionMapper = factionMapper ?? throw new ArgumentNullException(nameof(factionMapper));
        _classMapper = classMapper ?? throw new ArgumentNullException(nameof(classMapper));

        _pathService.PathChanged += OnPathChanged;

        _logger.LogDebug("GameDataService initialized");
    }

    private void OnPathChanged(object? sender, GamePathChangedEventArgs e)
    {
        _logger.LogInformation("Game path changed, unloading current data");
        Unload();
    }

    public async Task<bool> LoadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_pathService.IsPathSet)
        {
            _logger.LogError("Cannot load game data: no game path configured");
            SetState(GameDataState.Error, "No game path configured. Please set the game path first.");
            return false;
        }

        var streamingAssetsPath = _pathService.StreamingAssetsPath!;
        var locale = _pathService.CurrentLocale;

        _logger.LogInformation("Loading game data from {Path} (locale: {Locale})",
            streamingAssetsPath, locale);

        var lockAcquired = await _loadLock.WaitAsync(TimeSpan.Zero, cancellationToken);
        if (!lockAcquired)
        {
            _logger.LogWarning("Game data load already in progress");
            return false;
        }

        try
        {
            SetState(GameDataState.Loading);

            var loader = new GameDataLoader(_indexService, _settings, _assetService, _loggerFactory);

            var progress = new Progress<string>(message =>
            {
                _logger.LogDebug("Loading progress: {Message}", message);
            });

            var result = await loader.LoadAsync(
                streamingAssetsPath,
                locale,
                progress: progress,
                cancellationToken: cancellationToken);

            _data = result;
            _error = null;
            SetState(GameDataState.Loaded);

            _logger.LogInformation("Initializing FactionMapper with LangIndex...");
            _factionMapper.SetLangIndex(result.Lang);

            _logger.LogInformation("Initializing ClassMapper with LangIndex...");
            _classMapper.SetLangIndex(result.Lang);

            _logger.LogInformation("Building reference index...");
            _referenceIndexService.BuildIndex(result, locale);

            _logger.LogInformation("Game data loaded successfully in {TotalMs}ms",
                result.Durations.Sum(d => d.Ms));

            foreach (var (name, ms) in result.Durations)
            {
                _logger.LogDebug("  {Stage}: {Ms}ms", name, ms);
            }

            _logger.LogInformation("Loaded data summary:");
            _logger.LogInformation("  Units: {Count}", result.Units.Count);
            _logger.LogInformation("  Abilities: {Count}", result.AbilityIndex.Abilities.Count);
            _logger.LogInformation("  Heroes: {Count}", result.HeroesIndex.Heroes.Count);
            _logger.LogInformation("  HeroSpecializations: {Count}", result.HeroSpecializationsIndex.Specializations.Count);
            _logger.LogInformation("  Skills: {Count}", result.SkillsIndex.Skills.Count);
            _logger.LogInformation("  Subclasses: {Count}", result.SubclassesIndex.Subclasses.Count);
            _logger.LogInformation("  Spells: {Count}", result.SpellsIndex.Spells.Count);
            _logger.LogInformation("  Artifacts: {Count}", result.ArtifactsIndex.Artifacts.Count);
            _logger.LogInformation("  ItemSets: {Count}", result.ItemSetsIndex.ItemSets.Count);
            _logger.LogInformation("  Buildings: {Count}", result.BuildingsIndex.Buildings.Count);
            _logger.LogInformation("  FactionLaws: {Count}", result.FactionLawIndex.FactionLaws.Count);
            _logger.LogInformation("  MapObjects: {Count}", result.MapObjectsIndex.MapObjects.Count);
            _logger.LogInformation("  Difficulties: {Count}", result.DifficultiesIndex.GuardDifficulties.Count);
            _logger.LogInformation("  SideBuffs: {Count}", result.MapObjectsIndex.SideBuffs.Count);

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Game data loading was cancelled");
            SetState(GameDataState.NotLoaded);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load game data");
            _data = null;
            SetState(GameDataState.Error, ex.Message);
            return false;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void Unload()
    {
        ThrowIfDisposed();

        _logger.LogInformation("Unloading game data");

        _loadLock.Wait();
        try
        {
            _data = null;
            _error = null;

            _factionMapper.SetLangIndex(null);
            _classMapper.SetLangIndex(null);
            _indexService.Clear();
            _referenceIndexService.Clear();

            SetState(GameDataState.NotLoaded);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private void SetState(GameDataState newState, string? error = null)
    {
        var previousState = _state;
        _state = newState;
        _error = error;

        if (previousState != newState)
        {
            _logger.LogDebug("Game data state changed: {Previous} -> {New}", previousState, newState);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GameDataService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pathService.PathChanged -= OnPathChanged;
        _loadLock.Dispose();

        _disposed = true;

        _logger.LogDebug("GameDataService disposed");
    }
}
