# color_pid: the sample solution

A colour detector plus PID, the "ready example" of the task. It flies by itself, without knowing the track:

| Track | Full laps (5 noise seeds) | Lap time |
|---|---|---|
| `H1_Oval` | 5 of 5 | 26 s |
| `H2_Eight` | 5 of 5 | 43 to 57 s |
| `P1_Columns` | 4 of 5 | 51 to 53 s |
| `H3_Slalom` | 5 of 5 | 38 to 39 s |
| `P2_Maze` | 5 of 5 | 54 to 58 s |
| `X1_Mixed` | 0 of 5 (2 of 10 gates) | none: it only looks for magenta, and eight gates have other colours |

```
pip install ../../sdk/python opencv-python
python color_pid.py --show
```

Start the simulator in AI Bench mode first (menu **AI Bench**, or `Ushqyr.exe -bench`).

| File | What it is |
|---|---|
| `color_pid.py` | The solution. `GateDetector` finds the gates, `ColorPid` flies |
| `evaluate.py` | Flies several noise seeds and counts full laps (training mode) |

## How it works

1. **Detect.** The picture goes to HSV, pixels of the gates' LED colour (magenta on most public tracks) are kept, and
   every outline becomes a box. Boxes that are not filled like one frame are several gates run together and are not
   trusted.
2. **Measure.** A box becomes a direction and a distance through the lens model from the handshake. Sizes are taken
   as angles (a wide lens stretches things near the picture's edge), and from the box's width and top edge only: a
   shiny floor mirrors the LEDs and makes the blob taller than the gate.
3. **Follow one gate.** The gate being flown at is found again in each frame by its direction in the world (the gyro's
   heading plus the bearing in the picture). In the picture it swings about with every turn; in the world it stands
   still.
4. **Turn, climb, go.** A PD controller on the bearing drives the yaw stick; a PID on the ToF range holds the height of
   the gate's centre; pitch forward, less when the gate is far off to the side.
5. **Sideways slide.** The bearing changes because the drone turns (the gyro says how fast) and because it slides
   sideways. Bearing rate plus turn rate leaves the slide, and the roll stick leans against it. Without this the drone
   swings wide in every turn and misses.
6. **Fly through.** When the gate is closer than 1.5 m, or no longer fits the picture, steering stops and the drone
   flies straight for about a second.
7. **Which gate next.** A gate scores again only after four others, so flying a small circuit of the same gates earns
   nothing. There is no position sensor, but the drone can guess where it is from its own commands (tilt pushes, air
   brakes) and the gyro. It notes where the last four gates it passed stood, and takes the nearest gate in view that
   does not lie in the direction of one of them.
8. **Search.** No such gate in view: turn on the spot, towards where another gate was last seen, until one appears.
9. **Obstacles.** If the LiDAR shows something within a metre straight ahead that is not the gate, the drone stops
   and steps towards the side with more room.

It uses the camera, the gyro, the ToF and the LiDAR. It does not use the accelerometer or the battery voltage.

## Limits you will hit

- One LED colour (`GateDetector(hue=...)` changes it). Tracks with several colours or coloured distractors need more.
- About 2 m/s. It is tuned for the whoop; a heavier or stronger drone needs other gains.
- Its memory is four gates and its position guess drifts. Where the next gate is far away or hidden it loses time
  turning round, and sometimes skips a gate (the one failed run on `P1_Columns`).
- It reacts to obstacles only when they are a metre away and dead ahead.
