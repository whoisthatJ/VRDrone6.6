"""Reading and checking attempt folders written by the simulator (the same rules as the simulator's own code)."""
from __future__ import annotations

import csv
import hashlib
import hmac
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Tuple

MANIFEST = "manifest.json"
SIGNATURE = "manifest.sig"

VERIFIED = "verified"
CHECKSUMS_ONLY = "checksums_only"
MODIFIED = "modified"
MISSING = "missing"


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def _files(folder: Path) -> List[str]:
    names = [p.relative_to(folder).as_posix() for p in folder.rglob("*") if p.is_file()]
    return sorted(n for n in names if n not in (MANIFEST, SIGNATURE))


def verify(folder: Path, key: Optional[bytes] = None) -> Tuple[str, str]:
    """Checks a folder against its manifest. Returns (state, detail).

    verified: every file matches its checksum and the signature matches the key.
    checksums_only: every file matches; the signature could not be checked (no key given, or not signed).
    modified: a file was changed, added or removed, or the signature is wrong.
    missing: there is no manifest.
    """
    manifest_path = folder / MANIFEST
    if not manifest_path.is_file():
        return MISSING, "no manifest.json"
    raw = manifest_path.read_bytes()
    try:
        data = json.loads(raw.decode("utf-8"))
        entries = data["files"]
    except (ValueError, KeyError, TypeError):
        return MODIFIED, "manifest.json can't be read"

    listed = set()
    for e in entries:
        name = str(e.get("name", ""))
        listed.add(name)
        path = folder / name
        if ".." in name or not path.is_file():
            return MODIFIED, f"missing file: {name}"
        if not hmac.compare_digest(sha256_file(path), str(e.get("sha256", "")).lower()):
            return MODIFIED, f"changed file: {name}"
    for name in _files(folder):
        if name not in listed:
            return MODIFIED, f"file not in the manifest: {name}"

    sig_path = folder / SIGNATURE
    if data.get("signed") and not sig_path.is_file():
        return MODIFIED, "the signature file is missing"
    if not sig_path.is_file():
        return CHECKSUMS_ONLY, "checksums match; the attempt was not signed"
    if not key:
        return CHECKSUMS_ONLY, "checksums match; no key given to check the signature"
    expected = hmac.new(key, raw, hashlib.sha256).hexdigest()
    if not hmac.compare_digest(sig_path.read_text(encoding="utf-8").strip().lower(), expected):
        return MODIFIED, "the signature doesn't match"
    return VERIFIED, "checksums and signature match"


def find_hidden_tracks(build: Path, hidden: Path) -> List[str]:
    """Looks for hidden tracks inside a folder that will be handed out. Returns a description of every leak.

    A track counts as leaked when a file in the build is the track file itself (same checksum), or when any file
    contains the track's id or the text of one of its gate positions (so a track packed into an archive of plain
    files or pasted into another file is found too). Compressed archives are not unpacked.
    """
    needles = []
    for path in sorted(hidden.rglob("*.benchtrack.json")):
        raw = path.read_bytes()
        try:
            track = json.loads(raw.decode("utf-8"))
        except ValueError:
            continue
        marks = [str(track.get("id", "")).encode("utf-8")]
        gates = track.get("gates") or []
        if gates:
            # The first gate's position exactly as it is written in the file.
            start = raw.find(b'"position"', raw.find(b'"gates"'))
            end = raw.find(b"]", start)
            if start >= 0 and end > start:
                marks.append(raw[start : end + 1])
        # Compared without white space, so a re-indented or re-wrapped copy is found as well.
        marks = [re.sub(rb"\s+", b"", m) for m in marks if len(m) >= 6]
        needles.append((path.name, hashlib.sha256(raw).hexdigest(), marks))

    leaks: List[str] = []
    hidden_resolved = hidden.resolve()
    for file in build.rglob("*"):
        if not file.is_file() or hidden_resolved in file.resolve().parents:
            continue
        data = file.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        squeezed = re.sub(rb"\s+", b"", data) if len(data) < 64 * 1024 * 1024 else data
        for name, track_hash, marks in needles:
            if digest == track_hash:
                leaks.append(f"{file}: is the hidden track {name}")
            else:
                for mark in marks:
                    if mark in squeezed or mark.decode("utf-8").encode("utf-16-le") in data:
                        leaks.append(f"{file}: contains part of the hidden track {name}")
                        break
    return leaks


def load_result(folder: Path) -> Dict:
    return json.loads((folder / "result.json").read_text(encoding="utf-8"))


def load_results(root: Path) -> List[Dict]:
    """Every result.json below `root`, oldest first; each gets a "folder" entry."""
    out = []
    for path in root.rglob("result.json"):
        try:
            r = json.loads(path.read_text(encoding="utf-8"))
        except ValueError:
            continue
        r["folder"] = str(path.parent)
        out.append(r)
    return sorted(out, key=lambda r: r.get("date_utc", ""))


def commands(folder: Path) -> List[Optional[List[int]]]:
    """The RC packet of every step from telemetry.csv; None where the packet was rejected."""
    out: List[Optional[List[int]]] = []
    with open(folder / "telemetry.csv", newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            out.append([int(row[f"ch{i}"]) for i in range(1, 17)] if row["rc_ok"] == "1" else None)
    return out


@dataclass
class Row:
    rank: int
    team: str
    points: int
    time: float
    attempts: List[Dict]


def _better(a: Dict, b: Dict) -> bool:
    return a["points"] > b["points"] if a["points"] != b["points"] else a["time"] < b["time"]


def rank(results: Iterable[Dict], track: Optional[str] = None) -> List[Row]:
    """More points first, then less time. A team's line for a track is its best counted attempt there; with
    track=None the lines of all tracks are added up."""
    best: Dict[Tuple[str, str], Dict] = {}
    for r in results:
        if r.get("status") == "invalid" or (track is not None and r.get("track_id") != track):
            continue
        k = (r.get("team", ""), r.get("track_id", ""))
        if k not in best or _better(r, best[k]):
            best[k] = r
    rows: Dict[str, Row] = {}
    for (team, _), r in best.items():
        row = rows.setdefault(team, Row(0, team, 0, 0.0, []))
        row.points += int(r["points"])
        row.time += float(r["time"])
        row.attempts.append(r)
    ordered = sorted(rows.values(), key=lambda x: (-x.points, x.time, x.team))
    for i, row in enumerate(ordered):
        same = i > 0 and row.points == ordered[i - 1].points and row.time == ordered[i - 1].time
        row.rank = ordered[i - 1].rank if same else i + 1
    return ordered


CSV_COLUMNS = ["team", "track", "attempt", "points", "time", "status", "date", "build", "track_hash", "drone_config_hash",
               "solution_digest"]


def _cell(value: object) -> str:
    """Team names come from outside: never let a spreadsheet run one as a formula."""
    s = "" if value is None else str(value)
    return "'" + s if s[:1] in ("=", "+", "-", "@", "\t", "\r") else s


def write_results_csv(path: Path, results: Iterable[Dict], semicolon: bool = False) -> None:
    """All attempts as one CSV (UTF-8 with a byte-order mark; `semicolon` for regions that write decimals with a comma)."""
    with open(path, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f, delimiter=";" if semicolon else ",")
        w.writerow(CSV_COLUMNS)
        for r in results:
            time = f"{float(r.get('time', 0)):.3f}"
            w.writerow([_cell(r.get("team")), _cell(r.get("track_id")), r.get("attempt"), r.get("points"),
                        time.replace(".", ",") if semicolon else time, r.get("status"), r.get("date_utc"), r.get("build"),
                        r.get("track_hash"), r.get("drone_config_hash"), r.get("solution_digest")])


def write_leaderboard_csv(path: Path, rows: Iterable[Row], semicolon: bool = False) -> None:
    with open(path, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f, delimiter=";" if semicolon else ",")
        w.writerow(["rank", "team", "points", "time"])
        for r in rows:
            time = f"{r.time:.3f}"
            w.writerow([r.rank, _cell(r.team), r.points, time.replace(".", ",") if semicolon else time])
