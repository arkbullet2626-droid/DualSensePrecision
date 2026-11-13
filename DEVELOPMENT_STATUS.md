# Dual Precision — Development Status

Last update: 2026-11-10 · Version 0.18.0 · M1–4 verified on hardware; Stick, Trigger and Appearance verified; virtual Xbox 360 controller verified end to end; Button Mapping verified; Macros built; Xbox controller input built; optional HidHide hiding built (not yet hardware-tested).

## Verification log

- 2026-11-09 — Built and run on Windows 11 (.NET SDK 10.0.403). 84/84 tests passed (v0.2.0), then 86/86 (v0.2.1).
- Real DualSense (PID 0x0CE6, firmware 6.30) over USB: detection, firmware/MAC/IMU calibration read, battery, sticks, all buttons, touchpad (2 points), gyro, hot-plug reconnect — all confirmed working.
- Measured physical report rate: **~250 Hz** (P50 4.000 ms, P99 ~4.02 ms, 0 dropped).
- v0.2.1 fixed: false stall counts, two app instances running at once, default profile choice, clipped tiles.
- v0.3.0 Stick editor verified on hardware: 87/87 tests, deadzones, square output, live view, custom curve persists across restart.
- v0.5.0 built on Windows: 96/97 tests — on Windows a refused localhost connect surfaces as a timeout; fixed in v0.5.1 (timeout now reported as 'VIIPER isn't running').
- v0.6.0 verified on hardware: 101/101 tests; button remap, keyboard keys (+toggle), mouse buttons, remap onto/away from triggers, shift layer, persistence, reset, key release on unplug all working.
- v0.7.0 built on Windows: 112/113 — PreciseWait returned up to 1 ms early (integer ms rounding). Fixed in v0.7.1 (tick-based deadline).
- v0.7.1 (Macros) — user moved on to requesting stick recording before reporting the full macro test list.
- v0.10.0 failed to build: XAML x:Name "MotionResult" clashed with the MotionResult type (CS0236/CS1061, followed by a cascading WMC9999). Renamed to MotionResultText in v0.10.1; also fixed CS0108 (MainWindow.Current → `new`) and CS0252 (ReferenceEquals). Scanned all XAML for other name/type clashes: none.
- v0.18.0: user picked logo concept 02 (DP monogram); four variations drawn (A cut-corner, B rounded, C shared stem, D badge), A used provisionally: vector LogoMark control (title bar, About) in themed DsLogoBrush; per-preset logo colours + Logo slot in the editor; new AppIcon.ico (amber, 16–256 px). Interface-scale slider: drag previews a visual zoom anchored on the (counter-scaled) slider via CompositionTarget.Rendering easing; release commits layout scale, re-aligns scroll and eases the residual offset.
- v0.17.0: 12 presets (dark: Opifex, Midnight, Forest, Deep Red, Crimson, Graphite; fun: Synthwave, Deep Sea, Toxic; light: Paper, Daylight, Sand). Light palettes switch the window's controls to the WinUI Light theme live (root RequestedTheme) and the whole app after restart (Application.RequestedTheme set in App ctor from saved palette); both theme dictionaries are recoloured. Logo uses fixed DsLogoBrush/DsLogoInkBrush, never themed. Three new logo concepts drafted (Twin Reticle, DP Monogram, Stick Gate) for the user to choose.
- v0.16.1: user asked to drop Main/Second accent from the colour editor; accents fixed to Opifex amber/ember, presets only change the 4 surface/text colours.
- v0.16.0 (release candidate): user-customisable interface colours (6 slots: background, panels, borders, text, accent, second accent; ColorPicker flyout + hex box; presets; reset; WCAG contrast warning) applied live by mutating the shared brushes in Tokens.xaml (derived shades computed in ColorMath); interface scale 75–150 % via a ScaleHost panel (measure ÷ scale, render × scale). Saved in settings (Palette, UiScale).
- v0.15.0 (release candidate): Opifex Studios palette (from opifexstudios.ca, confirmed by the user) and bundled fonts (Oxanium/Figtree/IBM Plex Mono, OFL); tray icon (Win32 Shell_NotifyIcon) with close-to-tray, Open/Exit menu and one-time notice; Start with Windows (HKCU Run, --minimized; also an unticked Setup task, removed on uninstall); local daily log (7 days, no input data); Export diagnostics zip; Reset app (un-hides first, optional profile delete, restarts); GitHub update check + banner + About page (version, licence, notices, privacy, links); first-run welcome guide; game-rules wording on macro/keyboard remaps. CI release now also uploads DualSensePrecision-Setup.exe (stable latest/download name) and SHA256SUMS.txt.
- v0.13.0 Xbox test list: user confirmed all tests pass.
- v0.14.2 verified on hardware: hide switch works with correct status texts, controller keeps working in the app, Rocket League sees one controller (no second player), switching off and closing the app both un-hide.
- v0.14.2: HidHide installed fine via the 0.14.1 Setup (user confirmed). Switching hiding on then showed "HidHide request failed: The parameter is incorrect": IOCTL_GET_ACTIVE only accepts a 1-byte BOOLEAN output buffer (the generic get used a size query then 2 bytes). Worse, that step ran after the blacklist was written but before the app recorded its entries, so they wouldn't have been removed on exit. Fixed: GetActive uses a 1-byte buffer; the app records what it will hide before writing HidHide's list (write-ahead), counts every target as its own, and switching hiding off also un-hides the connected controller's entries (recovers anything a previous version lost track of). Settings are only rewritten when something changed. "Check what games see" now also tests a DualSense directly (an outside process tries to open it) because XInput can't see a DualSense.
- v0.14.1 (installer fix): on the user's PC the 0.14.0 Setup didn't install HidHide at all (no Program Files folder, no error). Diagnosed on a clean Windows runner: HidHide 1.5.230 uses an Advanced Installer bootstrapper (asInvoker) that can't request admin rights in silent mode, '/quiet' isn't its silent switch, and even its real silent mode ('/exenoui /qn') hangs because the MSI's RegisterUpdater action ('nefarius_HidHide_Updater.exe --install', condition NOT Installed, no property to skip it) shows a 'Failed to register in autostart' dialog. Fix: Setup now opens HidHide's installer with its normal screens (after explaining the clicks) and verifies the HidHide service exists afterwards, with Retry/Cancel. VIIPER's silent install verified on a clean runner (exit 3010 = installed, restart needed; port 3242 listening); because VIIPER refuses the driver install without admin rights, Setup now starts it with 'runas' (UAC prompt), treats 3010 as success and verifies viiper.exe exists. Every CI build now runs the Setup.exe unattended with the VIIPER task and refuses to publish if the app or VIIPER didn't install. App: tells 'HidHide installed, restart Windows' apart from 'not installed', links the official download page.
- v0.14.0: optional "Hide your real controller from games" via HidHide 1.5.230 (Nefarius, signed; setup can download it, SHA-256 pinned f4bbbcb8…05f6, runs its own installer with /quiet /norestart, UAC = consent, restart offered). In-app switch with a one-time consent dialog; hides only while the virtual pad is connected; app whitelisted; adds/removes only its own blacklist entries (remembered in settings so a crash is cleaned up on next start); never hides anything under ROOT\USBIP_WIN2 (VIIPER's pad), hubs or root devices. Xbox: XUSB device + same-VID/PID USB parents + matching HID nodes. DualSense: HID node + USB interface parent. "Check what games see" runs XInput from a separate, non-allowed process (PowerShell). Prompted by v0.13.0 hardware test: Rocket League added a second player from the doubled Xbox input.
- v0.13.0: Xbox controller support (Series / One / 360; USB, Bluetooth, Xbox Wireless Adapter) through XInput. Auto-detects alongside the DualSense; positional mapping so profiles/mappings/macros carry over; 16-bit sticks; rumble via XInputSetState scaled by the profile's vibration strength; battery shown as a level; app's own VIIPER pad and Steam virtual pads excluded from input; DualSense-only sections hidden on Xbox; Performance labels the number "update rate (XInput)". Not yet tested on hardware.
- v0.12.1: build-installer installs Inno Setup via winget automatically if missing (user hit the manual step).
- v0.12.0: single Setup.exe also downloads + installs VIIPER 0.9.0 (optional task, shown only when VIIPER is missing; SHA-256 pinned 142c144f…23e6 from the official release asset since upstream publishes no checksums; `viiper install --quiet --no-update`; UAC for usbip-win2 = explicit consent; offers restart). Not yet built on Windows.
- v0.11.0 (installer: build-installer.cmd → Inno Setup per-user Setup.exe, self-contained publish, app icon, version in Settings) not yet built on Windows.
- v0.10.1 verified on hardware: 129/129 tests; all Controller Tester features and the full diagnostic working.
- v0.9.1 verified on hardware: lightbar colour per profile fix confirmed; all Profiles features working.
- v0.9.0 verified on hardware except: lightbar colour didn't stay with its profile when switching (stale write from the closing Appearance page's colour picker into the newly active profile). Fixed in v0.9.1: ColorPicker no longer two-way bound (only user changes save); Appearance/Stick/Trigger editors bind to the profile they loaded and refuse writes after leaving the page or if the active profile changed.
- v0.8.2: reverted stick + trigger recordings (user: it was a test). Code identical to v0.7.1 except the Play-now countdown now survives switching pages.
- v0.8.0 built on Windows. User couldn't find the hidden track-record button → v0.8.1 makes 'Record everything' the primary always-visible button with inline opt-in.
- v0.6.1: Virtual Controller page shows pressed Xbox buttons by name (user feedback: hex code unclear).
- v0.5.1 verified on hardware with VIIPER v0.9.0 + usbip-win2: 97/97 tests; virtual Xbox 360 pad visible in XInput, sticks/triggers/all buttons map correctly, ~250 Hz output, deadzone and curve changes reach XInput, switch off removes the pad, app exit removes the pad, game rumble forwarded, reconnect restores lighting.
- v0.4.0 verified on hardware: 91/91 tests; lightbar colour + brightness, player LEDs, mute LED, test vibration, vibration-strength reduction (byte 37 confirmed), adaptive Resistance / Weapon / Vibration on R2, live trigger meter, resistance released on app exit.

## Milestone summary

| Milestone | Status |
|---|---|
| 1 — Research & architecture | **Complete** — `docs/ARCHITECTURE.md` |
| 2 — Project foundation (solution, nav, design system, launch) | **Complete, unverified build** |
| 3 — USB communication | **Complete** — verified on real hardware |
| 4 — Performance engine | **Complete** — verified on real hardware (~250 Hz) |
| 5 — Customization | **In progress** — Stick editor verified; Trigger + Appearance verified; Button Mapping verified |
| 6 — Advanced | **In progress** — adaptive triggers, LEDs, rumble verified; virtual Xbox 360 controller verified; macros, haptics, DS4 target pending |
| 7 — Profiles & testing | **Complete** — Profiles and Controller Tester verified on hardware |
| 8 — Polish & installer | **Release candidate** — installer verified (CI install test + hardware), Settings extras, tray, updates, About, logs/diagnostics done |

## Feature status

| Feature | Status | Notes |
|---|---|---|
| USB detection (VID 054C, PID 0CE6 / 0DF2 Edge) | In progress | `HidEnumerator`, SetupAPI |
| Hot-plug connect/disconnect/reconnect | In progress | `CM_Register_Notification` + read-error teardown + 3 s fallback rescan while disconnected |
| Input report parsing (sticks, triggers, all buttons, d-pad, gyro, accel, touch ×2, battery, sequence) | Complete (tested) | `DualSenseProtocol.TryParseUsbInput`, zero allocation |
| Edge extra buttons (Fn, back paddles) | In progress | Parsed; Edge not hardware-tested |
| Firmware / hardware version, MAC, IMU calibration | In progress | Feature reports 0x20 / 0x09 / 0x05 |
| Raw HID report monitor | Complete (hardware-verified) | Controller page |
| Report timing: rate, avg, P50/P95/P99, max, drops (sequence gaps), stalls, processing time, CPU | Complete (tested logic) | Lock-free ring buffer, QPC timestamps |
| Live interval graph | Complete (unverified) | Performance page |
| Output rate selector 125–8000 Hz | Complete (unverified) | Caps *virtual output*; UI states it can't raise the controller's rate |
| Stick editor: inner/outer/anti deadzone, radial/axial, sensitivity X/Y, circle/square, invert, live raw vs processed view | Complete (hardware-verified) | Stick Settings page |
| Response curves: drag editor, add/remove points, 6 presets + custom, live input marker | Complete (unverified build) | |
| Stick calibration wizard, drift test | Planned | Calibration math done; wizard UI pending |
| Trigger page: deadzone, 100% point, activation, curve, sensitivity, digital, dual-zone, live meter | Complete (hardware-verified) | Trigger Settings page |
| Adaptive triggers (Off/Resistance/Weapon/Vibration) | Complete (hardware-verified) | Sent live via OutputManager; reset to Off on exit |
| Lightbar colour + brightness, player LEDs, mute LED, vibration test, motor strength | Complete (hardware-verified) | Appearance page; re-applied on reconnect |
| Button mapping page: visual DualSense with live highlight, remap to controller/keyboard (+modifiers)/mouse, disable, toggle, shift layer, per-button and per-layer reset | Complete (hardware-verified) | Keyboard via SendInput scan codes; keys released on unplug/exit; remaps onto/away from L2/R2 drive the analog trigger correctly |
| Macros: record from controller (reader-thread timestamps), step editor (button/key/mouse/wait), once / while-held / toggle, assign to buttons, import/export, enable/disable | Complete (unverified build) | Safeguards: no stick/mouse-move steps, 256 steps, 10 s per wait, 60 s per play, 10 min repeat cap, ≥20 ms per repeat cycle, no macro→macro chaining, Create+Options emergency stop, stop on unplug/exit, everything released on stop |
| Haptics (voice-coil waveforms) | Planned | Needs USB audio path, not HID |
| Virtual Xbox 360 controller via VIIPER | Complete (hardware-verified) | User installs VIIPER + usbip-win2 themselves (`winget install Alia5.VIIPER`). Direct TCP client, 20-byte packets sent on the reader thread, auto-reconnect, game rumble forwarded to DualSense, pad removed on exit, XInput read-back check, measured output Hz |
| Stick + trigger input tracks (offline TAS test) | Removed | Built in 0.8.0–0.8.1 as a test, reverted in 0.8.2 at the user's request. Macros are back to buttons, keys and mouse buttons only (matches the original spec's no-gameplay-automation rule) |
| Hide the real controller (DualSense or Xbox) from games via HidHide | Complete (hardware-verified, Rocket League) | Optional component in setup; in-app switch with consent; active only while the virtual pad runs; undone on switch-off/exit/next start; "Check what games see" self-test |
| Virtual DualShock 4 target | Planned | VIIPER supports it; different packet format |
| Profiles page: list with active/startup badges, instant switch + sidebar quick switcher, 9 templates (Blank, Competitive FPS, Precision Aiming, Fortnite, Call of Duty, Rust, ARC Raiders, Rocket League, Racing) or copy of active, rename/duplicate/export/import (Keep both / Replace / Cancel)/restore defaults/delete (confirmed; deleting active switches first), startup profile | Complete (hardware-verified) | Switching stops macros, releases held keys, re-applies lights/triggers/virtual pad; open pages refresh to the new profile |
| Controller Tester: buttons, sticks (live + 36-direction range map), drift test with suggested deadzone, triggers, touchpad (2 fingers), gyro/accel at-rest check, guided full diagnostic, PASS/CHECK/FAIL report (copy/save), apply suggested deadzones | Complete (hardware-verified) | Raw hardware values; analyzers fed per report on the reader thread |
| Settings persistence (atomic writes, corrupt-file recovery) | Complete | |
| Mock controller (clearly labelled) | Complete | Settings → "Use simulated controller" |
| Installer (single Setup.exe, optional VIIPER download + install) | Complete (unverified build) | `build-installer.cmd` runs tests → `dotnet publish` (Release, self-contained, win-x64) → Inno Setup. Per-user, no admin; Start menu + desktop shortcuts; AppMutex closes running copy; uninstall asks to keep/delete data; unsigned (SmartScreen 'More info → Run anyway') |
| Xbox controllers (Series / One / 360, USB / Bluetooth / Wireless Adapter) | Complete (unverified on hardware) | XInput 1.4, ~1 ms polling, change-driven telemetry labelled as an update rate (not a hardware rate). Sticks, triggers, buttons, mapping, macros, profiles, tester, rumble, battery level. Own virtual pad excluded via `XboxSlotFilter`. Preference setting when both controller types are connected |
| DualSense over Bluetooth | Out of scope | Spec is USB-only |

## Known limitations (see ARCHITECTURE §4)

- The DualSense's physical report rate is set by its USB endpoint and can't be raised from software. The app measures it; it doesn't claim to change it.
- Remapping only affects games that read the virtual controller. Games reading the DualSense directly still see raw input unless the physical device is hidden (HidHide, optional, consent required).
- PS button may be captured by Steam / Game Bar.

## Next steps

1. Compile + run tests on Windows; fix errors.
2. Hardware test M3/M4 with a real DualSense: confirm byte offsets, firmware report, hot-plug, measured rate.
3. M5 UIs: stick editor with draggable curve + live raw/processed view, trigger page, lightbar/LED page.
4. Decide virtual-controller backend (needs your call).
