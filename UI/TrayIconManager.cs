using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using AutoRestartVoicemeeter.Core;
using AutoRestartVoicemeeter.Services;

namespace AutoRestartVoicemeeter.UI;

public enum TrayState { Starting, Connected, Restarting, Error }

/// <summary>
/// Owns the <see cref="NotifyIcon"/> and its context menu.
/// Manages the single <see cref="LogWindow"/> instance (show-on-demand, hide-on-close).
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon      _tray;
    private readonly VoicemeeterApi  _api;
    private readonly RestartService  _restart;
    private readonly HotkeyService   _hotkeys;
    private readonly AppSettings     _settings;

    // Lazily created
    private LogWindow? _logWindow;
    private DeviceSelectionWindow? _deviceWindow;

    // Icons for each state (disposed on Dispose)
    private readonly Icon _iconStarting;
    private readonly Icon _iconConnected;
    private readonly Icon _iconRestarting;
    private readonly Icon _iconError;

    // Context menu items that need runtime updates
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _hotkeyItem;
    private ToolStripMenuItem? _startupItem;

    // ── Constructor ────────────────────────────────────────────────────────────
    public TrayIconManager(
        VoicemeeterApi  api,
        RestartService  restart,
        HotkeyService   hotkeys,
        AppSettings     settings)
    {
        _api      = api;
        _restart  = restart;
        _hotkeys  = hotkeys;
        _settings = settings;

        _iconStarting   = IconHelper.Starting;
        _iconConnected  = IconHelper.Connected;
        _iconRestarting = IconHelper.Restarting;
        _iconError      = IconHelper.Error;

        _tray = new NotifyIcon
        {
            Icon    = _iconStarting,
            Visible = true,
            Text    = "AutoRestart VoiceMeeter — Starting…",
        };

        _tray.DoubleClick += (_, _) => ShowLog();
        _tray.ContextMenuStrip = BuildMenu();

        // Reflect VoiceMeeter availability on startup
        SetState(_api.IsAvailable ? TrayState.Connected : TrayState.Error);
    }

    // ── Context menu ───────────────────────────────────────────────────────────
    private ContextMenuStrip BuildMenu()
    {
        // ── Status (non-clickable header) ──────────────────────────────────────
        _statusItem = new ToolStripMenuItem("● Starting…") { Enabled = false };
        _statusItem.Font = new System.Drawing.Font(_statusItem.Font, System.Drawing.FontStyle.Bold);

        // ── Show log ───────────────────────────────────────────────────────────
        var showLog = new ToolStripMenuItem("📋  Show Log");
        showLog.Click += (_, _) => ShowLog();

        // ── Device Selection ───────────────────────────────────────────────────
        var selectDevices = new ToolStripMenuItem("⚙  Select Target Devices...");
        selectDevices.Click += (_, _) => ShowDeviceSelection();

        // ── Manual restart ─────────────────────────────────────────────────────
        var restartItem = new ToolStripMenuItem("🔄  Restart VoiceMeeter Engine");
        restartItem.Click += (_, _) =>
        {
            SetState(TrayState.Restarting);
            _restart.ManualRestart();
        };

        // ── Volume key toggle ──────────────────────────────────────────────────
        _hotkeyItem = new ToolStripMenuItem("🎵  Volume Key → Bus A3")
        {
            Checked           = _hotkeys.IsEnabled,
            CheckOnClick      = true,
            CheckState        = _hotkeys.IsEnabled ? CheckState.Checked : CheckState.Unchecked,
        };
        _hotkeyItem.Click += (_, _) =>
            _hotkeys.IsEnabled = _hotkeyItem.Checked;

        // ── Run at startup toggle ──────────────────────────────────────────────
        _startupItem = new ToolStripMenuItem("🚀  Run at Startup")
        {
            Checked           = StartupService.IsEnabled,
            CheckOnClick      = true,
            CheckState        = StartupService.IsEnabled ? CheckState.Checked : CheckState.Unchecked,
        };
        _startupItem.Click += (_, _) =>
        {
            StartupService.IsEnabled = _startupItem.Checked;
            Logger.Instance.Log(
                $"Run at startup: {(_startupItem.Checked ? "enabled" : "disabled")}",
                LogLevel.Info);
        };

        // ── Exit ───────────────────────────────────────────────────────────────
        var exitItem = new ToolStripMenuItem("✖  Exit");
        exitItem.Click += (_, _) =>
        {
            _tray.Visible = false; // Hide immediately before shutdown
            System.Windows.Application.Current.Shutdown();
        };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            new ToolStripSeparator(),
            showLog,
            selectDevices,
            restartItem,
            new ToolStripSeparator(),
            _hotkeyItem,
            _startupItem,
            new ToolStripSeparator(),
            exitItem,
        });

        return menu;
    }

    // ── State / icon management ────────────────────────────────────────────────
    public void SetState(TrayState state)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var (icon, tipText, statusText) = state switch
            {
                TrayState.Connected  => (_iconConnected,  "AutoRestart VoiceMeeter — Connected",    "● VoiceMeeter Connected"),
                TrayState.Restarting => (_iconRestarting, "AutoRestart VoiceMeeter — Restarting…",  "⟳ Restarting Engine…"),
                TrayState.Error      => (_iconError,      "AutoRestart VoiceMeeter — Error",         "✗ VoiceMeeter Error"),
                _                    => (_iconStarting,   "AutoRestart VoiceMeeter — Starting…",    "● Starting…"),
            };

            _tray.Icon = icon;
            _tray.Text = tipText.Length > 63 ? tipText[..63] : tipText;
            if (_statusItem is not null) _statusItem.Text = statusText;
        });
    }

    // ── Log window ─────────────────────────────────────────────────────────────
    private void ShowLog()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_logWindow is null || !_logWindow.IsLoaded)
                _logWindow = new LogWindow(_restart);

            _logWindow.Show();
            _logWindow.Activate();
        });
    }

    // ── Device Selection Window ────────────────────────────────────────────────
    private void ShowDeviceSelection()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_deviceWindow is null || !_deviceWindow.IsLoaded)
                _deviceWindow = new DeviceSelectionWindow(_settings);

            _deviceWindow.Show();
            _deviceWindow.Activate();
        });
    }

    // ── IDisposable ────────────────────────────────────────────────────────────
    public void Dispose()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _logWindow?.Close();
            _deviceWindow?.Close();
            _tray.Visible = false;
            _tray.Dispose();
        });

        _iconStarting.Dispose();
        _iconConnected.Dispose();
        _iconRestarting.Dispose();
        _iconError.Dispose();
    }
}
