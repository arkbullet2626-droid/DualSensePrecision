# DualSense Precision — Architecture (Milestone 1)

Status date: 2026-11-09

## 1. Platform decisions

| Area | Decision | Why |
|---|---|---|
| Runtime | .NET 10 (LTS, Nov 2025, supported to Nov 2028) | Current LTS; `LibraryImport` source-generated P/Invoke, `Span<T>` parsing with no allocations. |
| UI | WinUI 3 on Windows App SDK 2.x (`Microsoft.WindowsAppSDK` 2.6.4 at time of writing) | Spec requirement. 2.x is the current stable line and uses SemVer, so 2.x patches are non-breaking. |
| Packaging | Unpackaged (`WindowsPackageType=None`), self-contained Windows App SDK runtime, Inno Setup installer (Milestone 8) | No MSIX signing certificate needed; installs per-user without admin. |
| Min OS | Windows 10 1809+ (Windows App SDK floor). Windows 10 is out of mainstream support since Oct 2025, so Windows 11 is the primary test target. |
| MVVM | `CommunityToolkit.Mvvm` source generators | Removes boilerplate, no reflection. |
| Storage | JSON (System.Text.Json source-gen) for profiles + settings; SQLite deferred to Milestone 4 for history | Profiles must be portable and diff-able. |

## 2. Solution layout

```
DualSensePrecision.Core            net10.0          Models: ControllerState, buttons, touch, sensors, FeatureStatus. No Windows deps.
DualSensePrecision.HID             net10.0-windows  Win32 HID (hid.dll / setupapi / cfgmgr32), DualSense protocol parser + output report builder, device watcher, mock source.
DualSensePrecision.InputEngine     net10.0          Stick processing (deadzones, curves, output shape), trigger processing, pipeline.
DualSensePrecision.Telemetry       net10.0          High-res clock, lock-free interval ring buffer, percentile stats.
DualSensePrecision.Configuration   net10.0          App settings model + JSON store.
DualSensePrecision.Profiles        net10.0          Profile model, profile store (create/rename/duplicate/delete/import/export, no silent overwrite).
DualSensePrecision.VirtualController net10.0        Backend abstraction (no driver dependency yet — see §6).
DualSensePrecision.App             net10.0-windows  WinUI 3 shell, design system, pages, view models.
DualSensePrecision.Tests           net10.0          xUnit: parser, stick math, curves, telemetry, profiles.
```

Pure logic stays in plain `net10.0` projects so the test suite runs on any machine (including CI on Linux). Only HID and App are Windows-only.

## 3. DualSense USB protocol (what we rely on)

Sources: Linux kernel `hid-playstation.c` (Sony-contributed driver), SDL `SDL_hidapi_ps5.c`, and community reverse-engineering. Sony does not publish an official spec; every field below is cross-checked against at least two of these.

**Identification**: VID `0x054C`. PID `0x0CE6` = DualSense, `0x0DF2` = DualSense Edge.

**Input report `0x01` (USB, 64 bytes incl. report ID)** — byte offsets include the ID at `[0]`:

| Offset | Field |
|---|---|
| 1,2 / 3,4 | Left stick X,Y / Right stick X,Y (0–255, 128 ≈ centre, Y down = 255) |
| 5 / 6 | L2 / R2 analog (0–255) |
| 7 | Sequence counter (wraps 0–255) — used for **dropped report detection** |
| 8 | Low nibble: D-pad hat (0=N … 7=NW, 8=released). Bits 4–7: Square, Cross, Circle, Triangle |
| 9 | L1, R1, L2 digital, R2 digital, Create, Options, L3, R3 |
| 10 | PS, Touchpad click, Mute. Edge: bit4 Fn-L, bit5 Fn-R, bit6 Back-L, bit7 Back-R |
| 16–21 | Gyro pitch/yaw/roll (int16 LE) |
| 22–27 | Accel X/Y/Z (int16 LE) |
| 28–31 | Sensor timestamp (uint32 LE, 0.33 µs units) |
| 33–36, 37–40 | Touch points 0/1: byte0 bit7 = **not touching**, bits0–6 = contact ID; X 12-bit, Y 12-bit (pad is 1920×1080) |
| 53 | Battery: low nibble level 0–10, high nibble 0 discharging / 1 charging / 2 full / other = error |

**Output report `0x02` (USB)**: report ID + 47-byte common block. Flags at `[1]`,`[2]`,`[39]` select which fields the controller applies. Rumble `[3]` right / `[4]` left, mute LED `[9]`, right trigger effect `[11..21]`, left trigger effect `[22..32]`, motor/trigger power reduction `[37]`, lightbar setup `[42]`, LED brightness `[43]`, player LEDs `[44]`, lightbar RGB `[45..47]`. Written with `HidD_SetOutputReport`/`WriteFile` at the device's reported `OutputReportByteLength`. USB output needs **no CRC** (Bluetooth does — not in scope).

**Feature reports**: `0x05` IMU calibration (gyro/accel bias & range), `0x09` pairing info (MAC), `0x20` firmware info (build date/time, hardware version, firmware version, update version).

Variation across revisions: firmware ≥ 2.24 supports "improved rumble emulation" (flag2 bit2). Edge has extra buttons and a different feature set (profiles on-device). The parser takes a `DualSenseModel` so behaviour can branch per model and firmware.

## 4. Technical limitations identified up front

1. **Physical report rate is fixed by the controller's USB endpoint interval.** The app cannot raise it from user mode. Community measurements put the stock wired DualSense well below 8000 Hz; we do not quote a number — the Performance page **measures** it. Raising it requires a third-party filter driver (e.g. `hidusbf`) that modifies USB endpoint intervals; that is a system-level, unsigned-driver change. We will not do it, and will at most link to it with warnings, only if the user asks.
2. **The 125–8000 Hz selector therefore controls the app's output rate cap**, not the controller. Default is *Event-driven* (one output per input report, zero added latency). Any setting above the measured input rate is shown as "capped by controller: N Hz". The UI labels these separately (spec §4 items 1–5).
3. **Haptics** (voice-coil) on USB are driven by an audio endpoint, not HID. We support HID "compatible rumble" (two motors) now; true haptic waveforms via the USB audio interface are a later stretch item, marked *In progress* until real.
4. **Audio** (speaker/headset volume, mic) is controllable via output report fields; playback routing is a Windows audio device and outside this app.
5. **PS button / Mute** may be intercepted by Steam Input or Windows Game Bar if those are running. We detect nothing about that; we document it.
6. **Exclusive access**: Windows lets multiple processes open the HID device for shared read. Games reading the DualSense directly (DirectInput / Steam Input) will still see raw input — remapping only affects games that read the **virtual** controller. Hiding the physical device requires a filter driver (HidHide) — optional, with consent.
7. **Bluetooth is out of scope** per spec; architecture keeps the transport behind `IHidTransport` so it can be added (BT needs CRC32 on output reports).

## 5. Input pipeline

```
HID read thread (dedicated, AboveNormal priority, blocking ReadFile — no timers, no polling)
   │  timestamp = Stopwatch.GetTimestamp()  (QueryPerformanceCounter)
   ▼
DualSenseParser.Parse(span) → ControllerState (struct, zero alloc)
   ▼
InputPipeline.Process(state) → ProcessedState      (sticks, triggers, mapping)  — time measured
   ▼
├─ TelemetryRecorder (interval + processing time into lock-free ring buffers)
├─ IVirtualControllerBackend.Submit(...)  (Milestone 6)
└─ StateSnapshot (latest-wins, Volatile write) ← UI reads at display refresh, never blocks the reader
```

The UI never subscribes per report; it samples the latest snapshot from a `DispatcherQueueTimer` at ~60 Hz. This keeps 1000 Hz input from flooding the UI thread.

Device arrival/removal: `CM_Register_Notification` on `GUID_DEVINTERFACE_HID` → re-enumerate → open/close. Read errors (`ERROR_DEVICE_NOT_CONNECTED`) also trigger teardown, so unplug is handled even if the notification is late.

## 6. Virtual controller evaluation

| Option | Status | Verdict |
|---|---|---|
| **ViGEmBus** (Xbox 360 / DS4) | Archived after 2023 trademark dispute; no further updates. Existing installs keep working; old updaters contacted a transferred domain (IP leak) — fixed via Legacinator. | Usable **only if already installed**; never bundled or auto-installed. |
| **VIIPER + usbip-win2** | Actively developed; userspace USB/IP device emulation (Xbox 360, DS4, DualSense, keyboard, mouse). Needs the signed usbip-win2 kernel driver. Server GPL-3.0, client libs MIT. | Most promising maintained path. Needs licence review (GPL server shipped as separate process is likely fine) and latency testing. |
| Windows built-in (`Windows.UI.Input.Preview.Injection` gamepad injection) | Restricted capability (`inputInjectionBrokered`), needs packaged app + capability approval; Xbox-only semantics; not reliably visible to XInput games. | Not viable for an unpackaged app. |

Decision (2026-11-09, approved by user): **VIIPER**. The user installs VIIPER and usbip-win2 themselves (`winget install Alia5.VIIPER`, then VIIPER's own installer); the app never installs drivers.

Implementation: `ViiperClient` speaks VIIPER's documented localhost protocol directly (TCP :3242, `path[ payload]\0` request → JSON response; no auth needed for localhost by default), so there's no dependency on the GPL server code or an unverified NuGet package. `ViiperXbox360Backend` creates a bus + `xbox360` device, opens the device stream (`bus/{id}/{dev}\0`), then writes 20-byte input packets (`u32 buttons, u8 LT, u8 RT, i16 LX/LY/RX/RY, 6 reserved`) directly from the HID reader thread, and reads 2-byte rumble packets back. VIIPER auto-attaches the device to usbip-win2 on localhost. Output is capped by VIIPER at ~1000 Hz; the DualSense delivers ~250 Hz, so every report is forwarded.

## 7. Anti-cheat and fairness

No aim assist, no anti-recoil, no input automation beyond user-authored macros with runaway guards (max length, max repeat, global kill key). No device hiding without consent. No attempt to bypass any anti-cheat. Macro page shows a notice that some games forbid macros.
