# Headset test: M8 (polish + performance)

> No APK was built for M8. Build once (`GateRush/Build/Build Quest APK`) before testing.
> Baked lighting is in the scenes; if you ever run *Build All Scenes* again, re-run *GateRush/Lighting/Bake All Track Lighting*.

## Look & listen
1. **OSD** (Goggles view): voltage + per-cell at top-left, mAh, LQ; mode at the top; timer bottom-centre; throttle bottom-left; speed/altitude bottom-right; crosshair and a horizon bar that tilts with the quad.
2. **Battery sag**: full-throttle punches drop the voltage; after a few minutes the pack is weaker and LOW BATTERY flashes below 3.5 V/cell. (Settings › Physics › Battery sag to turn off.)
3. **Motor sound**: whine follows each motor; whoop is higher-pitched than the 5". Beep on GO and every lap. Crash = thump. In LOS view the sound comes from the quad.
4. **Analog video**: fly far away or behind containers/walls in Goggles view. Snow and breakup grow, LQ drops. Settings › Feed: Off / Light / Realistic.
5. **Props** blur into discs at speed (LOS view); **sparks** on crashes and hard bounces.
6. **Immersive**: horizon ring on the real horizon (Settings › Comfort); small lap/time HUD below your view in Immersive and LOS.

## Performance (most important)
`adb logcat -s Unity | findstr FPS` in each track, at 72 and 90 Hz, with Foveation Medium (default) and High.
Tell me the lowest numbers you see and where (track + view).

## Tell me
- Does the motor sound help or annoy? Volume OK?
- Is "Light" video noise a good default?
- Lighting: do the hangar and dockyard look better than before (softer bounce light, lamps glowing)?
