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
        WindowState = WindowState.Normal; // Use manual sizing instead of Maximized

        // Mute system audio
        AudioManager.Mute();

        // Install low-level keyboard hook to block Win key, Alt+Tab, etc.
        _hookProc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);

        // Focus the PIN entry
        PinEntry.Focus();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            var msg = (int)wParam;

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                // Block: Win keys (91, 92), Alt+Tab, Alt+Esc, Ctrl+Esc
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
        // Block Alt+F4
        if (e.Key == Key.System && e.SystemKey == Key.F4)
        {
            e.Handled = true;
        }
        // Block Alt+Tab at WPF level too
        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_unlocked)
        {
            e.Cancel = true;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_unlocked)
        {
            // Force back to foreground
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                Topmost = true;
                Activate();
                Focus();
            });
        }
    }

    private void PinEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryUnlock();
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        TryUnlock();
    }

    public void RemoteUnlock()
    {
        _unlocked = true;
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        AudioManager.Unmute();
        Close();
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

            // Remove keyboard hook
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            // Unmute audio
            AudioManager.Unmute();

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

            // Shake animation
            var left = Left;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            int shakeCount = 0;
            timer.Tick += (_, _) =>
            {
                Left = left + (shakeCount % 2 == 0 ? 10 : -10);
                shakeCount++;
                if (shakeCount >= 6)
                {
                    Left = left;
                    timer.Stop();
                }
            };
            timer.Start();
        }
    }
}
