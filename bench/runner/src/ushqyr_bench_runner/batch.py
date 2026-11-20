"""The qualification's batch run: every team's solution on every track, unattended, into one results file.

    ushqyr-bench batch --submissions submissions.yaml --tracks tracks.yaml --out results [--rules rules.json] [--key key.file]

submissions.yaml                         tracks.yaml
    - team: Alpha                            tracks: [H1_Oval, H2_Eight]     # ids, or files in `folder`
      image: alpha-solution:latest           folder: /srv/hidden_tracks      # optional: where hidden tracks live
    - team: Beta                             attempts: 2                     # per team and track (default 1)
      archive: beta.tar      # docker save   seed: 1                         # attempt n flies with seed + n - 1
    - team: Gamma
      folder: ./gamma        # has a Dockerfile

Every team flies the same tracks with the same seeds. A job that fails for a reason outside the solution (Docker, the
simulator not producing a result) is tried again; a solution that crashes, hangs or never connects is not: that is
its result. The run can be stopped and started again: finished jobs are kept (`batch_state.json`).
"""
from __future__ import annotations

import json
import re
import threading
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Dict, List, Optional

from . import attempt, sandbox

TEAM_NAME = re.compile(r"^[\w][\w .\-]{0,47}$")
TRACK_ID = re.compile(r"^[A-Za-z0-9_\-]{1,64}$")


class BatchError(Exception):
    pass


@dataclass
class Submission:
    team: str
    image: str = ""
    archive: str = ""
    folder: str = ""


@dataclass
class Plan:
    tracks: List[str]
    folder: Optional[Path] = None
    attempts: int = 1
    seed: int = 1


@dataclass
class Job:
    team: str
    track: str
    attempt: int
    seed: int

    @property
    def key(self) -> str:
        return f"{self.team}|{self.track}|{self.attempt}"

    @property
    def folder_name(self) -> str:
        return re.sub(r"[^\w.\-]", "_", f"{self.team}__{self.track}__a{self.attempt:02d}")


def _load(path: Path):
    text = path.read_text(encoding="utf-8-sig")
    if path.suffix.lower() == ".json":
        return json.loads(text)
    try:
        import yaml
    except ImportError as e:
        raise BatchError("reading YAML needs PyYAML (pip install pyyaml), or give the file as JSON") from e
    return yaml.safe_load(text)


def load_submissions(path: Path) -> List[Submission]:
    data = _load(path)
    if isinstance(data, dict):
        data = data.get("submissions")
    if not isinstance(data, list) or not data:
        raise BatchError(f"{path}: expected a list of submissions")
    out, seen = [], set()
    for i, item in enumerate(data):
        if not isinstance(item, dict):
            raise BatchError(f"{path}: entry {i + 1} is not a mapping")
        team = str(item.get("team", "")).strip()
        if not TEAM_NAME.match(team):
            raise BatchError(f"{path}: entry {i + 1}: team name {team!r} (1 to 48 letters, digits, spaces, dots, dashes)")
        if team.lower() in seen:
            raise BatchError(f"{path}: team {team!r} is listed twice")
        seen.add(team.lower())
        sources = [k for k in ("image", "archive", "folder") if item.get(k)]
        if len(sources) != 1:
            raise BatchError(f"{path}: team {team!r} needs exactly one of image, archive, folder")
        s = Submission(team=team, **{sources[0]: str(item[sources[0]])})
        # Paths are relative to the submissions file.
        if s.archive:
            s.archive = str((path.parent / s.archive).resolve())
        if s.folder:
            s.folder = str((path.parent / s.folder).resolve())
        out.append(s)
    return out


def load_plan(path: Path) -> Plan:
    data = _load(path)
    if isinstance(data, list):
        data = {"tracks": data}
    if not isinstance(data, dict) or not isinstance(data.get("tracks"), list) or not data["tracks"]:
        raise BatchError(f"{path}: expected `tracks`: a list of track ids")
    tracks = [str(t) for t in data["tracks"]]
    for t in tracks:
        if not TRACK_ID.match(t):
            raise BatchError(f"{path}: track id {t!r} (letters, digits, _ and - only)")
    if len(set(tracks)) != len(tracks):
        raise BatchError(f"{path}: a track is listed twice")
    folder = (path.parent / str(data["folder"])).resolve() if data.get("folder") else None
    if folder is not None:
        for t in tracks:
            if not (folder / f"{t}.benchtrack.json").is_file():
                raise BatchError(f"{path}: {folder / (t + '.benchtrack.json')} does not exist")
    attempts, seed = int(data.get("attempts", 1)), int(data.get("seed", 1))
    if not 1 <= attempts <= 20:
        raise BatchError(f"{path}: attempts must be 1 to 20")
    return Plan(tracks, folder, attempts, seed)


def jobs_for(submissions: List[Submission], plan: Plan) -> List[Job]:
    """Track by track, attempt by attempt, team by team: every team meets the same seed on the same track."""
    return [Job(s.team, track, n, plan.seed + n - 1)
            for track in plan.tracks for n in range(1, plan.attempts + 1) for s in submissions]


class State:
    """What has been done, on disk after every job, so an interrupted run can go on."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.lock = threading.Lock()
        self.jobs: Dict[str, Dict] = {}
        if path.is_file():
            self.jobs = json.loads(path.read_text(encoding="utf-8")).get("jobs", {})

    def done(self, job: Job, out: Path) -> bool:
        j = self.jobs.get(job.key)
        return bool(j and j.get("state") == "done" and (out / j.get("folder", "?") / "result.json").is_file())

    def set(self, job: Job, **fields) -> None:
        with self.lock:
            self.jobs[job.key] = {"team": job.team, "track": job.track, "attempt": job.attempt, "seed": job.seed, **fields}
            tmp = self.path.with_suffix(".tmp")
            tmp.write_text(json.dumps({"jobs": self.jobs}, indent=1, ensure_ascii=False), encoding="utf-8")
            tmp.replace(self.path)


def prepare_image(s: Submission, limits: sandbox.Limits) -> str:
    """The team's image, ready to run. Raises SandboxError when it can't be had or breaks the size limit."""
    if s.folder:
        image, size = sandbox.build_solution(Path(s.folder), limits)
    else:
        image = sandbox.load_solution(Path(s.archive)) if s.archive else s.image
        if not sandbox.image_exists(image):
            raise sandbox.SandboxError(f"image {image} not found")
        size = sandbox.image_size(image)
    if size > limits.image_bytes:
        raise sandbox.SandboxError(f"the image is {size / 1024**2:.0f} MB; the limit is {limits.image_bytes / 1024**2:.0f} MB")
    return image


def run(submissions: List[Submission], plan: Plan, out: Path, limits: Optional[sandbox.Limits] = None,
        rules: Optional[Path] = None, key: Optional[Path] = None, workers: int = 1, retries: int = 2,
        graphics: Optional[str] = None, video: Optional[bool] = None, sim_gpu: bool = False,
        say: Callable[[str], None] = print,
        fly: Callable[..., sandbox.Outcome] = sandbox.run_attempt,
        prepare: Callable[[Submission, sandbox.Limits], str] = prepare_image) -> Dict:
    """Runs what is not done yet and writes results.csv, results.json and leaderboard.csv into `out`."""
    limits = limits or sandbox.Limits()
    out = out.resolve()
    attempts_dir = out / "attempts"
    attempts_dir.mkdir(parents=True, exist_ok=True)
    state = State(out / "batch_state.json")
    owner = sandbox.owner_of(out)
    if fly is sandbox.run_attempt:
        # A run that was killed could not remove its containers; they would fly on and hold the video card.
        left = sandbox.remove_leftovers(owner)
        if left:
            say(f"removed {left} container(s) and network(s) left by a run that was killed")
    jobs = jobs_for(submissions, plan)
    todo = [j for j in jobs if not state.done(j, out)]
    say(f"{len(jobs)} job(s): {len(submissions)} team(s) x {len(plan.tracks)} track(s) x {plan.attempts} attempt(s); "
        f"{len(jobs) - len(todo)} already done")

    images: Dict[str, str] = {}
    problems: List[Dict] = []
    teams_todo = {j.team for j in todo}
    for s in submissions:
        if s.team not in teams_todo:
            continue
        try:
            images[s.team] = prepare(s, limits)
        except sandbox.SandboxError as e:
            problems.append({"team": s.team, "problem": "no usable image", "detail": str(e)})
            say(f"  {s.team}: no usable image: {e}")

    count = {"n": 0}
    count_lock = threading.Lock()

    def one(job: Job) -> None:
        if job.team not in images:
            state.set(job, state="no_image")
            return
        folder = attempts_dir / job.folder_name
        error = ""
        for attempt_no in range(1, retries + 2):
            try:
                o = fly(images[job.team], folder, job.track, job.seed, team=job.team, attempt=job.attempt, limits=limits,
                        rules=rules, key=key, tracks=plan.folder, graphics=graphics, video=video, sim_gpu=sim_gpu, owner=owner)
            except sandbox.SandboxError as e:
                o, error = None, str(e)
            if o is not None and o.result is not None:
                state.set(job, state="done", folder=str(o.folder.relative_to(out)), tries=attempt_no, notes=o.notes,
                          points=o.result.get("points"), status=o.result.get("status"))
                # Beside the attempt folder, not in it: the folder's manifest lists exactly the simulator's files.
                (folder / "solution.log").write_text(o.solution_log, encoding="utf-8", errors="replace")
                with count_lock:
                    count["n"] += 1
                    say(f"  [{count['n']}/{len(todo)}] {job.team} on {job.track}, attempt {job.attempt}: "
                        f"{o.result.get('status')}, {o.result.get('points')} point(s), {float(o.result.get('time', 0)):.2f} s")
                return
            error = error or "the simulator produced no result"
            say(f"  {job.team} on {job.track}, attempt {job.attempt}: infrastructure failure ({error}), try {attempt_no}")
        state.set(job, state="failed", tries=retries + 1, error=error)
        problems.append({"team": job.team, "track": job.track, "attempt": job.attempt, "problem": "not flown", "detail": error})

    if workers <= 1:
        for job in todo:
            one(job)
    else:
        with ThreadPoolExecutor(max_workers=workers) as pool:
            list(pool.map(one, todo))

    # Images built here from folders are this run's own; loaded and named ones are the organisers' to keep.
    for s in submissions:
        if s.folder and s.team in images:
            sandbox.docker("image", "rm", "-f", images[s.team], check=False, timeout=120)

    return write_results(out, state, problems, jobs, say)


def write_results(out: Path, state: State, problems: List[Dict], jobs: List[Job], say: Callable[[str], None] = print) -> Dict:
    wanted = {j.key for j in jobs}
    results = []
    for key, j in state.jobs.items():
        if key in wanted and j.get("state") == "done":
            folder = out / j["folder"]
            if (folder / "result.json").is_file():
                r = attempt.load_result(folder)
                r["folder"] = j["folder"]
                results.append(r)
        elif key in wanted and j.get("state") in ("failed", "no_image") and not any(
                p.get("team") == j["team"] and p.get("track") == j.get("track") and p.get("attempt") == j.get("attempt") for p in problems):
            if j["state"] == "failed":
                problems.append({"team": j["team"], "track": j["track"], "attempt": j["attempt"], "problem": "not flown",
                                 "detail": j.get("error", "")})
    results.sort(key=lambda r: (r.get("track_id", ""), r.get("attempt", 0), r.get("team", "")))
    attempt.write_results_csv(out / "results.csv", results)
    board = attempt.rank(results)
    attempt.write_leaderboard_csv(out / "leaderboard.csv", board)
    summary = {"jobs": len(jobs), "flown": len(results), "problems": problems,
               "leaderboard": [{"rank": r.rank, "team": r.team, "points": r.points, "time": round(r.time, 3)} for r in board],
               "attempts": results}
    (out / "results.json").write_text(json.dumps(summary, indent=1, ensure_ascii=False), encoding="utf-8")
    say(f"{len(results)} of {len(jobs)} job(s) have a result; {len(problems)} problem(s)")
    say(f"results: {out / 'results.csv'}, {out / 'results.json'}; leaderboard: {out / 'leaderboard.csv'}")
    return summary
