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

- Terrain: a 1.4 km mesh (5 m cells) of low Perlin hills, flattened round the pads and settling to a flat skirt that
  runs to the horizon under the fog. The playable area is 1.2 × 1.2 km.
- Four dirt roads (Catmull-Rom, 5 m points) that the moving and evasive targets drive.
- 26 static spots 150–700 m from the pads (a third with U-shaped earth revetments), 22 berms, 7 old hulks (no hitboxes),
  a watchtower and a command tent by the pads.
- Three concrete pads 24 m apart at the south edge, each with a red / yellow / green light tree for the 3-2-1 countdown.

## Traffic (2026-11-12)

Road vehicles never meet:

- Each convoy and each evasive vehicle gets **its own section of road** (`RangeDirector.Sections`); no two groups
  ever share a stretch.
- Inside its section a group drives a **lane loop**: out on the right lane, a U-turn, back on the other lane
  (lane centres 2.4 m either side of the 10 m road). Convoy members keep 18 m along the loop, so they never pass
  through each other, and a convoy runs on the round clock, so a respawned member rejoins its line.
- A hit vehicle's **wreck rolls off the road** (0.8 s) to a spot the director picks: beside its lane, further out
  or a little along the road, clear of other wrecks and of the traffic passing while it rolls and while it lies
  there. Deterministic (hits arrive in the same order on every PC). If no spot is free (1 in ~2 000 hits in
  tests) the wreck burns out and sinks instead.
- `StrikeTrafficTests`: full rounds on the real layout with random hits, respawns and wrecks, every 0.1 s, every
  pair of vehicles checked with oriented footprints (Normal and Hard, 8 seeds each): no overlaps.

## Look (2026-11-12, after the FPV strike-sim references)

- **Vehicles** (`VehicleKit`, `RangeVehicles.Model`): chamfered hulls, tracks with road wheels, sprockets and
  idlers, wheels with hubs, cabs with glass, mirrors and lights, canvas covers, hatches, antennas, stowage;
  weathered paint (a grime map under the paint colour). Still fictional designs, no insignia. One mesh per
  material, cached per kind.
- **Ground** (`RangeSteppeTerrain`): a Unity terrain on the layout's height function, layers of green grass,
  straw-gold dry grass, bare earth and gravel by the pads, grass tufts, bushes and stones, dusty road edges and
  wheel ruts, a power line, muted post-processing (ACES, -14 % saturation, light vignette) and more haze.
- **Hits**: a flash and sparks, then flames under the smoke while the wreck burns.
- Cost: 10.7 ms a frame in the editor in a round (Expo: 12.4 ms).

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
