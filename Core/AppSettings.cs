using System.IO;
using System.Text.Json;

namespace AutoRestartVoicemeeter.Core;

public enum DeviceFilterType
{
    All,
    Usb,
    AudioEndpoint,
    Keyword
}

public class DeviceFilter
{
    public string Name { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public DeviceFilterType Type { get; set; } = DeviceFilterType.All;
    public bool IsEnabled { get; set; } = true;

    public override string ToString() => $"{Name} ({DeviceCode})";
}

public class AppSettings
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoRestartVoicemeeter");

    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

    public List<DeviceFilter> TargetDevices { get; set; } = new();
    public bool VolumeHotkeyEnabled { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"⚠ Failed to load settings.json: {ex.Message}", LogLevel.Warning);
        }

        var defaultSettings = new AppSettings();
        defaultSettings.EnsureDefaultFallback();
        return defaultSettings;
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsFilePath, json);
            Logger.Instance.Log("✓ Settings saved successfully.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"⚠ Failed to save settings: {ex.Message}", LogLevel.Warning);
        }
    }

    public void EnsureDefaultFallback()
    {
        if (TargetDevices.Count == 0)
        {
            TargetDevices.Add(new DeviceFilter
            {
                Name = "Qudelix-5K (Default Fallback)",
                DeviceCode = "Qudelix",
                Type = DeviceFilterType.Keyword,
                IsEnabled = true
            });
        }
    }

    public bool IsDeviceMatched(string devicePathOrName, DeviceFilterType type)
    {
        if (string.IsNullOrWhiteSpace(devicePathOrName)) return false;

        var activeFilters = TargetDevices.Where(d => d.IsEnabled).ToList();
        if (activeFilters.Count == 0)
        {
            return devicePathOrName.Contains("Qudelix", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var filter in activeFilters)
        {
            // 1. Direct device code match (e.g. MMDevice ID, VID/PID, or custom pattern)
            if (!string.IsNullOrWhiteSpace(filter.DeviceCode) &&
                devicePathOrName.Contains(filter.DeviceCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. Extracted GUID match from device code (e.g. {88173984-7119-4a6d-b4c0-448bbdf5fb6})
            string guidPart = ExtractGuid(filter.DeviceCode);
            if (!string.IsNullOrWhiteSpace(guidPart) &&
                devicePathOrName.Contains(guidPart, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Exact or partial friendly name match
            if (!string.IsNullOrWhiteSpace(filter.Name) &&
                devicePathOrName.Contains(filter.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 4. Extracted product name inside parentheses if present (e.g. "Speakers (Qudelix-5K USB DAC)" -> "Qudelix-5K")
            string bracketed = ExtractBracketedText(filter.Name);
            if (!string.IsNullOrWhiteSpace(bracketed) && bracketed.Length >= 3 &&
                devicePathOrName.Contains(bracketed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractGuid(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        int lastDot = input.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < input.Length - 1)
        {
            return input.Substring(lastDot + 1).Trim('{', '}');
        }
        return input.Trim('{', '}');
    }

    private static string ExtractBracketedText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        int open = input.IndexOf('(');
        int close = input.IndexOf(')', open > 0 ? open : 0);
        if (open >= 0 && close > open + 1)
        {
            return input.Substring(open + 1, close - open - 1).Trim();
        }
        return string.Empty;
    }
}
