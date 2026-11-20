# ROS 2

The ROS 2 bridge turns the simulator's sensors into ordinary topics and accepts RC channels. The bridge itself is a
Python node that connects to the simulator through the SDK, so ROS 2 offers exactly the same as the SDK.

**Humble** and **Jazzy** are supported. Sources: `bench/ros2` (the packages `ushqyr_msgs` and `ushqyr_ros2_bridge`).

## Quick start in Docker

```
ushqyr-bench build-images --ros2 jazzy
ushqyr-bench submit-check bench/examples/color_pid_ros2 --logs
```

For Humble: `build-images --ros2 humble` and `submit-check ... --build-arg ROS_DISTRO=humble`.

The example `bench/examples/color_pid_ros2` is the same `color_pid` solution, but it gets all its data from topics.

## Running without Docker

You need ROS 2 installed and the SDK (`pip install ./bench/sdk/python`).

```
cd bench/ros2 && colcon build && source install/setup.bash
ros2 run ushqyr_ros2_bridge bridge
```

The simulator must be running in AI Bench mode. The bridge takes the address and the token from `USHQYR_BENCH_HOST`,
`USHQYR_BENCH_PORT`, `USHQYR_BENCH_TOKEN` or from the parameters `host`, `port`, `token`.

## Topics

The bridge publishes:

| Topic | Type | What it is |
|---|---|---|
| `/clock` | `rosgraph_msgs/Clock` | Simulator time |
| `ushqyr/camera/image_raw` | `sensor_msgs/Image` | The camera, `rgb8`, 30 Hz |
| `ushqyr/camera/camera_info` | `sensor_msgs/CameraInfo` | The camera calibration, with every frame |
| `ushqyr/imu/data_raw` | `sensor_msgs/Imu` | Gyroscope and accelerometer, 500 Hz, no orientation |
| `ushqyr/tof/range` | `sensor_msgs/Range` | The downward rangefinder; `inf` when there is no reading |
| `ushqyr/scan` | `sensor_msgs/LaserScan` | The 2D LiDAR; ray 0 points forward, angles grow to the left |
| `ushqyr/battery` | `sensor_msgs/BatteryState` | The battery voltage |
| `ushqyr/status` | `ushqyr_msgs/AttemptStatus` | Once per control step, after the sensor messages of that step |
| `ushqyr/info` | `std_msgs/String` | The simulator's handshake as JSON (kept for new subscribers) |

The bridge listens to:

| Topic | Type | What it is |
|---|---|---|
| `ushqyr/rc` | `ushqyr_msgs/RcChannels` | 16 channels in microseconds (1000–2000) |
| `ushqyr/joy` | `sensor_msgs/Joy` | Axes: roll, pitch, throttle (0..1), yaw. Buttons: 0 is arm, 1 is Horizon, 2 is Acro |

The `RcChannels` channels: 0 roll, 1 pitch, 2 throttle, 3 yaw, 4 AUX1 (arm), 5 AUX2 (flight mode). Arming happens
only when AUX1 goes high while the throttle is at or below 1050.

The body frame: x forward, y left, z up (REP 103). The timestamps in all messages are simulator time.

## Camera calibration

In `camera_info` the matrix `K` is the usual one, but the distortion model is our own:
`distortion_model = "ushqyr_radial_k1"`, `D = [k1]`. It is **not** `plumb_bob`. The model maps a point of the
distorted picture to the undistorted one: `x_u = x_d · (1 + k1 · r_d²)` in normalised coordinates. The standard ROS
undistortion nodes do not work with it; use the formula directly (example: `ColorPid._ray` in `color_pid.py`).

## How time runs

The attempt starts when the bridge receives the first message on `ushqyr/rc` or `ushqyr/joy` (parameter
`wait_for_rc`). Until you are ready, send "disarm"; the topic `ushqyr/info` is already available before that.

In sync mode, on every step the bridge publishes the sensors and waits for a new RC message for at most `sync_wait`
seconds of real time (default 0.2). If no message has come, the step goes without a packet, as with a lost link; after
`failsafe_ms` without packets the motors are cut.

A convenient scheme for a node: on every `ushqyr/status` message do one step of computation and send one RC message.

When the attempt is over, `ushqyr/status` arrives with `running = false`, and the bridge exits. Your node must exit
too, otherwise the container will not stop by itself.

## Bridge parameters

| Parameter | Default | What it is |
|---|---|---|
| `host`, `port`, `token` | from the environment | Where the simulator is |
| `connect_wait` | 60 | How many seconds to keep trying to connect |
| `wait_for_rc` | true | Start the attempt on the first RC message |
| `sync_wait` | 0.2 | How long to wait for RC on every step (sync mode) |
| `seed` | not set | Training mode: the seed of the sensor noise |

## In the solution's container

The nodes see only each other inside their own container. The ROS 2 logs are written to `/tmp`, because the file
system is read-only. The image with ROS 2 takes about 2.5 GB.
