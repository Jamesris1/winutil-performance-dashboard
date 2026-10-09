# Changelog

## 1.1.0 — 2026-10-09

### Fixed

- Full WinUtil reported errors without showing their details. A scrollable error log now appears directly beneath the error count, with copy and open-log controls.
- Live sensor updates replaced the dashboard's operation messages. The new status bar keeps operation state, progress, and errors separate from sensor updates.
- Startup failures in the hidden elevated worker were difficult to diagnose. Each launch now keeps a local session log and a readable completion or failure report.
- Dashboard content could be clipped at smaller window sizes. Pages now scroll while the status bar stays visible.

### Added

- A clearly labeled Gaming preset that enables Windows Game Mode only. The original Standard, Minimal, and Advanced presets remain available.
- A dashboard log viewer for the current WinUtil session.

The bundled upstream WinUtil release remains 26.10.07. Its file is unchanged; companion startup code adds the log panel and session reporting.

## 1.0.0 — 2026-10-09

- Initial portable dashboard release with live monitoring, ten-second before/after captures, JSON/CSV exports, and access to the original WinUtil tools.
- Included the pinned upstream script, MIT license notices, source, and build instructions.
