using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PcUsageTimer.Network;
using QRCoder;

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
    private bool _graceActive;
    private int _graceSeconds;
    private NotificationWindow? _graceNotification;
    private LockScreenWindow? _activeLockScreen;

    private RemoteLockServer? _remoteLockServer;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private DarkContextMenuStrip? _trayMenu;
    private System.Windows.Forms.ToolStripMenuItem? _statusMenuItem;
    private System.Windows.Forms.ToolStripMenuItem? _startWithWindowsMenuItem;
    private bool _forceClose;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly Image BlueDot = CreateStatusDot(Color.FromArgb(58, 130, 246));
    private static readonly Image GrayDot = CreateStatusDot(Color.FromArgb(130, 130, 130));
    private static readonly Image CheckImg = CreateCheckImage(Color.FromArgb(100, 180, 255));
    private static readonly Image Spacer = new Bitmap(32, 32, PixelFormat.Format32bppArgb);

    private const int RemotePort = 7742;
    private const string AutoStartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "PcUsageTimer";


    public MainWindow()
    {
        InitializeComponent();
        CustomMinutesBox.Text = "20";
        Closing += MainWindow_Closing;
        SetupTrayIcon();
        LoadAutoStartSetting();
        StartRemoteServer();
    }

    // ── Tray Icon ──────────────────────────────────────────────

    private void SetupTrayIcon()
    {
        _statusMenuItem = new System.Windows.Forms.ToolStripMenuItem("Idle")
        {
            Enabled = false,
            Image = GrayDot,
            Padding = new System.Windows.Forms.Padding(0, 6, 0, 6)
        };

        _startWithWindowsMenuItem = new System.Windows.Forms.ToolStripMenuItem(
            "Start with Windows", GetCheckImage(AutoStartCheckBox.IsChecked == true), (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AutoStartCheckBox.IsChecked = !(AutoStartCheckBox.IsChecked == true);
                });
                _startWithWindowsMenuItem!.Image = GetCheckImage(AutoStartCheckBox.IsChecked == true);
            })
        {
            Padding = new System.Windows.Forms.Padding(0, 4, 0, 4)
        };

        _trayMenu = new DarkContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = true,
            ShowCheckMargin = false,
            ImageScalingSize = new System.Drawing.Size(16, 16),
            BackColor = Color.FromArgb(44, 44, 44),
            ForeColor = Color.FromArgb(240, 240, 240),
            MinimumSize = new System.Drawing.Size(0, 0),
            AutoSize = true
        };

        _trayMenu.Items.Add(_statusMenuItem);
        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayMenu.Items.Add(CreateTrayMenuItem("Open", (_, _) => ShowFromTray()));
        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayMenu.Items.Add(_startWithWindowsMenuItem);
        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayMenu.Items.Add(CreateTrayMenuItem("Quit", (_, _) =>
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
            CloseTrayMenu();
            _forceClose = true;
            Close();
        }));

        _trayMenu.Opened += (_, _) =>
        {
            if (_trayMenu.AutoSize)
            {
                _trayMenu.AutoSize = false;
                _trayMenu.Width = _trayMenu.Width;
            }
            _trayMenu.AutoClose = false;
        };
        _trayMenu.LostFocus += (_, _) => CloseTrayMenu();

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "PC Usage Timer",
            Visible = true,
            Icon = IconGenerator.IdleIcon
        };
        _trayIcon.MouseClick += OnTrayMouseClick;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void OnTrayMouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != System.Windows.Forms.MouseButtons.Right || _trayMenu == null) return;

        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;

        _trayMenu.PerformLayout();
        var preferred = _trayMenu.GetPreferredSize(System.Drawing.Size.Empty);
        int menuW = Math.Max(preferred.Width, _trayMenu.MinimumSize.Width);
        int menuH = preferred.Height;

        int x = cursor.X - menuW;
        int y = screen.Bottom - menuH;
        if (x < screen.Left) x = screen.Left;
        if (x + menuW > screen.Right) x = screen.Right - menuW;

        SetForegroundWindow(_trayMenu.Handle);
        _trayMenu.Show(x, y);
    }

    private void CloseTrayMenu()
    {
        if (_trayMenu == null) return;
        _trayMenu.AutoClose = true;
        _trayMenu.Close();
    }

    private static System.Windows.Forms.ToolStripMenuItem CreateTrayMenuItem(string text, EventHandler handler)
    {
        return new System.Windows.Forms.ToolStripMenuItem(text, Spacer, handler)
        {
            Padding = new System.Windows.Forms.Padding(0, 4, 0, 4)
        };
    }

    private static Image GetCheckImage(bool isChecked) => isChecked ? CheckImg : Spacer;

    private static Image CreateStatusDot(Color color)
    {
        var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 6, 6, 20, 20);
        return bmp;
    }

    private static Image CreateCheckImage(Color color)
    {
        var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.Clear(Color.Transparent);
        using var pen = new Pen(color, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [new PointF(6, 16), new PointF(13, 24), new PointF(26, 8)]);
        return bmp;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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
        _trayIcon!.Visible = false;
        _trayIcon?.Dispose();
        _trayMenu?.Dispose();
        Application.Current.Shutdown();
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon != null)
            _trayIcon.Icon = _timerRunning ? IconGenerator.ActiveIcon : IconGenerator.IdleIcon;
        if (_statusMenuItem != null)
        {
            _statusMenuItem.Text = _timerRunning ? "Timer running" : "Idle";
            _statusMenuItem.Image = _timerRunning ? BlueDot : GrayDot;
        }
    }

    // ── Remote Lock Server ─────────────────────────────────────

    private void StartRemoteServer()
    {
        PinManager.Load();
        _remoteLockServer = new RemoteLockServer(RemotePort, GetTimerStatus);
        _remoteLockServer.LockRequested += OnRemoteLockRequested;
        _remoteLockServer.TimerStartRequested += OnRemoteTimerStartRequested;
        _remoteLockServer.UnlockRequested += OnRemoteUnlockRequested;
        _remoteLockServer.ExtendRequested += OnRemoteExtendRequested;

        try
        {
            _remoteLockServer.Start();
            var url = _remoteLockServer.ServerUrl ?? "unavailable";
            RemoteLockStatusText.Text = !PinManager.HasPin
                ? "Start a timer once to set your PIN, then open on your phone:"
                : "Open on your phone:";
            RemoteLockUrlText.Text = url;
            GenerateQrCode(url);
        }
        catch (Exception ex)
        {
            RemoteLockStatusText.Text = $"Remote lock unavailable: {ex.Message}";
            RemoteLockUrlText.Text = "";
        }
    }

    private void GenerateQrCode(string url)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            using var qrCode = new BitmapByteQRCode(qrData);
            var pngBytes = qrCode.GetGraphic(5, [230, 235, 245], [27, 30, 36]);

            var image = new System.Windows.Media.Imaging.BitmapImage();
            using var ms = new System.IO.MemoryStream(pngBytes);
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            QrCodeImage.Source = image;
        }
        catch { }
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
            UpdateTrayIcon();
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

    private void RemoteLockHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RemoteLockDetails.Visibility == Visibility.Collapsed)
        {
            RemoteLockDetails.Visibility = Visibility.Visible;
            RemoteLockChevron.Text = " \u25BC";
        }
        else
        {
            RemoteLockDetails.Visibility = Visibility.Collapsed;
            RemoteLockChevron.Text = " \u25B6";
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

        // Sync tray menu
        if (_startWithWindowsMenuItem != null)
            _startWithWindowsMenuItem.Image = GetCheckImage(AutoStartCheckBox.IsChecked == true);
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

        // Persist hashed PIN
        PinManager.Set(_pin);

        SetupPanel.Visibility = Visibility.Collapsed;
        TimerPanel.Visibility = Visibility.Visible;
        TimerRemoteLockUrl.Text = _remoteLockServer?.ServerUrl ?? "";
        UpdateCountdownDisplay();

        _timerRunning = true;
        UpdateTrayIcon();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private const int GracePeriodSeconds = 30;

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_graceActive)
        {
            _graceSeconds--;
            CountdownText.Text = $"-{_graceSeconds}s";
            if (_graceSeconds <= 0)
            {
                _graceActive = false;
                _graceNotification?.Close();
                _graceNotification = null;
                _timer?.Stop();
                ShowLockScreen();
            }
            return;
        }

        _remaining -= TimeSpan.FromSeconds(1);

        if (_remaining <= TimeSpan.Zero)
        {
            _remaining = TimeSpan.Zero;
            UpdateCountdownDisplay();

            if (_totalMinutes > 1)
            {
                // Start grace period
                _graceActive = true;
                _graceSeconds = GracePeriodSeconds;
                _graceNotification = new NotificationWindow("Time's up!", "Save your work — locking in 30 seconds");
                _graceNotification.SetPersistent();
                _graceNotification.Show();
            }
            else
            {
                _timer?.Stop();
                ShowLockScreen();
            }
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
                System.Media.SystemSounds.Exclamation.Play();
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
        _activeLockScreen = new LockScreenWindow();
        _activeLockScreen.Show();

        _activeLockScreen.Closed += (_, _) =>
        {
            _lockScreenActive = false;
            _activeLockScreen = null;
            _timerRunning = false;
            UpdateTrayIcon();
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
        var dialog = new PinPromptDialog() { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _timer?.Stop();
            _timerRunning = false;
            UpdateTrayIcon();
            SetupPanel.Visibility = Visibility.Visible;
            TimerPanel.Visibility = Visibility.Collapsed;
        }
    }
}
