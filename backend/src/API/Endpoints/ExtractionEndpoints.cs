using API.Contracts;
using API.Models;
using API.Services;

namespace API.Endpoints;

public static class ExtractionEndpoints
{
    public static IEndpointRouteBuilder MapExtractionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/extraction")
            .WithTags("Extraction")
            ;

        // POST /api/extraction/start - Start extraction
        group.MapPost("/start", StartExtraction)
            .WithName("StartExtraction")
            .WithSummary("Start asset extraction")
            .WithDescription("Starts extracting game assets (PNG icons and/or GLB models) from Unity game files.")
            .Produces<StartExtractionResponse>(200)
            .Produces<ErrorDto>(400)
            .Produces<ErrorDto>(409);

        // POST /api/extraction/cancel - Cancel extraction
        group.MapPost("/cancel", CancelExtraction)
            .WithName("CancelExtraction")
            .WithSummary("Cancel running extraction")
            .WithDescription("Cancels the currently running extraction job.")
            .Produces(204)
            .Produces<ErrorDto>(400);

        // POST /api/extraction/dismiss - Reset terminal state to Idle
        group.MapPost("/dismiss", DismissExtraction)
            .WithName("DismissExtraction")
            .WithSummary("Dismiss terminal extraction state")
            .WithDescription("Resets a Failed/Completed/Cancelled status back to Idle. No-op while extracting.")
            .Produces(204);

        // GET /api/extraction/status - Get status
        group.MapGet("/status", GetStatus)
            .WithName("GetExtractionStatus")
            .WithSummary("Get extraction status")
            .WithDescription("Returns the current extraction status and progress information.")
            .Produces<ExtractionStatusDto>(200);

        // GET /api/extraction/auto-extract - Get auto-extract setting
        group.MapGet("/auto-extract", GetAutoExtract)
            .WithName("GetAutoExtract")
            .WithSummary("Get auto-extract setting")
            .WithDescription("Returns whether auto-extraction is enabled on game load.")
            .Produces<AutoExtractSettingDto>(200);

        // PUT /api/extraction/auto-extract - Set auto-extract setting
        group.MapPut("/auto-extract", SetAutoExtract)
            .WithName("SetAutoExtract")
            .WithSummary("Set auto-extract setting")
            .WithDescription("Enables or disables auto-extraction on game load.")
            .Produces<AutoExtractSettingDto>(200);

        return endpoints;
    }

    private static IResult StartExtraction(
        StartExtractionRequest? request,
        IAssetExtractionService extractionService,
        ILogger<AssetExtractionService> logger)
    {
        try
        {
            var options = request ?? new StartExtractionRequest();

            if (!options.ExtractPng && !options.ExtractGlb)
            {
                return Results.BadRequest(new ErrorDto(
                    "Invalid request",
                    "At least one of ExtractPng or ExtractGlb must be true."
                ));
            }

            var jobId = extractionService.StartExtraction(options);

            logger.LogInformation("Extraction started with job ID: {JobId}", jobId);

            return Results.Ok(new StartExtractionResponse(jobId));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorDto("Extraction error", ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start extraction");
            return Results.BadRequest(new ErrorDto("Failed to start extraction", ex.Message));
        }
    }

    private static IResult CancelExtraction(
        IAssetExtractionService extractionService,
        ILogger<AssetExtractionService> logger)
    {
        try
        {
            extractionService.CancelExtraction();
            logger.LogInformation("Extraction cancellation requested");
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cancel extraction");
            return Results.BadRequest(new ErrorDto("Failed to cancel extraction", ex.Message));
        }
    }

    private static IResult DismissExtraction(IAssetExtractionService extractionService)
    {
        extractionService.DismissTerminalState();
        return Results.NoContent();
    }

    private static IResult GetStatus(IAssetExtractionService extractionService)
    {
        var status = extractionService.GetStatus();
        return Results.Ok(status);
    }

    private static IResult GetAutoExtract(SettingsService settingsService)
    {
        return Results.Ok(new AutoExtractSettingDto(settingsService.AutoExtractEnabled));
    }

    private static IResult SetAutoExtract(
        UpdateAutoExtractRequest request,
        SettingsService settingsService,
        ILogger<AssetExtractionService> logger)
    {
        settingsService.AutoExtractEnabled = request.Enabled;
        settingsService.Save();

        logger.LogInformation("Auto-extract setting updated and saved: {Enabled}", request.Enabled);

        return Results.Ok(new AutoExtractSettingDto(request.Enabled));
    }
}
