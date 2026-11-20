# Rules and scoring

This page describes how the simulator judges an attempt. All numbers come from the organisers' rules file; the values
below are the defaults. The final numbers of the qualification (how many attempts, how many tracks, the limits) are
announced by the organisers.

## Gates

| | |
|---|---|
| Outer size | 250 × 250 cm |
| Opening | 195 × 195 cm, centred |
| Frame depth | 27.5 cm |
| LEDs | each gate has its own: colour, brightness, steady or blinking |

The gates stand on the floor. The order of the gates is not set: you may fly to any of them.

## What counts as a pass

A pass is counted when the **centre of the drone** has crossed the plane of the opening inside the 195 × 195 cm
opening and has flown on at least 30 cm beyond that plane.

- Poking into the opening and coming back is not a pass.
- Flying round the gate on the outside is not a pass.
- The time of a pass is the moment the plane is crossed.
- Some gates may be flown in one direction only (the track's author sets this). A pass against the allowed direction
  gives no point.

## Points

Every counted pass gives **1 point** if that gate is not among the **last four** that gave a point.

An example on a track of six gates: G1, G2, G3, G4, G5 give 5 points. G1 again gives a point, because four other
gates have given points since. But G5 → G4 → G5 gives only one point, for G4: G5 is still among the last four.

The conclusion: flying back and forth through a pair of gates is useless. You need a route of at least five different
gates.

## The attempt

1. The simulator is running, the drone stands on the start pad, the motors are off.
2. The program connects and gives the start (`sim.reset()`). The attempt time runs from this moment.
3. The program arms and flies.
4. The attempt ends for one of these reasons:

| Status | Reason | Points |
|---|---|---|
| `finished` | The program itself said it was done (`sim.done()`) | counted |
| `time_limit` | The attempt time ran out (default 120 s; a track may have its own) | counted |
| `stuck` | The drone did not move more than 5 cm for 10 seconds (not checked in the first 15 seconds before the first point) | counted |
| `timeout` | The program did not connect within 60 s or did not answer a step within 10 s of real time | counted |
| `disconnected` | The program disconnected or crashed | counted |
| `invalid` | A failure on the simulator's side | the attempt does not count |

Collisions are written into the attempt's log. No points are taken off for them, and the drone does not break. But if
the drone lies still for 10 seconds after a hit, the attempt ends with status `stuck`.

If no RC packets arrive for 500 ms, the failsafe triggers: the motors are cut until the arm switch is turned off and
on again.

## Time

In sync mode (the default) the attempt time is **simulator time**. It advances only when the program makes a step. A
slow computer or a long computation does not take away flight time and does not change the result. Only the real time
to answer one step is limited: 10 seconds.

One control step is 1/100 of a second. The camera gives 30 frames per second, so there is not a new frame on every
step.

## The leaderboard

1. More points rank higher.
2. With equal points, the one with the **smaller time of the last counted gate** ranks higher.

For each track the team's best attempt is taken. Points and time are added up over all tracks.

## Competition mode

| | Training | Competition |
|---|---|---|
| Sensor data | yes | yes |
| Ground-truth coordinates, velocity, score (`truth`) | on request | **no** |
| Track name in the handshake | yes | **no** |
| Restarting the attempt (`reset` again), choosing the seed | yes | **no**: one launch, one attempt |
| Faster-than-real-time run | yes | **no** |
| Recording of the attempt (logs, video, checksums) | optional | always |

The mode is set by the simulator. Nothing the program sends can change it.

## What is the same for everyone

- The physics, the flight controller and the sensor models are the same for all teams.
- All teams fly the same track with the same noise seed.
- The qualification tracks are hidden: they are neither in the Team Kit nor in the builds. The program must find the
  gates with its sensors, not remember the track.
- The solution runs in a closed container without internet ([docker.md](docker.md)).

## What is recorded

Every attempt in competition mode is saved: the result, the event log (every pass with the judge's decision and the
reason), the telemetry, the onboard and trackside video, the checksums of all files and the signature. The organisers
can review any attempt in the replay and re-check the result by re-simulating it from the recorded commands.
