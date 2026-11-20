# Quick start

In 30 minutes: install the simulator and the SDK, run the ready-made example that flies the track by itself, and write
your first program. The other pages: [README.md](README.md).

The program receives only what the drone's sensors give (the FPV camera picture with analog noise, the IMU, the ToF
rangefinder, the LiDAR, the battery voltage) and answers with RC channels: throttle, roll, pitch, yaw. It does not
receive the coordinates of the drone or of the gates.

## What you need

| | Windows | Linux |
|---|---|---|
| System | Windows 10 or 11, 64-bit | Ubuntu 22.04 or 24.04, 64-bit |
| Video card | GTX 1650 level or better | the same, with the driver installed |
| Python | 3.9 or newer | 3.9 or newer |
| Internet | for `pip install` | for `pip install` |

The Team Kit is a folder with the simulator (`Ushqyr.exe` or `Ushqyr.x86_64`) and the `bench` folder (SDK, examples,
solution template, tools). All commands below are run in this folder.

## 1. Install the SDK (5 minutes)

Windows (PowerShell or cmd):

```
py -m venv .venv
.venv\Scripts\activate
pip install ./bench/sdk/python opencv-python
```

Linux:

```
python3 -m venv .venv
source .venv/bin/activate
pip install ./bench/sdk/python opencv-python
```

The SDK depends only on `numpy` and `msgpack`. OpenCV is needed by the example, not by the SDK.

## 2. Start the simulator in AI Bench mode (2 minutes)

Start the simulator from the Team Kit: it opens AI Bench straight away.

- Windows: `Ushqyr.exe`
- Linux: `chmod +x Ushqyr.x86_64`, then `./Ushqyr.x86_64`

In the full game, press **AI Bench** in the main menu or start it with the `-bench` option.

The hall opens with the track `H1_Oval` (6 gates in an oval). The top left corner says "Waiting for a program on port
5757". The drone stands on the start pad.

## 3. Run the example (3 minutes)

In a second terminal window (do not forget to activate `.venv` again):

```
python bench/examples/color_pid/color_pid.py --show
```

The drone takes off, finds a gate by the colour of its LEDs and flies round the loop. What you will see:

- top left: the gate counter and the attempt time;
- bottom right: the picture from the onboard camera, exactly the one the program receives;
- in a separate window (option `--show`): the same picture with a box around the gate that was found.

Key **C** switches the view: the camera behind the drone or an overview of the hall. In the full game **Esc** returns to the menu.

An attempt lasts 120 seconds. To stop after one lap, add `--points 6`.

How the example works is described in `bench/examples/color_pid/README.md`.

## 4. Write your own program (10 minutes)

Save it as `my_first.py` and run it the same way as the example:

```python
from ushqyr_bench import Sim, RC

with Sim.connect() as sim:
    obs = sim.reset()                       # drone on the start pad, time from zero
    for _ in range(15):                     # arming: switch off...
        obs = sim.step(RC(arm=False))
    for _ in range(15):                     # ...then on at zero throttle
        obs = sim.step(RC(arm=True))
    while sim.running:
        frame = obs.camera.image            # numpy, 480 x 640 x 3, RGB
        gyro, accel = obs.imu.gyro, obs.imu.accel
        height = obs.tof.range              # metres to the floor, if obs.tof.valid
        scan = obs.lidar.ranges             # 360 ranges round the circle, 0 = nothing measured
        rc = RC(throttle=0.56, pitch=0.1, arm=True)   # your algorithm goes here
        obs = sim.step(rc)                  # the simulator makes one step (1/100 s)
```

Important points about control:

- The channels are as on a real radio. `roll`, `pitch`, `yaw` from -1 to 1, `throttle` from 0 to 1.
- **Arming.** The flight controller turns the motors on only if the `arm` switch goes from "off" to "on" with the
  throttle down. First send a few packets with `arm=False`, then `arm=True` and `throttle=0`, and only then add
  throttle.
- The default mode is Angle (the drone levels itself): `pitch=0.2` is a forward tilt of about 9 degrees.
- Hover throttle is about 0.56.
- Simulator time advances only with your `step()` calls. If the program thinks for a long time, the flight simply
  goes slower; the result does not change.

Next, start from a copy of `color_pid.py`: the class `GateDetector` finds the gates, the class `ColorPid` steers. All
SDK classes are described in [sdk.md](sdk.md).

## 5. Training mode

By default the simulator runs in training mode. It additionally offers:

- `sim.reset()` as many times as you like, with a choice of the noise seed: `sim.reset(seed=7)`;
- ground-truth coordinates and the score for debugging: `Sim.connect(truth=True)`, then `obs.truth`;
- a faster-than-real-time run: `Sim.connect(realtime=False)`.

Competition mode has none of this. To check the example on several noise seeds:

```
cd bench/examples/color_pid
python evaluate.py --runs 10
```

## What next

| Task | Page |
|---|---|
| Understand what points are given for | [rules.md](rules.md) |
| Other tracks, your own track (key **T**) | [tracks.md](tracks.md) |
| Train a gate detector | [dataset.md](dataset.md) |
| Package a solution and check it as on the server | [docker.md](docker.md) |
| Work through ROS 2 | [ros2.md](ros2.md) |
| Something does not work | [troubleshooting.md](troubleshooting.md) |

The `color_pid` example reliably completes `H1_Oval` and most of the ready-made tracks, but it looks for only one LED
colour and flies slowly. It is a starting point, not a finished solution.
