"""Leak test for competition mode: can a program get the drone's or the gates' coordinates through the API?

Run it against a simulator started in competition mode with an output folder, one launch per run:

    Ushqyr.exe -bench -bench-mode competition -bench-team leaktest -bench-out <results folder>
    python leak_test.py --out <results folder>

What it does, in this order:
  1. Hostile input before the handshake, over many connections: random bytes, absurd lengths, unknown message
     numbers, truncated frames. The simulator must survive all of it.
  2. One real attempt in which the program asks for everything it should not get (ground truth, no pacing), flies for
     a while, and every byte it receives is kept.
  3. Forbidden messages (RESET, PING, unknown numbers): each must be refused.
  4. The checks on what was received:
       - only whitelisted keys in the handshake and in every observation;
       - no key that names a coordinate, a gate, the track or the score;
       - no number in any header equal to a true coordinate from the judges' truth.csv of this very attempt;
       - no stream of numbers that follows a true coordinate (correlation), except the downward distance sensor,
         which measures the height above the floor by design.

Exit code 0 = nothing leaked.
"""
from __future__ import annotations

import argparse
import csv
import os
import random
import socket
import struct
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Tuple

import msgpack
import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "sdk" / "python" / "src"))
sys.path.insert(0, str(HERE.parent / "examples" / "color_pid"))

from color_pid import ColorPid  # noqa: E402
from ushqyr_bench import client as proto  # noqa: E402
from ushqyr_bench.client import Battery, Camera, Imu, Observation, Tof  # noqa: E402

WELCOME_KEYS = {"proto", "mode", "sim", "time_mode", "control_hz", "time_limit", "drone", "rc", "sensors"}
OBSERVATION_KEYS = {"t", "tick", "running", "status", "imu_n", "tof", "battery", "camera"}
FORBIDDEN_WORDS = ("truth", "position", "pos", "velocity", "attitude", "quat", "gate", "track", "point", "score", "x", "y", "z")


def frame(kind: int, header: Dict[str, Any]) -> bytes:
    h = msgpack.packb(header, use_bin_type=True)
    return struct.pack("<IBI", 5 + len(h), kind, len(h)) + h


def connect(host: str, port: int) -> socket.socket:
    s = socket.create_connection((host, port), timeout=10)
    s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    return s


def hostile_input(host: str, port: int, rounds: int, rng: random.Random) -> None:
    """Garbage before the handshake. Nothing here may kill the simulator."""
    payloads = [
        b"\xff\xff\xff\xff" + b"A" * 64,                       # absurd length
        struct.pack("<I", 3) + b"\x01\x00\x00",                # shorter than a frame can be
        struct.pack("<IBI", 9, 1, 0xFFFFFFFF) + b"\x80" * 4,   # header longer than the frame
        struct.pack("<IBI", 6, 1, 1) + b"\xc1",                # header is not valid MessagePack
        struct.pack("<IBI", 6, 1, 1) + b"\x91",                # header is an array, and truncated
        frame(3, {"rc": [1500] * 16}),                         # STEP before HELLO
        frame(200, {}),                                        # unknown message number
        frame(1, {"proto": 999}),                              # wrong protocol version
        struct.pack("<IBI", 45, 1, 40) + b"\x91" * 40,         # nesting far too deep
        struct.pack("<IBI", 10, 1, 5) + b"\xdd\x7f\xff\xff\xff",  # an array claiming two thousand million items
    ]
    for i in range(rounds):
        data = payloads[i] if i < len(payloads) else bytes(rng.getrandbits(8) for _ in range(rng.randint(1, 4000)))
        try:
            s = connect(host, port)
            s.sendall(data)
            s.settimeout(0.05)
            try:
                s.recv(65536)
            except (socket.timeout, OSError):
                pass
            s.close()
        except OSError as e:
            raise SystemExit(f"FAIL: the simulator stopped accepting connections after hostile input #{i}: {e}") from e


def numbers(value: Any, path: str, out: List[Tuple[str, float]]) -> None:
    if isinstance(value, bool):
        return
    if isinstance(value, (int, float)):
        out.append((path, float(value)))
    elif isinstance(value, dict):
        for k, v in value.items():
            numbers(v, f"{path}.{k}", out)
    elif isinstance(value, (list, tuple)):
        for i, v in enumerate(value):
            numbers(v, f"{path}[{i}]", out)


def keys(value: Any, out: List[str]) -> None:
    if isinstance(value, dict):
        for k, v in value.items():
            out.append(str(k))
            keys(v, out)
    elif isinstance(value, (list, tuple)):
        for v in value:
            keys(v, out)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default=os.environ.get("USHQYR_BENCH_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("USHQYR_BENCH_PORT", "5757")))
    parser.add_argument("--out", required=True, help="the simulator's -bench-out folder (to read this attempt's truth.csv)")
    parser.add_argument("--seconds", type=float, default=30.0, help="simulator time to fly")
    parser.add_argument("--fuzz", type=int, default=200, help="hostile connections before the handshake")
    args = parser.parse_args()
    failures: List[str] = []
    rng = random.Random(20261119)

    print(f"1. hostile input: {args.fuzz} connections")
    hostile_input(args.host, args.port, args.fuzz, rng)

    print("2. one attempt, asking for everything")
    s = connect(args.host, args.port)
    s.sendall(frame(proto.HELLO, {"proto": 1, "token": os.environ.get("USHQYR_BENCH_TOKEN", ""), "truth": True, "pace": False,
                                  "client": "leak-test", "mode": "training", "track": True, "gates": True}))
    kind, welcome, _ = proto._receive(s)
    if kind != proto.WELCOME:
        raise SystemExit(f"FAIL: no WELCOME (message {kind}: {welcome}); is the simulator freshly started in competition mode?")
    if welcome.get("mode") != "competition":
        raise SystemExit("this test is for competition mode; start the simulator with -bench-mode competition")
    extra = set(welcome) - WELCOME_KEYS
    if extra:
        failures.append(f"WELCOME has keys outside the whitelist: {sorted(extra)}")

    headers: List[Dict[str, Any]] = []
    imu_rows: List[np.ndarray] = []
    pilot = ColorPid(welcome)
    cam, tof, bat = Camera(), Tof(), Battery()
    width, height = int(welcome["sensors"]["camera"]["width"]), int(welcome["sensors"]["camera"]["height"])
    s.sendall(frame(proto.START, {}))
    started = time.monotonic()
    while True:
        kind, h, payload = proto._receive(s)
        if kind != proto.OBSERVATION:
            raise SystemExit(f"FAIL: expected an observation, got message {kind}: {h}")
        headers.append(h)
        n = int(h.get("imu_n", 0))
        imu = Imu()
        if n:
            raw = np.frombuffer(payload, "<f4", n * 7).reshape(n, 7).copy()
            imu_rows.append(raw)
            imu = Imu(raw[:, 0], raw[:, 1:4], raw[:, 4:7])
        if "camera" in h:
            image = np.frombuffer(payload, np.uint8, int(h["camera"]["size"]), n * 28).reshape(height, width, 3)
            cam = Camera(image, float(h["camera"]["t"]), int(h["camera"]["seq"]), True)
        else:
            cam = Camera(cam.image, cam.t, cam.seq, False)
        tof = Tof(float(h["tof"]["range"]), bool(h["tof"]["valid"]), float(h["tof"]["t"]), True) if "tof" in h else Tof(
            tof.range, tof.valid, tof.t, False)
        bat = Battery(float(h["battery"]["voltage"]), float(h["battery"]["t"]), True) if "battery" in h else bat
        obs = Observation(float(h["t"]), int(h["tick"]), bool(h["running"]), str(h["status"]), cam, imu, tof, bat, h.get("truth"))
        if not obs.running or obs.t >= args.seconds:
            break
        s.sendall(frame(proto.STEP, {"rc": pilot.update(obs).channels(), "truth": True, "want": "position"}))
    flown = float(headers[-1]["t"])
    print(f"   flew {flown:.1f} s of simulator time in {time.monotonic() - started:.1f} s, {len(headers)} observations")

    print("3. forbidden messages")
    forbidden = (("RESET", proto.RESET, {"seed": 5}), ("PING", proto.PING, {"payload": 16}), ("unknown 77", 77, {}))
    for name, kind, header in forbidden:
        try:
            s.sendall(frame(kind, header))
            k, h, _ = proto._receive(s)
            if k != proto.ERROR:
                failures.append(f"{name} was answered with message {k} instead of an error")
        except proto.BenchError:
            pass  # the connection is already closed: also a refusal
        except OSError:
            pass
    s.close()
    time.sleep(1.0)
    try:
        s2 = connect(args.host, args.port)
        s2.sendall(frame(proto.HELLO, {"proto": 1, "token": os.environ.get("USHQYR_BENCH_TOKEN", "")}))
        k, h, _ = proto._receive(s2)
        if k != proto.ERROR:
            failures.append("a second attempt in the same launch was accepted")
        s2.close()
    except (proto.BenchError, OSError):
        pass

    print("4. checks")
    for i, h in enumerate(headers):
        extra = set(h) - OBSERVATION_KEYS
        if extra:
            failures.append(f"observation {i} has keys outside the whitelist: {sorted(extra)}")
            break
    names: List[str] = []
    keys(welcome, names)
    for h in headers[:50]:
        keys(h, names)
    allowed_names = {"proto", "tilt_deg", "protocol"}
    bad = sorted({n for n in names if n not in allowed_names and any(
        n == w or (len(w) > 2 and w in n) for w in FORBIDDEN_WORDS)} - {"time_limit"})
    # "mount" is the camera's place on the drone, not the drone's place in the world; "cx"/"cy" are pixels.
    if bad:
        failures.append(f"keys that sound like ground truth: {bad}")

    # The judges' truth of this very attempt.
    deadline = time.monotonic() + 180
    truth_path = None
    while time.monotonic() < deadline and truth_path is None:
        done = sorted(Path(args.out).glob("*/manifest.json"), key=lambda p: p.stat().st_mtime)
        if done and done[-1].stat().st_mtime > started - 5 + time.time() - time.monotonic():
            truth_path = done[-1].parent / "truth.csv"
        else:
            time.sleep(1.0)
    if truth_path is None:
        raise SystemExit("FAIL: the simulator wrote no attempt folder with a manifest; was it started with -bench-out?")
    truth: Dict[int, List[float]] = {}
    with open(truth_path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            truth[int(row["tick"])] = [float(row[c]) for c in ("x", "y_up", "z", "vx", "vy_up", "vz", "qx", "qy", "qz", "qw")]
    labels = ["x", "y_up", "z", "vx", "vy_up", "vz", "qx", "qy", "qz", "qw"]
    print(f"   truth: {truth_path} ({len(truth)} rows)")

    # Exact values: any number in a header equal to a true coordinate at that moment.
    equal = 0
    for h in headers:
        t = truth.get(int(h["tick"]))
        if t is None:
            continue
        found: List[Tuple[str, float]] = []
        numbers(h, "obs", found)
        for path, v in found:
            for j in range(6):
                # Coordinates near zero are skipped: plenty of honest numbers are 0.
                clock = path in ("obs.t", "obs.tick") or path.endswith(".t") or path.endswith(".seq")
                if abs(t[j]) > 0.05 and abs(v - t[j]) < 5e-4 and not path.startswith("obs.tof") and not clock:
                    equal += 1
                    if equal <= 3:
                        failures.append(f"tick {h['tick']}: {path} = {v} equals the true {labels[j]} = {t[j]}")
    # Streams that follow a coordinate.
    streams: Dict[str, List[Tuple[int, float]]] = {}
    for h in headers:
        found = []
        numbers(h, "obs", found)
        for path, v in found:
            streams.setdefault(path, []).append((int(h["tick"]), v))
    if imu_rows:
        imu_all = np.concatenate(imu_rows)
        for c, name in enumerate(("imu.t", "imu.gx", "imu.gy", "imu.gz", "imu.ax", "imu.ay", "imu.az")):
            streams[name] = [(int(round(float(r[0]) * 1000 / 10) * 10), float(r[c])) for r in imu_all[::5]]
    def detrended(v: np.ndarray, ticks: np.ndarray) -> np.ndarray:
        """Without its straight-line trend over time: two things that merely both grow are not a leak."""
        return v - np.polyval(np.polyfit(ticks, v, 1), ticks)

    worst = (0.0, "", "")
    for path, series in streams.items():
        # Clocks and counters are time itself, known to the program anyway.
        if path in ("obs.t", "obs.tick", "imu.t") or path.endswith(".t") or path.endswith(".seq") or path.endswith(".size"):
            continue
        pts = [(tick, v, truth[tick]) for tick, v in series if tick in truth]
        if len(pts) < 200:
            continue
        ticks = np.array([p[0] for p in pts], float)
        a = detrended(np.array([p[1] for p in pts]), ticks)
        if a.std() < 1e-9:
            continue
        for j, label in enumerate(labels):
            b = detrended(np.array([p[2][j] for p in pts]), ticks)
            if b.std() < 1e-6:
                continue
            r = abs(float(np.corrcoef(a, b)[0, 1]))
            if path == "obs.tof.range" and label == "y_up":
                print(f"   (the downward distance sensor follows the height, r = {r:.3f}: that is its job)")
                continue
            if r > worst[0]:
                worst = (r, path, label)
            if r > 0.95:
                failures.append(f"{path} follows the true {label} (correlation {r:.3f})")
    print(f"   strongest link between anything received and a true coordinate: {worst[1]} vs {worst[2]}, r = {worst[0]:.3f}")

    if failures:
        print("\nFAIL")
        for f_ in failures:
            print("  - " + f_)
        return 1
    print("\nPASS: no ground truth in anything the program received; hostile input survived; forbidden messages refused")
    return 0


if __name__ == "__main__":
    sys.exit(main())
