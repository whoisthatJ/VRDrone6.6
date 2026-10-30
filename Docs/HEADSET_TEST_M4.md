# Headset test: M4 (racing)

```
adb install -r Builds/GateRush.apk
adb logcat -s Unity
```

## Race flow
1. On the pad, in **Goggles** view: the first gate ahead is **green** (next), the one after is **orange**.
2. **Arm** (A, left stick fully down). A big **3 … 2 … 1** counts down while the quad is held, then **GO!**
3. Fly gate 0 (start/finish), then follow the green gates: 8 gates, one floating 3 m up, one wide hurdle you fly *under*.
4. Top-right OSD: `LAP n/3`, current lap time, total, best.
5. Passing the wrong gate shows **MISSED GATE**: go back for the green one.
6. Each lap flashes its time in the centre (green = personal best). From lap 2 a **cyan ghost** flies your best lap.
7. After lap 3: **FINISH** + results (lap times, total, leaderboard rank, track lap record).
8. **Restart**: click the left Touch stick (or T / gamepad left-stick click).

## Crashes
- A hard hit (> 7 m/s) crashes; after 1.5 s you respawn **in front of the last gate you passed**, held in the air.
  Re-arm (A, left stick down), then push throttle up to continue. The clock keeps running.

## What to tell me
- Do gate timings feel right (no missed passes when you clearly flew through)?
- Is the course fun/too tight for Touch controllers? (Gates can be resized later.)
- Ghost visible and smooth?
- FPS in logcat during a race (`[GateRush] FPS avg`).
