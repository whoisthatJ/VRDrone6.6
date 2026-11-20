# If something does not work

## Installation

| What you see | What to do |
|---|---|
| `python` or `pip` not found (Windows) | Install Python from python.org and use `py -m venv .venv`, `py -m pip ...` |
| `error: externally-managed-environment` (Linux) | Install into a virtual environment: `python3 -m venv .venv`, `source .venv/bin/activate` |
| `ModuleNotFoundError: ushqyr_bench` | The `.venv` environment is not activated or the SDK is not installed: `pip install ./bench/sdk/python` |
| `ModuleNotFoundError: cv2` | `pip install opencv-python` |
| `ushqyr-bench` not found | `pip install ./bench/runner` in the same environment |
| The simulator does not start on Linux | `chmod +x Ushqyr.x86_64`; check that the video card driver is installed |

## Connection

| What you see | What to do |
|---|---|
| `ConnectionRefusedError` | The simulator is not running in AI Bench mode: press **AI Bench** in the menu or start it with the option `-bench`. The screen must say "Waiting for a program on port 5757" |
| Port 5757 is in use | `Ushqyr.exe -bench -bench-port 5800`, and give the program `USHQYR_BENCH_PORT=5800` |
| `BenchError: token` | The simulator was started with a token (`-bench-token`). Set the same one in `USHQYR_BENCH_TOKEN` |
| `BenchError: attempt_over` | In competition mode one simulator launch is one attempt. Start the simulator again |
| `BenchError: already_started` | In competition mode `reset()` is called once |
| `BenchError: not_allowed` | The message is forbidden in competition mode (for example, `ping`) |
| A second program "kicked out" the first | One program connects to the simulator. A new connection replaces the old one |

## Flight

| What you see | What to do |
|---|---|
| The drone stands still, the motors are silent | Arming was not done. First packets with `arm=False`, then `arm=True` at `throttle=0`, and only then throttle |
| The motors turned off in flight | Failsafe: no packets for 500 ms. Call `step()` on every step. To turn the motors on again, switch `arm` off and on at zero throttle |
| The drone takes off and falls | Hover throttle is about 0.56. Hold the height with `obs.tof.range` |
| The drone flies backwards or sideways | Check the signs: `pitch` + is forward, `roll` + right, `yaw` + right |
| The attempt ended with `stuck` | The drone did not move for 10 seconds. Usually it is lying after a hit, or the program gives no throttle |
| The attempt ended with `timeout` | The program did not answer a step within 10 seconds of real time. Look for a hang or a computation that takes too long |
| `obs.camera.image` is `None` | There have been no frames yet. The first one comes with the first observation after `reset()` |
| The colours of the frame are mixed up in OpenCV | The frame is RGB, and OpenCV expects BGR: `cv2.cvtColor(image, cv2.COLOR_RGB2BGR)` |
| The gates in the picture "bend" near the edges | This is the barrel distortion of the lens. The formula is in [drone.md](drone.md) |
| The detector finds gates worse far from the start | The analog video link degrades with distance. Train the detector on the simulator's images ([dataset.md](dataset.md)) |
| The flight goes slower than real time | In sync mode the simulator waits for your program. This does not affect the result |
| The same run gives a different result | Check that the seed is the same (`sim.reset(seed=...)`) and that the program itself does not use randomness or the wall clock |

## Docker

| What you see | What to do |
|---|---|
| `the Docker engine is not running` | Start Docker Desktop (Windows) or the Docker service (Linux) |
| `the base image ... is not built yet` | `ushqyr-bench build-images` |
| `the attempt produced no result` | The solution did not connect or crashed. Look at the output with the option `--logs` |
| `the solution did not exit when the attempt ended` | The program must exit by itself: leave the loop when `sim.running` has become `False` |
| An error in `pip install` or in downloading weights during the flight | There is no internet in the container. Install everything in the `Dockerfile`, put the weights into the image |
| `Read-only file system` | You can write only to `/tmp` |
| The solution was killed for memory | The limit is 4 GB. Reduce the model or the batch size |
| The check takes tens of minutes | The simulator renders without a video card. See "Time" in [docker.md](docker.md) |
| The solution connects before the simulator is ready | `Sim.connect(wait=120)` |

## ROS 2

| What you see | What to do |
|---|---|
| There are no topics | The bridge is not running or did not connect: check `USHQYR_BENCH_HOST`, `USHQYR_BENCH_PORT` and that the simulator is in AI Bench mode |
| The attempt does not start | The bridge is waiting for the first message on `ushqyr/rc` or `ushqyr/joy` |
| The motors are cut | The node sends commands too rarely: the bridge waits for a command for at most 0.2 s per step, then the step goes without a packet |
| The standard undistortion gives a wrong picture | The distortion model is not `plumb_bob`. See [ros2.md](ros2.md) |
| The container does not exit after the attempt | Your node must exit when `ushqyr/status` has arrived with `running = false` |

## If that did not help

Collect and send to the organisers:

- the simulator version (it is in the handshake: `sim.info["sim"]`) and the operating system;
- the command you ran and the full text of the error;
- for Docker: the output of `ushqyr-bench submit-check ... --logs`.
