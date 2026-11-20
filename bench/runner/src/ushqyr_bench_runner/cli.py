"""ushqyr-bench: organiser tools for attempt folders.

    ushqyr-bench verify <attempt folder> [--key key.file]
    ushqyr-bench resim <attempt folder>              (needs the simulator running in training mode on the same track)
    ushqyr-bench leaderboard <results folder> [--track ID] [--csv results.csv] [--board leaderboard.csv] [--semicolon]
    ushqyr-bench check-build <build or kit folder> --hidden <folder with the hidden tracks>
    ushqyr-bench validate-drone <drone.yaml> [--limits class_limits.json]
    ushqyr-bench dataset --sim Ushqyr.exe --track H1_Oval --out <folder> [--count 500] [--seed 1] [--masks]
    ushqyr-bench dataset-check <dataset folder> [--draw 100]
    ushqyr-bench build-images [--build Builds/BenchServer]     (Docker: the simulator image and the solutions' base image)
    ushqyr-bench submit-check <solution folder or image.tar> [--track H1_Oval] [--seed 1] [--out results]
    ushqyr-bench batch --submissions submissions.yaml --tracks tracks.yaml --out results [--rules rules.json] [--key key.file]
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


def _bench_root() -> Path:
    """The bench/ folder: where this file lives in a checkout (bench/runner/src/ushqyr_bench_runner/cli.py), or, when
    the tool was installed with pip, the bench/ folder of the kit the command is run from."""
    here = Path(__file__).resolve().parents[3]
    if (here / "docker").is_dir():
        return here
    cwd = Path.cwd()
    for folder in [cwd, *cwd.parents]:
        for candidate in (folder / "bench", folder):
            if (candidate / "docker" / "base").is_dir() and (candidate / "sdk").is_dir():
                return candidate
    return here


def cmd_make_kit(args: argparse.Namespace) -> int:
    """Assembles a Team Kit and proves no hidden track is in it."""
    from . import kit

    repo = Path(args.repo) if args.repo else _bench_root().parent
    try:
        version = args.version or (repo / "Version.txt").read_text(encoding="utf-8").strip()
        out = Path(args.out)
        kit.make(Path(args.build), repo, out, version, Path(args.server) if args.server else None, archive=False)
        if args.hidden:
            leaks = attempt.find_hidden_tracks(out, Path(args.hidden))
            for leak in leaks:
                print("  LEAK: " + leak)
            if leaks:
                print(f"{len(leaks)} hidden track(s) in the kit: not archived")
                return 1
            print("no hidden track in the kit")
        else:
            print("not checked for hidden tracks (no --hidden folder given)")
        if not args.no_zip:
            kit.make_archive(out)
    except (kit.KitError, OSError) as e:
        print("error:", e)
        return 2
    return 0


def cmd_build_images(args: argparse.Namespace) -> int:
    from . import sandbox

    root = Path(args.bench) if args.bench else _bench_root()
    try:
        sandbox.require_engine()
        print(f"building {sandbox.BASE_IMAGE} ...")
        sandbox.build_base(root)
        for distro in args.ros2 or []:
            print(f"building ushqyr-bench-ros2:{distro} ...")
            sandbox.build_ros2(root, distro)
        if args.base_only or args.ros2:
            print("done")
            return 0
        build = Path(args.build) if args.build else root.parent / "Builds" / "BenchServer"
        print(f"building {sandbox.SERVER_IMAGE} from {build} ...")
        sandbox.build_server(root, build)
    except sandbox.SandboxError as e:
        print("error:", e)
        return 2
    print("done")
    return 0


def cmd_submit_check(args: argparse.Namespace) -> int:
    """Builds a solution's image and flies one competition attempt with it, exactly as the server will."""
    from . import sandbox

    limits = sandbox.Limits(memory=args.memory, cpus=args.cpus, wall_seconds=args.wall, gpu=args.gpu)
    source = Path(args.solution)
    built = False
    try:
        sandbox.require_engine()
        if source.is_dir():
            print(f"building the image from {source} ...")
            image, size = sandbox.build_solution(source, limits, args.build_arg)
            built = True
        else:
            image = sandbox.load_solution(source)
            size = sandbox.image_size(image)
        print(f"image: {size / 1024**2:.0f} MB (limit {limits.image_bytes / 1024**2:.0f} MB)")
        if size > limits.image_bytes:
            print("FAILED: the image is larger than the limit")
            return 1
        sim_gpu = args.sim_gpu == "on" or (args.sim_gpu == "auto" and sandbox.wsl_gpu())
        graphics = args.graphics or (None if sim_gpu else "Low")
        video = True if args.video else None if (args.rules or sim_gpu) else False
        print(f"flying one competition attempt on {args.track}, seed {args.seed} ...")
        if not sim_gpu:
            print("note: the simulator draws without a GPU: lowest graphics, no trackside video, and far slower than real time")
        try:
            o = sandbox.run_attempt(image, Path(args.out), args.track, args.seed, team=args.team, limits=limits,
                                    rules=Path(args.rules) if args.rules else None, key=Path(args.key) if args.key else None,
                                    graphics=graphics, video=video, sim_gpu=sim_gpu)
        finally:
            if built:
                sandbox.docker("image", "rm", "-f", image, check=False, timeout=120)
    except sandbox.SandboxError as e:
        print("error:", e)
        return 2
    for note in o.notes:
        print("note:", note)
    if args.logs or o.result is None:
        print("---- the solution printed ----")
        print(o.solution_log.strip()[-4000:] or "(nothing)")
    if o.result is None:
        print("---- the simulator printed (end) ----")
        print(o.simulator_log.strip()[-3000:] or "(nothing)")
        print("FAILED: the attempt produced no result")
        return 1
    r = o.result
    state, detail = attempt.verify(o.folder, _key(args.key))
    print(f"result: {r['status']}, {r['points']} point(s), time {r['time']:.2f} s, simulator time {r['sim_time']:.2f} s")
    print(f"attempt folder: {o.folder}  (files: {state}{', ' + detail if detail else ''})")
    print(f"the solution exited with code {o.solution_exit}")
    flew = r["sim_time"] > 0.0
    if not flew:
        print("the solution never started the attempt (it did not connect, or crashed first): see --logs")
    ok = state in (attempt.VERIFIED, attempt.CHECKSUMS_ONLY) and r["status"] != "error" and not o.notes and flew
    print("OK: the solution runs under the server's conditions" if ok else "FAILED")
    return 0 if ok else 1


def cmd_batch(args: argparse.Namespace) -> int:
    """Every team on every track, unattended; one results file."""
    from . import batch, sandbox

    limits = sandbox.Limits(memory=args.memory, cpus=args.cpus, wall_seconds=args.wall)
    try:
        submissions = batch.load_submissions(Path(args.submissions))
        plan = batch.load_plan(Path(args.tracks))
        sandbox.require_engine()
        if not sandbox.image_exists(sandbox.SERVER_IMAGE):
            raise sandbox.SandboxError(f"the simulator image {sandbox.SERVER_IMAGE} is not built yet: run `ushqyr-bench build-images`")
        sim_gpu = args.sim_gpu == "on" or (args.sim_gpu == "auto" and sandbox.wsl_gpu())
        summary = batch.run(submissions, plan, Path(args.out), limits, rules=Path(args.rules) if args.rules else None,
                            key=Path(args.key) if args.key else None, workers=args.workers, retries=args.retries,
                            graphics=args.graphics, video=False if args.no_video else None, sim_gpu=sim_gpu)
    except (batch.BatchError, sandbox.SandboxError, OSError, ValueError) as e:
        print("error:", e)
        return 2
    return 0 if summary["flown"] == summary["jobs"] else 1


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

    p = sub.add_parser("make-kit", help="assemble a Team Kit: simulator build + bench/ + documentation")
    p.add_argument("--build", required=True, help="folder of the Team Kit build (Builds/TeamKit/Windows or .../Linux)")
    p.add_argument("--out", required=True, help="the kit folder to create; the archive is written beside it")
    p.add_argument("--repo", help="the repository root (default: found from this tool)")
    p.add_argument("--server", help="also include this Linux Bench Server build, for `submit-check`")
    p.add_argument("--hidden", help="folder of hidden tracks: fail if any of them is in the kit")
    p.add_argument("--version", help="default: Version.txt")
    p.add_argument("--no-zip", action="store_true")
    p.set_defaults(run=cmd_make_kit)

    p = sub.add_parser("build-images", help="Docker: build the simulator image and the base image for solutions")
    p.add_argument("--build", help="folder of the Linux Bench Server build (default: Builds/BenchServer)")
    p.add_argument("--bench", help="the bench/ folder (default: next to this tool)")
    p.add_argument("--base-only", action="store_true", help="only the base image for solutions")
    p.add_argument("--ros2", action="append", metavar="DISTRO", help="also the ROS 2 base image (humble, jazzy); skips the simulator")
    p.set_defaults(run=cmd_build_images)

    p = sub.add_parser("submit-check", help="Docker: build a solution and fly one competition attempt, as the server will")
    p.add_argument("solution", help="folder with a Dockerfile, or a `docker save` file")
    p.add_argument("--track", default="H1_Oval")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--team", default="check")
    p.add_argument("--out", default="bench_results")
    p.add_argument("--rules", help="rules file (default: the one in the simulator image)")
    p.add_argument("--key", help="signing key file (organisers)")
    p.add_argument("--memory", default="4g")
    p.add_argument("--cpus", type=float, default=4.0)
    p.add_argument("--wall", type=float, default=1800.0, help="real seconds allowed for the attempt")
    p.add_argument("--gpu", action="store_true", help="give the solution the GPU")
    p.add_argument("--logs", action="store_true", help="print what the solution printed")
    p.add_argument("--build-arg", action="append", metavar="NAME=VALUE", help="passed to `docker build`")
    p.add_argument("--sim-gpu", default="auto", choices=["auto", "on", "off"],
                   help="let the simulator draw on the video card (auto: yes on Docker Desktop for Windows)")
    p.add_argument("--graphics", choices=["Low", "Medium", "High", "Ultra"],
                   help="graphics preset of the simulator (default: the build's own with a GPU, Low without)")
    p.add_argument("--video", action="store_true", help="record the trackside video even without a GPU")
    p.set_defaults(run=cmd_submit_check)

    p = sub.add_parser("batch", help="Docker: every solution on every track, into one results file")
    p.add_argument("--submissions", required=True, help="YAML or JSON: team and image, archive or folder")
    p.add_argument("--tracks", required=True, help="YAML or JSON: tracks, attempts, seed, folder of hidden tracks")
    p.add_argument("--out", required=True)
    p.add_argument("--rules", help="rules file (default: the one in the simulator image)")
    p.add_argument("--key", help="signing key file")
    p.add_argument("--workers", type=int, default=1, help="attempts flown at the same time")
    p.add_argument("--retries", type=int, default=2, help="extra tries after an infrastructure failure")
    p.add_argument("--memory", default="4g")
    p.add_argument("--cpus", type=float, default=4.0)
    p.add_argument("--wall", type=float, default=1800.0, help="real seconds allowed for one attempt")
    p.add_argument("--sim-gpu", default="auto", choices=["auto", "on", "off"])
    p.add_argument("--graphics", choices=["Low", "Medium", "High", "Ultra"], help="one preset for every team")
    p.add_argument("--no-video", action="store_true", help="no trackside video (faster without a GPU)")
    p.set_defaults(run=cmd_batch)

    args = parser.parse_args(argv)
    return int(args.run(args))


if __name__ == "__main__":
    sys.exit(main())
