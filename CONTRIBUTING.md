# Contributing

Small, focused improvements are welcome. For a bug, include what you expected, what happened, your Windows version, and steps that reproduce it. If a sensor is missing, mention which reading is affected and whether `--telemetry-test` reports a useful error.

Before sharing logs or exports, remove details you would prefer to keep private, such as workload notes and file paths.

Build with `.\build.ps1`, run `.\tests\check.ps1`, and check the part of the interface or collector you changed. For monitoring changes, also run `dist\WinUtil-Performance.exe --telemetry-test` and confirm that unavailable sensors remain readable as unavailable.

Keep the original WinUtil script unmodified. Changes to upstream WinUtil behavior belong in the [WinUtil repository](https://github.com/ChrisTitusTech/winutil). If updating the bundled release, update the pinned hash, source reference, and retained license together.

Please keep pull requests focused on one problem and explain the visible behavior and how you checked it. There is no need for a long description for a small fix.
