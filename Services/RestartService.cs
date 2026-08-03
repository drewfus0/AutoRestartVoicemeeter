using AutoRestartVoicemeeter.Core;

namespace AutoRestartVoicemeeter.Services;

/// <summary>
/// Orchestrates the debounced VoiceMeeter audio engine restart.
///
/// Flow:
/// 1. <see cref="OnQudelixArrived"/> receives device-arrival events.
/// 2. A 3-second debounce timer is (re-)armed on each event.
/// 3. After the debounce expires, a 1.5-second driver-settle delay runs.
/// 4. <see cref="VoicemeeterApi.RestartEngine"/> is called.
///
/// <see cref="ManualRestart"/> bypasses the debounce but still observes the settle delay.
/// </summary>
public sealed class RestartService : IDisposable
{
    private const int DebounceMs    = 200;
    private const int DriverSettleMs = 100;

    private readonly VoicemeeterApi _api;
    private System.Threading.Timer? _debounce;
    private readonly object _timerLock = new();

    // ── Events ─────────────────────────────────────────────────────────────────
    public event Action?         RestartCompleted;
    public event Action<string>? RestartFailed;

    public RestartService(VoicemeeterApi api) => _api = api;

    // ── Public interface ───────────────────────────────────────────────────────

    /// <summary>
    /// Event handler for <c>DeviceWatcher.QudelixArrived</c> and
    /// <c>AudioDeviceMonitor.QudelixArrived</c>. Arms/resets the debounce timer.
    /// </summary>
    public void OnQudelixArrived(object? sender, EventArgs e)
    {
        Logger.Instance.Log(
            $"⏱ Qudelix arrival — arming {DebounceMs / 1000}s debounce…", LogLevel.Info);

        lock (_timerLock)
        {
            _debounce?.Dispose();
            _debounce = new System.Threading.Timer(
                _ => _ = ExecuteRestartAsync("auto (device arrival)"),
                null,
                DebounceMs,
                System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>Triggers an immediate restart (skips debounce, keeps settle delay).</summary>
    public void ManualRestart()
    {
        Logger.Instance.Log("🔄 Manual restart requested.", LogLevel.Info);
        _ = ExecuteRestartAsync("manual");
    }

    // ── Core restart logic ─────────────────────────────────────────────────────

    private async Task ExecuteRestartAsync(string trigger)
    {
        try
        {
            Logger.Instance.Log(
                $"⏳ Waiting {DriverSettleMs}ms for driver initialisation…", LogLevel.Info);
            await Task.Delay(DriverSettleMs).ConfigureAwait(false);

            Logger.Instance.Log(
                $"🔄 Sending restart command to VoiceMeeter (trigger: {trigger})…", LogLevel.Warning);

            bool ok = _api.RestartEngine();

            if (ok)
            {
                Logger.Instance.Log("✓ VoiceMeeter engine restart sent.", LogLevel.Success);
                RestartCompleted?.Invoke();
            }
            else
            {
                const string msg = "Restart failed — is VoiceMeeter running?";
                Logger.Instance.Log($"✗ {msg}", LogLevel.Error);
                RestartFailed?.Invoke(msg);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"✗ Restart exception details:\n{ex}", LogLevel.Error);
            RestartFailed?.Invoke(ex.Message);
        }
    }

    // ── IDisposable ────────────────────────────────────────────────────────────
    public void Dispose()
    {
        lock (_timerLock)
        {
            _debounce?.Dispose();
            _debounce = null;
        }
    }
}
