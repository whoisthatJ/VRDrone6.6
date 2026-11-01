# Visual overhaul report (V0–V8)

Target: **Windows, monitor only** (owner's choice, see VISUALS_PLAN.md §6–7). Branch `feature/pc-radiomaster-pocket`.
All screenshots are 1920×1080 renders of the scene bookmarks (spawn, first gate, overview, in-flight FPV at the default
120° horizontal FOV) made by `Tools > Art > Screenshots`; the `ingame_after` images are real Play-mode captures.

## Before / after

| Scene | Before | After |
|---|---|---|
| Field (golden hour) | ![](screenshots/Track_Field/_sheet_before.jpg) | ![](screenshots/Track_Field/_sheet_after.jpg) |
| Field, in game (drone camera + OSD) | — | ![](screenshots/Track_Field/ingame_after.jpg) |
| Hangar (night) | ![](screenshots/Track_Hangar/_sheet_before.jpg) | ![](screenshots/Track_Hangar/_sheet_after.jpg) |
| Industrial (overcast) | ![](screenshots/Track_Industrial/_sheet_before.jpg) | ![](screenshots/Track_Industrial/_sheet_after.jpg) |
| Menu / pit tent (dusk), in game | ![](screenshots/Menu/ingame_before.jpg) | ![](screenshots/Menu/ingame_after.jpg) |
| Material library | — | ![](screenshots/material_library.jpg) |

Sheet order: top-left spawn, top-right first gate, bottom-left overview, bottom-right in-flight FPV.

### What changed, per scene
- **Field** — grid plane and green boxes → a 1 km Unity Terrain with ambientCG lawn, worn grass along the racing line,
  dirt at the pad and gate feet, two layers of detail grass (density follows the Graphics setting), rolling hills and a
  ridge at the edge, 520 billboard trees baked from a Poly Haven tree, a chain-link perimeter fence, a pilot pit
  (canopies, benches, chairs, flags, cones), sports-pitch chalk lines and a rubber launch pad. Golden-hour sky with the
  sun direction and colour read from the HDRI (11° elevation), soft cascaded shadows, fog, ACES grading and bloom.
- **Hangar** — flat coloured boxes → polished concrete with painted walkways, pilot box and hatched door zone,
  corrugated steel walls and roof, I-beam columns, 1.8 m lattice trusses, 18 LED high-bays (emissive fixtures + spot
  lights, six with soft shadows), clerestory windows onto the night, workshop dressing along the walls, three
  box-projected reflection probes.
- **Industrial** — grey yard → cracked asphalt with crack, oil and tyre-mark decals and sky-reflecting puddles, brick
  warehouses with rusty corrugated roofs, roller shutters, windows and security lights, painted corrugated containers,
  a yellow gantry crane, pole lamp heads, Jersey barriers, pallets, barrels and tyres; the water that used to sit hidden
  under the ground plane is now visible beyond the quay. Overcast HDRI, soft shadows, cool desaturated grade.
- **Menu** — dark void with a glowing ring → the pilot's pit tent at dusk: canopy, folding table with LiPo packs, a
  charger and tools, warm string lights and a task lamp, a lit race gate, flags and cones in the field, tree line and
  dusk sky behind the floating menu panel.
- **Gates** — grey bars → padded race covers (Square orange, Large blue, Hurdle red), aluminium bases with sandbags,
  and LED strips on both faces. The LEDs carry the race state (green = next, orange = upcoming) and bloom.
- **Quads** — grey primitives → carbon-fibre frame and top plate, anodized motor bells, tinted polycarbonate props
  (orange front, smoke rear), LiPo pack with strap, camera and lens, antenna and a rear LED.

## Performance

Measured on the development laptop (**GTX 1650 Ti 4 GB**, DX11) at **1920×1080**, in **editor Play mode** (the owner
asked to verify in the editor instead of a player build): `BenchmarkRunner.StartInEditor(...)` flies the drone camera
along each course for 12 s per scene and preset, VSync off. Raw data: `Docs/perf/editor.csv`.

| Scene | Medium avg fps (1 % low) | High | Ultra | GPU frame time Medium / High / Ultra |
|---|---|---|---|---|
| Field | 116 (51) | 117 (55) | 108 (31) | 3.5 / 5.6 / 3.1 ms |
| Hangar | 117 (42) | 109 (59) | 116 (56) | 2.7 / 2.4 / 4.9 ms |
| Industrial | 107 (56) | 116 (65) | 121 (85) | 2.2 / 2.6 / 3.0 ms |
| Menu | 146 (86) | 147 (82) | 126 (26) | 2.2 / 2.9 / 4.2 ms |

- **All presets clear the 60 fps target with a wide margin** even inside the editor. The editor run is CPU-bound
  (~8.6 ms main thread including the editor's own UI and Scene view), which is why the presets come out close; the
  GPU needs only 2–6 ms per frame at 1080p, i.e. a player build has headroom for 160+ fps on this laptop.
- 1 % lows include the first frames after each scene load (shader variants are compiled on first use in the editor);
  a player build precompiles them.
- Baseline before the overhaul (V0, player build, blockout scenes): 330–420 fps (`Docs/perf/baseline.csv`).
- **Player build note:** the first clean Windows build compiles all used shader variants once (it ran for hours with
  DX11 + DX12 and three soft-shadow qualities and was cancelled). V8 cut that work: DX11 only, one soft-shadow
  quality across presets, no stock-Lit materials left on hidden builder objects, and the unused 4-layer terrain add
  pass stripped (`ArtShaderStripping`). Later builds reuse `Library/ShaderCache`.

## Memory and budget

From `Tools > Art > Audit Project` (`Docs/art_audit_before.md` → `Docs/art_audit.md`); track scenes include their
course. Draw calls are renderer submeshes before batching (SRP Batcher/static batching make them cheaper).

| Scene | Draw calls before → after | Triangles before → after | Textures before → after | Lights (rt/mixed) |
|---|---|---|---|---|
| Field | 178 → 296 | 3,400 → 78,584 (+ terrain grass) | 1 (0.2 MB) → 55 (206 MB) | sun |
| Hangar | 117 → 285 | 2,872 → 128,244 | 1 → 64 (229 MB) | 18 high-bays + sun off |
| Industrial | 183 → 406 | 3,596 → 84,145 | 1 → 61 (298 MB) | sun |
| Menu | 4 → 70 | 160 → 25,710 | 0 → 37 (156 MB) | 3 warm practicals + sun |

Budget (VISUALS_PLAN §3): ≤ 1,500 draw calls ✓, ≤ 3–5 M triangles ✓, texture memory well inside the Medium mip-
streaming budget (1 GB) ✓. Racing-line check (`Tools > Art > Verify Racing Lines and Gates`): no added collider on
any straight line between gates, all 24 gates keep their colliders and LED state renderers. EditMode tests: 139/139.

## Assets and licences

Every external asset is CC0 (Poly Haven, ambientCG): see `Assets/_Project/Art/ASSET_LICENSES.md` (34 texture sets,
4 HDRIs, 15 models, one impostor-source tree kept out of git). Everything else (macro noise, carbon weave, decals,
grass blades, chalk, LiPo label, all procedural meshes) is generated by `Assets/_Project/Editor/Art/`.

## How to rebuild

| Task | Menu item |
|---|---|
| Fetch missing CC0 assets | Tools > Art > Download All Catalog Assets |
| Rebuild materials / skies / terrain layers | Tools > Art > Build Material Library, Build Skies and Terrain Layers |
| Re-apply the art to all scenes and prefabs | Tools > Art > Apply Art Pass (all scenes), then Bake Lighting |
| After changing a builder | Tools > Art > Rebuild Everything (Build All Scenes → art pass → bake) |
| Quality presets | Tools > Art > Build Quality Presets |
| Screenshots / audit | Tools > Art > Screenshots > Capture After, Tools > Art > Audit Project |
| Performance (editor) | Enter Play mode from Boot, then run `GateRush.Game.BenchmarkRunner.StartInEditor("Track_Field,Track_Hangar,Track_Industrial,Menu", "Medium,High,Ultra", 12f, "Docs/perf/editor.csv")` (e.g. via `unity command eval`) |
| Performance (player) | `GateRush.exe -benchmark -benchPresets Medium,High,Ultra -benchOut file.csv` |
| Verify gameplay is untouched | Tools > Art > Verify Racing Lines and Gates |

## Compromises

- **Monitor, not PC VR**: no headset tests, no 90 Hz/ASW checks; budgets are 60+ fps at 1080p (§6 of the plan).
- **Env Lit is URP Lit + ORM**, not a Shader Graph with world-space macro variation, vertex-colour blending and
  parallax: macro variation uses Lit's detail-albedo slot; parallax and vertex blending are not used (decals instead).
- **Metric-UV boxes instead of real architecture meshes**: buildings are still the builders' boxes (with real
  materials, trims, windows and doors added as dressing); no trim sheet.
- **Trees are impostor cards**: from the air at low angles the crossed cards can show; the nearest row is 22 m
  outside the play area.
- **No occlusion culling** (it culled the hangar roof from some positions) and **no APV** (lightmaps + legacy probes).
- **GPU Resident Drawer and STP not enabled**: available in URP 17.6, but not needed at these draw counts, and STP's
  temporal upscaling smears fast FPV motion.
- **Quads** use shared library materials on the existing primitives, not a modelled, atlased quad.
- **Props scaled in the track editor stretch their textures/models** (crates and barrels are CC0 models fitted to the
  1 m prefab; a non-uniform scale stretches them).
- **Puddles are transparent quads** (URP's default decal shader has no smoothness input).
- **GTX 1650 Ti numbers only**: the High/Ultra targets (RTX 3060/3070) are untested; run the benchmark there.

## Next ideas

- A real lawn-mower stripe pattern and a few close-up 3D grass clumps along the start straight.
- Realtime-refreshed reflection probe in the menu so the tent lights move with the time of day; time-of-day/weather
  variants per track (the HDRI → sun pipeline already supports any Poly Haven sky).
- Mesh trees (with LODs) for the nearest row of the Field's tree line; distant rows stay impostors.
- Volumetric light shafts under the hangar high-bays (fake cones or URP volumetrics when available).
- Modelled quad frame (UV-mapped, one atlas) and a few real quad models per class.
- Vertex-painted wear on the industrial asphalt, and decal-based gate shadows/footprints in the Field.
