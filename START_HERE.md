# DualSense Precision v0.14.2: hiding fix

**New in 0.14.2:** switching on "Hide your real controller from games" no longer fails with *"HidHide request failed: The parameter is incorrect"*, and the app now records what it hides **before** hiding it, so it's always undone when you switch off or close the app. "Check what games see" also works for a DualSense now.

Install it like any update: close the app, run `DualSensePrecision-0.14.2-setup.exe`. HidHide is already installed, so its option won't appear.

---

# From 0.14.1: the installer fix

This version fixes Setup so **HidHide really installs** (in 0.14.0 it silently did nothing on some PCs).

What was wrong: HidHide's own installer can't run invisibly. It needs to show its screens to ask Windows for administrator permission, and one of its steps can show a small message box that has to be clicked. 0.14.0 tried to run it invisibly, so it quietly gave up. Setup now opens HidHide's installer normally for you to click through, and **checks afterwards that it really installed**. VIIPER's install also now shows a proper Windows permission prompt and is checked the same way.

## Install

1. Close DualSense Precision.
2. Run **`DualSensePrecision-0.14.1-setup.exe`**. If SmartScreen appears: **More info → Run anyway**.
3. On the options page, make sure **"Download and install HidHide 1.5.230…"** is ticked, then click **Install**.
4. A message explains what's next. Click **OK**. HidHide's own installer opens:
   - click **Yes** when Windows asks for permission;
   - click through its screens (Next, accept the licence, Install, Finish);
   - if a small **"Nefarius' Updater"** box appears, click **Close** (HidHide still installs);
   - if it offers to restart, choose **later** (Setup offers a restart at the end).
5. Back in DualSense Precision's Setup, finish and choose **Restart now**.

## Tests for this version

**A. HidHide installs**
1. Steps 3–4 above happen as described. If Setup says *"HidHide doesn't look installed yet"*, finish HidHide's window if it's still open and click **Retry**; tell Claude if Retry keeps failing.
2. After restarting, the Start menu has **HidHide Configuration Client**, and `C:\Program Files\Nefarius Software Solutions\HidHide` exists.
3. Open DualSense Precision. **Settings** shows **0.14.1**. On **Virtual Controller**, the "HidHide isn't installed" warning is gone.

**B. Hiding works (from 0.14.0)**
4. Virtual controller **on**, then **Hide your real controller from games → On → Hide it**. The status says it's hiding your controller.
5. **Check what games see** says *✓ Games see exactly one Xbox controller: the virtual pad.* (If it says 2, unplug and replug the controller, then check again.)
6. Start **Rocket League** after this: pressing Menu adds only one player.
7. Switch hiding off: the check shows 2 again. Close the app with hiding on: your controller is visible again (Win+R → `joy.cpl`).

**C. Status messages**
8. (Only if convenient) Before restarting after step 5 of Install, opening the app shows *"HidHide is installed but not running yet. Restart Windows…"* instead of "not installed".

**D. Optional: fresh-PC check for VIIPER**
9. On a PC without VIIPER (or after uninstalling it from Windows Settings → Apps), run Setup with the VIIPER box ticked: Windows asks for permission (click **Yes**), Setup finishes without errors, and after restarting the Virtual Controller goes green.

## Behind the scenes

Every build now installs the new Setup.exe on a clean Windows machine on GitHub (app + VIIPER) and refuses to publish if anything didn't install. HidHide can't be checked that way because its installer needs a person to click through it.
