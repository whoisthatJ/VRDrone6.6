# Sphere District (Expo Astana): report

Track plan: `TRACK_EXPO_PLAN.md` (milestones E0–E8, decisions log). Mode: **Faithful** (owner's choice, E0); the
owner is responsible for written permission from the site operator and the architects before any public or
commercial release (plan §1a).

## What was built

| Milestone | Result | Commit |
|---|---|---|
| E1 Geo pipeline | One cached Overpass extract (~3 × 3 km), an importer to local metres (ECEF → ENU at the sphere), gizmo previews, projection tests | 8ffa220 |
| E2 Hero buildings | Sphere (R 40 m, centre 60 m, clear/fritted/PV hemispheres, eye opening, turbines, floors), 4 pillow-roof pavilions with courtyards, 3 pods, podium, canopy, landmarks, all from OSM footprints with colliders | 59ac83b |
| E3 Ground + streetscape | OSM roads with markings and crossings, paving, parks, water, ~6 100 trees, ~8 400 parked cars, lamps, benches, solar fields (GPU-instanced, combined colliders) | eb1628c |
| E4 City | 992 OSM buildings extruded per kind in 400 m cells with LOD culling and colliders, a 1.6–4.5 km far ring of 1 887 blocks, the skyline (tower on the axis), 6 km far plane | f3cddec |
| T6 Finish cube | Truss cube with the LED hexagon, nets and pads; slow motion, auto-hover, cinematic camera through the hexagon and the results screen | d82bff8 |
| E5 Lighting | Summer day (default), golden hour, night (uplit sphere, LED bands, glowing lamps); realtime reflection probes; Expo lighting setting | a8165a1 + 1f78bcd (code) |
| E6 Courses | Expo Sprint (1.40 km, top 31 m) and Expo Pro (2.33 km, helix to 90 m); air courses, Air gates, next-gate beacons, the finish cube | 1f78bcd |
| E7 Free flight + bounds | Expo Free Flight with four spawns (one hovering above the sphere); 1.2 km / 150 m flying area with warning and reset on every Expo track | 1f78bcd |

## Screenshots vs the reference

![reference vs render](screenshots/sphere_district/E8/reference_vs_render.jpg)

Left: `Docs/reference/ref_expo_aerial.png`. Right: the reference bookmark (`SceneShots.CaptureExpo`, summer day).

**Matches:** the plan and scale of the whole site (OSM footprints at 1 m = 1 unit): the four petals with their
courtyards, the sphere on its podium with the canopy and the three pods, the axis to the needle monument and the park,
the ring roads with their radial streets, the round arena and the long exhibition hall, the tower on the horizon and
the dense blocks either side of the axis.

**Doesn't match yet:**
- the sky and haze: the render's horizon is a warm yellow haze; the photo has a clear blue summer sky with a light grey
  horizon;
- the city reads as uniform white boxes: no balconies, rooftop clutter, colour palette or height variety beyond OSM;
- the pavilion and sphere glass is darker and flatter than the photo's bright, reflective silver;
- the roof skylight rhythm and roof greys are close, but the roofs lack panel seams and edge trims;
- the park paths and lawns are right in layout but greener and flatter than the photo's dry grass;
- the solar fields and car parks are present but darker.

Lighting presets (day / golden hour / night, reference view, walkway, sphere):
![presets](screenshots/sphere_district/E8/lighting_presets.jpg)

The finish moment (cinematic camera through the hexagon, T6):
![finish](screenshots/sphere_district/E8/finish_cinematic.jpg)

More: `Docs/screenshots/sphere_district/E1`–`E7`, `Docs/screenshots/Track_Expo/*_E6.jpg`, `Docs/screenshots/T6/`.

## Courses

| Course | Id | Length | Obstacles | Top | Autopilot laps (12 m/s cruise) | Estimate, 5" pilot at ~22 m/s |
|---|---|---|---|---|---|---|
| Expo Sprint | `expo-astana` | 1.40 km | 14 (16 checkpoints) | 31 m | 225 s and 217 s, clean | ~64 s (plan: 45–70 s) |
| Expo Pro | `expo-pro` | 2.33 km | 20 (22 checkpoints) | 90 m | 332 s clean; 430 s with one slalom rescue | ~106 s (plan: 80–120 s) |
| Expo Free Flight | `expo-free` | — | none | 150 m ceiling | — | — |

- Every metre of both flight lines is checked against the scenery colliders with a 2 m sphere
  (`CourseClearance.Check`): both come back clear.
- The autopilot is deliberately gentle (top ~35 km/h), so its lap times are 3–4× a pilot's. The estimate divides the
  line length by a typical 5" race average.
- The `expo-astana` online leaderboard keeps its id (now "Expo Sprint best lap"); `lap-expo-pro` is new and needs a
  deploy from the Deployment window before Pro times can be posted online.

## Performance

Editor Play mode, NVIDIA GeForce GTX 1650 Ti laptop, 1920 × 1080, `BenchmarkRunner` (Docs/perf/expo_E5.csv):

| Lighting | Low avg / 1% low | High avg / 1% low |
|---|---|---|
| Summer day | 75 / 52 fps | 69 / 54 fps |
| Golden hour | 81 / 67 fps | 72 / 60 fps |
| Night | 81 / 68 fps | 69 / 46 fps |

- **≥ 60 fps at Low: met** on this laptop at every preset (average).
- **144+ fps at High on the recommended PC: not shown.** This GPU is below the recommended spec, and Editor numbers
  run lower than a player build; at ~14 ms CPU vs ~7 ms GPU the scene is CPU-bound (draw submissions). A player-build
  benchmark on the recommended PC is still to do.
- History: E3 first cut 40 fps → 83/75 after 600 m cells; E4 city 74/59 → 85/75 after limiting city shadows to 700 m.

## Credits and licences

- **Map data:** © OpenStreetMap contributors, ODbL 1.0 (one Overpass extract, cached in
  `Assets/_Project/Data/Geo/SphereDistrict/osm_extract.json`). Shown in Main menu > Credits and listed in
  `Assets/_Project/Art/ASSET_LICENSES.md`.
- **Textures and skies:** CC0 1.0 from Poly Haven and ambientCG (each one listed in `ASSET_LICENSES.md`); the Expo
  skies are `hilly_terrain_01_puresky`, `qwantani_dusk_2_puresky` and `qwantani_night_puresky` (Poly Haven, CC0).
- **Everything else is procedural:** buildings, sphere, pavilions, trees, cars, lamps, gates, the finish cube, the
  beacon and all procedural textures are generated by the project's Editor tools.
- No Google 3D Tiles or satellite imagery are used.

## Known gaps

1. Sky colour and haze don't match the photo (see above); no cloud layer.
2. City façades: one material per building kind, no interior mapping, balconies or rooftop clutter; skyline blocks are plain boxes.
3. Glass looks darker and flatter than the photo; no layered glass shader or interiors visible through the clear hemisphere.
4. Night: city windows stay dark; the ground away from the hub is very dark.
5. No moving traffic on the ring roads.
6. Courses: gates on lamp posts and banner gates between pavilion corners aren't built (plain gates stand in); the glazed links over the courtyard mouths aren't modelled (the canopy is the only one to fly under).
7. The autopilot rescues itself at slaloms now and then; its lap times aren't a pilot's.
8. The finish cube has no brand banner on its top beam.
9. 144 fps at High is unverified (no recommended-spec PC benchmarked; Editor numbers only).
10. `lap-expo-pro` online leaderboard not deployed yet.
11. The boundary warning text was verified by code path and the reset by test, but a screenshot of the warning wasn't captured.
