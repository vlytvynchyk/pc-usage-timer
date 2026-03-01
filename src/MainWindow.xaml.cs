using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PcUsageTimer;

public partial class MainWindow : Window
{
    private DispatcherTimer? _timer;
    private TimeSpan _remaining;
    private int _totalMinutes;
    private string _pin = "";
    private bool _notified3Min;
    private bool _notified1Min;

    private bool _timerRunning;

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_timerRunning)
        {
            e.Cancel = true;
        }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag)
        {
            CustomMinutesBox.Text = tag;
        }
    }

    private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        // Validate minutes
        if (!int.TryParse(CustomMinutesBox.Text, out var minutes) || minutes < 1 || minutes > 480)
        {
            ErrorText.Text = "Enter a valid duration (1–480 minutes).";
            return;
        }

        // Validate PIN
        var pin = PinBox.Password;
        var pinConfirm = PinConfirmBox.Password;

        if (pin.Length != 4 || !Regex.IsMatch(pin, @"^\d{4}$"))
        {
            ErrorText.Text = "PIN must be exactly 4 digits.";
            return;
        }

        if (pin != pinConfirm)
        {
            ErrorText.Text = "PINs do not match.";
            return;
        }

        _pin = pin;
        _totalMinutes = minutes;
        _remaining = TimeSpan.FromMinutes(minutes);
        _notified3Min = false;
        _notified1Min = false;

        // Switch to timer view
        SetupPanel.Visibility = Visibility.Collapsed;
        TimerPanel.Visibility = Visibility.Visible;
        UpdateCountdownDisplay();

        _timerRunning = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _remaining -= TimeSpan.FromSeconds(1);

        if (_remaining <= TimeSpan.Zero)
        {
            _remaining = TimeSpan.Zero;
            _timer?.Stop();
            UpdateCountdownDisplay();
            ShowLockScreen();
            return;
        }

        UpdateCountdownDisplay();

        // Show notifications at 3 min and 1 min (only if total > 5 min)
        if (_totalMinutes > 5)
        {
            if (!_notified3Min && _remaining.TotalSeconds <= 180 && _remaining.TotalSeconds > 179)
            {
                _notified3Min = true;
                ShowNotification("3 minutes left", "Screen time ends in 3 minutes!");
            }
            if (!_notified1Min && _remaining.TotalSeconds <= 60 && _remaining.TotalSeconds > 59)
            {
                _notified1Min = true;
                ShowNotification("1 minute left", "Screen time ends in 1 minute!");
            }
        }
    }

    private void ShowNotification(string title, string message)
    {
        var notification = new NotificationWindow(title, message);
        notification.Show();
    }

    private void UpdateCountdownDisplay()
    {
        CountdownText.Text = _remaining.TotalHours >= 1
            ? _remaining.ToString(@"h\:mm\:ss")
            : _remaining.ToString(@"mm\:ss");
    }

    private void ShowLockScreen()
    {
        var lockScreen = new LockScreenWindow(_pin);
        lockScreen.Show();

        // When lock screen is closed (PIN entered), return to setup
        lockScreen.Closed += (_, _) =>
        {
            _timerRunning = false;
            SetupPanel.Visibility = Visibility.Visible;
            TimerPanel.Visibility = Visibility.Collapsed;
            PinBox.Password = "";
            PinConfirmBox.Password = "";
            CustomMinutesBox.Text = "";
            Show();
        };

        Hide();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Stopping requires the PIN too
        var dialog = new PinPromptDialog(_pin) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _timer?.Stop();
            _timerRunning = false;
            SetupPanel.Visibility = Visibility.Visible;
            TimerPanel.Visibility = Visibility.Collapsed;
        }
    }
}
