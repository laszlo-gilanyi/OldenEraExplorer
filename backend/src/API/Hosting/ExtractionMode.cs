using System.Text.Json;
using AssetExtractor.Pipeline;
using AssetExtractor.Utilities;

namespace API.Hosting;

/// <summary>
/// Subprocess mode for asset extraction.
/// Parent process spawns API with --extract flag to isolate heavy Unity asset operations.
/// </summary>
public static class ExtractionMode
{
    public static int Run(string[] args)
    {
        string? gamePath = null;
        string? outputPath = null;
        bool force = false;
        bool extractPng = false;
        bool extractGlb = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game-path" when i + 1 < args.Length:
                    gamePath = args[++i];
                    break;
                case "--output-path" when i + 1 < args.Length:
                    outputPath = args[++i];
                    break;
                case "--force":
                    force = true;
                    break;
                case "--png":
                    extractPng = true;
                    break;
                case "--glb":
                    extractGlb = true;
                    break;
                case "--all":
                    extractPng = true;
                    extractGlb = true;
                    break;
            }
        }

        if (string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(outputPath))
        {
            OutputJson("error", null, "Missing required arguments: --game-path and --output-path");
            return 1;
        }

        if (!extractPng && !extractGlb)
        {
            OutputJson("error", null, "Specify at least one of: --png, --glb, --all");
            return 1;
        }

        ProgressBar.JsonOutputMode = true;

        try
        {
            var orchestrator = new ExtractionOrchestrator(outputPath);

            if (extractPng && extractGlb)
            {
                var result = orchestrator.ExtractEverything(gamePath, force);
                var errors = result.Results.Where(r => !r.Success && r.ErrorMessage != null).ToList();
                foreach (var err in errors)
                    OutputJson("error", null, err.ErrorMessage);
                return (result.FailedCount > 0 || errors.Count > 0) ? 1 : 0;
            }
            else if (extractGlb)
            {
                var result = orchestrator.ExtractAllGlb(gamePath, force);
                return result.FailedCount > 0 ? 1 : 0;
            }
            else // extractPng only
            {
                var version = ManifestManager.DetectGameVersion(gamePath);
                if (!force && orchestrator.ManifestService.IsVersionExtracted(version))
                {
                    OutputJson("status", "completed", $"Version {version} already extracted");
                    return 0;
                }

                orchestrator.InitializeVersionManagement(gamePath);

                using var textureExtractor = new AssetExtractor.Extraction.StandaloneTextureExtractor(
                    gamePath, outputPath);

                var stats = textureExtractor.ExtractTexturesVersioned(version, orchestrator.ManifestService);
                orchestrator.ManifestService.SaveManifest();

                if (orchestrator.PromotionService.IsPromotionNeeded())
                {
                    orchestrator.PromoteAssetsAsync().Wait();
                }

                return stats.FailedCount > 0 ? 1 : 0;
            }
        }
        catch (Exception ex)
        {
            OutputJson("error", null, ex.Message);
            return 1;
        }
    }

    private static void OutputJson(string type, string? status, string? message)
    {
        var json = JsonSerializer.Serialize(new { type, status, message });
        Console.WriteLine(json);
    }
}
