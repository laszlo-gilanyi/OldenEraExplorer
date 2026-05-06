using GameData.Loading;
using Localization.Services;
using API.Contracts;
using API.Hosting;
using API.Services;

namespace API.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settings")
            .WithTags("Settings")
            ;

        // GET /api/settings - Get current settings
        group.MapGet("/", GetSettings)
            .WithName("GetSettings")
            .WithSummary("Get current settings")
            .WithDescription("Returns the current application settings including theme, locale, and resolver options.")
            .Produces<SettingsDto>(200);

        // PUT /api/settings - Update settings
        group.MapPut("/", UpdateSettings)
            .WithName("UpdateSettings")
            .WithSummary("Update settings")
            .WithDescription("Updates application settings. All fields are optional; only provided fields will be updated.")
            .Produces<SettingsDto>(200);

        // GET /api/settings/locales - Get available locales
        group.MapGet("/locales", GetAvailableLocales)
            .WithName("GetAvailableLocales")
            .WithSummary("Get available locales")
            .WithDescription("Returns a list of all available game locales found in the StreamingAssets/Lang directory.")
            .Produces<LocalesDto>(200);

        // GET /api/settings/check-update
        group.MapGet("/check-update", async (UpdateService updateService) =>
        {
            var release = await updateService.CheckForUpdatesAsync();
            return Results.Ok(release);
        })
        .WithName("CheckUpdate")
        .WithSummary("Check for a newer release on GitHub")
        .Produces<ReleaseInfo?>(200);

        // POST /api/settings/install-update
        group.MapPost("/install-update", (ReleaseInfo release, UpdateService updateService) =>
        {
            _ = Task.Run(() => updateService.DownloadAndInstallUpdateAsync(release));
            return Results.Accepted();
        })
        .WithName("InstallUpdate")
        .WithSummary("Download and install a release, then restart")
        .Produces(202);

        // GET /api/settings/update-progress
        group.MapGet("/update-progress", (UpdateService updateService) =>
        {
            return Results.Ok(updateService.InstallProgress);
        })
        .WithName("GetUpdateProgress")
        .WithSummary("Poll installation progress")
        .Produces<UpdateProgress?>(200);

        return endpoints;
    }

    private static IResult GetSettings(SettingsService settings, IGamePathService pathService)
    {
        var dto = new SettingsDto(
            Theme: settings.ThemeVariant,
            Locale: settings.LastLocale,
            UsePlaceholderResolver: settings.PlaceholderResolverEnabled,
            ShowResolverOutput: false,
            AutoExtractEnabled: settings.AutoExtractEnabled,
            ExtractPng: settings.ExtractPng,
            ExtractGlb: settings.ExtractGlb,
            MinimizeToTray: settings.MinimizeToTray,
            AutoUpdateEnabled: settings.AutoUpdateEnabled,
            Version: UpdateService.CurrentVersion
        );

        return Results.Ok(dto);
    }

    private static IResult UpdateSettings(
        UpdateSettingsRequest request,
        SettingsService settings,
        TrayIconService trayIconService,
        IGamePathService pathService,
        IGameDataService dataService,
        IDataCatalog dataCatalog)
    {
        bool localeChanged = false;
        bool resolverChanged = false;

        // Update theme if provided
        if (!string.IsNullOrEmpty(request.Theme))
        {
            if (request.Theme != "Default" && request.Theme != "Light" && request.Theme != "Dark")
            {
                return Results.BadRequest(new ErrorDto(
                    "Invalid theme value",
                    "Theme must be one of: 'Default', 'Light', or 'Dark'"
                ));
            }
            settings.ThemeVariant = request.Theme;
        }

        // Update locale if provided
        if (!string.IsNullOrEmpty(request.Locale))
        {
            // Track if locale actually changed
            if (!settings.LastLocale.Equals(request.Locale, StringComparison.OrdinalIgnoreCase))
            {
                settings.LastLocale = request.Locale;
                localeChanged = true;
            }
        }

        // Update placeholder resolver if provided
        if (request.UsePlaceholderResolver.HasValue)
        {
            if (settings.PlaceholderResolverEnabled != request.UsePlaceholderResolver.Value)
            {
                settings.PlaceholderResolverEnabled = request.UsePlaceholderResolver.Value;
                resolverChanged = true;
            }
        }

        // Update auto-extract enabled if provided
        if (request.AutoExtractEnabled.HasValue)
        {
            settings.AutoExtractEnabled = request.AutoExtractEnabled.Value;
        }

        // Update extract PNG if provided
        if (request.ExtractPng.HasValue)
        {
            settings.ExtractPng = request.ExtractPng.Value;
        }

        // Update extract GLB if provided
        if (request.ExtractGlb.HasValue)
        {
            settings.ExtractGlb = request.ExtractGlb.Value;
        }

        // Update minimize to tray if provided
        if (request.MinimizeToTray.HasValue && settings.MinimizeToTray != request.MinimizeToTray.Value)
        {
            settings.MinimizeToTray = request.MinimizeToTray.Value;
            if (settings.MinimizeToTray)
                trayIconService.ShowIcon();
            else
                trayIconService.HideIcon();
        }

        if (request.AutoUpdateEnabled.HasValue)
        {
            settings.AutoUpdateEnabled = request.AutoUpdateEnabled.Value;
        }

        // Save settings to disk
        settings.Save();

        // Handle data refresh based on what changed
        if (pathService.IsPathSet && pathService.GameRoot != null)
        {
            if (resolverChanged)
            {
                pathService.SetPath(pathService.GameRoot, settings.LastLocale);
            }
            else if (localeChanged && dataService.IsLoaded)
            {
                // Locale changed - need full reload to re-resolve text with new language
                // The cached data has already-resolved strings that won't update otherwise
                pathService.SetPath(pathService.GameRoot, settings.LastLocale);
            }
        }

        var dto = new SettingsDto(
            Theme: settings.ThemeVariant,
            Locale: settings.LastLocale,
            UsePlaceholderResolver: settings.PlaceholderResolverEnabled,
            ShowResolverOutput: false,
            AutoExtractEnabled: settings.AutoExtractEnabled,
            ExtractPng: settings.ExtractPng,
            ExtractGlb: settings.ExtractGlb,
            MinimizeToTray: settings.MinimizeToTray,
            AutoUpdateEnabled: settings.AutoUpdateEnabled,
            Version: UpdateService.CurrentVersion
        );

        return Results.Ok(dto);
    }

    private static async Task<IResult> GetAvailableLocales(
        IGamePathService pathService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var locales = new List<string>();

        if (pathService.IsPathSet && !string.IsNullOrEmpty(pathService.StreamingAssetsPath))
        {
            var discoveryService = new LocaleDiscoveryService(
                loggerFactory.CreateLogger<LocaleDiscoveryService>()
            );

            locales = await discoveryService.DiscoverLocalesAsync(
                pathService.StreamingAssetsPath,
                cancellationToken
            );
        }

        // This ensures all 14 supported languages are available on first run
        if (locales.Count == 0)
        {
            locales = OverlayService.Instance.AvailableLocales
                .OrderBy(x => x.Equals("english", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var dto = new LocalesDto(
            Locales: locales,
            CurrentLocale: pathService.CurrentLocale
        );

        return Results.Ok(dto);
    }
}

// Settings DTOs =====

/// <param name="Theme">Theme variant: "Default" (system), "Light", or "Dark".</param>
/// <param name="Locale">Current locale (e.g., "english", "hungarian").</param>
/// <param name="UsePlaceholderResolver">Whether to use placeholder text resolution.</param>
/// <param name="ShowResolverOutput">Whether to show resolver output in UI.</param>
/// <param name="AutoExtractEnabled">Whether to automatically extract assets after setting game path.</param>
/// <param name="ExtractPng">Whether to extract PNG icons during auto-extraction.</param>
/// <param name="ExtractGlb">Whether to extract GLB models during auto-extraction.</param>
/// <param name="MinimizeToTray">Whether to keep the app running in the system tray after all browser tabs are closed.</param>
/// <param name="AutoUpdateEnabled">Whether to automatically install updates on next launch.</param>
/// <param name="Version">Current application version.</param>
public record SettingsDto(
    string Theme,
    string Locale,
    bool UsePlaceholderResolver,
    bool ShowResolverOutput,
    bool AutoExtractEnabled,
    bool ExtractPng,
    bool ExtractGlb,
    bool MinimizeToTray,
    bool AutoUpdateEnabled,
    string Version
);

/// <param name="Theme">Optional: Theme variant to set.</param>
/// <param name="Locale">Optional: Locale to set.</param>
/// <param name="UsePlaceholderResolver">Optional: Whether to use placeholder resolver.</param>
/// <param name="ShowResolverOutput">Optional: Whether to show resolver output.</param>
/// <param name="AutoExtractEnabled">Optional: Whether to enable auto-extract after path set.</param>
/// <param name="ExtractPng">Optional: Whether to extract PNG icons.</param>
/// <param name="ExtractGlb">Optional: Whether to extract GLB models.</param>
/// <param name="MinimizeToTray">Optional: Whether to minimize to tray on tab close.</param>
/// <param name="AutoUpdateEnabled">Optional: Whether to enable automatic updates.</param>
public record UpdateSettingsRequest(
    string? Theme = null,
    string? Locale = null,
    bool? UsePlaceholderResolver = null,
    bool? ShowResolverOutput = null,
    bool? AutoExtractEnabled = null,
    bool? ExtractPng = null,
    bool? ExtractGlb = null,
    bool? MinimizeToTray = null,
    bool? AutoUpdateEnabled = null
);

/// <param name="Locales">List of available locale codes.</param>
/// <param name="CurrentLocale">The currently selected locale.</param>
public record LocalesDto(
    IReadOnlyList<string> Locales,
    string CurrentLocale
);

