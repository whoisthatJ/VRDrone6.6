# Phase 3 Plan: Better Track_expo, Vertical Routes on Track_expo, Realistic Quads, Bot Races, Strike Range (desktop PC)

> **Platform:** desktop PC simulator, no VR. Builds on `PLAN.md`, `FLIGHT_PHYSICS.md`, `VISUALS_PLAN.md`, `TRACK_GARAGE_PLAN.md` and `TRACK_EXPO_PLAN.md`.
> **Order of work:** A and C can run in parallel → B → D → E → F. Each workstream ends with a demo build and my review.

| # | Workstream | Your task |
|---|---|---|
| A | **Track_expo** (existing track): environment quality | 1 |
| B | **Track_expo** (existing track): vertical movement in its route (over the sphere, holographic rings) | 2 |
| C | Realistic quad models | 3 |
| D | Race against bots (difficulty levels) | 4 |
| E | Strike Range mode: 2-minute target attack, offline with bots | 5 |
| F | Online multiplayer for Strike Range (players + bots, LAN and internet) | 5 |

> **Scope of A and B: the existing `Track_expo` track only.**
> - Workstreams A and B **modify the existing `Track_expo` scene** and **its existing route**. That's the EXPO/sphere track built from `TRACK_EXPO_PLAN.md`, where it was called "Sphere District".
> - **Don't create a new scene or a new track, and don't rename anything.**
> - First find the scene and everything it uses: its `TrackDefinition`(s), environment generators and prefabs, materials, lighting settings, bookmarks and tests.
> - Work on a git branch.
>
> **Keep the first version as a distinct scene.** Before *any* change (step A-1 below), duplicate the current `Track_expo` scene into a separate scene, **`Track_expo_v1`**:
> - **Frozen:** `Track_expo_v1` keeps the original route *and* the original look exactly as they are now.
> - **Self-contained:** every asset it uses that A or B will modify (route `TrackDefinition`s, environment prefabs, materials, shaders, lighting/Volume profiles, lightmaps/probe data, generator settings) is copied into `Assets/_Project/Tracks/Track_expo_v1/` and the v1 scene points to the copies. Unchanged shared assets (flight model, quads, obstacle kit code) stay shared.
> - **Playable on its own:** `Track_expo_v1` gets its own menu entry (e.g. "Expo Classic"), its own track ID, and **keeps the existing best times, ghosts and leaderboards**. The upgraded `Track_expo` gets a **new track ID/version** with fresh records, because its layout changes.
> - **Verified:** screenshots of `Track_expo_v1` from all bookmarks are taken right after the copy and again at the end of A and B. They must match (no visual change), its tests must pass, and it must load and play from the menu.
>
> All upgrades in A and B then go into `Track_expo` only.

---

## A. Track_expo: environment quality
**Goal:** improve the existing `Track_expo` scene. From the reference camera angle, a screenshot should be hard to tell apart from the aerial photo (`Docs/reference/ref_expo_aerial.png`), while holding the frame-rate targets (144+ fps recommended PC at High, ≥ 60 fps minimum PC at Low). Improve the existing generators, prefabs and materials in place; don't rebuild the scene from scratch.

**A-1. Create `Track_expo_v1` first** (see the scope note above): copy the scene, make it self-contained, give it a menu entry and the existing records, and take its reference screenshots.

**A0. Audit.** Screenshots of `Track_expo` from all bookmarks, side by side with the reference. The agent writes `Docs/track_expo_quality_audit.md`: a ranked list of the 15–25 biggest visual gaps (e.g. "pavilion façades look flat", "road markings blurry", "trees all identical"), each with a planned fix and a cost estimate. Fix them in rank order.

**Typical upgrades (the audit decides the final list):**
| Area | Upgrade |
|---|---|
| **Sphere** | Layered glass shader (Fresnel, tinted reflection from a dedicated reflection probe, time-sliced realtime update optional at High+), bevelled mullion geometry, a PV-cell pattern, interior floors with lit exhibits visible through the clear side, a detailed opening near the top, turbines, a glazed podium with canopy ribs |
| **Pavilions and pods** | Real mullion grids on the façades, **interior-mapping window shader** (fake rooms with lights), roof panel seams and edge trims, skylights with visible interiors, gutters, entrances, rooftop equipment, neutral signage |
| **Ground** | Stone paving with the right pattern and scale, kerbs, drains and manholes, crisp road markings and crosswalks, parking lines, tyre/wear decals, puddles (optional), flower beds and shrubs |
| **Life** | 3–5 tree species with LODs and wind; benches, bins, bollards, lamps, signs, flags; parked cars with colour variation; **moving traffic** on the ring roads (cheap spline-driven instanced cars) |
| **City** | 4–6 façade materials per building type with interior mapping, balconies, rooftop clutter, an Astana colour palette (beige, white, pastel, glass), more accurate heights, higher-res skyline impostors, a detailed landmark tower |
| **Sky and light** | An HDRI and sun direction matching the photo, a cloud layer, haze colour, Adaptive Probe Volumes (or lightmaps) for indirect light, SSAO, reflection probes; **colour calibration**: compare average colour/brightness of sky, grass, roofs and asphalt regions between the reference and the screenshot, and tune exposure and white balance until they match |

**Done when:** the audit list is fixed (or consciously postponed), the reference-angle screenshot is approved by me, and the frame-rate targets are met at every preset.

---

## B. Track_expo: vertical movement in the route
**Goal:** the **existing `Track_expo` route** uses the third dimension: climbs, dives, flying **over the sphere** and **over/through giant holographic rings**. The route is reworked in the `Track_expo` scene, keeping the parts of the current route that work and adding vertical sections between them. The original flat route stays playable in **`Track_expo_v1`**, which is not touched.

**B1. Track system upgrade (needed for real vertical courses):**
- The route spline gets a full 3D orientation (tangent + up vector/roll), so it supports vertical and even inverted sections (loops, split-S).
- Gates align to the full 3D tangent, not only yaw. Checkpoints check the pass direction in 3D.
- **Route guidance in 3D:** floating holographic chevrons in the air (ground lanes don't help at 80 m up). The next-gate marker shows an up/down arrow when the gate is above or below the view, and the beacon becomes a **pulsing holo ring** around the next gate (a vertical beam is useless when the gate is overhead).

**B2. New obstacles:**
| Obstacle | Description |
|---|---|
| **HoloRing gate** (new type) | A floating ring with no supports: animated hologram shader (Fresnel glow, scanlines, flicker, slight distortion), particle sparkle, ring colour by state (next / later / passed). Sizes 4–8 m; any orientation (vertical, horizontal, tilted). **No physical collision** (it's light); touching the rim gives a glitch effect and a small time penalty in races |
| **Holo Halo** (set piece) | A giant horizontal holographic ring (Ø 30–40 m) floating ~60 m above the plaza/park, slowly rotating, with projector beams from the ground. The route goes **over it and dives down through its centre** (or climbs up through it on the beginner variant of `Track_expo`, if there is one) |
| **Sphere Crest** | A gate sequence that climbs along the sphere's flank, a **crest gate floating above the very top** (~112–115 m, above the turbines), then a **dive gate series down the far side** and a low pull-out gate at ~5 m |
| Other vertical moves | A power loop over a pavilion roof edge, a split-S from a high HoloRing, a vertical ladder (3 stacked rings), roof hops over the pillow roofs, a dive from a ring at the top of the needle monument |

**B3. Rework the existing Track_expo route:**
1. **Inspect** the current route(s) in `Track_expo`: how many courses/variants exist, their gates, length, heights and lap times. Write it up in `Docs/track_expo_route_v1.md` with a top-down map image.
2. **Propose** the new route as a top-down + side-elevation diagram (height along the lap) and get my OK before building. It must include:
   - the **Sphere Crest**: climb the flank, cross **over the top of the sphere**, dive down the far side;
   - the **Holo Halo**: fly **over** the giant holographic ring, then dive **through** it;
   - at least 3 further vertical moves (power loop over a pavilion roof, split-S from a high HoloRing, a ladder, roof hops);
   - the good existing sections of the route kept where they still fit.
3. **Per existing variant:**
   - if `Track_expo` already has a beginner variant and an advanced variant, the beginner one gets gentle vertical content (climbs up to ~60 m, a roof hop, a climb *up* through the Holo Halo), and the advanced one gets the full Sphere Crest and Halo dive (up to ~115 m);
   - if it has only one route, it gets the full vertical content, and I decide whether we also add an easier variant.
4. **Targets:** the lap time with a 5" stays roughly within ±30% of the current `Track_expo` lap time. Every gate is readable in the FPV view, and the route works with all guidance modes (Off / Standard / Trainer).

**Done when:** the reworked `Track_expo` route is flyable by me and by an Elite bot (from D), the lap times are reported, and `Track_expo_v1` still plays exactly as before.

---

## C. Realistic quad models
**Goal:** quads in the garage and in flight look like real FPV quads, with correct proportions, components, wiring and materials. Every visual part still comes from the garage part data, so the physics stays consistent.

**C1. References.**
- Photos of real quads (best: **your own quads**: top, side, front and 3/4 views, with a ruler in the frame) saved to `Docs/reference/quads/`.
- Typical real dimensions:
  - stack mounting 30.5 / 25.5 / 20 / 16 mm;
  - motor mount patterns 16 × 16 / 12 × 12 / 9 × 9 mm;
  - stator sizes;
  - camera widths 19 mm (micro) / 14 mm (nano);
  - standard prop diameters;
  - typical battery sizes per class.
- **No brand copies:** generic real-world construction; our own fictional brand names and graphics.

**C2. What a "real" quad has (the component checklist):**
- **Frame:** carbon plates with chamfered edges and visible weave, countersunk screws, aluminium standoffs, arm locks, TPU parts (camera mount, antenna mounts, arm bumpers).
- **Electronics:** FC/ESC stack (PCB with components, soft-mount grommets), a capacitor on the battery leads, an XT60/XT30 pigtail, a receiver with T-antennas.
- **Video:** an analog VTX + antenna (linear, pagoda/lollipop), **or** a digital HD air unit (bigger camera module, two antennas).
- **Camera:** an FPV camera with side plates, lens and protective TPU, set at the build's camera angle.
- **Wiring:** motor wires along the arms, heat-shrink, battery strap and grip pad, LEDs, buzzer.
- **Optional:** an action camera (generic design).
- **Classes:**
  - 5" freestyle (true-X);
  - 5" race (low, stretched X);
  - 3" toothpick;
  - 3.5" cinewhoop (ducts);
  - 65/75 mm whoop (ducted frame, AIO board, canopy);
  - 7" long range.

**C3. Tooling.** Generate the meshes with **Blender scripts run headless** (free; you install Blender 4.x LTS). Blender gives proper bevels, subdivision, UV unwrapping and baked normal/AO maps. The meshes stay **parametric**: the garage part data → a Blender script → FBX/glTF → a Unity prefab with sockets. If Blender isn't available, improve the in-Unity generators (bevelled edges, chamfers) instead.

**C4. Materials and in-flight details:**
- Materials: carbon twill, anodised aluminium (colour options), matte TPU, a PCB texture (our own), our own printed battery wrap, translucent props.
- In-flight details: props switch to a motion-blur disc at high RPM; LEDs; slight antenna wobble.
- Optional later: visible prop damage after crashes.

**Done when:** each class, side by side with a real reference photo at the same angle, reads as "a real quad of that type" (my judgement), and an automated check passes (wheelbase and prop clearance match the part data, no overlapping props, parts snap to sockets).

---

## D. Race against bots
**Goal:** a race mode with 1–7 AI pilots at selectable difficulty. **Bots fly with exactly the same physics and flight controller as the player** (they "move the sticks"), so they're fair and teach good lines.

**D1. Shared bot pilot core (reused by Strike Range in E):**
- **Planner:** a racing line for races (D2), or an intercept path for Strike (E3).
- **Trajectory follower:** position/velocity error + feed-forward acceleration → desired thrust vector → desired attitude → desired body rates.
- **Stick synthesiser:** desired body rates → stick deflections through the **inverse of the bot's rate curve** (Betaflight/Actual rates), and desired thrust → throttle. The output is a normal `PilotInput` → the same flight controller → the same physics.
- **Difficulty profile** (a ScriptableObject): pace scale, line accuracy (lateral noise), reaction delay, stick noise, mistake chance (late turn, overshoot), recovery skill, aggression.

**D2. Racing line baker (per track and quad class, an Editor button + cached asset):**
- Start from the route spline.
- Optimise the line inside the gate openings for minimum curvature.
- Compute the speed profile with a forward/backward pass, limited by the quad's real thrust-to-weight, drag and max lateral acceleration (from its `QuadProfile`).

**D3. Difficulty levels:**
| Level | Pace vs the reference line | Behaviour |
|---|---|---|
| Rookie | 55–65% | Wide lines, slow reactions, occasional mistakes |
| Amateur | 70–75% | |
| Pilot | 80–85% | |
| Pro | 90–95% | |
| Elite | 98–100% | Clean, near-optimal |
| **Adaptive** (teaching) | Tracks your own recent lap times ±5% | Always a close race |

**D4. Race mode:**
- Bot count 1–7, laps, a grid start with countdown, live positions and gaps, overtakes.
- **Midair collisions on/off** (off = ghost bots for beginners).
- Simple avoidance (bots shift their line when a drone is close ahead).
- Bot names, colours and builds.
- A results screen with positions, best laps and a "watch the winner's replay" option.

**Done when** (tests):
- the Elite bot completes every lap of every course (including both `Track_expo` and `Track_expo_v1`) on 10 random seeds without missing a gate;
- average lap times get faster with each level from Rookie to Elite;
- Adaptive stays within ±5% of a recorded player pace;
- 7 bots + player keep the frame-rate targets.

---

## E. Strike Range: 2-minute target attack (offline with bots)
**Concept:** a precision-flying score attack. You get **2 minutes** to hit as many large vehicle targets on a training range as you can. Each hit scores, and you immediately relaunch a fresh quad from a launch pad. **Easy targets are static; harder ones move.** You compete with bots on the same range.

**Content rules (it's an arcade game mode):**
- **Fictional generic vehicle designs:** no real-world vehicle models, insignia, unit names or countries.
- **No people** on the range.
- **Arcade hit effects:** sparks, flash, smoke, the vehicle shown "disabled", then it respawns.
- **No real weapon, warhead or armour-vulnerability modelling:** a hit is a hit.

**E1. Range scene `Range_Steppe`:**
- Steppe terrain with low hills, dirt roads, berms, revetments, old hulks, a watchtower and a command tent.
- 3 launch pads with countdown lights.
- Weather/time presets (day, overcast, dusk).
- Range size about 1.2 × 1.2 km. Targets are 150–700 m from the pads.

**E2. Targets:**
| Class | Movement | Points |
|---|---|---|
| **Static:** large tracked vehicles, wheeled carriers, trucks, artillery-like vehicles, radar trucks (fictional designs) | None | 100 |
| **Moving** | Drive along dirt-road splines at 10–40 km/h, convoys | 200 |
| **Evasive** | Stop-go, reversing, zig-zag, dropping smoke screens that block vision | 300 |
- **Hit rule:** contact with the target's hitbox at an impact speed ≥ 8 m/s. Below that it's a "bump", with no score and a crash.
- **Scoring:**
  - a **streak multiplier** (×1.1 per hit without a crash, max ×2);
  - a small speed bonus;
  - crashes into terrain cost time (relaunch delay).
- **Relaunch:** 1.5 s after a hit or crash, from the nearest free pad, with a fresh battery.
- A disabled target respawns elsewhere after 10–15 s, so the range never runs dry.
- **Difficulty presets for the range:**
  - Easy: static targets only, no wind;
  - Normal: mixed;
  - Hard: more evasive targets, gusty wind, smoke.

**E3. Bots on the range:**
- They reuse the D1 core with an **intercept planner**: predict a moving target's future position and fly to meet it.
- Target choice is by value ÷ estimated time to reach.
- Terrain/obstacle avoidance.
- Difficulty levels as in D3, plus target-choice smartness.

**E4. Round flow and UI:**
- Lobby → choose the quad (garage builds; a 7" or 5" recommended) → a 3-2-1 countdown.
- HUD: timer, score, streak, a target radar/minimap, off-screen target markers.
- End screen: scoreboard, hits per class, accuracy, best streak.
- Local leaderboard per range difficulty.

**Done when:** a 2-minute solo round and a round vs 3 bots run correctly; scoring has unit tests; bots at different levels produce clearly separated average scores; the frame-rate targets are met.

---

## F. Online multiplayer for Strike Range
**Goal:** up to **8 human players** (plus bots filling empty slots) on the same range, competing for the same targets.

| Topic | Decision |
|---|---|
| Tech | **Netcode for GameObjects** + Unity Transport + **Unity Multiplayer Services sessions with Relay** for internet play. **Direct IP / LAN** mode for classrooms (no internet or Unity account needed) |
| Topology | **Host-client** (one player hosts); no dedicated servers |
| Drones | Each client simulates **its own** quad physics locally (instant control, no input lag) and sends its state at 30 Hz; remote quads are interpolated |
| Targets | Moving targets follow deterministic paths from the round seed + shared clock, so every client computes them locally and only events are synced |
| Hits | The client reports a hit (target id, time, position, speed); the **host validates it** (target still active, position within tolerance) and awards the hit. The first valid hit wins the target |
| Bots | Run on the host and are synced like players |
| Sessions | Create/join by code, quick-join, ready-up, the host picks the range difficulty and the bot count/level, end-of-round scoreboard, rematch |
| Leaderboards | Local first; online leaderboards (Unity Leaderboards) optional later |
| Cost | Unity Relay has a free tier (first 50 average monthly concurrent users at the time of writing; check the current pricing). LAN mode is free |

**Optional later:** reuse the same networking for **online races** (D + F).

**Done when:**
- a LAN session with 2 PCs and an internet session over Relay both work;
- 4 humans + 4 bots stay in sync for a full round (no disputed hits, consistent scoreboards on every client);
- a disconnect is handled (the player is removed, the round continues; if the host leaves, the round ends cleanly);
- the network cost per player is measured.

---

## Milestone overview
| # | Milestone | Workstream |
|---|---|---|
| P0 | Orientation: project state, reference images, decisions confirmed | all |
| P1 | `Track_expo_v1` frozen copy as a separate scene; `Track_expo` environment audit + top fixes, reference-angle approval | A |
| P2 | Realistic quad pipeline (Blender or Unity generators) + all 6 classes | C |
| P3 | 3D track system, HoloRing, Holo Halo, Sphere Crest, reworked `Track_expo` route | B |
| P4 | Bot core + racing-line baker + race mode with difficulties | D |
| P5 | Strike Range scene, targets, scoring, offline rounds with bots | E |
| P6 | Online sessions (LAN + Relay), synced targets/hits, scoreboards | F |
| P7 | Polish, performance, reports | all |

## Risks
| Risk | Mitigation |
|---|---|
| Bots fly "unfairly" or unrealistically | They only produce stick inputs through the same flight controller and physics; tests check lap-time ordering and missed gates |
| Vertical sections confuse the guidance | 3D chevrons, up/down next-gate arrows, holo-ring beacons; playtests with beginners |
| Environment upgrades kill the frame rate | The audit includes a cost estimate per fix; profile after each fix; quality presets |
| Network desync of targets/hits | Deterministic target paths + host-validated hit events; tests with simulated latency/packet loss (Unity Transport network simulator) |
| Online service costs or account setup | LAN mode as the default for classrooms; Relay only when needed; the free tier covers small classes |
| Real-world IP (quads, vehicles, buildings) | Generic designs, fictional brands; `Track_expo` keeps whatever mode (Inspired/Faithful) it was built in |
| Reworking `Track_expo` breaks things that depend on it (menus, saved times, ghosts, tests) | Work on a branch; the first version lives on as the self-contained scene `Track_expo_v1` with the old records; the new `Track_expo` gets a new track ID; run all tests and play both tracks from the menu after each change |
| Shared assets changed for `Track_expo` silently alter `Track_expo_v1` | v1 uses its own copies of everything A/B modify; screenshot comparison of v1 before vs after each milestone |


## Milestone plan (agent, P0)
| # | Scope | Main steps | Done when |
|---|---|---|---|
| P1a | Freeze `Track_expo_v1` | Editor tool `TrackFreezer`: copy `Track_Expo.unity` → `Scenes/Track_Expo_v1.unity`; copy every generated asset it depends on (meshes, materials, textures, skies, volume profiles) into `Assets/_Project/Tracks/Track_expo_v1/` and remap every object reference in the copied scene (components, `InstancedProps` groups, lighting presets, `RenderSettings.skybox`) to the copies; environment `expo_v1`; the existing ids (`expo-astana`, `expo-pro`, `expo-free`) move to it as "Expo Classic …" so the records, ghosts and online boards stay with them; reference screenshots | v1 loads from the menu, screenshots match, tests green; commit `P1a` |
| P1 | Environment audit + fixes | `Docs/track_expo_quality_audit.md` (15–25 ranked gaps with costs), fixes in rank order in the generators, colour calibration against the photo (region means), profiling after each group | reference-angle screenshot, perf per preset; commit `P1` |
| P2 | Realistic quads | `Docs/quad_reference.md`; Blender 3.3 headless generator `Tools/Blender/quadgen.py` (bevels, UVs, glTF) driven by the garage part data; Unity import step → prefabs with sockets; 6 classes + analog / HD; prop blur disc; tests (wheelbase, prop clearance, mass sum) | garage turntable screenshots per class; commit `P2` |
| P3 | Vertical route | `Docs/track_expo_route_v1.md`; route roll / up vectors, gates on the 3D tangent; holo chevrons, up/down arrow, holo-ring beacon; HoloRing gate, Holo Halo, Sphere Crest; reworked Sprint (≤ 60 m, climb up through the Halo) and Pro (crest over the sphere, Halo dive, ≥ 3 more vertical moves) with new ids | both routes flown, lap times within ±30 %, v1 unchanged; commit `P3` |
| P4 | Bots + race mode | `Ushqyr.AI`: planner, trajectory follower, stick synthesiser (inverse rates), `BotDifficultyProfile`; racing-line baker; race mode with 1–7 bots, levels incl. Adaptive, ghost / collision option, results + replay | acceptance tests (Elite all laps on 10 seeds, ordered lap times, Adaptive ±5 %, 7-bot fps); commit `P4` |
| P5 | Strike Range offline | `Range_Steppe` scene generator, fictional vehicle silhouettes, deterministic target movers, rules + scoring with tests, intercept-planner bots, lobby / HUD / scoreboard | separated bot scores over 10 rounds; commit `P5` |
| P6 | Strike Range online | LAN / direct IP + Relay sessions on the existing Netcode stack, host-validated hits, simulated latency tests | message / validation / latency tests; LAN test instructions; commit `P6` |
| P7 | Polish + report | Profile every scene × preset, `Docs/phase3_report.md` | report |

## Decisions
| Date | Milestone | Decision | Why |
|---|---|---|---|
| 2026-11-04 | P0 | The owner asked to continue without stopping at the prompt's ask-points ("Continue steps yourself, do not wait for me"): proposals, audit lists and screenshots go into the docs and the work goes on. Anything that could cost money (Unity Cloud linking, paid services) still waits for the owner | Owner instruction |
| 2026-11-04 | P0 | `Track_expo` = `Assets/_Project/Scenes/Track_Expo.unity` (environment `expo`), the only candidate. It was built in **Faithful** mode (owner's TRACK_EXPO_PLAN E0 decision) and stays Faithful | Prompt: keep the mode it was built in |
| 2026-11-04 | P0 | `Track_expo` already has a beginner variant (Expo Sprint) and an advanced one (Expo Pro), plus free flight: B3 applies per variant (gentle Sprint, full Pro) | Plan B3.3 |
| 2026-11-04 | P0 | Records move to v1 by keeping the ids: `expo-astana`, `expo-pro` and `expo-free` become the v1 tracks ("Expo Classic Sprint / Pro / Free Flight", environment `expo_v1`); the upgraded routes get new ids in P3 | Local times, ghosts and online boards are keyed by track id, so nothing has to be migrated |
| 2026-11-04 | P0 | Blender 3.3.0 (installed) is used for P2 instead of 4.x LTS; the generator needs bmesh, bevel / UV operators and the glTF exporter, all present in 3.3 | Available on this PC |
| 2026-11-04 | P0 | `FLIGHT_PHYSICS.md` is not in the repo; `Scripts/Flight` + `PLAN.md` are the physics reference, as in earlier phases | Missing file |
| 2026-11-04 | P1a | `Track_Expo_v1` frozen by `TrackFreezer.Freeze()` (refuses to run twice): scene copy `Scenes/Track_Expo_v1.unity`, 404 generated assets copied to `Assets/_Project/Tracks/Track_expo_v1/` (meshes, library materials, procedural textures, sky materials, volume profiles; sub-folders kept), 2 680 object references in the copied scene and copies remapped (components, `InstancedProps` groups, lighting presets, reflection probes, `RenderSettings.skybox`); environment `expo_v1`, added to the build settings | Prompt P1 step 1 |
| 2026-11-04 | P1a | Stay shared, by design: downloaded CC0 textures (.jpg), HDR skies, FBX models, shaders (a copy would clash by shader name), scripts, and the obstacle / route kit (prefabs + their meshes, reached through `TrackPrefabs`: 128 assets). The kit is engine-level and P3 only adds new gate types; changing an existing kit gate would show in v1 and is not allowed from now on | "Engine-level shared code (flight model, quads, obstacle kit code) stays shared" |
| 2026-11-04 | P1a | The three Expo tracks keep their ids in v1 as **Expo Classic Sprint / Pro / Free Flight** (`expo-astana`, `expo-pro`, `expo-free`), frozen as JSON in `Tracks/Track_expo_v1/Data/` (catalogued from there; `RouteLibrary` no longer writes them). Their local times, ghosts and online boards (`lap-expo-astana`, `lap-expo-pro`) therefore stay with v1. `Track_Expo` now runs the same routes under new ids `expo-v2-sprint`, `expo-v2-pro`, `expo-v2-free` with fresh records (P3 reworks them); new board files `lap-expo-v2-sprint.lb`, `lap-expo-v2-pro.lb` need a deploy | Records keyed by id: nothing to migrate |
| 2026-11-04 | P1a | Verified: the v1 bookmark screenshots are pixel-identical to the original `Track_Expo` (mean difference 0.00 on all 9 bookmarks; `Docs/screenshots/track_expo_v1/reference/`); Expo Classic Sprint loads from the catalogue into `Track_Expo_v1` and flies a clean autopilot lap in 205.7 s; 222 tests pass | Prompt P1 step 1 |
| 2026-11-04 | P1 | Audit (`Docs/track_expo_quality_audit.md`, 20 ranked gaps) fixed in rank order for the cheap, high-impact items: a photo-matched bookmark `expo_photo`, a procedural summer sky with a cool haze, brighter roofs and glass, the day grade, light asphalt, lighter lawns and paving, white/silver cars, sky-reflecting pools, blue solar cells, and an Astana palette for the city (variants per building picked by OSM id) and the far ring. Calibration within 0.89–1.07 of the photo on sky, grass, roofs, asphalt and the whole frame (v1: 0.66–1.14). Façade/roof detail, trees, traffic, SSAO and clouds are postponed (listed in the audit). 87.6 / 75.1 fps Low / High | Biggest visual gaps per hour first |
| 2026-11-04 | P1 | Expo screenshots now apply the scene's own day preset before capturing (sky, ambient, reflection probes). Before that, the Editor's ambient carried over from the previously open scene and the realtime probes had never rendered, so two captures of the same scene could differ by 2–11 / 255. The v1 reference set was re-taken this way (v1's files are unchanged in git), and two v1 captures with a `Track_Expo` rebuild and capture in between are pixel-identical | The v1 freeze check must be repeatable |
| 2026-11-04 | P2 | Quad parts are generated in Blender 3.3 headless by `Tools/Blender/quadgen.py` from a spec the Unity exporter writes out of the garage part data (`QuadPartGenerator`, Ushqyr > Quads > Generate Quad Parts): 9 frames (carbon plates and arms with chamfers, motor-mount and lightening holes, standoffs, countersunk screws, camera side plates, TPU arm guards and antenna mount; ducted frames with ducts, lips, struts and the whoop canopy), 8 motors (base and tabs, stator with windings, vented bell, shaft and nut), 8 props (twisted airfoil blades from diameter / pitch / blade count) each with a blur disc, 9 batteries (pack or 18650 cells, leads, XT60 / XT30 / BT2.0 plug, balance lead, strap, grip pad), and the shared electronics (30.5 / 25.5 / 20 stacks with components and grommets, micro / nano / HD cameras, analog pagoda VTX, HD air unit with two antennas, receiver with T antenna, capacitor, action camera). 53 meshes, 160k vertices | Parametric from the same numbers the physics uses |
| 2026-11-04 | P2 | Blender writes a small JSON mesh format (Unity axes, one index list per material slot, named sockets) instead of FBX / glTF; the importer welds corners and makes mesh assets in `Art/Quads/Generated/` plus `Resources/QuadPartLibrary.asset`. `QuadModelBuilder` assembles the detailed parts on the frame's sockets whenever a build's frame, motor, prop and battery are in the library (the old procedural model stays as the fallback); slots map to the build's style and accent (bell, TPU, props) and fixed colours (PCB, wires, plug, rubber). Props on the counter-clockwise motors are mirrored; blur discs feed `PropVisuals` in flight | No FBX axis / unit conversion to get wrong, and no importer package needed |
| 2026-11-04 | P2 | New extra **Digital HD video** (`extra-hd-system`, 3"/5"/7"): HD camera module + air unit with two antennas instead of the analog camera and pagoda VTX (+24 g, a little drag). Analog stays the default | Plan: analog and digital-HD variants |
| 2026-11-04 | P2 | Tests (`QuadPartTests`): every catalogue part has a mesh; frame sockets match the wheelbase (±1 %) and the plate height; prop tips and blur discs match the diameter; on every shipped build the props don't overlap and sit on their motor sockets; the part masses sum to the flight profile's mass. Turntable shots of the six classes in `Docs/screenshots/quads/P2/`. No reference photos of your own quads were provided (`Docs/reference/quads/` is empty), so the side-by-side comparison with real photos is still to do; wiring along the arms and LED strips on the generated parts are not modelled yet | Prompt P2 acceptance |
| 2026-11-04 | P3 | Route system in 3D: `TrackRoute.Frame` gives the full tangent (vertical and inverted sections), up carried by parallel transport through steep parts and world-up elsewhere, plus the knots' interpolated `Roll` (180° = inverted). `RouteObstacle.Align3D` turns an obstacle with that frame and centres its opening on the line; checkpoints already test the pass direction against the gate's own plane, so they work in any orientation. Old tracks (no roll, no Align3D) build exactly as before. Tests: `Route3DTests` (vertical climb frame, inverted loop top, flat routes unchanged, a ring on the climb faces up and only counts upwards) | Plan B1 |
| 2026-11-04 | P3 | New kit types **HoloRing** (6 m light ring, scalable) and **HoloHalo** (35 m, turning, four projector beams), with the `Ushqyr/Hologram` shader (additive, Fresnel, scanlines, flicker, glitch) driven by the same `_BaseColor` / `_Intensity` as gate LEDs. No collider; touching a rim glitches the ring and costs **+0.5 s** (`LapTimer.AddPenalty`, HUD "RING TOUCHED +0.5 s"). Sparkle particles are not added | Plan B2 |
| 2026-11-04 | P3 | 3D guidance (air courses, guidance on): a pulsing holo ring round the next gate (overhead or below), the light column only for low upright gates, six holo chevrons floating along the line ahead (route frame), and the next-gate marker shows ↑/↓ with the height difference. `Track_Expo_v1`'s beacon has no hologram material, so it keeps the E6 column only (its look is unchanged; v1 screenshots still pixel-identical) | Plan B1 guidance |
| 2026-11-04 | P3 | Reworked routes (new ids, fresh records; see `Docs/track_expo_route_v2.md`): **Expo Sprint v2** 1 533 m (+10 %), top 63 m: the courtyard hop, the low orbit, a climb straight up through the Holo Halo (50 m), back down the axis through the slalom. **Expo Pro v2** 2 864 m (+23 %), top 113 m: the courtyard hop, the Sphere Crest (flank rings, crest ring at 113 m over the turbines, dive series, pull-out at 6 m), under / over / straight down through the Holo Halo, the monument-top dive, the ladder (three stacked rings), a split-S from a high ring, the sweeper and the second courtyard. Both clear the scenery by 2 m everywhere. The proposal was not held for approval (owner's instruction); a power loop with an inverted top is left to the P4 bots | Plan B3 |
| 2026-11-04 | P3 | Autopilot fixes found on the Halo climb (it circled 5 m under the ring): it commits to a gate a metre before its approach point, slows as the target comes overhead or underneath and slides onto the axis with pitch and roll there, measures alignment to a ring by the distance from its axis, and a progress watchdog rescues it when no gate is passed for 45 s. The lap-time runs of the new routes were skipped at the owner's request, so the lap times are **not measured yet**; by length the estimate for a 5" pilot is ~70 s (Sprint) and ~130 s (Pro), within ±30 % of the old routes | Owner: "skip runs" |
| 2026-11-05 | P4 | Bots (`Scripts/AI`, see `Docs/bots_p4.md`) fly only through stick inputs into the same flight controller and physics as the player: a baked racing line per track and size class (minimum curvature inside the gate openings and the corridor, clearance-checked against the real geometry, speed profile from the quad's flight envelope with braking / acceleration caps and a turn-rate limit), a line planner (pace, wander, mistakes, reaction, avoidance, rejoin) and a trajectory follower with inverse rate curves. Levels Rookie / Amateur / Pilot / Pro / Elite / Adaptive. Race mode: 1-7 bots on the grid, live positions and gaps, optional midair collisions, results with every racer, winner replay. Acceptance: Elite completes every lap of all ten courses on 10 seeds without a missed gate, lap times ordered by level, Adaptive within 5 %, 7 bots 0.76 ms per frame | Plan D |
| 2026-11-05 | P4 | Bots stay **off by default** (Settings > Race) until race mode is played through in a real session: Spline Test Loop and Expo Sprint v2 still see a crash on some seeds, and the lower levels crash more on the Expo courses | Not shipping a half-finished opponent to new players |
| 2026-11-05 | P4 | First measured lap times of the v2 Expo routes (Elite bot, flying lap): Expo Sprint v2 ~65 s, Expo Pro v2 ~114 s (Classic: 60 s / 97 s) | P3 left them unmeasured |
| 2026-11-05 | P5 | Strike Range offline (see `Docs/strike_p5.md`): generated `Range_Steppe` (1.4 km terrain, 4 roads, 26 static spots, revetments, berms, hulks, tower, tent, 3 pads with countdown lights, day / overcast / dusk), deterministic target director (static / moving convoys / evasive with smoke, respawn after 10–15 s), scoring with streaks and speed bonus, intercept-planner bots (0–3), HUD with radar and off-screen markers, results and a local board per difficulty, lobby on the *Strike Range* venue page. Bot levels separate over 10 rounds: Rookie 566, Pilot 1 070, Elite 1 661 mean points | Plan E |
| 2026-11-05 | P5 | Wind for Normal / Hard is defined but **not applied**: the flight model has no wind input. Deferred to P7 so the race physics stay untouched | Keeping the race results comparable |
| 2026-11-05 | P6 | Strike Range online (see `Docs/strike_online_p6.md`) on the existing race network: the host sends the round setup (seed, difficulty, bots) before the load, every PC runs the same target director on the shared clock, pilots claim hits and the host's referee validates them (target life, still up, ≥ 8 m/s, claim ≤ 1 s old, within 7 m of the target at that time); first valid claim wins; the host keeps every score and sends each change; host bots fly on the host and their poses go out at 10 Hz | Plan F |
| 2026-11-05 | P6 | **LAN / direct-address sessions** added beside the Unity sessions (*Host a LAN race* / *Join LAN*, UDP 7777): classrooms without internet or Unity accounts. Also `-lan-host` / `-lan-join` for two-copy smoke tests | Plan F: "Direct IP / LAN mode for classrooms" |
| 2026-11-05 | P6 | Strike difficulty and bots for an online round come from the host's own *Strike Range* page, not from extra rows in the online lobby | Keeps the lobby page as it is; one place to set the range |
