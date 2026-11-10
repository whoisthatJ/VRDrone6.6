# Ushqyr — Phase 3 report

Phase 3 (plan: `PHASE3_PLAN.md`, decisions log at its end) took the simulator from a single-player gate racer to a
training platform: a better Expo district with vertical routes, realistic quads, AI opponents, and a new **Strike
Range** mode offline and online. Everything below is committed on branch `track-expo-phase3` (not merged into `main`); nothing has been
pushed.

## Milestones

| | Delivered | Proof | Notes |
|---|---|---|---|
| **P0** | Orientation, decisions confirmed | `PHASE3_PLAN.md` decisions | Blender 3.3 used instead of 4.x |
| **P1a** | `Track_Expo_v1` frozen as its own scene with 404 copied assets; the old Expo ids keep their records there | v1 screenshots pixel-identical to the original | Engine-level kit stays shared |
| **P1** | `Track_expo` audit (20 ranked gaps) and fixes: sky, roofs, glass, asphalt, lawns, cars, pools, Astana palette | `Docs/track_expo_quality_audit.md`, before / after bookmarks | |
| **P2** | Quad parts generated in Blender headless (`Tools/Blender/quadgen.py`): 9 frames, motors, props with blur discs, batteries, cameras, Digital HD option | `QuadPartTests`; turntable shots of all 6 classes | |
| **P3** | 3D route system (inverted / vertical sections), HoloRing, Holo Halo, Sphere Crest; Expo Sprint v2 (1.5 km, to 63 m) and Pro v2 (2.9 km, to 113 m) | `Docs/track_expo_route_v2.md` | v1 unchanged |
| between | Track intro showdown (3 shots, ~8 s), softer garage depth of field, track menu by venue with previews, rename to **Ushqyr**, pitch deck with gameplay GIFs | commits `afb94fc`…`91e4772` | |
| **P4** | Bots fly through stick inputs into the player's flight model: baked racing lines, line planner, trajectory follower with inverse rates; levels Rookie → Elite + Adaptive; race mode with 1–7 bots, live standings, collisions option, results, winner replay | `BotAcceptanceTests`: Elite clean on all 10 courses × 10 seeds, lap times ordered, Adaptive ±5 %, 7 bots 0.76 ms/frame | Bots **off by default** until played through |
| **P5** | Strike Range offline: generated `Range_Steppe`, deterministic targets (static / moving convoys / evasive with smoke), scoring with streaks, intercept-planner bots, HUD with radar, results and a local board, lobby | `StrikeRulesTests`, `StrikeBotTests`: Rookie 566 / Pilot 1 070 / Elite 1 661 mean points over 10 rounds | `Docs/strike_p5.md` |
| **P6** | Strike Range online: shared-seed targets, host-validated hits (first valid claim wins), host scores and bots; **LAN / direct-IP sessions** beside the Unity Relay sessions | `StrikeNetTests` incl. a simulated 4-PC round with 20–250 ms lag (identical targets and scores everywhere); editor LAN round with host bots; a full round between two copies of the build | `Docs/strike_online_p6.md` |
| **P7** | Wind on the Strike Range (Normal 2 m/s, Hard 4 m/s with gusts, from the seed and clock), wind readout on the HUD; profiling of every scene × preset; this report | `WindTests`; `Docs/perf/phase3_P7.csv` | |

**Tests:** 292 EditMode tests, all passing at the end of P7 (222 at the start of Phase 3).

## Performance (P7)

Windows build, 1920×1080, GTX 1650 Ti laptop, 12 s camera flight per scene (`Docs/perf/phase3_P7.csv`). Average fps /
1 % low:

| Scene | Low | Medium | High | Ultra |
|---|---|---|---|---|
| Menu | 287 / 161 | 282 / 160 | 248 / 152 | 207 / 151 |
| Garage | 286 / 160 | 259 / 158 | 165 / 104 | 94 / 78 |
| Track_Field | 254 / 150 | 210 / 135 | 172 / 114 | 145 / 109 |
| Track_Hangar | 268 / 157 | 239 / 141 | 199 / 131 | 177 / 119 |
| Track_Industrial | 280 / 167 | 255 / 154 | 236 / 146 | 218 / 137 |
| Track_Stadium | 196 / 101 | 186 / 98 | 175 / 98 | 161 / 101 |
| Track_Expo | 117 / 81 | 121 / 88 | 109 / 86 | 109 / 84 |
| Track_Expo_v1 | 122 / 87 | 119 / 86 | 104 / 77 | 109 / 81 |
| Range_Steppe | 283 / 173 | 279 / 171 | 266 / 165 | 243 / 151 |

- Every scene averages above 90 fps on every preset. Track_Expo is the heaviest and **CPU-bound** (8–9 ms CPU vs
  6–7 ms GPU): its 1 % lows (77–88 fps) dipped under 90.
- **Follow-up (2026-11-10):** the district's 721 instanced prop groups (50 772 street props) were all submitted every
  frame. `InstancedProps` now skips groups outside the view (beyond the 160 m shadow range) and groups too far away for
  their size (180 m per metre of prop: cars ~900 m, trees ~1.8 km). About 260 groups are submitted instead of 721;
  aerial screenshots are unchanged (mean pixel difference < 0.1 / 255). Build, same laptop (`Docs/perf/expo_P7b.csv`):
  Track_Expo Low 117 → 153 fps (1 % low 81 → 106), Medium 121 → 148 (88 → 106), High 109 → 117 (86 → 89),
  Ultra 109 → 124 (84 → 89); Track_Expo_v1 alike. High / Ultra 1 % lows are now just under 90.
- The Garage on Ultra is GPU-bound (10.4 ms, depth of field + generated quad detail).
- One-off frame spikes (worst frame 30–75 ms) on Field and Hangar at Low–High are scene-load hitches in the first
  second of the run, not steady-state.
- The Range_Steppe numbers are for the empty range (the benchmark camera, no round running). A round adds the target
  views and up to 3 bots (P4 measured 7 race bots at 0.76 ms).
- Quest numbers were not measured in this phase (no headset session).

## What still needs a person

1. **Headset play-through** of race mode with bots, and the Strike Range with a radio: then decide whether bots can be
   on by default (`Docs/bots_p4.md`).
2. **Two-PC LAN test** in a real classroom network (`Docs/strike_online_p6.md`, *LAN test*), and one internet round
   over Relay with the range.
3. **Unity Cloud / Relay** costs stay with the owner (free tier noted in the plan).
4. The **SDU / SITAM logo** for the pitch deck needs the file or a go-ahead to download it.
5. Optional cleanup: `Builds/Windows/` still holds the pre-rename `GateRush*` files next to `Ushqyr*`.

## Known gaps

- Track_Expo 1 % lows on High / Ultra at 88–89 fps on the test laptop (Low / Medium: 106).
- Spline Test Loop and Expo Sprint v2 see an occasional bot crash on some seeds; lower bot levels crash more on Expo.
- Online Strike: bots don't top up empty slots automatically; no online leaderboard for the range.
- The track previews of the Strike Range show the bare range (targets only exist in a round).
