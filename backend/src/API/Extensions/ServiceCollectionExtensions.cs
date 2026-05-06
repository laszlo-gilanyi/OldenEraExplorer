using API.Hosting;
using API.Services;
using GameData.Loading;
using GameData.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameServices(this IServiceCollection services)
    {
        // Settings are pre-registered in Program.cs so the logger can read VerboseLogging
        // at startup. TryAdd preserves whichever instance was registered first.
        services.TryAddSingleton<SettingsService>(sp =>
        {
            var settings = new SettingsService();
            settings.Load();
            return settings;
        });

        services.AddSingleton<IndexService>();
        services.AddSingleton<IDataCatalog, DataCatalog>();
        services.AddSingleton<IGamePathService, GamePathService>();
        services.AddSingleton<IGameDataService, GameDataService>();
        services.AddSingleton<FactionMapper>();
        services.AddSingleton<ClassMapper>();

        services.AddSingleton<TexturePathResolver>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<TexturePathResolver>();
            return new TexturePathResolver(AppContext.BaseDirectory, logger);
        });

        services.AddSingleton<IAssetServingService, AssetServingService>();
        services.AddSingleton<IReferenceIndexService, ReferenceIndexService>();
        services.AddSingleton<IAssetExtractionService, AssetExtractionService>();
        services.AddSingleton<SearchService>();
        services.AddScoped<MapObjectDetailsService>();

        services.AddSingleton<ConnectionTracker>();
        services.AddSingleton<TrayIconService>();
        services.AddHttpClient<UpdateService>();

        return services;
    }
}
