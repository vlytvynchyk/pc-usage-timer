# Ideas & Future Improvements

## Bypass Hardening

- **Block Task Manager launch** — intercept `taskmgr.exe` via a WMI process creation event watcher or by renaming/restricting the binary through group policy at setup time
- **Disable Ctrl+Alt+Del options** — use registry keys (`DisableTaskMgr`, `DisableLockWorkstation`, `DisableChangePassword`) under `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies` while the lock screen is active; restore on unlock
- **Process watchdog** — spawn a lightweight guardian process that re-launches the app if it's killed via Task Manager; both processes monitor each other
- **Run as a Windows Service** — a service in Session 0 cannot be killed by a standard user; it would communicate with the WPF UI via named pipes and handle the actual locking/enforcement

## Remote Control Enhancements

- **Start timer from phone** — the mobile page currently only locks; add the ability to set duration + start a timer remotely so the parent doesn't need to touch the PC at all
- **Unlock from phone** — allow entering the PIN on the mobile page to dismiss the lock screen remotely
- **Push notifications to phone** — when the timer hits 3min/1min, send a browser push notification to the phone (via the Service Worker API on the mobile page)
- **QR code on setup screen** — display a QR code with the server URL so the parent can scan it instead of typing the IP manually
- **mDNS/Bonjour discovery** — advertise the service as `_pcusagetimer._tcp` so the phone page could auto-discover the PC without knowing the IP
- **Multiple PCs** — the mobile page could list/control several PCs on the LAN if each runs the server on a different port or is discovered via mDNS

## Usage Tracking & Scheduling

- **Daily usage log** — record session durations to a local JSON/SQLite file; show a simple history view in the app
- **Daily time budget** — set a total allowed screen time per day (e.g. 2 hours); the app tracks cumulative usage and auto-locks when the budget is spent
- **Scheduled allowed hours** — define time windows (e.g. 4–6 PM on weekdays) during which the PC can be used; auto-lock outside those windows
- **Usage report on phone** — serve a `/history` page showing daily/weekly usage charts (Chart.js or simple HTML table)

## User Experience

- **Countdown overlay** — instead of replacing the whole screen with the timer panel, show a small always-on-top floating widget with the countdown (draggable, semi-transparent)
- **Grace period** — when time expires, give a 30-second "save your work" warning before the full lock engages
- **Extend time from phone** — parent can grant +10/+15 minutes from the mobile page without walking to the PC
- **Sound alert** — play an alarm sound at 1 minute remaining (in addition to the toast notification)
- **Multiple profiles** — support different kids with different PINs and time budgets
- **Dark/light theme toggle** — some kids prefer a lighter UI

## Security

- **Hashed PIN storage** — currently the PIN is stored in plaintext in `pin.dat`; hash it with SHA-256 + a machine-specific salt
- **Rate-limit PIN attempts** — on the mobile `/lock` endpoint and the lock screen itself, add exponential backoff after 3 wrong attempts (e.g. 5s, 15s, 60s lockout)
- **HTTPS for the mobile server** — generate a self-signed certificate on first run so the PIN isn't sent in cleartext over the LAN; the phone would need to accept the cert once
- **Authentication token** — after correct PIN entry on the mobile page, issue a short-lived token stored in a cookie so the parent doesn't have to re-enter the PIN every time

## Technical Debt & Quality

- **Unit tests** — add xUnit/NUnit project for testing `LanHelper`, `RemoteLockServer` (route handling, PIN validation), timer logic
- **Integration tests** — headless WPF test host to verify lock screen activation/dismissal
- **CI smoke test** — run `dotnet test` in the GitHub Actions build workflow
- **Logging** — add structured logging (Serilog or `Microsoft.Extensions.Logging`) to a local file for debugging issues in the field
- **Single-instance enforcement** — use a named mutex to prevent multiple copies of the app from running simultaneously (would cause port conflicts on 7742)
- **Graceful server port fallback** — if port 7742 is occupied, try the next few ports and display whichever one works

## Distribution

- **MSIX packaging** — package as an MSIX for clean install/uninstall, auto-update via App Installer, and proper Start Menu integration
- **Winget manifest** — publish to the Windows Package Manager community repository
- **Auto-update** — check GitHub Releases API on startup and offer to download the latest version
- **Installer with firewall rule** — bundle a setup script that adds the `netsh http urlacl` and Windows Firewall rule so the user never sees access-denied errors
