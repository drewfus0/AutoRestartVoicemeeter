using Microsoft.Win32;
using AutoRestartVoicemeeter.Core;

namespace AutoRestartVoicemeeter.Services;

/// <summary>
/// Manages the <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> registry value
/// that causes this application to launch at Windows startup.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName    = "AutoRestartVoicemeeter";

    /// <summary>Gets or sets whether this app is registered to run at Windows startup.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(AppName) is not null;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"⚠ Registry read failed details:\n{ex}", LogLevel.Warning);
                return false;
            }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key is null) return;

                if (value)
                    // Quote the path to handle spaces in the executable path
                    key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
                else
                    key.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"⚠ Registry write failed details:\n{ex}", LogLevel.Warning);
            }
        }
    }
}
