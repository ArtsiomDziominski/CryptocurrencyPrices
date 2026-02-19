using System.IO;
using System.Text.Json;

namespace CryptoPrice;

/// <summary>
/// Persisted user preferences for the widget appearance.
/// </summary>
public class AppSettings
{
    private static readonly string Path = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

    // Background color as ARGB hex string, e.g. "#CC1A1A2E"
    public string BackgroundColor { get; set; } = "#CC1A1A2E";

    // Price text color as RGB hex string, e.g. "#FFFFFF"
    public string PriceFontColor { get; set; } = "#FFFFFF";

    // Symbol label color
    public string LabelFontColor { get; set; } = "#BBFFFFFF";

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
}
