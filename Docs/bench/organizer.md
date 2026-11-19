# AI Bench: organiser guide (B5)

How to run an attempt in competition mode, what the simulator writes, and how to check it. Team-facing material is in
`ru/quickstart.md` and `protocol.md`.

## Running one attempt

```
Ushqyr.exe -bench -bench-mode competition -bench-team "Team name" -bench-attempt 1 ^
           -bench-track H1_Oval -bench-out D:\qualification\results -bench-key D:\secret\bench.key -bench-exit
```

Then start the team's program. One launch is one attempt.

| Option | Meaning |
|---|---|
| `-bench` | Open AI Bench instead of the menu |
| `-bench-mode training\|competition` | Default `training` |
| `-bench-track <name or file>` | A public track by name, or any track file by path (this is how hidden tracks are used) |
| `-bench-rules <file>` | Rules file; missing fields keep the defaults of `StreamingAssets/Bench/rules.json` |
| `-bench-team`, `-bench-attempt`, `-bench-solution <digest>` | Written into the result |
| `-bench-out <folder>` | Where attempt folders go. Without it: `BenchResults` in the user's data folder. In training mode attempts are recorded only when this is given |
| `-bench-key <file>` | The signing key (at least 16 bytes of anything). Also `USHQYR_BENCH_KEY_FILE`. Without it attempts carry checksums but no signature |
| `-bench-ffmpeg <file>` | ffmpeg for the videos. Also `USHQYR_FFMPEG`, `StreamingAssets/Bench/ffmpeg.exe`, or the PATH. Without ffmpeg the attempt is recorded without video |
| `-bench-seed <n>`, `-bench-port <n>`, `-bench-token <text>` | Noise seed, API port, token the program must present |
| `-bench-listen-all` | Accept the program from another machine or a container (default: this computer only) |
| `-bench-exit` | Quit when the attempt is over and its files are written |
| `-bench-replay <attempt folder>` | Open the judge replay of an attempt |
| `-bench-edit <name or file>` | Open the track editor on a track |
| `-bench-generate <venue> <seed> <difficulty> <file>` | Write a random track to a file and quit (venue `hall` or `parking`, difficulty 1 to 3) |
| `-bench-drone <drone.yaml>` | Fly a team's own drone (class AI CORE) instead of the whoop |
| `-bench-limits <class_limits.json>` | The limits of the AI CORE class (default: the file next to the tracks) |
| `-bench-dataset <folder>` | Make a labelled dataset instead of running an attempt; with `-bench-dataset-count`, `-bench-dataset-seed`, `-bench-dataset-profile`, `-bench-dataset-masks`. Training mode only |

In the editor the same options are read from `UserSettings/bench-args.txt`, one option or value per line.

## Rules file

Everything the organisers may want to change lives in a rules file (`-bench-rules`), not in the build. The fields and
their defaults are in `StreamingAssets/Bench/rules.json`. Those added in B3:

| Field | Default | Meaning |
|---|---|---|
| `time_mode` | `sync` | `sync`: one step per command, repeatable. `realtime`: the simulator follows the wall clock |
| `rc_latency_ms`, `rc_jitter_ms`, `rc_loss_percent` | 10, 0, 0 | Link delay, extra delay that varies per packet, share of packets lost |
| `compute_delay_ms` | 0 | A fixed delay standing for the program's processing time, so slow and fast computers fly the same in sync mode |
| `camera_distortion` | 0.18 | Lens barrel distortion (`k1`); 0 = none |
| `camera_auto_exposure` | true | |
| `video_range` | 80 | Distance in metres at which the analog video is at its worst |
| `lidar`, `lidar_hz` | true, 10 | |
| `randomization` | `none` | `mild` or `strong`: each attempt varies the scene from its seed (see "Randomisation") |

## Tracks and venues

Two venues: **hall** (40 × 20 × 8 m) and **parking** (60 × 34 m, 3.4 m clear height, a grid of 28 columns, parked
cars along the long walls). Each has lighting variants (hall: default, dim, warm; parking: default, dim, bright),
chosen by the track file.

Six public tracks ship in `StreamingAssets/Bench/Tracks`:

| Track | Venue | Gates | Idea |
|---|---|---|---|
| `H1_Oval` | hall | 6 | Flat oval; the next gate is always in view |
| `H2_Eight` | hall | 8 | Figure-eight with a crossing |
| `H3_Slalom` | hall | 7 | Offset slalom and a 180° hairpin |
| `X1_Mixed` | hall | 10 | Five LED colours, with painted and glowing panels in the same colours |
| `P1_Columns` | parking | 6 | A loop round the column rows, low light |
| `P2_Maze` | parking | 9 | Turns behind columns: the next gate is often out of sight |

`track_check.md` (next to this file) records that each of them validates and that the oracle pilot completes a lap.

**Track editor.** In the app: **T** opens the track list (fly, edit, new). In the editor, drag gates on the floor
plan, give each its LED colour, brightness, steady or blinking, and allowed direction; move and turn the start pad;
pick the lighting and the time limit. The checks run as you edit: enough gates for the repeat rule, clearance to
walls, columns and cars, room to fly through, spacing, a gate in view from the start. **Test flight** lets the oracle
pilot fly the track and draws its path. **Save** writes the file to the user's `BenchTracks` folder; **Export map**
writes a top-down picture next to it; **Fly it** opens the track for a program.

**Oracle pilot.** An autopilot that knows the true positions. It is for authoring only (test flights, track checks,
later dataset camera paths), is compiled out of the server build, and has no connection to the API.

**Random tracks.** The editor's "Random track" (seed, level 1 to 3), or from the command line:
`Ushqyr.exe -bench-generate parking 4217 3 D:\private\HID_p_4217.benchtrack.json`. The same seed gives the same
track, and every generated track passes the checks. Test-fly it in the editor before using it.

## Team drones (AI CORE)

A team declares its drone in a `drone.yaml`: size, mass, motor thrust and lag, propellers, battery, controller limits,
camera tilt and field of view, and which sensors it carries. An example with comments is
`bench/examples/drones/example_3inch.yaml`; the shape of the file is in `bench/schemas/drone.schema.json`.

- The class limits are yours: `StreamingAssets/Bench/class_limits.json` (or `-bench-limits`). Defaults: at most
  300 × 300 × 180 mm (the task's limit), 25 to 1500 g, thrust-to-weight 1.5 to 8, up to 6 cells and 2200 mAh,
  rates up to 1000 deg/s, camera tilt 0 to 60°, field of view 60 to 150°, sensors from
  `camera, imu, tof, lidar, battery`.
- A team checks its file with `ushqyr-bench validate-drone drone.yaml [--limits class_limits.json]`. The simulator
  runs the same checks when it loads the file and refuses a drone that fails, with the reasons in plain words.
- Mass, thrust, motor lag, battery and limits are taken as declared. Inertia, drag and controller gains are computed
  from size and mass, so a team cannot declare itself an unphysical drone through them.
- The drone flies as a box of its declared size with a collision sphere to match. A sensor that is not declared is not
  sent. The SHA-256 of the file is the `drone_config_hash` of every result.
- The file is plain YAML: nested keys by indentation, numbers, text, and lists in one line `[a, b, c]`.

The organisers' whoop (class AI MISSION) is still the placeholder model; its calibration waits for measurements.

## Randomisation

`randomization` in the rules file makes every attempt vary the scene from its seed. The same seed gives every team
the same variation, and a variation never breaks the track's checks.

| | `mild` | `strong` |
|---|---|---|
| Gates | shifted up to 10 cm, turned up to 3° | 25 cm, 8° |
| Start pad | 15 cm, 5° | 30 cm, 10° |
| Light level and colour | 0.8 to 1.2, slightly warm or cool | 0.45 to 1.6, clearly warm or cool, direction changed |
| LED brightness | 75 to 100% | 45 to 100% |
| Video noise | 0.8 to 1.3 times | 0.5 to 2.5 times |
| Floor and wall materials | unchanged | one of five each |
| Clutter along the walls | none | up to 8 coloured boxes and panels, some glowing |

The attempt folder holds the track as it was flown, so the replay shows the gates where they stood.

## Datasets

Teams train detectors on labelled images made by the simulator itself. In the app: **T**, then **Dataset** next to a
track (500 images into the results folder). From the command line:

```
ushqyr-bench dataset --sim Ushqyr.exe --track H1_Oval --out data/h1 --count 2000 --seed 7 --masks
ushqyr-bench dataset-check data/h1 --draw 100
```

- **Images** come through the same camera as a run: lens, exposure and the analog link, with the strength of the
  link's effects, the exposure and a motion blur varied per image.
- **Camera poses**: 70% along the oracle pilot's flight with random offsets, 30% anywhere in the venue.
- **Scene**: the `strong` randomisation above, redrawn every 25 images, plus random LED colours.
- **Labels** for every gate in view: a box; the four corners of the opening on the face towards the camera
  (top-left, top-right, bottom-right, bottom-left as seen) with visibility 0 = outside the picture, 1 = hidden,
  2 = visible; `occluded` and `truncated` flags; distance.
- **Formats**: YOLO (`images/`, `labels/`, `data.yaml`; box plus keypoints, the "pose" layout) and COCO
  (`annotations/instances_{train,val,test}.json`). Split 80 / 10 / 10 from the seed.
- **Masks** (`--masks`): one PNG per image, 0 = no gate, n = the n-th gate of the track.
- **Label check**: every image is rendered a second time with the lights out and each gate glowing in its own
  colour. The box of each gate that is wholly in view must match the outline of its painted pixels within 2 px, and
  each visible corner must lie on its gate. The counts are in `dataset.json`; `dataset-check` fails below 98%.
  `--draw` writes labelled pictures (and the masks beside them) into `check/` for a look by eye.

Dataset mode does not exist in competition mode or in the server build.

## Hidden tracks

A hidden track is an ordinary track file that is never part of anything handed out.

1. Make it in the editor or with `-bench-generate`, and give it an id that says nothing (the id goes into results).
2. Keep it in a private folder outside the project and outside every build and kit.
3. Fly it by path: `-bench-track D:\private\HID_p_4217.benchtrack.json`. In competition mode the API never names the
   track; results carry its id and hash, and the attempt folder (judges only) carries a copy of the file.
4. Before handing out a build or kit, prove the tracks are not in it:

```
ushqyr-bench check-build <build or kit folder> --hidden D:\private
```

It fails if any file there is a hidden track, or contains a hidden track's id or gate data (also re-indented or
inside another file). Compressed archives are not unpacked: check the folder before zipping it.

## What competition mode changes

- No ground truth, no score, no track name on the API; `RESET` and `PING` are refused; the simulation always runs at
  the pace of a real flight.
- Nobody can fly by hand: AI Bench reads no keyboard, gamepad or radio, in either mode.
- One attempt per launch. The program has `connect_timeout` (60 s) to connect and start, and `step_timeout` (10 s) per
  step, both in real time.
- Every attempt is recorded, with video when ffmpeg is available.

## The attempt folder

`<out>/<team>_<track>_a<attempt>_<UTC time>/`

| File | Content |
|---|---|
| `result.json` | Points, time of the last scored gate, status, team, track id and hash, seed, build, rules hash, drone and its config hash, solution digest |
| `events.jsonl`, `events.csv` | Start, every gate pass (scored or not, with the reason), collisions, failsafes, end |
| `telemetry.csv` | Per step: the RC packet the program sent, armed and failsafe flags, and the IMU, ToF and battery readings it was given |
| `truth.csv` | **Judges only.** The true position, attitude and velocity per step |
| `track.benchtrack.json` | The track that was flown |
| `onboard.mp4` | Exactly the frames the program received |
| `trackside.mp4` | Fixed cameras around the venue, following the drone |
| `judge.mp4` | Trackside with the onboard picture in the corner, the clock, the running score and each scoring decision |
| `replay.grreplay` | Index for the in-app replay |
| `sim.log` | The recorder's own log |
| `manifest.json` | SHA-256 and size of every file above, plus the hashes that identify the run |
| `manifest.sig` | HMAC-SHA256 of `manifest.json` with the signing key |

Statuses: `finished` (the program said it was done), `time_limit`, `timeout`, `disconnected`, `stuck` (no movement for
10 s), `invalid` (our side failed, or a training attempt was restarted; does not count).

## Checking results

```
pip install ./bench/runner ./bench/sdk/python

ushqyr-bench verify <attempt folder> --key <key file>
ushqyr-bench leaderboard <results folder> --csv results.csv --board leaderboard.csv --semicolon
ushqyr-bench resim <attempt folder>
```

- **verify** prints `verified`, `checksums_only` (no key given or not signed) or `modified` with the file that differs.
  Editing a log breaks its checksum; rewriting the manifest to match breaks the signature.
- **leaderboard**: more points first, then the smaller time. A team's line for a track is its best counted attempt;
  all tracks are added up unless `--track` is given.
- **resim** replays the recorded RC packets with the attempt's seed and must print `same result`. It needs the
  simulator running in training mode on the attempt's track:
  `Ushqyr.exe -bench -bench-track <attempt folder>\track.benchtrack.json`.

In the app, **Tab** opens the leaderboard (this track or all tracks, CSV export, latest attempts). **Replay** next to
an attempt opens the judge replay: integrity state first, then a timeline, the list of scoring decisions (click to
jump), and five cameras (key C: chase, overview, onboard, trackside, free). Space plays and pauses; the arrow keys
step by 0.05 s, with Shift by 1 s.

## The leak test

`bench/runner/leak_test.py` proves that coordinates can't be obtained through the API. Start a fresh simulator in
competition mode with `-bench-out`, then:

```
python bench/runner/leak_test.py --out <results folder>
```

It sends hostile input over 200 connections, flies one attempt while asking for everything it should not get, tries
the forbidden messages, and then compares every number it received with the judges' `truth.csv` of that attempt.
Exit code 0 means nothing leaked.

## Solutions in Docker (B6)

A team's solution is a Docker image. The simulator runs in a second container, and the two share a network that has
no way out.

```
ushqyr-bench build-images                       # ushqyr-bench-server (simulator) and ushqyr-bench-base:cpu
ushqyr-bench submit-check <folder or image.tar> --track H1_Oval --seed 1 --out results [--key key.file] [--logs]
```

`build-images` needs the Linux server build in `Builds/BenchServer` (Unity menu **Ushqyr / Build / Build Bench Server
(Linux)**). That build holds only the AI Bench scenes and is compiled with `USHQYR_BENCH_SERVER`: the oracle pilot, the
track editor, the dataset tool and the ground-truth message are not in it.

**The simulator's container** starts a virtual display (Xvfb) and is never started with `-nographics`, which would
turn the camera off. How it draws:

| Where | How | One 120 s attempt (sample solution, H1_Oval) |
|---|---|---|
| Docker Desktop on Windows, `--sim-gpu auto` | Mesa's Direct3D 12 driver on the PC's video card (`--gpus all`, `/usr/lib/wsl` mounted read-only) | about 4 minutes for the whole check, videos included (GTX 1650 Ti) |
| No GPU | Mesa's software OpenGL (llvmpipe) | about 40 minutes at the Low preset with no trackside video; about 3 hours at High |
| Linux server with a GPU | **not set up or measured yet**: needs the real machine | — |

`-bench-graphics Low|Medium|High|Ultra` sets the preset for one run and `-bench-video on|off` overrides the rules
file's video setting. The preset changes the camera picture slightly (shadows, anti-aliasing), so a qualification
must run every team at the same preset. It writes the attempt folder to
`/results`; the rules file, the signing key and a folder of hidden tracks are mounted into it read-only when given.

**The solution's container** is treated as hostile:

| Measure | Docker option |
|---|---|
| Only the simulator is reachable; no internet, no DNS, no host | `--network <internal network>` |
| File system read-only, 256 MB of `/tmp` in memory | `--read-only --tmpfs /tmp` |
| No capabilities, no privilege gain, not root | `--cap-drop ALL --security-opt no-new-privileges --user 10001:10001` |
| Memory 4 GB (no swap), 4 processors, 256 processes | `--memory --memory-swap --cpus --pids-limit` |
| Nothing of the host mounted; shared memory of its own only (256 MB) | no `-v`; `--ipc private --shm-size` |

The limits are the plan's defaults (`sandbox.Limits`); the real ones are the organisers' decision.

`bench/runner/tests/hostile` is a solution that tries to get out (internet, DNS, the host, writing into the image,
reading `/results`, becoming root, the Docker socket, 400 processes, other ports of the simulator, asking for ground
truth). Run it with `submit-check --logs`: every line must say `blocked`.

### ROS 2

`ushqyr-bench build-images --ros2 jazzy` (or `humble`) builds the base image for ROS 2 solutions: ROS 2, the SDK, the
bridge node and its messages. A ROS 2 solution is an ordinary submission: its container runs the bridge and the team's
nodes together, and the simulator sees one client. Topics, parameters and timing are in `Docs/bench/ru/ros2.md`.

## Not in B2

- The scan of a Bench Server build for ground-truth code and hidden tracks comes with that build (B6).
- Programs are not yet isolated in a container (B6); until then the "no memory or file access" guarantees depend on
  running the program on another machine or account.
- The signing key and the hidden tracks have no fixed home yet: both are given by path and live outside the project.
- The track editor has no manual test flight: AI Bench has no hand-flying at all. Test flights are the oracle pilot's
  or a program's.
