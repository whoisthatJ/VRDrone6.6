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
