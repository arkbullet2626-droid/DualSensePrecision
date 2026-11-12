# DualSense Precision 0.15.0 — release candidate for 1.0

Everything planned for 1.0 is in this build. If the tests below pass, the next build is **1.0.0**.

## What's new

- **Opifex Studios look:** warm black, cream text, amber accent with an ember gradient, plus the studio fonts (Oxanium headings, Figtree text, IBM Plex Mono numbers).
- **Tray icon:** closing the window keeps the app running in the notification area, so your remaps, the virtual controller and hiding keep working. Right-click the icon → **Exit** to quit completely.
- **Start with Windows:** optional, starts quietly in the tray. You can turn it on in Setup or in Settings.
- **Welcome guide:** shown on first launch. You can open it again from About.
- **About page:** version, update check, help, licence, credits and privacy.
- **Update check:** asks GitHub for the newest version on start (can be switched off) and shows a banner when one is out.
- **Troubleshooting:** a local log, **Export diagnostics** (a .zip you can choose to share) and **Reset app**.
- Game-rules reminders next to macros and keyboard/mouse remaps.

## Install

1. Close DualSense Precision completely (if it's in the tray: right-click the icon → **Exit**).
2. Run **`DualSensePrecision-0.15.0-setup.exe`**. If SmartScreen appears: **More info → Run anyway**.
3. VIIPER and HidHide are already installed, so their boxes don't appear. Leave **Start DualSense Precision with Windows** unticked for now (test 9 covers it).

## Tests

**A. Look**
1. Everything is in the Opifex colours: warm near-black background, cream text, amber toggles/sliders/buttons (with **dark** text on amber buttons), and the small "P" logo in the title bar has an amber-to-orange gradient.
2. Page titles use the squared Oxanium font, body text uses Figtree, and numbers (labels like REPORT RATE, the raw monitor) use IBM Plex Mono. If text looks like plain Windows Segoe UI, tell Claude.
3. Click through every page: nothing is unreadable, clipped or still blue.

**B. Welcome guide**
4. On first start after updating, the **Welcome to DualSense Precision** guide appears with 4 steps. **Open Virtual Controller** takes you there; it doesn't appear again on the next start.

**C. Tray**
5. Close the window with the X: a Windows notification says it's still running, and the app's icon is in the notification area (it may be under the ^ arrow). Your controller still works in a game or in Windows' `joy.cpl`.
6. Left-click the tray icon: the window comes back. Right-click: **Open DualSense Precision** / **Exit (stops the virtual controller)**.
7. Choose **Exit**: the app quits, the virtual controller disappears, and your real controller is visible to games again.
8. Settings → turn off **Closing the window keeps the app running in the tray**: now the X quits the app completely. Turn it back on.

**D. Start with Windows**
9. Settings → turn on **Start with Windows**. Sign out and back in (or restart): the app starts in the tray without opening a window. Turn it off again if you don't want it.

**E. Updates and About**
10. **About** shows *Version 0.15.0*. **Check for updates** says it couldn't check (the repository is still private, so this is expected until release) or that you're up to date.
11. The buttons on About open the right things: **Licence** and **Third-party notices** open text files; **Source code** / **Report a problem** / **Privacy policy** / **opifexstudios.ca** open the browser (GitHub pages need you signed in until the repo is public, and the privacy section is added to the website at release).

**F. Troubleshooting**
12. Settings → **Open logs folder**: a folder opens with `app-<today>.log`. It lists start-up, the controller connecting, the virtual controller on/off and hiding, but no button presses.
13. **Export diagnostics…** → save the .zip: it contains `summary.txt`, `settings.json` and the logs.
14. **Reset app…** → **Cancel** changes nothing. Then do it for real with **Also delete my profiles** *unticked*: the app restarts with default settings (the welcome guide shows again), your profiles are still there, and your controller isn't left hidden.

**G. Everything else still works (quick check)**
15. Controller detected, sticks/triggers/mapping/macros work, the virtual controller goes green, and hiding still gives one controller in Rocket League.
