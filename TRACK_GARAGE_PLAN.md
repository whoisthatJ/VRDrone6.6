# Plan: Stadium Track, Finish Cube and Garage (desktop PC)

> **Platform: desktop PC simulator, no VR.** The game runs on a Windows PC with a monitor. Players fly with an RC radio (BetaFPV etc. over USB/Bluetooth HID), a gamepad or a keyboard. All XR packages and VR features are removed (see §0).

## 0. Platform reset: what changes now that VR is gone
| Area | Before (VR) | Now (desktop) |
|---|---|---|
| Packages | OpenXR, XR Plugin Management, Meta XR Core SDK | **Remove** them (and the XR rig, OVR components, XR input actions). Keep URP, Input System, Splines, Cinemachine |
| Cameras | Goggles / Immersive / LOS rigs | **FPV camera** (full-screen; tilt 0–60°, FOV 90–150°, optional fisheye/"True Lens" style distortion), **chase cam**, **LOS spectator cam** (fixed pilot spot), **replay cams** (Cinemachine) |
| UI | World-space panels | Screen-space **UI Toolkit** menus (mouse + gamepad + radio navigation) and a screen-space **OSD-style HUD** |
| Frame rate | 72–90 Hz VR | **Uncapped / 144+ fps target** on the recommended PC, **≥ 60 fps** on the minimum PC. VSync and frame-cap options. Low input latency matters more than eye candy |
| Post-processing | Comfort-limited | Allowed: tonemapping, color grading, bloom (LEDs), SSAO, optional motion blur (off by default in FPV), depth of field in the **garage and replays only**, optional analog-FPV camera effects (noise, scanlines) |
| Comfort features | Vignette, horizon line | Removed (or kept as optional HUD aids) |

## 1. Reference images
Save the three pictures to `Docs/reference/` as:
- `ref_track_stadium.png`: the stadium track (picture 1)
- `ref_finish_gate.png`: the final obstacle and finish screen (picture 2)
- `ref_garage.png`: the quad builder garage (picture 3)

The agent opens and compares against them at every visual milestone.

**Branding:** the pictures show other companies' logos ("EREADRONE" boards, "DRL"). **Don't copy any logo or brand name.** Use our own brand (e.g. "GATERUSH") on ad boards, banners, flags and LiPo wraps. Part brands are fictional too.

---

## 2. Feature A: the stadium track ("Track_Stadium", working title "Serpent Arena")

### 2.1 What picture 1 shows (the structure to reproduce)
- **Venue:** a football stadium. Striped grass pitch (105 × 68 m) with white line markings, a red tartan running track with lane lines, stands with red seats on two sides, covered blue dugouts, a perimeter of LED advertising boards, and floodlights (implied).
- **Route:** one continuous closed route that winds across the whole pitch:
  - two large **loops** around two black circular landing pads (white centre dots);
  - a **figure-eight crossing** in the middle;
  - a tight **S / double-hairpin** section at the left end;
  - an extra loop at the far (top) end;
  - **2–3 places where the route crosses over itself** using elevated sections (ramps up to about 3–4 m, back down to 1–2 m).
  - Rough count: 6 turns of 180° or more, plus several S-bends.
- **Route marking (the "highlight"):**
  - a **corridor of glowing LED tubes** following the route: **warm-white/yellow inner lines** and **red outer lines**, on low posts, with the corridor about 3–5 m wide;
  - a line of **black-and-white chevron arrow markers** down the middle of the corridor pointing in the flying direction.
  - On elevated sections the lines climb with the route on supports.
- **Obstacles:**
  - **red rectangular gates** (single frames, some in pairs/series);
  - **green cube gates** (scaffold cubes, about 2–3 m);
  - **green ladder / H-frame hurdles** (low "under-and-over" frames);
  - elevated ramp sections;
  - a **start pad** (white mat with green chevrons) next to the route, with the quads parked on it.

### 2.2 How we build it: data-driven, from a spline
- Add `com.unity.splines`. The **route is one closed 3D spline** (centre line, flight height included). Everything else is generated from it, so tracks are cheap to edit and the future track editor reuses the same code.
- `TrackDefinition` (ScriptableObject + JSON export, extending the PLAN.md track JSON) holds:
  - the route spline knots (position and tangents);
  - corridor width;
  - the obstacle list: `{type, distanceAlongRoute, lateralOffset, heightOffset, yaw, scale, color, isCheckpoint}`;
  - the start-pad transform and the finish obstacle;
  - the environment scene name.
- `TrackBuilder` (Editor and runtime) generates:
  1. **Lane lights:** two inner and two outer LED tube lines offset from the spline, posts every 2 m, clamps and a cable between posts. These are combined meshes per 20 m segment.
  2. **Chevron markers** every 3–4 m on the centre line (instanced).
  3. **Supports and truss bridges** under elevated sections, where the spline is more than 1.5 m above ground.
  4. **Obstacles** placed by distance along the route, aligned with the spline tangent.
  5. **Checkpoints:** a directional trigger per checkpoint obstacle, automatically ordered by distance. The finish cube is the last checkpoint.
  6. **Ground decals** (wear marks under gates, landing-pad mats).
- **Tracing the layout from the picture:** project `ref_track_stadium.png` onto the pitch.
  - Pick the 4 pitch corners/lines in pixel coordinates, compute a homography to pitch coordinates (105 × 68 m), and map the traced route points to world positions.
  - Heights come from the visual overpasses: 3–4 m on bridges, 1.2–2 m elsewhere.
  - Lap length target: **450–650 m**; a good 5" lap takes about 40–55 s.

### 2.3 Route highlighting (this is a teaching game, so make it adjustable)
Settings → **Route guidance**, with these options:
| Level | What's shown | For |
|---|---|---|
| Off | Only the physical track (lane lights + chevrons) | Experts, races |
| Standard (default) | **Next gate glows brighter and pulses**, passed gates dim, the gate after next shows a faint outline | Everyone |
| Trainer | Standard + a **floating racing line** (a thin glowing ribbon along the spline, coloured by recommended speed: green fast, yellow brake, red slow) + an off-screen arrow pointing to the next gate | Beginners |

- **Missed gate:** a red flash on the HUD plus a "Missed gate — go back" arrow. The lap only counts if all checkpoints were passed in order.
- **Lane lights glow** through emissive HDR materials + bloom, not real lights. Light probes plus baked emissive GI let them tint the grass at night.

### 2.4 More detailed obstacles (the catalog)
Every obstacle is built from a shared kit of parts, with LOD0/1/2 for all of them.
| Part | Detail level |
|---|---|
| **Box truss** (290 mm square aluminium truss look) | Procedural generator: 4 chords, zig-zag diagonals, end plates, bolts/couplers at joints; black or coloured powder coat with slight wear |
| **Padded covers** | Foam sleeves over the gate frames in red or green fabric (stitched seams in the normal map, Velcro straps, slight sag) |
| **LED strips** | Aluminium channel with a diffuser on the gate's inner edges; HDR emissive, colour per gate; pulses when it's the next gate |
| **Bases** | Steel base plates with sandbags or water-filled barrels; small cable ramps |
| **Netting** | Black safety net panels (alpha-tested) on cube gates and the finish cube |
| **Chevron boards** | Printed aluminium-composite boards on thin poles with small bases |
| **Flags** | Feather/teardrop flags with our brand, cloth or vertex-animated wind |
| **Landing pads** | Rubber mats with a white circle print, slightly raised edges |
| **Start pad** | Grid mat with numbered slots and green chevrons |

**Obstacle types for this track:** single gate, double gate (stacked), gate series (slalom), cube gate, ladder/H hurdle (fly over or under), dive gate (on a bridge, dive down after it), elevated ramp gate, tunnel/arch (optional), **finish cube** (§3).

**Colliders:** simple compound boxes/capsules per truss member, never mesh colliders. Nets are thin boxes that bounce. Lane posts get small colliders, with a "Ghost lanes" option for beginners that turns them off.

### 2.5 Stadium environment
- Pitch with mowing stripes and line markings (decals/texture), a red tartan running track with lane lines.
- Stands: modular seat rows (GPU-instanced seats in red/blue), stairs, roof edge, dugouts. Optional static spectators.
- Perimeter LED boards with our own animated brand content (emissive video/texture scroll).
- 4 floodlight towers (emissive heads + one baked/realtime key light).
- **Time-of-day presets:** **Night (default, so the LEDs pop)**, Dusk, Day.

---

## 3. Feature B: the finish cube (picture 2)
- **Structure:** a large **cube made of black square box truss** (about 4 × 4 × 4 m) standing on the route as the **last obstacle**.
- **Faces:** side faces have **black safety netting**; the bottom edges have **red padded foam tubes**.
- **Entry:** the front face has an inset **white glowing hexagonal frame** (an LED tube, about 2.2 m across), and the quad flies **through the hexagon into the cube and out of the open back** to finish.
- **Details:** bolts, couplers, cable ties on the nets, our brand banner on the top truss beam, and LED tubes slightly brighter than the lane lights.
- **Finish moment** (matches the screen in picture 2):
  1. Crossing the finish trigger → short slow-motion (0.5 s), then the quad switches to auto-hover.
  2. The camera cuts to a cinematic shot **looking out through the hexagon at the hovering quad**, with the truss and nets framing the shot, plus slight depth of field.
  3. **Results overlay:** race time; "New personal best" with the local leaderboard rank; crashes/resets; top speed; percentile vs your history; **lap bars** (fastest lap green, slowest lap red, with times); distance flown; buttons **Restart / Exit / Next**.
- Avatar/level/XP from the picture are **optional** (only if we add a profile system later).

---

## 4. Feature C: the garage (quad selection, picture 3)

### 4.1 Scene and look
`Garage` scene, the hub between the menu and flying:
- A workshop: a **workbench with a blue self-healing cutting mat** (our own grid texture), a pegboard with tools, shelves with LiPo bags and spare props, a soldering station, warm practical lights.
- The background is **blurred with depth of field**.
- The quad stands on the mat. Mouse or right-stick **orbit**, zoom, and an auto-**Spin** toggle.

### 4.2 Screen layout (like picture 3)
| Zone | Content |
|---|---|
| Top bar | **Back** · tabs **Frames · Motors · Props · LiPos · Extras · Style · Color** · **Save drone** (green) |
| Left panel | Editable build name · **stat rows with bars and a live delta preview** (green = better, red = worse when you hover a part): **Thrust (g), Weight (g), Top speed (km/h), Torque (N·m), Drag, RPM, Peak motor temp (°C)**; plus T/W ratio, hover throttle %, estimated flight time · toggles **Drag model** (simple/advanced) and **MOI** (shows the inertia ellipsoid overlay) · buttons **Spin**, **Data** (full spec sheet + thrust/RPM curves), **See** (inspect mode / exploded view), **Fly** (choose a track → go) |
| Right panel | Size filter (**Whoop · 2.5" · 3" · 5" · 7"**) · grid of part cards (thumbnail, name, key stat) with paging |
| Bottom-right card | Selected part: name, size, weight, short description, our fictional brand logo |
| Bottom bar | Current build summary · input device indicator · settings |

### 4.3 The parts system, tied to the physics (FLIGHT_PHYSICS.md)
- **ScriptableObject definitions:**
  - `FrameDef`: mass, arm length/wheelbase, geometry type (true-X, stretch-X, deadcat), per-axis drag areas, base inertia, prop size limit, socket positions.
  - `MotorDef`: stator size (e.g. 2207), KV, Rm, I0, mass, max current.
  - `PropDef`: diameter, pitch, blades, CT/CQ coefficients, mass, inertia.
  - `BatteryDef`: cells, capacity, internal resistance, mass, C-rating.
  - `ExtraDef`: camera, VTX/antenna, action-cam mount; mass, drag and CG offset.
  - `StyleDef` / `ColorDef`: visual only.
- **`BuildComposer`:** build → `QuadProfile` for the flight model (total mass, CG, inertia tensor from the part masses and positions, drag areas, motor/prop/battery parameters). Invalid combinations (prop too big for the frame) are blocked with a message.
- **Stats are computed from the physics, not typed in:**
  - **Static thrust** per motor: solve motor torque = prop torque at full throttle, with battery voltage at 80% state of charge under load, then thrust = kf·ω² (×4 for total).
  - **Weight:** the sum of the parts.
  - **Top speed:** a headless sim of level flight at the best pitch angle until speed stops increasing (uses advance-ratio thrust loss + drag). Cache it per build.
  - **Torque:** the peak motor torque. **RPM:** at full throttle under load.
  - **Peak temp:** a simple thermal model (I²·Rm heating minus airflow cooling). Mark it "estimate".
  - **Hover throttle, T/W, flight time:** from the hover current and the battery capacity.
- **Live delta preview:** hovering a part runs the stat calculation for "current build with this part" and shows the difference.
- **Save / load builds:** JSON in the user folder, plus a list of saved drones. Ship 6 presets: 5" race, 5" freestyle, 3" toothpick, 3.5" cinewhoop, 65 mm whoop, 7" long range.

### 4.4 Part models: procedural, so they always match the physics numbers
- **Frame generator:** a 2D outline (by geometry type + arm length/width), extruded carbon plates with holes, standoffs, top plate. Carbon-weave material.
- **Motor generator:** stator size → bell diameter and height, cooling-slot pattern, magnets ring, shaft, nut.
- **Prop generator:** diameter, pitch and blade count → twisted blade mesh; translucent polycarbonate; a motion-blur disc at high RPM in flight.
- **LiPo:** a rounded box with our printed wrap label and a strap; **Extras** are simple modelled parts (camera, antenna, VTX).
- **Thumbnails:** rendered automatically by an Editor tool (`Tools > Garage > Render Thumbnails`) in a light box.
- You can still add hand-made or purchased models later with the same sockets.

---

## 5. Shared rules
- **Licensing:** CC0 textures/models only (Poly Haven, ambientCG, …) or our own procedural geometry. No copied logos or brands. Log every asset in `Assets/_Project/Art/ASSET_LICENSES.md`.
- **Performance (desktop):**
  - ≥ 144 fps on the recommended PC (RTX 3060/4060-class), ≥ 60 fps on the minimum (GTX 1060/1650-class) at Low;
  - draw calls < 2000 (instancing for chevrons, seats, posts; combined lane meshes);
  - LEDs are emissive + bloom, no per-LED lights;
  - quality presets Low/Medium/High/Ultra.
- **Input:** every garage and results screen works with mouse, gamepad **and RC radio** (sticks navigate, a switch confirms), so radio-only students don't need a mouse.

## 6. Milestones
| # | Milestone | Done when |
|---|---|---|
| T0 | Desktop platform reset | XR packages and rigs removed; FPV/chase/LOS cameras work; screen-space HUD; Windows build runs at 144+ fps on an empty scene; all tests still pass |
| T1 | Track system | Splines installed; `TrackDefinition` + JSON; `TrackBuilder` generates lanes, chevrons, supports, obstacles and checkpoints on a test spline; checkpoint order + lap timing tests pass |
| T2 | Obstacle kit | Procedural truss, padded gates, cube gate, ladder hurdle, dive gate, ramp gate, chevron boards, flags, pads, start pad, all with LODs and colliders; a showroom scene with every obstacle |
| T3 | Stadium environment | Pitch, running track, stands, dugouts, LED boards, floodlights, Night/Dusk/Day presets |
| T4 | Serpent Arena layout | Route traced from the reference (homography), elevation and overpasses, ~20–25 obstacles placed; side-by-side screenshot with `ref_track_stadium.png` from a matching camera angle; full lap flyable; lap time in the target range |
| T5 | Route guidance | Off / Standard / Trainer modes; next-gate pulse; racing-line ribbon with speed colours; missed-gate warning |
| T6 | Finish cube + finish moment | Truss cube with the hexagon LED frame, nets and pads as the last checkpoint; slow-mo, cinematic camera and results overlay matching `ref_finish_gate.png` (with our branding) |
| T7 | Garage scene + UI | Workshop scene with DoF, turntable camera, tabs, stat panel, part grid, info card, Save; matches the layout of `ref_garage.png` |
| T8 | Parts + physics link | Part definitions, procedural part models, `BuildComposer` → `QuadProfile`, stats from physics with delta preview, 6 preset builds, save/load; flying a built quad uses its exact profile |
| T9 | Polish | Performance per preset, input with radio/gamepad/mouse everywhere, before/after screenshot report |

## 7. Risks
| Risk | Mitigation |
|---|---|
| Tracing an angled photo gives wrong proportions | Homography from the pitch markings; then adjust by flying; layout values live in data |
| Hundreds of LED/chevron objects hurt the frame rate | Combined meshes per segment + GPU instancing + LODs; profile in T4 |
| Procedural parts look "CG-cheap" | Bevels, bolts, chamfers, weave normal maps, decals, good lighting and DoF in the garage; leave room for hand-made models |
| Garage stats don't match the in-flight feel | Both come from the same `QuadProfile` and physics code; EditMode test: the stat "top speed" equals the flight-sim top speed within 3% |
| Copying a look too closely (brands) | Our own names, logos and colours; the reference is for structure and mood, not branding |

## Decisions
| Date | Milestone | Decision | Why |
|---|---|---|---|
| 2026-11-02 | — | Work on branch `feature/track-garage` (from `feature/pc-radiomaster-pocket`) | Large multi-milestone change, kept apart from the radio/visuals branch |
| 2026-11-02 | — | New screens (results, garage, HUD additions) use **UI Toolkit**; the existing code-built uGUI menus stay until they are redone | Rewriting every menu up front would delay the plan; both UI systems coexist fine |
| 2026-11-02 | — | `FLIGHT_PHYSICS.md` is not in the repo: `Scripts/Flight` + PLAN.md §4 are the physics reference | Nothing to read otherwise |
| 2026-11-02 | T0 | Cinemachine **6.6.0** (the version for this editor) + Splines 2.9.2; uGUI added explicitly (it only came in through the XR packages) | Removing XRI would otherwise remove uGUI/TextMeshPro |
| 2026-11-02 | T0 | One rendering camera ("Main Camera", CinemachineBrain, cuts only) under a "Camera Rig"; FPV = a CinemachineCamera on the quad's camera mount (`FpvCamera` drives tilt + horizontal FOV), chase = CinemachineFollow (world-up lock) + RotationComposer, LOS = RotationComposer on the pilot stand. The FPV view hides the pilot's quad via the camera's culling mask | Single camera keeps URP/post settings in one place; cuts suit FPV switching |
| 2026-11-02 | T0 | Chase offset scales with the quad class (whoop ≈ 0.3×) | A fixed 5" offset makes a 65 mm whoop a few pixels big |
| 2026-11-02 | T0 | View switch: V, gamepad LB, or a radio switch/button assigned in a new optional calibration step ("camera view"), each flip to "on" cycles FPV → chase → LOS | Radio-only students need a way to switch views |
| 2026-11-02 | T0 | Lens distortion = URP Lens Distortion (−0.35, scale 1.06) on a global volume weighted by the view + setting; off by default | Cheap "wide FPV lens" look, FPV only |
| 2026-11-02 | T0 | OSD/race HUD is a ScreenSpaceOverlay uGUI canvas (visible in every view, hidden while editing a track); crosshair + artificial horizon only in FPV; new `NextGateIndicator` (bracket on the gate + distance, edge arrow when off screen) | Desktop HUD; the T5 route guidance builds on it |
| 2026-11-02 | T0 | Analog video noise is not shown on the desktop (the VTX model still drives the OSD link quality); the setting is hidden | The noise lived in the goggles-screen shader; a screen-space version can come with T9 polish |
| 2026-11-02 | T0 | The track editor keeps its mouse/keyboard mode; its dormant VR branches (gated on `XRSettings.isDeviceActive`) stay until the editor is reworked. `com.unity.modules.xr` (built-in module) stays so they compile | Rewriting the editor is outside this plan |
| 2026-11-02 | T0 | Quality presets **Low** / Medium / High / Ultra (Low: render scale 0.85, SMAA, no shadows/SSAO, sparse grass, LOD bias 0.7) + **frame limit** (VSync, uncapped, 60/120/144/165/240) | §5 performance targets |
| 2026-11-02 | T0 | Performance verified in editor Play mode (no player build), as for the visuals work | The owner asked to verify in the editor instead of building |
| 2026-11-02 | T1 | A spline track is the existing track JSON (`TrackData`) plus an optional `Route` section (knots with optional Bezier tangents, corridor width, outer-line gap, flight height, ground height, obstacle list, finish index); the start pad is `Spawn`. `TrackDefinition` (ScriptableObject) wraps it for editing and exports the JSON | Catalog, menus, user track library, leaderboards and ghosts keep working unchanged; old gate-list tracks still load |
| 2026-11-02 | T1 | Route = `UnityEngine.Splines.Spline` (zero tangents → auto-smooth) re-sampled by arc length every 0.25 m; the level frame keeps posts/gates vertical | Everything is placed by metres along the route; deterministic |
| 2026-11-02 | T1 | The route is the flight line; the **lane deck** is the ground, or flight line − 1.5 m where the route climbs. Supports/bridge only where the deck is > 0.25 m up; a leg is skipped if it would stand in another branch's corridor | The plan's "spline > 1.5 m above ground" would put supports under every normal 1.5–2 m section |
| 2026-11-02 | T1 | Race gate 0 = the finish obstacle (start/finish line, laps end through it, as LapTimer expects); the start pad sits just before it; the other checkpoints follow by distance after the finish (`TrackCheckpoints.Order`) | "The finish cube is the last checkpoint" of every lap with the existing lap timer |
| 2026-11-02 | T1 | Lanes, posts, clamps, cables, chevron boards and supports are **combined into one mesh per 20 m segment** (5 sub-meshes), not GPU-instanced | Fewer draw calls than instancing for these, and it renders in edit-mode screenshots; instancing stays for stadium seats (T3) |
| 2026-11-02 | T1 | Lane posts get small box colliders (in "Lane Colliders", easy to switch off for the T5 "Ghost lanes" option); support legs get boxes; no mesh colliders | §2.4 colliders |
| 2026-11-02 | T1 | Obstacles use the existing gate/prop prefabs until the T2 kit; `Color` tints a gate's idle frame; obstacles without a Gate get one if they are checkpoints | Keeps T1 independent of T2 |
| 2026-11-02 | T1 | Test track "Spline Test Loop" (Field, 313 m figure-eight with a 2.6 m overpass, 11 gates) defined in code (`RouteLibrary`); the Field drops pillars and mows grass along built-in spline routes | Built-ins stay reproducible like the gate-list courses |
| 2026-11-02 | T1 | The in-game (gate-list) track editor refuses spline tracks; they are edited in Unity (TrackDefinition + Tools > Track > Rebuild) | A spline editor is outside this plan |
