# DualSense Precision v0.13.0: Xbox controller support

New in this version: **Xbox controllers work too** (Xbox Series X|S, Xbox One, Xbox Elite and Xbox 360; by USB cable, Bluetooth or the Xbox Wireless Adapter). Plug one in and the app picks it up automatically, just like the DualSense.

## Install

1. Close DualSense Precision if it's open (right-click its tray/taskbar icon → Close, or just close the window).
2. Run **`DualSensePrecision-0.13.0-setup.exe`**. If SmartScreen appears, click **More info → Run anyway**.
3. Click through and finish. VIIPER is already on your PC, so there's no VIIPER checkbox. Your profiles are kept.
4. The app opens. **Settings** shows **0.13.0**.

(Building it yourself instead: double-click `build-installer` (the Command Script). Tests should end with **all succeeded**; File Explorer then opens on the new Setup.exe.)

## What works on an Xbox controller

- Stick curves, deadzones, sensitivity (at the Xbox pad's full 16-bit precision), trigger shaping, button mapping, macros, profiles.
- Controller Tester: buttons, drift, stick range, triggers, full diagnostic.
- Vibration (test button and game rumble through the virtual pad), scaled by **Vibration strength**.
- Battery as a level (Full / Medium / Low / Empty) on wireless pads; "Wired" on a cable.

**Not available on Xbox controllers** (the hardware doesn't have it): lightbar, player lights, mute light, adaptive triggers, touchpad, motion sensors. Those sections are hidden while an Xbox pad is connected and come back with a DualSense.

**Honest limits:** Windows doesn't let apps time an Xbox controller's reports the way it does a DualSense's. The app reads Xbox pads through Windows' **XInput**, checks about 1,000 times a second, and counts how often the input *changed*. That's labelled **Update rate (XInput)**; it is not a hardware report rate. Also, until hiding the real controller (HidHide, the next update) is in, games with the Virtual Controller on will see **two** Xbox controllers.

## Tests for this version

**A. DualSense still works (quick check)**
1. Plug in your DualSense. The sidebar shows it, Home shows **REPORT RATE ~250 Hz**, and Appearance shows the lightbar as before.
2. Unplug the DualSense before the Xbox tests.

**B. Xbox controller detected**
3. Connect your Xbox controller (cable, Bluetooth, or wireless adapter). Within about a second the sidebar bottom shows its name (e.g. *Xbox Series X|S Controller*) and **USB · wired** or **Wireless**.
4. **Home:** the two stick dots move with your sticks. The first tile reads **UPDATE RATE** ("Input changes via XInput"). **Battery** shows *Wired* on a cable, or *Full / Medium / Low* wireless.

**C. Controller page**
5. **Device** lists Type *Xbox controller (read through XInput)*, Connection, Vendor/product ID, XInput slot.
6. **Live inputs:** sticks show values from −1 to +1 with four decimals; triggers say **LT / RT** 0–255; **Buttons held** shows Xbox names (press A, B, LB, View, Menu…). Motion and touchpad say *Not on this controller*.
7. **Raw XInput state** bytes change as you move things.

**D. Performance**
8. The page explains it's an XInput update rate. Move both sticks continuously for a few seconds: **UPDATE RATE (XINPUT)** shows a steady number. Let go: it drops. **Dropped reports** and **Stalls** show *n/a*.

**E. Sticks and triggers**
9. **Stick Settings:** raise the inner deadzone; the blue dot stays centred longer. Change the curve; the blue dot follows it.
10. **Trigger Settings:** tabs say **LT (left) / RT (right)**, an info bar explains adaptive triggers are hidden, and the live meter follows the trigger. Change the deadzone and see the processed bar respond.

**F. Button Mapping**
11. The drawing shows **A B X Y, LB RB, LT RT, LS RS, View, Menu, Xbox**, with no touchpad or mute. Pressing a button lights it up and selects it.
12. Map **A → Keyboard Space**. Open Notepad and press A: it types a space. Set A back to **Default**.

**G. Macros**
13. Record a short macro using Xbox buttons, assign it to a button, and play it. Steps show Xbox names.

**H. Appearance**
14. Only **Vibration & trigger strength** shows (without the adaptive slider), plus an info bar. **Test vibration** rumbles the Xbox controller.
15. Set **Vibration strength** to 0 % and test: no rumble. 50 %: weaker. Put it back.

**I. Controller Tester**
16. An info bar says touchpad and motion tests are hidden. Button chips use Xbox names (17 of them).
17. **Run full diagnostic**: 4 steps (Buttons, Stick drift, Stick range, Triggers), then a report saying *Update rate (XInput…)* and *Touchpad and motion sensors — not on Xbox controllers*.

**J. Virtual Controller with an Xbox pad**
18. Switch the Virtual Controller **on**. **What Windows sees** lists two controllers: one labelled *your real Xbox controller (input to the app)* and one *the virtual pad (what games should use)*.
19. Move the sticks: both entries move, and the virtual pad shows your processed values (e.g. deadzone applied). Nothing freezes or drifts on its own.
20. Switch the Virtual Controller off: the virtual entry disappears and the app keeps reading your real controller.

**K. Plugging and switching**
21. Unplug (or switch off) the Xbox controller: the sidebar says **No controller**. Reconnect: it comes back by itself.
22. With the Xbox controller connected, plug in the DualSense: the app switches to the DualSense (pages relabel to Cross/Circle/L2…).
23. **Settings → "When a DualSense and an Xbox controller are both connected, use" → The Xbox controller**: it switches to the Xbox pad straight away. Set it back if you prefer.
24. Switch profiles from the sidebar while using the Xbox pad: settings change instantly, as with the DualSense.

## Good to know

- The **Xbox button** may also open the Windows Game Bar; that's Windows, not the app (Game Bar settings can turn it off).
- The **Share** button on Series pads isn't visible to apps through XInput.
- If an Xbox controller doesn't appear, check it shows in Windows' **Set up USB game controllers** (press Win+R, type `joy.cpl`).

## If something doesn't work

Copy any red text or error and send it to Claude, along with the test number.
