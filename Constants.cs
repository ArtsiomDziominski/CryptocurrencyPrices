namespace CryptoPrice;

/// <summary>
/// Shared constants: API endpoints and timing intervals.
/// </summary>
public static class Constants
{
    // ── Binance API endpoints ───────────────────────────────────────
    public const string WsBaseUrl = "wss://stream.binance.com:9443/ws/";
    public const string TickerApiUrl = "https://api.binance.com/api/v3/ticker/24hr?symbol=";
    public const string KlinesApiUrl = "https://api.binance.com/api/v3/klines";

    // ── Timing ──────────────────────────────────────────────────────
    public const int ReconnectDelayMs = 5_000;
    public const int ThrottleIntervalMs = 250;
    public const int TickerRefreshMs = 60_000;
    public const int KlinesRefreshMs = 300_000;
}
