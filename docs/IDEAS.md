# Ideas & Future Improvements

Items marked with [x] have been implemented.

## Bypass Hardening

- **Block Task Manager launch** — intercept `taskmgr.exe` via a WMI process creation event watcher or by renaming/restricting the binary through group policy at setup time
- **Disable Ctrl+Alt+Del options** — use registry keys (`DisableTaskMgr`, `DisableLockWorkstation`, `DisableChangePassword`) under `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies` while the lock screen is active; restore on unlock
- **Process watchdog** — spawn a lightweight guardian process that re-launches the app if it's killed via Task Manager; both processes monitor each other
- **Run as a Windows Service** — a service in Session 0 cannot be killed by a standard user; it would communicate with the WPF UI via named pipes and handle the actual locking/enforcement

## Remote Control Enhancements

- [x] **Start timer from phone** — set duration + start a timer remotely via the mobile page
- [x] **Unlock from phone** — enter PIN on mobile page to dismiss lock screen remotely
- [x] **Extend time from phone** — parent can grant extra minutes from the mobile page
- [x] **QR code on setup screen** — display a QR code with the server URL for easy scanning
- **Push notifications to phone** — when the timer hits 3min/1min, send a browser push notification to the phone (via the Service Worker API on the mobile page)
- **mDNS/Bonjour discovery** — advertise the service as `_pcusagetimer._tcp` so the phone page could auto-discover the PC without knowing the IP
- **Multiple PCs** — the mobile page could list/control several PCs on the LAN if each runs the server on a different port or is discovered via mDNS

## Usage Tracking & Scheduling

- **Daily usage log** — record session durations to a local JSON/SQLite file; show a simple history view in the app
- **Daily time budget** — set a total allowed screen time per day (e.g. 2 hours); the app tracks cumulative usage and auto-locks when the budget is spent
- **Scheduled allowed hours** — define time windows (e.g. 4–6 PM on weekdays) during which the PC can be used; auto-lock outside those windows
- **Usage report on phone** — serve a `/history` page showing daily/weekly usage charts (Chart.js or simple HTML table)

## User Experience

- **Countdown overlay** — instead of replacing the whole screen with the timer panel, show a small always-on-top floating widget with the countdown (draggable, semi-transparent)
- [x] **Grace period** — 30-second "save your work" warning before the full lock engages (for sessions >1 min)
- [x] **Sound alert** — plays system exclamation sound at 1 minute remaining
- **Multiple profiles** — support different kids with different PINs and time budgets
- **Dark/light theme toggle** — some kids prefer a lighter UI

## Security

- [x] **Hashed PIN storage** — SHA-256 hash with salt stored in `%LOCALAPPDATA%\PcUsageTimer\pin.hash`
- [x] **Rate-limit PIN attempts** — exponential backoff after 3 wrong attempts (5s/15s/60s lockout) on both lock screen and HTTP API
- **HTTPS for the mobile server** — generate a self-signed certificate on first run so the PIN isn't sent in cleartext over the LAN; the phone would need to accept the cert once
- **Authentication token** — after correct PIN entry on the mobile page, issue a short-lived token stored in a cookie so the parent doesn't have to re-enter the PIN every time

## Technical Debt & Quality

- **Unit tests** — add xUnit/NUnit project for testing `LanHelper`, `PinManager`, timer logic
- **Integration tests** — headless WPF test host to verify lock screen activation/dismissal
- **CI smoke test** — run `dotnet test` in the GitHub Actions build workflow
- **Logging** — add structured logging (Serilog or `Microsoft.Extensions.Logging`) to a local file for debugging issues in the field
- [x] **Single-instance enforcement** — named mutex prevents multiple copies from running
- [x] **Graceful server port fallback** — tries ports 7742–7751 if the preferred port is occupied

## Distribution

- **MSIX packaging** — package as an MSIX for clean install/uninstall, auto-update via App Installer, and proper Start Menu integration
- **Winget manifest** — publish to the Windows Package Manager community repository
- **Auto-update** — check GitHub Releases API on startup and offer to download the latest version
- [x] **Install/uninstall scripts** — PowerShell scripts (`install.ps1`, `uninstall.ps1`) that set up firewall rule, HTTP URL ACL, and copy the exe
