using System.Runtime.InteropServices;
using System.Text;

namespace AutoRestartVoicemeeter.Core;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public class DiscoveredDevice : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _deviceCode = string.Empty;
    private DeviceFilterType _type;
    private bool _isSelected;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string DeviceCode
    {
        get => _deviceCode;
        set { _deviceCode = value; OnPropertyChanged(); }
    }

    public DeviceFilterType Type
    {
        get => _type;
        set { _type = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override string ToString() => $"[{Type}] {Name} ({DeviceCode})";
}

public static class DeviceEnumerator
{
    // ──────────────────────────────────────────────────────────────────────────
    // SetupAPI Interop for USB / PnP Devices
    // ──────────────────────────────────────────────────────────────────────────

    private const uint DIGCF_PRESENT     = 0x00000002;
    private const uint DIGCF_ALLCLASSES  = 0x00000004;

    private const uint SPDRP_DEVICEDESC    = 0x00000000;
    private const uint SPDRP_HARDWAREID    = 0x00000001;
    private const uint SPDRP_FRIENDLYNAME  = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr ClassGuid,
        string? Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr DeviceInfoSet,
        uint MemberIndex,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        out uint PropertyRegDataType,
        StringBuilder PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        StringBuilder DeviceInstanceId,
        uint DeviceInstanceIdSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    // ──────────────────────────────────────────────────────────────────────────
    // Public Enumeration Methods
    // ──────────────────────────────────────────────────────────────────────────

    public static List<DiscoveredDevice> GetDiscoveredDevices(AppSettings currentSettings)
    {
        var devices = new List<DiscoveredDevice>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Audio Endpoints via AudioDeviceMonitor static scan
        var audioEndpoints = AudioDeviceMonitor.EnumerateAudioEndpoints();
        foreach (var endpoint in audioEndpoints)
        {
            if (seenCodes.Add(endpoint.DeviceCode))
            {
                bool isSelected = currentSettings.TargetDevices.Any(t =>
                    t.IsEnabled &&
                    (string.Equals(t.DeviceCode, endpoint.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(t.Name, endpoint.Name, StringComparison.OrdinalIgnoreCase)));

                devices.Add(new DiscoveredDevice
                {
                    Name = endpoint.Name,
                    DeviceCode = endpoint.DeviceCode,
                    Type = DeviceFilterType.AudioEndpoint,
                    IsSelected = isSelected
                });
            }
        }

        // 2. USB & PnP Devices via SetupAPI
        var pnpDevices = EnumeratePnpDevices();
        foreach (var pnp in pnpDevices)
        {
            if (seenCodes.Add(pnp.DeviceCode))
            {
                bool isSelected = currentSettings.TargetDevices.Any(t =>
                    t.IsEnabled &&
                    (string.Equals(t.DeviceCode, pnp.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(t.Name, pnp.Name, StringComparison.OrdinalIgnoreCase)));

                devices.Add(new DiscoveredDevice
                {
                    Name = pnp.Name,
                    DeviceCode = pnp.DeviceCode,
                    Type = DeviceFilterType.Usb,
                    IsSelected = isSelected
                });
            }
        }

        return devices;
    }

    private static List<DiscoveredDevice> EnumeratePnpDevices()
    {
        var result = new List<DiscoveredDevice>();

        IntPtr devInfo = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (devInfo == (IntPtr)(-1))
            return result;

        try
        {
            SP_DEVINFO_DATA da = new SP_DEVINFO_DATA();
            da.cbSize = (uint)Marshal.SizeOf(da);

            uint i = 0;
            while (SetupDiEnumDeviceInfo(devInfo, i, ref da))
            {
                i++;
                var idBuffer = new StringBuilder(1024);
                if (SetupDiGetDeviceInstanceId(devInfo, ref da, idBuffer, (uint)idBuffer.Capacity, out _))
                {
                    string instanceId = idBuffer.ToString();

                    string friendlyName = GetDeviceProperty(devInfo, ref da, SPDRP_FRIENDLYNAME);
                    if (string.IsNullOrWhiteSpace(friendlyName))
                        friendlyName = GetDeviceProperty(devInfo, ref da, SPDRP_DEVICEDESC);

                    if (string.IsNullOrWhiteSpace(friendlyName))
                        friendlyName = instanceId;

                    // Extract VID/PID code pattern if present (e.g. VID_04D8&PID_EEAC)
                    string deviceCode = ExtractVidPidOrCode(instanceId);

                    result.Add(new DiscoveredDevice
                    {
                        Name = friendlyName,
                        DeviceCode = deviceCode,
                        Type = DeviceFilterType.Usb
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"⚠ Error enumerating PnP devices: {ex.Message}", LogLevel.Warning);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfo);
        }

        return result;
    }

    private static string GetDeviceProperty(IntPtr devInfo, ref SP_DEVINFO_DATA da, uint property)
    {
        var buffer = new StringBuilder(1024);
        if (SetupDiGetDeviceRegistryProperty(devInfo, ref da, property, out _, buffer, (uint)buffer.Capacity, out _))
        {
            return buffer.ToString();
        }
        return string.Empty;
    }

    private static string ExtractVidPidOrCode(string instanceId)
    {
        // Example: USB\VID_04D8&PID_EEAC\5&12345678&0&1
        // We can extract "VID_04D8&PID_EEAC" if available, or use full instance ID
        int vidIdx = instanceId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        if (vidIdx >= 0)
        {
            int nextSlash = instanceId.IndexOf('\\', vidIdx);
            if (nextSlash > vidIdx)
            {
                return instanceId.Substring(vidIdx, nextSlash - vidIdx);
            }
            return instanceId.Substring(vidIdx);
        }

        return instanceId;
    }
}
