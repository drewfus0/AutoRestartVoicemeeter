using System.Threading;
using AutoRestartVoicemeeter.Core;
using AutoRestartVoicemeeter.Services;
using AutoRestartVoicemeeter.UI;

namespace AutoRestartVoicemeeter;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;

    private AppSettings?        _settings;
    private VoicemeeterApi?     _api;
    private DeviceWatcher?      _deviceWatcher;
    private AudioDeviceMonitor? _audioMonitor;
    private RestartService?     _restartService;
    private HotkeyService?      _hotkeyService;
    private TrayIconManager?    _trayManager;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Single-instance guard ───────────────────────────────────────────
        _singleInstanceMutex = new Mutex(true, @"Global\AutoRestartVoicemeeter_v1", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show(
                "AutoRestart VoiceMeeter is already running.\nLook for the icon in your system tray.",
                "Already Running",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Logger.Instance.Log("AutoRestart VoiceMeeter v1.0 starting…", LogLevel.Info);

        // ── Load Settings ───────────────────────────────────────────────────
        _settings       = AppSettings.Load();
        _api            = new VoicemeeterApi();
        _restartService = new RestartService(_api);
        _deviceWatcher  = new DeviceWatcher(_settings);
        _audioMonitor   = new AudioDeviceMonitor(_settings);
        _hotkeyService  = new HotkeyService(_api, _settings);
        _trayManager    = new TrayIconManager(_api, _restartService, _hotkeyService, _settings);

        // ── Wire device-detection → restart service ─────────────────────────
        _deviceWatcher.QudelixArrived += _restartService.OnQudelixArrived;
        _audioMonitor.QudelixArrived  += _restartService.OnQudelixArrived;

        // ── Wire restart outcomes → tray icon ───────────────────────────────
        _restartService.RestartCompleted += () => _trayManager.SetState(TrayState.Connected);
        _restartService.RestartFailed    += _  => _trayManager.SetState(TrayState.Error);

        // ── Start monitoring ────────────────────────────────────────────────
        _deviceWatcher.Start();
        _audioMonitor.Start();

        Logger.Instance.Log("✓ Monitoring target devices (USB + Bluetooth audio endpoints)…", LogLevel.Success);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Logger.Instance.Log("Shutting down…", LogLevel.Info);

        _hotkeyService?.Dispose();
        _audioMonitor?.Dispose();
        _deviceWatcher?.Dispose();
        _trayManager?.Dispose();
        _api?.Dispose();

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
