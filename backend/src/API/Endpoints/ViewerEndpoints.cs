using API.Contracts;
using API.Services;

namespace API.Endpoints;

public static class ViewerEndpoints
{
    public static IEndpointRouteBuilder MapViewerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/viewer")
            .WithTags("Viewer");

        group.MapGet("/platform", GetPlatformGlb)
            .WithName("GetPlatformGlb")
            .WithSummary("Get the platform GLB file for Game Preview mode")
            .Produces(200, contentType: "model/gltf-binary")
            .Produces<ErrorDto>(404);

        group.MapGet("/background", GetBackground)
            .WithName("GetViewerBackground")
            .WithSummary("Get the background texture for Game Preview mode")
            .Produces(200, contentType: "image/png")
            .Produces<ErrorDto>(404);

        group.MapGet("/environment", GetEnvironment)
            .WithName("GetViewerEnvironment")
            .WithSummary("Get the equirectangular environment map for IBL lighting")
            .Produces(200, contentType: "image/png")
            .Produces<ErrorDto>(404);

        group.MapGet("/sky/{faction}", GetFactionSky)
            .WithName("GetFactionSky")
            .WithSummary("Get the faction-specific sky panorama for Game Preview background")
            .Produces(200, contentType: "image/png")
            .Produces<ErrorDto>(404);

        return endpoints;
    }

    private static IResult GetPlatformGlb(IAssetServingService assetService)
    {
        var glbPath = ResolvePlatformGlbPath(assetService.ExtractedAssetsDirectory);

        if (glbPath == null || !File.Exists(glbPath))
        {
            return Results.NotFound(new ErrorDto("Platform model not found"));
        }

        var fileBytes = File.ReadAllBytes(glbPath);
        return Results.File(fileBytes, "model/gltf-binary", "platform.glb");
    }

    private static IResult GetBackground(IAssetServingService assetService)
    {
        // EA renamed the unit hire background; fall back to the pre-EA name
        var path = ResolveAssetPath(assetService.ExtractedAssetsDirectory, "Assets", "Texture2D", "City_Background_Unithire 3.png")
                ?? ResolveAssetPath(assetService.ExtractedAssetsDirectory, "Assets", "Texture2D", "unit_info_back.png");

        if (path == null || !File.Exists(path))
        {
            return Results.NotFound(new ErrorDto("Background texture not found. Run extract-textures in unity-asset-to-glb."));
        }

        var fileBytes = File.ReadAllBytes(path);
        return Results.File(fileBytes, "image/png", Path.GetFileName(path));
    }

    private static IResult GetEnvironment(IAssetServingService assetService)
    {
        var path = ResolveAssetPath(assetService.ExtractedAssetsDirectory, "Assets", "Cubemap", "Cold Sunset Equirect.png");

        if (path == null || !File.Exists(path))
        {
            return Results.NotFound(new ErrorDto("Environment map not found. Run extract-textures in unity-asset-to-glb."));
        }

        var fileBytes = File.ReadAllBytes(path);
        return Results.File(fileBytes, "image/png", "Cold Sunset Equirect.png");
    }

    private static IResult GetFactionSky(string faction, IAssetServingService assetService)
    {
        var skyFileName = faction.ToLowerInvariant() switch
        {
            "human"    => "city_human_sky2.png",
            "demon"    => "demon_sky_texture.png",
            "dungeon"  => "dungeon_sky_texture.png",
            "nature"   => "city_nature_sky.png",
            "undead"   => "necro_sky_texture2.png",
            "unfrozen" => "unfrozen_sky_texture.png",
            _          => "Grass_sky_texture.png",
        };

        var path = ResolveAssetPath(assetService.ExtractedAssetsDirectory, "Assets", "Texture2D", skyFileName);

        if (path == null || !File.Exists(path))
            return Results.NotFound(new ErrorDto($"Sky texture for faction '{faction}' not found. Run extraction first."));

        var fileBytes = File.ReadAllBytes(path);
        return Results.File(fileBytes, "image/png", skyFileName);
    }

    private static string? ResolvePlatformGlbPath(string extractedDir)
    {
        return ResolveAssetPath(extractedDir, "Assets", "GameObject", "PLATFORM + BACK.glb");
    }

    private static string? ResolveAssetPath(string extractedDir, params string[] pathParts)
    {
        if (!Directory.Exists(extractedDir))
            return null;

        // Search in all version directories (newest first based on directory name)
        var versionDirs = Directory.GetDirectories(extractedDir, "Assets-*")
            .OrderByDescending(d => d);

        foreach (var versionDir in versionDirs)
        {
            var assetPath = Path.Combine(new[] { versionDir }.Concat(pathParts).ToArray());
            if (File.Exists(assetPath))
                return assetPath;
        }

        return null;
    }
}
