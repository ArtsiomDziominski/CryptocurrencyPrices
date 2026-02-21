using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CryptoPrice;

/// <summary>
/// Persisted user preferences for the widget appearance.
/// </summary>
public class AppSettings
{
    private static readonly string Path = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "CryptoPrice";

    // Background color as ARGB hex string, e.g. "#CC1A1A2E"
    public string BackgroundColor { get; set; } = "#CC1A1A2E";

    // Price text color as RGB hex string, e.g. "#FFFFFF"
    public string PriceFontColor { get; set; } = "#FFFFFF";

    // Symbol label color
    public string LabelFontColor { get; set; } = "#BBFFFFFF";

    // Visibility toggles
    public bool ShowNavigation { get; set; } = true;
    public bool ShowSymbolLabel { get; set; } = true;
    public bool ShowChange24h { get; set; } = true;
    public bool RunAtStartup { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path, json);
        }
        catch { /* non-critical */ }
    }

    // ── Windows startup registry ─────────────────────────────────

    public static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
        return key?.GetValue(StartupValueName) != null;
    }

    public static void SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (exePath != null)
                    key.SetValue(StartupValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(StartupValueName, false);
            }
        }
        catch { /* non-critical */ }
    }

    public void ApplyStartup() => SetStartupEnabled(RunAtStartup);
}
