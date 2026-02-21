using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static CryptoPrice.Constants;

namespace CryptoPrice;

public partial class AlertToast : Window
{
    public AlertToast(PriceAlert alert, decimal currentPrice)
    {
        InitializeComponent();

        var symbol = FormatSymbol(alert.Symbol);
        var accentColor = currentPrice >= alert.TargetPrice
            ? Color.FromRgb(76, 175, 80)
            : Color.FromRgb(244, 67, 54);

        TitleText.Text = $"{symbol} — Price Alert";
        MessageText.Text = $"Price crossed {alert.TargetPrice:C2} (now {currentPrice:C2})";
        ToastBorder.BorderBrush = new SolidColorBrush(accentColor);
        AlertIcon.Foreground = new SolidColorBrush(accentColor);

        PositionOnScreen();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AlertToastDurationMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FadeOutAndClose();
        };
        timer.Start();
    }

    private void PositionOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void FadeOutAndClose()
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }

    private void Window_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Close();
    }

    private static string FormatSymbol(string symbol)
    {
        var s = symbol.ToUpperInvariant();
        if (s.EndsWith("USDT")) return s[..^4];
        if (s.EndsWith("USD")) return s[..^3];
        return s;
    }
}
