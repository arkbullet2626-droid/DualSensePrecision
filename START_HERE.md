# DualSense Precision v0.12.1: build the single Setup.exe and test it

Setup is now **one file** that installs everything: the app (with .NET and the Windows App SDK built in) and, if you leave the box ticked, **VIIPER** for the virtual Xbox controller, downloaded from its official GitHub release and checked before it runs.

You build that file once per version on your PC (this workspace can't compile Windows apps). After that, the Setup.exe is all anyone needs.

## 1. Extract

1. Close DualSense Precision if it's open.
2. Right-click `DualSensePrecision-0.12.1.zip` in Downloads → **Extract All…** → **Extract**.

## 2. Build the Setup.exe

1. Open the extracted `DualSensePrecision-0.12.1` folder.
2. **Double-click `build-installer`, the one whose Type is *Windows Command Script*** (gear icon). Not the PowerShell one.
   - **First time only:** if Inno Setup (the tool that makes Setup.exe files) isn't installed, it installs it for you automatically. If Windows asks for permission, click **Yes**.
   - Then it runs the tests (**129 succeeded**), builds the release app, and makes the installer.
3. File Explorer opens with **`DualSensePrecision-0.12.1-setup.exe`** selected (in `installer\Output`). That's the single file to keep or share.

If the window shows red text, copy all of it and send it to Claude.

## What changed in 0.12.1

- `build-installer` now **installs Inno Setup automatically** the first time, so there's no manual step.

## What's new in 0.12

- **Setup downloads and installs VIIPER for you** (optional, ticked by default when VIIPER isn't installed):
  - downloads VIIPER 0.9.0 (~13 MB) from github.com/Alia5/VIIPER;
  - verifies the file's SHA-256 fingerprint and refuses anything that doesn't match;
  - runs VIIPER's own unattended installer, which installs the signed **usbip-win2** driver. Windows asks for **administrator permission** for that driver, and that prompt is your OK;
  - offers a **restart** at the end, because the driver may need one.
- If VIIPER is already installed, the option doesn't appear.
- If the download fails, Setup asks whether to carry on without it.
- The Virtual Controller page's setup help now points to this option.
- Everything from 0.11.0: per-user install with no admin needed for the app itself, Start menu and desktop shortcuts, the uninstaller asks whether to keep your profiles, and the app icon.

## Tests for this version

**The build (new in 0.12.1)**
A. Double-click `build-installer` (Command Script). If Inno Setup was missing, you see "Inno Setup isn't installed - installing it now", then possibly a Windows permission prompt (click **Yes**), and then the build carries on by itself.
B. File Explorer opens with `DualSensePrecision-0.12.1-setup.exe` selected.

**Normal install (VIIPER already on your PC)**
1. Run `DualSensePrecision-0.12.1-setup.exe`. If SmartScreen appears, click **More info → Run anyway**.
2. On the options page there's **no** VIIPER checkbox, because you already have it. Finish the install.
3. The app opens. **Settings** shows **0.12.1**, your profiles are all there, and **Virtual Controller** goes green when switched on.

**VIIPER download path (optional, to prove the one-file setup works on a fresh PC)**

4. Uninstall VIIPER: **Windows Settings → Apps → Installed apps → VIIPER → Uninstall**. Restart if asked.
5. Run the setup again. The options page now shows **"Download and install VIIPER 0.9.0…"**, ticked.
6. Click **Install**. A "Downloading VIIPER" page shows progress. Then, while installing, Windows asks for **administrator permission** (that's for the usbip-win2 driver); click **Yes**.
7. The finish page offers **Restart now / Restart later**. Choose **Restart now**.
8. After restarting, the VIIPER icon is in the system tray. Open DualSense Precision → **Virtual Controller** → switch on. It goes green and **What Windows sees** shows the Xbox controller.

**Untick test**

9. Uninstall VIIPER again, run the setup, and **untick** the VIIPER box. It installs the app only: no download, no administrator prompt, no restart offer. The Virtual Controller page then shows the setup help. Reinstall afterwards with the box ticked.

**Re-check**

10. Uninstall DualSense Precision and choose **No** to keep your profiles. Reinstall: your profiles are still there.
11. Controller Tester, Stick Settings, Button Mapping, Macros and Appearance all work in the installed app.

## If something doesn't work

- **The build window says winget isn't available:** install Inno Setup from https://jrsoftware.org/isdl.php, then run `build-installer` again.
- **"VIIPER could not be downloaded":** check your internet connection. You can click **Yes** to continue without it and install it later with `winget install Alia5.VIIPER`.
- **A hash or "file is corrupt" error:** the downloaded file didn't match the one this version was tested with, so Setup refused it (on purpose). Tell Claude, because the pinned version may need updating.
- **Virtual Controller stays red after install:** restart Windows (the driver may need it), then check the VIIPER icon is in the tray.
