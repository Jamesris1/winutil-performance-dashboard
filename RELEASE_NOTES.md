# WinUtil Performance Dashboard 1.0.0

A portable Windows dashboard for live performance monitoring, repeatable before/after captures, and the complete Chris Titus Tech WinUtil interface.

This is an independent companion project. It is not affiliated with or endorsed by Chris Titus Tech or CT Tech Group LLC. WinUtil **26.10.07** is embedded without modification, with its original MIT license.

## Included

- Live CPU and physical RAM readings, with recent activity graphs.
- Disk read/write rates, activity, latency, and per-volume capacity.
- Network receive/send rates.
- NVIDIA GPU activity, VRAM, temperature, and power where available.
- Ten-second before/after captures, saved locally, with JSON and CSV exports.
- Full original WinUtil interface, plus explicit preset/configuration launch options.

Download **WinUtil-Performance.exe** and double-click it. Opening the dashboard makes no system changes. **Open full WinUtil** requests administrator approval for the original tools. No preset is selected automatically.

Use the same workload for both captures. Differences describe observed activity; they do not establish a guaranteed performance gain. This public release includes no personal baseline measurements.

## Files

- `WinUtil-Performance.exe` — portable Windows application.
- `SHA256SUMS.txt` — SHA-256 checksums for release assets.
- `LICENSE` and `WINUTIL-LICENSE.txt` — dashboard and original WinUtil MIT notices.

Requires Windows, .NET Framework 4.x, and Windows PowerShell 5.1 for WinUtil. NVIDIA readings also need an available `nvidia-smi`. Individual tools may require internet access or a restart.

This release is unsigned. Windows may display a reputation warning for a new executable. The complete source and build script are included in the repository for inspection and local builds.
