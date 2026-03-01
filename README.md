# PC Usage Timer

A simple Windows parental-control app that enforces screen-time limits for kids. Set a duration, walk away, and the PC locks itself when time runs out.

![app-icon](src/Assets/app-icon.png)

## Features

- **Preset durations** — 10, 20, 30 minutes or custom (up to 8 hours)
- **PIN-protected** — 4-digit PIN to stop the timer or dismiss the lock screen
- **Fullscreen lock** — covers all monitors with a "Time's Up!" message when time expires
- **Audio mute** — system volume is muted automatically when time runs out
- **Time warnings** — toast notifications at 3 minutes and 1 minute remaining (for sessions longer than 5 minutes)
- **Anti-bypass** — blocks Alt+F4, Alt+Tab, Win key, and re-activates if focus is lost
- **Single exe** — no installer, no .NET runtime required, just download and run

## Download

Go to the [Releases](../../releases) page and download `PcUsageTimer.exe`.

## Usage

1. Run `PcUsageTimer.exe`
2. Select a duration (click a preset or type custom minutes)
3. Set a 4-digit PIN and confirm it
4. Click **START TIMER**
5. When time expires, the screen locks and audio is muted
6. Enter the PIN to unlock

> **Note:** `Ctrl+Alt+Del` is handled by the Windows kernel and cannot be blocked by any user-mode app. A determined user could open Task Manager from there. For typical kid usage the current protections work well.

## Build from Source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later.

```bash
git clone https://github.com/YOUR_USERNAME/pc-usage-timer.git
cd pc-usage-timer/src
dotnet run
```

To publish a self-contained single-file exe:

```bash
cd src
dotnet publish -c Release -o ./publish
```

The output is `./publish/PcUsageTimer.exe` (~60 MB, includes the .NET runtime).

## CI/CD

- **Every push/PR to `main`** triggers a build check
- **Pushing a tag** like `v1.0.0` creates a GitHub Release with the exe attached

```bash
git tag v1.0.0
git push origin v1.0.0
```

## License

[MIT](LICENSE)
