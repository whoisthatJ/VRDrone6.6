# Headset test: M3

## Install
```
adb install -r Builds/GateRush.apk
adb logcat -s Unity
```
Start **GateRush VR** from *Library → Unknown Sources*.

## Controls (Touch, Mode 2)
| | |
|---|---|
| Left stick | Throttle (Y, spring-centre = hover) / yaw (X) |
| Right stick | Pitch / roll |
| A | Arm / disarm. **Pull the left stick fully down while pressing A** (arming needs throttle low) |
| B | Flight mode: Acro → Angle → Horizon |
| X | Reset to the pad |
| Y | View mode: Goggles → Immersive → LOS. Hold Y = recentre the goggles screen |
| Left menu (≡) | Switch to / from Input Diagnostics |

## What to check
1. **Frame rate:** logcat shows `[GateRush] FPS avg ...` every 5 s. It should read ~72 in all three views. In Diagnostics press **B** to request 90 Hz, then check the log reads ~90 back in the flight scene.
2. **Goggles:** a curved screen in a dark room. Same image in both eyes; OSD text in the top-left of the feed. The screen should drift back in front of you when you turn your head. Hold Y to snap it.
3. **Immersive:** you sit on the quad with uptilt; turning your head looks around. Fast rolls/flips should darken the edges (vignette). Comfort rating: intense.
4. **LOS:** you stand left-behind the pad and see the quad; fly it in Angle mode (B).
5. **Feel:** is hover-assist throttle usable? Does Acro feel floaty or twitchy? (I need your subjective notes here.)

## BetaFPV radio (early check, M6 prep)
1. Plug the radio into the Quest with a USB-C OTG cable (LiteRadio 4: power the radio *off*, or pair it in Bluetooth joystick mode via Quest Settings → Devices → Bluetooth).
2. Press the left menu button to open **Input Diagnostics**. Use the right stick left/right to select the radio, up/down to page through axes.
3. Move each stick to all corners and flip every switch. Note which rows move, and their min/max.
4. Press **A** to save the report, then send me:
   ```
   adb pull /sdcard/Android/data/com.gaterush.vr/files/input-devices.txt
   adb logcat -d -s Unity | findstr "GateRush][Input"
   ```
