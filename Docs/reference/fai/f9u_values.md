# FAI F9U (drone racing): the rules the simulator follows

**Source.** FAI Sporting Code, Section 4 Aeromodelling, Volume F9 Drone Sports, **2026 Edition, Version 2, effective
1 June 2026** (`CIAM-F9-Drones-2026-06-01.pdf` in this folder; drone racing is pages 14 to 27, the brackets are
Annexes A to C on pages 42 to 51). Read on 22 November 2026. F9U is an **official class**. The bracket of Annex C
(16 pilots) was read from the page itself; Annexes A (64) and B (32) were **not** read.

The racing rules file of the simulator takes its values from this page.

## The race
| What | Rule | Paragraph |
|---|---|---|
| Circuit | a closed loop, **three laps** | F9U.2 |
| Pilots in a race | at most **4**; qualifying rounds may fly **6** in a group | F9U.6 |
| Flight time | **3 minutes** to complete 3 consecutive laps, in every stage | F9U.6 |
| Laps that count | only finished laps; an unfinished lap gives no result, placing or tie-break | F9U.6.1 |
| Timing, qualification | starts when the model passes the timing gate (after take-off it must go straight to it) | F9U.6.1 |
| Timing, elimination and final | starts **at the start signal** | F9U.6.1 |
| More than three laps | may be disqualified from the race | F9U.9.1 |

## The start
- The starter says "Pilots, arm your quads".
- After **2 to 4 seconds** one tone starts the race. **No audible countdown.**
- **False start:** a model that leaves its start position before the signal *and goes through the first gate* is
  disqualified from the race. The race goes on for the others where possible.
- A mid-air collision before the first gate that changes a model's path: the starter may call a restart, once per
  race; not if the models went on through the first gate. (F9U.6.2, F9U.8.2)

## Faults
- A missed obstacle, pylon, flag or a circuit cut: **the lap is not validated**, unless the pilot corrects the mistake
  at once and safely; then the lap counts. A collision with another model during that correction: disqualified from
  the race. (F9U.7.2)
- After a crash the model stays on the ground with motors off until the race ends. (F9U.7.3)

## Qualification (F9U.6.3)
- At least **3 rounds**. Groups by blind draw; a different draw each round is recommended, and one draw may not be
  used for more than three rounds. Short groups go to the end of the round; at least 3 pilots a group where possible.
- **Option 1, fastest 3 consecutive laps:** each round's result is the time for the 3 laps (or the laps completed and
  their time). Ranking by each pilot's best result: 3 laps ahead of 2 laps ahead of 1 lap; no time, last. Ties by the
  second best result, then the third. Not recommended with 3 rounds or fewer.
- **Option 2, average of the 3 best lap times** over all rounds (they may come from different rounds). Pilots with only
  2 lap times are ranked after, by the average of the two; then those with 1; no time, last. Ties by the fourth best
  lap time, then the fifth, and so on.
- A reflight is possible only in qualification, for an incident outside the pilot's control; the original flight is
  then cancelled. (F9U.8.1)

## Placing in an elimination or final race (F9U.6.4, F9U.6.5, F9U.9.1)
1. by the time to complete 3 laps;
2. then those who did not finish, by laps completed and the time for those laps (a pilot with no finished lap is
   still ahead of a disqualified one);
3. then the disqualified; several of them, in the order of the qualification ranking;
4. then those who did not fly.

The **two best** of each race go on. A tie for second place is split by the qualification ranking.

## Scenario C: 16 pilots from qualification (Annex C)
**First elimination round** (numbers are places after qualification):

| Race 1 | Race 2 | Race 3 | Race 4 |
|---|---|---|---|
| 1, 8, 9, 16 | 4, 5, 12, 13 | 3, 6, 10, 14 | 2, 7, 11, 15 |

With fewer than 16 pilots the missing places are simply empty: some races fly with 3. (F9U.6.4, note)

**Without double elimination**
- Semi 1: 1st and 2nd of races 1 and 2. Semi 2: 1st and 2nd of races 3 and 4.
- Final: 1st and 2nd of each semi (places 1 to 4). Small final: 3rd and 4th of each semi (places 5 to 8).
- Places 9 to 16: the 3rd and 4th of races 1 to 4, in the order of the qualification ranking.

**With double elimination**

| Race | Who flies |
|---|---|
| 5 | 4th of race 1, 3rd of race 2, 3rd of race 3, 4th of race 4 |
| 6 | 4th of race 2, 3rd of race 1, 3rd of race 4, 4th of race 3 |
| 7 | 1st and 2nd of races 1 and 2 |
| 8 | 1st and 2nd of races 3 and 4 |
| 9 | 4th of race 8, 2nd of race 6, 1st of race 5, 3rd of race 7 |
| 10 | 4th of race 7, 2nd of race 5, 1st of race 6, 3rd of race 8 |
| 11 | 1st and 2nd of races 9 and 10 |
| 12 | 1st and 2nd of races 7 and 8 |
| 13 | 4th of race 12, 2nd of race 11, 1st of race 11, 3rd of race 12 |
| Final | 1st and 2nd of race 12, 1st and 2nd of race 13 |

Places: 1 to 4 from the final; 5 and 6 the 3rd and 4th of race 13; 7 and 8 the 3rd and 4th of race 11; 9 to 12 the
3rd and 4th of races 9 and 10, in the order of the qualification ranking; 13 to 16 the 3rd and 4th of races 5 and 6,
in that order too.

## The final (F9U.6.5)
- One race unless the organiser announced otherwise.
- **Successive finals:** the final is over as soon as one finalist has won **two** final races; that pilot wins the
  event. For places 2 to 4, each final race gives 1 point to the first, 2 to the second, 3 to the third, 4 to the
  fourth; the lower sum is placed higher; a tie is split by the placing in the last final race. Not for the small
  final.

## Small events (F9U.6, note)
With fewer than 16 pilots the event may be one stage of a fixed number of rounds: in each race a pilot gets points
equal to their place, and the final placing is by the sum of the points over all rounds (the lower the better).

## What the simulator does of its own, outside the FAI text
- **Eight pilots or fewer in an elimination:** the FAI scenarios start at 16. With 5 to 8 pilots the simulator starts
  at the semi-finals (semi 1: places 1, 4, 5, 8; semi 2: places 2, 3, 6, 7), with 4 or fewer at the final.
- "Races of up to 8 with the top half advancing" (`EDU_PLAN.md` §5) is a preset of ours, not an FAI format.
- 32 and 64 pilots: not built, because Annexes A and B were not read.
