# AI Bench: documentation

AI Bench is a mode of the Ushqyr simulator in which a program flies the drone. The program receives only the data of
the onboard sensors and answers with RC channels. The teams' programs fly a track of LED gates; the more gates in the
time allowed, the higher the place.

## For teams

| Page | What it covers |
|---|---|
| [Quick start](quickstart.md) | Installing and running the ready-made example in 30 minutes, Windows and Linux |
| [Rules and scoring](rules.md) | What counts as a pass, how an attempt goes, how the leaderboard is built |
| [Drone and sensors](drone.md) | The specifications of the drone, camera, IMU, rangefinder, LiDAR and RC link; your own drone (AI CORE) |
| [Python SDK](sdk.md) | All classes and methods |
| [Tracks and the editor](tracks.md) | Ready-made tracks, venues, the editor, random tracks |
| [Datasets](dataset.md) | Labelled images for training a detector |
| [Solution in Docker](docker.md) | How to package a solution and check it the same way the server does |
| [ROS 2](ros2.md) | Topics, parameters and timing of the ROS 2 bridge |
| [If something does not work](troubleshooting.md) | Common errors and what to do about them |
| [Protocol](protocol.md) | The message format, if the client is not written in Python |

## For organisers

| Page | What it covers |
|---|---|
| [Organiser guide](organizer.md) | Running attempts, the rules file, hidden tracks, checking results, the batch run |

## Where to start

1. Go through the [quick start](quickstart.md): the example must fly the track by itself.
2. Read the [rules](rules.md) and [drone and sensors](drone.md).
3. Copy the example `bench/examples/color_pid` and change it.
4. Before submitting, check the solution with `ushqyr-bench submit-check` ([Docker](docker.md)).

## What is not final yet

- The model of the organisers' drone is a placeholder: it is not calibrated against the real drone
  (`calibrated=false` in the handshake).
- The limits for solutions (memory, processor, image size), the number of attempts and the number of tracks are set by
  the organisers. The numbers on these pages are the defaults.
