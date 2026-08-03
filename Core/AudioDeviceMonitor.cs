using System.Runtime.InteropServices;

namespace AutoRestartVoicemeeter.Core;

/// <summary>
/// Uses the Windows Core Audio <c>IMMNotificationClient</c> COM callback to detect
/// when any audio endpoint is added, then reads its friendly name to identify matching target devices.
/// </summary>
public sealed class AudioDeviceMonitor : IDisposable
{
    private const int DEVICE_STATE_ACTIVE = 0x00000001;

    // ──────────────────────────────────────────────────────────────────────────
    //  COM interface definitions
    // ──────────────────────────────────────────────────────────────────────────

    private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    // CoClass
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),
     ClassInterface(ClassInterfaceType.None)]
    private class MMDeviceEnumeratorCom { }

    // Consumed COM interfaces (→ RCW)
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int state, out IMMDeviceCollection col);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice dev);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dev);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int Item(int i, out IMMDevice dev);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr pParams,
                                   [MarshalAs(UnmanagedType.IUnknown)] out object ppIface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetAt(int i, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        [PreserveSig] int Commit();
    }

    // Implemented COM interface (→ CCW when passed to native code)
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role,
                                    [MarshalAs(UnmanagedType.LPWStr)] string? id);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY key);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Structs
    // ──────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid  fmtid;
        public uint  pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort  vt;
        [FieldOffset(8)] public IntPtr  pszVal;   // VT_LPWSTR (31)
    }

    private const ushort VT_LPWSTR = 31;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pv);

    // PKEY_Device_FriendlyName = {A45C254E-DF1C-4EFD-8020-67D146A850E0}, pid 14
    private static readonly PROPERTYKEY PKEY_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid   = 14,
    };

    // ──────────────────────────────────────────────────────────────────────────
    //  Notification client implementation
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly AudioDeviceMonitor _owner;
        public NotificationClient(AudioDeviceMonitor owner) => _owner = owner;

        public void OnDeviceAdded(string id)
        {
            // Minimal delay — allow Windows endpoint property store to register
            Task.Delay(100).ContinueWith(_ => _owner.CheckDeviceId(id));
        }

        public void OnDeviceStateChanged(string id, int state)   { }
        public void OnDeviceRemoved(string id)                    { }
        public void OnDefaultDeviceChanged(EDataFlow f, ERole r, string? id) { }
        public void OnPropertyValueChanged(string id, PROPERTYKEY k) { }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  State
    // ──────────────────────────────────────────────────────────────────────────

    private IMMDeviceEnumerator? _enumerator;
    private NotificationClient?  _client;
    private readonly AppSettings _settings;

    /// <summary>Fired (on a thread-pool thread) when a target audio endpoint appears.</summary>
    public event EventHandler? QudelixArrived;

    public AudioDeviceMonitor(AppSettings settings)
    {
        _settings = settings;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────────────────────

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            _client     = new NotificationClient(this);
            _enumerator.RegisterEndpointNotificationCallback(_client);
            Logger.Instance.Log("✓ Audio endpoint monitor started (IMMNotificationClient).", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"⚠ AudioDeviceMonitor init failed details:\n{ex}", LogLevel.Warning);
        }
    }

    internal void CheckDeviceId(string deviceId)
    {
        try
        {
            if (_enumerator is null) return;

            _enumerator.GetDevice(deviceId, out var device);
            var name = GetFriendlyName(device);

            Logger.Instance.Log($"Audio endpoint added: {name ?? deviceId}", LogLevel.Info);

            if (_settings.IsDeviceMatched(name ?? string.Empty, DeviceFilterType.AudioEndpoint) ||
                _settings.IsDeviceMatched(deviceId, DeviceFilterType.AudioEndpoint))
            {
                Logger.Instance.Log($"🎧 Target audio endpoint detected: {name ?? deviceId}", LogLevel.Success);
                QudelixArrived?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"⚠ CheckDeviceId error details:\n{ex}", LogLevel.Warning);
        }
    }

    public static List<(string Name, string DeviceCode)> EnumerateAudioEndpoints()
    {
        var result = new List<(string Name, string DeviceCode)>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.EnumAudioEndpoints(EDataFlow.eAll, DEVICE_STATE_ACTIVE, out var collection) == 0 && collection != null)
            {
                if (collection.GetCount(out int count) == 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (collection.Item(i, out var dev) == 0 && dev != null)
                        {
                            dev.GetId(out string id);
                            string name = GetFriendlyName(dev) ?? id;
                            result.Add((name, id));
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                }
                Marshal.ReleaseComObject(collection);
            }
            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"⚠ Audio endpoints enumeration failed details:\n{ex}", LogLevel.Warning);
        }

        return result;
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(0 /*STGM_READ*/, out var store);
            var key = PKEY_FriendlyName;
            store.GetValue(ref key, out var pv);

            string? name = null;
            if (pv.vt == VT_LPWSTR)
                name = Marshal.PtrToStringUni(pv.pszVal);

            PropVariantClear(ref pv);
            return name;
        }
        catch { return null; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        try
        {
            if (_enumerator is not null && _client is not null)
                _enumerator.UnregisterEndpointNotificationCallback(_client);
        }
        catch { /* best-effort */ }

        if (_enumerator is not null)
            Marshal.ReleaseComObject(_enumerator);

        _enumerator = null;
        _client     = null;
    }
}
