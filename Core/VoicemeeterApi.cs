using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoRestartVoicemeeter.Core;

/// <summary>
/// Wraps VoicemeeterRemote64.dll via NativeLibrary dynamic loading.
/// Thread-safe; all public methods can be called from any thread.
/// </summary>
public sealed class VoicemeeterApi : IDisposable
{
    // ── Delegate types (must match the DLL's calling convention) ───────────────
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VBVMR_Login_t();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VBVMR_Logout_t();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VBVMR_SetParameterFloat_t(
        [MarshalAs(UnmanagedType.LPStr)] string paramName, float value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VBVMR_GetParameterFloat_t(
        [MarshalAs(UnmanagedType.LPStr)] string paramName, out float value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VBVMR_IsParametersDirty_t();

    // ── Loaded function pointers ───────────────────────────────────────────────
    private VBVMR_Login_t?               _login;
    private VBVMR_Logout_t?              _logout;
    private VBVMR_SetParameterFloat_t?   _setFloat;
    private VBVMR_GetParameterFloat_t?   _getFloat;
    private VBVMR_IsParametersDirty_t?   _isDirty;

    // ── State ──────────────────────────────────────────────────────────────────
    private IntPtr _dllHandle  = IntPtr.Zero;
    private bool   _loggedIn   = false;
    private readonly object _lock = new();

    // Local cache for Bus A3 gain.
    // VBVMR_GetParameterFloat returns a stale value immediately after a SET
    // because the VoiceMeeter engine processes writes asynchronously.
    // We seed this once from the API on first use, then maintain it ourselves.
    private float? _busA3GainCache;

    public bool    IsAvailable  => _dllHandle != IntPtr.Zero;
    public string? DllPath      { get; private set; }
    public string  StatusMessage { get; private set; } = "Not initialised";

    // ── Constructor ────────────────────────────────────────────────────────────
    public VoicemeeterApi() => Initialise();

    // ── Initialisation ─────────────────────────────────────────────────────────
    private void Initialise()
    {
        var path = FindDllPath();
        if (path is null)
        {
            StatusMessage = "DLL not found – install VoiceMeeter";
            Log("⚠ VoicemeeterRemote64.dll not found. Please install VoiceMeeter.", LogLevel.Warning);
            return;
        }

        DllPath = path;
        Log($"Found DLL: {path}", LogLevel.Info);

        try
        {
            _dllHandle = NativeLibrary.Load(path);
            BindExports();
            Login();
        }
        catch (Exception ex)
        {
            StatusMessage = $"DLL load error: {ex.Message}";
            Log($"✗ DLL load failed details:\n{ex}", LogLevel.Error);
        }
    }

    private void BindExports()
    {
        _login    = GetExport<VBVMR_Login_t>("VBVMR_Login");
        _logout   = GetExport<VBVMR_Logout_t>("VBVMR_Logout");
        _setFloat = GetExport<VBVMR_SetParameterFloat_t>("VBVMR_SetParameterFloat");
        _getFloat = GetExport<VBVMR_GetParameterFloat_t>("VBVMR_GetParameterFloat");
        _isDirty  = GetExport<VBVMR_IsParametersDirty_t>("VBVMR_IsParametersDirty");
    }

    private T GetExport<T>(string name) where T : Delegate
    {
        var ptr = NativeLibrary.GetExport(_dllHandle, name);
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    // ── DLL Discovery ──────────────────────────────────────────────────────────
    private static string? FindDllPath()
    {
        const string dll = "VoicemeeterRemote64.dll";

        var candidates = new[]
        {
            // Registry: HKLM\SOFTWARE\WOW6432Node\VB Audio\Voicemeeter  →  InDir
            RegistryDir(@"SOFTWARE\WOW6432Node\VB Audio\Voicemeeter",  "InDir"),
            RegistryDir(@"SOFTWARE\VB Audio\Voicemeeter",              "InDir"),
            // Uninstall entry
            UninstallDir(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter"),
            UninstallDir(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter"),
            // Hardcoded fallback
            @"C:\Program Files (x86)\VB\Voicemeeter",
            @"C:\Program Files\VB\Voicemeeter",
        };

        foreach (var dir in candidates)
        {
            if (dir is null) continue;
            var full = Path.Combine(dir.Trim().Trim('"'), dll);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string? RegistryDir(string keyPath, string value)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(keyPath);
            return k?.GetValue(value) as string;
        }
        catch { return null; }
    }

    private static string? UninstallDir(string keyPath)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(keyPath);
            var s = k?.GetValue("UninstallString") as string;
            return s is null ? null : Path.GetDirectoryName(s.Trim('"'));
        }
        catch { return null; }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Logs into the VoiceMeeter API. Returns true on success.</summary>
    public bool Login()
    {
        if (_login is null) return false;
        lock (_lock)
        {
            try
            {
                var r = _login();
                // 0 = OK, 1 = launched VM, 2 = already open by another app
                _loggedIn = r >= 0;
                StatusMessage = _loggedIn ? "Connected to VoiceMeeter" : "VoiceMeeter not running";
                if (_loggedIn)
                    Log($"✓ VoiceMeeter API login OK (code {r})", LogLevel.Success);
                else
                    Log($"⚠ VoiceMeeter login returned {r} – is VoiceMeeter running?", LogLevel.Warning);
                return _loggedIn;
            }
            catch (Exception ex)
            {
                Log($"✗ Login exception details:\n{ex}", LogLevel.Error);
                return false;
            }
        }
    }

    /// <summary>Logs out from the API. Safe to call even if not logged in.</summary>
    public void Logout()
    {
        if (_logout is null || !_loggedIn) return;
        lock (_lock)
        {
            try { _logout(); } catch { /* ignore */ }
            finally { _loggedIn = false; }
        }
    }

    /// <summary>
    /// Sends <c>Command.Restart = 1</c> to restart the VoiceMeeter audio engine.
    /// Also invalidates the Bus A3 gain cache so the post-restart gain is re-read
    /// from VoiceMeeter on the next volume key press.
    /// </summary>
    public bool RestartEngine()
    {
        bool ok = SetParameterFloat("Command.Restart", 1.0f);
        if (ok) InvalidateBusA3Cache();   // post-restart gain may differ
        return ok;
    }

    /// <summary>
    /// Sets a VoiceMeeter float parameter (e.g. "Bus[2].Gain").
    /// </summary>
    public bool SetParameterFloat(string paramName, float value)
    {
        if (_setFloat is null) return false;
        lock (_lock)
        {
            EnsureLoggedIn();
            try
            {
                var r = _setFloat(paramName, value);
                if (r != 0)
                {
                    Log($"✗ SetParameterFloat({paramName}, {value}) failed with code {r}.", LogLevel.Error);
                }
                return r == 0;
            }
            catch (Exception ex)
            {
                Log($"✗ SetParameterFloat({paramName}) exception details:\n{ex}", LogLevel.Error);
                return false;
            }
        }
    }

    /// <summary>
    /// Reads a VoiceMeeter float parameter. Returns null on failure.
    /// </summary>
    public float? GetParameterFloat(string paramName)
    {
        if (_getFloat is null) return null;
        lock (_lock)
        {
            EnsureLoggedIn();
            try
            {
                var r = _getFloat(paramName, out float v);
                if (r != 0)
                {
                    Log($"✗ GetParameterFloat({paramName}) failed with code {r}.", LogLevel.Error);
                }
                return r == 0 ? v : null;
            }
            catch (Exception ex)
            {
                Log($"✗ GetParameterFloat({paramName}) exception details:\n{ex}", LogLevel.Error);
                return null;
            }
        }
    }

    /// <summary>
    /// Adjusts Bus A3 (<c>Bus[2].Gain</c>) by <paramref name="delta"/> dB,
    /// clamped to the valid range [−60, +12] dB.
    /// </summary>
    /// <remarks>
    /// Uses a local gain cache to avoid the VoiceMeeter API's stale-read problem:
    /// VBVMR_GetParameterFloat returns the pre-write value immediately after a SET
    /// because the engine processes writes asynchronously. We seed the cache from
    /// the API once, then maintain it ourselves on every successful write.
    /// </remarks>
    public void AdjustBusA3Gain(float delta)
    {
        // Seed the cache from the live API value on first call.
        // Call IsParametersDirty() first to flush the engine's internal state.
        if (_busA3GainCache is null)
        {
            _isDirty?.Invoke();   // pump the parameter bus so GET returns fresh data
            _busA3GainCache = GetParameterFloat("Bus[2].Gain") ?? 0f;
            Log($"Bus A3 Gain seed: {_busA3GainCache:+0.0;-0.0;0.0} dB", LogLevel.Info);
        }

        var current = _busA3GainCache.Value;
        var next    = Math.Clamp(current + delta, -60f, 12f);

        if (SetParameterFloat("Bus[2].Gain", next))
        {
            _isDirty?.Invoke();   // pump the parameter bus so GET returns fresh data
            _busA3GainCache = GetParameterFloat("Bus[2].Gain") ?? 0f;   // keep cache in sync with what we just wrote
            Log($"Bus A3 Gain: {current:+0.0;-0.0;0.0} → {next:+0.0;-0.0;0.0} dB  ({delta:+0.0;-0.0} dB)", LogLevel.Info);
        }
    }

    /// <summary>
    /// Invalidates the Bus A3 gain cache so the next call to
    /// <see cref="AdjustBusA3Gain"/> re-seeds from the live VoiceMeeter value.
    /// Call this after an engine restart or if the gain may have been changed externally.
    /// </summary>
    public void InvalidateBusA3Cache() => _busA3GainCache = null;

    // ── Helpers ────────────────────────────────────────────────────────────────
    private void EnsureLoggedIn() { if (!_loggedIn) Login(); }

    private static void Log(string msg, LogLevel level)
        => Logger.Instance.Log(msg, level);

    // ── IDisposable ────────────────────────────────────────────────────────────
    public void Dispose()
    {
        Logout();
        if (_dllHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_dllHandle);
            _dllHandle = IntPtr.Zero;
        }
    }
}
