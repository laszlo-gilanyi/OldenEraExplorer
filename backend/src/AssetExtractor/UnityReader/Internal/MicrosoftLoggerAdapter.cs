using System.Runtime.CompilerServices;
using AsLog = AssetStudio.Logger;
using AsEvent = AssetStudio.LoggerEvent;
using AsILogger = AssetStudio.ILogger;
using MsLog = Microsoft.Extensions.Logging;

namespace UnityReader.Internal;

// Installed once by a module initializer and never re-assigned, so concurrent extract
// workers cannot race with a Logger.Default rotation. Consumers must configure
// UnityScene.LoggerFactory before the first UnityScene.Load call.
internal sealed class MicrosoftLoggerAdapter : AsILogger
{
    private static readonly MicrosoftLoggerAdapter Instance = new();

    [ModuleInitializer]
    internal static void Install()
    {
        AsLog.Default = Instance;
    }

    public void Log(AsEvent loggerEvent, string message, bool ignoreLevel = false)
    {
        var target = UnityScene.LoggerFactory.CreateLogger("UnityReader.Vendor");
        var level = loggerEvent switch
        {
            AsEvent.Verbose => MsLog.LogLevel.Trace,
            AsEvent.Debug   => MsLog.LogLevel.Debug,
            AsEvent.Info    => MsLog.LogLevel.Information,
            AsEvent.Warning => MsLog.LogLevel.Warning,
            AsEvent.Error   => MsLog.LogLevel.Error,
            _               => MsLog.LogLevel.Information,
        };
        if (!target.IsEnabled(level)) return;
        MsLog.LoggerExtensions.Log(target, level, "{Message}", message);
    }
}
