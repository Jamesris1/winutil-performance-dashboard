# Third-party notices

This project is an independent companion dashboard. It is not affiliated with or endorsed by Chris Titus Tech or CT Tech Group LLC.

## Chris Titus Tech WinUtil

- Upstream project: <https://github.com/ChrisTitusTech/winutil>
- Included release: [26.10.07](https://github.com/ChrisTitusTech/winutil/releases/tag/26.10.07)
- Copyright: **Copyright (c) 2022 CT Tech Group LLC**
- License: **MIT**; the complete original notice is included in [vendor/WINUTIL-LICENSE.txt](vendor/WINUTIL-LICENSE.txt).
- Included source: `vendor/winutil.ps1`, embedded without modification in the application.
- SHA-256: `EEAE69922FBE6354EA4E2A37F705EAA17BDAF45CC7889618A313178A513B13EE`

WinUtil provides the original system tools, presets, and software-installation features. The dashboard, monitoring, comparison, and export interface are this project's additions.

The build verifies the pinned script hash before compiling. The application verifies it again before running WinUtil and includes the original MIT license as an embedded resource. The license can also be inspected with `--license`.

The root [LICENSE](LICENSE) applies to this project's original dashboard and supporting files. It does not replace the original WinUtil copyright or license.
