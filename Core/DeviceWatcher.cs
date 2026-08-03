using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AutoRestartVoicemeeter.Core;

/// <summary>
/// Listens for <c>WM_DEVICECHANGE / DBT_DEVICEARRIVAL</c> via a hidden <see cref="HwndSource"/>
/// and fires <see cref="QudelixArrived"/> when arriving device path matches configured target device codes.
/// </summary>
public sealed class DeviceWatcher : IDisposable
{
    // ── Win32 constants ────────────────────────────────────────────────────────
    private const int  WM_DEVICECHANGE           = 0x0219;
    private const int  DBT_DEVICEARRIVAL         = 0x8000;
    private const int  DBT_DEVTYP_DEVICEINTERFACE = 5;
    private const uint DEVICE_NOTIFY_WINDOW_HANDLE          = 0;
    private const uint DEVICE_NOTIFY_ALL_INTERFACE_CLASSES  = 4;

    // ── DEV_BROADCAST_DEVICEINTERFACE header (fixed part before dbcc_name) ────
    [StructLayout(LayoutKind.Explicit)]
    private struct DEV_BROADCAST_DEVICEINTERFACE_HEADER
    {
        [FieldOffset(0)]  public int  dbcc_size;
        [FieldOffset(4)]  public int  dbcc_devicetype;
        [FieldOffset(8)]  public int  dbcc_reserved;
        [FieldOffset(12)] public Guid dbcc_classguid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_DEVICEINTERFACE_FILTER
    {
        public int  dbcc_size;
        public int  dbcc_devicetype;
        public int  dbcc_reserved;
        public Guid dbcc_classguid;
        public char dbcc_name;
    }

    // ── P/Invoke ───────────────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(
        IntPtr hRecipient,
        ref DEV_BROADCAST_DEVICEINTERFACE_FILTER notificationFilter,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    // ── State ──────────────────────────────────────────────────────────────────
    private HwndSource? _hwnd;
    private IntPtr      _notifHandle = IntPtr.Zero;
    private readonly AppSettings _settings;

    // ── Events ─────────────────────────────────────────────────────────────────
    /// <summary>Raised (on any thread) when a target USB device path is detected.</summary>
    public event EventHandler? QudelixArrived;

    public DeviceWatcher(AppSettings settings)
    {
        _settings = settings;
    }

    // ── Start / Stop ───────────────────────────────────────────────────────────
    public void Start()
    {
        var p = new HwndSourceParameters("VM_DeviceWatcher")
        {
            WindowStyle          = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle  = 0,
            Width = 0, Height = 0,
            PositionX = -32000, PositionY = -32000,
        };

        _hwnd = new HwndSource(p);
        _hwnd.AddHook(WndProc);

        RegisterAllInterfaces(_hwnd.Handle);
        Logger.Instance.Log("Device watcher started (WM_DEVICECHANGE).", LogLevel.Info);
    }

    private void RegisterAllInterfaces(IntPtr hwnd)
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE_FILTER
        {
            dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid  = Guid.Empty,
            dbcc_name       = '\0',
        };
        filter.dbcc_size = Marshal.SizeOf(filter);

        _notifHandle = RegisterDeviceNotification(
            hwnd,
            ref filter,
            DEVICE_NOTIFY_WINDOW_HANDLE | DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);

        if (_notifHandle == IntPtr.Zero)
            Logger.Instance.Log(
                $"⚠ RegisterDeviceNotification failed (error {Marshal.GetLastWin32Error()})",
                LogLevel.Warning);
        else
            Logger.Instance.Log("✓ Registered for all device-interface arrivals.", LogLevel.Info);
    }

    // ── Message hook ───────────────────────────────────────────────────────────
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE || wParam.ToInt32() != DBT_DEVICEARRIVAL || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            var hdr = Marshal.PtrToStructure<DEV_BROADCAST_DEVICEINTERFACE_HEADER>(lParam);
            if (hdr.dbcc_devicetype != DBT_DEVTYP_DEVICEINTERFACE)
                return IntPtr.Zero;

            const int NameByteOffset = 28;
            var namePtr    = IntPtr.Add(lParam, NameByteOffset);
            var devicePath = Marshal.PtrToStringUni(namePtr) ?? string.Empty;

            Logger.Instance.Log($"Device arrived: {devicePath}", LogLevel.Info);

            if (_settings.IsDeviceMatched(devicePath, DeviceFilterType.Usb))
            {
                Logger.Instance.Log($"🎧 Target device detected via WM_DEVICECHANGE: {devicePath}", LogLevel.Success);
                QudelixArrived?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"⚠ WM_DEVICECHANGE parse error details:\n{ex}", LogLevel.Warning);
        }

        return IntPtr.Zero;
    }

    // ── IDisposable ────────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_notifHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notifHandle);
            _notifHandle = IntPtr.Zero;
        }
        _hwnd?.Dispose();
        _hwnd = null;
    }
}
