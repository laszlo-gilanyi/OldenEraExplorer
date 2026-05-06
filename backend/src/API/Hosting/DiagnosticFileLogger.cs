using System.Runtime.InteropServices;
using API.Services;
using Microsoft.Extensions.Logging;

namespace API.Hosting;

/// <summary>
/// File-based diagnostic logger with two modes:
///  - Default (verbose=false): in-memory circular buffer; flushes to "logs/crash-{ts}.log"
///    on the first Error/Critical entry, then writes real-time. No file when nothing fails.
///  - Verbose (verbose=true): opens "logs/verbose-{ts}.log" immediately on construction
///    and writes every entry from then on.
/// Both modes write a fixed system-context header at the top of the file.
/// SetVerbose() switches modes at runtime.
/// </summary>
public sealed class DiagnosticFileLogger : ILoggerProvider
{
    private readonly CircularBuffer<LogEntry> _buffer = new(1000);
    private readonly object _lock = new();
    private readonly SettingsService _settings;
    private readonly Func<string?> _gamePathProvider;

    private bool _verbose;
    private bool _fileOpen;
    private string? _logPath;
    private StreamWriter? _fileWriter;

    public DiagnosticFileLogger(SettingsService settings, Func<string?>? gamePathProvider = null)
    {
        _settings = settings;
        _gamePathProvider = gamePathProvider ?? (static () => null);
        _verbose = settings.VerboseLogging;

        if (_verbose)
        {
            OpenFile(verbose: true);
        }
    }

    public ILogger CreateLogger(string categoryName) => new BufferedLogger(this, categoryName);

    public void SetVerbose(bool enabled)
    {
        lock (_lock)
        {
            if (enabled == _verbose) return;
            _verbose = enabled;

            if (enabled)
            {
                if (!_fileOpen)
                {
                    OpenFile(verbose: true);
                    foreach (var entry in _buffer.GetAll())
                    {
                        WriteToFile(entry);
                    }
                }
                else
                {
                    _fileWriter?.WriteLine();
                    _fileWriter?.WriteLine($"=== Verbose logging re-enabled at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    _fileWriter?.WriteLine();
                }
            }
            else
            {
                // Verbose off: keep the verbose file on disk for the user. A subsequent Error event
                // will open a fresh crash file. We only close handles when the current file is verbose-*.
                if (_fileOpen && _logPath != null && _logPath.Contains("verbose-"))
                {
                    try
                    {
                        _fileWriter?.WriteLine();
                        _fileWriter?.WriteLine($"=== Verbose logging disabled at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    }
                    catch { }
                    _fileWriter?.Dispose();
                    _fileWriter = null;
                    _fileOpen = false;
                }
            }
        }
    }

    /// <summary>
    /// Set the game path provider after registration. Allows the header to include the
    /// configured game path even when the logger is constructed before the path service.
    /// </summary>
    public void AttachGamePathProvider(Func<string?> provider)
    {
        // Stored as a field assignment under the lock — header for already-open files
        // does not retroactively change, but next file open (e.g. crash flush) will use it.
        lock (_lock)
        {
            // Use reflection-free reassignment via boxing into a closure-friendly holder
            _gamePathHolder = provider;
        }
    }

    private Func<string?>? _gamePathHolder;

    internal void Log(LogLevel level, string category, string message, Exception? exception)
    {
        var entry = new LogEntry(DateTime.Now, level, category, message, exception);

        lock (_lock)
        {
            _buffer.Add(entry);

            if (!_fileOpen && level >= LogLevel.Error)
            {
                OpenFile(verbose: false);
                foreach (var buffered in _buffer.GetAll())
                {
                    WriteToFile(buffered);
                }
                return;
            }

            if (_fileOpen)
            {
                WriteToFile(entry);
            }
        }
    }

    private void OpenFile(bool verbose)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var prefix = verbose ? "verbose" : "crash";
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", $"{prefix}-{timestamp}.log");

            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            _fileWriter = new StreamWriter(_logPath, append: true) { AutoFlush = true };

            WriteHeader(verbose);
            _fileOpen = true;
        }
        catch (Exception ex)
        {
            // Logger must never throw out — file system permission failures must not crash the app.
            Console.WriteLine($"Failed to open diagnostic log file: {ex.Message}");
            _fileWriter = null;
            _fileOpen = false;
        }
    }

    private void WriteHeader(bool verbose)
    {
        if (_fileWriter == null) return;

        try
        {
            _fileWriter.WriteLine("=== OldenEraExplorer diagnostic log ===");
            _fileWriter.WriteLine($"Mode:             {(verbose ? "verbose (started at app launch)" : "error-triggered (buffer flush)")}");
            _fileWriter.WriteLine($"Timestamp:        {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _fileWriter.WriteLine($"App version:      {SafeGet(() => UpdateService.CurrentVersion)}");
            _fileWriter.WriteLine($".NET runtime:     {RuntimeInformation.FrameworkDescription}");
            _fileWriter.WriteLine($"OS:               {RuntimeInformation.OSDescription}");
            _fileWriter.WriteLine($"Architecture:     {RuntimeInformation.OSArchitecture} / process {RuntimeInformation.ProcessArchitecture}");
            _fileWriter.WriteLine($"Process path:     {SafeGet(() => Environment.ProcessPath ?? "unknown")}");
            _fileWriter.WriteLine($"Working dir:      {SafeGet(() => Environment.CurrentDirectory)}");
            _fileWriter.WriteLine($"Base directory:   {AppContext.BaseDirectory}");
            _fileWriter.WriteLine($"Process args:     {SafeGet(() => string.Join(" ", Environment.GetCommandLineArgs()))}");
            _fileWriter.WriteLine($"Free disk:        {SafeGet(() => GetFreeDiskInfo(AppContext.BaseDirectory))}");
            _fileWriter.WriteLine("Settings snapshot:");
            _fileWriter.WriteLine($"  GamePath:               {SafeGet(_gamePathHolder ?? _gamePathProvider) ?? "(not set)"}");
            _fileWriter.WriteLine($"  Locale:                 {_settings.LastLocale}");
            _fileWriter.WriteLine($"  AutoExtractEnabled:     {_settings.AutoExtractEnabled}");
            _fileWriter.WriteLine($"  ExtractPng:             {_settings.ExtractPng}");
            _fileWriter.WriteLine($"  ExtractGlb:             {_settings.ExtractGlb}");
            _fileWriter.WriteLine($"  MinimizeToTray:         {_settings.MinimizeToTray}");
            _fileWriter.WriteLine($"  AutoUpdateEnabled:      {_settings.AutoUpdateEnabled}");
            _fileWriter.WriteLine($"  VerboseLogging:         {_settings.VerboseLogging}");
            _fileWriter.WriteLine("=== begin log ===");
            _fileWriter.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write log header: {ex.Message}");
        }
    }

    private static string SafeGet(Func<string?> getter)
    {
        try { return getter() ?? "unknown"; }
        catch { return "unknown"; }
    }

    private static string GetFreeDiskInfo(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);

            DriveInfo? best = null;
            var bestLen = -1;
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var name = drive.Name;
                if (fullPath.StartsWith(name, StringComparison.OrdinalIgnoreCase) && name.Length > bestLen)
                {
                    best = drive;
                    bestLen = name.Length;
                }
            }

            if (best == null)
            {
                // Linux fallback when no mountpoint prefix matched (rare): use root.
                best = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name == "/");
            }

            if (best == null) return "unknown";

            var freeGb = best.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            var totalGb = best.TotalSize / (1024.0 * 1024.0 * 1024.0);
            return $"{freeGb:F2} GB free of {totalGb:F2} GB on {best.Name}";
        }
        catch
        {
            return "unknown";
        }
    }

    private void WriteToFile(LogEntry entry)
    {
        if (_fileWriter == null) return;

        var levelStr = entry.Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        try
        {
            _fileWriter.WriteLine($"[{entry.Timestamp:HH:mm:ss} {levelStr}] [{entry.Category}] {entry.Message}");

            if (entry.Exception != null)
            {
                _fileWriter.WriteLine(entry.Exception.ToString());
                _fileWriter.WriteLine();
            }
        }
        catch
        {
            // Disk full / handle dead — drop silently rather than tearing down the host.
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _fileWriter?.Dispose();
            _fileWriter = null;
            _fileOpen = false;
        }
    }

    private sealed class BufferedLogger : ILogger
    {
        private readonly DiagnosticFileLogger _provider;
        private readonly string _categoryName;

        public BufferedLogger(DiagnosticFileLogger provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter == null) return;

            var message = formatter(state, exception);
            _provider.Log(logLevel, _categoryName, message, exception);
        }
    }

    private record LogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message, Exception? Exception);
}

internal sealed class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _index = 0;
    public int Count { get; private set; }

    public CircularBuffer(int capacity)
    {
        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        _buffer[_index] = item;
        _index = (_index + 1) % _buffer.Length;
        if (Count < _buffer.Length) Count++;
    }

    public IEnumerable<T> GetAll()
    {
        var start = Count < _buffer.Length ? 0 : _index;
        for (int i = 0; i < Count; i++)
        {
            yield return _buffer[(start + i) % _buffer.Length];
        }
    }
}
