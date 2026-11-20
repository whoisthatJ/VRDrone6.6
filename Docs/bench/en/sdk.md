# Python SDK

The package `ushqyr_bench`. Sources: `bench/sdk/python`. It needs Python 3.9 or newer; dependencies: `numpy`,
`msgpack`.

```
pip install ./bench/sdk/python
```

The SDK hides and filters nothing: what the mode does not allow, the simulator simply does not send. A client in
another language can be written from [protocol.md](protocol.md).

## Connecting

```python
Sim.connect(host=None, port=None, token=None, truth=False, realtime=True, timeout=30.0, wait=0.0) -> Sim
```

| Argument | Meaning |
|---|---|
| `host`, `port`, `token` | Where the simulator is. If not given, they are taken from the environment variables `USHQYR_BENCH_HOST` (`127.0.0.1`), `USHQYR_BENCH_PORT` (`5757`), `USHQYR_BENCH_TOKEN` (empty) |
| `truth` | Training only: add ground-truth coordinates and the score to every observation |
| `realtime` | Training only: `False` lets the simulation run as fast as the computer allows. The result is the same |
| `timeout` | How many seconds to wait for the simulator's answer |
| `wait` | How many seconds to keep retrying the connection while the simulator starts. In Docker set `wait=120` |

In a solution for the qualification call `Sim.connect()` without an address and token: the server passes them through
environment variables.

`Sim` works as a context manager: `with Sim.connect() as sim:` closes the connection itself.

## Sim

| Field | What it is |
|---|---|
| `sim.info` | The simulator's handshake: a dictionary with the mode, the drone, the rates and the camera calibration (see below) |
| `sim.mode` | `"training"` or `"competition"` |
| `sim.control_hz` | Control steps per second of simulator time (100) |
| `sim.realtime` | `True` if the simulator follows the wall clock and does not wait for `step()` |
| `sim.running` | `True` while the attempt is running |
| `sim.status` | `"waiting"`, `"running"`, then the reason the attempt ended |

| Method | What it does |
|---|---|
| `sim.reset(seed=None)` | Starts the attempt and returns the first observation. Training: as many times as you like, `seed` sets the sensor noise. Competition: once, `seed` has no effect |
| `sim.step(rc)` | Sends one packet of channels; the simulator makes one step and returns an observation. `sim.step(None)` makes a step without a packet, as if it were lost |
| `sim.done()` | Says that the program has finished. The attempt ends with status `finished` |
| `sim.send(rc)` | Realtime mode: sends a packet and waits for nothing |
| `sim.latest(wait=False)` | Realtime mode: the newest observation that has arrived, or `None`. With `wait=True` it waits for the next one |
| `sim.ping(payload=0)` | Training only: the round-trip time of one message, in seconds |
| `sim.close()` | Closes the connection |

`step()` must not be called before `reset()`: it raises `BenchError("not_started")`.

## RC

```python
RC(roll=0.0, pitch=0.0, throttle=0.0, yaw=0.0, arm=False, mode="angle", aux=())
```

| Field | Range | Sign |
|---|---|---|
| `roll` | -1 to 1 | + right |
| `pitch` | -1 to 1 | + forward (nose down) |
| `throttle` | 0 to 1 | |
| `yaw` | -1 to 1 | + right (clockwise seen from above) |
| `arm` | `False` / `True` | the AUX1 switch |
| `mode` | `"angle"`, `"horizon"`, `"acro"` | the AUX2 switch |
| `aux` | up to 10 integers | spare channels, the simulator does not read them |

Values outside the range are clamped. `NaN` is replaced with zero.

`RC.from_us([1500, 1500, 1000, 1500, 2000, 1000])` sets the channels directly in microseconds (1000 to 2000) in the
order roll, pitch, throttle, yaw, AUX1, AUX2.

`rc.channels()` returns 16 values in microseconds: what will go to the simulator.

## Observation

What `reset()`, `step()`, `done()` and `latest()` return.

| Field | Type | What it is |
|---|---|---|
| `obs.t` | float | Simulator time since the start, seconds |
| `obs.tick` | int | Physics steps since the start (1000 per second) |
| `obs.running` | bool | `False` once the attempt is over |
| `obs.status` | str | `running`, or why it ended: `finished`, `time_limit`, `stuck`, `disconnected` |
| `obs.camera` | `Camera` | The latest frame |
| `obs.imu` | `Imu` | All IMU samples since the previous observation |
| `obs.tof` | `Tof` | The downward rangefinder |
| `obs.lidar` | `Lidar` | The latest full turn of the LiDAR |
| `obs.battery` | `Battery` | The voltage |
| `obs.truth` | dict or `None` | Training with `truth=True` only: `position` and `velocity` as [east, north, up] in metres, `points`, `score_time` |

Sensors run at their own rates, so in most observations only the IMU samples are new. The other sensors have a `new`
field: `True` if this observation brought a new value. Until there is a new one, the field holds the previous value.

### Camera

| Field | What it is |
|---|---|
| `image` | A `numpy` array height × width × 3, `uint8`, RGB, top row first. `None` before the first frame |
| `t` | Simulator time when the frame was taken. It reaches the program later by the camera latency |
| `seq` | Frame number |
| `new` | This frame is new |

For OpenCV convert the colours: `cv2.cvtColor(obs.camera.image, cv2.COLOR_RGB2BGR)`.

### Imu

| Field | What it is |
|---|---|
| `t` | An array of sample times, length n |
| `gyro` | n × 3, rad/s: x roll right +, y nose down +, z yaw left + |
| `accel` | n × 3, m/s², specific force: at rest on a level floor (0, 0, +9.81) |

`len(obs.imu)` is the number of samples (usually 5 per step). Body axes: x forward, y left, z up.

### Tof, Lidar, Battery

| Field | What it is |
|---|---|
| `tof.range`, `tof.valid` | Metres to the surface below; `0` and `False` when out of range |
| `lidar.ranges` | An array of ranges in metres, one per ray; `0` = nothing measured; `None` before the first scan. Ray `k` points `k · angle_step` from the forward axis, with angles growing to the left |
| `lidar.t` | Time of the end of the turn |
| `battery.voltage` | Volts |
| `.t`, `.new` | Each of them has these: the time of the reading and the flag of a new value |

## The handshake: sim.info

```python
cam = sim.info["sensors"]["camera"]
fx, fy, cx, cy = cam["fx"], cam["fy"], cam["cx"], cam["cy"]
k1 = cam["distortion"]["k1"]
```

| Key | What it is |
|---|---|
| `mode`, `time_mode`, `control_hz`, `time_limit` | The mode, `sync` or `realtime`, the step rate, the attempt time limit |
| `track` | Training only: `id` and `hash` of the track |
| `drone` | `class` (`ai_mission` or `ai_core`), `name`, `calibrated` |
| `rc` | `channels`, `rate_hz`, `latency_ms`, `failsafe_ms` |
| `sensors.camera` | `width`, `height`, `fps`, `hfov_deg`, `tilt_deg`, `latency_ms`, `fx`, `fy`, `cx`, `cy`, `distortion`, `mount` |
| `sensors.lidar` | `rate_hz`, `rays`, `min_m`, `max_m`, `angle_step_deg`. The key is absent when the drone has no LiDAR |
| `sensors.imu`, `sensors.tof`, `sensors.battery` | Rates and ranges |

## Errors

`BenchError` with the fields `code` and `message`.

| `code` | When |
|---|---|
| `token` | Wrong token |
| `not_allowed` | The message is forbidden in this mode (for example, `ping` in competition) |
| `already_started` | A second start in competition mode |
| `attempt_over` | The attempt of this simulator launch is already over |
| `not_started` | `step()` before `reset()` |
| `timeout` | The simulator did not answer within `timeout` seconds |
| `disconnected` | The simulator closed the connection |
| `protocol` | The message does not follow the protocol |

If the simulator is not running, `Sim.connect()` raises an ordinary `ConnectionRefusedError` (or waits, if `wait` is
set).

## Realtime mode

If the rules say `time_mode: "realtime"`, the simulator follows the wall clock and waits for nobody. `step()` then
sends a packet and returns the newest observation, skipping the ones that have piled up. The IMU samples from the
skipped observations are kept in order. For your own loop use `send()` and `latest()`.

## Tests without the simulator

`ushqyr_bench.fake.FakeSim` is a stand-in for the simulator for unit tests of your code:

```python
from ushqyr_bench import Sim, RC
from ushqyr_bench.fake import FakeSim

with FakeSim() as fake, Sim.connect(port=fake.port) as sim:
    obs = sim.reset()
    obs = sim.step(RC(arm=True))
    assert fake.received[-1][4] == 2000     # AUX1
```

It gives out small grey frames (64 × 48) and does not model flight: it is for checking the exchange, not the
algorithm.
