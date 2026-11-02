# Prompt for the Claude Code agent: stadium track, finish cube, garage (desktop PC)

> Before pasting:
> 1. Put `TRACK_GARAGE_PLAN.md` in the project root (next to `PLAN.md`, `FLIGHT_PHYSICS.md`, `VISUALS_PLAN.md`).
> 2. Save the three reference pictures as `Docs/reference/ref_track_stadium.png`, `Docs/reference/ref_finish_gate.png` and `Docs/reference/ref_garage.png`.
> 3. Paste everything below the line into Claude Code on the PC with the project.

---

You are a senior Unity gameplay and technical-art engineer continuing **GateRush**, an FPV drone racing simulator inspired by VelociDrone. **Important: we dropped VR.** The game is now a **desktop PC simulator** (Windows, monitor, played with an RC radio such as a BetaFPV, a gamepad or a keyboard). Wherever older docs mention VR, XR, Quest, Link, goggles mode or comfort settings, that is obsolete.

## First: get oriented (don't skip)
1. Read `TRACK_GARAGE_PLAN.md` (the source of truth for this work), then `PLAN.md`, `FLIGHT_PHYSICS.md` and `VISUALS_PLAN.md`.
2. **Open and look at the three reference images** in `Docs/reference/`. Describe to me in a few lines what you see in each, so I can confirm we see the same thing.
3. Load the `unity-cli` skill, then run `unity status`. Inspect the current project: scenes, scripts, packages, existing track/race/checkpoint code, the `QuadProfile` and flight model, the UI system in use, and the git status. Then write `Docs/project_state.md` (what exists, what's missing, what conflicts with the plan).
4. Post a short plan for milestone T0, then start.

## Rules
- **Unity CLI and live Editor.** If an Editor is connected, drive it with `unity command` / `unity command eval`, and never hand-edit `.unity`, `.prefab`, `.mat` or `.asset` YAML. Build scenes, prefabs and generated content with **repeatable Editor scripts** in `Assets/_Project/Editor/` (menu items + `-executeMethod`). Use `unity-package-management` for packages (`com.unity.splines`, `com.unity.cinemachine`), the `ui` / `ui-uitk` skills for all menus and HUD, `urp-postprocessing` for bloom/DoF/color grading, and `physics-3d-collision` when colliders or triggers misbehave.
- **Don't break the flight model.** The garage feeds the existing `QuadProfile`; don't change physics formulas unless a test proves a bug. Keep all existing tests green. Add EditMode/PlayMode tests for anything new with logic: track generation, checkpoint ordering, lap validation, build stats, save/load.
- **No copied branding.** The references contain other companies' logos ("EREADRONE", "DRL"). Use our own brand "GATERUSH" and fictional part brands. Reproduce the **structure, layout and mood**, not the logos.
- **Licensing.** Use only CC0 assets (Poly Haven, ambientCG, …) or procedural geometry you generate. Log every asset in `Assets/_Project/Art/ASSET_LICENSES.md`.
- **Performance (desktop):** ≥ 144 fps on an RTX 3060/4060-class PC at High; ≥ 60 fps on a GTX 1060/1650-class PC at Low.
  - LEDs are HDR emissive + bloom, never one light per LED.
  - Use GPU instancing for repeated items (chevrons, posts, seats) and combined meshes per lane segment.
  - LOD0/1/2 on everything. Colliders are simple primitives, never mesh colliders on obstacles.
- **Every screen works with mouse, gamepad and RC radio** (sticks navigate, a switch confirms/backs).
- **Visual verification.** At each visual milestone, render screenshots from camera bookmarks to `Docs/screenshots/<milestone>/`, open them yourself, compare them with the matching reference image, and tell me what matches and what doesn't yet.
- **Commits:** one per milestone, `T<n>: <summary>`. Use LFS for binaries.

## Milestones (details in TRACK_GARAGE_PLAN.md §6)
### T0: Desktop platform reset
- Remove XR Plugin Management, OpenXR, Meta XR SDK, XR rigs, OVR components and XR-only input actions. Make sure the project compiles and the old scenes still open.
- Cameras with Cinemachine:
  - **FPV** camera on the quad: tilt 0–60°, FOV 90–150°, optional lens distortion;
  - **chase** camera;
  - **LOS spectator** camera;
  - a switch key/button/radio switch between them.
- Screen-space OSD-style HUD (timer, lap, speed, battery voltage, armed state, next-gate indicator).
- Windows x86_64 build target. Quality presets Low/Medium/High/Ultra; VSync and frame-cap options.
- Update `PLAN.md`'s platform note to "desktop PC, no VR" if it still says otherwise.

### T1: Spline-based track system
- `TrackDefinition` (ScriptableObject + JSON) as specified in §2.2. `TrackBuilder` generates:
  - lane LED lines (inner warm white, outer red) with posts, clamps and cables;
  - centre chevron markers;
  - supports/truss bridges under elevated sections;
  - obstacles by distance along the route;
  - directional checkpoint triggers in order;
  - ground decals.
- Generation is deterministic and can be rerun: `Tools > Track > Rebuild`.
- Tests:
  - checkpoints are sorted by distance along the route;
  - a lap counts only if every checkpoint was passed in order, in the right direction;
  - JSON round-trips.

### T2: Detailed obstacle kit
- **Procedural box truss generator** (chords, zig-zag diagonals, end plates, bolts/couplers).
- On top of it: padded gate covers (fabric, stitched-seam normal map, straps), LED channel strips, bases with sandbags/barrels, safety nets (alpha-tested), chevron boards, feather flags (wind), landing pads, start pad.
- Obstacle prefabs: single gate, stacked double gate, slalom series, cube gate, ladder/H hurdle, dive gate, elevated ramp gate, optional arch.
- Each obstacle exposes a `NextGateHighlight` (LED intensity/pulse) and a checkpoint trigger.
- Build an `Obstacle_Showroom` scene showing them all, take screenshots, and review them yourself.

### T3: Stadium environment
- Striped pitch with line markings, a red tartan running track with lanes, stands with GPU-instanced seats, dugouts, perimeter LED boards with an animated GATERUSH brand, and floodlight towers.
- Lighting presets **Night (default) / Dusk / Day**, switchable from the menu.

### T4: "Serpent Arena" layout, reproducing `ref_track_stadium.png`
1. Trace the route from the picture:
   - find the pixel coordinates of the pitch corners or penalty-box corners;
   - compute a **homography** to pitch coordinates (105 × 68 m);
   - trace about 60–120 points along the corridor centre and map them to world positions;
   - fit a closed spline.
   Write the tracing tool as an Editor script that takes pixel points from a JSON file you produce by examining the image, so it can be repeated.
2. Heights: 3–4 m on the 2–3 overpass sections where the route crosses itself, 1.2–2 m elsewhere, with smooth ramps.
3. Place about 20–25 obstacles in the same spirit as the picture:
   - red single/paired gates on the loops;
   - green cube gates and ladder hurdles on the straights and S-bends;
   - ramp gates on the overpasses;
   - landing pads inside the two big loops;
   - the start pad on the right side;
   - **the finish cube (T6) as the last obstacle**.
4. Target a lap length of 450–650 m. Fly it in Play mode with the default 5" build and report the lap time.
5. Render a screenshot from a high camera at roughly the same angle as the reference, and compare them side by side.

### T5: Route guidance (this game teaches beginners)
Settings → Route guidance:
- **Off;**
- **Standard** (default): the next gate glows and pulses, passed gates dim, the gate after next is outlined;
- **Trainer:** a floating racing-line ribbon coloured by recommended speed, plus an off-screen arrow to the next gate.

Also: a missed-gate warning with a go-back arrow, and a "Ghost lanes" option (no lane-post colliders).

### T6: Finish cube + finish moment, matching `ref_finish_gate.png`
- A black square-truss cube (~4 m) with nets on the sides, red padded tubes on the bottom edges, a white glowing **hexagonal frame** on the entry face, and an open back. Crossing it finishes the race.
- **Finish moment:**
  1. 0.5 s slow-mo, then auto-hover.
  2. A Cinemachine shot from inside the cube looking out through the hexagon at the quad, with light DoF.
  3. A results overlay (UI Toolkit): race time, personal best + local leaderboard rank, crashes/resets, top speed, percentile vs history, lap bars (fastest green / slowest red), distance, and **Restart / Exit / Next** buttons.
- Use the reference's layout and our branding.

### T7: Garage scene and UI, matching `ref_garage.png`
- **Workshop:** a workbench with a blue self-healing cutting mat (generate our own grid texture), pegboard, tools, shelves with LiPo bags, warm lights, background depth of field.
- **Turntable camera:** orbit, zoom, and a Spin toggle.
- **UI layout per §4.2:**
  - top tabs **Frames · Motors · Props · LiPos · Extras · Style · Color**, plus **Back** and **Save drone**;
  - left panel: editable name, stat bars with green/red delta preview, Drag model and MOI toggles, and **Spin / Data / See / Fly** buttons;
  - right panel: size filter + part grid with paging;
  - bottom-right: selected-part card.

### T8: Parts system linked to the physics
- Add `FrameDef`, `MotorDef`, `PropDef`, `BatteryDef`, `ExtraDef`, `StyleDef` and `ColorDef` ScriptableObjects.
- **Procedural part models** (frame by geometry type/arm length, motor by stator size, prop by diameter/pitch/blades, LiPo with our wrap, extras), all attached at frame sockets.
- **Auto-rendered thumbnails.**
- `BuildComposer` → `QuadProfile` (mass, CG, inertia tensor, drag areas, motor/prop/battery parameters). Block invalid combinations.
- **Stats from the physics code** (§4.3): static thrust, weight, top speed (headless level-flight sim, cached), torque, RPM, peak temp (estimate), T/W, hover throttle, flight time. Delta preview on hover.
- Ship 6 preset builds; save/load JSON.
- Tests:
  - the stat top speed is within 3% of the in-flight top speed;
  - weight equals the sum of the parts;
  - presets load.
- **Fly** → track select → spawn on the start pad with exactly that profile.

### T9: Polish and report
- Profile every scene at every preset, fix the hot spots, and verify mouse, gamepad and radio on every screen.
- Write `Docs/track_garage_report.md`: screenshots vs references, performance numbers, the asset license list, known gaps.

## Stop and ask me
- After step 2 of orientation, to confirm your reading of the reference images.
- When you need my judgement on look or feel (e.g. after T4, T6 and T7 screenshots).
- When an asset isn't CC0 or our own.
- When a feature from the plan conflicts with existing code in a way that needs a design decision.

Otherwise decide, note the decision in `TRACK_GARAGE_PLAN.md` under "Decisions", and keep going.
