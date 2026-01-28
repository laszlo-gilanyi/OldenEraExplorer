using API.Services;
using GameData.Loading;
using GameData.Services;

namespace API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameServices(this IServiceCollection services)
    {
        services.AddSingleton<SettingsService>(sp =>
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

        return services;
    }
}
