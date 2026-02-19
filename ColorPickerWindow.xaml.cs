using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryptoPrice;

public partial class ColorPickerWindow : Window
{
    private const int SvSize = 256;
    private const int HueBarWidth = 24;
    private const int HueBarHeight = 256;

    private double _hue;        // 0–360
    private double _saturation; // 0–1
    private double _value;      // 0–1

    private bool _suppressHexEvent;

    private readonly WriteableBitmap _svBitmap;
    private readonly WriteableBitmap _hueBitmap;
    private readonly Action<Color>? _onColorChanged;

    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(Color initial, Action<Color>? onColorChanged = null)
    {
        InitializeComponent();
        _onColorChanged = onColorChanged;

        _svBitmap = new WriteableBitmap(SvSize, SvSize, 96, 96, PixelFormats.Bgra32, null);
        _hueBitmap = new WriteableBitmap(HueBarWidth, HueBarHeight, 96, 96, PixelFormats.Bgra32, null);

        SvImage.Source = _svBitmap;
        HueImage.Source = _hueBitmap;

        ColorToHsv(initial, out _hue, out _saturation, out _value);
        SelectedColor = initial;

        RenderHueBar();
        RenderSvCanvas();
        UpdatePreview();
        UpdateCursors();
    }

    // ── SV Canvas mouse handling ────────────────────────────────────

    private void SvCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        UpdateSvFromMouse(e.GetPosition(SvCanvas));
        SvCanvas.CaptureMouse();
    }

    private void SvCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        => SvCanvas.ReleaseMouseCapture();

    private void UpdateSvFromMouse(Point pos)
    {
        _saturation = Math.Clamp(pos.X / SvSize, 0, 1);
        _value = Math.Clamp(1.0 - pos.Y / SvSize, 0, 1);
        ApplyHsvChange(renderSv: false);
    }

    // ── Hue bar mouse handling ──────────────────────────────────────

    private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        UpdateHueFromMouse(e.GetPosition(HueCanvas));
        HueCanvas.CaptureMouse();
    }

    private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            UpdateHueFromMouse(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        => HueCanvas.ReleaseMouseCapture();

    private void UpdateHueFromMouse(Point pos)
    {
        _hue = Math.Clamp(pos.Y / HueBarHeight, 0, 0.9999) * 360;
        ApplyHsvChange(renderSv: true);
    }

    // ── Hex box ─────────────────────────────────────────────────────

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressHexEvent) return;

        var text = HexBox.Text.TrimStart('#');
        if (text.Length != 6) return;

        try
        {
            var r = Convert.ToByte(text[..2], 16);
            var g = Convert.ToByte(text[2..4], 16);
            var b = Convert.ToByte(text[4..6], 16);
            var color = Color.FromRgb(r, g, b);

            ColorToHsv(color, out _hue, out _saturation, out _value);
            SelectedColor = color;

            RenderSvCanvas();
            UpdateCursors();
            ColorPreview.Background = new SolidColorBrush(color);
            _onColorChanged?.Invoke(color);
        }
        catch { /* partial / invalid input */ }
    }

    // ── Core update ─────────────────────────────────────────────────

    private void ApplyHsvChange(bool renderSv)
    {
        SelectedColor = HsvToRgb(_hue, _saturation, _value);
        if (renderSv) RenderSvCanvas();
        UpdateCursors();
        UpdatePreview();
        _onColorChanged?.Invoke(SelectedColor);
    }

    private void UpdatePreview()
    {
        ColorPreview.Background = new SolidColorBrush(SelectedColor);

        _suppressHexEvent = true;
        HexBox.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        _suppressHexEvent = false;
    }

    private void UpdateCursors()
    {
        Canvas.SetLeft(SvCursor, _saturation * SvSize - 7);
        Canvas.SetTop(SvCursor, (1 - _value) * SvSize - 7);

        Canvas.SetTop(HueCursor, _hue / 360.0 * HueBarHeight - 3);
    }

    // ── Bitmap rendering ────────────────────────────────────────────

    private void RenderSvCanvas()
    {
        var pixels = new byte[SvSize * SvSize * 4];
        for (var y = 0; y < SvSize; y++)
        {
            var val = 1.0 - (double)y / (SvSize - 1);
            for (var x = 0; x < SvSize; x++)
            {
                var sat = (double)x / (SvSize - 1);
                var c = HsvToRgb(_hue, sat, val);
                var idx = (y * SvSize + x) * 4;
                pixels[idx] = c.B;
                pixels[idx + 1] = c.G;
                pixels[idx + 2] = c.R;
                pixels[idx + 3] = 255;
            }
        }
        _svBitmap.WritePixels(new Int32Rect(0, 0, SvSize, SvSize), pixels, SvSize * 4, 0);
    }

    private void RenderHueBar()
    {
        var pixels = new byte[HueBarWidth * HueBarHeight * 4];
        for (var y = 0; y < HueBarHeight; y++)
        {
            var hue = (double)y / (HueBarHeight - 1) * 360;
            var c = HsvToRgb(hue, 1, 1);
            for (var x = 0; x < HueBarWidth; x++)
            {
                var idx = (y * HueBarWidth + x) * 4;
                pixels[idx] = c.B;
                pixels[idx + 1] = c.G;
                pixels[idx + 2] = c.R;
                pixels[idx + 3] = 255;
            }
        }
        _hueBitmap.WritePixels(new Int32Rect(0, 0, HueBarWidth, HueBarHeight), pixels, HueBarWidth * 4, 0);
    }

    // ── HSV ↔ RGB ───────────────────────────────────────────────────

    private static Color HsvToRgb(double h, double s, double v)
    {
        h %= 360;
        if (h < 0) h += 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;

        double r, g, b;
        switch (h)
        {
            case < 60:  r = c; g = x; b = 0; break;
            case < 120: r = x; g = c; b = 0; break;
            case < 180: r = 0; g = c; b = x; break;
            case < 240: r = 0; g = x; b = c; break;
            case < 300: r = x; g = 0; b = c; break;
            default:    r = c; g = 0; b = x; break;
        }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static void ColorToHsv(Color col, out double h, out double s, out double v)
    {
        double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0) { h = 0; return; }

        if (max == r)      h = 60 * ((g - b) / delta % 6);
        else if (max == g) h = 60 * ((b - r) / delta + 2);
        else               h = 60 * ((r - g) / delta + 4);

        if (h < 0) h += 360;
    }
}
