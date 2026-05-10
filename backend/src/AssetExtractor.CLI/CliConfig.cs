using AssetExtractor.Models;

namespace AssetExtractor.CLI;

record CliConfig(
    string Command,
    string? GamePath,
    string? OutputPath,
    bool IsVerbose,
    bool Force,
    bool JsonProgress,
    List<string> Arguments
)
{
    public static CliConfig? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            return null;
        }

        string? gamePath = null;
        string? outputPath = null;
        bool isVerbose = false;
        bool force = false;
        bool jsonProgress = false;
        var filteredArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game-path" when i + 1 < args.Length:
                    gamePath = args[i + 1];
                    i++;
                    break;

                case "--output-path" when i + 1 < args.Length:
                    outputPath = args[i + 1];
                    i++;
                    break;

                case "--force":
                case "-f":
                    force = true;
                    break;

                case "--json-progress":
                    jsonProgress = true;
                    break;

                case "--verbose":
                    isVerbose = true;
                    break;

                case "--versioned":
                case "-v":
                    break;

                default:
                    filteredArgs.Add(args[i]);
                    break;
            }
        }

        string command = args[0].ToLower();

        return new CliConfig(
            Command: command,
            GamePath: gamePath,
            OutputPath: outputPath,
            IsVerbose: isVerbose,
            Force: force,
            JsonProgress: jsonProgress,
            Arguments: filteredArgs
        );
    }

    public static bool HasDeprecatedVersionedFlag(string[] args)
    {
        return args.Any(arg => arg == "--versioned" || arg == "-v");
    }
}
