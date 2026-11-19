# AI Bench: organiser guide (B2)

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

In the editor the same options are read from `UserSettings/bench-args.txt`, one option or value per line.

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
