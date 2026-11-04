# Track_expo route, version 2: vertical (Phase 3, P3)

The reworked routes of `Track_Expo`, with new ids (fresh records): **Expo Sprint** `expo-v2-sprint` and **Expo Pro**
`expo-v2-pro`. The first versions stay unchanged in `Track_Expo_v1` (`Docs/track_expo_route_v1.md`). Per the owner's
instruction this proposal was written and then built without waiting for approval (decision in `PHASE3_PLAN.md`).

![top-down map](track_expo_route/v2_map.jpg)

Yellow: Sprint v2; red: Pro v2 (the Pro leaves the map to the north for the needle monument, 471 m out). White circles:
obstacles.

![height along the lap](track_expo_route/v2_elevation.png)

## New system pieces
| Piece | What it does |
|---|---|
| 3D route frame | `TrackRoute.Frame`: the tangent in 3D (vertical and inverted sections), up carried through steep parts, knot `Roll` (180° = inverted). Tests in `Route3DTests` |
| `RouteObstacle.Align3D` | The obstacle turns with the 3D frame and its opening is centred on the line (rings on climbs, dives, vertical drops) |
| **HoloRing** gate | 6 m ring of light (scalable 4–8 m), hologram shader (Fresnel, scanlines, flicker), next / later / passed colours like any gate, **no collider**; touching the rim glitches it and costs **+0.5 s** |
| **Holo Halo** | 35 m holo ring (gate), turning slowly, four projector beams to the ground |
| 3D guidance | a pulsing holo ring round the next gate (works overhead); holo chevrons floating along the line ahead; the next-gate marker shows ↑/↓ and the height difference |

## Expo Sprint v2 (beginner)
| | v1 | v2 |
|---|---|---|
| Length | 1 398 m | 1 533 m (+10 %) |
| Top | 31 m | 63 m |
| Obstacles | 14 | 14 (2 HoloRings + the Holo Halo) |
| Vertical content | two roof hops | the roof hops, and a gentle climb **straight up through the Holo Halo** (50 m) over the north plaza, then a descending turn |
| Kept | walkway, finish cube, courtyard hop, low orbit under the canopy, west orbit, walkway home |

## Expo Pro v2 (advanced)
| | v1 | v2 |
|---|---|---|
| Length | 2 327 m | 2 864 m (+23 %) |
| Top | 90 m | 113 m |
| Obstacles | 20 | 23 (14 HoloRings + the Holo Halo) |
| Sections | courtyard hop → helix to 90 m → park dive → sweeper → second courtyard | courtyard hop → **Sphere Crest**: up the south-east flank (rings at 40 and 86 m), the crest ring at **113 m** over the top (above the turbines), a dive series down the north side (82, 55, 28 m) and the pull-out ring at **6 m** → **Holo Halo**: under it, up and back **over** it (93 m), then **straight down through its centre** → climb to the **needle**: a ring just over its top (55 m) and a dive → west through the park → the **ladder**: three stacked rings, straight up (15, 28, 41 m) → a **high ring** (64 m) and a **split-S** (half loop down, ring at the bottom) → ring-road sweeper → the 226° pavilion's courtyard (roof hops) → walkway → finish cube |
| Vertical moves | — | Sphere Crest, Halo over + dive, monument-top dive, ladder, split-S, two roof hops, the Halo loop works as a power loop over the ring |

## Checks
- Every metre of both lines clears the scenery by 2 m (`CourseClearance`; holo rings don't collide).
- Lap time target ±30 % of the old one: the Sprint is +10 % longer, the Pro +23 %; flown lap times are in `PHASE3_PLAN.md` (P3).
- A power loop over a pavilion roof edge with an inverted top is not in the routes: the Angle-mode autopilot can't fly
  inverted, and the Halo loop gives the same "over and back down" moment. The route system supports it (knot roll,
  `Route3DTests.Frame_RolledKnots_InvertTheTopOfALoop`), for the P4 bots.
