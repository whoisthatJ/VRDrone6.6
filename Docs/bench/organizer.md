# AI Bench: organiser guide (B3)

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

## Not in B2

- The scan of a Bench Server build for ground-truth code and hidden tracks comes with that build (B6).
- Programs are not yet isolated in a container (B6); until then the "no memory or file access" guarantees depend on
  running the program on another machine or account.
- The signing key and the hidden tracks have no fixed home yet: both are given by path and live outside the project.
- The track editor has no manual test flight: AI Bench has no hand-flying at all. Test flights are the oracle pilot's
  or a program's.
