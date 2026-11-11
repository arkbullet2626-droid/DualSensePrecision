# DualSense Precision v0.14.0: hide your real controller from games

New in this version: games can be made to see **only the tuned virtual controller**, so Rocket League (and others) stop picking up your real controller as a second player. It uses **HidHide**, a free, signed driver by Nefarius that Setup can download and install for you. Hiding is **off until you switch it on** in the app.

## Install

1. Close DualSense Precision.
2. Run **`DualSensePrecision-0.14.0-setup.exe`**. If SmartScreen appears: **More info → Run anyway**.
3. On the options page, leave **"Download and install HidHide 1.5.230…"** ticked. (It only appears if HidHide isn't installed yet.)
4. Click **Install**. A download page appears briefly. Windows then asks for **administrator permission** for the HidHide driver: click **Yes**.
5. At the end choose **Restart now**. The restart is needed so HidHide can attach to your controllers.

## Switch hiding on

1. Open DualSense Precision, connect your controller, go to **Virtual Controller** and switch the virtual controller **on**.
2. In the new card **"Hide your real controller from games"**, switch it **On**. Read the explanation and click **Hide it** (asked once).
3. The status line says *Hiding your Xbox controller from games (N device entries)…*.
4. **Start (or restart) your game afterwards.** A game that was already open keeps seeing the controller it had.

## Tests for this version

**A. Install**
1. Setup shows the HidHide option, downloads it, Windows asks for permission, and the finish page offers a restart. After restarting, **Settings** shows **0.14.0**.

**B. Before switching hiding on**
2. With the virtual controller on and hiding off, click **Check what games see**. It reports *Games see 2 Xbox-style controllers; this app sees 2.*

**C. Switch hiding on**
3. Switch **Hide your real controller from games** on: the explanation dialog appears. **Cancel** leaves it off; switch it on again and click **Hide it**.
4. The status says it's hiding your controller, and the yellow *Games may see two controllers* warning disappears.
5. Click **Check what games see**: after a second or two it says *✓ Games see exactly one Xbox controller: the virtual pad.*
   - If it still says 2: unplug the controller and plug it back in (or switch a wireless pad off and on), then check again.
6. The app still reads your controller: the Home stick dots move, and **What Windows sees** still lists both pads (the app is allowed to see hidden ones).

**D. Rocket League (the original problem)**
7. Start Rocket League **after** step 5. Press **Menu/Start**: only one player joins, and your tuned settings apply (try a big inner deadzone and see the car ignore small stick movement).

**E. Undo**
8. Switch hiding **off**: status says *Off…*, and **Check what games see** shows 2 again.
9. Switch hiding back on, then turn the **virtual controller off**: hiding is undone straight away (games see your real controller again, so it still works in games).
10. With hiding on, **close the app**. Open the Windows Game Controllers panel (Win+R, type `joy.cpl`): your controller is listed again.

**F. DualSense too (optional)**
11. Connect your DualSense instead, with the virtual controller and hiding on: status says *Hiding your DualSense…*. In a game that supports PlayStation controllers natively, only the virtual Xbox pad shows up.

**G. Everything from 0.13.0 still works**
12. Quick spot-check: stick settings, button mapping and the Controller Tester work with the Xbox pad while hiding is on.

## Good to know

- Only DualSense Precision is allowed to see the hidden controller. Steam, browsers and other controller apps won't see it while hiding is on.
- If the app ever closes unexpectedly, the next start un-hides your controller. You can also open **HidHide Configuration Client** (Start menu) and untick the device.
- Uninstalling: HidHide is a separate program (*Windows Settings → Apps → HidHide*).

## If something doesn't work

Send Claude the test number, plus the text of the status line and the **Check what games see** result.
