# Headset test: M7 (VR track editor)

> No APK was built for M7. Build once (`GateRush/Build/Build Quest APK`) before testing.

Main menu → **Track editor** → *New track in: Open Field / Hangar / Dockyard*, or *Copy and edit* a built-in.

## Controls
| | Right controller | Left controller |
|---|---|---|
| Stick | while dragging: X rotate, Y push/pull · otherwise: snap turn | move |
| Trigger | point + pull: select / drag; on a panel button: press | fly down |
| Grip | hold + stick Y while dragging: scale | fly up |
| A / X | A: add the current piece where you point | X: undo |
| B / Y | B: delete selected | Y: next piece type |

The **wrist panel** (left hand) has every action: piece type `< >`, Add, Delete, Snap, Earlier/Later (race order),
Reverse, Laps −/+, Undo/Redo, Save, **Test fly**, Spawn (select the start pad), Exit (saves).

Gate numbers float above each gate (`START`, 1, 2, ...); the line shows the race order.

## Check
1. Build a small course: add 4–6 gates, move/rotate/scale them, add a few props, move the spawn pad.
2. Reorder a gate (Earlier/Later) and watch the numbers and line update.
3. **Test fly**: race it; the menu button brings you back to the editor where you were.
4. **Exit**, then *Choose track*: your track is listed; fly it from there too.
5. Comfort/usability: is pointing precise enough? Is the panel readable on the wrist? Movement speed OK?

## Sharing
Tracks are `.json` files in `/sdcard/Android/data/com.gaterush.vr/files/tracks/`.
```
adb pull /sdcard/Android/data/com.gaterush.vr/files/tracks/ ./my-tracks
adb push friend-track.json /sdcard/Android/data/com.gaterush.vr/files/tracks/
```
