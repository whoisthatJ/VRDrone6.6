"""Flies the sample several times with different noise seeds and reports how often it completes a lap.

    python evaluate.py                    # 10 runs, a lap = 6 gates (H1_Oval)
    python evaluate.py --runs 5 --gates 8

Training mode only: it reads the score from the ground truth, which a competition run does not provide.
The acceptance bar for the sample is a full lap in at least 9 of 10 runs.
"""
from __future__ import annotations

import argparse
import sys
import time

from color_pid import ColorPid
from ushqyr_bench import Sim


def fly(seed: int, gates: int, limit: float, host: str | None, port: int | None) -> tuple[int, float, float, tuple]:
    """One attempt. Returns points, simulator time of the last scored gate, speed-up, final position."""
    with Sim.connect(host, port, truth=True, realtime=False) as sim:
        if sim.mode != "training":
            raise SystemExit("evaluate.py needs the simulator in training mode")
        pilot = ColorPid(sim.info)
        obs = sim.reset(seed)
        started = time.perf_counter()
        while sim.running and obs.t < limit and obs.truth["points"] < gates:
            obs = sim.step(pilot.update(obs))
        speed = obs.t / max(1e-6, time.perf_counter() - started)
        position = tuple(round(v, 4) for v in obs.truth["position"])
        return int(obs.truth["points"]), float(obs.truth["score_time"]), speed, position


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--runs", type=int, default=10)
    parser.add_argument("--gates", type=int, default=6, help="gates in one lap")
    parser.add_argument("--limit", type=float, default=90.0, help="give up after this much simulator time (s)")
    parser.add_argument("--first-seed", type=int, default=1)
    parser.add_argument("--repeat", action="store_true", help="fly the first seed twice and compare (determinism check)")
    parser.add_argument("--host", default=None)
    parser.add_argument("--port", type=int, default=None)
    args = parser.parse_args()

    laps = 0
    for i in range(args.runs):
        seed = args.first_seed + i
        points, score_time, speed, _ = fly(seed, args.gates, args.limit, args.host, args.port)
        ok = points >= args.gates
        laps += ok
        lap = "LAP" if ok else "no lap"
        print(f"seed {seed:3d}: {points} gate(s), last at {score_time:6.2f} s  {lap}  ({speed:.1f}x real time)")
    print(f"full lap in {laps} of {args.runs} runs")

    same = True
    if args.repeat:
        a = fly(args.first_seed, args.gates, args.limit, args.host, args.port)
        b = fly(args.first_seed, args.gates, args.limit, args.host, args.port)
        same = (a[0], a[1], a[3]) == (b[0], b[1], b[3])
        print(f"same seed twice: {'identical' if same else 'DIFFERENT'}  {a[0]} gates at {a[1]:.3f} s, end {a[3]}  |  "
              f"{b[0]} gates at {b[1]:.3f} s, end {b[3]}")
    sys.exit(0 if laps * 10 >= args.runs * 9 and same else 1)


if __name__ == "__main__":
    main()
