# Dual Precision

DualSense (PS5) and Xbox controller tuning for Windows 10/11: real report-timing telemetry, stick and trigger shaping, button mapping, macros, adaptive triggers (DualSense) and profiles. By Opifex Studios.

## Requirements (development)

- Windows 11 (or Windows 10 1809+)
- .NET 10 SDK
- Visual Studio 2022 17.14+ / 2026 with the **Windows application development** workload (for WinUI 3), or just the .NET SDK for CLI builds

## Install (for users)

**Download the latest installer: [DualPrecision-Setup.exe](https://github.com/arkbullet2626-droid/DualSensePrecision/releases/latest/download/DualPrecision-Setup.exe)** (also on the Releases page with SHA-256 checksums). Nothing else is needed: no .NET, no build tools. Everything below is for developers.

### Building the installer yourself

Double-click `build-installer.cmd` (needs Inno Setup 6: `winget install JRSoftware.InnoSetup`). It runs the tests,
publishes a self-contained Release build and writes `installer\Output\DualPrecision-<version>-setup.exe`.
The installer is per-user: no admin rights, no drivers. Profiles live in `%LOCALAPPDATA%\DualSensePrecision` and survive
updates; uninstalling asks whether to keep them.

## Build & run (for development)

```powershell
dotnet test tests\DualSensePrecision.Tests
dotnet run --project src\DualSensePrecision.App -p:Platform=x64
```

No administrator rights are needed. Settings and profiles live in `%LOCALAPPDATA%\DualSensePrecision`.

No controller handy? Settings → **Use simulated controller**. Everything it shows is labelled as simulated.

## Layout

```
src/
  DualSensePrecision.App              WinUI 3 shell, design system (Themes/Tokens.xaml), pages
  DualSensePrecision.Core             Controller state model
  DualSensePrecision.HID              Win32 HID, DualSense protocol, Xbox (XInput) reader, hot-plug, mock source
  DualSensePrecision.InputEngine      Sticks, curves, triggers, mapping, pipeline
  DualSensePrecision.Telemetry        Report timing statistics
  DualSensePrecision.Configuration    App settings
  DualSensePrecision.Profiles         Portable JSON profiles
  DualSensePrecision.VirtualController Virtual Xbox 360 pad via VIIPER
tests/DualSensePrecision.Tests        xUnit
docs/ARCHITECTURE.md                  Protocol notes, decisions, limitations
DEVELOPMENT_STATUS.md                 What works, what doesn't, what's next
```

## Troubleshooting

- **Not detected:** use a data-capable USB-C cable (many are charge-only), plug directly into the PC rather than a hub, and close apps that take exclusive controller access.
- **Xbox controller not detected:** it must show in Windows' Game Controllers (joy.cpl). Bluetooth Xbox pads need Windows 10 1903+ and recent controller firmware (update it in the Xbox Accessories app).
- **Report rate lower than expected:** that's the controller's hardware rate, measured honestly. USB hubs and front-panel ports can add jitter.

## Licence

MIT, © 2026 Opifex Studios. See [`LICENSE`](LICENSE). Third-party components and optional downloads (VIIPER, usbip-win2, HidHide) keep their own licences: see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
