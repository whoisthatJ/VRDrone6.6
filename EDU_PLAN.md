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
