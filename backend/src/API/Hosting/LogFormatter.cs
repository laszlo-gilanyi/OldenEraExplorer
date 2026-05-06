using API.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace API.Hosting;

public static class LoggingExtensions
{
    public static DiagnosticFileLogger ConfigureLogging(this WebApplicationBuilder builder, SettingsService settings)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsoleFormatter<CleanFormatter, SimpleConsoleFormatterOptions>(options =>
        {
            options.IncludeScopes = true;
        });
        builder.Logging.AddConsole(options => options.FormatterName = "clean");

        var diagnosticLogger = new DiagnosticFileLogger(settings);
        builder.Logging.AddProvider(diagnosticLogger);

        builder.Logging.SetMinimumLevel(
            builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Error);

        return diagnosticLogger;
    }
}

internal sealed class CleanFormatter : ConsoleFormatter
{
    private const string RESET = "\x1b[0m";
    private const string RED = "\x1b[91m";
    private const string YELLOW = "\x1b[93m";
    private const string MAGENTA = "\x1b[95m";
    private const string GREEN = "\x1b[92m";
    private const string CYAN = "\x1b[96m";
    private const string BLUE = "\x1b[94m";

    public CleanFormatter() : base("clean") { }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message == null) return;

        var scopes = new List<string>();
        scopeProvider?.ForEachScope((scope, state) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object>> scopeItems)
            {
                foreach (var item in scopeItems)
                {
                    scopes.Add($"{item.Key}: {item.Value}");
                }
            }
            else if (scope != null)
            {
                scopes.Add(scope.ToString() ?? "");
            }
        }, (object?)null);

        string levelPrefix = logEntry.LogLevel switch
        {
            LogLevel.Warning => $"{YELLOW}[WARNING]{RESET} ",
            LogLevel.Error => $"{RED}[ERROR]{RESET} ",
            LogLevel.Critical => $"{RED}[CRITICAL]{RESET} ",
            _ => ""
        };

        string coloredMessage = ColorizeMessage(message);

        if (scopes.Count > 0 && !string.IsNullOrEmpty(scopes[0]))
        {
            textWriter.WriteLine($"{levelPrefix}[{scopes[0]}] {coloredMessage}");
        }
        else
        {
            textWriter.WriteLine($"{levelPrefix}{coloredMessage}");
        }

        if (logEntry.Exception != null)
        {
            textWriter.WriteLine($"{RED}{logEntry.Exception}{RESET}");
        }
    }

    private static string ColorizeMessage(string message)
    {
        string lower = message.ToLowerInvariant();

        if (lower.Contains("reference") || lower.Contains("index"))
            return $"{MAGENTA}{message}{RESET}";

        if (lower.Contains("unit") || lower.Contains("spell") || lower.Contains("skill") ||
            lower.Contains("artifact") || lower.Contains("building") || lower.Contains("hero") ||
            lower.Contains("game data") || lower.Contains("abilit") || lower.Contains("subclass") ||
            lower.Contains("itemset") || lower.Contains("factionlaw") || lower.Contains("mapobject") ||
            lower.Contains("difficult") || lower.Contains("sidebuff"))
            return $"{GREEN}{message}{RESET}";

        if (lower.Contains("asset") || lower.Contains("serving") || lower.Contains("cache"))
            return $"{CYAN}{message}{RESET}";

        if (lower.Contains("extraction") || lower.Contains("extract") ||
            lower.Contains("glb") || lower.Contains("png"))
            return $"{BLUE}{message}{RESET}";

        return message;
    }
}
