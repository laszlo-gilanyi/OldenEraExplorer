using API.Extensions;
using API.Hosting;
using API.Hubs;
using API.Services;

if (args.Length > 0 && args[0] == "--extract")
{
    return ExtractionMode.Run(args);
}

const string AppUrl = "http://localhost:5176";

UpdateService.CleanupOldFiles();

if (!SingleInstanceService.EnsureSingleInstance())
{
    return 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Environment.ApplicationName = "Olden Era Explorer";

builder.ConfigureLogging();

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
    BrowserLauncher.OpenWithRetry(AppUrl);
    app.Services.GetRequiredService<TrayIconService>().Initialize(AppUrl);
}

app.Run();
return 0;
