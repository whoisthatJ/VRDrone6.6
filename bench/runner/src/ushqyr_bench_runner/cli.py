"""ushqyr-bench: organiser tools for attempt folders.

    ushqyr-bench verify <attempt folder> [--key key.file]
    ushqyr-bench resim <attempt folder>              (needs the simulator running in training mode on the same track)
    ushqyr-bench leaderboard <results folder> [--track ID] [--csv results.csv] [--board leaderboard.csv] [--semicolon]
    ushqyr-bench check-build <build or kit folder> --hidden <folder with the hidden tracks>
    ushqyr-bench validate-drone <drone.yaml> [--limits class_limits.json]
    ushqyr-bench dataset --sim Ushqyr.exe --track H1_Oval --out <folder> [--count 500] [--seed 1] [--masks]
    ushqyr-bench dataset-check <dataset folder> [--draw 100]
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import List, Optional

from . import attempt


def _key(path: Optional[str]) -> Optional[bytes]:
    return Path(path).read_bytes() if path else None


def cmd_verify(args: argparse.Namespace) -> int:
    folder = Path(args.folder)
    state, detail = attempt.verify(folder, _key(args.key))
    print(f"{state}: {detail}")
    if state in (attempt.VERIFIED, attempt.CHECKSUMS_ONLY):
        r = attempt.load_result(folder)
        print(f"  team {r['team']}, track {r['track_id']}, attempt {r['attempt']}: {r['points']} point(s), "
              f"time {r['time']:.3f} s, status {r['status']}")
    if state == attempt.VERIFIED:
        return 0
    if state == attempt.CHECKSUMS_ONLY:
        # Without the key nothing proves the manifest itself is the original one.
        return 0 if args.allow_unsigned else 2
    return 1


def cmd_resim(args: argparse.Namespace) -> int:
    """Flies the recorded RC packets again with the same seed and compares the score."""
    from ushqyr_bench import RC, Sim  # the SDK is needed only here

    folder = Path(args.folder)
    result = attempt.load_result(folder)
    packets = attempt.commands(folder)
    with Sim.connect(args.host, args.port, truth=True, realtime=False) as sim:
        if sim.mode != "training":
            print("resim needs the simulator in training mode (it reads the score from the ground truth)")
            return 2
        track = sim.info.get("track", {})
        if track.get("hash") != result["track_hash"]:
            print(f"the simulator has another track loaded ({track.get('id')});")
            print("start it with -bench-track <attempt folder>/track.benchtrack.json")
            return 2
        obs = sim.reset(int(result["seed"]))
        for packet in packets:
            if not sim.running:
                break
            obs = sim.step(RC.from_us(packet) if packet is not None else None)
        points, time = int(obs.truth["points"]), float(obs.truth["score_time"])
    same = points == int(result["points"]) and abs(time - float(result["time"])) < 1e-6
    print(f"recorded: {result['points']} point(s), time {float(result['time']):.6f} s")
    print(f"replayed: {points} point(s), time {time:.6f} s")
    print("same result" if same else "DIFFERENT result")
    return 0 if same else 1


def cmd_leaderboard(args: argparse.Namespace) -> int:
    root = Path(args.root)
    results = attempt.load_results(root)
    if not results:
        print(f"no results under {root}")
        return 1
    rows = attempt.rank(results, args.track)
    print(f"{'#':>3}  {'team':<24} {'points':>6}  {'time, s':>9}")
    for r in rows:
        print(f"{r.rank:>3}  {r.team:<24} {r.points:>6}  {r.time:>9.3f}")
    if args.csv:
        attempt.write_results_csv(Path(args.csv), results, args.semicolon)
        print(f"all attempts: {args.csv}")
    if args.board:
        attempt.write_leaderboard_csv(Path(args.board), rows, args.semicolon)
        print(f"leaderboard: {args.board}")
    return 0


def cmd_check_build(args: argparse.Namespace) -> int:
    """Makes sure no hidden track got into something that will be handed out."""
    leaks = attempt.find_hidden_tracks(Path(args.build), Path(args.hidden))
    hidden = sorted(Path(args.hidden).rglob("*.benchtrack.json"))
    print(f"{len(hidden)} hidden track(s) checked against {args.build}")
    for leak in leaks:
        print("  LEAK: " + leak)
    print("clean" if not leaks else f"{len(leaks)} leak(s)")
    if not hidden:
        print("no hidden tracks found in that folder: nothing was checked")
        return 2
    return 1 if leaks else 0


def cmd_validate_drone(args: argparse.Namespace) -> int:
    """Checks a team drone file against the class limits, the way the simulator will."""
    from . import drone

    text = Path(args.file).read_text(encoding="utf-8")
    errors = drone.validate(text, drone.load_limits(Path(args.limits) if args.limits else None))
    if errors:
        print(f"NOT accepted: {len(errors)} problem(s)")
        for e in errors:
            print("  - " + e)
        return 1
    print("accepted")
    print("  config hash: " + drone.config_hash(text))
    return 0


def cmd_dataset(args: argparse.Namespace) -> int:
    """Starts the simulator in dataset mode and waits for the dataset."""
    from . import dataset

    code = dataset.make(Path(args.sim), args.track, Path(args.out), args.count, args.seed, args.profile, args.masks, args.timeout)
    if code != 0 or not (Path(args.out) / "dataset.json").is_file():
        print(f"the simulator did not finish the dataset (exit code {code})")
        return 1
    return cmd_dataset_check(argparse.Namespace(folder=args.out, draw=0))


def cmd_dataset_check(args: argparse.Namespace) -> int:
    from . import dataset

    folder = Path(args.folder)
    problems, summary = dataset.check(folder)
    if summary:
        print(f"{summary['images']} images, {summary['gates']} labelled gates")
        print(f"label check by the simulator: {summary['boxes_within_2px']} of {summary['boxes_checked']} boxes within 2 px of "
              f"the painted outline, {summary['corners_on_gate']} of {summary['corners_checked']} visible corners on their gate")
    for p in problems:
        print("  PROBLEM: " + p)
    if args.draw:
        try:
            print(f"{args.draw} labelled pictures to look at: {dataset.draw(folder, args.draw)}")
        except ImportError:
            print("drawing needs opencv: pip install opencv-python")
    print("consistent" if not problems else f"{len(problems)} problem(s)")
    return 1 if problems else 0


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="ushqyr-bench", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("verify", help="check an attempt folder against its manifest and signature")
    p.add_argument("folder")
    p.add_argument("--key", help="the organisers' key file; without it only the checksums are checked")
    p.add_argument("--allow-unsigned", action="store_true",
                   help="exit 0 when the checksums match but the signature could not be checked")
    p.set_defaults(run=cmd_verify)

    p = sub.add_parser("resim", help="replay the recorded RC commands and compare the score")
    p.add_argument("folder")
    p.add_argument("--host", default=None)
    p.add_argument("--port", type=int, default=None)
    p.set_defaults(run=cmd_resim)

    p = sub.add_parser("leaderboard", help="rank the results of a folder; export CSV")
    p.add_argument("root")
    p.add_argument("--track", default=None, help="one track's board (default: all tracks added up)")
    p.add_argument("--csv", help="write every attempt to this CSV file")
    p.add_argument("--board", help="write the ranked board to this CSV file")
    p.add_argument("--semicolon", action="store_true",
                   help="';' as separator and ',' as decimal mark (spreadsheets in Russian or Kazakh regions)")
    p.set_defaults(run=cmd_leaderboard)

    p = sub.add_parser("check-build", help="make sure a build or kit folder contains no hidden track")
    p.add_argument("build")
    p.add_argument("--hidden", required=True, help="the private folder with the hidden *.benchtrack.json files")
    p.set_defaults(run=cmd_check_build)

    p = sub.add_parser("validate-drone", help="check a team drone file (class AI CORE) against the class limits")
    p.add_argument("file")
    p.add_argument("--limits", help="the organisers' class_limits.json (default: the simulator's built-in limits)")
    p.set_defaults(run=cmd_validate_drone)

    p = sub.add_parser("dataset", help="make a labelled gate dataset (YOLO and COCO) with the simulator")
    p.add_argument("--sim", required=True, help="path to the simulator (Ushqyr.exe)")
    p.add_argument("--track", default="H1_Oval", help="a public track's name or a track file")
    p.add_argument("--out", required=True)
    p.add_argument("--count", type=int, default=500)
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--profile", default="strong", choices=["none", "mild", "strong"], help="how much the scene varies")
    p.add_argument("--masks", action="store_true", help="also write a mask per image: which pixels belong to which gate")
    p.add_argument("--timeout", type=float, default=3600.0)
    p.set_defaults(run=cmd_dataset)

    p = sub.add_parser("dataset-check", help="check a dataset's files against each other; draw labels over pictures")
    p.add_argument("folder")
    p.add_argument("--draw", type=int, default=0, help="draw the labels over this many random pictures (needs opencv)")
    p.set_defaults(run=cmd_dataset_check)

    args = parser.parse_args(argv)
    return int(args.run(args))


if __name__ == "__main__":
    sys.exit(main())
