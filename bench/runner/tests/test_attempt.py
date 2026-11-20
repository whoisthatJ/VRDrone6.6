import hashlib
import hmac
import json
from pathlib import Path

from ushqyr_bench_runner import attempt
from ushqyr_bench_runner.cli import main

KEY = b"a test key of 32 bytes.........."


def make_attempt(folder: Path, key=KEY, points=3, team="A", track="T1", time=12.5, status="finished") -> Path:
    """Writes a folder the way the simulator does: files, manifest.json, manifest.sig."""
    folder.mkdir(parents=True)
    result = {"team": team, "track_id": track, "attempt": 1, "points": points, "time": time, "status": status,
              "seed": 1, "track_hash": "abc", "date_utc": "2026-11-19T10:00:00Z", "build": "1.2", "drone_config_hash": "d",
              "solution_digest": ""}
    (folder / "result.json").write_text(json.dumps(result), encoding="utf-8")
    (folder / "telemetry.csv").write_text(
        "tick,t,rc_ok," + ",".join(f"ch{i}" for i in range(1, 17)) + ",armed\n"
        "10,0.010,1," + ",".join(["1500"] * 16) + ",0\n"
        "20,0.020,0," + "," * 15 + ",0\n", encoding="utf-8")
    write_manifest(folder, key)
    return folder


def write_manifest(folder: Path, key) -> None:
    files = [{"name": p.name, "sha256": hashlib.sha256(p.read_bytes()).hexdigest(), "bytes": p.stat().st_size}
             for p in sorted(folder.iterdir()) if p.name not in ("manifest.json", "manifest.sig")]
    raw = (json.dumps({"schema": 1, "signed": key is not None, "files": files}, indent=4) + "\n").encode("utf-8")
    (folder / "manifest.json").write_bytes(raw)
    if key is not None:
        (folder / "manifest.sig").write_text(hmac.new(key, raw, hashlib.sha256).hexdigest() + "\n", encoding="utf-8")


def test_untouched_folder_verifies(tmp_path):
    folder = make_attempt(tmp_path / "a")
    assert attempt.verify(folder, KEY)[0] == attempt.VERIFIED
    assert attempt.verify(folder, None)[0] == attempt.CHECKSUMS_ONLY
    assert attempt.verify(folder, b"another key of 32 bytes.........")[0] == attempt.MODIFIED


def test_changed_score_is_caught(tmp_path):
    folder = make_attempt(tmp_path / "a")
    (folder / "result.json").write_text((folder / "result.json").read_text().replace('"points": 3', '"points": 9'))
    state, detail = attempt.verify(folder, KEY)
    assert state == attempt.MODIFIED and "result.json" in detail


def test_rewritten_manifest_without_the_key_is_caught(tmp_path):
    folder = make_attempt(tmp_path / "a")
    (folder / "result.json").write_text((folder / "result.json").read_text().replace('"points": 3', '"points": 9'))
    write_manifest(folder, b"the cheater's own key 32 bytes..")
    assert attempt.verify(folder, KEY)[0] == attempt.MODIFIED


def test_added_removed_and_unsigned(tmp_path):
    folder = make_attempt(tmp_path / "a")
    (folder / "extra.txt").write_text("x")
    assert attempt.verify(folder, KEY)[0] == attempt.MODIFIED
    (folder / "extra.txt").unlink()
    (folder / "manifest.sig").unlink()
    assert attempt.verify(folder, KEY)[0] == attempt.MODIFIED, "a signed attempt whose signature was deleted"
    (folder / "telemetry.csv").unlink()
    assert attempt.verify(folder, KEY)[0] == attempt.MODIFIED
    assert attempt.verify(tmp_path / "nothing", KEY)[0] == attempt.MISSING


def test_commands_are_read_with_rejected_packets(tmp_path):
    folder = make_attempt(tmp_path / "a")
    packets = attempt.commands(folder)
    assert packets == [[1500] * 16, None]


def test_ranking_and_csv(tmp_path):
    make_attempt(tmp_path / "r" / "a1", team="A", track="T1", points=5, time=40.0)
    make_attempt(tmp_path / "r" / "a2", team="A", track="T2", points=4, time=25.0)
    make_attempt(tmp_path / "r" / "b1", team="B", track="T1", points=6, time=50.0)
    make_attempt(tmp_path / "r" / "b2", team="B", track="T2", points=9, time=10.0, status="invalid")
    make_attempt(tmp_path / "r" / "c1", team="=cmd()", track="T1", points=1, time=3.0)
    results = attempt.load_results(tmp_path / "r")
    assert [r.team for r in attempt.rank(results, "T1")] == ["B", "A", "=cmd()"]
    overall = attempt.rank(results)
    assert (overall[0].team, overall[0].points, overall[0].time) == ("A", 9, 65.0)
    assert overall[1].points == 6, "the invalid run doesn't count"

    out = tmp_path / "all.csv"
    attempt.write_results_csv(out, results, semicolon=True)
    text = out.read_text(encoding="utf-8-sig")
    assert text.startswith("team;track;attempt;points;time")
    assert ";5;40,000;finished;" in text
    assert "'=cmd()" in text


def test_cli_exit_codes(tmp_path, capsys):
    folder = make_attempt(tmp_path / "r" / "a")
    key = tmp_path / "key.bin"
    key.write_bytes(KEY)
    assert main(["verify", str(folder), "--key", str(key)]) == 0
    assert main(["verify", str(folder)]) == 2, "no key: can't call it verified"
    assert main(["verify", str(folder), "--allow-unsigned"]) == 0
    (folder / "result.json").write_text("{}")
    assert main(["verify", str(folder), "--key", str(key)]) == 1
    assert "modified" in capsys.readouterr().out
    assert main(["leaderboard", str(tmp_path / "empty")]) == 1


def test_hidden_tracks_are_found_in_a_build(tmp_path):
    hidden = tmp_path / "private"
    hidden.mkdir()
    track = {"schema": 1, "id": "HID_final_07", "gates": [{"id": "G1", "position": [3.25, 0.0, -4.5], "yaw": 10.0}]}
    text = json.dumps(track, indent=4)
    (hidden / "HID_final_07.benchtrack.json").write_text(text, encoding="utf-8")

    clean = tmp_path / "kit"
    (clean / "Tracks").mkdir(parents=True)
    (clean / "Tracks" / "H1_Oval.benchtrack.json").write_text('{"id": "H1_Oval", "gates": []}', encoding="utf-8")
    assert attempt.find_hidden_tracks(clean, hidden) == []
    assert main(["check-build", str(clean), "--hidden", str(hidden)]) == 0

    # The file itself, a renamed copy, and its content pasted into something else.
    (clean / "Tracks" / "copy.json").write_text(text, encoding="utf-8")
    (clean / "bundle.bin").write_bytes(b"\x00\x01junk" + text.encode("utf-8")[40:] + b"more")
    (clean / "notes.txt").write_text("remember HID_final_07 for the final", encoding="utf-16-le")
    leaks = attempt.find_hidden_tracks(clean, hidden)
    assert len(leaks) == 3
    assert main(["check-build", str(clean), "--hidden", str(hidden)]) == 1
    assert main(["check-build", str(clean), "--hidden", str(tmp_path / "empty")]) == 2


EXAMPLE_DRONE = Path(__file__).resolve().parents[2] / "examples" / "drones" / "example_3inch.yaml"


def test_validate_drone_accepts_the_example_and_names_what_is_wrong(tmp_path, capsys):
    from ushqyr_bench_runner import drone

    text = EXAMPLE_DRONE.read_text(encoding="utf-8")
    assert drone.validate(text) == []
    assert main(["validate-drone", str(EXAMPLE_DRONE)]) == 0
    assert drone.config_hash(text) in capsys.readouterr().out

    def errors(old: str, new: str) -> str:
        assert old in text
        return "; ".join(drone.validate(text.replace(old, new)))

    assert "width = 340 mm: allowed 30 to 300" in errors("size_mm: [170, 170, 55]", "size_mm: [340, 170, 55]")
    assert "thrust-to-weight" in errors("max_thrust_g: 420", "max_thrust_g: 900")
    assert "battery.cells" in errors("cells: 4", "cells: 8")
    assert "not in the catalogue" in errors("[camera, imu, tof, lidar, battery]", "[camera, imu, gps]")
    assert "camera is required" in errors("[camera, imu, tof, lidar, battery]", "[imu, tof]")
    assert "unknown key" in errors("mass_g: 240", "mass_g: 240\nturbo: 9")
    assert "mass_g: missing" in errors("mass_g: 240", "weight: 240")

    bad = tmp_path / "bad.yaml"
    bad.write_text(text.replace("cells: 4", "cells: 8"), encoding="utf-8")
    assert main(["validate-drone", str(bad)]) == 1
    tight = tmp_path / "limits.json"
    tight.write_text('{"max_thrust_to_weight": 5}', encoding="utf-8")
    assert main(["validate-drone", str(EXAMPLE_DRONE), "--limits", str(tight)]) == 1


def make_dataset(folder: Path) -> Path:
    """A two-image dataset laid out the way the simulator writes it."""
    width, height = 640, 480
    for split in ("train", "val", "test"):
        for sub in ("images", "labels"):
            (folder / sub / split).mkdir(parents=True)
    (folder / "annotations").mkdir()
    box = [100.0, 120.0, 200.0, 160.0]
    keypoints = [120.0, 140.0, 2, 280.0, 140.0, 2, 280.0, 260.0, 1, 0, 0, 0]
    yolo = "0 0.312500 0.416667 0.312500 0.333333 0.187500 0.291667 2 0.437500 0.291667 2 0.437500 0.541667 1 0.000000 0.000000 0\n"
    for split, names in (("train", ["000000", "000001"]), ("val", []), ("test", [])):
        images, annotations = [], []
        for n, name in enumerate(names):
            (folder / "images" / split / f"{name}.jpg").write_bytes(b"not really a jpeg")
            (folder / "labels" / split / f"{name}.txt").write_text(yolo if n == 0 else "", encoding="utf-8")
            images.append({"id": n + 1, "file_name": f"images/{split}/{name}.jpg", "width": width, "height": height})
            if n == 0:
                annotations.append({"id": 1, "image_id": 1, "category_id": 1, "iscrowd": 0, "bbox": box, "area": 32000.0,
                                    "keypoints": keypoints, "num_keypoints": 3})
        (folder / "annotations" / f"instances_{split}.json").write_text(
            json.dumps({"images": images, "annotations": annotations, "categories": [{"id": 1, "name": "gate"}]}), encoding="utf-8")
    meta = {"images": 2, "train": 2, "val": 0, "test": 0, "gates_labelled": 1, "width": width, "height": height, "masks": False,
            "label_check": {"boxes_checked": 1, "boxes_within_2px": 1, "corners_checked": 2, "corners_on_gate": 2}}
    (folder / "dataset.json").write_text(json.dumps(meta), encoding="utf-8")
    return folder


def test_dataset_check_accepts_a_consistent_dataset_and_finds_what_is_broken(tmp_path):
    from ushqyr_bench_runner import dataset

    folder = make_dataset(tmp_path / "d")
    problems, summary = dataset.check(folder)
    assert problems == [] and summary["images"] == 2 and summary["gates"] == 1
    assert main(["dataset-check", str(folder)]) == 0

    (folder / "labels" / "train" / "000000.txt").write_text("", encoding="utf-8")
    assert any("YOLO labels but" in p for p in dataset.check(folder)[0])

    folder = make_dataset(tmp_path / "e")
    (folder / "images" / "train" / "000001.jpg").unlink()
    assert any("missing image" in p for p in dataset.check(folder)[0])

    folder = make_dataset(tmp_path / "f")
    meta = json.loads((folder / "dataset.json").read_text())
    meta["label_check"]["boxes_within_2px"] = 0
    (folder / "dataset.json").write_text(json.dumps(meta))
    assert any("label check" in p for p in dataset.check(folder)[0])
    assert main(["dataset-check", str(folder)]) == 1
    assert dataset.check(tmp_path / "nothing")[0] == ["no dataset.json: the dataset is not finished"]


def test_solution_container_is_locked_down():
    from ushqyr_bench_runner import sandbox

    args = sandbox.solution_args(sandbox.Limits(), "net-x", "tok", 5757)
    joined = " ".join(args)
    assert "--network net-x" in joined
    for must in ("--read-only", "--cap-drop ALL", "no-new-privileges", "--user 10001:10001", "--pids-limit 256",
                 "--memory 4g", "--memory-swap 4g", "--ipc private"):
        assert must in joined, must
    # Nothing of the host is mounted, and the host's network is never used.
    assert "-v" not in args and "--volume" not in args and "--mount" not in args
    assert "host" not in joined.replace("USHQYR_BENCH_HOST", "")
    assert "--privileged" not in args and "--gpus" not in args
    assert "USHQYR_BENCH_TOKEN=tok" in joined


def test_ros2_example_carries_the_same_pilot():
    examples = Path(__file__).resolve().parents[2] / "examples"
    assert (examples / "color_pid_ros2" / "color_pid.py").read_bytes() == (examples / "color_pid" / "color_pid.py").read_bytes()


def test_team_kit_layout(tmp_path):
    """A kit holds the player, bench/ and the documentation at the repository's own paths, and no caches."""
    import pytest

    from ushqyr_bench_runner import kit

    repo, build = tmp_path / "repo", tmp_path / "build"
    for f in ["bench/sdk/python/pyproject.toml", "bench/sdk/python/__pycache__/x.pyc", "bench/examples/a.py",
              "bench/runner/tests/hostile/hostile.py", "bench/runner/tests/test_x.py", "bench/template/Dockerfile",
              "Docs/bench/README.md", "Docs/bench/ru/quickstart.md", "Docs/bench/demo/video.mp4", "Assets/secret.cs"]:
        (repo / f).parent.mkdir(parents=True, exist_ok=True)
        (repo / f).write_text("x", encoding="utf-8")
    for f in ["Ushqyr.exe", "Ushqyr_Data/level0", "Ushqyr_BurstDebugInformation_DoNotShip/a.pdb"]:
        (build / f).parent.mkdir(parents=True, exist_ok=True)
        (build / f).write_text("x", encoding="utf-8")

    out = tmp_path / "kit"
    archive = kit.make(build, repo, out, "9.9", say=lambda _: None)
    assert kit.files(out) == ["Docs/bench/README.md", "Docs/bench/ru/quickstart.md", "README.txt", "Ushqyr.exe",
                              "Ushqyr_Data/level0", "bench/examples/a.py", "bench/runner/tests/test_x.py",
                              "bench/sdk/python/pyproject.toml", "bench/template/Dockerfile"]
    assert archive.is_file() and "9.9" in (out / "README.txt").read_text(encoding="utf-8")
    with pytest.raises(kit.KitError):
        kit.make(build, repo, out, "9.9", say=lambda _: None)      # never into a folder that has something in it
