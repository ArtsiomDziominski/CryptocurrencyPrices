using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CryptoPrice.Controls;

/// <summary>
/// Renders a list of prices as a small line chart (sparkline).
/// </summary>
public partial class SparklineControl : UserControl
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(76, 175, 80));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(244, 67, 54));

    private List<decimal>? _prices;

    public SparklineControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Render();
    }

    /// <summary>
    /// Provide new price data and redraw the sparkline.
    /// </summary>
    public void UpdatePrices(List<decimal> prices)
    {
        _prices = prices;
        Render();
    }

    private void Render()
    {
        SparklineLine.Points.Clear();

        if (_prices is not { Count: >= 2 }) return;

        var w = ChartCanvas.ActualWidth;
        var h = ChartCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var min = _prices[0];
        var max = _prices[0];
        foreach (var p in _prices)
        {
            if (p < min) min = p;
            if (p > max) max = p;
        }

        var range = max - min;
        if (range == 0) range = 1;

        const double padY = 2;
        var drawH = h - padY * 2;
        var step = w / (_prices.Count - 1);

        var points = new PointCollection(_prices.Count);
        for (var i = 0; i < _prices.Count; i++)
        {
            var x = i * step;
            var y = padY + (double)(1 - (_prices[i] - min) / range) * drawH;
            points.Add(new Point(x, y));
        }

        SparklineLine.Points = points;
        SparklineLine.Stroke = _prices[^1] >= _prices[0] ? GreenBrush : RedBrush;
    }
}
