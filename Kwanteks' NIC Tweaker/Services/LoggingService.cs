using System.Collections.ObjectModel;

namespace Kwanteks.NicTweaker.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly object _gate = new();
    private readonly ObservableCollection<string> _entries = new();
    private readonly string _logFile;
    private readonly string _appLogFile;

    public event EventHandler<string>? EntryAdded;
    public IReadOnlyList<string> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    public LoggingService(IStorageService storage)
    {
        _logFile = Path.Combine(storage.LogsDirectory, $"log-{DateTime.Now:yyyyMMdd}.txt");
        _appLogFile = AppPaths.AppLogFile;
    }

    public void Info(string message) => Add("INFO", message);
    public void Warn(string message) => Add("WARN", message);
    public void Error(string message) => Add("ERROR", message);

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    private void Add(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        lock (_gate)
        {
            _entries.Add(line);
            try { File.AppendAllText(_logFile, line + Environment.NewLine); } catch { /* ignore */ }
            try { File.AppendAllText(_appLogFile, line + Environment.NewLine); } catch { /* ignore */ }
        }
        EntryAdded?.Invoke(this, line);
    }
}
