# DualSense Precision 0.18.3 — release candidate for 1.0

**0.18.3** (from Claude's test of 0.18.2 on your PC):
- **Setup closes the running app for you**, the clean way (same as tray → Exit: your controller is un-hidden and the virtual pad removed first). Updating from 0.18.2 or older still asks you once, because those versions don't understand the request yet.
- Fixed a hidden error logged when **Restart now** closed the window right after you typed a hex colour.

### Tests for 0.18.3
U1. With 0.18.3 installed and running (even just in the tray), run the 0.18.3 setup again: it doesn't ask you to close anything, installs, and **Finish** starts the app again. Your controller works normally afterwards.
U2. Type a hex colour, press Enter, then **Restart now**: no new entry appears in `errors.log` (Settings → Open logs folder).

---

# From 0.18.2

**0.18.2:** after **Restart now** into a light theme, Windows-drawn pop-ups (dropdown lists, dialogs) now really start light. Before, the app could still think it had started dark and kept showing the "restart" note.

### Tests for 0.18.2
T1. Pick **Paper (light)** → **Restart now**: the app reopens in Paper and the *"You switched between a light and a dark theme…"* note is **gone**. Open **Reset app…** and press **Cancel**: the dialog is light.
T2. Switch back to your colours → **Restart now**: the note is gone again and dialogs are dark.

---

# From 0.18.1

**0.18.1** fixes what Claude found testing 0.18.0 on your PC:
- **Restart now** and **Reset app** work again (they hit an error while shutting down and the app didn't come back).
- If VIIPER is installed but not running, the app now **starts it for you** (once per session) instead of waiting for you to sign out and in.
- While VIIPER is missing the log no longer fills up with two lines every few seconds.
- On light themes, green and red status text (e.g. "Free · Open source", "Connected") is darker and readable.
- The Tester and Stick Settings say **amber** instead of "blue" for the pressed/processed markers.

### Tests for 0.18.1
R1. Settings → Interface → pick **Paper (light)** → **Restart now**: the app closes and reopens in Paper within a few seconds. Pick your own colours again (or Opifex) → **Restart now** again.
R2. **Reset app…** → leave *Also delete my profiles* unticked → confirm: the app reopens with default settings and the welcome guide, your profiles are still there.
R3. Quit VIIPER if it's running (its tray icon → Quit, or Task Manager → end `viiper.exe`), then open DualSense Precision: within a few seconds **Virtual Controller** turns green without you starting VIIPER yourself.
R4. In a light theme, the green "Connected" dot text in the sidebar and the green text on About are easy to read.

---

# From 0.18.0

**0.18.0:**
- **New logo:** the DP monogram (cut-corner version) replaces the old "P" square in the title bar and on About, and is the new app, taskbar and tray icon.
- **The logo follows the theme:** every preset has its own logo colour (amber for Opifex, blue for Midnight, pink for Synthwave…), and the colour editor has a **Logo** slot for custom palettes. (The taskbar/tray icon file stays amber.)
- **Smoother interface scale:** while you drag the slider it stays exactly under your mouse and the rest of the app zooms smoothly around it; when you let go, the new size settles into place.

### Tests for 0.18.0
L1. The title bar shows the new DP mark; About shows it large; the taskbar, Alt-Tab and tray show the new amber DP icon.
L2. Switch presets: the DP mark changes colour with each (Opifex amber, Midnight blue, Synthwave pink, Deep Sea cyan, Paper brown…).
L3. In the colour editor, change **Logo** to any colour: the title-bar and About marks follow live, and it's kept after reopening.
S1. Settings → Interface → drag **Interface scale** slowly from 100 % to 150 % and back: the slider stays under the mouse the whole time and everything else grows/shrinks smoothly around it.
S2. Let go: the page settles smoothly at the new size with the slider still roughly where it was. Clicking the bar (instead of dragging) and the **100 %** button also animate smoothly.
S3. Use the keyboard (click the slider, then arrow keys): each step applies.

---

# From 0.17.0

**0.17.0:** 12 colour presets, including three fun ones (Synthwave, Deep Sea, Toxic), Deep Red and three **light** themes (Paper, Daylight, Sand). With a light theme, the app's built-in controls switch to their light style too. The small logo always stays Opifex amber, whatever theme you pick.

### Tests for 0.17.0
P1. Settings → Interface → **Start from a preset**: try **Synthwave**, **Deep Sea**, **Toxic** and **Deep Red**. Each repaints the whole app straight away, and the amber "P" logo in the title bar doesn't change.
P2. Pick **Paper (light)**: the app goes light with dark text, and toggles, sliders, text boxes and dropdowns all look right and are readable. The title-bar minimise/close buttons stay visible.
P3. A note appears: *"You switched between a light and a dark theme…"*. Click **Restart now**: the app reopens in Paper, and dropdown menus and dialogs (e.g. Reset app…) are light as well.
P4. Try **Daylight (light)** and **Sand (light)**, then go back to **Opifex (default)** (restart when asked).

---

# From 0.16.1

**0.16.1:** the colour editor now has four colours (Background, Panels and cards, Borders and lines, Main text). The accents always stay Opifex amber and orange, and presets only change those four.

---

# From 0.16.0

## New in 0.16.0: your own colours and interface scale

**Settings → Interface** now has:
- **Interface scale** (75–150 %): makes everything in the window bigger or smaller, live.
- **Colours**: six parts of the app you can recolour — Background, Panels and cards, Borders and lines, Main text, Main accent, Second accent. Click a swatch to drag the colour picker (spectrum + sliders), or type a hex code. Everything repaints straight away; lighter shades are worked out for you.
- **Presets** (Opifex, Midnight, Forest, Crimson, Graphite), **Reset to Opifex colours**, and a readability check that warns if text and background are too close.

### Tests for 0.16.0
A1. Settings → Interface → drag **Interface scale** to 125 %: the whole window (sidebar and pages) grows, still fits the window, and clicks still land on the right things. **100 %** puts it back. Close and reopen the app: your scale is kept.
A2. Pick the **Midnight** preset: the background, cards, borders and text turn blue-grey instantly while the amber accent stays. Pick **Opifex (default)** again.
A3. Click the **Panels and cards** swatch and drag around the colour picker: cards and the sidebar footer follow live.
A4. Type `#102030` into **Background**'s hex box and press Enter: it applies. Type `hello`: it goes back to the previous colour.
A5. Set **Main text** to a dark colour like `#202020`: the readability line warns it may be hard to read. Click **Reset to Opifex colours**.
A6. Close and reopen the app: your custom colours are kept.

---

## From 0.15.0

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
2. Run **`DualSensePrecision-0.18.3-setup.exe`**. If SmartScreen appears: **More info → Run anyway**.
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
10. **About** shows *Version 0.18.3*. **Check for updates** says you're up to date.
11. The buttons on About open the right things: **Licence** and **Third-party notices** open text files; **Source code** / **Report a problem** / **Privacy policy** / **opifexstudios.ca** open the browser (GitHub pages need you signed in until the repo is public, and the privacy section is added to the website at release).

**F. Troubleshooting**
12. Settings → **Open logs folder**: a folder opens with `app-<today>.log`. It lists start-up, the controller connecting, the virtual controller on/off and hiding, but no button presses.
13. **Export diagnostics…** → save the .zip: it contains `summary.txt`, `settings.json` and the logs.
14. **Reset app…** → **Cancel** changes nothing. Then do it for real with **Also delete my profiles** *unticked*: the app restarts with default settings (the welcome guide shows again), your profiles are still there, and your controller isn't left hidden.

**G. Everything else still works (quick check)**
15. Controller detected, sticks/triggers/mapping/macros work, the virtual controller goes green, and hiding still gives one controller in Rocket League.
