# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PC Usage Timer is a Windows parental control WPF application (C# / .NET 9.0) that enforces screen-time limits with PIN protection. It produces a single self-contained executable (~60MB) targeting win-x64.

## Build Commands

All commands run from the `src/` directory:

```bash
dotnet run                              # Dev run
dotnet build -c Release                 # Release build
dotnet publish -c Release -o ./publish  # Publish single-file exe
```

No test framework or linter is configured.

## Architecture

The app is a single WPF project with no external NuGet dependencies. All source lives under `src/`.

**Two lock triggers:** timer expiry and remote lock from phone via LAN.

**Flow:** MainWindow (setup → countdown) → NotificationWindow (warnings at 3min/1min) → LockScreenWindow (fullscreen lock on all monitors) → PIN unlock. Remote lock bypasses the timer and locks immediately.

**Key files:**
- `MainWindow.xaml/.cs` — Setup UI, timer countdown via `DispatcherTimer`, tray icon, auto-start, remote server lifecycle
- `LockScreenWindow.xaml/.cs` — Fullscreen lock covering all virtual monitors, low-level keyboard hook (blocks Win keys, Alt+Tab, Alt+Esc, Ctrl+Esc), forced reactivation on deactivation
- `NotificationWindow.xaml/.cs` — Toast-style notification with fade animations, auto-dismiss
- `PinPromptDialog.xaml/.cs` — Modal 4-digit PIN prompt for stopping the timer early
- `Audio/AudioManager.cs` — Windows Core Audio API (COM interop) for mute/unmute
- `Network/RemoteLockServer.cs` — Always-on `HttpListener` server (port 7742) serving a mobile-friendly HTML page and PIN-protected `/lock` API
- `Network/LanHelper.cs` — Detects LAN IPv4 address for server URL display

**System tray:** App minimizes to tray on close/minimize. Always-on HTTP server runs regardless of timer state. Tray right-click menu: Open / Exit. Exit blocked while timer is running.

**Auto-start:** Optional registry entry (`HKCU\...\Run`) launches with `--minimized` flag. `App.xaml.cs` handles startup mode; `ShutdownMode.OnExplicitShutdown` keeps app alive when window is hidden.

**PIN persistence:** Saved to `%LOCALAPPDATA%\PcUsageTimer\pin.dat`. Loaded on startup so remote lock works immediately after auto-start.

**Anti-bypass techniques:** Global low-level keyboard hook via P/Invoke, virtual screen coverage for multi-monitor, window focus reactivation, close prevention. Note: Ctrl+Alt+Del cannot be blocked (kernel-level).

## Code Conventions

- Namespace: `PcUsageTimer`
- Nullable reference types enabled
- Catppuccin Mocha color theme (background `#1E1E2E`, blue `#89B4FA`, green `#A6E3A1`, red `#F38BA8`)
- Heavy P/Invoke and COM interop usage for Windows APIs
- No external NuGet dependencies — uses WPF + WinForms (for `NotifyIcon`) + BCL only

## CI/CD

- **Build workflow** (`.github/workflows/build.yml`): Triggers on push/PR to main. Restores, builds, publishes, uploads artifact.
- **Release workflow** (`.github/workflows/release.yml`): Triggers on `v*` tags. Creates GitHub Release with the exe attached.

To release: `git tag v1.0.0 && git push origin v1.0.0`
