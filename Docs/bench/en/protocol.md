# Ushqyr AI Bench: wire protocol, version 1

The Python SDK (`bench/sdk/python`) speaks this protocol. It is written down so that a client can be written in any
language. It covers sync and realtime modes and all sensors. The version number is raised only if an existing
message changes.

## Transport

- TCP. The simulator listens on port **5757** (rules file `port`, or `-bench-port`), on this computer only unless it
  was started with `-bench-listen-all`.
- One program at a time. A new connection replaces the old one.
- Disable Nagle's algorithm (`TCP_NODELAY`) on the client.

## Frame

Both directions use the same frame. All integers are little-endian.

| Bytes | Field | Meaning |
|---|---|---|
| 4 | `length` | Number of bytes that follow (type + header length + header + payload) |
| 1 | `type` | Message number, see below |
| 4 | `header_length` | Size of the header |
| `header_length` | `header` | A [MessagePack](https://msgpack.org) map with string keys |
| rest | `payload` | Raw bytes; their layout is described by the header |

Limits on what the simulator accepts: a frame of at most **16 KiB**, header nesting at most 8 levels, at most 256
items per map or array, strings of at most 4096 bytes. A frame that breaks a limit, a header that is not a map, or more
than 64 unanswered messages closes the connection.

## Messages

| No. | Name | Direction | Training | Competition |
|---|---|---|---|---|
| 1 | `HELLO` | program → simulator | yes | yes |
| 2 | `START` | program → simulator | yes | yes, once |
| 3 | `STEP` | program → simulator | yes | yes |
| 4 | `RESET` | program → simulator | yes | **no** |
| 5 | `DONE` | program → simulator | yes | yes |
| 6 | `PING` | program → simulator | yes | **no** |
| 129 | `WELCOME` | simulator → program | | |
| 130 | `OBSERVATION` | simulator → program | | |
| 131 | `PONG` | simulator → program | | |
| 132 | `ERROR` | simulator → program | | |

A message that the mode does not allow, an unknown message number, or anything before `HELLO` is answered with `ERROR`
and the connection is closed. The mode is decided by the simulator; nothing the program sends can change it.

### HELLO → WELCOME

`HELLO` header:

| Key | Type | Meaning |
|---|---|---|
| `proto` | int | Must be `1` |
| `token` | str | Must match the simulator's token, if one is set |
| `client` | str | Free text for the simulator's log |
| `truth` | bool | Training only: add ground truth to every observation |
| `pace` | bool | Training only: `false` lets the simulation run as fast as the computer allows |

`WELCOME` header:

| Key | Meaning |
|---|---|
| `proto` | `1` |
| `mode` | `"training"` or `"competition"` |
| `track` | Training only: `id` and `hash` of the loaded track file. Never sent in competition mode |
| `sim` | Simulator version |
| `time_mode` | `"sync"` or `"realtime"` (see "Realtime mode") |
| `control_hz` | Steps per second of simulator time (default 100): one `STEP` advances `1 / control_hz` |
| `time_limit` | Attempt time limit in seconds |
| `drone` | `class` (`"ai_mission"`: the organisers' whoop; `"ai_core"`: a team's drone from its file), `name`, `calibrated` (false = the whoop's placeholder model) |
| `rc` | `channels` (16), `rate_hz`, `latency_ms`, `failsafe_ms` |
| `sensors.camera` | `width`, `height`, `fps`, `format` (`"rgb8"`), `hfov_deg` (edge to edge), `tilt_deg` (upward tilt), `latency_ms`, intrinsics `fx`, `fy`, `cx`, `cy` in pixels, `distortion` (`model: "radial_k1"`, `k1`), `mount` (position in the body frame, metres) |
| `sensors.lidar` | `rate_hz`, `rays`, `min_m`, `max_m`, `angle_step_deg`. Absent when the drone has no LiDAR |
| `sensors.imu` | `rate_hz`, `frame` |
| `sensors.tof` | `rate_hz`, `min_m`, `max_m`, `direction` (`"down"`) |
| `sensors.battery` | `rate_hz`, `cells` |

### START / RESET → OBSERVATION

Both put the drone on the start pad, set simulator time to zero and answer with the first observation, which carries a
camera frame taken at that instant. `START` has an empty header. `RESET` (training) may carry `seed` (int): the seed of
the sensor noise. In competition mode `START` is accepted once per connection.

### STEP → OBSERVATION

`STEP` header: `rc`, an array of up to 16 numbers, the RC channels in microseconds (1000 to 2000):

| Index | Channel | Meaning |
|---|---|---|
| 0 | Roll | 1500 centre, 2000 = right |
| 1 | Pitch | 1500 centre, 2000 = forward (nose down) |
| 2 | Throttle | 1000 = zero, 2000 = full |
| 3 | Yaw | 1500 centre, 2000 = right (clockwise seen from above) |
| 4 | AUX1, arm | above 1500 = armed. The flight controller arms only when this goes high **while throttle is at or below 1050** |
| 5 | AUX2, flight mode | below 1300 Angle (self-levelling), 1300 to 1700 Horizon, above 1700 Acro |
| 6–15 | spare | ignored |

Values outside 1000–2000 are clamped; missing channels are centred (throttle and AUX low). An array holding anything
but finite numbers is dropped as a whole, which the link sees as a lost packet. A `STEP` without `rc` advances the
simulation without a packet (the same lost packet, on purpose).

The simulator sends the packet through the link model (constant delay `latency_ms`; no packet for `failsafe_ms` cuts the
motors until AUX1 is switched off and on again), advances exactly one control period with the same flight controller and
flight model a human pilot flies, and answers. While the attempt is not running, `STEP` changes nothing and returns the
current observation.

### OBSERVATION

Header:

| Key | Type | Present | Meaning |
|---|---|---|---|
| `t` | float | always | Simulator time since the start signal (s) |
| `tick` | int | always | Physics steps since the start signal (1 kHz) |
| `running` | bool | always | False once the attempt is over |
| `status` | str | always | `running`, or why it ended: `finished`, `time_limit`, `stuck`, `disconnected` |
| `imu_n` | int | always | Number of IMU samples in the payload |
| `tof` | map | when a new reading exists | `t`, `range` (m), `valid` (false when out of range; `range` is then 0) |
| `battery` | map | when a new reading exists | `t`, `voltage` (V) |
| `lidar` | map | when a full turn is complete | `t` (end of the turn), `n` (rays) |
| `camera` | map | when a new frame exists | `t` (when the frame was taken), `seq`, `size` (bytes) |
| `truth` | map | training, if asked for | `position` and `velocity` as [east, north, up] in metres, `points`, `score_time` |

Payload, in this order:

1. `imu_n` × 7 `float32`: `t, gx, gy, gz, ax, ay, az`.
2. If `lidar` is present: `n` `float32` ranges in metres; 0 = nothing measured.
3. If `camera` is present: `size` bytes, `height` × `width` × 3, RGB, top row first.

**Camera lens.** The picture has barrel distortion. With `x_d = (u - cx) / fx`, `y_d = (v - cy) / fy` and
`r² = x_d² + y_d²`, the tangents of the true angles are `x_u = x_d (1 + k1 r²)`, `y_u = y_d (1 + k1 r²)`
(right and down are positive). `k1 = 0` is a plain perspective picture.

**Camera link.** The frames pass through a model of an analog video link: soft detail, smeared and drifting colour,
noise, jumping lines and torn bands. It gets worse with the distance from the start pad (where the receiver stands)
and when something solid is in between. The camera also adjusts its exposure to the scene.

**LiDAR.** A 2D scanner in the drone's own horizontal plane. Ray `k` points `k * angle_step_deg` from the forward axis,
turning left (anticlockwise seen from above). The rays of one turn are taken one after another while the drone moves,
so a scan made in flight is bent accordingly.

**Frames and axes.** Body frame: x forward, y left, z up, right-handed. Gyro in rad/s (x: roll right +, y: nose
down +, z: yaw left +). Accelerometer in m/s² as specific force: at rest and level it reads (0, 0, +9.81).

**Timing.** All times are simulator time. A camera frame is delivered `latency_ms` after it was taken. Sensors keep
their own rates (camera 30 Hz, IMU 500 Hz, ToF 50 Hz, battery 10 Hz by default), so most observations carry IMU
samples only.

**What is never sent in competition mode:** position, velocity, attitude, gate positions, the track's name, the score.

### DONE → OBSERVATION

The program says it has finished. The attempt ends with status `finished`.

### PING → PONG (training)

`PING` header: `payload` (int, at most 4 MiB). `PONG` comes back with that many bytes. For measuring the transport.

### ERROR

Header: `code` and `message`. Codes: `hello_first`, `protocol`, `token`, `not_allowed`, `already_started`,
`attempt_over`, `unknown_message`. The connection is closed after an `ERROR`.

## Competition mode

One simulator launch is one attempt. The program must connect and send `START` within `connect_timeout` seconds
(default 60) and answer every observation within `step_timeout` seconds (default 10) of real time; otherwise the
attempt ends with status `timeout`. A program that leaves or is refused ends it with `disconnected`. After the
attempt, `HELLO` is answered with `attempt_over`. `status` values a program can see: `running`, `finished`,
`time_limit`, `stuck`.

## Realtime mode

With `time_mode: "realtime"` in the rules the simulator follows the wall clock and waits for nobody. `STEP` then only
hands a packet to the link; an `OBSERVATION` is sent after every control period whether or not the program asked.
A program should read everything that has arrived and act on the newest observation (the SDK's `step()` and
`latest()` do that). A program that stops reading is disconnected when the send buffer is full. Realtime runs are
not repeatable; `result.json` records the time mode.

## Determinism

Simulator time advances only with `STEP`. The same build, track, seed and sequence of `STEP` messages give the same
flight and the same score on the same type of computer, whatever the speed of the program or the load on the machine.
