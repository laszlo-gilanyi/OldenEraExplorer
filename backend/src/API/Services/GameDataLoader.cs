using GameData.Indexing;
using GameData.Loading;
using Localization.DbAccess;
using Localization.Indexing;
using Localization.Resolution;
using Localization.Scripting;
using API.Utilities;

namespace API.Services;

public class GameDataLoader
{
    private readonly IndexService _indexService;
    private readonly SettingsService _settings;
    private readonly IAssetServingService _assetService;
    private readonly ILoggerFactory _loggerFactory;

    public GameDataLoader(
        IndexService indexService,
        SettingsService settings,
        IAssetServingService assetService,
        ILoggerFactory loggerFactory)
    {
        _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public async Task<GameDataLoadResult> LoadAsync(
        string streamingAssetsRoot,
        string locale,
        IProgress<string>? progress = null,
        Action<string>? beginStageCallback = null,
        Action? endStageCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(streamingAssetsRoot))
            throw new ArgumentException("StreamingAssets root path cannot be empty.", nameof(streamingAssetsRoot));

        if (string.IsNullOrEmpty(locale))
            throw new ArgumentException("Locale cannot be empty.", nameof(locale));

        var durations = new List<(string Name, double Ms)>();

        return await Task.Run(() => LoadCore(streamingAssetsRoot, locale, progress, beginStageCallback, endStageCallback, durations, cancellationToken), cancellationToken);
    }

    private GameDataLoadResult LoadCore(
        string streamingAssetsRoot,
        string locale,
        IProgress<string>? progress,
        Action<string>? beginStageCallback,
        Action? endStageCallback,
        List<(string Name, double Ms)> durations,
        CancellationToken cancellationToken)
    {
        LangIndex? lang = null;
        DbIndex? db = null;
        DbAccessor? dbAccessor = null;
        ITextResolver? resolver = null;
        List<DbIndex.UnitRecord> units = new();

        void Report(string message) => progress?.Report(message);

        _indexService.SetStreamingAssetsRoot(streamingAssetsRoot);

        Report("Loading localization files...");
        MeasureStage(durations, "Lang", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var langIndex = new LangIndex(streamingAssetsRoot, locale, fallbackToEnglish: true);
            langIndex.Load();
            lang = langIndex;
            _indexService.SetLangIndex(langIndex);
        }, beginStageCallback, endStageCallback);

        var effectiveLang = lang ?? throw new InvalidOperationException("LangIndex failed to load.");

        Report("Preparing text resolvers...");
        MeasureStage(durations, "Resolvers", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new InfoScriptIndex(effectiveLang.StreamingAssetsRoot);
            var scriptRegistry = new ScriptRegistry(effectiveLang.StreamingAssetsRoot);
            dbAccessor = new DbAccessor(effectiveLang.StreamingAssetsRoot);
            var scriptSettings = new ScriptSettings
            {
                AssumeBaselineStacksWhenMissing = true,
                AssumeZeroForMissingNumericConfig = true,
            };
            var interpreter = new ScriptInterpreter(scriptRegistry, dbAccessor, scriptSettings);

            var basic = new BasicResolver(effectiveLang);
            var spec = new PlaceholderResolver(effectiveLang, info, interpreter, dbAccessor);
            resolver = new TextResolverFacade(_settings.PlaceholderResolverEnabled ? spec : basic);
        }, beginStageCallback, endStageCallback);

        Report("Loading database (units, artifacts, buffs...)");
        MeasureStage(durations, "DbIndex.LoadUnits", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dbIndex = new DbIndex(streamingAssetsRoot);
            var loadedUnits = dbIndex.LoadUnits().ToList();
            db = dbIndex;
            units = loadedUnits;
            _indexService.SetDbIndex(dbIndex);
        }, beginStageCallback, endStageCallback);

        Report("Scanning spells index...");
        MeasureStage(durations, "SpellsIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateSpellsIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning skills index...");
        MeasureStage(durations, "SkillsIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateSkillsIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning artifacts index...");
        MeasureStage(durations, "ArtifactsIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateArtifactsIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning item sets index...");
        MeasureStage(durations, "ItemSetsIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateItemSetsIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning buildings index...");
        MeasureStage(durations, "BuildingsIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateBuildingsIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning subclasses index...");
        MeasureStage(durations, "SubclassesIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateSubclassesIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning map objects index...");
        MeasureStage(durations, "MapObjectsIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateMapObjectsIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning faction laws index...");
        MeasureStage(durations, "FactionLawIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateFactionLawIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning abilities index...");
        MeasureStage(durations, "AbilityIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateAbilityIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning hero specializations index...");
        MeasureStage(durations, "HeroSpecializationsIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateHeroSpecializationsIndex();
        }, beginStageCallback, endStageCallback);

        Report("Scanning heroes index...");
        MeasureStage(durations, "HeroesIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateHeroesIndex();
            if (_indexService.HeroesIndex != null && _indexService.HeroSpecializationsIndex != null)
                _indexService.HeroesIndex.ApplySpecializationIcons(_indexService.HeroSpecializationsIndex);
        }, beginStageCallback, endStageCallback);

        Report("Scanning difficulties index...");
        MeasureStage(durations, "DifficultiesIndex", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _indexService.CreateDifficultiesIndex();
        }, beginStageCallback, endStageCallback);

        List<GameData.Indexing.AbilityAggregateResult> aggregatedAbilities = new();
        Report("Aggregating abilities...");
        MeasureStage(durations, "AbilityAggregation", () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            aggregatedAbilities = AggregateAbilities(units, lang!, resolver!);
        }, beginStageCallback, endStageCallback);

        return new GameDataLoadResult(
            Lang: lang ?? throw new InvalidOperationException("LangIndex failed to load."),
            Db: db ?? throw new InvalidOperationException("DbIndex failed to load."),
            DbAccessor: dbAccessor ?? throw new InvalidOperationException("DbAccessor failed to initialize."),
            ResolverFacade: resolver ?? throw new InvalidOperationException("Text resolver facade failed to initialize."),
            SpellsIndex: _indexService.SpellsIndex ?? throw new InvalidOperationException("Spells index failed to load."),
            SkillsIndex: _indexService.SkillsIndex ?? throw new InvalidOperationException("Skills index failed to load."),
            ArtifactsIndex: _indexService.ArtifactsIndex ?? throw new InvalidOperationException("Artifacts index failed to load."),
            ItemSetsIndex: _indexService.ItemSetsIndex ?? throw new InvalidOperationException("Item sets index failed to load."),
            BuildingsIndex: _indexService.BuildingsIndex ?? throw new InvalidOperationException("Buildings index failed to load."),
            SubclassesIndex: _indexService.SubclassesIndex ?? throw new InvalidOperationException("Subclasses index failed to load."),
            MapObjectsIndex: _indexService.MapObjectsIndex ?? throw new InvalidOperationException("Map objects index failed to load."),
            FactionLawIndex: _indexService.FactionLawIndex ?? throw new InvalidOperationException("Faction law index failed to load."),
            AbilityIndex: _indexService.AbilityIndex ?? throw new InvalidOperationException("Ability index failed to load."),
            HeroSpecializationsIndex: _indexService.HeroSpecializationsIndex ?? throw new InvalidOperationException("Hero specializations index failed to load."),
            HeroesIndex: _indexService.HeroesIndex ?? throw new InvalidOperationException("Heroes index failed to load."),
            DifficultiesIndex: _indexService.DifficultiesIndex ?? throw new InvalidOperationException("Difficulties index failed to load."),
            Units: units,
            AggregatedAbilities: aggregatedAbilities,
            Durations: durations
        );
    }

    private void MeasureStage(List<(string Name, double Ms)> durations, string name, Action action, Action<string>? beginStageCallback, Action? endStageCallback)
    {
        beginStageCallback?.Invoke(name);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            action();
        }
        finally
        {
            sw.Stop();
            durations.Add((name, sw.Elapsed.TotalMilliseconds));
            endStageCallback?.Invoke();
        }
    }

    private List<GameData.Indexing.AbilityAggregateResult> AggregateAbilities(
        List<DbIndex.UnitRecord> units,
        LangIndex lang,
        ITextResolver resolver)
    {
        var logger = _loggerFactory.CreateLogger<GameData.Indexing.AbilityAggregator>();
        var aggregator = new GameData.Indexing.AbilityAggregator(logger, StringComparer.Ordinal);

        var assignedAbilitySids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignedAbilityIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var unit in units)
        {
            if (!string.IsNullOrWhiteSpace(unit.BaseClassNameSid))
            {
                var nameSid = unit.BaseClassNameSid;
                var descriptionSid = unit.BaseClassDescSid ?? "";
                var abilityId = $"{unit.Id}__baseclass__{nameSid}";

                // Resolve description with unit context for proper variant detection
                var ctx = new Localization.Resolution.ResolutionContext(lang.Locale) { UnitId = unit.Id };
                var resolvedDescription = string.IsNullOrWhiteSpace(descriptionSid)
                    ? ""
                    : (resolver.Resolve(descriptionSid, ctx, out _) ?? descriptionSid);

                aggregator.AddAbility(
                    abilityId,
                    nameSid,
                    descriptionSid,
                    resolvedDescription,
                    "BaseClass",
                    unit.Id,
                    "",
                    ""
                );

                assignedAbilitySids.Add(nameSid);
            }

            if (unit.Passives != null)
            {
                for (int abilityIndex = 0; abilityIndex < unit.Passives.Count; abilityIndex++)
                {
                    var passive = unit.Passives[abilityIndex];
                    if (string.IsNullOrWhiteSpace(passive.NameSid)) continue;

                    var nameSid = passive.NameSid;
                    var descriptionSid = passive.DescriptionSid ?? "";
                    var abilityId = $"{unit.Id}__passive__{nameSid}";

                    // Resolve description with full unit context for proper variant detection
                    var ctx = new Localization.Resolution.ResolutionContext(lang.Locale)
                    {
                        UnitId = unit.Id,
                        AbilityIndex = abilityIndex,
                        IsActiveAbility = false
                    };
                    var resolvedDescription = string.IsNullOrWhiteSpace(descriptionSid)
                        ? ""
                        : (resolver.Resolve(descriptionSid, ctx, out _) ?? descriptionSid);

                    aggregator.AddAbility(
                        abilityId,
                        nameSid,
                        descriptionSid,
                        resolvedDescription,
                        "Passive",
                        unit.Id,
                        "",
                        ""
                    );

                    assignedAbilitySids.Add(nameSid);
                }
            }

            if (unit.Abilities != null)
            {
                for (int abilityIndex = 0; abilityIndex < unit.Abilities.Count; abilityIndex++)
                {
                    var active = unit.Abilities[abilityIndex];
                    if (string.IsNullOrWhiteSpace(active.NameSid)) continue;

                    var nameSid = active.NameSid;
                    var descriptionSid = active.DescriptionSid ?? "";
                    var rank = active.Rank?.ToString() ?? "";
                    var energy = active.Energy?.ToString() ?? "";
                    var abilityId = $"{unit.Id}__active__{nameSid}";

                    // Resolve description with full unit context for proper variant detection
                    var ctx = new Localization.Resolution.ResolutionContext(lang.Locale)
                    {
                        UnitId = unit.Id,
                        AbilityIndex = abilityIndex,
                        IsActiveAbility = true
                    };
                    var resolvedDescription = string.IsNullOrWhiteSpace(descriptionSid)
                        ? ""
                        : (resolver.Resolve(descriptionSid, ctx, out _) ?? descriptionSid);

                    aggregator.AddAbility(
                        abilityId,
                        nameSid,
                        descriptionSid,
                        resolvedDescription,
                        "Active",
                        unit.Id,
                        rank,
                        energy
                    );

                    assignedAbilitySids.Add(nameSid);
                }
            }
        }

        var orphanLogger = _loggerFactory.CreateLogger<GameData.Indexing.OrphanAbilityProcessor>();
        var orphanProcessor = new GameData.Indexing.OrphanAbilityProcessor(orphanLogger, _assetService.ExtractedAssetsDirectory);
        var orphanResult = orphanProcessor.ProcessOrphanAbilities(
            lang,
            resolver,
            assignedAbilitySids,
            assignedAbilityIcons
        );

        foreach (var orphanId in orphanResult.OrphanAbilityIds)
        {
            var nameSid = orphanId.Replace("orphan__", "");
            var abilityId = orphanId;

            orphanResult.AbilityDescriptions.TryGetValue(nameSid, out var resolvedDescription);
            orphanResult.AbilityDescriptionSids.TryGetValue(nameSid, out var descriptionSid);

            aggregator.AddAbility(
                abilityId,
                nameSid,
                descriptionSid ?? "",
                resolvedDescription ?? "",
                "Orphan",
                "",
                "",
                ""
            );
        }

        // Use natural string comparer for deterministic variant ordering (base unit -> upgraded -> upgraded_alt)
        var naturalComparer = new NaturalStringComparer();
        return aggregator.GetAggregates(naturalComparer);
    }
}

public record GameDataLoadResult(
    LangIndex Lang,
    DbIndex Db,
    DbAccessor DbAccessor,
    ITextResolver ResolverFacade,
    SpellsIndex SpellsIndex,
    SkillsIndex SkillsIndex,
    ArtifactsIndex ArtifactsIndex,
    ItemSetsIndex ItemSetsIndex,
    BuildingsIndex BuildingsIndex,
    SubclassesIndex SubclassesIndex,
    MapObjectsIndex MapObjectsIndex,
    FactionLawIndex FactionLawIndex,
    AbilityIndex AbilityIndex,
    HeroSpecializationsIndex HeroSpecializationsIndex,
    HeroesIndex HeroesIndex,
    DifficultiesIndex DifficultiesIndex,
    List<DbIndex.UnitRecord> Units,
    List<GameData.Indexing.AbilityAggregateResult> AggregatedAbilities,
    List<(string Name, double Ms)> Durations
);
