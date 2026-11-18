# color_pid: the sample solution

A colour detector plus PID, the "ready example" of the task. It flies `H1_Oval` by itself: a full lap in 10 of 10 test
runs, about 24 s per lap with the placeholder whoop.

```
pip install ../../sdk/python opencv-python
python color_pid.py --show
```

Start the simulator in AI Bench mode first (menu **AI Bench**, or `Ushqyr.exe -bench`).

| File | What it is |
|---|---|
| `color_pid.py` | The solution. `GateDetector` finds the gate, `ColorPid` flies |
| `evaluate.py` | Flies several noise seeds and counts full laps (training mode) |

## How it works

1. **Detect.** The picture goes to HSV, pixels of the gate's LED colour (magenta on the public tracks) are kept, small
   gaps are closed, and the outline with the largest box is taken as the nearest gate.
2. **Turn.** A PD controller on the gate's bearing drives the yaw stick.
3. **Height.** A PID on the downward ToF range holds the height. The gate's centre in the picture, together with its
   apparent size, tells which height to hold, so gates need not stand on the floor.
4. **Speed.** Pitch forward, less when the gate is far off to the side.
5. **Sideways slide.** The bearing changes because the drone turns (the gyro says how fast) and because it slides
   sideways. Bearing rate plus turn rate leaves the slide, and the roll stick leans against it. Without this the drone
   swings wide in every turn and misses.
6. **Fly through.** When the frame no longer fits the picture, steering stops and the drone flies straight for a second.
7. **Search.** No gate in sight: turn left on the spot until one appears.

It uses the camera, the gyro and the ToF. It does not use the accelerometer or the battery voltage, has no memory of
the track, and flies at about 2 m/s. Those are the obvious places to do better.

## Limits you will hit

- One LED colour (`GateDetector(hue=...)` changes it). Tracks with several colours or coloured distractors need more.
- "Largest outline is the next gate" is true on an oval and false on a track that doubles back.
- The search always turns left.
