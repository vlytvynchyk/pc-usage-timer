using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PcUsageTimer.Audio;

namespace PcUsageTimer;

public partial class LockScreenWindow : Window
{
    private bool _unlocked;
    private int _failedAttempts;
    private DateTime _lockoutUntil = DateTime.MinValue;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private DispatcherTimer? _focusTimer;

    // Virtual screen bounds (covers all monitors)
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    // Low-level keyboard hook
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Taskbar hide/show
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern int ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    // Media pause
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

    public LockScreenWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Cover all monitors
        Left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        Top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        Width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        Height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        WindowState = WindowState.Normal;

        // Hide taskbar
        HideTaskbar();

        // Mute system audio and pause media playback
        AudioManager.Mute();
        keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 2, UIntPtr.Zero);

        // Install low-level keyboard hook to block Win key, Alt+Tab, etc.
        _hookProc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);

        // Periodic focus enforcement — catches any brief taskbar/app activation
        _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _focusTimer.Tick += (_, _) =>
        {
            if (!_unlocked)
            {
                Topmost = true;
                Activate();
            }
        };
        _focusTimer.Start();

        // Focus the PIN entry
        PinEntry.Focus();
    }

    private static void HideTaskbar()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero) ShowWindow(taskbar, SW_HIDE);

        // Also hide the secondary taskbar on multi-monitor setups
        var secondaryTaskbar = FindWindow("Shell_SecondaryTrayWnd", null);
        if (secondaryTaskbar != IntPtr.Zero) ShowWindow(secondaryTaskbar, SW_HIDE);
    }

    private static void ShowTaskbar()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero) ShowWindow(taskbar, SW_SHOW);

        var secondaryTaskbar = FindWindow("Shell_SecondaryTrayWnd", null);
        if (secondaryTaskbar != IntPtr.Zero) ShowWindow(secondaryTaskbar, SW_SHOW);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            var msg = (int)wParam;

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (vkCode == 0x5B || vkCode == 0x5C) // LWin, RWin
                    return (IntPtr)1;
                if (vkCode == 0x09 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) // Alt+Tab
                    return (IntPtr)1;
                if (vkCode == 0x1B && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) // Alt+Esc
                    return (IntPtr)1;
                if (vkCode == 0x1B && (Keyboard.Modifiers & ModifierKeys.Control) != 0) // Ctrl+Esc
                    return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey == Key.F4)
            e.Handled = true;
        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            e.Handled = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_unlocked)
        {
            e.Cancel = true;
            return;
        }
        Cleanup();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_unlocked)
        {
            // Immediately reclaim focus
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                Topmost = true;
                Activate();
                Focus();
                PinEntry.Focus();
            });
        }
    }

    private void PinEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TryUnlock();
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        TryUnlock();
    }

    public void RemoteUnlock()
    {
        _unlocked = true;
        Cleanup();
        Close();
    }

    private void Cleanup()
    {
        _focusTimer?.Stop();

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        ShowTaskbar();
        AudioManager.Unmute();
    }

    private void TryUnlock()
    {
        if (DateTime.UtcNow < _lockoutUntil)
        {
            var wait = (int)Math.Ceiling((_lockoutUntil - DateTime.UtcNow).TotalSeconds);
            ErrorText.Text = $"Too many attempts. Wait {wait}s.";
            PinEntry.Password = "";
            PinEntry.Focus();
            return;
        }

        if (PinManager.Validate(PinEntry.Password))
        {
            _failedAttempts = 0;
            _unlocked = true;
            Close();
        }
        else
        {
            _failedAttempts++;
            if (_failedAttempts >= 3)
            {
                var delay = _failedAttempts >= 9 ? 60 : _failedAttempts >= 6 ? 15 : 5;
                _lockoutUntil = DateTime.UtcNow.AddSeconds(delay);
                ErrorText.Text = $"Wrong PIN. Locked for {delay}s.";
            }
            else
            {
                ErrorText.Text = "Wrong PIN. Try again.";
            }
            PinEntry.Password = "";
            PinEntry.Focus();

            // Shake the error text instead of the window (don't move the window!)
            var origMargin = ErrorText.Margin;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            int shakeCount = 0;
            timer.Tick += (_, _) =>
            {
                ErrorText.Margin = new Thickness(
                    origMargin.Left + (shakeCount % 2 == 0 ? 8 : -8),
                    origMargin.Top, origMargin.Right, origMargin.Bottom);
                shakeCount++;
                if (shakeCount >= 6)
                {
                    ErrorText.Margin = origMargin;
                    timer.Stop();
                }
            };
            timer.Start();
        }
    }
}
