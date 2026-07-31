using System.Runtime.InteropServices;
using AutoRestartVoicemeeter.Core;

namespace AutoRestartVoicemeeter.Services;

/// <summary>
/// Installs a low-level keyboard hook (<c>WH_KEYBOARD_LL</c>) that intercepts the
/// system media Volume-Up and Volume-Down keys and redirects them to Bus A3 gain
/// adjustment via <see cref="VoicemeeterApi"/>. The events are consumed (swallowed)
/// so Windows does not also adjust the master volume.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // ── Win32 constants ────────────────────────────────────────────────────────
    private const int  WH_KEYBOARD_LL  = 13;
    private const int  WM_KEYDOWN      = 0x0100;
    private const int  WM_SYSKEYDOWN   = 0x0104;
    private const uint VK_VOLUME_MUTE  = 0xAD; // pass through (don't intercept)
    private const uint VK_VOLUME_DOWN  = 0xAE;
    private const uint VK_VOLUME_UP    = 0xAF;

    // ── Structs ────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint  vkCode;
        public uint  scanCode;
        public uint  flags;
        public uint  time;
        public IntPtr dwExtraInfo;
    }

    // ── P/Invoke ───────────────────────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc proc,
                                                   IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? name);

    // ── State ──────────────────────────────────────────────────────────────────
    private readonly VoicemeeterApi _api;
    private readonly AppSettings    _settings;
    private readonly LowLevelKeyboardProc _proc; // Must be kept alive for the hook lifetime!
    private IntPtr _hookId = IntPtr.Zero;
    private bool   _enabled = true;

    /// <summary>Gets or sets whether media key interception is active.</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (_settings.VolumeHotkeyEnabled != value)
            {
                _settings.VolumeHotkeyEnabled = value;
                _settings.Save();
            }
            Logger.Instance.Log(
                $"Media key hook {(value ? "enabled ✓" : "disabled")}.", LogLevel.Info);
        }
    }

    // ── Constructor ────────────────────────────────────────────────────────────
    public HotkeyService(VoicemeeterApi api, AppSettings settings)
    {
        _api      = api;
        _settings = settings;
        _enabled  = settings.VolumeHotkeyEnabled;
        _proc     = HookCallback; // Pin delegate before passing to SetWindowsHookEx
        Install();
    }

    // ── Hook installation ──────────────────────────────────────────────────────
    private void Install()
    {
        using var mod = System.Diagnostics.Process.GetCurrentProcess().MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);

        if (_hookId == IntPtr.Zero)
            Logger.Instance.Log(
                $"⚠ Keyboard hook install failed (error {Marshal.GetLastWin32Error()}). " +
                "Volume keys will not control Bus A3.", LogLevel.Warning);
        else
            Logger.Instance.Log(
                "✓ Media key hook active: Vol ↑ / ↓  →  Bus A3 Gain ±2 dB (events swallowed).",
                LogLevel.Success);
    }

    // ── Hook callback ──────────────────────────────────────────────────────────
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _enabled &&
            (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (kb.vkCode == VK_VOLUME_UP)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _api.AdjustBusA3Gain(+2.0f));
                return (IntPtr)1; // Swallow — do not pass to system
            }

            if (kb.vkCode == VK_VOLUME_DOWN)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _api.AdjustBusA3Gain(-2.0f));
                return (IntPtr)1;
            }

            // VK_VOLUME_MUTE is intentionally NOT intercepted
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ── IDisposable ────────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
