# Phase 3, P4: bots and race mode

AI opponents that fly through the same flight model as the player: the same rates, PID, mixer, motors, battery and
collisions. A bot only produces stick inputs; it gets no extra grip, thrust or shortcuts.

## How a bot flies

| Piece | File | What it does |
|---|---|---|
| Flight envelope | `Scripts/AI/FlightEnvelope.cs` | From the quad's `QuadProfile`: the most acceleration along the path for a speed and climb angle (thrust with prop-inflow falloff, gravity, drag), the most braking, the most sideways acceleration, the top speed |
| Racing-line baker | `Scripts/AI/RacingLineBaker.cs` | Starts from the course's flight line (or a spline through the gates on gate-only tracks), pins it through every gate opening (each opening probed with sphere checks for rungs and bars), fits the corridor to the real geometry, relaxes the line towards minimum curvature (2 m, optionally refined at 1 m for slaloms on bends), pushes any point closer than 0.45 m to geometry back out, then computes the speed profile: corner speed from grip, acceleration and braking capped at 15 / 12 m/s², a turn-rate (jerk) limit, slower through small openings |
| Baked line | `Scripts/AI/BotLine.cs`, `RacingLineAsset.cs` | Points every 0.5 m with tangent, curvature, speed, acceleration and the room left to each side; cached per track and size class in `Resources/RacingLines/`, checked by a hash at load and rebaked on the spot when the track changed |
| Planner | `Scripts/AI/LinePlanner.cs` | Where the bot wants to be now: the line point closest to it, the profile speed times the level's pace, smooth wander inside the room left, mistakes (a wide, slow exit), reaction time (turns noticed late), avoidance input, rejoin (fly back along the line to a missed gate) |
| Follower + stick synthesiser | `Scripts/AI/TrajectoryFollower.cs`, `RateInverse.cs` | Position / velocity PD plus acceleration feed-forward → thrust vector (gravity and current drag added) → wanted body up and heading → body rates → **inverse rate curves** → Acro sticks; thrust → throttle through the inverse motor model |
| One bot in a race | `Scripts/AI/BotRun.cs` | Own `QuadState` stepped by `QuadSimulator` at 1 kHz, controller at 250 Hz, `CollisionHook` against the track, own `LapTimer` over the course gates, crash recovery (respawn on the line before the gate, or before a whole slalom, already hovering), stuck watchdog, Adaptive pace |
| Levels | `Scripts/AI/BotDifficultyProfile.cs` | Rookie 0.50, Amateur 0.64, Pilot 0.76, Pro 0.86, Elite 0.95 of the profile speed, plus line noise, reaction time, stick noise, mistake chance, recovery time and aggression; Adaptive follows the player's recent lap time |
| Race mode | `Scripts/AI/BotRace.cs`, `BotRoster.cs`, `Game/WinnerReplay.cs`, `UI/RaceHud.cs` | 1-7 bots on the start grid, the same countdown, live positions and gaps (from checkpoint pass times), avoidance, optional midair collisions (off = ghost bots), names, colours and builds of the player's size class, results with every racer, winner replay (R / gamepad Y) |
| Settings | Settings > Race | Bots (0-7), bot level, midair collisions, track intro |

Lessons from tuning, kept in the code comments: the reaction delay must not sit inside the attitude loop (even 16 ms
made the quad flip); yaw control fighting the tilt was the main cause of tracking errors (yaw gain 1.5, no body-rate
feed-forward); hard braking (up to 50 m/s² by the envelope) swung the thrust nearly backwards and lost the line, so the
profile caps it at 12 m/s².

## Tools
- **Ushqyr > Bots > Bake Racing Lines**: bakes every race course for the 5" class in both line styles and keeps the
  first one an Elite bot flies cleanly on two seeds headless (else the one with the fewest crashes and misses).
- `BotHarness` (Editor): `Report(course, laps, seed, "Elite,Pilot")`, `Trace(...)`, `Gates(course)`,
  `LineClearance(course)` for headless flights and diagnostics.

## Results (headless, Elite, 2 laps, seeds 1 / 2)

| Course | Line | Laps (s) | Crashes | Missed gates |
|---|---|---|---|---|
| Open Field | smooth | 21.3 / 17.6 | 0 / 0 | 0 |
| Hangar 7 | smooth | 12.7 / 11.0 | 0 / 0 | 0 |
| Dockyard | smooth | 21.1 / 17.4 | 0 / 0 | 0 |
| Spline Test Loop | tight | 23.3 / 20.6 (seed 2: 47.0 / 27.8) | 0 / 4 | 0 |
| Stadium Oval | smooth | 22.9 / 21.1 | 0 / 0 | 0 |
| Serpent Arena | tight | 39.0 / 36.3 | 0 / 0 | 0 |
| Expo Sprint v2 | smooth | 66.9 / 65.2 (seed 2: 70.5 / 65.3) | 0 / 1 | 0 |
| Expo Pro v2 | smooth | 116.0 / 114.3 | 0 / 0 | 0 |
| Expo Classic Sprint | smooth | 61.4 / 59.7 | 0 / 0 | 0 |
| Expo Classic Pro | smooth | 98.4 / 96.7 | 0 / 0 | 0 |

Stadium Oval, 3 laps, all levels (seeds 1 and 3): Elite 21.1, Pilot 26.3-27.2, Rookie 40-42 s per flying lap, no
crashes. These are the first lap times measured for the v2 Expo routes too (Elite bot): about 65 s Sprint, 114 s Pro.

Acceptance tests (`Tests/EditMode/BotAcceptanceTests.cs`, all green, 257 tests in the suite):
- Elite completes every lap of all ten race courses (Expo v2 and the frozen Classic routes included) on 10 seeds with
  no missed gate;
- mean lap times strictly ordered Rookie > Amateur > Pilot > Pro > Elite (Stadium Oval, Open Field, Hangar 7);
- Adaptive settles within 5 % of a recorded pace (Open Field, Stadium Oval);
- seven bots cost **0.76 ms** per 90 fps frame (simulation, collisions, gates), under the 2 ms budget.

## Known gaps
- Spline Test Loop and Expo Sprint v2 still crash on some seeds (slaloms); they still finish every lap without a missed
  gate. The lower levels crash more on the Expo courses; Rookie does not reliably finish Expo Sprint v2.
- Race mode was checked in Play mode on the Stadium Oval with 5 Pilot bots (grid start, countdown, live standings
  and gaps, no errors); the results screen and the winner replay have not been checked at the end of a full race yet.
- Bots are **off by default** (Settings > Race) until race mode has been played through in a real session.
- Lines are baked for the 5" class only; other classes bake at load (a fraction of a second, not verified in flight).
- Player-to-bot collisions push the player only through `QuadVehicle.ApplyImpulse`; no damage model.
