# WinUtil Performance Dashboard 1.1.0

This update makes errors visible in full WinUtil, adds a Gaming preset, and gives the dashboard a persistent status bar.

## Changed

- **Full WinUtil:** a scrollable session log appears directly below the error count, with readable error details and a way to open the saved log.
- **Gaming preset:** enables Windows Game Mode only, with its scope shown before applying it.
- **Dashboard status:** operation state, progress, and errors remain visible while live monitoring updates separately. A log viewer keeps session details close at hand.
- **Startup failures:** recorded even when the full WinUtil window cannot open.

Download **WinUtil-Performance.exe** and double-click it. Opening the dashboard makes no system changes. WinUtil operations still request Windows administrator approval and run only after you choose them.

This is an independent companion project, not affiliated with or endorsed by Chris Titus Tech or CT Tech Group LLC. The official WinUtil **26.10.07** script remains unchanged in the executable; a companion extension adds the full-window log panel at launch. Both MIT license notices are included.

## Files

- `WinUtil-Performance.exe` — portable Windows application.
- `SHA256SUMS.txt` — SHA-256 checksums for release assets.
- `LICENSE` and `WINUTIL-LICENSE.txt` — companion and original WinUtil MIT notices.

Requires Windows, .NET Framework 4.x, and Windows PowerShell 5.1 for WinUtil. NVIDIA telemetry needs an available `nvidia-smi`. Session logs remain local and can contain machine and software details; review them before sharing.

This release is unsigned. The repository includes the complete source and build instructions.
