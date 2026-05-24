using AssetExtractor.Models;
using AssetExtractor.Pipeline;
using AssetExtractor.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System.IO;
using System.Collections.Generic;

namespace AssetExtractor.CLI;

sealed class CleanFormatter : ConsoleFormatter
{
    const string RESET = "\x1b[0m";
    const string RED = "\x1b[91m";      // Error
    const string YELLOW = "\x1b[93m";   // Warning
    const string MAGENTA = "\x1b[95m";  // Animation
    const string GREEN = "\x1b[92m";    // Mesh
    const string CYAN = "\x1b[96m";     // Bone/Skeleton
    const string BLUE = "\x1b[94m";     // Texture

    public CleanFormatter() : base("clean") { }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message == null) return;

        var scopes = new List<string>();
        scopeProvider?.ForEachScope((scope, state) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object>> scopeItems)
            {
                foreach (var item in scopeItems)
                {
                    scopes.Add($"{item.Key}: {item.Value}");
                }
            }
            else if (scope != null)
            {
                scopes.Add(scope.ToString() ?? "");
            }
        }, (object?)null);

        string levelPrefix = logEntry.LogLevel switch
        {
            LogLevel.Warning => $"{YELLOW}[WARNING]{RESET} ",
            LogLevel.Error => $"{RED}[ERROR]{RESET} ",
            LogLevel.Critical => $"{RED}[CRITICAL]{RESET} ",
            _ => ""  // No prefix for Info/Debug
        };

        string coloredMessage = ColorizeMessage(message);

        if (scopes.Count > 0 && !string.IsNullOrEmpty(scopes[0]))
        {
            textWriter.WriteLine($"{levelPrefix}[{scopes[0]}] {coloredMessage}");
        }
        else
        {
            textWriter.WriteLine($"{levelPrefix}{coloredMessage}");
        }
    }

    private string ColorizeMessage(string message)
    {
        string lower = message.ToLowerInvariant();

        if (lower.Contains("animation") || lower.Contains("animator") || lower.Contains("clip") || lower.Contains("controller"))
            return $"{MAGENTA}{message}{RESET}";

        if (lower.Contains("mesh") || lower.Contains("skinned") || lower.Contains("rigid"))
            return $"{GREEN}{message}{RESET}";

        if (lower.Contains("bone") || lower.Contains("skeleton") || lower.Contains("skin") || lower.Contains("joint"))
            return $"{CYAN}{message}{RESET}";

        if (lower.Contains("texture") || lower.Contains("cubemap") || lower.Contains("material"))
            return $"{BLUE}{message}{RESET}";

        return message;
    }
}

class Program
{
    static int Main(string[] args)
    {
        var config = CliConfig.Parse(args);
        if (config == null)
        {
            PrintHelp();
            return 0;
        }

        if (CliConfig.HasDeprecatedVersionedFlag(args))
        {
            Console.WriteLine("Warning: --versioned flag is deprecated and ignored. Version management is always enabled.\n");
        }

        try
        {
            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddConsoleFormatter<CleanFormatter, SimpleConsoleFormatterOptions>(options =>
                {
                    options.IncludeScopes = true;
                });
                builder.AddConsole(options => options.FormatterName = "clean");

                // The debug command emits findings at Information. Without raising the floor
                // they would be silent at the default Warning level.
                LogLevel minLevel = config.IsVerbose
                    ? LogLevel.Debug
                    : config.Command == "debug"
                        ? LogLevel.Information
                        : LogLevel.Warning;
                builder.SetMinimumLevel(minLevel);

                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);

                // Vendor parser warnings ("Cannot process empty mesh" etc.) are bundle-wide
                // and unrelated to the debug command's prefab focus, so they are suppressed
                // there. extract-* keeps Warning because vendor warnings can flag real
                // export problems in that context.
                LogLevel vendorLevel = config.IsVerbose
                    ? LogLevel.Trace
                    : config.Command == "debug"
                        ? LogLevel.Error
                        : LogLevel.Warning;
                builder.AddFilter("UnityReader.Vendor", vendorLevel);
            });

            var serviceProvider = services.BuildServiceProvider();

            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            LoggingConfig.IsVerboseMode = config.IsVerbose;

            ProgressBar.JsonOutputMode = config.JsonProgress;

            // Wire UnityReader's vendor diagnostics into our logging pipeline once at startup.
            UnityReader.UnityScene.LoggerFactory = loggerFactory;

            var orchestratorLogger = loggerFactory.CreateLogger<ExtractionOrchestrator>();
            var orchestrator = config.OutputPath != null
                ? new ExtractionOrchestrator(config.OutputPath, orchestratorLogger, loggerFactory)
                : new ExtractionOrchestrator(orchestratorLogger, loggerFactory);

            return config.Command switch
            {
                "extract-glb" => HandleExtractGlb(orchestrator, config.Arguments, config.GamePath),
                "extract-glb-units" => HandleExtractGlbUnits(orchestrator, config.GamePath, config.Force),
                "extract-glb-mapobjects" => HandleExtractGlbMapObjects(orchestrator, config.GamePath, config.Force),
                "extract-glb-gameobjects" => HandleExtractGlbGameObjects(orchestrator, config.GamePath, config.Force),
                "extract-all-glb" => HandleExtractAllGlb(orchestrator, config.GamePath, config.Force),

                "extract-textures" => HandleExtractTextures(orchestrator, config.GamePath, config.Force),

                "extract-all" => HandleExtractAll(orchestrator, config.GamePath, config.Force),

                "list-units" => HandleListUnits(orchestrator, config.GamePath),
                "list-map-objects" => HandleListMapObjects(orchestrator, config.GamePath),
                "list-game-objects" => HandleListGameObjects(),
                "list-all-prefabs" => HandleListAllPrefabs(orchestrator, config.GamePath, config.Arguments),
                "list-resource-paths" => HandleListResourcePaths(orchestrator, config.GamePath, config.Arguments),

                "debug" => HandleDebug(orchestrator, config.Arguments, config.GamePath),

                "extract" => HandleExtractGlb(orchestrator, config.Arguments, config.GamePath),
                "extract-gameobject" => HandleExtractGlb(orchestrator, config.Arguments, config.GamePath),
                "extract-all-units" => HandleExtractGlbUnits(orchestrator, config.GamePath, config.Force),
                "extract-all-map-objects" => HandleExtractGlbMapObjects(orchestrator, config.GamePath, config.Force),

                _ => HandleUnknownCommand(config.Command)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }


    static int HandleExtractGlb(ExtractionOrchestrator orchestrator, List<string> args, string? manualPath)
    {
        if (args.Count < 2)
        {
            Console.WriteLine("Error: Insufficient arguments");
            Console.WriteLine("Usage: extract-glb <name> [--game-path <path>]");
            Console.WriteLine("Examples: extract-glb esquire");
            Console.WriteLine("          extract-glb interactive/mine_gold");
            Console.WriteLine("          extract-glb \"PLATFORM + BACK\"");
            return 1;
        }

        string name = args[1];
        Console.WriteLine($"Extracting: {name}\n");

        ExtractionResult result;

        if (ExtractionOrchestrator.IsKnownGameObject(name))
        {
            result = orchestrator.ExtractGameObject(name, manualPath);
        }
        else
        {
            result = orchestrator.ExtractPrefab(name, manualPath);

            if (!result.Success && result.ErrorMessage?.Contains("not found") == true)
            {
                Console.WriteLine("Not found as unit/map object, trying as GameObject...\n");
                result = orchestrator.ExtractGameObject(name, manualPath);
            }
        }

        if (result.Success)
        {
            Console.WriteLine($"\nSuccess! Exported to: {result.OutputPath}");

            if (result.TexturePaths.Count > 0)
            {
                Console.WriteLine($"\nTextures ({result.TexturePaths.Count}):");
                foreach (var texturePath in result.TexturePaths)
                {
                    Console.WriteLine($"  - {texturePath}");
                }
            }
            return 0;
        }
        else
        {
            Console.WriteLine($"\n[ERROR] {result.ErrorMessage}");
            Console.WriteLine("\nTroubleshooting: Run 'list-units', 'list-map-objects', or 'list-all-prefabs' to verify name");
            return 1;
        }
    }

    static int HandleExtractGlbUnits(ExtractionOrchestrator orchestrator, string? manualPath, bool force)
    {
        var result = orchestrator.ExtractAllUnits(manualPath, force);
        return PrintBatchResult(result);
    }

    static int HandleExtractGlbMapObjects(ExtractionOrchestrator orchestrator, string? manualPath, bool force)
    {
        var result = orchestrator.ExtractAllMapObjects(manualPath, force);
        return PrintBatchResult(result);
    }

    static int HandleExtractGlbGameObjects(ExtractionOrchestrator orchestrator, string? manualPath, bool force)
    {
        var result = orchestrator.ExtractAllGameObjects(manualPath, force);
        return PrintBatchResult(result);
    }

    static int HandleExtractAllGlb(ExtractionOrchestrator orchestrator, string? manualPath, bool force)
    {
        var result = orchestrator.ExtractAllGlb(manualPath, force);
        return PrintBatchResult(result);
    }

    static int HandleExtractTextures(ExtractionOrchestrator orchestrator, string? manualPath, bool force)
    {
        try
        {
            var gamePath = orchestrator.ResolveGamePath(manualPath);
            if (gamePath == null)
            {
                Console.WriteLine("Error: Could not resolve game path. Use --game-path to specify manually.");
                return 1;
            }

            var version = ManifestManager.DetectGameVersion(gamePath);

            if (!force && orchestrator.ManifestService.IsVersionExtracted(version))
            {
                Console.WriteLine($"Version {version} already extracted. Use --force to re-extract.");
                return 0;
            }

            Console.WriteLine($"Extracting textures for version: {version}\n");

            var result = orchestrator.ExtractTexturesOnly(gamePath, force);

            return result.FailedCount > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Texture extraction failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner: {ex.InnerException.Message}");
            }
            return 1;
        }
    }

    static int HandleExtractAll(ExtractionOrchestrator orchestrator, string? manualPath, bool force)
    {
        var result = orchestrator.ExtractEverything(manualPath, force);
        return PrintBatchResult(result);
    }

    static int PrintBatchResult(BatchExtractionResult result)
    {
        if (ProgressBar.JsonOutputMode)
        {
            var status = new
            {
                type = "status",
                status = result.FailedCount > 0 ? "completed_with_errors" : "completed",
                success = result.SuccessCount,
                failed = result.FailedCount,
                total = result.TotalCount,
                failedItems = result.FailedItems.Take(20).ToList()
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(status));
        }
        else
        {
            Console.WriteLine($"\nCOMPLETE: {result.SuccessCount}/{result.TotalCount} success, {result.FailedCount} failed");

            if (result.FailedItems.Count > 0)
            {
                Console.WriteLine($"Failed: {string.Join(", ", result.FailedItems.Take(10))}");
                if (result.FailedItems.Count > 10)
                {
                    Console.WriteLine($"... and {result.FailedItems.Count - 10} more");
                }
            }
        }

        return result.FailedCount > 0 ? 1 : 0;
    }

    static int HandleListUnits(ExtractionOrchestrator orchestrator, string? manualPath)
    {
        Console.WriteLine("Listing available units...\n");

        var units = orchestrator.ListUnits(manualPath);

        if (units.Count == 0)
        {
            Console.WriteLine("Error: Could not load units. Check game path.");
            return 1;
        }

        Console.WriteLine($"Found {units.Count} units:\n");
        foreach (var unit in units)
        {
            Console.WriteLine($"  - {unit}");
        }

        return 0;
    }

    static int HandleListMapObjects(ExtractionOrchestrator orchestrator, string? manualPath)
    {
        Console.WriteLine("Listing available map objects...\n");

        var mapObjects = orchestrator.ListMapObjects(manualPath);

        if (mapObjects.Count == 0)
        {
            Console.WriteLine("Error: Could not load map objects. Check game path.");
            return 1;
        }

        Console.WriteLine($"Found {mapObjects.Count} map objects:\n");

        var grouped = mapObjects.GroupBy(obj =>
        {
            var parts = obj.Split('/');
            return parts.Length > 0 ? parts[0] : "unknown";
        });

        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            Console.WriteLine($"\n{group.Key.ToUpper()}:");
            foreach (var obj in group.OrderBy(x => x))
            {
                Console.WriteLine($"  - {obj}");
            }
        }

        return 0;
    }

    static int HandleListGameObjects()
    {
        Console.WriteLine("Known GameObjects (for batch extraction):\n");
        Console.WriteLine("  - PLATFORM + BACK");
        Console.WriteLine("\nTotal: 1");
        Console.WriteLine("\nNote: Any GameObject can be extracted by name using 'extract-glb <name>'");

        return 0;
    }

    static int HandleListAllPrefabs(ExtractionOrchestrator orchestrator, string? manualPath, List<string> args)
    {
        Console.WriteLine("Listing all prefabs from asset cache...\n");

        var prefabs = orchestrator.ListAllPrefabs(manualPath);

        if (prefabs.Count == 0)
        {
            Console.WriteLine("Error: Could not load prefabs. Check game path.");
            return 1;
        }

        string? filter = args.Count > 1 ? args[1].ToLower() : null;

        var filtered = filter != null
            ? prefabs.Where(p => p.ToLower().Contains(filter)).ToList()
            : prefabs;

        Console.WriteLine($"Found {prefabs.Count} total prefabs");
        if (filter != null)
        {
            Console.WriteLine($"Filtered to {filtered.Count} containing '{filter}':\n");
        }
        else
        {
            Console.WriteLine();
        }

        foreach (var prefab in filtered)
        {
            Console.WriteLine($"  - {prefab}");
        }

        return 0;
    }

    static int HandleListResourcePaths(ExtractionOrchestrator orchestrator, string? manualPath, List<string> args)
    {
        Console.WriteLine("Listing resource paths from IResourceManager...\n");

        string? filter = args.Count > 1 ? args[1] : null;

        var paths = orchestrator.ListResourcePaths(manualPath, filter);

        if (paths.Count == 0)
        {
            Console.WriteLine(filter != null
                ? $"No resource paths found matching '{filter}'."
                : "No resource paths found.");
            return 0;
        }

        Console.WriteLine($"Found {paths.Count} resource paths" + (filter != null ? $" matching '{filter}'" : "") + ":\n");

        foreach (var path in paths)
        {
            Console.WriteLine($"  - {path}");
        }

        return 0;
    }

    static int HandleDebug(ExtractionOrchestrator orchestrator, List<string> args, string? manualPath)
    {
        if (args.Count < 2)
        {
            Console.WriteLine("Error: Insufficient arguments");
            Console.WriteLine("Usage: debug <name-or-term> [--game-path <path>]");
            return 1;
        }

        string nameOrTerm = args[1];
        orchestrator.Debug(nameOrTerm, manualPath);
        return 0;
    }

    static int HandleUnknownCommand(string command)
    {
        Console.WriteLine($"Error: Unknown command '{command}'");
        Console.WriteLine("Run with --help to see available commands");
        return 1;
    }

    static void PrintHelp()
    {
        Console.WriteLine("Unity Asset to GLB Exporter");
        Console.WriteLine("Extract unit models, map objects, and GameObjects from Unity .assets files to GLB format");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run --project src/AssetExtractor.CLI/AssetExtractor.CLI.csproj -- <command> [options]");
        Console.WriteLine();
        Console.WriteLine("GLB Extraction Commands:");
        Console.WriteLine("  extract-glb <name>               Extract single GLB (auto-detect: unit/mapobject/gameobject)");
        Console.WriteLine("                                   Examples: esquire, interactive/mine_gold, \"PLATFORM + BACK\"");
        Console.WriteLine("  extract-glb-units                Extract all units");
        Console.WriteLine("  extract-glb-mapobjects           Extract all map objects (filtered)");
        Console.WriteLine("  extract-glb-gameobjects          Extract all known GameObjects");
        Console.WriteLine("  extract-all-glb                  Extract all GLBs (units + mapobjects + gameobjects)");
        Console.WriteLine();
        Console.WriteLine("Texture Extraction Commands:");
        Console.WriteLine("  extract-textures                 Extract all textures (icons + objects)");
        Console.WriteLine();
        Console.WriteLine("Combined Extraction:");
        Console.WriteLine("  extract-all                      Extract everything (GLBs + textures)");
        Console.WriteLine();
        Console.WriteLine("List Commands:");
        Console.WriteLine("  list-units                       List all available units");
        Console.WriteLine("  list-map-objects                 List all available map objects");
        Console.WriteLine("  list-game-objects                List known GameObjects for batch extraction");
        Console.WriteLine("  list-all-prefabs [filter]        List all prefabs (optionally filter by name)");
        Console.WriteLine();
        Console.WriteLine("Debug/Analysis Commands:");
        Console.WriteLine("  debug <name-or-term>             Inspect a prefab (renderers, animators, hierarchy)");
        Console.WriteLine("                                   or search assets by name if no prefab matches");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --game-path <path>               Manually specify game installation path");
        Console.WriteLine("  --output-path <path>             Specify output directory (default: ./output)");
        Console.WriteLine("  --force, -f                      Force re-extraction even if version is cached");
        Console.WriteLine("  --json-progress                  Output progress as JSON lines (for automation)");
        Console.WriteLine("  --verbose                        Enable verbose logging (DEBUG level)");
        Console.WriteLine("  --help, -h                       Show this help message");
        Console.WriteLine();
        Console.WriteLine("Output Structure:");
        Console.WriteLine("  output/Assets-{version}/Resources/units/{faction}/{name}.glb");
        Console.WriteLine("  output/Assets-{version}/Resources/objects/{category}/{name}.glb");
        Console.WriteLine("  output/Assets-{version}/GameObject/{name}.glb");
        Console.WriteLine("  output/Assets-{version}/Resources/icons/{category}/{name}.png");
        Console.WriteLine("  output/Assets-shared/...         (shared assets after auto-promotion)");
        Console.WriteLine("  output/cache_manifest.json       (asset tracking manifest)");
        Console.WriteLine();
        Console.WriteLine("Note: Version management and deduplication are always enabled.");
        Console.WriteLine("      Batch commands skip extraction if version is already cached (use --force to override).");
        Console.WriteLine("      Asset promotion runs automatically after extraction.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- extract-glb esquire");
        Console.WriteLine("  dotnet run -- extract-glb interactive/mine_gold");
        Console.WriteLine("  dotnet run -- extract-glb \"PLATFORM + BACK\"");
        Console.WriteLine("  dotnet run -- extract-glb-units");
        Console.WriteLine("  dotnet run -- extract-all-glb");
        Console.WriteLine("  dotnet run -- extract-textures");
        Console.WriteLine("  dotnet run -- extract-all");
        Console.WriteLine("  dotnet run -- list-all-prefabs platform");
    }
}
