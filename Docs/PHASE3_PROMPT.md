# Prompt for the Claude Code agent: Phase 3 (desktop PC)

> Before pasting:
> 1. Put `PHASE3_PLAN.md` in the project root.
> 2. Optional but recommended: put photos of your own real quads (top, side, front, 3/4 views, with a ruler) in `Docs/reference/quads/`, and install **Blender 4.x LTS** (free).
> 3. Paste everything below the line into Claude Code on the PC with the project.

---

You are a senior Unity engineer and technical artist continuing **GateRush**, a **desktop PC** FPV drone simulator (no VR) used to teach many people to fly. This phase has six workstreams, described in `PHASE3_PLAN.md`:
- **A:** environment quality of the **existing `Track_expo` track**;
- **B:** vertical movement in the **existing `Track_expo` route** (HoloRings, the Holo Halo, flying over the sphere);
- **C:** realistic quad models;
- **D:** racing against bots;
- **E:** the Strike Range 2-minute target mode with bots;
- **F:** online multiplayer for the Strike Range.

**Important: A and B are about the track that already exists in the project, `Track_expo`.**
- Find it first. The scene may be named `Track_expo` / `Track_Expo`; it's the EXPO/sphere track that `TRACK_EXPO_PLAN.md` calls "Sphere District".
- Work **in that scene and on its existing route**. Don't create a new scene or a new track, don't rename anything, and don't rebuild it from scratch. Improve its existing generators, prefabs, materials and route data in place.
- If you find more than one candidate scene, or none, stop and ask me.
- **The first version must survive as its own scene, `Track_expo_v1`.** Create it in P1 step 1 before changing anything, and never modify it afterwards.

## Process
- Use the **superpowers** skills if installed:
  - `superpowers:writing-plans` to turn each milestone into small tasks;
  - `superpowers:test-driven-development` for all logic (track math, racing-line baker, bot controller, scoring, hit validation, networking messages);
  - `superpowers:systematic-debugging` for bugs;
  - `superpowers:dispatching-parallel-agents` / `subagent-driven-development` for independent work (A and C can run in parallel);
  - `superpowers:verification-before-completion` before you report a milestone as done.
- Load `unity-cli`; run `unity status` before touching scenes; drive a connected Editor with `unity command` / `unity command eval`; never hand-edit `.unity`, `.prefab`, `.mat` or `.asset` YAML. Everything generated (environment, tracks, quads, range) comes from **repeatable Editor tools**.
- Other skills to use:
  - `unity-package-management` for packages;
  - `urp-postprocessing` for lighting and post;
  - `physics-3d-collision` for collider/trigger problems;
  - `ui` / `ui-uitk` for all new screens;
  - `setup-multiplayer-services` for workstream F;
  - `build-live-game` only if we add online leaderboards.
- One commit per milestone (`P<n>: <summary>`); LFS for binaries. Keep **all existing tests green**.

## First: orient (P0, don't skip)
1. Read `PHASE3_PLAN.md`, then `PLAN.md`, `FLIGHT_PHYSICS.md`, `VISUALS_PLAN.md`, `TRACK_GARAGE_PLAN.md` and `TRACK_EXPO_PLAN.md`.
2. Inspect the project and update `Docs/project_state.md`: track system (can splines carry roll/up vectors yet?), obstacle kit, guidance, finish cube, garage + part data, quad generators, quality presets, input, and any existing networking. **For `Track_expo` specifically**, list:
   - its scene path;
   - its `TrackDefinition`(s) and route variants (gate count, length, max height, your measured lap time);
   - the generators, prefabs, materials and lighting it uses;
   - its camera bookmarks and tests;
   - everything that references it (menus, leaderboards, ghosts, bots).
3. Look at `Docs/reference/ref_expo_aerial.png` and any photos in `Docs/reference/quads/`.
4. Check whether Blender is installed (`blender --version`), and tell me.
5. Post your milestone plan for P1–P7 and wait for my OK.

## Hard rules
- **Same physics for everyone.** Bots (D, E) produce only `PilotInput` stick values that go through the same flight controller and physics as the player. No kinematic movement or speed cheats.
- **Performance:** 144+ fps on the recommended PC at High and ≥ 60 fps on the minimum PC at Low. This includes 7 bots in a race and 8 players + bots online. Profile every milestone and report the numbers.
- **IP:** no real brands, logos or copied product designs: quads are generic real-world construction with fictional brands; `Track_expo` keeps the mode it was built in (Inspired by default); vehicles are fictional. CC0 or our own procedural/Blender-generated assets only, logged in `Assets/_Project/Art/ASSET_LICENSES.md`; OSM attribution stays.
- **Strike Range is an arcade score-attack mode:**
  - fictional generic vehicles;
  - no real insignia, unit names or countries;
  - no people;
  - stylised hit effects;
  - a hit is simply contact at ≥ 8 m/s;
  - don't model real weapons, payloads, warheads or vehicle weak points.
- **Visual verification:** for every visual milestone, render screenshots from fixed bookmarks, open them yourself, compare them with the references, and tell me what matches and what doesn't.
- **Every new screen** works with mouse, gamepad and RC radio.

## Milestones
### P1: Track_expo environment quality (A)
- Create a git branch `track-expo-phase3`.
- **Step 1: keep the first version as a distinct scene, before changing anything.**
  - Duplicate the current `Track_expo` scene to **`Track_expo_v1`**, a frozen copy of the original route *and* look.
  - Make it **self-contained**: copy every asset it uses that P1 or P3 might change (route `TrackDefinition`s, environment prefabs, materials, shaders, lighting/Volume profiles, lightmaps/probe data, generator settings) into `Assets/_Project/Tracks/Track_expo_v1/`, and repoint the v1 scene to those copies. Engine-level shared code (flight model, quads, obstacle kit code) stays shared.
  - Register it as its own track: menu entry "Expo Classic", its own track ID, and **move the existing `Track_expo` best times, ghosts and leaderboards to it**.
  - Render reference screenshots of `Track_expo_v1` from all bookmarks into `Docs/screenshots/track_expo_v1/reference/`, fly a lap, and run the tests. Commit this step on its own (`P1a: freeze Track_expo_v1`).
  - From now on, **all P1 and P3 changes go into `Track_expo` only**. At the end of P1 and P3, re-render the v1 screenshots and confirm there's no visual change.
- Write `Docs/track_expo_quality_audit.md`: reference-angle and bookmark screenshots of `Track_expo` vs the photo, and a **ranked list of 15–25 visual gaps** with a planned fix and a cost estimate each. Show me the list before fixing.
- Fix in rank order (see the plan's "typical upgrades" table: sphere glass and interior, façade mullions + interior-mapping windows, roofs, paving and road detail, trees and street life, moving ring-road traffic, city façade variety, sky/haze/GI).
- **Colour calibration:** compare the mean colour/brightness of matching regions (sky, grass, roofs, asphalt) between the reference and your screenshot, and tune exposure, white balance and grading until they're close. Report the numbers.
- Profile after each group of fixes. Finish with the reference-angle screenshot for my approval.

### P2: Realistic quads (C)
- **Component checklist** and real dimension tables as in the plan (C1–C2); a doc `Docs/quad_reference.md` with the dimensions you use, per class.
- **With Blender:** a parametric Python generator (run headless: `blender -b -P Tools/Blender/quadgen.py -- <json>`) that builds each part from the garage part data with bevels, UVs and baked normal/AO maps, then exports glTF/FBX into `Assets/_Project/Art/Quads/Generated/`. A Unity import step creates prefabs with sockets. **Without Blender:** upgrade the Unity generators with bevels/chamfers and the same component set.
- All 6 classes:
  - 5" freestyle;
  - 5" race;
  - 3" toothpick;
  - 3.5" cinewhoop;
  - 65/75 mm whoop;
  - 7" long range.
  Plus analog and digital-HD video variants.
- Wiring, straps, antennas, TPU parts, LEDs.
- Prop motion-blur disc at high RPM.
- Tests: wheelbase and prop clearance match the part data; no prop overlap; part masses still sum to the `QuadProfile` mass.
- Garage turntable screenshots of each class next to reference photos.

### P3: Vertical movement in the existing Track_expo route (B)
0. **Document the current route first** (it already lives on unchanged in `Track_expo_v1`, created in P1): write `Docs/track_expo_route_v1.md` with a top-down map, gates, length, max height and the lap time you measure.
1. **Track system:**
   - full 3D spline orientation (roll/up vectors), vertical and inverted sections;
   - gates aligned to the 3D tangent;
   - 3D pass-direction checks.
   Tests for gate orientation on vertical/inverted segments and for checkpoint direction.
2. **3D guidance:** floating holo chevrons, up/down arrows on the next-gate marker, and a pulsing holo-ring beacon.
3. **New obstacles:**
   - the **HoloRing** gate: hologram shader with Fresnel, scanlines and flicker, sparkles, colour by state, no physical collider, and a glitch effect + small time penalty when the rim is touched;
   - the **Holo Halo** set piece, placed in the `Track_expo` scene: Ø 30–40 m, ~60 m above the plaza/park, rotating, with projector beams;
   - the **Sphere Crest** sequence: flank climb, crest gate ~112–115 m above the plinth (above the turbines), a dive series down the far side, a pull-out gate at ~5 m;
   - power loop, split-S, ladder, roof hops, monument-top dive.
4. **Rework the existing `Track_expo` route** (plan section B3):
   - First show me a **proposal**: a top-down map + a side-elevation graph of height along the lap. It must include flying **over the top of the sphere** and then diving down, flying **over the Holo Halo and diving through it**, and at least 3 more vertical moves. It keeps the good parts of the current route.
   - Wait for my OK, then edit the existing `TrackDefinition`(s) of `Track_expo` in place.
   - If the track has beginner/advanced variants, give the beginner one gentle vertical content (≤ ~60 m, climb *up* through the Halo) and the advanced one the full sphere crest and Halo dive (≤ ~115 m). If there's only one route, ask me whether to add an easier variant.
   - Give `Track_expo` a **new track ID/version** with fresh records (the old records stay with `Track_expo_v1`).
   - Check that both `Track_expo` and `Track_expo_v1` load from the menu, that `Track_expo` works with all guidance modes, and that all tests pass. Re-render the `Track_expo_v1` screenshots and confirm they still match its reference set.
   - Fly it in Play mode and report the lap time; target within about ±30% of the old lap time.

### P4: Bots and race mode (D)
1. **Bot core** (`GateRush.AI`):
   - planner interface;
   - trajectory follower (position/velocity PD + feed-forward → thrust vector → attitude → body rates);
   - **stick synthesiser** (inverse rate curve for each rate type → stick deflection; thrust → throttle);
   - `BotDifficultyProfile` ScriptableObjects (pace, line noise, reaction delay, stick noise, mistake chance, recovery, aggression).
   Unit tests for the inverse rate curves (round-trip error < 0.5%).
2. **Racing-line baker** (an Editor button, a cached asset per track × quad class): minimum-curvature line inside the gate openings, plus a forward/backward speed profile limited by the `QuadProfile` (thrust-to-weight, drag, max lateral acceleration). Rebake automatically when a track changes.
3. **Race mode:**
   - 1–7 bots; levels Rookie / Amateur / Pilot / Pro / Elite / **Adaptive** (±5% of the player's recent pace);
   - grid start, live positions and gaps;
   - midair collisions on/off (ghost bots);
   - simple avoidance;
   - bot names, colours and builds;
   - results with a "watch winner replay" option.
4. **Acceptance tests** (headless):
   - Elite completes every lap of every course (including `Track_expo` and `Track_expo_v1`) on 10 seeds with no missed gate;
   - mean lap times are strictly ordered Rookie > … > Elite;
   - Adaptive stays within ±5% of a recorded pace;
   - the frame rate holds with 7 bots.

### P5: Strike Range offline (E)
1. **`Range_Steppe` scene** (≈1.2 × 1.2 km):
   - terrain with hills and dirt roads, berms/revetments, hulks, a watchtower, a tent;
   - 3 launch pads with countdown lights;
   - day / overcast / dusk presets.
2. **Fictional vehicle models** (Blender or Unity generators; tracked, wheeled, truck, artillery-like and radar-truck silhouettes). Simple compound hitbox colliders. Disabled state (smoke, scorch decal), then respawn after 10–15 s at a new position.
3. **Target behaviours:**
   - static (100 pts);
   - moving on road splines at 10–40 km/h, including convoys (200 pts);
   - evasive: stop-go, reverse, zig-zag, smoke screens (300 pts).
   Moving targets must be **deterministic from a round seed + round time** (needed for F).
4. **Rules:**
   - 120 s round;
   - a hit = contact at ≥ 8 m/s;
   - streak multiplier ×1.1 per hit up to ×2, reset on crash;
   - small speed bonus;
   - relaunch 1.5 s after a hit or crash from the nearest free pad with a fresh battery;
   - range difficulty Easy (static only, no wind) / Normal / Hard (more evasive targets, gusts, smoke).
   Unit tests for the scoring and hit rules.
5. **Bots:** reuse the bot core with an **intercept planner** (predict the target's future position; choose targets by value ÷ estimated time to reach; terrain avoidance) and the D difficulty levels plus target-choice smartness.
6. **UI:**
   - lobby (range difficulty, bot count/levels, quad choice from the garage);
   - HUD (timer, score, streak, minimap/radar, off-screen markers);
   - end scoreboard (hits per class, accuracy, best streak);
   - local leaderboard.
   Acceptance: bot levels give clearly separated average scores over 10 rounds.

### P6: Online Strike Range (F)
1. **Packages:** Netcode for GameObjects + Unity Transport + the Multiplayer Services sessions/Relay package. Check the versions compatible with our Unity version first.
2. **Connection modes:**
   - **LAN/direct IP** (default for classrooms; no account needed);
   - **internet sessions with Relay** (create/join by code, quick-join).
   Stop and ask me before linking the project to a Unity Cloud project or enabling any paid service.
3. **Topology:** host-client.
   - Each client simulates its **own** quad locally and sends state at 30 Hz; remote quads are interpolated.
   - Bots run on the host.
   - Targets are computed locally from the shared seed + clock.
   - **Hits are claimed by clients and validated by the host** (target active, position/time tolerance); the first valid hit wins.
4. **Session flow:** lobby, ready-up, host settings, round, synced scoreboard, rematch; disconnect and host-leave handling.
5. **Tests:**
   - message serialisation;
   - hit-validation rules;
   - simulated latency/packet loss with the Unity Transport network simulator (100 ms, 2% loss): no disputed hits, identical final scoreboards.
   - Then a real LAN test on 2 PCs, which I'll run with your instructions.

### P7: Polish and reports
Profile every scene/mode at every preset, then write `Docs/phase3_report.md`: screenshots vs references, bot lap-time and score tables, network measurements (bandwidth per player), the asset licence list, and known gaps.

## Stop and ask me
- After P0 (milestone plan, plus your `Track_expo` inventory), after the P1 audit list, after the P1 final screenshot, after the P2 class screenshots, **after the P3 route proposal (before building it)**, and after the P3 lap times.
- Before linking Unity Cloud services or enabling anything that could cost money.
- If a requirement here conflicts with existing code in a way that needs a design decision.

Otherwise decide, record the decision in `PHASE3_PLAN.md` under "Decisions", and keep going.
