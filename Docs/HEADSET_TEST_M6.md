# Headset test: M6 (BetaFPV radios)

> M6 was not built into an APK (builds are skipped on request). Build once before testing:
> `GateRush/Build/Build Quest APK` in the Editor. That first build also compiles the Android radio plugin
> (`Assets/Plugins/Android/GateRushRadio.androidlib`). If Gradle complains about it, send me the error.

```
adb install -r Builds/GateRush.apk
adb logcat -s Unity
```

## 1. Connect
- **LiteRadio 2 SIM**: joystick mode, USB-C OTG cable into the Quest.
- **LiteRadio 3 / 2 SE**: joystick mode; if nothing shows up, enable Android/joystick compatibility in BETAFPV Configurator.
- **LiteRadio 4**: pair in *Quest Settings > Devices > Bluetooth* (Bluetooth joystick mode), or USB-C with the radio **switched off**.

## 2. Radio setup (main menu)
- Does it show your radio's name, "via Input System", and a preset?
- Move every stick and switch: which `chN` rows move? Do the THR/YAW/PIT/ROL/ARM/MODE tags match?
- If some sticks/switches don't move: press **Reader** until it says *Android raw*, and check again.

## 3. Calibrate
- Press **Calibrate** and follow the steps; holding each position for a second advances by itself.
- After saving: arm with your arm switch (throttle low), fly a lap. Mode switch = Acro / Angle / Horizon.

## 4. Hot-plug
- During a race, unplug the cable (or switch Bluetooth off): the race pauses with "Radio disconnected".
- Reconnect, centre the sticks, flip arm on: it resumes.

## 5. Send me
```
adb pull /sdcard/Android/data/com.gaterush.vr/files/input-devices.txt
adb pull /sdcard/Android/data/com.gaterush.vr/files/radios.json
adb logcat -d -s Unity | findstr "GateRush][Input"
```
plus: which radio and mode, which `chN` each stick/switch moved, and whether the default preset already flew correctly.
I'll turn that into verified presets.
