using System.Collections.ObjectModel;

namespace AutoRestartVoicemeeter.Core;

// ── Log level ──────────────────────────────────────────────────────────────────
public enum LogLevel { Info, Success, Warning, Error }

// ── Immutable log entry ────────────────────────────────────────────────────────
public sealed record LogEntry(DateTime Timestamp, string Message, LogLevel Level);

// ── Singleton logger ───────────────────────────────────────────────────────────
/// <summary>
/// Thread-safe, UI-dispatcher-aware logger. All components write here;
/// the LogWindow binds directly to <see cref="Entries"/>.
/// </summary>
public sealed class Logger
{
    // ── Singleton ──────────────────────────────────────────────────────────────
    private static readonly Logger _instance = new();
    public static Logger Instance => _instance;

    // ── Storage ────────────────────────────────────────────────────────────────
    private readonly ObservableCollection<LogEntry> _entries = [];
    public ObservableCollection<LogEntry> Entries => _entries;

    // ── Events ─────────────────────────────────────────────────────────────────
    /// <summary>Raised on the UI thread after each new entry is appended.</summary>
    public event EventHandler<LogEntry>? EntryAdded;

    private Logger() { }

    // ── Public API ─────────────────────────────────────────────────────────────
    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(DateTime.Now, message, level);

        // Always append on the UI dispatcher (ObservableCollection notifies UI)
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            AppendCore(entry);
            return;
        }

        if (app.Dispatcher.CheckAccess())
            AppendCore(entry);
        else
            app.Dispatcher.BeginInvoke(() => AppendCore(entry));

        System.Diagnostics.Debug.WriteLine($"[{level}] {entry.Timestamp:HH:mm:ss}  {message}");
    }

    private void AppendCore(LogEntry entry)
    {
        _entries.Add(entry);
        EntryAdded?.Invoke(this, entry);
    }
}
