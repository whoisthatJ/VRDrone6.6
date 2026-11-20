# Solution in Docker

In the qualification your solution is run as a Docker image. This page shows how to build the image and check it on
your own machine under the same conditions as on the server.

## What you need

- Docker (on Windows: Docker Desktop with WSL2).
- From the Team Kit: the folders `bench/template`, `bench/runner`, `bench/docker` and the Linux build of the simulator, `Builds/BenchServer`.
- The simulator image `ushqyr-bench-server` and the base image `ushqyr-bench-base:cpu`. One command builds them (see
  below).

```
pip install ./bench/runner
ushqyr-bench build-images
```

`build-images` takes the Linux build of the simulator from `Builds/BenchServer`. Teams get it ready-made with the
Team Kit; the organisers build it in Unity: menu **Ushqyr / Build / Build Bench Server (Linux)**.

## The template

Copy `bench/template` and write your code in `solution.py`.

| File | What it is |
|---|---|
| `Dockerfile` | How the image is built. Base: `ushqyr-bench-base:cpu` (Python 3.11, SDK, NumPy, OpenCV) |
| `solution.py` | Your solution. In the template the drone takes off and hovers |
| `requirements.txt` | Additional Python packages |

The simulator's address and the token come in the environment variables `USHQYR_BENCH_HOST`, `USHQYR_BENCH_PORT`,
`USHQYR_BENCH_TOKEN`. `Sim.connect()` reads them itself. The program must exit by itself when the attempt is over.

## The check

```
ushqyr-bench submit-check ./my_solution --track H1_Oval --logs
```

The command builds the image, starts the simulator in competition mode and your solution beside it, waits for the end
of the attempt and prints the result. The attempt folder (logs, videos, checksums) appears in `bench_results`.

A ready-made example: `ushqyr-bench submit-check bench/examples/color_pid`.

## Conditions in the container

The solution runs in a closed container. Plan for this in advance.

| Condition | Value |
|---|---|
| Network | Only the simulator. No internet and no DNS: all packages and model weights must be inside the image |
| Files | The file system is read-only. You can write to `/tmp` (256 MB, in memory) |
| User | Not root (uid 10001), no privileges |
| Memory | 4 GB |
| Processor | 4 cores |
| Processes | at most 256 |
| Image size | at most 8 GB |
| Video card | none (by default) |

The numbers in the table are provisional: the final limits are set by the organisers.

## Time

| Where it runs | How long the check of a 120 s attempt takes |
|---|---|
| Docker Desktop on Windows with a video card (tested on a GTX 1650 Ti) | about 4 minutes, including building the image and recording the videos |
| No video card (software rendering) | about 40 minutes on 8 cores |

On Windows `submit-check` connects the video card to the simulator's container by itself (option `--sim-gpu auto`).
Without a video card the simulator renders in software: the command turns on the simplest graphics and does not
record the trackside video, otherwise the attempt would take hours.

The speed does not affect the result: in sync mode the attempt time is counted by the simulator's clock, and the
simulator waits for your command on every step.

## Common errors

| Message | Cause |
|---|---|
| `the Docker engine is not running` | Start Docker Desktop |
| `the base image ... is not built yet` | Run `ushqyr-bench build-images` |
| `the attempt produced no result` | The solution did not connect or crashed. Look at the output with `--logs` |
| `the solution did not exit when the attempt ended` | The program did not exit by itself after the end of the attempt |
| An error in `pip install` or in downloading weights during the flight | There is no internet in the container. Install everything in the `Dockerfile` |
