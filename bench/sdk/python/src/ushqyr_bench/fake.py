"""A tiny stand-in for the simulator, for unit tests of programs and of this SDK.

It speaks the real protocol but simulates nothing: the camera shows a grey image with the step number written into
the first pixel, the IMU reads gravity, the ToF reads 1 m.

    with FakeSim() as fake:
        sim = Sim.connect(port=fake.port)
"""
from __future__ import annotations

import socket
import struct
import threading
from typing import Any, Dict, List

import msgpack
import numpy as np

from .client import DONE, ERROR, HELLO, OBSERVATION, PING, PONG, PROTOCOL, RESET, START, STEP, WELCOME, _read_exact


class FakeSim:
    def __init__(
        self, mode: str = "training", width: int = 64, height: int = 48, time_limit: float = 2.0, token: str = ""
    ) -> None:
        self.mode, self.width, self.height, self.time_limit, self.token = mode, width, height, time_limit, token
        self.received: List[List[int]] = []
        """RC channel lists of every STEP, in order."""
        self._server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server.bind(("127.0.0.1", 0))
        self._server.listen(1)
        self.port: int = self._server.getsockname()[1]
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def __enter__(self) -> FakeSim:
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()

    def close(self) -> None:
        try:
            self._server.close()
        except OSError:
            pass

    def _run(self) -> None:
        while True:
            try:
                conn, _ = self._server.accept()
            except OSError:
                return
            try:
                self._serve(conn)
            except Exception:  # noqa: BLE001, S110 - a broken client must not kill the fake
                pass
            finally:
                conn.close()

    def _welcome(self) -> Dict[str, Any]:
        return {
            "proto": PROTOCOL,
            "mode": self.mode,
            "sim": "fake",
            "time_mode": "sync",
            "control_hz": 100.0,
            "time_limit": self.time_limit,
            "drone": {"class": "ai_mission", "name": "fake", "calibrated": False},
            "rc": {"channels": 16, "rate_hz": 100.0, "latency_ms": 10.0, "failsafe_ms": 500.0},
            "sensors": {
                "camera": {
                    "width": self.width,
                    "height": self.height,
                    "fps": 30.0,
                    "format": "rgb8",
                    "hfov_deg": 120.0,
                    "tilt_deg": 20.0,
                    "latency_ms": 30.0,
                    "fx": 18.5,
                    "fy": 18.5,
                    "cx": self.width / 2,
                    "cy": self.height / 2,
                    "distortion": {"model": "radial_k1", "k1": 0.0},
                    "mount": [0.03, 0.0, 0.01],
                },
                "imu": {"rate_hz": 500.0, "frame": "x forward, y left, z up"},
                "tof": {"rate_hz": 50.0, "min_m": 0.05, "max_m": 4.0, "direction": "down"},
                "battery": {"rate_hz": 10.0, "cells": 1},
                "lidar": {"rate_hz": 10.0, "rays": 360, "min_m": 0.15, "max_m": 12.0, "angle_step_deg": 1.0},
            },
        }

    def _serve(self, conn: socket.socket) -> None:
        tick, running, status, started = 0, False, "waiting", False

        def send(kind: int, header: Dict[str, Any], payload: bytes = b"") -> None:
            h = msgpack.packb(header, use_bin_type=True)
            conn.sendall(struct.pack("<IBI", 5 + len(h) + len(payload), kind, len(h)) + h + payload)

        def observation(first: bool) -> None:
            n = 0 if first else 5
            imu = np.zeros((n, 7), "<f4")
            imu[:, 0] = [(tick - 10 + 2 * (i + 1)) / 1000.0 for i in range(n)]
            imu[:, 6] = 9.81
            header: Dict[str, Any] = {"t": tick / 1000.0, "tick": tick, "running": running, "status": status, "imu_n": n}
            payload = imu.tobytes()
            if not first and tick % 100 == 0:
                header["lidar"] = {"t": tick / 1000.0, "n": 360}
                payload += np.full(360, 5.0, "<f4").tobytes()
            if first or tick % 30 == 0:
                image = np.full((self.height, self.width, 3), 128, np.uint8)
                image[0, 0, 0] = (tick // 10) % 256
                header["camera"] = {"t": tick / 1000.0, "seq": tick // 30, "size": image.size}
                payload += image.tobytes()
            if first or tick % 20 == 0:
                header["tof"] = {"t": tick / 1000.0, "range": 1.0, "valid": True}
            if first or tick % 100 == 0:
                header["battery"] = {"t": tick / 1000.0, "voltage": 4.1}
            send(OBSERVATION, header, payload)

        hello_done = False
        while True:
            (length,) = struct.unpack("<I", _read_exact(conn, 4))
            body = _read_exact(conn, length)
            kind = body[0]
            (hl,) = struct.unpack_from("<I", body, 1)
            h = msgpack.unpackb(bytes(body[5 : 5 + hl]), raw=False)
            if not isinstance(h, dict):
                return
            if not hello_done:
                if kind != HELLO or h.get("proto") != PROTOCOL:
                    send(ERROR, {"code": "protocol", "message": "send HELLO with the right protocol first"})
                    return
                if self.token and h.get("token") != self.token:
                    send(ERROR, {"code": "token", "message": "wrong token"})
                    return
                hello_done = True
                send(WELCOME, self._welcome())
                continue
            if self.mode == "competition" and kind not in (START, STEP, DONE):
                send(ERROR, {"code": "not_allowed", "message": "message not allowed in competition mode"})
                return
            if kind in (START, RESET):
                if kind == START and self.mode == "competition" and started:
                    send(ERROR, {"code": "already_started", "message": "the attempt has already started"})
                    return
                tick, running, status, started = 0, True, "running", True
                observation(True)
            elif kind == STEP:
                if running:
                    rc = h.get("rc")
                    self.received.append(list(rc) if isinstance(rc, list) else [])
                    tick += 10
                    if tick / 1000.0 >= self.time_limit:
                        running, status = False, "time_limit"
                observation(False)
            elif kind == DONE:
                if running:
                    running, status = False, "finished"
                observation(False)
            elif kind == PING:
                size = int(h.get("payload", 0))
                send(PONG, {"size": size}, bytes(size))
            else:
                send(ERROR, {"code": "unknown_message", "message": "unknown message type"})
                return
