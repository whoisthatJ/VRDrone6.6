# Track_expo route, first version (Phase 3, P3 step 0)

The first routes live on unchanged in `Track_Expo_v1` as **Expo Classic Sprint** (`expo-astana`) and **Expo Classic
Pro** (`expo-pro`), frozen as JSON in `Assets/_Project/Tracks/Track_expo_v1/Data/`. Both are air courses (no lanes),
anticlockwise round the sphere, finishing in the finish cube on the south walkway. Bearings are clockwise from north,
distances from the sphere centre.

![top-down map](track_expo_route/v1_map.jpg)

Yellow: Classic Sprint; red: Classic Pro (it shares the opening with the Sprint, drawn over it). White circles: gates.

![height along the lap](track_expo_route/v1_elevation.png)

## Expo Classic Sprint (beginner)
| | |
|---|---|
| Length | 1 398 m, 2 laps |
| Obstacles | 14 (16 checkpoints): finish cube, 5 singles, 4 air gates, slalom (3), double |
| Height | 1.8–31 m (two 31 m hops: over the 160° pavilion's west wing and its inner wing) |
| Sections | walkway north → finish cube → hop into the 160° courtyard → out over the inner wing → under the canopy through the 118–138° pod gap → orbit at 61 m, 3 m high, anticlockwise to the north → park slalom and U-turn (to 236 m) → back down the axis → west side of the orbit → south pod gap → walkway south → plaza U-turn → cube |
| Lap time | autopilot (12 m/s cruise, Angle mode): 205.7 s on `Track_Expo_v1`, 225 / 217 s on `Track_Expo` before P1; a 5" pilot at ~22 m/s average: **~64 s** |

## Expo Classic Pro (advanced)
| | |
|---|---|
| Length | 2 327 m, 2 laps |
| Obstacles | 20 (22 checkpoints): finish cube, 9 air gates, 5 singles, slalom (3), double |
| Height | 1.8–90 m |
| Sections | the Sprint opening (courtyard hop, canopy) → orbit to 60° → 1.25-turn helix round the sphere from 3 m to 90 m (canopy-free side until 14 m) → dive down the north-west side into the park → park slalom → ring-road sweeper (284° → 236°, 330 m out) → hop over the 226° pavilion's west wing into its courtyard and out over the east wing → walkway south → plaza U-turn → cube |
| Lap time | autopilot: 332 s clean, 430 s with a slalom rescue; a 5" pilot at ~22 m/s: **~106 s** |

## What works and is kept in P3
- The walkway start and finish cube, the courtyard roof hops (both pavilions), the low orbit under the canopy, the park, the ring-road sweeper.
- Clearance: both lines are checked every metre with a 2 m sphere against the scenery (`CourseClearance`).

## What is missing for P3
- Nothing goes above 90 m, nothing crosses the sphere, no vertical or inverted elements; gates only turn by yaw; the
  beacon is a vertical column (useless for a gate overhead).
