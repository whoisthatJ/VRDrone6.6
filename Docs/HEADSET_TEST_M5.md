# Headset test: M5 (content + settings)

```
adb install -r Builds/GateRush.apk
adb logcat -s Unity
```

The app now starts in the **main menu**. Point with either controller and pull the **trigger** to click.

## Check
1. **Menu**: readable at arm's length? Laser pointers on both hands, buttons highlight and click.
2. **Choose quad**: 5" Race, 5" Freestyle, 65mm Whoop. **Choose track**: Open Field, Hangar 7, Dockyard. **FLY** loads it.
3. In flight, the **left menu button (≡)** returns to the menu.
4. **Feel per class** (most useful feedback for me):
   - Race: twitchy and fast. Freestyle: heavier, smoother. Whoop: slow and floaty (try it in Hangar 7).
   - Anything floaty, twitchy, or "on rails"? Throttle too sensitive?
5. **Settings** (change, fly, come back, the values are kept after restarting the app):
   - Rates: switch to *Custom*, try Betaflight vs Actual; the orange line shows max deg/s.
   - Camera: tilt, FOV, default view, goggles screen size/follow.
   - Input: Touch throttle *Centre = hover* vs *Trigger*; deadband; expo.
   - Comfort: vignette strength, **90 Hz**. Check that `[GateRush] FPS avg` reads ~90 in the log.
   - Physics: crash speed, drag, motor lag.
6. **Performance**: FPS in all three tracks, especially Hangar 7 and Dockyard.

Settings file on the headset: `/sdcard/Android/data/com.gaterush.vr/files/settings.json`.
