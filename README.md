# AutoRestart VoiceMeeter 🎧⚡

**AutoRestart VoiceMeeter** is a silent Windows system tray application built in C# (.NET 8 WPF) that automatically restarts the VoiceMeeter audio engine whenever targeted audio devices or USB DACs (such as the Qudelix-5K over USB or Bluetooth) connect to your computer.

---

## ✨ Features

- **⚡ Automatic Audio Engine Restart**: Detects hardware arrivals in real-time and restarts the VoiceMeeter audio engine (`VBVMR_RESTART`) after a short debounce delay, eliminating silent or desynced audio endpoints.
- **🔍 Target Device Picker with Live Filtering**:
  - Live hardware enumeration of active **Audio Endpoints** (WASAPI / MMDevice) and **USB Devices** (PnP / SetupAPI).
  - Built-in real-time search box to quickly filter devices by friendly name or hardware ID / device code.
  - Distinct visual highlighting for active targets.
  - Ability to add custom device code patterns (e.g. `VID_04D8&PID_EEAC` or custom keywords).
- **📡 Dual-Layer Hardware Detection**:
  - **USB Arrival Watcher**: Listens for Win32 `WM_DEVICECHANGE` / `DBT_DEVICEARRIVAL` events.
  - **Bluetooth & Audio Endpoint Monitor**: Listens for Core Audio `IMMNotificationClient` endpoint additions.
- **🎵 Global Volume Hotkeys**: Option to hook media volume keys directly to control VoiceMeeter Bus A3 Gain.
- **🚀 Windows Startup Integration**: One-click toggle to launch silently at Windows login via the Windows Registry.
- **📋 Live Diagnostics Log Viewer**: Floating dark-themed log window to monitor real-time arrival logs, VoiceMeeter API calls, and debug messages.

---

## 🛠 Prerequisites & Requirements

- **Operating System**: Windows 10 / Windows 11 (x64)
- **Runtime**: [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- **VoiceMeeter**: VoiceMeeter, VoiceMeeter Banana, or VoiceMeeter Potato installed (provides `VoicemeeterRemote64.dll` at `C:\Program Files (x86)\VB\Voicemeeter`).

---

## 🚀 Building & Running

### Building from Source
Open a terminal in the project directory and run:

```bash
dotnet build AutoRestartVoicemeeter.csproj -c Release
```

### Running the App
Run the compiled executable located at:

```
bin\Release\net8.0-windows\AutoRestartVoicemeeter.exe
```

The application will launch silently into your **System Tray**.

---

## ⚙ Usage

### System Tray Context Menu
Right-click the **AutoRestart VoiceMeeter** icon in the Windows notification area to access:

- **● Status Indicator**: Displays VoiceMeeter connection state (*Connected*, *Restarting*, *Error*).
- **📋 Show Log**: Opens the real-time event log window.
- **⚙ Select Target Devices...**: Opens the device selection dialog to manage which connected devices trigger a VoiceMeeter engine restart.
- **🔄 Restart VoiceMeeter Engine**: Manually triggers an immediate VoiceMeeter engine restart.
- **🎵 Volume Key → Bus A3**: Toggles media key intercept for Bus A3 gain control.
- **🚀 Run at Startup**: Enables/disables auto-start on Windows login.
- **✖ Exit**: Closes the application.

---

## 📁 Configuration File

Settings are automatically persisted to:

```
%APPDATA%\AutoRestartVoicemeeter\settings.json
```

### Example Configuration (`settings.json`)
```json
{
  "TargetDevices": [
    {
      "Name": "Qudelix-5K (Default Fallback)",
      "DeviceCode": "Qudelix",
      "Type": 3,
      "IsEnabled": true
    }
  ]
}
```

---

## 📂 Project Structure

```
AutoRestartVoicemeeter/
├── App.xaml / App.xaml.cs          # Entry point & single-instance Mutex guard
├── AutoRestartVoicemeeter.csproj    # .NET 8.0 WPF project file
├── Core/
│   ├── AppSettings.cs              # Settings model & JSON persistence
│   ├── AudioDeviceMonitor.cs       # Core Audio IMMNotificationClient callback
│   ├── DeviceEnumerator.cs         # SetupAPI & WASAPI live hardware enumeration
│   ├── DeviceWatcher.cs            # Win32 WM_DEVICECHANGE window message hook
│   ├── Logger.cs                   # Thread-safe in-memory & UI logger
│   └── VoicemeeterApi.cs           # P/Invoke wrapper for VoicemeeterRemote64.dll
├── Services/
│   ├── HotkeyService.cs            # Low-level Windows keyboard hook for volume keys
│   ├── RestartService.cs           # Debounced VoiceMeeter restart engine
│   └── StartupService.cs           # Windows Registry HKCU\Software\Microsoft\Windows\CurrentVersion\Run
└── UI/
    ├── DeviceSelectionWindow.xaml  # Target device picker with live search & visual highlights
    ├── IconHelper.cs               # GDI+ dynamic system tray icon generator
    ├── LogWindow.xaml              # WPF dark-themed diagnostics log viewer
    └── TrayIconManager.cs          # System tray icon & context menu controller
```

---

## 📜 License

Distributed under the MIT License. Feel free to modify and adapt for your own audio workflow setup!
