using API.Services;

namespace API.Hosting;

public class ConnectionTracker
{
    private int _count = 0;
    private CancellationTokenSource? _shutdownCts;
    private readonly object _lock = new();

    private readonly IHostApplicationLifetime _lifetime;
    private readonly SettingsService _settings;
    private readonly ILogger<ConnectionTracker> _logger;

    public ConnectionTracker(
        IHostApplicationLifetime lifetime,
        SettingsService settings,
        ILogger<ConnectionTracker> logger)
    {
        _lifetime = lifetime;
        _settings = settings;
        _logger = logger;
    }

    public void Increment()
    {
        lock (_lock)
        {
            _count++;
            _logger.LogInformation("Browser tab connected. Active connections: {Count}", _count);

            if (_shutdownCts != null)
            {
                _logger.LogInformation("Reconnection detected — cancelling scheduled shutdown.");
                _shutdownCts.Cancel();
                _shutdownCts.Dispose();
                _shutdownCts = null;
            }
        }
    }

    public void Decrement()
    {
        lock (_lock)
        {
            _count--;
            _logger.LogInformation("Browser tab disconnected. Active connections: {Count}", _count);

            if (_count <= 0 && !_settings.MinimizeToTray)
            {
                ScheduleShutdown();
            }
        }
    }

    private void ScheduleShutdown()
    {
        _shutdownCts?.Cancel();
        _shutdownCts?.Dispose();
        _shutdownCts = new CancellationTokenSource();
        var token = _shutdownCts.Token;

        _logger.LogInformation("All tabs closed and MinimizeToTray is off — shutting down in 10 seconds.");

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(10_000, token);
                _logger.LogInformation("Shutdown timer expired. Stopping application.");
                _lifetime.StopApplication();
            }
            catch (TaskCanceledException) { }
        });
    }
}
