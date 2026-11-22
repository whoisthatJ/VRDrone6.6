# Education Suite Plan (TZ items 5–11): training, racing, drone soccer, scenarios, programming, teacher tools, platform

> **Platform:** desktop PC simulator, no VR. Windows first; Linux where the AI Bench already needs it.
> **Requirements source:** `Docs/reference/EDU_TZ_ru.md` (items 5–11 as received). **If this plan and the TZ disagree, the TZ wins**; record the difference under "Decisions".
> **Not covered:** TZ items 1–4 were not sent. This plan assumes they describe things that already exist or are already planned (physics, drones, locations, controls).
> **Builds on:** `PLAN.md` (flight model, races, ghosts, radios), `TRACK_GARAGE_PLAN.md` (spline tracks, obstacle kit, garage), `PHASE3_PLAN.md` (bots, LAN sessions), `AIBENCH_PLAN.md` (API server, Python SDK, sensors, recorder, replay viewer, docs site), `VISUALS_PLAN.md`.

## 0. What this phase is
It turns the simulator into a **classroom product**. Students learn to fly (5), race (6), play drone soccer (7), fly work missions (8) and program drones (9). A teacher runs the lesson from one PC (10). A school installs it without internet, with a licence per workstation, in Kazakh, Russian and English (11).

**Size.** Seven modules. This block is about as large as everything planned before it put together. The order in §11 is chosen so that every step ends with something a school can already use.

**Priority against the AI Bench.** The AI Bench has real competition dates and its demo is overdue. This phase must not delay AI Bench milestones B1–B2. Pieces both need (API server and Python transport, recorder and replay, docs site, Linux build, three-language UI) are built once and shared.

## 1. TZ → plan map
| TZ item | What it asks | Section | Milestone |
|---|---|---|---|
| 5 Training | 30 exercises with auto-scoring; free flight; record and replay | §4 | ED3 |
| 6 Racing | 10 tracks; track editor; race of up to 8 pilots over LAN; timing; tournament bracket | §5 | ED5 |
| 7 Drone soccer | Field and rules per FAI; 20 cm and 40 cm spheres; match up to 5 v 5 over LAN; referee mode | §6 | ED8 |
| 8 Scenarios | 10 ready scenarios of 7 kinds; teacher's editor; work on any location | §7 | ED6 |
| 9 Programming | Python API; MAVLink checked with QGroundControl; block editor; route on a map; camera and sensors; 10 auto-checked tasks | §8 | ED7 |
| 10 Teacher | Groups; assigning tasks; watching the seats; changing weather and failures live; journal; reports in XLSX/CSV and PDF | §9 | ED4 |
| 11 Platform | Installer; offline per-seat licence; local data; UI in Kazakh, Russian, English | §10 | ED1 (languages, data), ED9 (installer, licence) |

## 2. Decisions
Items marked ❓ are my defaults; the customer should confirm them (§13). Each ❓ value lives in a settings or rules file.

| # | Decision | Why |
|---|---|---|
| D1 | **One "Activity" model** for everything that can be assigned and graded: exercise, race, soccer match, scenario, programming task | The teacher's assigning, journal and reports then work the same way for every module |
| D2 | **Scores are computed from the recorded flight.** Scoring is deterministic and can be re-run | A teacher can re-check any grade; a rubric fix can re-grade old attempts |
| D3 | **Assisted flight modes and a built-in autopilot sit on top of the existing flight controller and physics.** No second physics | One flight model to calibrate; bots, students' programs and people all fly the same drone |
| D4 | **Two network layers.** A light *classroom link* (teacher ↔ every seat, always on) and *game sessions* (a race or a match among chosen seats, on the Phase 3 netcode). Each pilot's PC simulates its own drone; the host is the referee | Stick response stays instant; the teacher sees everything without joining every race |
| D5 | **Sport rules are data.** FAI soccer values and race formats live in rules files that name the FAI edition | FAI updates the rules; nothing should need a rebuild |
| D6 | **A scenario = template + placement rules + seed**, applied to a location's profile | This is what makes "works on any location" achievable |
| D7 | **Programming: one Python API.** Blocks generate Python. A bundled Python runs it. The editor ("Studio") is a local web page opened in an app-style browser window. Everything works offline | One execution path to test; students see the Python their blocks become |
| D8 | **MAVLink: built-in endpoint first**, presenting itself as an ArduPilot-compatible multirotor, tried in a time-boxed spike. **Fallback: the real ArduPilot simulator** connected to our physics. Acceptance is the same QGroundControl checklist either way (§8) | QGroundControl is picky about vehicles that call themselves "generic": its mode list stays empty and guided actions are refused |
| D9 | **All data stays local:** one database file on the teacher's PC, a cache on each seat. No cloud, no analytics, no internet needed at any point | TZ item 11 |
| D10 | **Licence = a signed file tied to the PC's hardware**, activated without internet | TZ item 11 |
| D11 | **Three languages from the first screen.** No user-facing text is hard-coded anywhere | Retrofitting translation later costs far more |
| D12 | **Pass mark 60 of 100, stars at 60 / 80 / 95** ❓ | Needed to make "auto-scoring" concrete; teachers can change it |
| D13 | **The game is called Ushqyr.** The plan and the prompt say GateRush; code, files and screens use Ushqyr. The Python package of §8 keeps the name `gaterush` until the customer says otherwise ❓ | Same decision as D12 of `AIBENCH_PLAN.md` |
| D14 | **Database: SQLite through the library the operating system already has** (`winsqlite3.dll` on Windows 10 and 11, `libsqlite3.so.0` on Linux), called directly. Nothing is bundled, no third-party code. Layout changes are numbered steps, each applied completely or not at all | ED0 spike: works outside Unity; the check inside a player build is in `Docs/spikes/player_build.md`. LiteDB (the plan's fallback) is not needed |
| D15 | **Reports are written by our own two small writers** (`XlsxWriter`, `PdfWriter`), with the Liberation Sans font file inside every PDF. No report library | ED0 spike: both files read back correctly with every Kazakh letter; no licence to track beyond the font's (SIL OFL) |
| D16 | **Activities are JSON files under `StreamingAssets/Edu/Activities`**, one file per activity, named after its id. The hash of an activity is taken over its content with names sorted, so spacing and key order do not change it | A content author edits a text file; a build needs no code change; an old grade always names the exact version it was earned on |
| D17 | **Assisted modes and the autopilot are an outer loop** (`FlightAssist`, `Autopilot` in `Scripts/Flight`): they read the drone's state and produce ordinary Angle-mode stick commands for the existing flight controller. The flight model got one addition, `QuadState.MotorLoss` (thrust lost per motor, 0 by default), and a healthy drone computes exactly what it did before | This is D3 made concrete. AI Bench flies the same code path as before |
| D18 | **The autopilot flies on what the sensors say** (`PositionEstimator`: GPS and barometer outdoors, optical flow indoors, all from a seed), and the physics runs on the truth | "Positioning lost" and "GPS degraded" then have real consequences, and two runs with one seed are the same |
| D19 | **MAVLink: path A (our own endpoint), provisionally.** It presents an ArduPilot quadrotor and passes 25 of 25 automated checks flying the real flight model through the autopilot. Final only after the QGroundControl try | `Docs/spikes/mavlink_qgroundcontrol.md` |
| D20 | **Soccer contacts: each PC simulates its own ball**, other balls are predicted forward by their speed, one bounce per pair until they separate, the host decides goals and fouls. Provisional until tried on two real instances in ED8 | `Docs/spikes/lan_collisions.md` (a numerical model, not yet a LAN test) |
| D22 | **Third-party parts are fetched by a script at pinned versions and checked against recorded hashes** (`Tools/Edu/fetch_third_party.py`). Blockly (923 KB of text files) is kept in the repository; the Python runtime (237 MB) is not, and a build takes it from `ThirdParty/edu/python` | A build never depends on what the internet serves that day; the repository stays small |
| D23 | **Soccer follows FAI Volume F9, 2026 Edition, Version 2 (effective 1 June 2026)**, as read into `Docs/reference/fai/f9a_values.md`. Where that page and the table in §6 differ, the page wins: goal rings 2 m from the ground to the bottom of the inner opening, hanging from the cage's ceiling; **Position and Altitude hold are off in soccer** (the rules forbid automatic position and height holding); the Striker changes within a set only when its ball can no longer fly; instant replay is not a referee's tool in a "strict FAI" match | The prompt's rule "sport rules follow the FAI text"; which edition the customer's events use is still question 6 of §13 |
| D24 | **A flight nobody flew records the commands, not empty sticks.** With a person at the sticks the recording holds the sticks as held; with nobody there (a mission, a student's program) it holds what the flight controller was given, and the note `sticks` says which | The stick display, the throttle graph and the smoothness measure would otherwise be empty for every programmed flight |
| D25 | **The time between two flights is measured along the path**: when the other flight was at the nearest place, searching forward only; no figure when the two are more than 5 m apart | Laps and crossings must not be confused; for a hover the figure means little, so the hub shows it only for activities with a route |
| D26 | **Reference flights are files** `Edu/Activities/<id>.grreplay`, flown in the simulator; a test requires one for every shipped activity, scoring 90 or more | The prompt's rule "activities are data with a reference flight of 90 or more as a test", on real flights |
| D27 | **A flight with nobody chosen is stored under the seat**, a profile of the role "seat" named after the computer, never under a student; the choice of profile is forgotten when the program closes | A shared PC must not credit one student with another's flight; "seat name only" of F5 with no personal data |
| D28 | **PINs are typed on an on-screen keypad**: digits only, 4 to 12 | Works with a mouse alone and in a headset; a PIN only keeps classmates out |
| D29 | **The school build is the same code built without the internet packages** (`Tools/Edu/school_manifest.py`); a define set by the package's presence decides what is compiled. The build also switches off the engine's own reporting, including "Engine Diagnostics" | One code base to test; measured: 3 connections at start before, 0 after |
| D30 | **LAN races stay in the school build; internet races and online lap boards do not** | F5: nothing is sent to the internet; the classroom still races |
| D31 | **Flight assist is a choice for every drone**; the default "by the drone" gives position hold on the Education Suite's drones and none on the racing quads | The flight-mode ladder of F3 without changing how the racers fly |
| D32 | **Failures and weather are set by name and value** (`gps_lost`, `prop2`, `baro_drift` …) | One vocabulary for activity files, the teacher's screen and the attempt record |
| D33 | **A photo and a payload drop are events of the flight**, with their place and direction | Scores come from the recording alone (the rule of F1) |
| D34 | **An exercise is written in its own frame** (origin under its start point, +Z the way the drone first faces); a flight is scored in that frame | The same exercise in any open place; a reference flown in one scene shown in another |
| D35 | **One step tracker guides the pilot and grades the recording** | What the screen asked for and what was scored cannot disagree; the grade still comes from the recording alone |
| D36 | **"Steps done" is required and earns nothing below half of the steps** | A flight that did not do the exercise must not pass on its side marks (found by test: a hover passed the pirouette) |
| D37 | **Reference flights are made by flying the exercise's own recipe** through the autopilot or the sticks | A reference of 90 or more for every exercise, re-made whenever the exercise changes |
| D38 | **A failure or a change of weather can follow a step of a guided exercise** instead of the clock | The pilot sets the pace; the emergency comes when the drone is where the drill needs it |
| D39 | **FPV exercises: the autopilot flies the reference along the same course; the exercise holds the flight mode on the pilot** | The autopilot cannot fly acro, and the rubric measures gates, height, path and time, which are the same either way |
| D21 | **Names are compared and sorted in code, not in SQL.** SQLite folds case for Latin letters only and orders by character code, which made "7Б" and "7б" two groups and put every Kazakh letter after the Russian alphabet | Found by the store tests |

## 3. Shared foundations
Built first, because every module uses them.

### F1. Activities and auto-scoring
- **`ActivityDefinition`** (JSON, versioned, hashed): type; title, briefing and instructions as translation keys; location requirements; allowed drones, flight modes and assists; environment (weather, time, failure schedule); objectives; scoring rubric; time limit; randomisation and seed policy.
- **Metric library** used by all rubrics:
  - reach a zone; pass gates in order; hold position, height or heading within a tolerance for a time;
  - path-following error against a reference path; speed band; landing accuracy and touchdown speed;
  - smoothness (stick activity, attitude rates); crashes and contacts; rule breaches (geofence, height limit, no-fly zone);
  - camera on target (target framed for a time, or a photo with enough coverage); area covered; finds reported; payload delivered within a radius; battery left; time.
- **Rubric** → score 0–100, pass or fail, 1–3 stars, a breakdown per criterion, and two short tips taken from the weakest criteria.
- **`AttemptRecord`:** student, activity id and hash, date, duration, score, pass, metric values, events, settings used (drone, input device, assists), flags (teacher changed weather, failure injected, paused), replay file, program source for programming tasks, build hash.
- **Quality gate:** every activity ships with a *reference flight* that must score ≥ 90 and a deliberately *bad flight* that must fail. Both run as automated tests.

### F2. Flight recorder and replay (extends ghosts and the AI Bench replay)
- Records every activity automatically; free flight keeps a rolling "last flight" that can be saved.
- 100 Hz: drone state, **stick inputs**, flight mode, events, environment changes, camera and gimbal. Multi-pilot sessions are recorded by the host.
- File `.grreplay`: compressed, versioned, hashed.
- **Viewer:** timeline; 0.1–4× speed; onboard, chase, free, fixed and top-down cameras; **stick overlay**; graphs of height, speed and throttle; event markers; comparison of two flights (ghost plus time gap); the reference flight for each exercise; video export through ffmpeg.
- Sharing: seat → teacher over the classroom link; file export and import.

### F3. Assisted flight, autopilot, payloads, drone classes
- **Flight-mode ladder for people:** Position (sticks command speed; hands off = hover) → Altitude hold → Angle → Horizon → Acro.
- **Guided modes for programs and missions:** take off, land, hold, go to a point, fly a velocity, orbit, mission (waypoint list), return to launch.
- **Failsafes:** radio loss, low battery (warn → return → land), geofence (height, radius, polygon), loss of positioning (drops to Altitude hold and drifts with the wind).
- **Position sources with realistic errors:** GPS outdoors (noise, dropouts), barometer, compass, and optical flow plus ToF indoors. Each can fail (F4).
- **Payloads:** stabilised camera gimbal with tilt and zoom; photo and video capture; thermal view; spotlight; payload drop or winch with a payload that has mass.
- **New drone classes** (generic designs, our own brand):
  | Class | For | Notes |
  |---|---|---|
  | Camera drone, ~900 g and ~250 g | Training blocks A–C, scenarios | Gimbal camera, GPS |
  | Delivery drone, ~2.5 kg + 1 kg payload | Delivery, flood | Drop mechanism |
  | Edu drone, small | Programming | Front and down cameras, ToF front and down, LED, optional gripper; GPS outdoors, optical flow indoors |
  | Drone balls, 20 cm and 40 cm | Soccer | §6 |
- Autopilot gains are derived from each drone's `QuadProfile`. Tests: step response within spec for every class; hover drift under 0.3 m in calm air; the camera drone holds position in 8 m/s wind.

### F4. Weather and failures
- **Weather:** wind (steady, gusts, direction), rain, fog and visibility, time of day (only the lighting presets a location has), temperature (affects the battery). Changes blend in over a few seconds.
- **Failures:** one motor weak or stopped; damaged propeller (vibration and thrust loss); battery sag or sudden drop; GPS lost or degraded; compass interference; radio link lost; video link noisy or lost; IMU or barometer drift; payload release jammed; gimbal stuck.
- Both can be scripted on an activity's timeline or **injected live by the teacher**, per seat or for everyone. Every change is logged in the attempt record.

### F5. Local data
- One SQLite database file on the teacher's PC (LiteDB as the fallback if SQLite gives trouble in the build). Tables: users, groups, seats, activities, assignments, attempts, replays, tournaments, matches, settings. Versioned migrations.
- Seats keep a local cache so students can practise without the teacher's PC, and sync when it's back.
- Backup, restore, export, and "delete this student's data".
- **Nothing is sent to the internet.** Unity analytics, crash reporting and cloud services are switched off and removed from the build.

### F6. LAN
- **Discovery:** UDP broadcast plus manual IP entry (some school networks block broadcast).
- **Classroom link:** teacher's PC hosts; seats join with a class code. Teacher commands are signed with a key created with the class, and seats remember that key, so one student can't send "teacher" commands to another.
- **Game sessions:** up to 8 racers or 10 soccer players, plus referee and spectators. State is sent at up to 120 Hz on LAN. Clocks are synchronised to the host.
- The installer opens the needed Windows Firewall ports; the ports are documented for school IT.
- Target: 1 teacher + 30 seats ❓.

### F7. Three languages
- Unity Localization string tables for `kk`, `ru`, `en`; language switch at runtime, saved per user.
- Fonts that contain every Kazakh Cyrillic letter (ә ғ қ ң ө ұ ү һ і).
- All content is translated too: exercises, scenarios, tasks, block names, API help, reports.
- Automated checks: no hard-coded user-facing strings; every key exists in all three languages; screenshots in the longest language show no clipped text.
- Kazakh text is marked "needs native review" until a named reviewer signs it off ❓.

## 4. Training (TZ 5)
**Training hub:** exercise list with progress, free flight, replay library.

**30 exercises in 5 blocks.** Each has a briefing, a short demo (the reference flight), on-screen guidance (target boxes, path ribbon), a score with breakdown, tips, and a replay comparison with the reference. Blocks unlock in order by default; the teacher can unlock everything.

| # | Exercise | Mode / view | Mainly scored on |
|---|---|---|---|
| **A** | **First flights** (camera drone) | Position; line-of-sight or chase | |
| 1 | Controls check: move each stick as asked (on the ground) | — | Correct stick and direction |
| 2 | Take off, hover 20 s in a 2 m box at 2 m, land on the pad | Position | Position error, landing |
| 3 | Height steps 1 → 3 → 5 → 2 m, hold each 5 s | Position | Height error |
| 4 | Pirouette 360° left and right inside a 1.5 m box | Position | Drift while turning |
| 5 | Out and back: 15 m forward, stop in the box, return | Position | Stopping accuracy |
| 6 | Square, tail towards the pilot, stop at each corner | Position | Corner accuracy |
| **B** | **Orientation and precision** | Position → Angle; line-of-sight | |
| 7 | Square, nose forward (turn at each corner) | Position | Corners, heading |
| 8 | Circle a pole with the nose on the pole, both directions | Position | Radius, heading on target |
| 9 | Figure-eight around two poles | Position | Path error, height |
| 10 | Nose-in hover and landing (controls feel reversed) | Position | Position error |
| 11 | Landing circuit: 4 pads of shrinking size | Position | Landing accuracy and speed |
| 12 | Hover and square with position hold off, light wind | Angle | Drift, smoothness |
| **C** | **Camera-drone proficiency** (modelled on NIST's public open-lane tests) | Position; camera view | |
| 13 | Position: align the camera with bucket targets at two heights | | Alignments, time |
| 14 | Traverse: fly straight and level between targets | | Alignments, height |
| 15 | Orbit: circle the lane keeping targets in view | | Alignments |
| 16 | Spiral: climb and descend around the targets | | Alignments |
| 17 | Recon: hover over objects and read the targets inside | | Targets read |
| 18 | Emergency drills: positioning lost; low-battery return; landing with a weak motor | Mixed | Correct reaction, safe landing |
| **D** | **FPV basics** | FPV; Angle → Horizon → Acro | |
| 19 | Hover and fly a straight line through 3 gates | Angle | Gates, height |
| 20 | Coordinated turns: oval around 2 flags at constant height | Angle | Path, height |
| 21 | Slalom through 6 flags | Angle | Flags, time |
| 22 | Low flight: stay between 0.5 and 1.5 m along a course | Horizon | Height band |
| 23 | Acro hover: hold a 3 m box for 20 s | Acro | Position error |
| 24 | Acro circuit: 2 laps of a simple gate track | Acro | Completion, lap consistency |
| **E** | **Racing and freestyle technique** | FPV; Acro | |
| 25 | Racing line: hairpin and chicane against bronze/silver/gold times | Acro | Time, gates |
| 26 | Split-S through a dive gate | Acro | Gate, height lost |
| 27 | Power loop over a gate | Acro | Gate, exit height |
| 28 | Roll and flip, keeping height and heading | Acro | Height lost, heading |
| 29 | Proximity: 5 gaps of shrinking size | Acro | Gaps, contacts |
| 30 | Check ride: a mixed course of everything, timed | Acro | Total; printable certificate |

**Free flight:** any location, any drone, flight mode, assists, weather, time, optional failures.

**Record and replay:** F2.

**Done when**
- All 30 exercises pass their reference-flight and bad-flight tests.
- Scores agree with a human instructor: an instructor ranks 10 recorded flights of 5 exercises; the automatic ranking matches closely (rank correlation ≥ 0.8) ❓.
- Every exercise can be flown with a radio, a gamepad and a keyboard, in all three languages.
- A flight can be recorded, replayed with stick overlay, compared with the reference and exported.

## 5. Racing (TZ 6)
**10 tracks** (adapt to what the project already has; reach 10 with a spread of difficulty):
| # | Track | Location | Class | Level |
|---|---|---|---|---|
| 1 | Training Oval | Field | 5" | Beginner |
| 2 | Field Sprint | Field | 5" | Beginner |
| 3 | Hall Circuit | Hall | Whoop | Beginner |
| 4 | Hangar Loop | Hangar | 3" / 5" | Intermediate |
| 5 | Parking Maze | Parking | Whoop / 3" | Intermediate |
| 6 | Serpent Arena | Stadium | 5" | Intermediate |
| 7 | Expo Classic | `Track_expo_v1` | 5" | Intermediate |
| 8 | Industrial Run | Industrial | 5" | Advanced |
| 9 | Expo Vertical | `Track_expo` | 5" | Advanced |
| 10 | Steppe Canyon | Steppe range terrain | 5" / 7" | Advanced |

Each track has bronze/silver/gold times, a baked bot racing line, a ghost, a map and a thumbnail. Tracks for 5" quads are closed circuits of at least 250 m, matching FAI drone racing.

**Track editor** (for teachers and students): place gates, flags and obstacles from the kit on any location; set the checkpoint order (the route line is generated from it); validation; test fly; bake the bot line; save, share by file or push to seats over the classroom link.

**LAN race, up to 8 pilots:**
- Lobby with seat colours; identical drones for everyone ("spec") or free builds within limits ❓; laps; collisions on or off.
- **Start as in FAI racing:** "arm" call, then the start tone after a random 2–4 s with no countdown. Jump start = disqualified from that race.
- A missed gate invalidates the lap unless the pilot goes back at once.
- Race director and spectator views; a big-screen mode with automatic camera switching and a timing tower.

**Timing:** lap times; a split at every gate; best lap; best 3 consecutive laps; time for the full race; holeshot; live gaps and positions; DNF. Times are measured in simulator time on each pilot's PC to 1 ms and confirmed by the host. Results export to CSV, XLSX and PDF.

**Tournament:**
- **Qualification:** at least 3 rounds; ranking by fastest 3-lap time or by the average of the best laps (both FAI options).
- **Elimination bracket:** races of 4 with the top 2 advancing (FAI preset), or races of up to 8 with the top half advancing; optional double elimination (losers get a second-chance bracket).
- **Final:** one race, or successive finals until someone wins two.
- **Small classes (under 16):** a points format over a fixed number of rounds.
- Bracket screen, printable bracket and results (PDF), manual corrections (re-run, penalty, disqualify), and resume after a power cut (state saved after every race).

**Done when**
- 8 PCs on a LAN finish a 3-lap race; every PC shows the same results; lap times agree to 1 ms.
- A 16-pilot tournament runs from qualification to the final with correct advancement, including a re-run and a disqualification.
- A teacher builds a new track in the editor, pushes it to the seats and races on it.

## 6. Drone soccer (TZ 7)
**Rules file.** The values below are my reading of the **FAI Sporting Code, Volume F9 Drone Sports, 2026 edition v1.0** (class F9A). FAI lists a **version 2 effective 1 June 2026**. The agent must check every value against the official PDF in `Docs/reference/fai/` and note the edition in the rules file ❓.

| | F9A-A ("40 cm") | F9A-B ("20 cm") |
|---|---|---|
| Ball (protective sphere) | 40 cm (+2 cm) | 20 cm (+2 cm) |
| Max weight | 1.2 kg | 300 g |
| Battery / propellers | up to 6S / up to 6" | up to 4S / up to 3" |
| Flying zone (recommended) | 14 × 7 m, cage 5 m high | 6 × 3 m, cage 3 m high |
| Goal ring (recommended) | outer 100 cm, inner 60 cm, up to 20 cm thick, 1.5 m inside the baseline | outer 70 cm, inner 40 cm, up to 10 cm thick, 1 m inside the baseline |
| Ring height | read from the official PDF | read from the official PDF |

Common to both:
- **Teams:** 3 to 5 active players, the same number on both sides. One is the **Striker**, visibly marked; **only the Striker scores**.
- **Match:** 3 sets of 3 minutes, a 2–3 minute break between sets; a set goes to the team with more goals; the match goes to the team that wins 2 sets; a tied match is settled by a method announced beforehand (coin toss, sudden-death overtime or a penalty shoot-out).
- **Start:** balls on the start areas; "Arm your quads"; the signal sounds 3–5 s later. Lifting off early gives the other team a penalty shot.
- **Goal:** the Striker's whole ball passes through the opponent's ring towards the opponent's baseline. After a goal, every active player of the scoring team must return to their own half before the team can score again.
- **Penalty shot:** the Striker against one defender, 10 seconds. Given for an early start, for not returning after a goal, or for a non-Striker parking in its own ring to defend.
- **Cards:** warning; yellow (player out for the rest of the set); red (player out for the rest of the match, team plays one short).
- A ball that is down and can't fly is out for the rest of the set. Balls, batteries and players change only between sets. The Striker can be re-assigned once per set.
- **Officials:** a match referee and two scoring referees, one on each side of the flying zone.

**Physics**
- Drone-ball profiles for both subclasses, tuned to typical real balls ❓. The cage adds drag and inertia.
- Sphere collisions with bounce, friction and spin: ball–ball, ball–net, ball–floor, ball–ring. A ball can roll on the floor and take off again only if it rests on its flat base.

**Arenas:** an F9A-A arena and an F9A-B arena inside the Hall, each with net cage, LED goal rings, centre line, start areas and pilot areas. An outdoor variant is optional.

**LAN match, up to 5 v 5**
- Default: each PC simulates its own ball; other balls are moving colliders, and each PC computes its own bounce from a two-body impact. The host runs the clock and validates goals and fouls.
- The ED0 spike compares this with "the host simulates every ball" on a real LAN and picks one by stick latency and how well the PCs agree.
- Bots can fill empty places and allow solo practice (optional ❓).

**Referee mode**
- Three levels: **Auto** (the simulator referees; for class practice), **Assisted** (the simulator proposes, the referee confirms or overrules), **Manual**.
- The simulator detects: goals, early starts, the "return to own half" state, a defender parked in its own ring, balls that are down.
- The referee's tools: start, pause and end a set; award or cancel a goal; penalty shot; warning, yellow and red cards; re-assign the Striker; views beside each goal and overhead; instant replay of the last 15 s; scoreboard.
- Match protocol as PDF: sets, goal timeline, penalties, cards.

**Done when**
- Every value in the rules file is checked against the official FAI PDF, with the edition written down.
- 10 players and a referee on a LAN play a full 3-set match in both subclasses.
- The automatic goal decision matches a frame-by-frame replay review in 30 of 30 test passes, including near misses, wrong-direction passes, non-Striker passes and "not yet returned" cases.
- Contacts between balls look the same on both PCs involved (positions agree within 10 cm after 0.2 s).

## 7. Scenarios (TZ 8)
**How "works on any location" is achieved**
- A **Location Analyzer** (Editor tool) builds a `LocationProfile` for every location: bounds, height map, surface types (ground, roof, road, water, vegetation), open flat areas, tall structures, road paths, spawn points, and a top-down map image.
- A **scenario template** holds placement rules, not fixed positions. Example: "5 people on the ground inside the search area, at least 30 m apart, 1 in 5 partly hidden". The rules scale to the size of the location.
- Template + location profile + seed = one concrete, repeatable scenario. A validator confirms every objective is reachable.

**10 ready scenarios** (the TZ's 7 kinds)
| # | Scenario | Kind | Task | Scored on |
|---|---|---|---|---|
| 1 | Missing hikers | Search | Find and mark N people in a search area, daylight | Finds, false reports, time |
| 2 | Night search | Search | The same at night with the thermal camera | Finds, time |
| 3 | Structure check | Inspection | Photograph marked defects on a tower, roof or façade from the required distance | Photos that meet framing rules |
| 4 | Line patrol | Inspection | Follow a line of poles at a set distance and find the faults | Standoff error, faults found |
| 5 | Fire recon | Fire | Locate the fire seats through smoke, trace the edge, keep a safe distance | Seats found, perimeter covered, safety breaches |
| 6 | Flood rescue | Flood | Water rises; find stranded people and drop rescue kits to them | People found, drops within radius |
| 7 | Perimeter patrol | Patrol | Fly the route on schedule; photograph intruders in the closed zone | Checkpoints on time, intruders reported |
| 8 | Follow shot | Filming | Keep a moving subject framed; fly an orbit shot | Time in frame, smoothness |
| 9 | Area survey | Filming | Fly a photo grid over an area with the required overlap | Area covered, height kept |
| 10 | Parcel run | Delivery | Carry a parcel around no-fly zones and land or drop it on the mark | Accuracy, time, battery left |

**Content rules:** people are simple stylised figures who wave or walk; nothing graphic; the framing is always rescue and help.

**Pilot tools in scenarios:** map with zones, mark-a-find, photo, thermal toggle, payload release, checklist.

**Teacher's scenario editor**
- Pick a location → pick a template or start blank → place and edit people, vehicles, fires, water level, markers, zones (search area, no-fly, geofence), pads, inspection points, delivery points, route checkpoints.
- Set weather, time, a failure timeline, the drone and payload, objectives and scoring (from the metric library), the time limit and the instructions (written in one language; the other two fall back to it).
- Test fly, save, export or import a `.grscenario` file, assign.

**Done when**
- Automated matrix: each of the 10 scenarios × every shipped location × 3 seeds builds, passes validation, and is completed by the built-in autopilot with a passing score.
- A teacher following the docs makes a new scenario in 15 minutes or less and it runs on a second PC.

## 8. Programming (TZ 9)
**Python API** (`from gaterush import Drone`): simple and blocking by default.
```python
from gaterush import Drone

drone = Drone()                 # connects to the simulator on this PC
drone.takeoff(2.0)
drone.go_to(x=5, y=0, z=2)      # metres from the start point
drone.rotate(90)
if drone.front_range.read() < 1.5:
    drone.hover(2)
photo = drone.camera.photo()    # numpy image
drone.land()
```
- **Movement:** take off, land, forward/back/left/right/up/down, rotate, go to, set speed, hover.
- **Telemetry:** position (when the task allows it), height, heading, speed, battery.
- **Camera and sensors:** front and down camera frames, range finders, LiDAR, IMU, GPS; helpers for colour blobs, markers and line following.
- **Reactive control:** non-blocking velocity commands for control loops.
- **Extras:** LED colour, gripper or drop, timers.
- It shares its transport with the AI Bench SDK (`gaterush_bench`), which stays the low-level RC-channel API. Each task says which parts of the API are allowed, and the simulator enforces that.

**Studio** (the editor students work in)
- A local web page served by the simulator on `127.0.0.1` with a session token, opened next to the simulator window in an app-style browser window (Edge on Windows; the default browser elsewhere). All files are bundled; no internet.
- **Blocks** tab (Blockly) with a drone block set; blocks produce Python, shown read-only. **Python** tab with a code editor. Console, task panel with the checks and hints, and API help in three languages.
- A **bundled Python** (with numpy, OpenCV, pymavlink and our package pre-installed) runs the program as a normal process with Run and Stop. This is not a security sandbox; grades come from the simulator, not from the student's code.
- Programs are saved locally and attached to the attempt, so the teacher can open them.

**Route on a map**
- A mission planner screen on our own top-down map of the location (works offline): add and drag waypoints; height, speed, hold time and action (photo, drop, land) per waypoint; survey-grid generator; no-fly zones and geofence shown; upload and fly; live track.
- Missions use the same format as MAVLink missions and import/export QGroundControl `.plan` files.

**MAVLink** (decision D8)
- UDP 14550 so QGroundControl connects by itself. A unique system id per seat.
- Every location has a geographic origin; `Track_expo` uses the real coordinates.
- Subset served: heartbeat and status; attitude, position, GPS, battery, home; the parameter protocol (a small parameter set); the mission protocol; commands (arm, disarm, take off, land, return, set mode, reposition, message rates, payload); position and velocity targets for off-board control.
- **QGroundControl needs internet for its map pictures**; the docs explain pre-caching maps or flying over a blank background.
- **Acceptance checklist with the current stable QGroundControl** (write down the version) ❓:
  1. Connects by itself within 5 s, no error pop-ups; the vehicle is ready only after parameters load.
  2. Fly view shows attitude, height, speed, battery, GPS and home; the drone moves correctly on the map.
  3. Arm, take off, land, return and pause work from the Fly view.
  4. "Go to location" on the map works.
  5. A 5-waypoint mission with take-off and landing is planned, uploaded, flown and downloaded back unchanged.
  6. The flight-mode list shows and switches modes.
  7. The parameter page loads; changing one parameter (for example return height) takes effect.
  8. Closing the simulator shows "communication lost"; restarting reconnects.
- The same sequences run in CI as `pymavlink` tests, so regressions are caught without opening QGroundControl.
- **If the fallback is chosen:** bundle the unmodified ArduPilot simulator as a separate program and feed it our physics through its JSON interface over UDP. ArduPilot is GPLv3, so ship its licence and a source offer and have that checked.

**10 auto-checked tasks.** Positions and distances are randomised by seed, so hard-coded answers fail. Each task gives per-check feedback.
| # | Task | Tools | Checks |
|---|---|---|---|
| 1 | First flight: up to 2 m, hover 5 s, land | Blocks | Height held; lands within 0.5 m of the pad |
| 2 | Square with a 5 m side, return and land | Blocks | 4 corners within 0.5 m |
| 3 | Polygon with N sides using a loop and a function | Blocks or Python | Corners; uses a loop |
| 4 | Climb until the range finder reads the target; report the battery | Python | Final height; value printed |
| 5 | Plan a route through 5 checkpoints around a no-fly zone | Map | Checkpoints; no breach |
| 6 | Fly forward and stop 1.5 m from a wall at an unknown distance | Python + range finder | Stop distance |
| 7 | Find the red pad among three and land on it | Python + camera | Correct pad; accuracy |
| 8 | Treasure hunt: read markers that give the next heading | Python + camera | Markers visited in order |
| 9 | MAVLink: connect, arm, take off, fly a triangle, land | pymavlink | Messages used; path |
| 10 | Mini-project: lawn-mower search, find the orange marker, drop a kit within 2 m, return with ≥ 20% battery | Any | Find; drop; battery |

**Done when**
- The QGroundControl checklist passes on Windows (and Linux if required).
- Reference solutions for all 10 tasks pass on 5 seeds; a hard-coded "cheat" solution fails on a different seed.
- A block program and its generated Python fly the same path.
- Everything runs with the network cable unplugged.

## 9. Teacher (TZ 10)
**Teacher console** (a role in the same app, protected by a teacher password).
- **Groups:** create groups; import a list from CSV or XLSX; students sign in by picking their name and a PIN ❓, or seats run under seat names with no personal data; a seat map that matches the room.
- **Assigning:** any activity to a group or to chosen students, with settings (attempt limit, assists allowed, weather). Lesson plans (an ordered list of activities). "Start on all seats now". Lock mode keeps a student inside the assigned activity (inside our app only).
- **Watching the seats:**
  - a grid of seats with status (idle, flying, crashed, finished, disconnected), a small live picture at 2–5 frames per second, key numbers and current score;
  - alerts (crash, stuck, off-task); a "raise hand" button for students;
  - click a seat to watch that flight live with a free camera or the student's view, with the stick overlay; send a short message or hint.
  - Only the simulator's own picture is ever captured, never the desktop.
- **Live control**, per seat or for all: pause, resume, restart; change the weather; inject a failure; "attention" mode that blanks the seats.
- **Journal:** students × activities with best score, attempts, time and pass; open any attempt, its replay and (for programming) its code; manual grade and comment; attendance per lesson.
- **Reports:** student progress (PDF); group gradebook (XLSX and CSV); activity statistics; race results, tournament bracket and soccer match protocol (PDF); lesson summary. Header with school name and logo. All in three languages, with Kazakh letters rendered correctly.
- Works with no internet. Data lives on the teacher's PC, with backup and restore.

**Done when**
- 1 teacher + 30 seats (real PCs plus simulated seats to reach 30): an assignment starts on every seat within 5 s; the grid updates at ≥ 2 frames per second; a weather change or failure takes effect within 0.5 s; streaming costs under 5% CPU per seat.
- After each attempt the journal shows it within 3 s; after a seat disconnects and returns, nothing is lost.
- XLSX opens correctly in Excel and LibreOffice; PDF shows Kazakh, Russian and English text correctly.

## 10. Platform (TZ 11)
**Installer**
- Windows installer with a silent mode for school IT; components (student seat, teacher console, programming tools); firewall rules; bundled ffmpeg and Python; clean uninstall that asks whether to keep data.
- Decide: EXE or MSI ❓; code-signing certificate ❓ (without one, Windows shows a warning on every install).
- Offline updates from an update file; pushing updates from the teacher console is optional.
- Linux package only if required ❓.

**Offline licence per workstation**
- The app creates a **request file** with a fingerprint of the PC's hardware (several identifiers, hashed; the licence still works if one changes; identifiers that survive disk cloning are preferred, because school PCs are often cloned).
- The vendor's private **licence tool** returns a **signed licence file**: customer, seat fingerprint, modules enabled, valid-until or perpetual, updates-until.
- The app checks the signature with a public key built into it. No internet at any step. A whole class can be activated in one go: the teacher console collects the requests into one bundle and distributes the returned licences.
- Also: clock-rollback detection; a grace period after a hardware change ❓; a trial mode ❓.
- The private key never enters the repo or any build.
- Honest limit: an offline licence stops casual copying; it can't stop a determined cracker.

**Local data:** F5. A final check with a network monitor confirms the app makes no internet connections during a full lesson.

**Languages:** F7.

**School PCs:** many classrooms have integrated graphics. Add an **"iGPU" quality preset** (720p, lowest textures, no shadows or ambient occlusion) and test the classroom-critical content on it: training, hall and parking, soccer, programming ❓.

**Done when**
- On a clean Windows 10/11 PC with no internet: install, activate from a licence file, run every module.
- The same licence file is refused on another PC; an edited licence is refused; turning the clock back is detected.
- Every screen is complete in all three languages.
- Uninstall leaves nothing behind except data the user chose to keep.

## 11. Milestones
| # | Milestone | TZ | Done when |
|---|---|---|---|
| ED0 | Orientation and spikes | — | Project state written up; spike results for MAVLink/QGroundControl, LAN collisions, Studio, reports with Kazakh text, licence crypto, database, iGPU check; §13 questions sent |
| ED1 | Foundations I | 11 (part) | Three-language framework with the existing UI converted; local database; user profiles; Activity model and scoring engine; recorder and replay viewer; a basic installer for LAN tests |
| ED2 | Foundations II | — | Assisted modes and autopilot; camera, delivery and edu drones; payloads; weather; failures |
| ED3 | Training | 5 | §4 "Done when" |
| ED4 | Teacher console | 10 | §9 "Done when" (with Training as the first assignable content) |
| ED5 | Racing | 6 | §5 "Done when" |
| ED6 | Scenarios and editor | 8 | §7 "Done when" |
| ED7 | Programming | 9 | §8 "Done when" |
| ED8 | Drone soccer | 7 | §6 "Done when" |
| ED9 | Platform | 11 | §10 "Done when" |
| ED10 | Classroom pilot | all | A real lesson with one teacher and a full class on real school PCs; fix list closed |

After ED2, the modules ED3, ED6 and ED7 are independent and can be built in parallel. ED5 and ED8 share the session netcode. ED4 comes right after ED3 so the core loop (teacher assigns → students fly → journal → report) exists early.

The "Done when" lists in §4–§10 are also the **proposed acceptance tests** for the customer, since TZ items 5–11 don't state any.

## 12. Reuse of earlier work
| Needed here | Comes from |
|---|---|
| Flight physics, controller, radios | `PLAN.md`, `FLIGHT_PHYSICS.md` |
| Gates, obstacles, spline tracks, garage builds | `TRACK_GARAGE_PLAN.md` |
| Bots (opponents, the autopilot that validates scenarios and tracks) | `PHASE3_PLAN.md` |
| LAN sessions | `PHASE3_PLAN.md` |
| API server, Python transport, sensors, camera pipeline | `AIBENCH_PLAN.md` |
| Recorder, replay viewer, video export | `AIBENCH_PLAN.md` |
| Hall and parking venues, docs site | `AIBENCH_PLAN.md` |
| Locations and visuals | `VISUALS_PLAN.md`, `TRACK_EXPO_PLAN.md` |

If one of these isn't built yet, build the shared piece once, in the form both plans need.

## 13. Вопросы заказчику (готово к отправке)
1. **Пункты 1–4 ТЗ.** Пришлите их, чтобы проверить, что все требования учтены.
2. **Сроки и приоритеты.** Что из пунктов 5–11 нужно первым и к каким датам? Как это соотносится со сроками режима AI Bench?
3. **Класс.** Возраст учеников; сколько рабочих мест (до 30?); характеристики компьютеров (процессор, видеокарта или встроенная графика, память); Windows 10 или 11, нужен ли Linux; сеть проводная или Wi-Fi; чем управляют (пульты, геймпады, клавиатура).
4. **Обучение.** Есть ли утвержденная программа или методика, под которую нужно подогнать 30 упражнений? Какой порог зачета?
5. **Гонки.** Формат турнира: по FAI (заезды по 4, проходят двое) или заезды до 8 пилотов? Одинаковые дроны у всех или свои сборки?
6. **Дрон-футбол.** Какая редакция регламента FAI (на сайте FAI есть версия 2 от 1 июня 2026 года)? Нужны ли боты? Нужен ли турнир (группы и плей-офф)? Параметры реальных дрон-шаров (масса, моторы, батарея), чтобы настроить полет.
7. **Сценарии.** Какие дроны и полезная нагрузка нужны (камера на подвесе, тепловизор, сброс груза)? Есть ли обязательные локации, нужны ли реальные местности Казахстана?
8. **Программирование.** Какими реальными дронами пользуются школы (чтобы сделать Python API похожим)? Какая версия QGroundControl и что именно должно работать при проверке (подключение, телеметрия, взлет и посадка, миссия)? Нужны ли MAVSDK или DroneKit? Нужно ли видео в QGroundControl?
9. **Преподаватель.** Достаточно ли отчетов XLSX/CSV и PDF, или нужна связь с электронным журналом? Есть ли шаблоны отчетов и логотип? Нужно ли запрещать ученику выходить из задания?
10. **Установщик.** EXE или MSI (для массовой установки)? Будет ли сертификат для подписи кода?
11. **Лицензия.** Бессрочная или на срок? Что делать при замене комплектующих? Нужен ли пробный режим? Продаются ли модули отдельно? Отдельная ли лицензия у места преподавателя? Кто выпускает лицензии?
12. **Данные учеников.** Храним фамилии и имена или только псевдонимы и номера мест? Есть ли требования к хранению и удалению?
13. **Казахский язык.** Кто вычитывает перевод интерфейса, заданий и отчетов?

## 14. Risks
| Risk | Mitigation |
|---|---|
| Scope: seven modules | Foundations first; each milestone ends with something usable; ask for priorities (question 2) |
| Conflict with AI Bench dates | AI Bench B1–B2 keep priority; shared pieces built once |
| QGroundControl refuses a home-made MAVLink vehicle | ED0 spike with a time box; fallback to the real ArduPilot simulator; one checklist for both |
| Soccer contacts differ between PCs | ED0 spike on a real LAN; host is the only authority for goals and fouls; pick the model by measurement |
| FAI numbers wrong or outdated | Rules as data; every value checked against the official PDF; edition recorded |
| "Any location" produces broken scenarios | Location profiles, placement validation, the scenario × location × seed test matrix |
| Auto-scores feel unfair | Reference and bad flights per activity; comparison with a human instructor; teacher can override a grade |
| School PCs too weak | iGPU preset; classroom-critical content tested on it; ask for the spec (question 3) |
| School networks block discovery or have Wi-Fi isolation | Manual IP entry; documented ports; firewall rules in the installer; test on the real network in ED10 |
| Kazakh translation quality | Named native reviewer; glossary of drone terms agreed first |
| Licence lock-outs after repairs or re-imaging | Tolerant fingerprint; grace period; a simple re-issue process |
| Third-party licences (Blockly, Python packages, PDF and XLSX libraries, ArduPilot if used) | Every dependency and its licence recorded; nothing copyleft linked into the app |

## 15. Status log
### 21 November 2026: ED0 done; engine-free parts of ED1 and ED2; the flight layer flies in a scene
**ED0.** Project state written (`Docs/project_state.md`), spike reports in `Docs/spikes/`, ED1 task list in
`Docs/edu/ED1_tasks.md`, decisions D13 to D21.

| Spike | Result |
|---|---|
| MAVLink endpoint | Works. 25 of 25 automated checks, also inside a player build and on the real flight model through the autopilot. **QGroundControl itself not tried** (not installed) |
| Soccer collisions over LAN | Numerical model only: own-ball simulation needs prediction and "one bounce per contact"; host simulation costs 23 to 82 ms of stick delay |
| Studio | Works in a player build: local page behind a token, Edge app window, Python program as a process, drone moved. Without Blockly and without a bundled Python (both are downloads) |
| Reports | XLSX and PDF with Kazakh text written by a player build and read back correctly. Not opened in Excel or LibreOffice (neither is installed) |
| Licence | RSA signature check works in a player build; ECDSA does not exist in Unity's runtime. 7 hardware identifiers readable |
| Database | SQLite from Windows itself works in a player build |
| Weak PCs | **Not done:** needs a Windows graphics setting changed by the owner, or a school PC |
| FAI rules | **Not read:** fai.org refuses automated reading and offers the PDF as a download only |

**Built and tested** (422 EditMode tests pass, 67 of them new; every earlier test still passes):

| Piece | Where | State |
|---|---|---|
| Class database with numbered upgrades; students, groups, PINs, attempts, grades, backup, "delete this student's data" | `Scripts/Edu/Core/EduDatabase.cs`, `EduStore.cs` | Logic done; no screens yet |
| Activity files, 15 metrics, rubric with pass mark, stars and tips; one sample activity | `Scoring.cs`, `StreamingAssets/Edu/Activities` | Logic done; camera, area, finds and payload metrics wait for the things they measure |
| Flight recording file and an even-rate recorder | `FlightRecording.cs`, `FlightRecorderCore.cs`, `Runtime/EduRecorder.cs` | Records in a scene; **no viewer yet** |
| XLSX and PDF writers | `XlsxWriter.cs`, `PdfWriter.cs` | Writers done; no report layouts yet |
| Assisted flight (Position, Altitude hold), autopilot (take off, land, hold, go to, velocity, orbit, mission, return), failsafes (radio, battery, geofence, positioning) | `Scripts/Flight/FlightAssist.cs`, `Autopilot.cs` | Flown on the real flight model in tests and live in `Track_Field` |
| Position sources with errors; weather (wind, gusts, blending, cold battery); motor loss | `PositionSources.cs`, `Weather.cs`, `QuadState.MotorLoss` | Logic done; rain and fog have no pictures yet |
| The layer on a drone in a scene | `Runtime/EduFlight.cs`, `QuadVehicle.Director` | Works; nothing in the menus switches it on yet |
| MAVLink endpoint joined to the autopilot | `Mavlink.cs`, `MavDrone.cs` | Works outside Unity; not yet wired into a scene |

**Live check in `Track_Field` (3-inch quad, 127 frames a second):** take-off to 4.01 m; flight to a point 14 m away
ended within 1 cm, facing the asked heading; 6 m/s wind with gusts moved it 4 cm (9° of lean); return to launch climbed
to 15 m, landed 20 cm from the start in that wind and switched the motors off; the 99 s recording is 277 KB.

**Measured limits, not bugs:** a 32 g whoop cannot hold position in 8 m/s wind (its test uses 4 m/s) and tops out near
6 m/s; a quadcopter with one motor stopped falls, as a real one does.

**Not started of ED1:** replay viewer, the remaining hard-coded strings and the font atlas, user-profile screens, the
school build without cloud packages, the installer. **Not started of ED2:** the three new drone classes, gimbal and
payloads, rain and fog pictures, the remaining failures (GPS and compass faults exist; video, IMU, gimbal do not).

**Later the same day: the loop "fly, grade, store" works end to end.** `ActivityAttempt` grades a recording against
an activity, writes the recording next to the database (named by activity and content) and adds the attempt to the
journal; `Regrade` grades a stored attempt again. Live in `Track_Field` the autopilot flew the sample exercise
"take off, hover at 2 m, land": **99.4 of 100, three stars**, one journal row, a 44 KB recording. This is the plan's
"reference flight must score 90 or more", flown for real instead of made up from numbers.

**FAI rules read (21 November, evening).** The owner saved the PDF; all F9A values are in
`Docs/reference/fai/f9a_values.md` with paragraph numbers, and the nine differences from the table in §6 are listed
there (decision D23). The one that touches a design already built: assisted modes must be switched off for drone balls.

### 21 November 2026, night: the replay viewer (ED1 tasks 4.4 and 4.5)
**What exists.** `EduReplayViewer.Open(drone, flight, reference)` shows a `.grreplay` on a timeline: play and pause,
0.1 to 4 times the flown speed, steps of 0.05 s and 1 s, five cameras (behind, from the drone, fixed, from above,
free), the sticks as a mode 2 radio holds them, graphs of height, speed and throttle with a cursor, coloured markers
on the timeline for what happened, and the flight's path as a line in the air. With a second flight it also shows that
flight as a light-blue drone with its own path, and the time between the two ("0.95 s behind the reference"). The
viewer flies copies of the drone's model, stops the game's clock and hides the game's screens, so nothing in the scene
reacts to a flight that is only watched; closing gives everything back. Texts are in three languages
(`Localization/Text/edu.tsv`, Kazakh needs a native review).

**Decisions.**
- **D24. Sticks of a flight nobody flew.** With a person at the sticks the recording holds the sticks as that person
  held them. With nobody there (a mission, later a student's program) it holds the commands the flight controller was
  given, and the note `sticks` says which (`pilot` or `commands`). Otherwise the stick display, the throttle graph and
  the smoothness measure would be empty for every programmed flight.
- **D25. The time between two flights** is found along the path: for each moment of one flight, when the other was at
  the nearest place, searching forward only (so laps and crossings are not confused). More than 5 m apart there is no
  figure. For a hover the figure means little; the hub should show it only for activities with a route.
- **D26. Reference flights are files.** `Edu/Activities/<id>.grreplay` beside the activity's definition, flown in the
  simulator, and a test checks that every shipped activity has one that scores 90 or more. The sample exercise's
  reference was flown by the autopilot: 98.4 of 100.

**Checked.** 9 new tests (clock, blending, events, graph columns, time between flights on a line and on laps, the
reference gate); 432 of 432 pass. Live in `Track_Field`: two autopilot flights of the same route at 5 and 3.5 m/s,
opened one against the other; the screen was looked at in the five views and the three languages; closing restores
the clock, the cameras, the game's screens and the drone.

**Not done of the viewer.**
- **Video export (task 4.6):** there is no ffmpeg on this PC, and getting one is a download the owner has to allow.
- **Opening it from the game:** there is no button yet. It comes with the training hub (ED3: replay library, "watch
  my last flight", "compare with the reference" after an exercise).
- **Propellers do not turn** in the replay (the models are still copies).
- **The picture on a weak PC** was not measured.

### 21 November 2026, later: a replay can be saved as a video (ED1 task 4.6)
**What exists.** The viewer has a "Video" button (key V). It plays the flight from the start by itself and writes an
MP4 of what the viewer shows: the chosen camera, the sticks, the graphs, the reference flight. 30 pictures for each
second of the flight, at the size of the window; Esc stops it and removes the half-written file. Files go to
`edu/videos` in the user's data folder. `EduVideo` finds ffmpeg (the variable `USHQYR_FFMPEG`, the copy shipped with
the program, the developer's copy in `ThirdParty/edu/ffmpeg`, the PATH), tries which H.264 encoder really runs on
this PC and uses the first that does.

**ffmpeg.** The owner allowed the download. `Tools/Edu/fetch_third_party.py ffmpeg` fetches a pinned **LGPL** build
(8.1.3, no x264), checks its hash and keeps `ffmpeg.exe` and the licence text, 134 MB, outside the repository.

**Checked.** In the editor a 34 s comparison of two flights was exported at 1920 × 1080: a 29 MB H.264 file, 34.4 s,
30 pictures a second; ffmpeg decodes all of it without an error, and the picture at 6 s shows the viewer at 0:06.00,
the right way up. The first try was upside down; the cause (a texture's rows run bottom to top, and Direct3D's screen
grab is already turned over) is written in the code.

**Not checked or not decided.**
- **In a built program:** only the editor was tried. The picture's orientation on Linux (OpenGL or Vulkan) follows
  from the same reasoning but was not seen.
- **Another video player:** the file was decoded by ffmpeg only, not opened in Windows' own player.
- **Shipping:** ffmpeg is in no build yet. 134 MB more in the school installer is the owner's call, and so is the
  licence question in `THIRD_PARTY_NOTICES.md` (LGPL text and source offer; H.264 patents).
- **AI Bench videos:** its recorder asks for the x264 encoder, which the LGPL build lacks. Nothing there was changed.

### 21 to 22 November 2026: languages, the baked font, text that fits, "Who is flying?" (ED1 tasks 1.1 to 1.4, 2.3)
**Languages (1.1, 1.2).** The estimate of "60 to 120 hard-coded strings" was too high: the calibration wizard, the
display modes and the catalogue already went through the tables by way of variables. Really untranslated were the
pause overlay for a controller change, a unit, and nothing else. Two tests now guard this: one scans every place that
puts text on the screen and refuses a literal that is not a key of the sheets (names such as "SMAA" are listed by
hand; the two FPV-style displays stay English on purpose, as on real goggles), the other checks the texts kept in data
(parts, quads, tracks, wizard steps, display modes, pause reasons). The Education Suite's texts are in
`Localization/Text/edu.tsv`.

**The baked font (1.3).** The main font asset held Latin only, so every Russian and Kazakh letter was drawn while the
program ran, into an asset that was rewritten after every run. `Ushqyr > Localization > Bake Cyrillic Font` now bakes
both alphabets and every other character of the sheets (93 characters) into a second asset that never changes; it is
asked first. A test requires every character of the sheets to be in a baked font. Checked: the menu was shown in
Kazakh and Russian with the changing asset emptied first, and it was still empty afterwards. Liberation Sans has no
tenge sign; no text needs one.

**Text that fits (1.4).** `Ushqyr > Localization > Check Text Fits`, in play mode, opens every screen of the main menu
and every settings tab in the three languages and lists labels that are cut or overflow. It found 7 (Online, a
settings tab, three settings rows); buttons and settings row labels now shrink to fit (down to 60%) instead of being
cut. 69 screens, 1120 labels, none clipped. **Not covered:** the garage, the in-flight displays, the AI Bench panels
and the replay viewer (they do not build their screens with "Show…" methods); they were looked at by eye only.

**"Who is flying?" (2.3).** A button on the main menu opens the class list (alphabetical in the Kazakh order, ten to
a page, a group filter). A student with a PIN types it on a keypad on the screen; "New student" takes a name and an
optional PIN; "Without a name (this seat)" flies as the seat. Flights are stored under the chosen profile.

- **D27. Nobody chosen means the seat, not a student.** A flight with no profile chosen goes to a profile of the role
  "seat", named after the computer; before, it went to the first student in the list. The choice is forgotten when the
  program closes: a shared school PC asks again.
- **D28. A PIN is typed on an on-screen keypad**, digits only, 4 to 12 of them: it works with a mouse alone and in a
  headset. It keeps classmates out; it is not strong protection, and there is no limit on wrong tries yet.

**Checked.** In play mode against a scratch database: the list, a wrong and a right PIN, the new-student page, the
seat. 436 tests pass. **Not there yet:** changing or removing one's own PIN, renaming, and the teacher's side (reset a
PIN, import a class list on screen); they belong to the teacher console (ED4).

### 22 November 2026: the school build has nothing that reaches the internet (ED1 task 2.5)
**What exists.** `Tools/Edu/school_manifest.py strip` takes the Unity services packages and the analytics module out
of the project; `BuildScript.BuildSchoolWindows` then builds the whole program, refusing to start while those packages
are still there, and `restore` puts them back. The code is the same for both versions: the network assembly knows
whether the packages are present, and without them the Online page offers LAN races only. `Docs/edu/school_build.md`
has the steps and the measurements.

**What was found on the way.** Removing the packages was not enough. A program built by this Unity version opens three
connections at start by itself: two for the engine's statistics and one for "Engine Diagnostics", a setting that is on
by default. The school build now switches all of that off, in the build's settings and again at start-up. Measured on
a small build: **3 connections before, 0 after.**

- **D29. One code base, two package sets.** The school build is the same code built without the internet packages,
  not a second branch. A define set by the presence of the package (`USHQYR_CLOUD`) decides what is compiled.
- **D30. LAN races are part of the school build; internet races and online boards are not.**

**Not done.** The full school build (every scene) was not built or watched, only the small probe; a longer run and a
traffic recorder on a second PC are for the classroom test. The AI Bench Team Kit still has Unity's default reporting;
changing it is the owner's decision.

### 22 November 2026: ED2, the drone classes, their equipment, weather and failures
**Drone classes.** Four profiles in `Flight/EduDrones.cs`, sized like typical aircraft of each class: a 900 g and a
249 g camera drone, a 2.5 kg delivery drone, and an 87 g education drone. They are in the quad catalogue ("Choose
quad") with models of their own, built from simple shapes at run time. The assist's limits (lean, speed, climb) are
worked out from each drone's profile. Tested on the flight model for every class, and for the delivery drone with its
1 kg box: a stick step (90% of the angle within 0.6 s, overshoot under 15%), hover drift under 0.3 m, a stop at a point
25 m away without flying past, a hold in wind (8 m/s for the 900 g camera drone and the delivery drone, 5 for the
small camera drone, 3 for the education drone), a landing, and 8 to 60 minutes of hover on the battery.

**For the person flying.** A setting "Flight assist" (by the drone, off, altitude hold, position hold) and key G in
flight. The Education Suite's drones hold position unless told otherwise; the racing quads do not. Key L opens the
replay viewer on the last flight, which gives the viewer its first way in from the game. The on-screen display and the
recording show the assist or the autopilot as the flight mode.

**Equipment.** The camera drones have a stabilised gimbal: the picture stays level while the drone leans (measured:
drone leaning 5.6°, picture 0.0°), tilts from straight down to 30° up, zooms to four times, takes photos (an event of
the flight with place, heading, tilt and zoom, and a picture file in the user's data folder) and carries a light. The
delivery drone flies heavier with its 1 kg box (3.5 kg, more inertia); key B releases it, the box falls as an ordinary
body, and where it comes to rest is an event of the flight. Checked live: flown 11.7 m by the autopilot, released at
4 m, the box rested 1 cm from the point under the drone.

**Weather to look at.** Rain around the viewer, slanted by the wind, and fog by the visibility in metres; the scene's
own fog settings come back when the weather clears. Every change of weather is an event of the flight.

**Failures by name.** `EduFlight.SetFailure("gps_lost", 1)`: a weak or stopped motor, a damaged propeller (thrust loss
and a shake), a battery drop, GPS lost or degraded, compass interference, radio loss, video noise or loss, a wrong
attitude sensor, a barometer that runs away, a jammed release, a stuck gimbal. Each change is an event of the flight.
Tested: the names, a damaged propeller that can still be flown and landed, and a wrong attitude sensor that makes
altitude hold drift while position hold copes.

- **D31. Assist is a choice for every drone**, with "by the drone" as the default: position hold on the Education
  Suite's drones, none on the racing quads. The flight layer is on a drone only while an assist or the autopilot is.
- **D32. Failures and weather are set by name and value**, the same words in an activity file, on the teacher's screen
  and in the attempt record.
- **D33. A photo and a payload drop are events of the flight**, with their place: exercises are scored from them.

**Not done of ED2.**
- **Thermal view and a winch.** The plan lists both; the scenarios that need them are in ED6.
- **Time of day.** Only the lighting presets a location already has; nothing new.
- **The gimbal in the replay.** The recording does not hold the camera's tilt, so the replay's on-board view looks
  straight ahead.
- **Keys only.** Gimbal, photo, light and release are on the keyboard; a gamepad or radio has no control for them yet.
- **Fog in a built program.** Seen in the editor only; Unity can leave fog out of a build when no scene uses it.
- **By hand.** Everything here was flown by the autopilot or set from code. Nobody has flown the new drones with
  sticks, pressed G, L or B, or judged how the camera drone feels.
- **Drone balls** belong to soccer (ED8).

### 22 November 2026: ED3 started: an exercise runs end to end, the training hub, block A
**How an exercise runs.** The training hub (a "TRAINING" button on the main menu) lists the blocks A to E and the
exercises of each with the student's best score and stars; a block opens when the one before it is passed. Choosing an
exercise opens its place with nothing in it but what the exercise brings: its drone, its assist and view, its objects
(a see-through box to hover in, a landing pad, poles, flags, gates, targets), its weather and the failures of its
timeline. The pilot reads the briefing and what is scored, starts, and is led through the steps on the screen. The
attempt ends on landing, on the last step, on a crash, at the time limit or when the pilot says so; the recording is
graded and stored, and the result shows the score, stars, every line with its measured value and points, two tips, and
the replay with the reference flight beside it.

**What an exercise is.** A file with texts, a "setup" (place, drone, assist, view, start point, objects, weather,
timeline), a rubric, and a "reference" list that says how a good flight goes. `Tools/Edu/make_exercises.py` writes the
files and the sheet of their texts in three languages.

- **D34. An exercise is written in its own frame** (origin: the ground under its start point; +Z: the way the drone
  first faces). The flight notes the frame it was flown in and is brought into it for scoring, so an exercise can be
  laid out in any open place and a reference flown in one scene can be shown in another.
- **D35. One step tracker for guiding and for grading.** The steps of a guided exercise (a stick moved, a zone or a
  height held, a turn, a heading, a landing) are followed by the same code while flying and when the recording is
  graded; nothing is scored from what the screen showed.
- **D36. Half the steps or nothing.** The "steps done" line is required and earns nothing below half of the steps. A
  test caught why: a plain hover passed the pirouette on its side marks alone.
- **D37. A reference flight is made by flying the exercise's own recipe** through the autopilot (or the sticks, for the
  controls check) on the same flight model, and can be made again when the exercise changes.

**Block A, six exercises, all with reference flights (99.5 to 100):** controls check on the ground, take off-hover-
land, height steps, pirouette, out and back, square tail-in.

**Tests (460 pass).** Every shipped exercise has a reference that scores 90 or more; three flights that did not do the
exercise (never took off; a plain hover; flying away) fail each one; every text of every exercise is in the three
languages; frames, steps and the course's block order have their own tests.

**Not done of ED3.**
- **Blocks B to E:** 24 exercises. B needs circling a point and landing on a chosen pad; C needs camera targets;
  D and E are FPV with gates.
- **Flown by a person:** nobody has flown an exercise with sticks yet. Every flight so far was the autopilot's, so the
  thresholds (how many metres, how many seconds) are untested against real hands, and the plan's check against an
  instructor's ranking is not started.
- **The demo before an exercise** (playing the reference in the briefing), a replay library, and the printable
  certificate of exercise 30.
- **Radio and gamepad:** the runner listens to the keyboard for start, finish, retry and leave.
- **Unlocking everything** is a stored setting with no screen yet (the teacher console, ED4).

### 22 November 2026, later: block B of the training course
Six more exercises, flown from where the pilot stands: square nose forward, circling a pole with the nose on it (both
ways), a figure of eight round two poles, nose-in hover and landing (the drone starts facing the pilot), a circuit of
four pads of shrinking size, and a square without position hold in a 3 m/s wind. New for them: a step for circling a
point (only the way flown at the right distance counts) and for landing on a chosen pad; measures of the distance from
a circle and of the nose staying on a target, which look only at the part of the flight on the circle, however long the
way there and back takes. References: 99 to 100. 463 tests pass.

Two faults found by flying the references, both mine: the figure of eight's first pole stood on the straight line from
the pad to the middle box (the reference flew into it; the poles now stand side by side), and an exercise with two
objects of the same name was refused by the file check, as it should be.

**Twelve of thirty exercises exist.** Blocks C (camera targets), D and E (FPV with gates) are not started.

### 22 November 2026, later still: block C of the training course
Six exercises flown through the camera drone's own camera: three buckets (low, high, on the ground mouth up), four in
a row, the four sides of a stand, a tower climbed in a spiral, three buckets looked into from above, and the emergency
drills. A photo is a step done only when the camera really looks into the bucket: in front of its mouth within 15° of
its axis, 1 to 15 m away, and aimed at it within 8°. The emergency drill's failures follow the pilot, not the clock:
the battery drops when the far box is reached, a motor weakens when the drone is back over the pad. References: 98.8
to 100. 465 tests pass.

- **D38. A failure can follow a step** (`after_step`), with a delay if wanted. A failure by the clock would come
  before a slow pilot was ready for it, or after a fast one had finished.

**Left out of the emergency drill: loss of positioning.** With no position the autopilot cannot fly the reference
(it drops to altitude hold and drifts, as it should), and an exercise with no reference flight is not allowed. That
drill needs either a reference flown by a person or a way to record one; block B's "without position hold" exercise
teaches the same handling meanwhile.

**Eighteen of thirty exercises exist.** Blocks D and E (FPV: gates, flags, acro) are not started.

### 22 November 2026, night: four exercises of block D, and two faults found by flying them
**Block D so far (19 to 22):** three gates in a line, an oval round two flags, a slalom, and low flight through four
low gates. They are flown from the drone's own camera on the 5" freestyle quad with no assist, in the flight mode the
exercise fixes (Angle, Horizon for the low flight). New for them: a step for flying through a gate (through the
opening, the right way), the share of time inside a height band, and a flight mode the exercise holds whatever the
pilot's switch says. References: 95.6 to 100. 467 tests pass.

**Two faults, both found because a reference flight scored badly.**
- **A waypoint was counted without being reached.** A zone, height or heading step with no holding time was accepted
  at once, wherever the drone was. The oval's reference hovered on the spot and was credited with two laps. Fixed,
  with a test; every earlier reference was graded again with the fixed tracker and still scores 90 or more.
- **The scene could take an exercise's assist away.** When a radio finishes being detected, or a setting changes, the
  scene applies the pilot's own "flight assist" setting again. In an exercise that switched the flight layer off (the
  first two block D references never left the ground) and could have replaced an exercise's assist with the pilot's.
  While an exercise runs, assist and flight mode are now the exercise's, and the free-flight keys G and L do nothing.

- **D39. Reference flights of the FPV exercises are flown by the autopilot along the same course;** the flight mode is
  held on the pilot by the exercise, not checked in the score. The autopilot cannot fly acro, and what the rubric
  measures (gates, height, path, time) is the same either way.

**Twenty-two of thirty exercises exist.** Left: 23 acro hover, 24 acro circuit, 25 racing line, 26 split-S, 27 power
loop, 28 roll and flip, 29 proximity gaps, 30 check ride. Of these 26 to 28 are manoeuvres the autopilot cannot fly,
so their reference flights need scripted stick work (or a person): not started.

**Also during this session** the Unity editor closed and reopened once while a reference was being flown (a shell on
this PC ran out of resources at the same moment). Nothing was lost; the flight was flown again.


### 22 November 2026, late night: the rest of blocks D and E except the check ride

**Exercises 23, 24, 25 and 29** (acro hover, acro circuit, racing line, proximity gaps): references flown by the
autopilot along the course, all 100.

**Exercises 26, 27 and 28** (split-S, power loop, roll and flip) are judged by two new step kinds, `roll` and `flip`:
how far the drone has turned about its own nose-to-tail or wing-to-wing axis since the step began, either way round,
whichever way it faces. Rocking back and forth cancels out, and a flip is not counted as a roll (tested).

- **D40. The references of the three manoeuvres are flown on the sticks, by a script.** The reference pilot gained
  stick commands that take the drone from the autopilot, and `hold`, which gives it back wherever it is. The
  autopilot carries the drone to the start and lands it; the manoeuvre itself is stick positions held until a step is
  done, on the same flight model in acro. References: 100 each.
- **D41. The power loop is judged in four parts,** not as one 360 degree flip: through the gate, nose to the sky,
  over the top of the gate (a zone 5.5 to 12.5 m up), round to level, and through the same gate again. The first
  reference flipped on the spot beside the gate and passed a plain "flip 360"; that is not a power loop.

**Twenty-nine of thirty exercises exist.** Left in ED3: 30 check ride with its certificate, a demonstration of the
reference in the briefing, the replay library, and gamepad or radio control of the exercise screens. 467 tests pass.

### 22 November 2026, morning: the check ride and the certificate. All thirty exercises exist

**Exercise 30, the check ride:** three gates out, a climb to 12 m, one roll, one flip, two gates back and a landing
where the flight began, in acro, with the time deciding the stars. Reference (autopilot for the gates, sticks for the
roll and flip): 100, in 50 seconds.

**The certificate** appears as a button in the training hub once the check ride is passed. It is one A4 page saved as
a PDF in the class data folder on that PC (`certificates`) and opened in the PC's own PDF reader to be printed. It
gives the student's name, exercises passed, the check ride's score, the average score, the stars, the date, and lines
for the school and the teacher to sign. Russian, Kazakh and English, with the font inside the file.

- **D42. The certificate says what it is.** Its small print reads: the flights were flown in the simulator; the
  certificate records training and is not a permit to fly a drone.
- **D43. A seat profile's certificate has an empty line for the name.** A seat names a PC, not a person; the teacher
  writes the name by hand.

Left in ED3: a demonstration of the reference in the briefing, the replay library, and gamepad or radio control of
the exercise screens. 469 tests pass.

### 22 November 2026, midday: the demonstration, the replay library, a gamepad on the exercise screens. ED3 is built

- **"Show me"** in the briefing (key D) plays the exercise's reference flight in the replay viewer before the student
  tries. Closing it returns to the briefing.
- **"My flights"** in the training hub lists the student's own attempts, newest first, eight to a page: when, which
  exercise, the score, the time. Choosing one lays the exercise out again and plays the stored recording beside the
  reference; closing it returns to the menu. A flight whose exercise is no longer shipped, or whose recording is gone
  from the disk, is shown but cannot be opened.
- **A gamepad works the briefing and the result:** A starts or flies again, B leaves, X shows the demonstration, Y the
  replay. In flight the gamepad's buttons stay the drone's (Start arms). A radio has no such buttons: with a radio the
  screens are worked with the keyboard or mouse.

**ED3 as built:** 30 exercises in five blocks, each with a reference flight scoring 95.6 to 100; the hub with blocks
that open in order; briefing, guided steps, result with tips; the demonstration; the replay with the reference beside
it; the library; the certificate. **Not done, and said so:** the loss-of-positioning drill has no automatic reference
(left out); nothing has been flown by a person yet, so the pass marks and the star times are guesses to be set in the
classroom pilot (ED10); the Kazakh texts need a native review. 469 tests pass.

### 22 November 2026, afternoon: ED4 started: the teacher's console on one PC

**What exists.** "Who is flying?" has a **Teacher** button. The first time it asks for a password to be chosen (4
characters or more; kept only as a salted, slowed hash); after that it asks for it. Behind it, four tabs:

- **Journal:** students down, the exercises of one block across, the best grade in each cell (green passed, red
  flown but not passed), with "passed n of 30" and the average. A cell opens that student's attempts on that
  exercise: date, computed score, time, **Replay** (the stored flight beside the reference) and **Mark** (the
  teacher's own mark from 0 to 100 and a comment; an empty mark gives the computed score back). The teacher's mark
  decides the pass as well as the grade.
- **Students:** groups, a new group, a new student, **Import a list** (paste the names, one on a line; names already
  in the group are skipped). A student's page: progress report, remove the PIN, take out of the group, and "delete
  this student's data", which must be pressed twice and removes the profile, the results and the recordings.
- **Reports:** the school's name; the group's gradebook as XLSX (best grades as numbers, a second sheet with how the
  group did on each exercise) and as CSV; a progress report (PDF) for one student or for every student of the group.
  Files go to `reports` in the class data folder.
- **Settings:** all exercises open or opening in order; back up the class data; restore from a backup; change the
  password; leave the console.

- **D44. CSV is written for a spreadsheet set up for Russian or Kazakh:** semicolons between cells, a comma in
  numbers, UTF-8 with a byte-order mark. A cell that begins like a formula is written as text.
- **D45. Restoring a backup first saves what it replaces** as `before-restore-…` in the same folder, so the step can
  be undone. The file is checked before anything is touched. Recordings are files beside the database and stay.
- **D46. The teacher's password is a lock on a shared PC, not security:** whoever can replace the database file gets
  round it. The school's Windows accounts are the real protection; the installer's notes for school IT will say so.

**Checked:** in the editor on a made-up class of 11 students and 66 attempts: every screen drawn; the XLSX is a
well-formed workbook of two sheets; the CSV and the PDF read correctly with Kazakh names; backup, a change, restore
gives the class back. **Not checked:** the XLSX in Excel or LibreOffice (neither is installed here); typing into the
boxes by hand (they were filled by code); a school logo in the header is not done (the PDF writer has no pictures).

**Still to build in ED4:** assignments and lesson plans; the classroom link over the LAN (seats, "start on all
seats", live control, the grid with small pictures, messages); attendance; simulated seats to reach thirty.
470 tests pass.

### 22 November 2026, later: assignments and lesson plans

- **Tasks** is the console's fifth tab. The teacher gives an exercise to a group or to everyone, with an attempt
  limit (none, 1, 2, 3, 5 or 10); the list shows how many of the group have passed each one, and "take back".
- **Lesson plans:** a name and exercises pressed in the order of the lesson; a plan is given to a group in one press.
- **The student** sees "My tasks: n to do" in the training hub: the exercises given, in order, with attempts used and
  the result. A given exercise opens whatever block it is in. The briefing shows the attempts left; with none left
  the exercise does not start.
- The class database moved to layout 2 (lesson plans; an older file is upgraded when it is opened).

- **D47. An exercise given again to the same people replaces the earlier one,** and the attempt count starts
  afresh. What a student is given directly outranks what their group is given, which outranks "everyone".
- **D48. The teacher's mark decides the pass everywhere.** Found on the screen: an attempt marked 45 by the teacher
  was "not passed" in the journal but still "passed" in the student's hub. Both now follow the mark (test added).

**Not done here:** assists allowed and weather as settings of an assignment (the exercise's own are used);
"start on all seats now" and lock mode belong to the classroom link, next. 471 tests pass.

### 22 November 2026, evening: the class link

**What exists.**
- **The link** (`ClassLink.cs`, plain sockets, no engine code): the teacher's PC listens on TCP 47810 and announces
  the class by UDP broadcast on 47811; a seat finds it in a list or types the address, and types the six-digit code
  from the teacher's screen. A seat sends its status twice a second, a small picture three times a second, and each
  finished attempt; the teacher's PC sends signed commands.
- **The teacher's Class tab:** the code and address; a grid of seats (eighteen to a page) with the live picture, who
  is flying, what they are doing and the score; a raised hand lights the seat up. For all seats: start an exercise
  (with an attempt limit, and "leaving not allowed"), pause, resume, "attention" (the seats go black and say "look
  at the teacher"), everyone to the menu. For one seat: a larger picture, start, start again, pause, resume, wind,
  three failures, clear them, and a short message.
- **A seat:** "Join the class" on "Who is flying?"; F2 raises a hand. The class list of names arrives from the
  teacher's PC (names only), so a student finds their name on any seat.
- **Results:** a finished attempt waits in the seat's outbox until the teacher's PC confirms it has the recording,
  and is sent again whenever the link comes back; a recording already stored is not stored twice.

- **D49. Teacher commands are signed with a key made with the class** (RSA 2048, PKCS#1 with SHA-256, the one
  signature the ED0 spike showed working in a build). The key lives in the class database on the teacher's PC; a seat
  remembers its fingerprint for the class's address and refuses another. A command carries the session and a number
  that only grows, and the seat it is for, so it cannot be replayed later or to another seat.
- **D50. The teacher's PC grades every attempt again from the recording.** A seat's own score is not trusted: in
  the check below a seat claimed 3 for a flight; the journal stored 100, which is what the recording earns.
- **D51. The class link listens on the school network, not only on this PC,** unlike the program's other local
  servers: seats are other PCs. It is guarded by the class code and the signed commands. The code is six digits
  and the link is not encrypted: it keeps a neighbouring classroom out; it is not proof against someone recording
  the school's network. Written down for school IT in `Docs/edu/class_link.md`.
- **D52. Lock mode is "leaving not allowed"** inside our program: the pilot cannot leave the exercise the teacher
  started until the teacher sends everyone to the menu or starts another. Nothing outside the program is touched.

**Measured on this PC (one program being teacher and seat, plus simulated seats, over loopback):**

| Requirement of section 9 | Measured | Note |
|---|---|---|
| An assignment starts on every seat within 5 s | 30 simulated seats heard the command inside the test's 5 s limit; a real seat showed the briefing about 8 s after the command, almost all of it loading the field | Not met when the seat must load a place first. Not yet measured on a second PC |
| The grid updates at 2 frames a second or more | 3 pictures a second sent and received (174 of 174 in one flight) | |
| A weather change takes effect within 0.5 s | wind 6 m/s was on the seat's drone at the next check, 2 s later | Not timed finer than that |
| Streaming costs under 5% CPU per seat | 0.6 ms a picture, three a second: about 0.2% of one core | It was 8 to 18 ms with a blocking read; the picture is now fetched from the graphics card without waiting |
| The journal shows an attempt within 3 s | stored when it arrives | |
| After a seat disconnects and returns, nothing is lost | tried as a whole on loopback: the teacher's end was closed in the middle of a flight; the flight was finished (99.5) and waited in the outbox; when the teacher's end came back the seat rejoined by itself within 10 s and the recording arrived; the outbox was empty | Not tried by pulling a network cable |

**Not done, and not claimed:** nothing has been tried between two PCs (it needs a second PC: a stop-and-ask point);
watching a seat's flight live with a free camera and the stick overlay (the grid shows the seat's own picture
instead); "stuck" and "off-task" alerts; attendance; a seat map laid out like the room; assists and weather as
settings of an assignment; a seat practising alone does not yet fetch its assignments from the teacher's PC.
479 tests pass.

### 22 November 2026, late evening: what was left of ED4 that one PC can do

- **Tasks reach the seats.** When a seat joins, it receives the class's names and what each group and student has
  been given (exercise ids, attempt limits, lesson and place in it), and "My tasks" on the seat shows it. An
  exercise the seat's version does not ship is left out. A PC that is itself holding a class ignores this.
- **Two alerts in the grid:** "no progress" when a flying seat has gone a minute without finishing a step, and "not
  in the task" when an exercise was started on every seat and this one is in the menu.
- **Attendance:** a student seen on a seat of an open class is noted as present that day. The student's page in the
  console shows lessons attended, and the gradebook (XLSX) has a third sheet, students by days. The class database
  moved to layout 3.

- **D53. A seat follows its class:** the tasks sent on joining replace the tasks the seat held before.

**ED4 as built:** the console (journal with marks and replays, class lists with import, tasks and lesson plans,
reports, backup and restore) and the class link (seats, pictures, start on all, live control, results, tasks,
attendance, alerts). **Open, and said so:** a trial between two real PCs and on a school network (needs a second
PC); watching a seat's flight with a free camera and stick overlay; a seat map shaped like the room; assists and
weather as settings of an assignment; a school logo on reports; the XLSX opened in Excel or LibreOffice;
the "5 seconds to start" figure on real seats. 479 tests pass.

### 22 November 2026, night: ED5 started: the FAI racing rules read, the tournament engine

**The rules.** FAI F9U (drone racing) was read from the same volume as the soccer rules and written into
`Docs/reference/fai/f9u_values.md`, with paragraph numbers: the race (three laps, four pilots, three minutes), the
start (arm call, one tone after 2 to 4 s, no countdown, the false start), faults (a missed gate voids the lap unless
corrected at once), the two qualification options with their tie-breaks, placing in a race, the 16-pilot bracket
with and without double elimination (Annex C, read from the page), the final classification, successive finals, and
the points format for small events.

**The tournament engine** (`Tournament.cs`, no engine code, 10 tests): blind draws that differ each round and repeat
for the same seed; the ranking by either FAI option; the elimination of scenario C with and without double
elimination; a single final or successive finals; the points format; a race flown again; a disqualification or a
penalty set after the race (what followed from that race is dropped and laid out again); a pilot disqualified from
the event; the whole state written to text and read back in the middle of an event.

- **D54. Racing follows FAI F9U, 2026 Edition, Version 2,** as read into `f9u_values.md`. Where that page and the
  list in section 5 differ, the page wins.
- **D55. Brackets for 32 and 64 pilots are not built:** Annexes A and B were not read. Sixteen pilots is the size the
  plan's own test asks for, and a class is at most thirty.
- **D56. Outside the FAI text, and marked as ours:** with 5 to 8 pilots the elimination starts at the semi-finals
  (places 1, 4, 5, 8 and 2, 3, 6, 7), with 4 or fewer at the final; a tie in the points format goes to the faster
  best lap; "races of up to 8 with the top half advancing" is a preset of ours still to be built.

**Checked by test against the FAI page:** with the faster qualifier always ahead, every race of the double
elimination bracket has exactly the pilots the annex names, and the final classification returns the qualification
order place by place. The plan's own test at the level of the engine: 16 pilots from qualification to the final,
with a disqualification that changes who goes on and a final flown again. **Not yet:** timing from the gates, the
FAI start, the screens, the LAN, the result sheets. 489 tests pass.

### 22 November 2026, night, later: race timing and the result sheets

- **Timing** (`RaceTiming.cs`, no engine code, 7 tests): from gate passes alone it gives the holeshot, lap times, a
  split at every gate, the best lap, the best three laps in a row, the time for the race, live positions and the gap
  to the leader, to a thousandth of a second. Elimination and final races are timed from the start signal,
  qualification from the timing gate. It ends at three minutes of flight, and can disqualify for an extra lap.
- **Result sheets** (`RaceReports.cs`): the classification, the qualification ranking and every race with its lap
  times as XLSX; every race as CSV; a PDF with the classification and the bracket, one box a race, a column a round.
  Three languages; checked by eye in Russian with Kazakh names.

- **D57. A missed gate, as the simulator reads F9U.7.2:** a gate passed out of turn does not count; the pilot may go
  back through the missed gate, and then the lap counts with the time the detour took. Crossing the line with a gate
  still missing voids the lap. The race clock runs on, so a void lap costs all its time, and "three laps in a row"
  does not span a void lap. The FAI text says only that the lap "will not be validated"; the rest is ours.
- **D58. A false start is two things together,** as in F9U.6.2: the model left its start position before the signal,
  and it went through the first gate. A model that dropped back to the ground before the signal is not disqualified.

**Not yet:** these are not joined to the game's own race (its countdown is still 3-2-1, its gates still feed the old
lap timer), nor to the LAN, nor to any screen. 497 tests pass.

### 22 November 2026, night, later still: tournaments in the teacher's console

- **Racing** is the console's seventh tab. A tournament is made from a group: its name, the format (FAI, or points
  for small classes), the number of rounds, the ranking option, double elimination, one final or finals until
  someone has won two.
- **The tournament's page** shows the race to fly next with its pilots, the classification as far as it is known,
  the races flown, and the sheets (PDF with the bracket, XLSX, CSV).
- **A result is entered by hand** for now: each pilot's lap times typed with spaces between them (seconds, or
  minutes:seconds), "disqualified" and "did not fly". A flown race can be **flown again** (pressed twice) or
  **corrected**: a pilot disqualified from it, or a penalty of 1, 2, 5 or 10 seconds; the races that followed from it
  are laid out again.
- The tournament is saved in the class database after every change (layout 4), so it goes on from where it was after
  the program is closed or the power goes.

**Checked** in the editor on the made-up class: with 11 pilots the first elimination race is flown by three (places
1, 8 and 9), as the FAI note says for a missing place; the three sheets were written from the console.
**Not yet:** results come from typing, not from a race flown over the LAN; a time typed by hand is the sum of the
laps (the holeshot of an elimination race is not asked for). 498 tests pass.
