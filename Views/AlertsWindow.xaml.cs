using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using CryptoPrice.Models;

namespace CryptoPrice.Views;

/// <summary>
/// View-model wrapper for displaying a PriceAlert in the ItemsControl.
/// </summary>
public class AlertViewModel
{
    private readonly PriceAlert _alert;

    public AlertViewModel(PriceAlert alert) => _alert = alert;

    public string Id => _alert.Id;
    public bool IsEnabled
    {
        get => _alert.IsEnabled;
        set => _alert.IsEnabled = value;
    }

    public string SymbolLabel => FormatSymbol(_alert.Symbol);
    public string PriceLabel => _alert.TargetPrice.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    public string OptionsLabel
    {
        get
        {
            var parts = new List<string>();
            if (_alert.PlaySound) parts.Add("sound");
            if (_alert.FlashWidget) parts.Add("flash");
            if (_alert.Persistent) parts.Add("persistent");
            return parts.Count > 0 ? string.Join(", ", parts) : "silent";
        }
    }

    private static string FormatSymbol(string symbol)
    {
        var s = symbol.ToUpperInvariant();
        if (s.EndsWith("USDT")) return s[..^4] + "/USDT";
        if (s.EndsWith("USD")) return s[..^3] + "/USD";
        return s;
    }
}

public partial class AlertsWindow : Window
{
    private readonly List<PriceAlert> _alerts;
    private readonly List<string> _symbols;

    public AlertsWindow(List<string> symbols, List<PriceAlert> alerts)
    {
        InitializeComponent();
        _symbols = symbols;
        _alerts = alerts;

        foreach (var sym in _symbols)
        {
            var label = FormatSymbol(sym);
            SymbolCombo.Items.Add(new ComboBoxItem { Content = label, Tag = sym });
        }

        if (SymbolCombo.Items.Count > 0)
            SymbolCombo.SelectedIndex = 0;

        RefreshList();
    }

    private void RefreshList()
    {
        AlertsList.ItemsSource = _alerts.Select(a => new AlertViewModel(a)).ToList();
        EmptyLabel.Visibility = _alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddAlert_Click(object sender, RoutedEventArgs e)
    {
        if (SymbolCombo.SelectedItem is not ComboBoxItem symbolItem) return;

        var priceText = PriceBox.Text.Trim().Replace(",", "").Replace("$", "");
        if (!decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price <= 0)
        {
            MessageBox.Show("Enter a valid price.", "Invalid Price",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var alert = new PriceAlert
        {
            Symbol = symbolItem.Tag?.ToString() ?? "btcusdt",
            TargetPrice = price,
            PlaySound = ChkSound.IsChecked == true,
            FlashWidget = ChkFlash.IsChecked == true,
            Persistent = ChkPersistent.IsChecked == true
        };

        _alerts.Add(alert);
        PriceAlert.SaveAll(_alerts);
        PriceBox.Text = "";
        RefreshList();
    }

    private void PriceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddAlert_Click(sender, e);
    }

    private void DeleteAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        _alerts.RemoveAll(a => a.Id == id);
        PriceAlert.SaveAll(_alerts);
        RefreshList();
    }

    private void AlertToggle_Changed(object sender, RoutedEventArgs e)
    {
        PriceAlert.SaveAll(_alerts);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private static string FormatSymbol(string symbol)
    {
        var s = symbol.ToUpperInvariant();
        if (s.EndsWith("USDT")) return s[..^4] + " / USDT";
        if (s.EndsWith("USD")) return s[..^3] + " / USD";
        return s;
    }
}
