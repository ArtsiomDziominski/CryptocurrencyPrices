using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using static CryptoPrice.Constants;

namespace CryptoPrice.Services;

/// <summary>
/// Handles all Binance API communication: WebSocket trade streams,
/// 24h ticker REST endpoint, and klines (candlestick) data.
/// </summary>
public static class BinanceService
{
    private static readonly HttpClient Http = new();

    /// <summary>
    /// Runs a reconnecting WebSocket loop that streams trades for the given symbol.
    /// Calls <paramref name="onTrade"/> with each parsed price.
    /// Calls <paramref name="onConnectionChanged"/> when connection state changes.
    /// </summary>
    public static async Task ConnectWebSocketAsync(
        string symbol,
        Action<decimal> onTrade,
        Action<bool> onConnectionChanged,
        CancellationToken ct)
    {
        var url = $"{WsBaseUrl}{symbol}@trade";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), ct);
                onConnectionChanged(true);
                await ReceiveLoopAsync(ws, onTrade, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { onConnectionChanged(false); }

            try { await Task.Delay(ReconnectDelayMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task ReceiveLoopAsync(
        ClientWebSocket ws,
        Action<decimal> onTrade,
        CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var price = ParseTradePrice(json);
            if (price.HasValue)
                onTrade(price.Value);
        }
    }

    private static decimal? ParseTradePrice(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("p", out var priceElement)) return null;

            var priceStr = priceElement.GetString();
            if (priceStr is not null && decimal.TryParse(priceStr,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                return price;
        }
        catch { /* malformed message */ }
        return null;
    }

    /// <summary>
    /// Fetches the current (last traded) price for a symbol via REST.
    /// Used for alert polling on non-active symbols.
    /// </summary>
    public static async Task<decimal?> FetchCurrentPriceAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var url = $"{TickerApiUrl}{symbol.ToUpperInvariant()}";
            var response = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("lastPrice", out var priceEl))
                return null;

            var priceStr = priceEl.GetString();
            if (priceStr is not null && decimal.TryParse(priceStr,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                return price;
        }
        catch { /* network error */ }
        return null;
    }

    /// <summary>
    /// Fetches the 24-hour price change percentage for a symbol.
    /// Returns null on failure.
    /// </summary>
    public static async Task<decimal?> Fetch24hChangeAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var url = $"{TickerApiUrl}{symbol.ToUpperInvariant()}";
            var response = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("priceChangePercent", out var pctEl))
                return null;

            var pctStr = pctEl.GetString();
            if (pctStr is not null && decimal.TryParse(pctStr,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
                return pct;
        }
        catch { /* network error */ }
        return null;
    }

    /// <summary>
    /// Fetches candlestick (kline) close prices for a symbol.
    /// Returns a list of close prices ordered chronologically.
    /// </summary>
    public static async Task<List<decimal>> FetchKlinesAsync(
        string symbol,
        string interval = "1m",
        int limit = 60,
        CancellationToken ct = default)
    {
        var prices = new List<decimal>();
        try
        {
            var url = $"{KlinesApiUrl}?symbol={symbol.ToUpperInvariant()}&interval={interval}&limit={limit}";
            var response = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(response);

            foreach (var kline in doc.RootElement.EnumerateArray())
            {
                // Kline format: [openTime, open, high, low, close, ...]
                var closeStr = kline[4].GetString();
                if (closeStr is not null && decimal.TryParse(closeStr,
                        NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                    prices.Add(close);
            }
        }
        catch { /* network error */ }
        return prices;
    }
}
