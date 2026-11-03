# Plan: Sphere District Track (Astana EXPO 2017 environment), desktop PC

> **Platform:** desktop PC simulator, no VR (see `PLAN.md` platform note).
> **Depends on:** the spline track system, obstacle kit, route guidance and finish cube from `TRACK_GARAGE_PLAN.md` (milestones T1, T2, T5, T6). If those aren't finished yet, finish them first. This track reuses them and adds no new track code unless needed.

## 0. The environment (reference photo)
Save the photo as `Docs/reference/ref_expo_aerial.png`. It shows the former **EXPO 2017 grounds in Astana, Kazakhstan** (coordinates 51°05′28.5″N 71°24′46.1″E), looking north along the main axis.

| Element | Description (from the photo + public facts) |
|---|---|
| **Central sphere** (Nur Alem) | A glass sphere, **80 m in diameter, 100 m tall** from plinth level, 8 floors. One hemisphere is fully see-through, the other translucent, with built-in photovoltaic panels, an oval glazed "eye" opening near the top, and two wind turbines at the very top. It sits on a round, undulating glass podium with a radial canopy |
| **Petal pavilions** | **4 large horseshoe/C-shaped pavilions** around the sphere (2 at the front, 2 at the sides/back), with soft "pillow" silver metal roofs, rows of roof skylight strips, glass outer façades and green courtyards inside the "C" |
| **Pods** | 4 smaller lens/teardrop-shaped pavilions between the big ones, close to the sphere; glazed links/canopies connect the buildings |
| **Ground** | A grey stone central axis/plaza running south-north between the front pavilions, with lawns, organic curved park paths (north of the sphere), a playground, and a tall needle monument on the axis |
| **Roads** | **Concentric ring roads** around the whole complex with lane markings, crossings and radial streets; car parks with cars; solar-panel fields in the bottom corners |
| **Surroundings** | Dense mid/high-rise residential blocks (beige/white), office buildings with glass façades, a round arena, a long exhibition hall, and on the horizon a tall skyscraper on the main axis plus the city skyline; flat steppe terrain, clear summer sky with scattered clouds |

## 1. Legal and data decisions (read before building)
**a) Copying the buildings.** Kazakhstan has **no freedom of panorama**: depicting copyrighted buildings is allowed only incidentally and non-commercially. These buildings are recent (2017, by Adrian Smith + Gordon Gill Architecture), so they are still protected. A track built *around* the sphere isn't "incidental". (I'm not a lawyer; this is a risk flag, not legal advice.)

So the plan supports two modes, and you choose:
| Mode | What it is | When |
|---|---|---|
| **Inspired (default)** | Same *kind* of place: a central glass sphere, petal pavilions, ring roads, park, Astana-style city around it, real geography and scale. But it has an **original façade design** (our own panel pattern, a different roof profile and skylight rhythm, different pod shapes) and a fictional name: **"Sphere District"** | Shipping to many students, or any public or commercial release, without permission |
| **Faithful** | As close to the real buildings as possible | Only with **written permission** from the rights holders/operators (the site operator and the architects). Hero buildings are swappable prefabs, so switching modes is a prefab swap |

**b) Map data.**
- **OpenStreetMap** (ODbL licence) is used for roads, building footprints, parks, car parks, trees and building heights, with "© OpenStreetMap contributors" in the credits and the attribution file.
- **Not** Google Photorealistic 3D Tiles: Google's terms don't allow caching them for offline use, and a sim must work offline.
- Satellite imagery isn't used for tracing either. The reference photo and the public dimensions are enough.

**c) Names.** Don't use "EXPO", "Nur Alem", "Astana Hub", "AIFC" or other logos/trademarks on in-game signage. Use our own GATERUSH branding and neutral names.

## 2. Scale and zones
- **Origin:** the sphere centre = world (0, 0, 0). OSM lat/lon is converted to local metres with a local tangent-plane projection (east = +X, north = +Z), so 1 unit = 1 m.
- **Zones:**
| Zone | Radius from the sphere | Detail | Built how |
|---|---|---|---|
| **Hero** | 0–450 m | Full detail: sphere, pavilions, pods, podium, plaza, park, ring roads, street furniture, cars, trees | Procedural generators + hand-tuned |
| **Mid** | 450–1500 m | Buildings from OSM footprints + heights, generic façade materials, roads, trees | Automated OSM pipeline |
| **Far** | 1.5–6 km | Skyline: landmark tower(s) and city silhouettes | Low-poly blocks → baked **impostor cards** + haze |
- **Flyable area:** 1.2 km radius, 150 m altitude. Beyond that a soft warning appears, then an automatic reset.

## 3. Hero buildings (procedural generators, so they can be re-tuned and swapped)
1. **`SphereGenerator`:** radius 40 m, centre 60 m above the plinth.
   - **Façade:** a panel grid (lat-long or geodesic, with a different pattern in Inspired mode), mullions, a clear/translucent split by hemisphere, PV-panel tiles, the oval opening near the top, 2 turbines on top (rotating).
   - **Inside:** simplified floor slabs visible through the clear side.
   - **Collider:** a perfect `SphereCollider` (plus a podium collider).
   - **Night:** emissive panel lighting with an animated pattern (our own "light show").
2. **`PillowPavilionGenerator`:** takes a 2D footprint polyline (C/horseshoe shape from OSM or traced from the photo) → extrudes the walls with a glass façade, a rounded "pillow" roof edge, a metal roof with skylight strips, and an inner courtyard. Parameters: height (~18–25 m), roof radius, skylight rhythm. Collider: a simplified mesh collider (static, fine for the environment).
3. **`PodGenerator`:** lens/teardrop pavilions with metal roofs and a glass band.
4. **Podium and canopies:** the round glass podium around the sphere base, radial canopy ribs, glazed links between buildings (placed above the flight corridors, or with gaps the quad can fly under).
5. **Landmarks** (simple but recognisable silhouettes, original detailing): the needle monument, the round arena, the long exhibition hall, the tall tower on the horizon (Far zone).

## 4. Ground and streetscape
- **Ring roads and radial streets** from OSM: asphalt with lane markings, crossings and kerbs (generated road meshes + decals).
- **Plazas:** a stone paving pattern on the main axis, granite kerbs, stairs/ramps.
- **Park:** lawns, organic curved paths (OSM or traced from the photo), a playground, young trees (GPU-instanced, with LODs), benches, lamp posts.
- **Car parks** with parked cars (8–10 low-poly CC0 or procedural variants, instanced); **solar fields** (instanced panel rows).
- **Mid zone:** OSM footprints extruded to `height` or `building:levels × 3.2 m` (default 30 m for residential blocks); façade materials by type (beige residential, glass offices); a roof cap; LOD0/1 + cull; combined meshes per 200 m cell.
- **Terrain:** flat (Astana is flat) with a slight micro-variation; steppe grass at the far edge.

## 5. Lighting and atmosphere
- **Presets:** **Summer day (default, like the photo)** · Golden hour · **Night** (the sphere lit up, street lights, LED gates glowing).
- An HDRI sky matching each preset (CC0, e.g. Poly Haven); haze/fog for depth in the Far zone; reflection probes around the sphere and pavilions (glass and metal need them).
- Keep the frame-rate targets from `VISUALS_PLAN.md` (desktop update): 144+ fps recommended, ≥ 60 fps minimum at Low.

## 6. Courses
The obstacles look like a **real pop-up drone-race event** set up on the site: the truss gates, padded gates, inflatable arches, flags and finish cube from the existing kit, plus a few urban variants:
- gates hung under canopies/links;
- flag pairs on lamp posts;
- banner gates between pavilion corners.

| Course | Length | Idea | Level |
|---|---|---|---|
| **Sprint** | ~1.0–1.3 km | Start on the south plaza → up the central axis between the front pavilions → a loop through one pavilion courtyard → around the sphere at low altitude → park slalom → back down the axis → **finish cube** on the plaza | Beginner (wide gates, low altitude, Trainer guidance recommended) |
| **Pro** | ~2.0–2.5 km | Sprint elements + a **spiral climb around the sphere** (gates on a helix up to ~90 m) → **dive** down the far side (the signature FPV moment) → a fast **ring-road sweeper** (gates on lamp posts) → **threading under the glazed links** → through two courtyards → park → finish cube | Advanced |
| **Free flight** | — | No gates, the whole flyable area, multiple spawn points (plaza, sphere top, park, ring road) | Practice / freestyle |

**Route guidance** (existing Off/Standard/Trainer modes) plus one addition for the open city: a **beacon** (a soft vertical light beam) on the next gate, because sightlines here are long and gates can hide behind buildings.

## 7. Milestones
| # | Milestone | Done when |
|---|---|---|
| E0 | Prerequisites + decision | The track system, obstacle kit, guidance and finish cube work; mode **Inspired/Faithful** confirmed by me; the reference photo saved; the agent describes the photo and I confirm it |
| E1 | Geo pipeline | Overpass/OSM extract (~3 × 3 km around the sphere) saved to the repo with attribution; an Editor importer converts it to local metres and generates gizmo previews of roads, footprints, parks, car parks and trees; a unit test checks the projection (known points end up where expected) |
| E2 | Hero buildings | Sphere, 4 petal pavilions, 4 pods, podium, canopies and links generated with colliders; an aerial screenshot from a camera matching the photo angle, side by side with `ref_expo_aerial.png` |
| E3 | Ground + streetscape | Roads, markings, plaza paving, park, trees, lamps, cars, solar fields |
| E4 | City around it | Mid-zone OSM buildings with LODs and cells; Far-zone skyline impostors + haze; the landmark tower on the horizon |
| E5 | Lighting | Day / golden hour / night presets, reflection probes, frame-rate check per quality preset |
| E6 | Courses | Sprint + Pro built with `TrackDefinition`; next-gate beacons; finish cube; full laps flown in Play mode; lap times reported (Sprint ~45–70 s, Pro ~80–120 s with a 5") |
| E7 | Free flight + bounds | Free-flight mode, spawn points, a soft boundary with warning + reset |
| E8 | Polish + report | Screenshots vs reference, performance per preset, the credits list (OSM attribution, CC0 assets), known gaps |

## 8. Risks
| Risk | Mitigation |
|---|---|
| Copyright on the buildings | Inspired mode by default; Faithful mode only with written permission; swappable hero prefabs |
| Huge open area hurts the frame rate | Zones, LOD + cells + impostors, GPU instancing / GPU Resident Drawer if available, haze, culling distances per layer |
| OSM data is incomplete (missing heights, footprints) | Defaults by building type; manual fix-ups in a `GeoOverrides` asset; the photo as a check |
| Gates get lost against the busy city background | Beacons + next-gate pulse + high-contrast gate colours; test readability at 120 km/h in the FPV view |
| Glass everywhere looks flat or costs too much | Reflection probes + cheap glass shader (no screen-space refraction); interior "fake depth" cubemaps for pavilion windows |

## Decisions
| Date | Milestone | Decision | Why |
|---|---|---|---|
| 2026-11-03 | E0 | **Faithful mode** (owner's choice). Hero buildings follow the real architecture; the owner is responsible for obtaining written permission from the site operator and the architects before any public or commercial release (§1a). Hero buildings stay separate generator outputs so an Inspired variant can replace them | Owner decision after the §1a risk flag |
| 2026-11-03 | E0 | The finish cube (TRACK_GARAGE_PLAN T6) is missing; E1–E5 don't need it, so it is built before E6 instead of blocking the district | Keeps the district moving; E6 is the first milestone that needs it |
| 2026-11-03 | E0 | The earlier `expo-astana` track (ExpoArt, Track_Expo, commit ba564fd) is the starting point: E1 replaces its hand-measured constants with the cached OSM extract, E2–E4 move its generators into `Editor/Environment/SphereDistrict/` | Reuse instead of a parallel second district |
| 2026-11-03 | E0 | Origin = the sphere centre from the OSM outline (51.08931 N, 71.41615 E), east = +X, north = +Z | Plan §2 |
| 2026-11-03 | E2 | Hero buildings from the OSM footprints (`Editor/Environment/SphereDistrict/`): `PillowPavilionGenerator` (4 petals, courtyards from the relations' inner rings, 24 m, 8 m pillow edge, radial skylight bands), `PodGenerator` (3 pods and the lobed podium), `CanopyGenerator` (south canopy at 6.5 m, open underneath, ribs, columns kept off the course orbit), `SphereGenerator` (80 m, centre 60 m, clear north / fritted south hemisphere, PV tiles high up, floor mullions with LED strips, 48 vertical mullions, the south-west scoop with a recessed bowl and two spinning turbines, 8 visible floor slabs, `SphereCollider`), `LandmarkGenerator` (congress arena with dome, exhibition hall, hotel, the 46 m needle). Landmark OSM ids live in `OsmImporter.Landmarks` and the overrides asset | Faithful mode, real footprints |
| 2026-11-03 | E2 | OSM has 3 pods (not 4) plus the lobed podium; built as mapped. The courtyards are closed rings in OSM; the glazed arches over their mouths (photo) are not modelled yet | Data over guesswork; links are a later polish item |
| 2026-11-03 | E2 | The pods reach 112-119 m and the petals start at 125 m, so the course orbit moved from 112 m to 61 m (between the podium, out to 55 m, and the pods, from 67 m; under the south canopy). Route clearance from every hero footprint >= 5.5 m. E6 redesigns the courses | Real footprints leave no flyable gap outside the pods |
| 2026-11-03 | E3 | Streetscape from OSM (`StreetscapeGenerator`): roads as asphalt ribbons (height by class, edge/centre/lane markings, kerbs on primary-tertiary), footways/cycleways as paving with zebra crossings, areas (grass, parks, woods, pitches, playgrounds, parking, pedestrian squares, water, construction); GPU-instanced props via `Core/InstancedProps` (~6 100 trees from OSM trees, tree rows, woods and park planting; ~8 400 parked cars; 2 460 street lights; 3 370 park lamps; 770 benches; the solar field), combined collider meshes for lamps and cars; props keep 7 m off the race corridor. Base ground = urban paving, dry steppe band from 1.5 km | Faithful layout straight from the map data |
| 2026-11-03 | E3 | Performance (editor, GTX 1650 Ti laptop, 1080p): first cut 40 fps (CPU-bound, ~7 000 draw submissions); with 600 m cells, 4 car colours / 4 tree variants and no shadows from cars and street lights: **83 fps Low, 75 fps High** (`Docs/perf/expo_E3.csv`) | Draw-call count, not GPU, was the limit |
| 2026-11-03 | E4 | City from OSM (`CityGenerator`): 992 non-hero buildings extruded to tagged / levels × 3.2 m / per-kind height, facade by kind (residential window rhythm, curtain wall for office/hotel/commercial, plain light-grey concrete otherwise), dark glazed ground floor on buildings over 9 m, grey roofs; combined per 400 m cell (61 cells) with a culling LOD and a mesh collider; shadows only within 700 m. A far ring of 1 887 procedural slabs and towers (1.58–4.5 km, the Baiterek axis kept open) and the existing skyline fill the horizon. The camera far plane is 6 000 m for Expo (1 500 m elsewhere). Editor perf: **85 fps Low, 75 fps High** (`Docs/perf/expo_E4.csv`) | Fills the horizon at almost no cost; the source concrete texture read brown, so plain grey is used |
| 2026-11-04 | E5 | Lighting presets through the stadium's runtime `StadiumLighting` (now per venue): **Summer day** (default: midday sun from the SSW, like the photo), **Golden hour** (qwantani dusk sky, low warm sun), **Night** (moon, eight 75 m-out uplights washing the sphere, the sphere LED bands at 5×, street and park lamps at full glow). Each preset has its own sky copy, fog, post volume, lamp-glow level for the GPU-instanced lamps (`InstancedProps.EmissionScale`) and re-renders two realtime reflection probes (hub, sphere). New setting Settings > Feed > Expo lighting (default Summer day). Editor perf (GTX 1650 Ti, 1080p): day 75/69, golden 81/72, night 81/69 fps Low/High (`Docs/perf/expo_E5.csv`). City windows stay dark at night | Live switching with nothing baked, as the stadium; probes via scripting so they cost nothing per frame |
