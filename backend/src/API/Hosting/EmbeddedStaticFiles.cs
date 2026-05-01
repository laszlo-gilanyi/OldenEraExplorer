using Microsoft.Extensions.FileProviders;

namespace API.Hosting;

public static class EmbeddedStaticFilesExtensions
{
    public static void UseEmbeddedStaticFiles(this WebApplication app)
    {
        if (!HasEmbeddedFrontend())
        {
            Console.WriteLine("WARNING: Frontend not embedded. Build with node scripts/build-release.js for a complete release.");
            Console.WriteLine("         Running in API-only mode.");
            return;
        }

        var embeddedProvider = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embeddedProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedProvider });

        app.MapFallback(async context =>
        {
            var fileInfo = embeddedProvider.GetFileInfo("index.html");
            if (fileInfo.Exists)
            {
                context.Response.ContentType = "text/html";
                await using var stream = fileInfo.CreateReadStream();
                await stream.CopyToAsync(context.Response.Body);
            }
        });
    }

    private static bool HasEmbeddedFrontend()
    {
        var assembly = typeof(Program).Assembly;
        var manifestName = $"{assembly.GetName().Name}.wwwroot.manifest";
        return assembly.GetManifestResourceNames().Any(n => n.Contains("wwwroot"));
    }
}
