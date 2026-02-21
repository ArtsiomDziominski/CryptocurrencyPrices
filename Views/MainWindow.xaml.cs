using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CryptoPrice.Models;
using CryptoPrice.Services;
using static CryptoPrice.Models.Constants;

namespace CryptoPrice.Views;

/// <summary>
/// Transparent desktop widget that streams live crypto prices
/// from Binance over WebSocket. Supports multiple symbols with
/// add/remove management and left/right navigation.
/// </summary>
public partial class MainWindow : Window
{
    private readonly List<string> _symbols = new() { "btcusdt", "ethusdt" };
    private int _currentIndex;

    private CancellationTokenSource _wsCts = new();
    private DateTime _lastUiUpdate = DateTime.MinValue;

    private static readonly string SymbolsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "symbols.json");

    private AppSettings _appSettings = AppSettings.Load();
    private List<PriceAlert> _alerts = PriceAlert.LoadAll();
    private readonly Dictionary<string, decimal> _lastPrices = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        LoadSymbols();
        ApplySettings(_appSettings);
        ApplyZoom(_appSettings.ZoomScale);
        _appSettings.ApplyStartup();
        Loaded += (_, _) =>
        {
            StartStream();
            _ = RunAlertPollingLoopAsync(_wsCts.Token);
        };
    }

    // ─── Symbol management ──────────────────────────────────────────

    private string CurrentSymbol => _symbols[_currentIndex];

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
        var ct = _wsCts.Token;
        _ = BinanceService.ConnectWebSocketAsync(CurrentSymbol, OnTrade, SetConnected, ct);
        _ = RunTickerLoopAsync(CurrentSymbol, ct);
        _ = RunKlinesLoopAsync(CurrentSymbol, ct);
    }

    // ─── Persistence ────────────────────────────────────────────────

    private void LoadSymbols()
    {
        try
        {
            if (!File.Exists(SymbolsPath)) return;
            var json = File.ReadAllText(SymbolsPath);
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
            File.WriteAllText(SymbolsPath, json);
        }
        catch { /* non-critical */ }
    }

    // ─── Trade callback (from WebSocket via BinanceService) ─────────

    private void OnTrade(decimal price)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastUiUpdate).TotalMilliseconds < ThrottleIntervalMs) return;
        _lastUiUpdate = now;

        var formatted = price.ToString("C2",
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        Dispatcher.Invoke(() =>
        {
            PriceText.Text = formatted;
            CheckAlerts(CurrentSymbol, price);
        });
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

    // ─── 24h ticker loop ────────────────────────────────────────────

    private async Task RunTickerLoopAsync(string symbol, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pct = await BinanceService.Fetch24hChangeAsync(symbol, ct);
            if (pct.HasValue)
                Dispatcher.Invoke(() => Update24hChange(pct.Value));

            try { await Task.Delay(TickerRefreshMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Update24hChange(decimal pct)
    {
        var sign = pct >= 0 ? "+" : "";
        ChangeText.Text = $"24ч: {sign}{pct:F2}%";
        ChangeText.Foreground = pct >= 0
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
            : new SolidColorBrush(Color.FromRgb(244, 67, 54));
    }

    // ─── Klines / sparkline loop ────────────────────────────────────

    private async Task RunKlinesLoopAsync(string symbol, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_appSettings.ShowSparkline)
            {
                var prices = await BinanceService.FetchKlinesAsync(symbol, ct: ct);
                if (prices.Count > 0)
                    Dispatcher.Invoke(() => Sparkline.UpdatePrices(prices));
            }

            try { await Task.Delay(KlinesRefreshMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ─── Context menu: Crypto List (add / remove / switch) ──────────

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        CryptoListMenu.Items.Clear();

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

        var addItem = new MenuItem { Header = "Add…" };
        addItem.Click += AddSymbol_Click;
        CryptoListMenu.Items.Add(addItem);

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

    private void ApplyZoom(double newScale, bool persist = false)
    {
        newScale = Math.Clamp(newScale, ZoomMin, ZoomMax);
        WidgetScale.ScaleX = newScale;
        WidgetScale.ScaleY = newScale;
        SizeToContent = SizeToContent.WidthAndHeight;

        if (persist)
        {
            _appSettings.ZoomScale = newScale;
            _appSettings.Save();
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
        => ApplyZoom(WidgetScale.ScaleX + ZoomStep, persist: true);

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
        => ApplyZoom(WidgetScale.ScaleX - ZoomStep, persist: true);

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
        => ApplyZoom(1.0, persist: true);

    // ─── Alerts ──────────────────────────────────────────────────

    private void Alerts_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AlertsWindow(_symbols, _alerts) { Owner = this };
        dialog.ShowDialog();
        _alerts = PriceAlert.LoadAll();
    }

    private void CheckAlerts(string symbol, decimal price)
    {
        var hasPrevious = _lastPrices.TryGetValue(symbol, out var previousPrice);
        _lastPrices[symbol] = price;

        if (!hasPrevious) return;

        var triggered = _alerts
            .Where(a => a.IsEnabled
                        && a.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                        && a.IsCrossed(previousPrice, price))
            .ToList();

        foreach (var alert in triggered)
        {
            if (!alert.Persistent)
                alert.IsEnabled = false;
            TriggerAlert(alert, price);
        }

        if (triggered.Count > 0)
            PriceAlert.SaveAll(_alerts);
    }

    private void TriggerAlert(PriceAlert alert, decimal price)
    {
        if (alert.PlaySound)
            SystemSounds.Exclamation.Play();

        if (alert.FlashWidget)
            FlashBorder();

        var toast = new AlertToast(alert, price);
        toast.Show();
    }

    private void FlashBorder()
    {
        var originalBrush = MainBorder.Background as SolidColorBrush;
        var originalColor = originalBrush?.Color ?? Color.FromArgb(0xCC, 0x1A, 0x1A, 0x2E);

        var flashColor = Color.FromRgb(255, 193, 7); // amber

        var brush = new SolidColorBrush(originalColor);
        MainBorder.Background = brush;

        var anim = new ColorAnimation
        {
            From = flashColor,
            To = originalColor,
            Duration = TimeSpan.FromMilliseconds(800),
            AutoReverse = false,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        anim.Completed += (_, _) =>
        {
            MainBorder.Background = originalBrush ?? new SolidColorBrush(originalColor);
        };

        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private async Task RunAlertPollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(AlertPollIntervalMs, ct); }
            catch (OperationCanceledException) { break; }

            var symbolsToCheck = _alerts
                .Where(a => a.IsEnabled
                            && !a.Symbol.Equals(CurrentSymbol, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var symbol in symbolsToCheck)
            {
                if (ct.IsCancellationRequested) break;
                var price = await BinanceService.FetchCurrentPriceAsync(symbol, ct);
                if (price.HasValue)
                    Dispatcher.Invoke(() => CheckAlerts(symbol, price.Value));
            }
        }
    }

    // ─── Settings ─────────────────────────────────────────────────

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_appSettings, ApplySettings) { Owner = this };
        dialog.ShowDialog();
        _appSettings = dialog.Result;
        _appSettings.ApplyStartup();
        _appSettings.Save();
    }

    private void ApplySettings(AppSettings s)
    {
        try
        {
            MainBorder.Background = new BrushConverter().ConvertFromString(s.BackgroundColor) as SolidColorBrush
                                    ?? MainBorder.Background;
            PriceText.Foreground = new BrushConverter().ConvertFromString(s.PriceFontColor) as SolidColorBrush
                                   ?? PriceText.Foreground;
            SymbolLabel.Foreground = new BrushConverter().ConvertFromString(s.LabelFontColor) as SolidColorBrush
                                    ?? SymbolLabel.Foreground;
        }
        catch { /* invalid color string — keep current appearance */ }

        var navVis = s.ShowNavigation ? Visibility.Visible : Visibility.Collapsed;
        PrevButton.Visibility = navVis;
        NextButton.Visibility = navVis;

        var labelVis = s.ShowSymbolLabel ? Visibility.Visible : Visibility.Collapsed;
        SymbolLabel.Visibility = labelVis;
        StatusIndicator.Visibility = labelVis;

        ChangeText.Visibility = s.ShowChange24h ? Visibility.Visible : Visibility.Collapsed;
        Sparkline.Visibility = s.ShowSparkline ? Visibility.Visible : Visibility.Collapsed;

        ApplyZoom(s.ZoomScale);
    }

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
