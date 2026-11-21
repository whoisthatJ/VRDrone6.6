# Spike: MAVLink endpoint and QGroundControl (ED0, 21 November 2026)

**Question.** Can our own MAVLink endpoint satisfy QGroundControl (path A), or do we have to bundle the real ArduPilot
simulator (path B)?

**Result so far: path A looks sound, but QGroundControl itself has not been tried.** It is not installed on this PC,
so only the automated half of the check is done.

## What was built
`Assets/_Project/Scripts/Edu/Core/Mavlink.cs` (no engine types, about 500 lines):

- `MavlinkEndpoint`: MAVLink 2 over UDP, sends to port 14550 and answers whoever talks to it. It presents itself as an
  **ArduPilot quadrotor** (decision D8), with ArduCopter's flight-mode numbers, so a ground station shows the mode list.
- Served: heartbeat, system and battery status, attitude, position, GPS, home, HUD values, landed state, mission
  progress; the parameter protocol (23 parameters under ArduCopter names); the mission protocol (upload, download,
  clear); commands arm, disarm, take off, land, return, set mode, reposition, mission start, message requests, autopilot
  version; position targets for "go to".
- `IMavVehicle`: what the endpoint needs from the drone.
- `MavDrone` (`Scripts/Edu/Core/MavDrone.cs`): joins the endpoint to the simulator's autopilot; ArduCopter's modes map
  to ours (Stabilize, AltHold, Loiter for a person; Guided, Auto, RTL, Land for the autopilot).
- `MavTestVehicle`: a stand-in that moves in straight lines. **It is not flight physics** and exists only for this test.

## Automated check (`Docs/spikes/data/mav_check.py`, pymavlink as the ground station)
24 of 25 checks passed outside Unity. The one failure was in the test, not the endpoint: it expected to see mission
item 1 reported, and the take-off item finishes between two once-a-second reports. The script is corrected.

| Group | Checks | Result |
|---|---|---|
| Connection | heartbeat within 5 s; seen as an ArduPilot quadrotor | pass |
| Parameters | full list (23 of 23); a change is applied and echoed | pass |
| Telemetry | attitude, position, GPS, status, battery, home, HUD | pass |
| Guided actions | take-off refused when disarmed; mode change; arm; take off to 5 m; go to a point 20 m away; unknown command answered "unsupported" | pass |
| Mission | 6 items uploaded, downloaded back unchanged, flown in AUTO to the end, landed at home, disarmed | pass |

The check was then repeated in two harder settings, **25 of 25 in both**:

| Setting | What it proves |
|---|---|
| The endpoint inside a Windows player build (with the stand-in drone) | Unity's runtime sends and receives the UDP frames correctly |
| The endpoint on the **real flight model through the autopilot** (`MavDrone` + `Autopilot` + `QuadSimulator`, outside Unity, in real time) | Arm, take-off, "go to", a 6-item mission and return-to-launch are flown by the real drone physics, not by a stand-in |

## Not done
- **QGroundControl.** The 8-point checklist of `EDU_PLAN.md` §8 needs the program installed. The likely trouble spots:
  QGroundControl's ArduPilot pages expect many more parameters than 23 (it may warn about missing ones), and it asks
  for message streams in ArduPilot's own way.
- MAVLink 1 frames and signed frames are ignored.
- One ground station per upload; no fence or rally points (answered as empty lists).

## Recommendation
Stay on **path A**. Decide finally after the 5-minute QGroundControl try below; path B (ArduPilot's simulator through
its JSON interface) stays the fallback and would bring a GPLv3 program into the installer.

## 5-minute try in QGroundControl
1. Install the current stable QGroundControl and start it. Write down its version.
2. Start the spike player: `Builds\EduSpike\Ushqyr.exe -edu-spike C:\Temp\edu -edu-spike-serve 600`.
3. Within 5 s the vehicle should appear with no error pop-up. Note any pop-up text exactly.
4. Fly view: attitude, height, speed, battery and the home marker are shown; the map shows the drone near the Expo
   site in Astana (a blank map is expected without internet).
5. Mode list: open it and switch to Guided.
6. Arm, then "Takeoff" (slider). The drone climbs.
7. Click on the map → "Go to location".
8. Plan view: 4 waypoints with take-off and return; upload; back in Fly view start the mission.
9. Parameters page: find `RTL_ALT`, change it, check it stays changed.
10. Close the player: QGroundControl should say that communication is lost.
