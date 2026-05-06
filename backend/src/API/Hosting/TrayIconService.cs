using API.Services;
using NotificationIcon.NET;

namespace API.Hosting;

public class TrayIconService : IDisposable
{
    private NotifyIcon? _trayIcon;
    private string? _tempIconPath;
    private string? _url;

    private readonly SettingsService _settings;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TrayIconService> _logger;

    public TrayIconService(
        SettingsService settings,
        IHostApplicationLifetime lifetime,
        ILogger<TrayIconService> logger)
    {
        _settings = settings;
        _lifetime = lifetime;
        _logger = logger;
    }

    public void Initialize(string url)
    {
        _url = url;

        if (_settings.MinimizeToTray)
        {
            ShowIcon();
        }
    }

    public void ShowIcon()
    {
        if (_url == null)
        {
            _logger.LogWarning("Cannot show tray icon: TrayIconService not initialized.");
            return;
        }

        if (_trayIcon != null) return;

        Task.Run(() =>
        {
            try
            {
                var assembly = typeof(TrayIconService).Assembly;
                var iconName = OperatingSystem.IsWindows() ? "icon.ico" : "icon.png";
                using var stream = assembly.GetManifestResourceStream(iconName);

                if (stream == null)
                {
                    _logger.LogError("Tray icon embedded resource '{IconName}' not found.", iconName);
                    return;
                }

                _tempIconPath = Path.Combine(Path.GetTempPath(), $"olden-era-explorer-{iconName}");
                using (var file = File.Create(_tempIconPath))
                {
                    stream.CopyTo(file);
                }

                _trayIcon = NotifyIcon.Create(_tempIconPath, BuildMenuItems());
                _trayIcon.Show();
                _logger.LogInformation("System tray icon active.");
            }
            catch (PlatformNotSupportedException)
            {
                _logger.LogWarning("System tray not available on this platform. Use Ctrl+C to exit.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tray icon.");
            }
        });
    }

    public void HideIcon()
    {
        if (_trayIcon == null) return;

        try { _trayIcon.Dispose(); } catch { }
        _trayIcon = null;

        if (_tempIconPath != null && File.Exists(_tempIconPath))
        {
            try { File.Delete(_tempIconPath); } catch { }
            _tempIconPath = null;
        }
    }

    private List<MenuItem> BuildMenuItems()
    {
        return
        [
            new("Olden Era Explorer") { IsDisabled = true },
            new("-"),
            new("Open Browser") { Click = (_, _) => BrowserLauncher.Open(_url!) },
            new("-"),
            new("Exit") { Click = (_, _) => _lifetime.StopApplication() },
        ];
    }

    public void Dispose() => HideIcon();
}
