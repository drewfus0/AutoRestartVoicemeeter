using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AutoRestartVoicemeeter.Core;
using AutoRestartVoicemeeter.Services;
using MediaColor = System.Windows.Media.Color;

namespace AutoRestartVoicemeeter.UI;

// ── Log window code-behind ─────────────────────────────────────────────────────
public partial class LogWindow : Window
{
    private readonly RestartService _restart;

    public LogWindow(RestartService restart)
    {
        _restart = restart;
        InitializeComponent();

        // Populate with history
        foreach (var e in Logger.Instance.Entries)
            AddLogToDocument(e);

        UpdateFooter();
        ScrollToEnd();

        // Subscribe to future entries
        Logger.Instance.EntryAdded += OnEntryAdded;

        UpdateStatusIndicator();
    }

    // ── Logger subscription ────────────────────────────────────────────────────
    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        // Logger already marshals to dispatcher; this runs on UI thread
        AddLogToDocument(entry);
        UpdateFooter();
        ScrollToEnd();
        UpdateStatusIndicator();
    }

    private void AddLogToDocument(LogEntry entry)
    {
        var p = new Paragraph();

        // Timestamp Run
        var timeStr = entry.Timestamp.ToString("HH:mm:ss");
        var timeRun = new Run(timeStr + "  ")
        {
            Foreground = (SolidColorBrush)FindResource("TextDim")
        };
        p.Inlines.Add(timeRun);

        // Message Run
        var colorStr = entry.Level switch
        {
            LogLevel.Success => "#4ADE80",  // green
            LogLevel.Warning => "#FBBF24",  // amber
            LogLevel.Error   => "#F87171",  // red-pink
            _                => "#94A3B8",  // slate (info)
        };
        var color = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
        var messageRun = new Run(entry.Message)
        {
            Foreground = new SolidColorBrush(color)
        };
        p.Inlines.Add(messageRun);

        LogDocument.Blocks.Add(p);
    }

    // ── Header status indicator ────────────────────────────────────────────────
    private void UpdateStatusIndicator()
    {
        // Reflect latest log severity in the dot colour
        bool hasError = false, hasWarn = false, hasOk = false;
        // Scan last 20 entries for recent state
        var recent = Logger.Instance.Entries;
        int start  = Math.Max(0, recent.Count - 20);
        for (int i = start; i < recent.Count; i++)
        {
            switch (recent[i].Level)
            {
                case LogLevel.Error:   hasError = true; break;
                case LogLevel.Warning: hasWarn  = true; break;
                case LogLevel.Success: hasOk    = true; break;
            }
        }

        if (hasError)
        {
            StatusDot.Fill   = new SolidColorBrush(MediaColor.FromRgb(0xF8, 0x71, 0x71));
            StatusLabel.Text = "Error";
        }
        else if (hasWarn)
        {
            StatusDot.Fill   = new SolidColorBrush(MediaColor.FromRgb(0xFB, 0xBF, 0x24));
            StatusLabel.Text = "Warning";
        }
        else if (hasOk)
        {
            StatusDot.Fill   = new SolidColorBrush(MediaColor.FromRgb(0x4A, 0xDE, 0x80));
            StatusLabel.Text = "Connected";
        }
        else
        {
            StatusDot.Fill   = new SolidColorBrush(MediaColor.FromRgb(0x60, 0xA5, 0xFA));
            StatusLabel.Text = "Starting…";
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private void UpdateFooter()
    {
        int count = Logger.Instance.Entries.Count;
        EntryCount.Text = $"{count} {(count == 1 ? "entry" : "entries")}";
    }

    private void ScrollToEnd()
        => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                                  () => LogBox.ScrollToEnd());

    // ── Button handlers ────────────────────────────────────────────────────────
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!LogBox.Selection.IsEmpty)
            {
                LogBox.Copy();
            }
            else
            {
                var textRange = new TextRange(LogDocument.ContentStart, LogDocument.ContentEnd);
                string text = textRange.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    System.Windows.Clipboard.SetText(text);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"⚠ Failed to copy to clipboard: {ex.Message}", LogLevel.Warning);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Entries.Clear();
        LogDocument.Blocks.Clear();
        UpdateFooter();
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
        => _restart.ManualRestart();

    // ── Close to tray (hide instead of destroy) ────────────────────────────────
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;   // Don't destroy the window
        Hide();            // Just hide it; the instance is reused
    }

    // ── Cleanup when application exits ────────────────────────────────────────
    protected override void OnClosed(EventArgs e)
    {
        Logger.Instance.EntryAdded -= OnEntryAdded;
        base.OnClosed(e);
    }
}
