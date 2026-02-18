using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CryptoPrice;

/// <summary>
/// Transparent desktop widget that streams live crypto prices
/// from Binance over WebSocket. Supports multiple symbols with
/// add/remove management and left/right navigation.
/// </summary>
public partial class MainWindow : Window
{
    private const string WsBaseUrl = "wss://stream.binance.com:9443/ws/";
    private const string TickerApiUrl = "https://api.binance.com/api/v3/ticker/24hr?symbol=";
    private const int ReconnectDelayMs = 5_000;
    private const int ThrottleIntervalMs = 250;
    private const int TickerRefreshMs = 60_000; // refresh 24h change every 60s

    // Persisted symbol list and the currently displayed index.
    private readonly List<string> _symbols = new() { "btcusdt", "ethusdt" };
    private int _currentIndex;

    private CancellationTokenSource _wsCts = new();
    private DateTime _lastUiUpdate = DateTime.MinValue;
    private static readonly HttpClient Http = new();

    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "symbols.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadSymbols();
        Loaded += (_, _) => StartStream();
    }

    // ─── Symbol management ──────────────────────────────────────────

    private string CurrentSymbol => _symbols[_currentIndex];

    /// <summary>Format "btcusdt" → "BTC / USDT" for display.</summary>
    private static string FormatLabel(string symbol)
    {
        var s = symbol.ToUpperInvariant();
        if (s.EndsWith("USDT"))
            return s[..^4] + " / USDT";
        if (s.EndsWith("USD"))
            return s[..^3] + " / USD";
        return s;
    }

    private void SwitchToIndex(int index)
    {
        if (_symbols.Count == 0) return;
        _currentIndex = ((index % _symbols.Count) + _symbols.Count) % _symbols.Count;
        RestartStream();
    }

    /// <summary>Cancel the current WebSocket and start a new one for the selected symbol.</summary>
    private void RestartStream()
    {
        _wsCts.Cancel();
        _wsCts = new CancellationTokenSource();

        SymbolLabel.Text = FormatLabel(CurrentSymbol);
        PriceText.Text = "Connecting…";
        ChangeText.Text = "";
        _lastUiUpdate = DateTime.MinValue;

        StartStream();
    }

    private void StartStream()
    {
        SymbolLabel.Text = FormatLabel(CurrentSymbol);
        _ = RunWebSocketLoopAsync(CurrentSymbol, _wsCts.Token);
        _ = RunTickerLoopAsync(CurrentSymbol, _wsCts.Token);
    }

    // ─── Persistence ────────────────────────────────────────────────

    private void LoadSymbols()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list is { Count: > 0 })
            {
                _symbols.Clear();
                _symbols.AddRange(list);
                if (_currentIndex >= _symbols.Count)
                    _currentIndex = 0;
            }
        }
        catch { /* first run or corrupt file — use defaults */ }
    }

    private void SaveSymbols()
    {
        try
        {
            var json = JsonSerializer.Serialize(_symbols);
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* non-critical */ }
    }

    // ─── WebSocket lifecycle ────────────────────────────────────────

    private async Task RunWebSocketLoopAsync(string symbol, CancellationToken ct)
    {
        var url = $"{WsBaseUrl}{symbol}@trade";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), ct);
                SetConnected(true);
                await ReceiveLoopAsync(ws, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { SetConnected(false); }

            try { await Task.Delay(ReconnectDelayMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            ProcessTradeMessage(json);
        }
    }

    // ─── 24h ticker (REST API) ─────────────────────────────────────

    /// <summary>
    /// Periodically fetches the 24h price change percentage from
    /// Binance REST API and updates the ChangeText label.
    /// </summary>
    private async Task RunTickerLoopAsync(string symbol, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Fetch24hChangeAsync(symbol, ct);
            try { await Task.Delay(TickerRefreshMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task Fetch24hChangeAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var url = $"{TickerApiUrl}{symbol.ToUpperInvariant()}";
            var response = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("priceChangePercent", out var pctEl))
                return;

            var pctStr = pctEl.GetString();
            if (pctStr is null || !decimal.TryParse(pctStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var pct))
                return;

            Dispatcher.Invoke(() => Update24hChange(pct));
        }
        catch { /* network error — will retry next cycle */ }
    }

    private void Update24hChange(decimal pct)
    {
        var sign = pct >= 0 ? "+" : "";
        ChangeText.Text = $"24ч: {sign}{pct:F2}%";
        ChangeText.Foreground = pct >= 0
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))   // green
            : new SolidColorBrush(Color.FromRgb(244, 67, 54));  // red
    }

    // ─── JSON parsing & UI update ───────────────────────────────────

    private void ProcessTradeMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("p", out var priceElement)) return;

            var priceStr = priceElement.GetString();
            if (priceStr is null || !decimal.TryParse(priceStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var price))
                return;

            var now = DateTime.UtcNow;
            if ((now - _lastUiUpdate).TotalMilliseconds < ThrottleIntervalMs) return;
            _lastUiUpdate = now;

            var formatted = price.ToString("C2",
                System.Globalization.CultureInfo.GetCultureInfo("en-US"));

            Dispatcher.Invoke(() => PriceText.Text = formatted);
        }
        catch { /* malformed message — ignored */ }
    }

    private void SetConnected(bool connected)
    {
        Dispatcher.Invoke(() =>
        {
            StatusIndicator.Fill = connected
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));
            if (!connected)
                PriceText.Text = "Reconnecting…";
        });
    }

    // ─── Context menu: Crypto List (add / remove / switch) ──────────

    /// <summary>
    /// Dynamically populate the "Crypto List" submenu each time
    /// the context menu opens.
    /// </summary>
    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        CryptoListMenu.Items.Clear();

        // One item per symbol — click to switch, bold = current
        for (var i = 0; i < _symbols.Count; i++)
        {
            var idx = i;
            var item = new MenuItem
            {
                Header = FormatLabel(_symbols[i]),
                IsChecked = i == _currentIndex,
                FontWeight = i == _currentIndex ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (_, _) => SwitchToIndex(idx);
            CryptoListMenu.Items.Add(item);
        }

        CryptoListMenu.Items.Add(new Separator());

        // "Add…" opens a small input dialog
        var addItem = new MenuItem { Header = "Add…" };
        addItem.Click += AddSymbol_Click;
        CryptoListMenu.Items.Add(addItem);

        // "Remove current" (disabled when only one symbol left)
        var removeItem = new MenuItem
        {
            Header = $"Remove {FormatLabel(CurrentSymbol)}",
            IsEnabled = _symbols.Count > 1
        };
        removeItem.Click += RemoveCurrentSymbol_Click;
        CryptoListMenu.Items.Add(removeItem);
    }

    private void AddSymbol_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Add Crypto",
            "Enter Binance symbol (e.g. ethusdt, solusdt, dogeusdt):")
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
        {
            var sym = dialog.Result.Trim().ToLowerInvariant();
            if (_symbols.Contains(sym))
            {
                SwitchToIndex(_symbols.IndexOf(sym));
                return;
            }
            _symbols.Add(sym);
            SaveSymbols();
            SwitchToIndex(_symbols.Count - 1);
        }
    }

    private void RemoveCurrentSymbol_Click(object sender, RoutedEventArgs e)
    {
        if (_symbols.Count <= 1) return;
        _symbols.RemoveAt(_currentIndex);
        if (_currentIndex >= _symbols.Count) _currentIndex = 0;
        SaveSymbols();
        RestartStream();
    }

    // ─── Navigation arrows ──────────────────────────────────────────

    private void PrevSymbol_Click(object sender, RoutedEventArgs e)
        => SwitchToIndex(_currentIndex - 1);

    private void NextSymbol_Click(object sender, RoutedEventArgs e)
        => SwitchToIndex(_currentIndex + 1);

    // ─── Zoom (scale the entire widget) ────────────────────────────

    private const double ZoomStep = 0.15;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 2.5;

    private void ApplyZoom(double newScale)
    {
        newScale = Math.Clamp(newScale, ZoomMin, ZoomMax);
        WidgetScale.ScaleX = newScale;
        WidgetScale.ScaleY = newScale;
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
        => ApplyZoom(WidgetScale.ScaleX + ZoomStep);

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
        => ApplyZoom(WidgetScale.ScaleX - ZoomStep);

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
        => ApplyZoom(1.0);

    // ─── Window interactions ────────────────────────────────────────

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void Exit_Click(object sender, RoutedEventArgs e)
        => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _wsCts.Cancel();
        base.OnClosing(e);
    }
}
