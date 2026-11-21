# AI Bench Plan: autonomous gate-flying benchmark and online qualification

> **Platform:** desktop PC simulator (Windows + Linux), no VR, plus a Linux server build that runs with no screen.
> **Requirements source:** `Docs/reference/AIBENCH_TZ_ru.md` (the task as received). This plan turns it into decisions, architecture, milestones and tests. **If the plan and the TZ disagree, the TZ wins**; record the difference under "Decisions".
> **Builds on:** `PLAN.md`, `FLIGHT_PHYSICS.md`, `VISUALS_PLAN.md`, `TRACK_GARAGE_PLAN.md`, `PHASE3_PLAN.md`.

## 0. What AI Bench is
A mode where a team's **program** flies the drone, not a person. The program receives only what a real drone would give it (camera picture with analog noise, IMU, ToF, LiDAR, battery voltage) and answers with **RC channels** (throttle, roll, pitch, yaw), as if it were holding a real transmitter. Teams train at home in **training mode**. The organisers then run every team's solution on a server in **competition mode** and rank the results: this is the online qualification.

**Schedule note.** TZ stage 1, the demo (track, whoop, API, sample solution), was due **17 November**. This plan was written on 18 November, so that date has already passed. Milestone B1 is therefore the demo, cut to the minimum, and nothing else starts before it works. The two later dates in the TZ are still "[дата]" and must be asked for (§16).

## 1. TZ → plan map
| TZ item | Section | Milestone |
|---|---|---|
| Goal: training mode + online qualification | §0, §9 | B1–B8 |
| Standard gate 250×250 cm, opening 195×195 cm, own LED per gate | §4 | B1 |
| Track editor, 5+ ready tracks, hidden tracks | §4 | B3 |
| Venues: hall and parking | §4 | B1 (hall), B3 (parking) |
| AI MISSION whoop with real mass, thrust, delays | §5 | B1 (v0), B4 (calibrated) |
| AI CORE team drone ≤ 30×30×18 cm from a config | §5 | B4 |
| Sensors: FPV camera + analog noise, IMU, ToF, LiDAR, battery; no GPS, no coordinates | §6 | B1 (basic), B3 (complete) |
| Python SDK | §7 | B1 |
| ROS 2 bridge | §7 | B6 |
| Control by RC channels as through a real TX; configurable link delay | §7 | B1, B3 |
| Sample solution: colour detector + PID | §7 | B1 |
| Scoring: 1 point per gate, repeat after 4 others, tie → less time | §8 | B2 |
| Leaderboard with CSV export | §8 | B2 |
| Competition mode: no ground truth, no manual control | §9 | B2 |
| Team solution runs in Docker | §12 | B6 |
| Logs protected by checksum | §10 | B2 |
| Datasets in YOLO and COCO; randomised light, background, noise | §11 | B5 |
| Onboard + trackside video, CSV/JSON logs, replay for judges | §10 | B2 |
| Batch run on a server with no graphics, several tracks, one results file | §12 | B7 |
| Windows and Linux | §13 | B1 (Windows), B6 (Linux) |
| Docs in Russian, Kazakh, English | §13 | B1 (RU quickstart), B8 (all) |
| Test with 3–5 teams before the qualification | §14 | B8 |
| 5 acceptance criteria | §15 | B1, B2, B7, B8 |

## 2. Decisions
Items marked ❓ are my defaults; the organisers must confirm them (§16). Everything marked ❓ is a setting in a rules file, not hard-coded, so an answer never needs a rebuild.

| # | Decision | Why |
|---|---|---|
| D1 | **Same physics and flight controller as the rest of the simulator.** The solution's RC channels go through an RC-link model into the existing Betaflight-style controller and the existing flight model. No separate "AI drone" physics | One physics to calibrate; what a human pilot feels and what the algorithm flies are the same |
| D2 | **Two modes enforced on the simulator side:** TRAINING (ground truth available, manual control allowed, any settings) and COMPETITION (none of that). The competition restrictions live in the simulator, never in the SDK | The SDK runs in the team's hands; it can't be trusted to hide anything |
| D3 | **Two time modes.** *Sync (lock-step):* the simulator advances one control period per command and waits for the next. *Realtime:* the simulator runs at wall-clock speed. Official runs default to **sync** ❓ | Sync is deterministic: the same solution on the same track gives the same score on any server, and judges can re-run it. Realtime depends on server load |
| D4 | **All delays are simulated in simulator time:** camera latency, RC-link latency (+ jitter, packet loss), and one fixed "compute delay" that stands for the team's processing time ❓ (0 in training; the organisers set it for the competition) | The TZ asks for real delays and a configurable link delay; in sync mode they must not depend on the server |
| D5 | **API transport:** our own small versioned binary protocol over TCP (length-prefixed frames, MessagePack headers, raw payloads). Checked by a spike in B0; ZeroMQ is the fallback | No native dependencies, works the same in Docker, on Windows and on Linux |
| D6 | **ROS 2 bridge = a Python ROS 2 node built on the SDK.** Unity gets no ROS dependency | One protocol to secure and test; any ROS 2 version can be supported from outside |
| D7 | **Scoring is free-order** (as the TZ words it): any gate gives a point, and a gate can score again only after 4 other gates have scored. Precise definitions in §8 | Follows the TZ text; no fixed gate sequence is mentioned there |
| D8 | **Hidden tracks never ship.** They are plain track files that exist only on the organisers' server, outside the public repo and outside every team build | The simplest protection is not distributing the file |
| D9 | **Three build flavours from one project** (Unity build profiles): the full game; a **Team Kit** (AI Bench only, Windows + Linux, with graphics); a **Bench Server** (Linux, no screen, competition mode only) | Teams get a small download; the server build contains no training/ground-truth code paths |
| D10 | **Attempt defaults** ❓: time limit 120 s per attempt; 1 official attempt per track; total = sum of points over tracks, tie → smaller sum of times; small seeded start-pose and gate-pose jitter, identical for every team | Needed to make the batch run concrete; all configurable |
| D11 | **Solution gets no score feedback in competition mode** ❓ (only: attempt running / finished, and simulator time) | A real drone isn't told which gates counted |
| D12 | **Names follow the project, which is now called Ushqyr** (the plan was written with the old name GateRush): C# namespace `Ushqyr.Bench`, Python package `ushqyr_bench` (pip name `ushqyr-bench`), environment variables `USHQYR_BENCH_HOST / _PORT / _TOKEN`. The `grbench` command of later milestones becomes `ushqyr-bench` unless the owner prefers otherwise | One name for teams to learn; the code base has no `GateRush` namespace |
| D13 | **Layout as built:** code in `Assets/_Project/Scripts/Bench` (`Core` = pure logic with tests, `Runtime` = Unity side), editor generators in `Assets/_Project/Editor/Bench`, gate prefab and materials in `Assets/_Project/Bench`, **public tracks and the default rules file in `Assets/StreamingAssets/Bench`** (they ship as plain files and any other track is given by path), team docs in `Docs/bench/` (the repo's existing docs folder is `Docs`) | Track files must be loadable from outside the build for hidden tracks; one docs folder |
| D14 | **Drone stepping:** `QuadVehicle.ExternalStepping` lets the bench session call the unchanged `QuadSimulator.Step` + collision sweep once per 1 ms physics step from its own clock (a whole number of steps since the start signal). Control period 10 ms (100 Hz), IMU 500 Hz, ToF 50 Hz, battery 10 Hz, camera 30 fps | D1 without a second physics path; no wall-clock time anywhere in the simulation |
| D15 | **Whoop v0 = the existing `QuadProfile.Whoop65`** (32 g, 4 × 0.245 N, 1S 300 mAh, battery sag on), collision sphere 45 mm. It never "crashes and respawns": it bounces and stays where it falls. Attempt ends as `stuck` after 10 s without moving (not during the first 15 s) | `FLIGHT_PHYSICS.md` is not in the repo; the catalogue whoop is the placeholder. Shown as `calibrated=false` in the HUD and in the API handshake |
| D16 | **Arming like a real flight controller:** AUX1 must go from low to high while throttle ≤ 1050 µs; after a failsafe the switch has to be cycled. Default flight mode Angle on AUX2 low | "As through a real TX"; the sample shows the sequence |
| D17 | **Sensor conventions:** body frame x forward, y left, z up (ROS REP-103); accelerometer reports specific force; camera is a pinhole with the intrinsics in the handshake (lens distortion comes with the full analog chain in B3); images are RGB, top row first | Fixed now because every team's code depends on it |
| D18 | **Analog video v0 runs on the CPU** (Burst job, whole-number maths, a seed per frame and per line): colour smear, detail loss, noise, line jitter | Identical frames on every graphics card for the same input; 1.3 ms per frame |
| D19 | **Training-only conveniences in the protocol:** `truth` (true position, velocity and score in every observation) and `pace=false` (run as fast as the computer allows). Both are ignored in competition mode, and ground truth is compiled out with `USHQYR_BENCH_SERVER` | Debugging and faster training; the mode gate stays in the simulator |
| D20 | **Gate LED and scoring feedback:** gates never change appearance when passed (that would be score feedback through the camera). The gate counter exists only in the spectator HUD | D11 |
| D21 | **Protocol frame:** `u32 length, u8 type, u32 header length, MessagePack header, raw payload`; own minimal MessagePack reader with depth, size and count limits (no third-party C# dependency). Inbound frames ≤ 16 KiB | D5 confirmed by the B0 measurement below |
| D22 | **One simulator launch = one attempt in competition mode.** The program has `connect_timeout` (60 s) to connect and start and `step_timeout` (10 s) per step, in real time; these are the only uses of the wall clock and they sit outside the simulation. A program that leaves or sends a forbidden message ends the attempt as `disconnected`, keeping the points it had | Simple to reason about and to batch; a second attempt can't be sneaked in |
| D23 | **When attempts are recorded:** always in competition; in training only with `-bench-out` (or `record: "on"`). A training attempt cut short by a reset is closed as `invalid` | Learning runs reset thousands of times and must not fill the disk |
| D24 | **Signing:** `manifest.sig` = HMAC-SHA256 of `manifest.json` with the bytes of a key file given by `-bench-key` / `USHQYR_BENCH_KEY_FILE`. No default location and no key in the project: without a key attempts carry checksums only and say so (`signed: false`). Where the real key and the hidden tracks live: see D47 | A checksum alone can be recomputed by whoever edits a log; the key can't |
| D25 | **Video:** raw frames piped to an external `ffmpeg` (found by option, environment, `StreamingAssets/Bench`, or PATH; not bundled yet, licence check at B6). `onboard.mp4` holds the frames at the moment they are taken, which are the frames the program gets 30 ms later. `trackside.mp4` cuts between fixed positions that follow the drone. `judge.mp4` is made after the attempt by ffmpeg: trackside + onboard in the corner + subtitles with the clock, running score and each decision | No video code inside the simulation loop beyond a pipe write; fixed frame rate in simulator time |
| D26 | **`telemetry.csv` holds all 16 channels of every packet**, and `truth.csv` the pose per control step, so `resim` and the replay need nothing else. The "replay file" `replay.grreplay` is an index of those files | One source of truth per fact |
| D27 | **Leaderboard counting:** a team's line for a track is its best counted attempt (the rules will say how many attempts are allowed); the overall board adds one line per track | D10, made concrete |
| D28 | **Leak test compares what the program received with the judges' `truth.csv` of the same attempt**: key whitelist, equal values, and correlation after removing linear trends (clocks excluded; the ToF sensor follows the height by design) | "Coordinates can't be obtained through the API" as a runnable check |
| D29 | **Lens model: radial distortion with one coefficient** (`x_u = x_d (1 + k1 r²)`, default `k1 = 0.18`, in the rules file), not a full fisheye. The scene is rendered as a plain perspective view about 1.5 times larger and resampled through a fixed map; `hfov_deg` stays the edge-to-edge angle | A true 120° fisheye needs a render several times larger for the same sharpness; this keeps the camera above real time on a GTX 1650 and still makes teams deal with a bent picture. Replace with the real lens's calibration when the organisers send it |
| D30 | **Analog link quality** = distance from a receiver at the start pad (worst at `video_range`, default 80 m) times 0.55 when something solid is in between. It scales noise, line jitter, tearing, colour drift and band break-up | "Noise that grows with distance and obstruction" with two numbers to tune |
| D31 | **LiDAR is a 2D 360° scanner** in the drone's horizontal plane: 360 rays, 10 Hz, 0.15–12 m, swept across the turn while the drone moves (36 rays per control step). Not written to `telemetry.csv` (360 numbers a scan) | Plan default; the sweep gives the motion distortion for free |
| D32 | **Compute delay is added to the RC link delay** (not to the observations) | One queue instead of two; the effect on the control loop is the same |
| D33 | **Realtime mode keeps one message for both directions:** `STEP` hands a packet to the link, observations are pushed every control period, the SDK's `step()` sends and then returns the newest observation that has arrived | The same program runs in both time modes |
| D34 | **Venues are described once in code** (`Venue`: size, columns, parked cars); the scene builder, the validator and the generator all read that description | They cannot disagree about where a column stands |
| D35 | **Track editor has no manual test flight.** AI Bench has no hand-flying in any mode (that is also why competition mode needs no input lock). Test flights are the oracle pilot's or a program's | Deviation from plan §4 ("manual in training mode"); adding a hand-flown bench drone would re-open the "no manual control" guarantee |
| D36 | **The oracle pilot is new code** (`BenchOracle`: waypoints before and after each gate, velocity control through Angle-mode sticks), not the Phase 3 bot core | The bot core follows baked racing lines on spline routes; gate lists in free order need none of it |
| D37 | **Public tracks keep the plan's names and gate counts**, and a track file may carry `props` (solid coloured panels and boxes) for `X1_Mixed`'s distractors | Plan §4 |
| D38 | **Hidden-track protection = never shipping the file, plus a check** (`ushqyr-bench check-build`) that a build or kit folder contains no hidden track, its id or its gate data. **The private folder itself is the owner's choice, still open** | D8 with a test |
| D39 | **`drone.yaml` is read by the simulator itself** with a small parser for plain YAML (nested keys, numbers, text, one-line lists), and checked there against `class_limits.json`. `ushqyr-bench validate-drone` (PyYAML) repeats the checks for teams; the simulator's verdict is the one that counts | No YAML library in the build; the check can't be skipped by not running a tool |
| D40 | **What a team may declare and what is computed.** Declared: size, mass, per-motor thrust, motor time constant, motor positions, propeller, battery, rates, max angle, camera tilt and field of view, sensor list. Computed from size and mass: inertia, drag, controller gains (scaled from the 5" race quad's tuning by control authority). Default limits: thrust-to-weight 1.5 to 8, up to 6 cells, mass 25 to 1500 g, in `class_limits.json` | A team can't tune itself an unphysical drone through inertia or drag; every number that matters for fairness has a limit the organisers own |
| D41 | **Team drone visual and collider:** a box of the declared size with four arms and propellers; collision sphere of 0.4 × the larger of width and length | "Collider and visual box from the declared size"; the flight model uses a sphere for every drone |
| D42 | **Randomisation is a rules setting** (`randomization`: none / mild / strong) driven by the attempt's seed; it also provides D10's "seeded start-pose and gate-pose jitter, identical for every team". A nudge that would break the track's checks is not applied to that gate. Runs keep the LED colours; datasets also vary them | One mechanism for training variety, fair competition jitter and datasets |
| D43 | **Dataset keypoints are 3D points**: the opening's corners on the gate face towards the camera, ordered as seen. Visibility 0 / 1 / 2 as in COCO | Usable for pose estimation (PnP), not only detection |
| D44 | **Label check = second render with the lights out and gates painted in ID colours.** A gate wholly in view must have its box within 2 px of the painted outline on every side; visible corners must lie on the gate. (The plan's "corners against the ID mask" fails by construction on near gates, where the corner sits between two visible gate surfaces.) | A check of the projection against what the renderer really drew |
| D45 | **Masks are instance-id PNGs**, not COCO polygons | Simple to read; conversion is one line for whoever needs polygons |
| D46 | **`dataset` is a simulator mode** (`-bench-dataset`), and `ushqyr-bench dataset` only starts it; the in-app button makes 500 images | The camera pipeline exists once |
| D47 | **Hidden tracks and the signing key exist only on the qualification server** (owner's decision, 19 November 2026). Not on a developer PC, not in any repository. Until the server exists (B6/B7) no real hidden track or key is made; development uses public tracks and a throwaway test key | The fewest copies; nothing to leak from a laptop or from git |

## 3. Architecture
```
Team solution  (Python script / ROS 2 node / Docker container)
      │   Python SDK  ──  TCP, versioned binary protocol
      ▼
Bench API server (inside the simulator)        mode gate: TRAINING | COMPETITION
      │
      ├─ RC link model   rate limit, latency, jitter, loss, failsafe
      │        └─► existing flight controller ─► existing flight physics (1 kHz)
      ├─ Sensor suite    camera + analog chain, IMU, ToF, LiDAR, battery  (own clocks, seeded noise)
      ├─ Referee         gate-pass detection, scoring, attempt lifecycle   (never reachable from the API)
      ├─ Recorder        logs, onboard + trackside video, replay file, checksummed manifest
      └─ Ground truth    TRAINING only: poses, labels, dataset generator, debug overlays

Batch runner (Python, on the server):  submissions × tracks → [simulator, no screen] + [solution container] → one results file
```

**Repo layout**
```
Assets/_Project/Scripts/Bench/   GateRush.Bench.{Api,RcLink,Sensors,Referee,Recording,GroundTruth,Tracks,UI}
Assets/_Project/Bench/           standard gate prefab, venues, public tracks, drone profiles
bench/sdk/python/                gaterush_bench (pip package + tests)
bench/examples/color_pid/        the sample solution (script, ROS 2 node, Dockerfile)
bench/ros2/                      gaterush_msgs, gaterush_ros2_bridge, Dockerfiles per ROS 2 version
bench/runner/                    grbench CLI: batch, verify, resim, submit-check, validate-drone, dataset
bench/docker/                    solution template, base images
bench/schemas/                   JSON Schemas: track, drone, rules, result, protocol messages
docs/                            documentation site: ru/, kk/, en/
(private, not in this repo)      hidden tracks, signing key, real qualification rules file
```

**Determinism rule:** same build + same track + same seed + same command sequence → identical trajectory and score on the same machine type. A test runs an attempt twice and compares state hashes.

## 4. Gates, tracks, venues, editor
**Standard gate** (`Gate_Standard_250`): outer 2500 × 2500 mm, opening 1950 × 1950 mm, centred. The frame bar is therefore 275 mm wide on all four sides, and the opening's lower edge is 275 mm above the floor. Depth 275 mm ❓. LED strip around the inner edge and the front face; **each gate has its own LED settings** (colour, brightness, solid/blink) stored in the track file. LEDs are emissive + bloom and also light nearby surfaces slightly.

**Track file** (`*.benchtrack.json`, versioned schema, content hash):
- venue + lighting variant;
- gates: id, position, yaw, LED settings, allowed pass direction (`any` ❓ default, or `front`);
- start pad pose; time limit; rule overrides (repeat window = 4);
- randomisation ranges; name, author, difficulty.

**Track editor** (in the Team Kit, for organisers and teams):
- top-down and 3D views; place, move, rotate, duplicate, delete gates with snapping; LED colour picker; start pad; venue and lighting;
- validation: at least 5 gates (otherwise the repeat rule stalls scoring), minimum gate spacing, clearance to walls, columns and ceiling, at least one gate visible from the start pad;
- "test fly" (manual in training mode, or the oracle pilot below); save / load / export; top-down map image export;
- **random track generator** with a seed and a difficulty level, for making hidden tracks of comparable difficulty.

**Oracle pilot:** a ground-truth autopilot (reuse the Phase 3 bot core if it exists). It never runs in competition mode. It proves every track is flyable within the time limit and supplies camera paths for datasets.

**Public tracks (6):**
| Track | Venue | Gates | Idea |
|---|---|---|---|
| `H1_Oval` | Hall | 6 | Flat oval; the next gate is always in view. **The demo track** |
| `H2_Eight` | Hall | 8 | Figure-eight with a crossing |
| `H3_Slalom` | Hall | 7 | Offset slalom and a 180° hairpin |
| `P1_Columns` | Parking | 6 | Between column rows, low light |
| `P2_Maze` | Parking | 9 | Turns behind columns; the next gate isn't always visible, so the solution must search |
| `X1_Mixed` | Hall | 10 | Different LED colours plus coloured distractors |

**Hidden tracks:** at least 3 per qualification, made in the editor or by the generator, checked by the oracle pilot, stored only in the private location.

**Venues** (dimensions ❓, ideally from photos of the real places):
- **Hall:** multi-purpose hall about 40 × 20 × 8 m, lighting variants.
- **Parking:** covered level about 60 × 34 m with a column grid, parked cars, markings. Clear ceiling height about 3.4 m, because the gate itself is 2.5 m tall.

## 5. Drones
**AI MISSION: the organisers' whoop.**
- `v0` (for the demo): a placeholder whoop from `FLIGHT_PHYSICS.md`, marked `calibrated=false` everywhere it's shown.
- `v1`: built from the organisers' measurements (list in §16): mass, size, motors, props, battery, thrust-stand data, camera, sensor mounting, Betaflight settings, measured delays.
- **Calibration checks** against their real flights/logs: mass exact; hover throttle within ±3 points; max thrust ±5%; step-response rise time ±10%; top speed ±10%; end-to-end delays as measured.
- Flight mode ❓: default Angle (self-levelling) with the whoop's real rates and limits; arm on AUX1.

**AI CORE: the team's own drone, from a config.**
- `drone.yaml` checked against a JSON Schema: size (≤ 300 × 300 × 180 mm, from the TZ), mass, motors, props, battery, drag, controller settings, sensors chosen from an allowed catalogue with mounting poses, camera tilt/FOV. Inertia is computed from the parts if not given.
- A **class limits file** owned by the organisers ❓ (max thrust-to-weight, battery cells, sensor catalogue, camera ranges). A config outside the limits is rejected with a readable error.
- `grbench validate-drone drone.yaml` lets teams check their file. The config hash is stored in every result.

## 6. Sensors
All timestamps are simulator time. Every sensor has its own rate, latency, noise parameters and seed. Values marked ❓ should match the real hardware.

| Sensor | Default | Model |
|---|---|---|
| **FPV camera** | 640 × 480, 30 fps, wide lens ❓ | Lens distortion, auto-exposure, then the analog chain: blur of fine detail and colour, noise that grows with distance and obstruction, line jitter and tearing, colour shift, occasional frame breakup. Delivered as raw RGB (JPEG optional) |
| **IMU** | Gyro + accelerometer, 500 Hz ❓ | White noise, bias, slow bias drift, scale error, motor-vibration noise that follows RPM, saturation limits |
| **ToF** | Downward, single zone, 50 Hz, 0.05–4 m ❓ | Ray cone, noise growing with distance, dropouts, out-of-range flag |
| **LiDAR** | 2D 360° scan, 10 Hz, 0.15–12 m, 1° step ❓ | Batched raycasts, range noise, dropouts, distortion from moving during the sweep |
| **Battery** | Voltage, 10 Hz | From the existing battery-sag model, plus measurement noise |
| **Never provided** | Position, velocity, attitude, GPS, gate positions, track name | In competition mode these don't exist on the API at all |

## 7. API, SDK, ROS 2, sample solution
**RC link ("as through a real TX"):**
- 16 channels, 1000–2000 µs (the SDK also accepts −1…1); order roll, pitch, throttle, yaw, then AUX1 = arm, AUX2 = mode.
- Packet rate like a real link (default 100 Hz ❓); faster sending is down-sampled.
- **Configurable link delay:** constant + jitter + packet-loss %, per class or per track.
- Failsafe: no packet for 500 ms → the controller reacts as the real one is set up. Values are clamped; invalid numbers are rejected.

**Protocol:** handshake (protocol version, mode, drone class, sensor list with camera calibration and mounting), token, message whitelist per mode, size and rate limits. Written up in the docs so teams can write clients in other languages.

**Python SDK** (`pip install gaterush-bench`; Python ≥ 3.9; needs only numpy and msgpack):
```python
from gaterush_bench import Sim, RC

with Sim.connect() as sim:                 # address and token come from environment variables
    obs = sim.reset()                      # training mode only; in competition the attempt just starts
    while sim.running:
        frame = obs.camera.image           # numpy array, H × W × 3
        rc = my_controller(frame, obs.imu, obs.tof, obs.lidar, obs.battery)
        obs = sim.step(rc)                 # sync mode: advance one control period
```
Also: a realtime API (latest-value getters and callbacks), type hints, a fake server for unit tests, and an optional Gymnasium wrapper for reinforcement learning (training mode only).

**ROS 2 bridge:** topics for camera image + camera info, IMU, range (ToF), laser scan (LiDAR), battery state and `/clock`; a subscriber for RC channels (our own message, plus `sensor_msgs/Joy` for convenience). Supported versions: Humble, Jazzy and Lyrical ❓. Shipped as source and as Docker images.

**Sample solution `color_pid`** (the TZ's "detector by colour plus PID"):
- OpenCV colour threshold on the gate LED → largest blob → its centre and apparent size;
- PID: yaw from horizontal error, throttle from vertical error (with ToF height hold), forward pitch scaled by how well the gate is centred;
- when no gate is visible: slow rotation to search; after flying through: short straight segment, then search again.
- Ships as a plain script, as a ROS 2 node and as a Docker example. A CI test runs it with no screen and requires a full lap.

## 8. Referee: scoring, attempts, leaderboard
- **Gate pass:** the drone's centre crosses the gate's opening plane **inside the 1950 × 1950 mm opening**, checked between every two physics steps (segment against rectangle, so nothing is missed at speed). The pass is complete when the centre is at least 0.3 m beyond the plane; the time stamp is the moment of crossing. Poking in and backing out is not a pass. Flying around the gate is not a pass.
- **Point:** +1 if this gate is **not among the last 4 scored gates**. Otherwise the pass is logged as "repeat ignored".
- **Time (tie-break):** simulator time from the start signal to the last scored gate ❓.
- **Ranking:** more points first; with equal points, less time first.
- **Attempt ends** on: time limit; the solution reporting it's done; the solution not answering (timeout); disconnect. Crash rule ❓: default "keep going" (the attempt ends if the drone hasn't moved for 10 s).
- **Statuses:** `finished`, `time_limit`, `timeout`, `disconnected`, `stuck`, `invalid` (our infrastructure failed → rerun, doesn't count against the team).
- **Leaderboard:** in-app screen (per track and overall) and **CSV export**: team, track, attempt, points, time, status, date, build, track hash, drone-config hash, solution image digest. UTF-8 with an option for `;` separators so it opens correctly in Excel with Russian/Kazakh regional settings.

## 9. Competition mode and anti-cheat
| Threat | Measure |
|---|---|
| Reading drone or gate coordinates through the API | The ground-truth service isn't created in competition mode, and its code isn't in the Bench Server build. Automated leak tests (§15) |
| Manual flying | Competition mode ignores keyboard, gamepad and radio; the UI is spectator-only |
| A person steering remotely through the team's code | The container has no internet and accepts no incoming connections |
| Reading the simulator's files or memory | The solution runs in a container that shares nothing with the simulator: no volumes, separate process and network namespaces, access to the API port only |
| Memorising the track | Hidden tracks; seeded jitter of start pose and gate poses; seeded lighting variant |
| Changing the run (reset, time scale, seed, track) | Those messages don't exist in competition mode |
| Editing logs afterwards | Logs are written by the simulator, outside the container; checksummed manifest signed with the organisers' key (§10) |
| Breaking the simulator with bad input (invalid numbers, floods, huge messages) | Validation, clamping, rate and size limits; fuzz tests |
| Stalling | Timeouts for connecting, per step and for the whole attempt |
| A hostile container | Non-root, read-only filesystem, all capabilities dropped, no privilege escalation, limits on CPU, memory and process count; runs on a dedicated machine |

**What this does and doesn't promise:** the API never gives coordinates. A team may still *estimate* its position from the sensors; that's the task itself.

## 10. Logs, recording, judge replay
**Per-attempt folder:**
- `result.json`: points, time, status, team, track id and hash, drone-config hash, solution image digest, build hash, seed, rules hash.
- `events.jsonl` + `events.csv`: every gate pass (gate, time, scored or not, and why), collisions, failsafes, start and end.
- `telemetry.csv`: what the solution received and sent (RC channels, IMU, ToF, battery, timing).
- `truth.csv`: ground-truth trajectory. **Judges only**; never visible to the container.
- `onboard.mp4` (exactly the frames the solution got), `trackside.mp4` (2–3 fixed cameras + an overview), `judge.mp4` (both views with an overlay: time, running score, each gate event).
- `replay.grreplay`, `sim.log`, `solution.log` (the container's output, size-capped).
- `manifest.json`: SHA-256 of every file plus all the hashes above; `manifest.sig`: HMAC-SHA256 with a key that exists only on the organisers' server.

**Tools:** `grbench verify <folder>` → verified / modified. `grbench resim <folder>` replays the recorded RC commands with the same seed and must reproduce the same score.

**Video:** frames are captured from render textures and piped to an external `ffmpeg`. In sync mode the video is rendered at a fixed frame rate in simulator time, so it's smooth however slow the server is. Check the licence of the ffmpeg build that gets bundled.

**Judge replay viewer** (in the app): open an attempt folder → integrity check → timeline; free, trackside and onboard cameras; list of gate events with jump-to; slow motion; each scoring decision shown with its reason (for example "repeat ignored: only 2 other gates since").

## 11. Training tools
**Dataset generator** (`grbench dataset`, and a screen in the Team Kit; training mode only):
- Camera poses from oracle-pilot flights with random offsets, plus fully random poses.
- **Labels:** a box per visible gate; the 4 inner corners as keypoints with visibility; optional instance masks; occlusion and truncation flags.
- **Formats:** YOLO (images, label text files, `data.yaml`) and COCO (JSON); train/val/test split; seed.
- **Randomisation:** light intensity, colour, direction and flicker; LED colour and brightness; floor and wall materials; clutter and coloured distractors; camera noise level, exposure, motion blur; small gate-pose jitter.
- **Check:** a script draws the labels over 100 random images for a visual check; a test compares projected corners with a rendered ID mask (within 2 px).

**Randomisation profiles** also apply to normal training runs, so teams can train against varied light, background and noise.

## 12. Batch runner and Docker
**Submission contract:** a Docker image (a `docker save` file, size-capped ❓). It reads the simulator address and token from environment variables, flies, and exits. A template project and base images (CPU; GPU optional ❓) are provided. `grbench submit-check ./my_solution` builds the image and runs it locally in competition mode on a public track, so teams test exactly what the server will do.

**Batch runner** (`grbench batch --submissions submissions.yaml --tracks tracks.yaml --rules rules.yaml --out results/`): for every team × track × attempt it
1. starts the simulator with no screen;
2. starts the hardened container on an isolated network;
3. waits for the handshake, runs the attempt, enforces timeouts;
4. collects logs and video, signs the manifest, tears everything down.

It retries only infrastructure failures, can resume after an interruption, and can run several workers in parallel. Output: **one `results.csv` (and `results.json`)** with every attempt, plus `leaderboard.csv`.

**Running with no screen:** use the normal Linux build in batch mode under a virtual display (Xvfb), **without** `-nographics`: that switch disables rendering, and the camera would send black frames. Unity's Dedicated Server target can't be used for the same reason. A GPU is strongly recommended; software rendering also works in sync mode, only slower. The B0 spike confirms this setup on our Unity version before anything is built on it.

## 13. Platforms, builds, docs, onboarding
- **Team Kit:** Windows x64 and Linux x64, small (target ≤ 1.5 GB), AI Bench only. Contents: the app, the SDK wheel (installable offline), examples, docs.
- **Bench Server:** Linux x64. Started from the command line with track, drone, seed, mode, port and output folder; exits when the attempt ends.
- **Docs site** in Russian (primary), Kazakh and English: 30-minute quickstart; SDK reference; drone and sensor specs; rules and scoring; track editor; dataset tool; ROS 2; Docker submission guide; organiser guide (server setup, batch runner, judge replay); troubleshooting. The Kazakh text needs a native reviewer ❓.
- **AI Bench screens** in the app are localised in the same three languages.
- **Performance targets:** sync mode at ≥ 1× real time on a GTX 1650-class PC with camera and LiDAR on; ≥ 3× on an RTX 3060-class PC; realtime-mode API overhead under 5 ms (95th percentile) on one machine.

## 14. Milestones
| # | Milestone | TZ stage | Done when |
|---|---|---|---|
| B0 | Orientation and spikes | — | Project state written up; transport spike measured (640 × 480 RGB at 60 Hz + IMU, Unity → Python); Linux no-screen rendering spike; the §16 questions sent to the organisers, with the plan's defaults in use until they answer |
| **B1** | **Demo** | **1 (17 Nov, overdue)** | Hall v1 with `H1_Oval` and standard LED gates; whoop v0; camera, IMU, ToF, battery; API + Python SDK in sync mode, paced to real time so the flight looks normal on screen (realtime mode moves to B3); `color_pid` flies a full lap by itself in 9 of 10 runs; on-screen gate counter; one-page Russian quickstart; a recorded demo video |
| B2 | Referee, competition mode, logs | 2 | Scoring rules with tests; attempt lifecycle; leaderboard + CSV; competition mode + leak tests; logs + checksummed manifest; onboard and trackside video; judge replay |
| B3 | Tracks, venues, full sensors | 2–3 | Editor; 6 public tracks; Parking; hidden-track workflow + generator; LiDAR; full analog model; link delay settings; realtime mode; oracle pilot passes every track |
| B4 | Drones | 3 | Whoop v1 calibrated (needs the organisers' data); AI CORE config, schema, class limits, validator |
| B5 | Datasets and randomisation | 3 | YOLO + COCO export; randomisation profiles; label check passes |
| B6 | Linux, ROS 2, Docker | 3 | Linux Team Kit and Bench Server; ROS 2 bridge + images; solution template; `submit-check` |
| B7 | Batch runner | 3 | 20 solutions × 3 tracks run unattended into one results file |
| B8 | Docs, onboarding, pilot | 4 | RU/KZ/EN docs; 30-minute test passed on Windows and Linux; pilot with 3–5 teams; their fix list closed |

After B1, B2 and B3 can run in parallel. B4 waits for data. B5 can start any time after B3. B6 comes before B7.

## 15. Acceptance tests (the TZ's five criteria)
| TZ criterion | How it's proven |
|---|---|
| The sample solution flies the track by itself | Automated: `color_pid` runs with no screen on `H1_Oval`, `H2_Eight`, `P1_Columns` × 5 seeds; it must score every gate of one lap within the time limit in ≥ 90% of runs |
| Points match a manual check by video | Unit tests for every rule and edge case (partial entry, pass from the back, repeat too early, flying around, clipping the frame). Then a protocol: 10 recorded attempts, a person counts from `judge.mp4` with the printed rule sheet; all 10 must equal the automatic score |
| Drone and gate coordinates can't be obtained through the API | Automated in competition mode: every message type is checked against a whitelist; unknown and malformed messages are fuzzed; everything sent to the client is scanned for the true position values; the build is scanned to confirm no hidden-track files and no ground-truth code; a container test confirms it can reach nothing but the API port |
| 20 solutions on 3 tracks with no manual intervention | A dry run with 20 variants of the sample solution, including broken ones: crashes at start, hangs, never connects, floods output, is very slow, tries to request ground truth. One command, one results file, correct statuses, and resume works after a forced stop |
| A new team installs and runs the sample in 30 minutes from the docs | Stopwatch test with at least 3 people who have never seen it, on Windows and on Linux; fix the docs until the median is ≤ 30 minutes including download |

## 16. Вопросы организаторам (готово к отправке)
1. **Демо.** Срок 17 ноября прошел. Что именно нужно показать на демо и к какой дате? Какие даты у этапов, где в ТЗ стоит «[дата]», и когда сам отбор?
2. **Вупер (AI MISSION).** Нужны данные: масса с батареей, размеры, моторы, пропеллеры, батарея; замеры тяги; измеренные задержки (видео, радиоканал); выгрузка настроек Betaflight (`diff all`); логи Blackbox, если есть; модель камеры, угол обзора и наклон; какие ToF и LiDAR стоят и как закреплены; разрешение и частота кадров видео, которое получает программа.
3. **Режим полета.** Angle или Acro? Можно ли решению переключать режимы?
4. **Ворота.** Глубина рамы и конструкция, где расположены LED, какие цвета используются? Есть ли фото или чертеж? Засчитывается пролет в любом направлении или только в одном?
5. **Попытка.** Длительность попытки? Сколько попыток на трассу? Что происходит при аварии: попытка заканчивается или продолжается?
6. **Время при равенстве очков.** Считаем время до последних засчитанных ворот. Верно? Часы стартуют по сигналу старта или по первому движению?
7. **Итог по нескольким трассам.** Сумма очков по всем трассам или лучшая попытка?
8. **Режим времени на отборе.** Пошаговый (результат не зависит от мощности и загрузки сервера, попытку можно точно повторить) или реальное время? Нужна ли фиксированная «задержка на вычисления», и какая?
9. **Обратная связь.** Получает ли решение во время попытки информацию о засчитанных воротах и счете?
10. **AI CORE.** Какие пределы параметров допустимы (отношение тяги к весу, батарея, список разрешенных сенсоров, камера)? Кто проверяет конфиг команды?
11. **LiDAR.** Какой тип (2D-сканер на 360°, многозонный ToF, другое) и какие характеристики?
12. **Площадки.** Размеры и высота потолка зала и паркинга; есть ли фото? Паркинг крытый или открытый?
13. **Сервер отбора.** Какая ОС, есть ли видеокарта и Docker? Сколько команд ожидается?
14. **Решения команд.** Лимиты для Docker-образа: размер, процессор, память, можно ли использовать видеокарту?
15. **ROS 2.** Какие версии нужны (Humble, Jazzy, Lyrical)?
16. **Казахский язык.** Кто вычитывает перевод документации?

## 17. Risks
| Risk | Mitigation |
|---|---|
| The demo date has passed | B1 is the minimum slice and comes first; agree a new demo date now (question 1) |
| No real whoop data → "real mass, thrust and delays" can't be met | Placeholder v0 clearly marked; data request sent on day one (question 2); calibration checks listed in §5 |
| Sync vs realtime changes the nature of the competition | Both are built; the choice is one line in the rules file (question 8) |
| Linux, Docker and no-screen rendering can't be fully tested on one Windows PC | Docker Desktop + WSL2 for functional tests; a Linux machine with a GPU (ideally the real qualification server) for the final B6–B7 checks |
| A leak of ground truth through some forgotten path | Ground-truth code compiled out of the server build; whitelist + byte-scan leak tests in CI |
| Scoring disputes | Deterministic re-simulation, judge video with overlay, every scoring decision logged with its reason |
| Gate detection by colour fails under randomised light (sample solution looks broken) | The sample runs with a mild randomisation profile by default; docs explain how to make it robust |
| Kazakh documentation quality | Native reviewer named before B8 (question 16) |
| Scope: this is a large piece of work with fixed competition dates | Strict order B1 → B2 → …; each milestone ends with something the organisers can try |

## 18. Status log

### B0 (18–19 November 2026)
- **Project state:** see `Docs/project_state.md`, section "AI Bench orientation".
- **Machine:** Windows 10, i5-10300H, GTX 1650 Ti, 16 GB; Python 3.11.3; Unity 6000.6.0f1 with Windows, macOS and Android modules. **Missing:** Linux Build Support module, Docker, WSL2, a system `ffmpeg` (a bundled one from `imageio-ffmpeg` exists). These block the Linux no-screen spike and B6–B7, not B1.
- **Transport spike (inside the real protocol, `PING`):** a reply the size of one 640 × 480 RGB frame (921,600 bytes) takes 0.95 ms median and 8.4 ms at the 95th percentile for the round trip; an empty message 0.05 ms. A full sync loop (STEP, physics, camera, IMU, reply, Python decode) runs at **3.3× real time** on this computer with the camera on. Plain TCP is enough; ZeroMQ is not needed.
- **Linux no-screen rendering spike:** not run (no Linux module, no Docker/WSL2). Moved to the start of B6.

### B1 (19 November 2026): demo
| Item | State |
|---|---|
| Standard gate `Gate_Standard_250` with per-gate LED | Done; dimensions checked by a unit test |
| Hall v1 (40 × 20 × 8 m) and `H1_Oval` (6 gates) | Done; generated by `Ushqyr > Bench > Build Everything` |
| Whoop v0 | Done (placeholder, `calibrated=false`) |
| Camera 640 × 480 at 30 fps with analog noise, IMU, ToF, battery | Done |
| RC link: 16 channels, rate limit, constant delay, failsafe, arming | Done |
| API server, protocol v1, Python SDK (sync mode, paced to real time) | Done; `Docs/bench/protocol.md` |
| Sample `color_pid` | Done: **full lap in 10 of 10 runs** (seeds 1–10), about 24.3 s per lap; the same seed twice gives an identical result |
| Gate counter and timer on screen, Russian quickstart | Done; `Docs/bench/ru/quickstart.md` |
| Tests | 29 EditMode tests (`BenchTests`), 10 Python tests (`bench/sdk/python/tests`) |

### B2 (19 November 2026): referee, competition mode, logs
| Item | State |
|---|---|
| Referee rules with tests (partial entry, from the back, repeat too early, around, through the frame, fast pass, tie-break time) | Done in B1, unchanged |
| Attempt life cycle and statuses (`finished`, `time_limit`, `timeout`, `disconnected`, `stuck`, `invalid`), rules file | Done |
| Competition mode | Done: message whitelist, one attempt per launch, timeouts, no ground truth; no manual control exists in AI Bench at all |
| Leak tests | Done: `bench/runner/leak_test.py` passes against a live competition run (200 hostile connections survived; strongest link between anything received and a true coordinate r = 0.68, an accelerometer axis against tilt). **Build scan moved to B6**, with the Bench Server build |
| Per-attempt folder, SHA-256 manifest, HMAC signature | Done; the simulator's folder verifies with the Python tool, and every kind of edit is caught in tests on both sides |
| `ushqyr-bench verify`, `resim`, `leaderboard` | Done; `resim` reproduced a recorded attempt exactly (4 points, 16.394792 s) |
| Onboard, trackside and judge video | Done (needs ffmpeg on the machine) |
| In-app leaderboard with CSV export (`,` or `;`) | Done (Tab) |
| Judge replay viewer | Done: integrity state, timeline, decisions with reasons, five cameras |
| Tests | 37 `BenchTests` (335 EditMode in all), 10 SDK tests, 7 runner tests |
| Not verified | The competition options were exercised in the editor (through `UserSettings/bench-args.txt`), not yet from a built exe |

### B3 (19 November 2026): tracks, venues, complete sensors
| Item | State |
|---|---|
| LiDAR, IMU vibration noise, link jitter and loss, compute delay | Done, with tests |
| Full analog camera chain: lens distortion, auto-exposure, signal-dependent noise, tearing, colour drift, break-up | Done; 6.3 ms render + 2.2 ms processing per frame on the GTX 1650 Ti, the sync loop runs at about 2.2× real time |
| Realtime mode in the API and SDK | Done; the sample flew a lap in realtime mode unchanged |
| Parking venue, lighting variants | Done (`Bench_Parking`; hall: default / dim / warm, parking: default / dim / bright) |
| Five more public tracks | Done: `H2_Eight`, `H3_Slalom`, `X1_Mixed`, `P1_Columns`, `P2_Maze` |
| Oracle pilot passes every track | Done: all six, zero collisions, laps of 20 to 48 s (`Docs/bench/track_check.md`) |
| Track editor: place / move / turn / duplicate / delete, LED settings, start pad, venue, lighting, checks, test flight, save, map export | Done. No manual test flight (D35). Tried through its functions and a screenshot, not yet by hand with the mouse |
| Random track generator with seed and difficulty | Done: in the editor and as `-bench-generate`; 36 generated tracks validated in tests |
| Hidden-track workflow | Done as a procedure plus `ushqyr-bench check-build`; the private folder is not chosen (owner's decision) |
| Sample solution on the new sensors | `H1_Oval` 5 of 5 laps, `H2_Eight` 4 of 5, `P1_Columns` 4 of 5 (5 seeds each). Below the 90% acceptance bar on two tracks: to be improved before B8 |
| Tests | 49 `BenchTests`, 12 SDK tests, 8 runner tests |

### B4 (19 November 2026): drones, the part that needs no organiser data
| Item | State |
|---|---|
| Whoop v1 from the organisers' data | **Not started: the data has not arrived** (`Docs/reference/whoop/` is empty). v0 stays, marked `calibrated=false` |
| AI CORE: `drone.yaml`, JSON Schema, class limits file | Done (`bench/examples/drones/example_3inch.yaml`, `bench/schemas/drone.schema.json`, `StreamingAssets/Bench/class_limits.json`) |
| `ushqyr-bench validate-drone` | Done; same checks as the simulator, tested on both sides |
| Config hash in results | Done (SHA-256 of the file) |
| Collider and visual box from the declared size | Done |
| Flight | The example drone (240 g, thrust-to-weight 7) is stable in tests (hover, 20° step without oscillation) and flew the oval with the unchanged sample: 5 gates in 60 s, the sample being tuned for the whoop |

### B5 (19 November 2026): datasets and randomisation
| Item | State |
|---|---|
| Randomisation of light, background and noise, usable in normal runs | Done: `randomization` in the rules; the sample flew 5 of 5 laps under `mild`, identical for a repeated seed |
| Dataset generator: boxes, corner keypoints, optional masks | Done |
| YOLO and COCO output, splits, seed | Done; `dataset-check` confirms the two formats agree |
| In-app screen | A "Dataset" button per track in the track list (500 images); options only from the command line |
| Label-overlay check script | Done: `ushqyr-bench dataset-check <folder> --draw 100` |
| 2-pixel projection test | Done as a box-against-painted-outline check (D44): 228 of 230 boxes within 2 px (99.1%), 1268 of 1268 visible corners on their gate, on a 200-image dataset |
| Tests | 57 `BenchTests` (355 EditMode in all), 12 SDK tests, 10 runner tests |

### Sample solution, second version (19 November 2026)
The first `color_pid` ("turn left, take the biggest blob") fell below the acceptance bar once the lens, the analog link
and the new tracks arrived. Rewritten around four ideas: follow one gate by its direction in the world; measure by
angles from the box's width and top edge (the floor reflects the LEDs); remember where the last four gates stood by
dead reckoning and prefer gates that are not among them (the repeat rule makes a small circuit worthless); step
round obstacles with the LiDAR.

| Track | Before | After (5 seeds) |
|---|---|---|
| `H1_Oval` | 5 of 5, 24 to 37 s | 5 of 5, 25.5 to 25.8 s |
| `H2_Eight` | 4 of 5, 64 to 107 s | 5 of 5, 43 to 57 s |
| `P1_Columns` | 4 of 5, 42 to 66 s | 4 of 5, 51 to 53 s |

14 of 15 runs complete a lap (93%), which meets the plan's acceptance line for the sample (at least 90% over these
three tracks and five seeds). The one failure is on `P1_Columns` seed 5: the next gate, 18 m away in the dim light,
is not picked up after the second gate, and the drone wanders. Measured in the editor in training mode, sync time.

### B6, first part: Docker sandbox and `submit-check` (19 November 2026)
Docker Desktop with WSL2 was installed on the development PC (the owner chose it over a Linux machine for now).

| Item | State |
|---|---|
| Linux no-screen rendering spike | Done. The Linux player renders under Xvfb in a container: Mesa llvmpipe without a GPU, Mesa d3d12 on the video card under Docker Desktop for Windows |
| Bench Server build | Done: `BuildScript.BuildBenchServer`, Linux, Boot + the two Bench scenes, `USHQYR_BENCH_SERVER` (269 MB). Addressables are now rebuilt for the build's platform; before, a Linux build carried Windows language bundles |
| Images | `ushqyr-bench-server` (1.2 GB) and `ushqyr-bench-base:cpu` (550 MB); `ushqyr-bench build-images` |
| Solution template | `bench/template`; the sample has a Dockerfile |
| `submit-check` | Done: builds the image, checks its size, flies one competition attempt on an internal network, verifies the attempt folder |
| Hardening | Done and tested with `bench/runner/tests/hostile`: internet, DNS, host, image writes, `/results`, root, Docker socket, 400 processes, other simulator ports and ground truth all blocked |
| Sample in the sandbox | `H1_Oval`, seed 1: 28 points in the 120 s limit; 241 s of real time for the whole check with the GPU |
| Speed without a GPU | About 20 times slower than real time at Low, about 100 times at High. All of it is drawing |

**Not done in B6 yet:** ROS 2 bridge and images; Linux Team Kit build; a Linux server with a GPU (drawing there is
neither set up nor measured); the build scan for leaks (`check-build` against real hidden tracks, which exist only on
the server, D47); image size and resource limits are the plan's defaults, not the organisers'.

**Found on the way:** the camera picture depends on the graphics preset (render scale, shadows, anti-aliasing), and
the preset is each player's own setting. For a fair qualification the server must fix one preset for everyone, and
teams should train at that preset. Not decided yet.

### B6, second part: ROS 2 bridge (20 November 2026)
| Item | State |
|---|---|
| Messages | `bench/ros2/ushqyr_msgs`: `RcChannels` (16 channels, microseconds), `AttemptStatus` |
| Bridge | `bench/ros2/ushqyr_ros2_bridge`: a Python node on the SDK (D6). Camera image and info, IMU, range, laser scan, battery, `/clock`, status, the handshake as JSON; RC in as `RcChannels` or `sensor_msgs/Joy` |
| Images | `ushqyr-bench-ros2:jazzy` (2.5 GB) and `:humble` (2.05 GB); `ushqyr-bench build-images --ros2 <version>` |
| Sample as a ROS 2 node | `bench/examples/color_pid_ros2`: the same pilot fed from topics |
| Test in the sandbox, `H1_Oval`, seed 1, 120 s limit | Jazzy 27 points (280 s of real time); Humble 26 points (544 s); the plain script scores 28 |

Decisions taken here:
- **Sync mode over ROS 2:** each step the bridge publishes the sensors and waits up to `sync_wait` (0.2 s of real time)
  for a new RC message; none in time means a step without a packet. This makes a ROS 2 run depend slightly on the
  machine's speed, unlike a run through the SDK. The points differ by one or two gates for that reason.
- **The attempt starts on the first RC message**, so a node has time to load its model.
- **Camera info** carries the simulator's own distortion model (`ushqyr_radial_k1`), not `plumb_bob`: the simulator's
  formula goes from the distorted picture to the straight one, and the two are not the same function.
- **Lyrical** is not built: the organisers have not said which versions they need (question 15).
- **Package names stay `ushqyr_*`**, like the SDK (the plan said `gaterush_*`).
- The solution's container now has its own 256 MB of shared memory (`--ipc private`), which ROS 2's middleware uses
  between nodes; it is shared with nobody.
- `submit-check` now fails a solution that never starts the attempt, and when the real-time limit runs out it stops
  the solution first so the simulator can still write the attempt folder.

**Still open in B6:** Linux Team Kit build; a Linux server with a GPU.

### B7: batch runner (20 November 2026)
`ushqyr-bench batch --submissions ... --tracks ... --out ...` (`bench/runner/src/ushqyr_bench_runner/batch.py`).

| Item | State |
|---|---|
| Every team x track x attempt, same seeds for every team | Done |
| Simulator with no screen, hardened container, isolated network, timeouts, logs, video, signed manifest, teardown | Done (the B6 sandbox, per job) |
| Retries only infrastructure failures | Done (`--retries`, default 2) |
| Resume after an interruption | Done (`batch_state.json`) |
| Parallel workers | Done (`--workers`) |
| One `results.csv` and `results.json`, plus `leaderboard.csv` | Done |
| Hidden tracks from a folder outside the build | Done: mounted read-only into the simulator's container only |

**Tested for real** on the development PC: 3 solutions (the sample, the hover template, the hostile one) plus one
team with a missing image, on 2 tracks taken from an outside folder, 2 workers, signed with a test key. 6 of 6
possible attempts flown in 516 s; all six folders verify (checksums and signature); the sample scored 28 and 20, the
missing team was reported and did not stop the run; a second start flew nothing again.

The acceptance run itself, 20 solutions on 3 tracks, was flown on 20 November: see "B7, acceptance run" below.

Note: a track file carries its own `time_limit`, and that one is used; the rules file's value did not shorten the
test attempts.

### B8, first part: documentation in three languages (20 November 2026)
`Docs/bench/ru` (the reference text), `Docs/bench/en`, `Docs/bench/kk`; the same twelve pages in each.

| Page | State |
|---|---|
| `README` (index), `quickstart` (30 minutes, Windows and Linux), `rules`, `drone` (drone and sensors, AI CORE), `sdk`, `tracks` (and editor), `dataset`, `docker`, `ros2`, `troubleshooting`, `organizer`, `protocol` | Written in Russian (`organizer` and `protocol` translated from the English originals), translated to English and Kazakh |
| Kazakh | Machine translation with a fixed glossary and the app's own strings for buttons. **Every page is marked as needing a native review**; no reviewer is named yet (question 16) |
| Consistency | `bench/runner/tests/test_docs.py`: the same pages in every language, the same code blocks, table sizes and headings as the Russian text, every link resolves |

`Docs/bench/organizer.md` and `protocol.md` moved to `Docs/bench/en/`.

**Not done in B8:**
- The 30-minute test with people who have never seen it (at least 3, Windows and Linux): needs people.
- The pilot with 3 to 5 teams and its fix list: needs the organisers.
- The quickstart's Linux half is untested on a Linux desktop (see the Team Kit entry below).

### B6, third part: the Team Kit (20 November 2026)
What a team downloads: the simulator, the `bench` folder and the documentation in one archive, with the repository's
own paths so that every command in the documentation works unchanged.

| Part | State |
|---|---|
| Team Kit player (`Ushqyr/Build/Build Team Kit (Windows)` and `(Linux)`) | Done: only Boot and the `Bench_*` scenes, define `USHQYR_BENCH_KIT`, opens AI Bench without `-bench`, windowed 1280x720. Editor, oracle, datasets and training mode are in; no other game mode is |
| `ushqyr-bench make-kit` | Done: player + `bench/` (SDK, examples, template, runner, Docker, ROS 2, schemas) + `Docs/bench/` + optionally `Builds/BenchServer/` + `README.txt`, zipped beside the folder; Linux executables keep their run permission in the zip; with `--hidden` it refuses to zip a kit that holds a hidden track |
| Left out of the kit | Unity's debug folders, caches, `.meta` files, the hostile test solution |
| Runner installed with pip (not `-e`) | Fixed: it finds the `bench/` folder from the current directory |

Version 1.4. `UshqyrAIBench-win-1.4.zip` 342 MB, `UshqyrAIBench-linux-1.4.zip` 344 MB, both with the server build
(in `Builds/TeamKit/`, not in the repository).

**Tested for real:** the Windows kit, by the quickstart: the player started with no options opened AI Bench on
`H1_Oval` in training mode, the SDK was installed from the kit into a fresh virtual environment, and the kit's
`color_pid` sample scored 6 of 6 gates in 25.5 s.

**Tested for real, Linux kit in a container** (Ubuntu image, GPU through WSL, virtual display): the player started
with no `-bench` opened AI Bench on `H1_Oval`; the SDK was installed from the kit; the kit's `color_pid` sample scored
6 of 6 gates in 25.539 s, the same time to the millisecond as the Windows kit.

**Not tested:** the Linux kit on a real Linux desktop (window, own GPU driver, unzipping on Linux).

**Notes:**
- No hidden track exists yet (D47), so neither kit went through a `--hidden` check. It must be run on the
  qualification server before anything is handed out.
- In the kit Esc does nothing in AI Bench (there is no menu to return to); the window is closed the usual way.

### B7, acceptance run: 20 solutions on 3 tracks (20 November 2026)
One command, competition mode, sync time, 2 workers, signed with the test key, on the development PC (GTX 1650 Ti).
Tracks `H1_Oval`, `H2_Eight`, `P1_Columns`, taken from a folder outside the build. The set is generated by a script
and lives in `Builds/bench_test/accept` (not in the repository).

| Solution | Count | Status on every track | Right? |
|---|---|---|---|
| The sample, unchanged | 3 | `time_limit`, 56 to 58 points over the three tracks | Yes |
| The sample with another cruise angle | 4 | `time_limit`, 46 to 73 points | Yes |
| The sample as a ROS 2 node | 2 | `time_limit`, 49 and 51 points | Yes |
| The hover template | 2 | `time_limit`, 0 points | Yes |
| Crashes at start | 1 | `timeout` (never connected), 0 points | Yes |
| Crashes 5 s into the flight | 1 | `disconnected`, keeps the 1 point it had on `H1_Oval` | Yes |
| Hangs 3 s into the flight | 1 | `timeout` (10 s without a step) | Yes |
| Never connects | 1 | `timeout` after 60 s | Yes |
| Floods its output (1000-character lines without end) | 1 | `time_limit`, 57 points; the kept log is the last 400 lines | Yes |
| Very slow (30 ms of sleep per step) | 1 | `time_limit`, 57 points: sync mode waits for it | Yes |
| Asks for ground truth | 1 | Flies; no truth is sent | Yes |
| The hostile one | 1 | `finished`, 0 points; every break-out line says "blocked" | Yes |
| No image | 1 | Reported as a problem, 3 jobs not flown, the others go on | Yes |

57 of the 57 possible attempts have a result (the exit code is 1 because of the team without an image); one
`results.csv`, `results.json`, `leaderboard.csv`; all 57 attempt folders pass `verify` (checksums and signature); no
infrastructure failure and no retry. The last, uninterrupted stretch (53 jobs) took 1 h 46 min; the results take
5.8 GB, almost all of it video.

**Forced stops:** the run was killed three times, once between jobs and twice during flights, and continued with the
same command each time.

**Found and fixed:** a run killed during a flight left its containers and networks running (a solution that never
connects would sleep there for ever). Everything a batch starts now carries a label with its results folder, and the
next start removes what a killed run left. Two half-written attempt folders of the killed flights stay beside the
finished ones; they have no `result.json` and are not counted.

**Found, open: the camera picture is not always the same between two runs.** Three copies of the same sample with
the same seed got the same points on `H1_Oval` (28) and `H2_Eight` (17), but 13, 11 and 11 on `P1_Columns`, and their
times on the hall tracks differ by a few hundredths of a second. The logs show where: sensors, commands and ground
truth are identical for up to 100 s (in some pairs for all 12,000 steps, and always for the hover template, which
does not use the camera), then the program's command changes first, with every logged sensor still equal. The only
input that is not logged is the camera frame. So the physics and the referee are deterministic, the frame is not,
bit for bit; it showed with one worker as well (the two Linux kit runs: 25.615 s and 25.539 s). The determinism rule
("same build, track, seed and commands give the same flight") holds; "the same program gives the same score" does
not yet. **Fixed on 21 November: see "Camera determinism" below.**

### Camera determinism: the same program now gets the same pictures (21 November 2026)
**Evidence.** A solution that replays recorded commands flies the same path every time, so its frames must be equal.
With a fingerprint of every frame written into the attempt folder, all 1801 frames of four such runs differed, from
frame 0 with the drone still on the pad, already in the render before the lens and the video link. In the editor on
Windows the same still scene rendered identically frame after frame. Saved renders from the Linux server showed two
patterns: noise in corners and creases (High preset), and plus or minus one on almost half of all pixels (Low preset).

**Two causes, both in the frame counter.** In a paced run the number of window frames between two camera frames
depends on the load of the computer, and two parts of the rendering follow that counter:

| Cause | Where | Fix |
|---|---|---|
| Ambient occlusion with the blue-noise method moves its noise pattern with `Time.frameCount` and takes the next noise texture on every render. Only in a built player: in the editor URP switches this off, which is why the editor was stable | URP, `SSAOUtils` | The four PC renderers use the interleaved-gradient method, which depends on the pixel's place on the screen only |
| The graphics preferences were applied to every game camera, the drone's camera included: they switched post-processing, dithering and anti-aliasing back on over the sensor's own setup. URP seeds the dither pattern from `Time.frameCount` | `GraphicsQuality.ApplyScene` | A camera can be marked as an instrument (`GraphicsQuality.LeaveAlone`); the sensor camera is, and keeps its own setup: no post-processing, no dithering, no anti-aliasing |

**Verified on the Linux server, 2 workers:** two replays, 181 frames each, equal in every byte; then three copies of
the sample on `H1_Oval`, `H2_Eight` and `P1_Columns` for the full 120 s: `telemetry.csv`, `truth.csv`, `frames.csv`
and `events.csv` are the same files in all three copies on every track (28, 20 and 12 points).

**New in the attempt folder:** `frames.csv` (frame number, time, fingerprint of the render, fingerprint of the
delivered frame). **New diagnostic option:** `-bench-dump-source N` saves the first N renders as raw RGB.

**What this changes, to know:**
- **The camera picture is a little different from before**: no post-processing (bloom, tone mapping), no
  anti-aliasing. This is what the sensor's code always asked for; the preferences were overriding it. The sample
  scores 28, 20 and 12 on the three tracks (before: 28, 17 and 11 to 13). Datasets made before this date show the old
  picture and should be made again.
- The picture no longer depends on the post-processing and anti-aliasing preferences. It still depends on the preset
  through shadows and ambient occlusion, so "one preset for the whole qualification" stays.
- The ambient occlusion grain of the whole game on High and Ultra changed slightly (another noise pattern).
- **The Team Kit archives of 20 November (v1.4) were built before this fix**: their players have the old picture and
  no `frames.csv`. They must be built again before anything is handed out.
- Checked on one machine only. Two different video cards or drivers may still differ in the last bit; that needs a
  second machine to find out.
