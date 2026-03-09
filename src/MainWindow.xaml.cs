using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PcUsageTimer.Network;

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
    private bool _lockScreenActive;
    private LockScreenWindow? _activeLockScreen;

    private RemoteLockServer? _remoteLockServer;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _forceClose;

    private const int RemotePort = 7742;
    private const string AutoStartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "PcUsageTimer";

    private static readonly string PinFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PcUsageTimer", "pin.dat");

    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
        SetupTrayIcon();
        LoadAutoStartSetting();
        StartRemoteServer();
    }

    // ── Tray Icon ──────────────────────────────────────────────

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "PC Usage Timer",
            Visible = true
        };

        // Use the embedded icon from the exe, fall back to system icon
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch
        {
            _trayIcon.Icon = SystemIcons.Application;
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            if (_timerRunning)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Cannot exit while timer is running. Stop the timer first.",
                    "PC Usage Timer",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }
            _forceClose = true;
            Close();
        });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_timerRunning)
        {
            e.Cancel = true;
            return;
        }

        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Actually closing — clean up
        _remoteLockServer?.Dispose();
        _trayIcon?.Dispose();
        Application.Current.Shutdown();
    }

    // ── PIN Persistence ──────────────────────────────────────

    private string LoadSavedPin()
    {
        try
        {
            if (System.IO.File.Exists(PinFilePath))
                return System.IO.File.ReadAllText(PinFilePath).Trim();
        }
        catch { }
        return "";
    }

    private void SavePin(string pin)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(PinFilePath)!;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(PinFilePath, pin);
        }
        catch { }
    }

    // ── Remote Lock Server ─────────────────────────────────────

    private void StartRemoteServer()
    {
        _pin = LoadSavedPin();
        _remoteLockServer = new RemoteLockServer(RemotePort, _pin, GetTimerStatus);
        _remoteLockServer.LockRequested += OnRemoteLockRequested;
        _remoteLockServer.TimerStartRequested += OnRemoteTimerStartRequested;
        _remoteLockServer.UnlockRequested += OnRemoteUnlockRequested;
        _remoteLockServer.ExtendRequested += OnRemoteExtendRequested;

        try
        {
            _remoteLockServer.Start();
            var url = _remoteLockServer.ServerUrl ?? "unavailable";
            RemoteLockStatusText.Text = string.IsNullOrEmpty(_pin)
                ? "Start a timer once to set your PIN, then open on your phone:"
                : "Open on your phone:";
            RemoteLockUrlText.Text = url;
        }
        catch (Exception ex)
        {
            RemoteLockStatusText.Text = $"Remote lock unavailable: {ex.Message}";
            RemoteLockUrlText.Text = "";
        }
    }

    private TimerStatus GetTimerStatus()
    {
        // Called from background thread — read volatile state
        return new TimerStatus(_timerRunning, _timerRunning ? _remaining : null, _lockScreenActive);
    }

    private void OnRemoteLockRequested()
    {
        Dispatcher.Invoke(() =>
        {
            if (_lockScreenActive) return;

            if (_timerRunning)
            {
                _timer?.Stop();
                _remaining = TimeSpan.Zero;
                UpdateCountdownDisplay();
            }

            ShowLockScreen();
        });
    }

    private void OnRemoteTimerStartRequested(int minutes)
    {
        Dispatcher.Invoke(() =>
        {
            if (_timerRunning || _lockScreenActive) return;

            _totalMinutes = minutes;
            _remaining = TimeSpan.FromMinutes(minutes);
            _notified3Min = false;
            _notified1Min = false;

            SetupPanel.Visibility = Visibility.Collapsed;
            TimerPanel.Visibility = Visibility.Visible;
            TimerRemoteLockUrl.Text = _remoteLockServer?.ServerUrl ?? "";
            UpdateCountdownDisplay();

            _timerRunning = true;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        });
    }

    private void OnRemoteUnlockRequested()
    {
        Dispatcher.Invoke(() =>
        {
            if (!_lockScreenActive || _activeLockScreen == null) return;
            _activeLockScreen.RemoteUnlock();
        });
    }

    private void OnRemoteExtendRequested(int minutes)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_timerRunning) return;
            _remaining += TimeSpan.FromMinutes(minutes);
            UpdateCountdownDisplay();
        });
    }

    // ── Auto-Start ─────────────────────────────────────────────

    private void LoadAutoStartSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, false);
            AutoStartCheckBox.IsChecked = key?.GetValue(AutoStartValueName) != null;
        }
        catch
        {
            AutoStartCheckBox.IsChecked = false;
        }
    }

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true);
            if (key == null) return;

            if (AutoStartCheckBox.IsChecked == true)
            {
                var exePath = Environment.ProcessPath ?? "";
                key.SetValue(AutoStartValueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(AutoStartValueName, false);
            }
        }
        catch { }
    }

    // ── Timer Setup & Control ──────────────────────────────────

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

        if (!int.TryParse(CustomMinutesBox.Text, out var minutes) || minutes < 1 || minutes > 480)
        {
            ErrorText.Text = "Enter a valid duration (1–480 minutes).";
            return;
        }

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

        // Update the remote server's PIN and persist it
        _remoteLockServer?.UpdatePin(_pin);
        SavePin(_pin);

        SetupPanel.Visibility = Visibility.Collapsed;
        TimerPanel.Visibility = Visibility.Visible;
        TimerRemoteLockUrl.Text = _remoteLockServer?.ServerUrl ?? "";
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
        _lockScreenActive = true;
        _activeLockScreen = new LockScreenWindow(_pin);
        _activeLockScreen.Show();

        _activeLockScreen.Closed += (_, _) =>
        {
            _lockScreenActive = false;
            _activeLockScreen = null;
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
