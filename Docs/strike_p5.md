# Strike Range (P5, offline)

A two-minute score attack on a steppe training range. The pilot launches from a pad, dives onto vehicle targets
(contact at 8 m/s or faster is a hit) and relaunches 1.5 s later from the nearest free pad. Up to 3 bots fly the same
flight model on the same range. Content rules from the plan hold: fictional box-built vehicles, no people, arcade
effects (dark tint, smoke, scorch), a hit is a hit.

## Where things are

| Part | File |
|---|---|
| Rules, scoring, deterministic random | `Scripts/Strike/StrikeRules.cs` |
| Range data (pads, static spots, roads, heightfield) | `Scripts/Strike/RangeLayout.cs`, asset `Data/Strike/RangeLayout_Steppe.asset` |
| Target plans, motion, respawn director | `Scripts/Strike/RangeTargets.cs` |
| Vehicle silhouettes, hitboxes, disabled look | `Scripts/Strike/RangeVehicles.cs` |
| Intercept-planner bot | `Scripts/Strike/StrikeBot.cs` |
| Round flow, player contact, pad lights, local board | `Scripts/Strike/StrikeSession.cs` |
| HUD: timer, score / streak, standings, radar, off-screen markers, results | `Scripts/Strike/StrikeHud.cs` |
| Day / overcast / dusk | `Scripts/Strike/RangeLighting.cs` |
| Scene generator | `Editor/Environment/Strike/RangeSteppeBuilder.cs` (environment `steppe`, scene `Scenes/Range_Steppe.unity`) |
| Menu lobby | `MenuController.StrikeLobby` (venue *Strike Range*) |
| Track scene hookup | `TrackSceneController.SetupStrike` |
| Strings | `Localization/Text/strike.tsv` |

Rebuild the scene with `TrackSceneBuilder.Build("steppe")` (or *Ushqyr > Builders > Build Track Scenes*), then
`CatalogBuilder.BuildTracks()`.

## The range

- Terrain: a 1.4 km Unity terrain of low Perlin hills, flattened round the pads and settling to a flat skirt that
  runs to the horizon under the fog. The playable area is 1.2 × 1.2 km.
- Four dirt roads (Catmull-Rom, 5 m points) that the moving and evasive targets drive.
- 26 static spots 150–700 m from the pads (a third with U-shaped earth revetments), 22 earth banks, 7 old hulks (no hitboxes),
  a watchtower and a command tent by the pads.
- Three concrete pads 24 m apart at the south edge, each with a red / yellow / green light tree for the 3-2-1 countdown.

## Traffic (2026-11-12)

Road vehicles never meet:

- Each convoy and each evasive vehicle gets **its own section of road** (`RangeDirector.Sections`); no two groups
  ever share a stretch.
- Inside its section a group drives a **lane loop**: out on the right lane, a turn, back on the other lane
  (lane centres 2.4 m either side of the 10 m road). Convoy members keep 18 m along the loop, so they never pass
  through each other, and a convoy runs on the round clock, so a respawned member rejoins its line.
- A hit vehicle's **wreck rolls off the road** (0.8 s) to a spot the director picks: beside its lane, further out
  or a little along the road, clear of other wrecks and of the traffic passing while it rolls and while it lies
  there. Deterministic (hits arrive in the same order on every PC). If no spot is free the wreck burns out and
  sinks instead.
- `StrikeTrafficTests`: full rounds on the real layout with random hits, respawns and wrecks, every 0.1 s, every
  pair of vehicles checked with oriented footprints (Normal and Hard, 8 seeds each): no overlaps.

### Movement (2026-11-12, second pass)

- **Turns**: the U-turn at each end of a section is a keyhole on a 5 m radius (swing out, round, swing back into
  the other lane) driven at 60 % speed; it was a 2.4 m pivot. Sections keep `TurnExtent` + 6 m from their ends.
- **Heading** follows the road's direction over 6 m, not the polyline segment (it snapped every 5 m).
- **Evasive vehicles** ease between speeds over 1.4 s (they stopped and started instantly) and weave by distance
  driven, fading in and out along each leg (the weave used to switch off, and the vehicle jump sideways, when it
  stopped).
- **Slopes**: vehicles lean with the ground (`RangeDirector.GroundNormal`).
- **Roads cross** (roads 2 and 3): sections and their turns keep 14 m clear of every crossing, and wrecks never
  roll within 13 m of another road.
- **Wrecks** roll off to the side they are on (also when hit in a turn), at most three steps (19.5 m) along the
  road, with the way there checked every metre and against a wreck that is still rolling. In a stress test (a road
  vehicle hit every 2-5 s) 13 of 408 found no free spot and sank.
- The radar antenna sweeps round its mast (it was tumbling about its forward axis).
- Checked numerically over 12 rounds: no position jumps (top speed 11.6 m/s), turn rate at most 77 deg/s.

## Look (2026-11-12, after the FPV strike-sim references)

- **Vehicles** (`VehicleKit`, `RangeVehicles.Model`): chamfered hulls, tracks with road wheels, sprockets and
  idlers, wheels with hubs, cabs with glass, mirrors and lights, canvas covers, hatches, antennas, stowage;
  weathered paint (a grime map under the paint colour). Still fictional designs, no insignia. One mesh per
  material, cached per kind.
- **Ground** (`RangeSteppeTerrain`): a Unity terrain on the layout's height function, layers of green grass,
  straw-gold dry grass, bare earth and packed dusty track, grass tufts, bushes and stones, a power line, muted
  post-processing (ACES, -14 % saturation, light vignette) and more haze. Grass sways in a light breeze (small,
  slow, no colour pulsing).
- **Field roads** are painted into the terrain (no road mesh): a ragged edge that wanders in and out, packed wheel
  tracks, grass down the middle of each lane, dust spilling into the grass at the sides, tufts thinning out
  towards the road.
- **Earth banks** (berms and the U-shaped revetments) are sculpted into the terrain (`RangeSteppeTerrain.Mound`):
  long ridges with a soft crest, wandering line, uneven width and height, rounded ends; grass on the crest, bare
  earth on the steep sides. Whole banks stay 30 m+ from the roads (clear of rolled-off wrecks).
- **Hits**: a flash and sparks, then flames under the smoke while the wreck burns.
- Cost: 10.7 ms a frame in the editor in a round (Expo: 12.4 ms).

## Visual pass (2026-11-12)

The whole scene was reworked after it still read as flat and empty:

- **Sky and light** (`RangeSteppeDressing.Skies`, `RangeLighting.Sky`): photographic skies for Day (low golden
  sun), Overcast and Dusk. The sun direction, sun colour and horizon haze are measured from each sky, so light,
  shadows and fog agree with the picture. Falls back to the procedural sky if the presets are missing.
- **Ground**: an even grass layer and a mottled wild-grass layer (`T_Steppe_GrassWild`, the photo with its
  large-scale gradient divided out: tiled as-is it showed as bands across the plain), olive and straw tints,
  bare earth only as rare scuffs, trodden ground with a ragged edge round the camp. No normal maps on the grass
  layers. The plane beyond the range uses the same grass (`T_Steppe_Plain`), so the range no longer reads as a
  square.
- **Grass**: short dense grass plus taller dry tufts (billboards), 1024 detail map, colours matched to the ground.
- **Trees** (`RangeSteppeDressing.Trees`): nine shelter belts (two staggered rows with scrub) along the field
  edges and inside the range, 26 lone trees and clumps, all 42 m+ from the roads and 38 m+ from the target
  spots; trunk and crown colliders. `Horizon`: 26 tree lines of scanned-tree cards 0.8-1.4 km out on the plain.
- **Camp** (`RangeSteppeCamp`): two ridge tents with poles and guy ropes, a braced timber watchtower with a
  ladder and tin roof, sandbag walls by the pads and round the camp, crates on pallets, drums, jerrycans, a
  generator with its cable, a guyed radio mast, a windsock, a table under an awning, a wire fence and two parked
  vehicles (no hitboxes).
- **Vehicles**: camouflage paint (`T_Strike_VehicleCamo`: seamless three-tone blotches, dust, chips, in tones of
  the class colour), treaded tyres with wheel nuts, cleated tracks, and fittings per type: mudguards, spare track
  links, tools, tow cable, exhaust, lamp housings, turret basket, smoke dischargers, gun sleeve, fume extractor
  and recoil spade (artillery), trim vane, mirrors, stowage boxes and jerrycans (wheeled), grille bars, winch,
  steps, door seams, mud flaps, hooped canvas and bed stakes (trucks), a curved lattice antenna (radar). Lamps
  no longer glow. Hitboxes and footprints unchanged.
- Cost: 11.1 ms a frame in the editor in a round (10.7 ms before the pass).

## Rules

| | Static | Moving | Evasive |
|---|---|---|---|
| Points | 100 | 200 | 300 |
| Movement | none | road ping-pong, convoys of 3, 10–40 km/h | stop-go, reversing, zig-zag, smoke |

- Streak multiplier ×1.1 per hit without a crash, max ×2. Speed bonus 1 % per m/s over 8 m/s, max 25 %.
- A slow contact is a "bump": no score, counts as a crash, streak reset.
- Disabled targets respawn somewhere else after 10–15 s.
- Mix: Easy 10 static; Normal 6 / 5 / 2; Hard 4 / 5 / 5.
- Board: top 10 per difficulty in `persistentDataPath/strike.json`. A round where the pilot scored nothing is not entered.

## Bots

Same quad build, physics, collision and trajectory follower as the race bots (P4), with an intercept planner on top:
choose a target by value ÷ time to reach (better levels; weak ones take the nearest and avoid moving targets), lead
moving targets by the time to get there (Pilot and up), transit 22 m over the terrain, dive inside 55 m at 13 m/s or
more. Cruise speed, aim noise and re-planning interval come from the level.

**Acceptance (headless, `StrikeBotTests`, 10 rounds of 120 s each, Normal mix, solo):**

| Level | Mean score | Crashes in 10 rounds |
|---|---|---|
| Rookie | 566 | 0 |
| Pilot | 1 070 | 1 |
| Elite | 1 661 | 0 |

Play mode check: a round against 3 Pro bots on Hard ran to the results screen with no errors (bots 1 512 / 1 188 / 868
points, 3–5 hits each).

## Not done yet

- Wind (Normal 2 m/s, Hard 4 m/s with gusts) was added in P7: `StrikeRules.WindAt` from the seed and the round clock,
  applied to the pilot and the bots as relative airflow in the flight model (zero everywhere else).
- The menu preview shows the bare range: the targets only exist once a round starts.
- Not played through on the Quest headset or with a radio yet.
