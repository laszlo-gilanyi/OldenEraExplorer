using API.Extensions;
using API.Hosting;
using API.Hubs;
using API.Services;
using GameData.Services;

if (args.Length > 0 && args[0] == "--extract")
{
    return ExtractionMode.Run(args);
}

const string AppUrl = "http://localhost:5176";

if (!SingleInstanceService.EnsureSingleInstance())
{
    return 1;
}

// Settings must be loaded before logger construction so VerboseLogging takes effect at startup.
// The same instance is reused as the DI singleton so toggle changes are observed by the logger.
var settingsService = new SettingsService();
settingsService.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Environment.ApplicationName = "Olden Era Explorer";

var diagnosticLogger = builder.ConfigureLogging(settingsService);

builder.Services.AddSingleton(settingsService);
builder.Services.AddSingleton(diagnosticLogger);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddSignalR();
builder.Services.AddGameServices();
builder.Services.AddSingleton<ExtractionHubBroadcaster>();

// Bind to localhost only (no network exposure)
if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls(AppUrl);
}

var app = builder.Build();

SingleInstanceService.StartIpcListener(AppUrl);

// Late-binding: now that DI is built, give the logger a way to read the live game path
// for any subsequent crash-flush header.
diagnosticLogger.AttachGamePathProvider(() =>
    app.Services.GetRequiredService<IGamePathService>().GameRoot);

app.Services.GetRequiredService<ExtractionHubBroadcaster>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();

app.MapHub<ExtractionHub>("/ws/extraction");

app.MapAllEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseEmbeddedStaticFiles();

    if (args.Contains("--from-update"))
    {
        // Auto-update relaunch: give the existing browser tab a chance to reconnect via SignalR
        // before falling back to opening a fresh tab. Avoids leaving the user with two tabs.
        var tracker = app.Services.GetRequiredService<ConnectionTracker>();
        _ = Task.Run(async () =>
        {
            var connected = await tracker.WaitForConnectionAsync(TimeSpan.FromSeconds(5));
            if (connected)
            {
                Console.WriteLine("[update] Existing tab reconnected — skipping fresh tab launch.");
            }
            else
            {
                Console.WriteLine("[update] No tab reconnected within 5s — opening fresh tab.");
                BrowserLauncher.OpenWithRetry(AppUrl);
            }
        });
    }
    else
    {
        BrowserLauncher.OpenWithRetry(AppUrl);
    }

    app.Services.GetRequiredService<TrayIconService>().Initialize(AppUrl);
}

app.Run();
return 0;
