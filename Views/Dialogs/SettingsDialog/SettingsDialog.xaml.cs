using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using CryptoPrice.Models;

namespace CryptoPrice.Views.Dialogs;

public partial class SettingsDialog : Window
{
    public AppSettings Result { get; private set; }

    private readonly Action<AppSettings>? _onChanged;

    private string _bgHex;
    private int _bgOpacity;   // 0-100
    private string _fgHex;
    private double _zoomPercent; // 50-250 (slider value)

    // ── Preset palettes ────────────────────────────────────────────
    private static readonly string[] BgPresets =
    {
        "#1A1A2E", "#12121F", "#0D1117",
        "#1C1C1C", "#1E2A3A", "#0A192F",
        "#2D1B69", "#1A0A2E", "#1F1F1F",
        "#192734", "#0F2027", "#1B2838"
    };

    private static readonly string[] FgPresets =
    {
        "#FFFFFF", "#F0F0F0", "#4C8BF5",
        "#4CAF50", "#F44336", "#FFC107",
        "#00BCD4", "#E91E63", "#FF9800",
        "#9C27B0", "#03A9F4", "#CDDC39"
    };

    public SettingsDialog(AppSettings current, Action<AppSettings>? onChanged = null)
    {
        InitializeComponent();
        Result = current;
        _onChanged = onChanged;

        var (hex, opacity) = SplitBgColor(current.BackgroundColor);
        _bgHex = hex;
        _bgOpacity = opacity;
        _fgHex = current.PriceFontColor.TrimStart('#');

        BuildPalette(BgPalette, BgPresets, OnBgSwatchClick);
        BuildPalette(FgPalette, FgPresets, OnFgSwatchClick);

        BgHexBox.Text = _bgHex;
        FgHexBox.Text = _fgHex;
        OpacitySlider.Value = _bgOpacity;

        ChkNavigation.IsChecked = current.ShowNavigation;
        ChkSymbolLabel.IsChecked = current.ShowSymbolLabel;
        ChkChange24h.IsChecked = current.ShowChange24h;
        ChkSparkline.IsChecked = current.ShowSparkline;
        ChkRunAtStartup.IsChecked = current.RunAtStartup;

        _zoomPercent = Math.Round(current.ZoomScale * 100);
        ZoomSlider.Value = _zoomPercent;

        RefreshBgPreview();
        RefreshFgPreview();
    }

    // ── Palette builder ────────────────────────────────────────────
    private static void BuildPalette(WrapPanel panel, string[] colors, MouseButtonEventHandler handler)
    {
        foreach (var hex in colors)
        {
            var border = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(3),
                Background = BrushFromHex(hex, 255),
                Cursor = Cursors.Hand,
                ToolTip = hex,
                Tag = hex.TrimStart('#')
            };
            border.MouseLeftButtonDown += handler;
            panel.Children.Add(border);
        }
    }

    // ── Swatch click handlers ──────────────────────────────────────
    private void OnBgSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string hex)
        {
            _bgHex = hex;
            BgHexBox.TextChanged -= BgHexBox_TextChanged;
            BgHexBox.Text = hex;
            BgHexBox.TextChanged += BgHexBox_TextChanged;
            RefreshBgPreview();
            NotifyChanged();
        }
    }

    private void OnFgSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string hex)
        {
            _fgHex = hex;
            FgHexBox.TextChanged -= FgHexBox_TextChanged;
            FgHexBox.Text = hex;
            FgHexBox.TextChanged += FgHexBox_TextChanged;
            RefreshFgPreview();
            NotifyChanged();
        }
    }

    // ── HEX text changed ───────────────────────────────────────────
    private void BgHexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _bgHex = BgHexBox.Text.TrimStart('#');
        RefreshBgPreview();
        NotifyChanged();
    }

    private void FgHexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _fgHex = FgHexBox.Text.TrimStart('#');
        RefreshFgPreview();
        NotifyChanged();
    }

    // ── Opacity slider ─────────────────────────────────────────────
    private void OpacitySlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        _bgOpacity = (int)e.NewValue;
        if (OpacityLabel != null)
            OpacityLabel.Text = $"{_bgOpacity}%";
        RefreshBgPreview();
        NotifyChanged();
    }

    // ── Preview refresh ────────────────────────────────────────────
    private void RefreshBgPreview()
    {
        var alpha = (byte)Math.Round(_bgOpacity / 100.0 * 255);
        var brush = BrushFromHex(_bgHex, alpha);
        if (brush != null && BgPreview != null)
            BgPreview.Background = brush;
    }

    private void RefreshFgPreview()
    {
        var brush = BrushFromHex(_fgHex, 255);
        if (brush != null && FgPreview != null)
            FgPreview.Background = brush;
    }

    // ── Color picker (click on preview square) ────────────────────
    private void BgPreview_Click(object sender, MouseButtonEventArgs e)
    {
        OpenColorPicker(_bgHex, hex =>
        {
            _bgHex = hex;
            BgHexBox.TextChanged -= BgHexBox_TextChanged;
            BgHexBox.Text = hex;
            BgHexBox.TextChanged += BgHexBox_TextChanged;
            RefreshBgPreview();
            NotifyChanged();
        });
    }

    private void FgPreview_Click(object sender, MouseButtonEventArgs e)
    {
        OpenColorPicker(_fgHex, hex =>
        {
            _fgHex = hex;
            FgHexBox.TextChanged -= FgHexBox_TextChanged;
            FgHexBox.Text = hex;
            FgHexBox.TextChanged += FgHexBox_TextChanged;
            RefreshFgPreview();
            NotifyChanged();
        });
    }

    private void OpenColorPicker(string hex, Action<string> onChanged)
    {
        var color = HexToColor(hex);
        var picker = new ColorPickerWindow(color, c =>
        {
            onChanged($"{c.R:X2}{c.G:X2}{c.B:X2}");
        }) { Owner = this };
        picker.ShowDialog();
    }

    private static Color HexToColor(string hex)
    {
        hex = NormalizeHex(hex, 6);
        try
        {
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return Color.FromRgb(r, g, b);
        }
        catch { return Colors.White; }
    }

    // ── Visibility checkboxes ───────────────────────────────────────
    private void Visibility_Changed(object sender, RoutedEventArgs e) => NotifyChanged();

    // ── Run at startup ─────────────────────────────────────────────
    private void RunAtStartup_Changed(object sender, RoutedEventArgs e) => NotifyChanged();

    // ── Zoom slider ─────────────────────────────────────────────────
    private void ZoomSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        _zoomPercent = Math.Round(e.NewValue);
        if (ZoomLabel != null)
            ZoomLabel.Text = $"{(int)_zoomPercent}%";
        NotifyChanged();
    }

    // ── Live apply ────────────────────────────────────────────────
    private AppSettings BuildCurrentSettings()
    {
        var alpha = (byte)Math.Round(_bgOpacity / 100.0 * 255);
        return new AppSettings
        {
            BackgroundColor  = $"#{alpha:X2}{NormalizeHex(_bgHex, 6)}",
            PriceFontColor   = $"#{NormalizeHex(_fgHex, 6)}",
            LabelFontColor   = $"#BB{NormalizeHex(_fgHex, 6)}",
            ShowNavigation   = ChkNavigation.IsChecked == true,
            ShowSymbolLabel  = ChkSymbolLabel.IsChecked == true,
            ShowChange24h    = ChkChange24h.IsChecked == true,
            ShowSparkline    = ChkSparkline.IsChecked == true,
            RunAtStartup     = ChkRunAtStartup.IsChecked == true,
            ZoomScale        = _zoomPercent / 100.0
        };
    }

    private void NotifyChanged()
    {
        if (!IsLoaded) return;
        Result = BuildCurrentSettings();
        _onChanged?.Invoke(Result);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    // ── Helpers ────────────────────────────────────────────────────
    private static SolidColorBrush? BrushFromHex(string hex, byte alpha)
    {
        try
        {
            hex = NormalizeHex(hex, 6);
            if (hex.Length != 6) return null;
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        }
        catch { return null; }
    }

    private static string NormalizeHex(string hex, int expectedLen)
    {
        hex = hex.TrimStart('#').ToUpperInvariant();
        // Pad or trim to expected length
        if (hex.Length < expectedLen)
            hex = hex.PadRight(expectedLen, '0');
        else if (hex.Length > expectedLen)
            hex = hex[..expectedLen];
        return hex;
    }

    private static (string hex, int opacity) SplitBgColor(string stored)
    {
        stored = stored.TrimStart('#');
        // #AARRGGBB (8 chars) → split alpha
        if (stored.Length == 8)
        {
            var alpha = Convert.ToByte(stored[..2], 16);
            var opacity = (int)Math.Round(alpha / 255.0 * 100);
            return (stored[2..], opacity);
        }
        // #RRGGBB (6 chars)
        return (stored, 80);
    }
}
