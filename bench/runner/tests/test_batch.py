import json
from pathlib import Path

import pytest

from ushqyr_bench_runner import batch, sandbox


def write(path: Path, data) -> Path:
    path.write_text(json.dumps(data), encoding="utf-8")
    return path


def fake_fly(fail_first=(), never=()):
    """A stand-in for sandbox.run_attempt: writes a result the way the simulator does."""
    calls = []

    def fly(image, out, track, seed, team="", attempt=1, **_):
        calls.append((team, track, attempt, seed))
        if (team, track) in never or ((team, track) in fail_first and calls.count((team, track, attempt, seed)) == 1):
            return sandbox.Outcome(None, None, None, "", "")
        folder = Path(out) / f"{team}_{track}_a{attempt:02d}_x"
        folder.mkdir(parents=True, exist_ok=True)
        result = {"team": team, "track_id": track, "attempt": attempt, "points": len(team) + seed, "time": 10.0 + len(track),
                  "sim_time": 20.0, "status": "finished", "seed": seed}
        (folder / "result.json").write_text(json.dumps(result), encoding="utf-8")
        return sandbox.Outcome(folder, result, 0, "log", "")

    fly.calls = calls
    return fly


def subs(*teams):
    return [batch.Submission(team=t, image=t.lower()) for t in teams]


def quiet(_):
    pass


def image_of(s, limits):
    return s.image


def test_twenty_teams_three_tracks_into_one_file(tmp_path):
    teams = [f"Team{i:02d}" for i in range(20)]
    summary = batch.run(subs(*teams), batch.Plan(["A", "B", "C"]), tmp_path, fly=fake_fly(), prepare=image_of, say=quiet)
    assert summary["jobs"] == 60 and summary["flown"] == 60 and not summary["problems"]
    rows = (tmp_path / "results.csv").read_text(encoding="utf-8-sig").strip().splitlines()
    assert len(rows) == 61
    assert len(json.loads((tmp_path / "results.json").read_text(encoding="utf-8"))["attempts"]) == 60
    assert len((tmp_path / "leaderboard.csv").read_text(encoding="utf-8-sig").strip().splitlines()) == 21


def test_every_team_gets_the_same_seed_on_a_track(tmp_path):
    fly = fake_fly()
    batch.run(subs("Alpha", "Beta"), batch.Plan(["A"], attempts=2, seed=7), tmp_path, fly=fly, prepare=image_of, say=quiet)
    assert sorted(fly.calls) == [("Alpha", "A", 1, 7), ("Alpha", "A", 2, 8), ("Beta", "A", 1, 7), ("Beta", "A", 2, 8)]


def test_resume_skips_what_is_done(tmp_path):
    plan = batch.Plan(["A", "B"])
    first = fake_fly(never={("Beta", "B")})
    s1 = batch.run(subs("Alpha", "Beta"), plan, tmp_path, fly=first, prepare=image_of, retries=0, say=quiet)
    assert s1["flown"] == 3 and s1["problems"][0]["team"] == "Beta"
    second = fake_fly()
    s2 = batch.run(subs("Alpha", "Beta"), plan, tmp_path, fly=second, prepare=image_of, say=quiet)
    assert second.calls == [("Beta", "B", 1, 1)]
    assert s2["flown"] == 4 and not s2["problems"]


def test_infrastructure_failure_is_retried(tmp_path):
    fly = fake_fly(fail_first={("Alpha", "A")})
    summary = batch.run(subs("Alpha"), batch.Plan(["A"]), tmp_path, fly=fly, prepare=image_of, say=quiet)
    assert len(fly.calls) == 2 and summary["flown"] == 1


def test_a_team_without_an_image_does_not_stop_the_others(tmp_path):
    def prepare(s, limits):
        if s.team == "Broken":
            raise sandbox.SandboxError("no Dockerfile")
        return s.image

    summary = batch.run(subs("Alpha", "Broken"), batch.Plan(["A"]), tmp_path, fly=fake_fly(), prepare=prepare, say=quiet)
    assert summary["flown"] == 1 and summary["problems"][0]["team"] == "Broken"


def test_parallel_workers(tmp_path):
    summary = batch.run(subs("A1", "B1", "C1", "D1"), batch.Plan(["A", "B"]), tmp_path, fly=fake_fly(), workers=4,
                        prepare=image_of, say=quiet)
    assert summary["flown"] == 8


def test_bad_files_are_refused(tmp_path):
    with pytest.raises(batch.BatchError):
        batch.load_submissions(write(tmp_path / "s.json", [{"team": "=cmd()", "image": "x"}]))
    with pytest.raises(batch.BatchError):
        batch.load_submissions(write(tmp_path / "s.json", [{"team": "A", "image": "x"}, {"team": "a", "image": "y"}]))
    with pytest.raises(batch.BatchError):
        batch.load_submissions(write(tmp_path / "s.json", [{"team": "A", "image": "x", "folder": "y"}]))
    with pytest.raises(batch.BatchError):
        batch.load_plan(write(tmp_path / "t.json", {"tracks": ["../../etc/passwd"]}))
    with pytest.raises(batch.BatchError):
        batch.load_plan(write(tmp_path / "t.json", {"tracks": ["H9"], "folder": "nowhere"}))
    plan = batch.load_plan(write(tmp_path / "t.json", {"tracks": ["H1_Oval"], "attempts": 2}))
    assert plan.attempts == 2 and plan.folder is None
