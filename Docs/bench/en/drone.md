# Drone and sensors

The numbers on this page are the defaults. The exact values of the current run always come in the simulator's
handshake: `sim.info` (see [sdk.md](sdk.md)). Read them from there instead of hard-coding them.

## Two classes

| Class | Drone | Who defines it |
|---|---|---|
| **AI MISSION** | The organisers' whoop, the same for everyone | The organisers |
| **AI CORE** | The team's own drone, described in `drone.yaml` | The team, within the class limits |

## The whoop (AI MISSION)

| | |
|---|---|
| Size | 65 mm, 4 motors, ducted propellers |
| Mass | 32 g |
| Battery | 1S |
| Thrust-to-weight | about 3 : 1 |
| Hover throttle | about 0.56 |
| Collision radius | 4.5 cm |

**The model is a placeholder.** It is not calibrated against the organisers' real drone: the handshake says
`drone.calibrated = false`. After calibration the thrust, inertia and drag will change, so do not tune your solution
too closely to today's numbers.

## Control

The program sends RC channels, like a real transmitter. Direct motor commands are not available.

| Channel | What it is | Range in the SDK |
|---|---|---|
| Roll | roll, + right | -1 to 1 |
| Pitch | pitch, + forward (nose down) | -1 to 1 |
| Throttle | throttle | 0 to 1 |
| Yaw | yaw, + right (clockwise seen from above) | -1 to 1 |
| AUX1 | arming | off / on |
| AUX2 | flight mode | `angle`, `horizon`, `acro` |

| | |
|---|---|
| Control step rate | 100 Hz |
| RC link delay | 10 ms |
| Failsafe | 500 ms without packets: the motors are cut until the drone is armed again |

**Flight modes.** Angle: the drone levels itself, the stick sets the tilt angle. Horizon: the same near the stick
centre, at the edges the drone rotates. Acro: the stick sets the rotation rate, the drone does not level itself.

**Arming.** The motors turn on only when AUX1 goes from "off" to "on" with the throttle near zero.

## Camera

| | |
|---|---|
| Frame | 640 × 480, RGB, 8 bits per channel |
| Rate | 30 frames per second |
| Field of view | 120° horizontal, edge to edge |
| Tilt | 20° upward |
| Latency | 30 ms from capture to the program |
| Lens distortion | barrel, `k1 = 0.18` |
| Exposure | automatic |

**Lens.** Let `x_d = (u - cx) / fx`, `y_d = (v - cy) / fy`, `r² = x_d² + y_d²`. Then the tangents of the true angles
are `x_u = x_d · (1 + k1 · r²)`, `y_u = y_d · (1 + k1 · r²)`. The values `fx, fy, cx, cy, k1` come in the handshake.
An example of the calculation: the method `ColorPid._ray` in `color_pid.py`.

**Analog video link.** The picture passes through a model of an analog video link: soft detail, smeared and drifting
colour, noise, jumping lines and torn bands. The further the drone is from the start pad (where the receiver stands)
and the more obstacles there are in between, the worse the picture. A detector trained on clean images makes mistakes
on such a picture: train it on the simulator's images ([dataset.md](dataset.md)).

## IMU

| | Gyroscope | Accelerometer |
|---|---|---|
| Rate | 500 Hz | 500 Hz |
| Units | rad/s | m/s² |
| Noise (one standard deviation) | 0.004 | 0.08 |
| Constant bias per attempt | 0.003 | 0.04 |
| Limit | 2000 °/s | 16 g |

- Body axes: x forward, y left, z up.
- Gyroscope: x roll right +, y nose down +, z yaw left +.
- The accelerometer measures specific force: at rest on a level floor it reads (0, 0, +9.81).
- Motor vibration raises the noise: the higher the motor speed, the noisier both sensors.
- The IMU does not give attitude (angles). You have to compute it yourself.

One control step brings 5 IMU samples.

## ToF rangefinder

| | |
|---|---|
| Direction | down |
| Rate | 50 Hz |
| Range | 0.05 to 4 m |
| Noise | 5 mm + 1% of the distance |

Out of range, `valid = false` and `range = 0`.

## LiDAR

| | |
|---|---|
| Type | 2D, in the drone's own horizontal plane |
| Rays | 360, step 1° |
| Rate | 10 turns per second |
| Range | 0.15 to 12 m |
| Noise | 1 cm + 0.5% of the distance; 0.5% of the rays are lost |

Ray `k` points `k` degrees from the forward axis, with angles growing to the left (anticlockwise seen from above).
The value 0 means "nothing measured". The rays of one turn are taken one after another while the drone moves, so a
scan made in flight is bent. When the drone is tilted, the scan plane tilts with it and may touch the floor.

## Battery

The voltage, 10 times per second. Under load the voltage sags, towards the end of the attempt it drops, and the
thrust drops with it.

## Your own drone (AI CORE)

Describe the drone in `drone.yaml`: size, mass, motor thrust and lag, propellers, battery, controller limits, camera
tilt and field of view, the set of sensors. An example with comments: `bench/examples/drones/example_3inch.yaml`.

```
ushqyr-bench validate-drone drone.yaml
Ushqyr.exe -bench -bench-drone drone.yaml
```

The default class limits (the organisers may change them):

| Parameter | Limit |
|---|---|
| Size | at most 300 × 300 × 180 mm |
| Mass | 25 to 1500 g |
| Thrust-to-weight | 1.5 to 8 |
| Battery | up to 6 cells, up to 2200 mAh |
| Motor time constant | at least 10 ms |
| Rotation rate | up to 1000 °/s |
| Tilt angle in Angle | up to 70° |
| Camera tilt | 0 to 60° |
| Camera field of view | 60 to 150° |
| Sensors | from the set `camera, imu, tof, lidar, battery` |

How it works:

- Mass, thrust, motor lag, battery and limits are taken from the file as they are. Inertia, drag and controller gains
  are computed from size and mass: you cannot declare an "unphysical" drone through them.
- The simulator checks the file on loading by the same rules as `validate-drone` and refuses a drone that breaks
  them. The reasons are given in plain words.
- A sensor that is not in the `sensors` list is not sent.
- Collisions are computed with a sphere whose size matches the declared size of the drone.
- The file is plain YAML: nested keys by indentation, numbers, text, and lists in one line `[a, b, c]`.
