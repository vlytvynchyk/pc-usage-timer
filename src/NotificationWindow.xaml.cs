using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PcUsageTimer;

public partial class NotificationWindow : Window
{
    private readonly DispatcherTimer _dismissTimer;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public NotificationWindow(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _dismissTimer.Tick += (_, _) => FadeOutAndClose();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Position at top-right of primary screen
        var screenW = GetSystemMetrics(SM_CXSCREEN);
        Left = screenW - ActualWidth - 10;
        Top = 20;

        // Fade in
        Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        BeginAnimation(OpacityProperty, fadeIn);

        _dismissTimer.Start();
    }

    private void FadeOutAndClose()
    {
        _dismissTimer.Stop();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
