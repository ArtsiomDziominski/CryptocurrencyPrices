using System.IO;
using System.Text.Json;

namespace CryptoPrice.Models;

public class PriceAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Symbol { get; set; } = "btcusdt";
    public decimal TargetPrice { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public bool FlashWidget { get; set; } = true;

    private static readonly string FilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "alerts.json");

    public static List<PriceAlert> LoadAll()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<PriceAlert>>(json) ?? [];
        }
        catch { return []; }
    }

    public static void SaveAll(List<PriceAlert> alerts)
    {
        try
        {
            var json = JsonSerializer.Serialize(alerts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Returns true if the price crossed the target between previousPrice and currentPrice.
    /// </summary>
    public bool IsCrossed(decimal previousPrice, decimal currentPrice) =>
        (previousPrice < TargetPrice && currentPrice >= TargetPrice) ||
        (previousPrice > TargetPrice && currentPrice <= TargetPrice);
}
