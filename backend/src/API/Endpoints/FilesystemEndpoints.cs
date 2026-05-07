using System.Runtime.InteropServices;
using API.Contracts;

namespace API.Endpoints;

public static class FilesystemEndpoints
{
    public static IEndpointRouteBuilder MapFilesystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/filesystem")
            .WithTags("Filesystem");

        group.MapGet("/roots", GetRoots)
            .WithName("GetFilesystemRoots")
            .WithSummary("Get filesystem root locations")
            .WithDescription("Returns available drives/mount points for the folder picker sidebar.")
            .Produces<FilesystemRootsDto>(200);

        group.MapGet("/list", ListDirectory)
            .WithName("ListDirectory")
            .WithSummary("List directory contents")
            .WithDescription("Returns folders in the specified directory for navigation.")
            .Produces<DirectoryListingDto>(200)
            .Produces<ErrorDto>(400);

        return endpoints;
    }

    private static IResult GetRoots()
    {
        var roots = new List<FilesystemRootDto>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    var label = string.IsNullOrEmpty(drive.VolumeLabel)
                        ? drive.Name.TrimEnd('\\')
                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
                    roots.Add(new FilesystemRootDto(
                        Id: drive.Name,
                        Name: label,
                        Icon: "drive"
                    ));
                }
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            roots.Add(new FilesystemRootDto(Id: home, Name: "Home", Icon: "home"));
            roots.Add(new FilesystemRootDto(Id: "/", Name: "Root", Icon: "drive"));

            if (Directory.Exists("/mnt"))
            {
                foreach (var mnt in Directory.GetDirectories("/mnt"))
                {
                    var name = Path.GetFileName(mnt);
                    if (name.Length == 1 && char.IsLetter(name[0]))
                    {
                        roots.Add(new FilesystemRootDto(
                            Id: mnt,
                            Name: $"{name.ToUpper()}:",
                            Icon: "drive"
                        ));
                    }
                }
            }

            if (Directory.Exists("/home"))
                roots.Add(new FilesystemRootDto(Id: "/home", Name: "Users", Icon: "folder"));
        }

        return Results.Ok(new FilesystemRootsDto(roots));
    }

    private static IResult ListDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        path = Path.GetFullPath(path);

        if (!Directory.Exists(path))
        {
            return Results.BadRequest(new ErrorDto("Directory not found", $"The path '{path}' does not exist."));
        }

        var entries = new List<DirectoryEntryDto>();

        try
        {
            // On Linux, .NET maps any dot-prefixed name to FileAttributes.Hidden by convention,
            // but those are normal locations the user often needs (e.g. ~/.steam, ~/.local/share/Steam,
            // ~/.var/app for Flatpak). Only filter Hidden on Windows, where the attribute is an
            // explicit user/OS marker rather than a naming convention.
            bool isWindows = OperatingSystem.IsWindows();

            foreach (var dir in Directory.GetDirectories(path))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if ((info.Attributes & FileAttributes.System) != 0)
                        continue;
                    if (isWindows && (info.Attributes & FileAttributes.Hidden) != 0)
                        continue;

                    entries.Add(new DirectoryEntryDto(
                        Id: dir,
                        Name: info.Name,
                        Type: "folder",
                        Date: info.LastWriteTime
                    ));
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorDto("Access denied", $"Cannot read directory '{path}'."));
        }

        entries = entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var parent = Directory.GetParent(path)?.FullName;

        return Results.Ok(new DirectoryListingDto(
            Current: path,
            Parent: parent,
            Entries: entries
        ));
    }
}

// DTOs

public record FilesystemRootsDto(IReadOnlyList<FilesystemRootDto> Roots);

public record FilesystemRootDto(string Id, string Name, string Icon);

public record DirectoryListingDto(
    string Current,
    string? Parent,
    IReadOnlyList<DirectoryEntryDto> Entries
);

public record DirectoryEntryDto(
    string Id,
    string Name,
    string Type,
    DateTime Date
);
