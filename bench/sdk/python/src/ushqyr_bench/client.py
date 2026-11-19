"""Client for the Ushqyr AI Bench simulator (protocol version 1, see Docs/bench/protocol.md).

    from ushqyr_bench import Sim, RC

    with Sim.connect() as sim:
        obs = sim.reset()
        for _ in range(15):
            obs = sim.step(RC(arm=False))      # arm switch off, then on with the throttle down
        for _ in range(15):
            obs = sim.step(RC(arm=True))
        while sim.running:
            obs = sim.step(RC(throttle=0.56, arm=True))

The program gets only what the drone's sensors give (camera, IMU, ToF, battery) and answers with RC channels,
as if it held a transmitter. Nothing in this package hides or filters data: what a mode does not allow is simply
never sent by the simulator.
"""
from __future__ import annotations

import os
import socket
import struct
import time
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Sequence

import msgpack
import numpy as np

PROTOCOL = 1

# Message numbers.
HELLO, START, STEP, RESET, DONE, PING = 1, 2, 3, 4, 5, 6
WELCOME, OBSERVATION, PONG, ERROR = 129, 130, 131, 132

_MAX_FRAME = 64 * 1024 * 1024
_IMU_FIELDS = 7  # t, gx, gy, gz, ax, ay, az


class BenchError(Exception):
    """The simulator refused something, or the connection broke."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(f"{code}: {message}")
        self.code = code
        self.message = message


@dataclass
class RC:
    """What a transmitter sends. Sticks are -1..1, throttle is 0..1.

    roll: + is right.  pitch: + is forward (nose down).  yaw: + is right (clockwise seen from above).
    arm: the AUX1 switch. The flight controller arms only when the switch goes on while the throttle is down.
    mode: the AUX2 switch: "angle" (self-levelling, default), "horizon" or "acro".
    """

    roll: float = 0.0
    pitch: float = 0.0
    throttle: float = 0.0
    yaw: float = 0.0
    arm: bool = False
    mode: str = "angle"
    aux: Sequence[int] = field(default_factory=tuple)

    _MODES = {"angle": 1000, "horizon": 1500, "acro": 2000}

    def channels(self) -> List[int]:
        """The 16 channel values in microseconds (1000..2000): roll, pitch, throttle, yaw, AUX1, AUX2, spare."""

        def stick(v: float) -> int:
            if v != v:  # NaN
                v = 0.0
            return int(round(1500 + 500 * max(-1.0, min(1.0, float(v)))))

        t = self.throttle if self.throttle == self.throttle else 0.0
        ch = [
            stick(self.roll),
            stick(self.pitch),
            int(round(1000 + 1000 * max(0.0, min(1.0, float(t))))),
            stick(self.yaw),
            2000 if self.arm else 1000,
            self._MODES.get(self.mode, 1000),
        ]
        ch += [int(a) for a in self.aux][:10]
        ch += [1500] * (16 - len(ch))
        return ch

    @staticmethod
    def from_us(channels: Sequence[int]) -> _RawRC:
        """Channel values given directly in microseconds."""
        return _RawRC(list(channels))


@dataclass
class _RawRC:
    values: List[int]

    def channels(self) -> List[int]:
        ch = [int(v) for v in self.values][:16]
        return ch + [1500] * (16 - len(ch))


@dataclass
class Camera:
    """The newest frame of the FPV camera."""

    image: Optional[np.ndarray] = None
    """H x W x 3, uint8, RGB, top row first. None until the first frame arrives."""
    t: float = 0.0
    """Simulator time when the frame was taken (it arrives later by the camera's delay)."""
    seq: int = -1
    new: bool = False
    """True when this observation brought a frame that the previous one didn't have."""


@dataclass
class Imu:
    """All IMU readings since the previous observation. Body frame: x forward, y left, z up."""

    t: np.ndarray = field(default_factory=lambda: np.zeros(0, np.float32))
    gyro: np.ndarray = field(default_factory=lambda: np.zeros((0, 3), np.float32))
    """rad/s: x roll right +, y nose down +, z yaw left +."""
    accel: np.ndarray = field(default_factory=lambda: np.zeros((0, 3), np.float32))
    """m/s^2, specific force: at rest and level it reads (0, 0, +9.81)."""

    def __len__(self) -> int:
        return int(self.t.shape[0])


@dataclass
class Tof:
    """Downward distance sensor."""

    range: float = 0.0
    """Metres; 0 when not valid."""
    valid: bool = False
    t: float = 0.0
    new: bool = False


@dataclass
class Battery:
    voltage: float = 0.0
    t: float = 0.0
    new: bool = False


@dataclass
class Observation:
    t: float
    """Simulator time since the start signal (s)."""
    tick: int
    running: bool
    status: str
    """"running", then why the attempt ended: finished, time_limit, stuck, disconnected."""
    camera: Camera
    imu: Imu
    tof: Tof
    battery: Battery
    truth: Optional[Dict[str, Any]] = None
    """Training mode only, and only when asked for with Sim.connect(truth=True)."""


class Sim:
    """A connection to the simulator. Use Sim.connect()."""

    def __init__(self, sock: socket.socket, info: Dict[str, Any]) -> None:
        self._sock = sock
        self.info = info
        """What the simulator said at the handshake: mode, drone, control rate, sensor list and camera calibration."""
        self.mode: str = str(info.get("mode", "training"))
        self.control_hz: float = float(info.get("control_hz", 100.0))
        self.running = False
        self.status = "waiting"
        self._camera = Camera()
        self._tof = Tof()
        self._battery = Battery()
        self._started = False

    # ---------- connecting ----------

    @classmethod
    def connect(
        cls,
        host: Optional[str] = None,
        port: Optional[int] = None,
        token: Optional[str] = None,
        truth: bool = False,
        realtime: bool = True,
        timeout: float = 30.0,
        wait: float = 0.0,
    ) -> Sim:
        """Connects and shakes hands.

        host, port, token default to the environment variables USHQYR_BENCH_HOST (127.0.0.1),
        USHQYR_BENCH_PORT (5757) and USHQYR_BENCH_TOKEN (empty). `wait` keeps retrying for that many seconds
        while the simulator is still starting. `realtime=False` (training mode only) lets the
        simulation run as fast as the computer allows instead of at the speed of a real flight; the result is the
        same either way, because simulator time advances only with step().
        """
        host = host or os.environ.get("USHQYR_BENCH_HOST", "127.0.0.1")
        port = int(port or os.environ.get("USHQYR_BENCH_PORT", "5757"))
        token = token if token is not None else os.environ.get("USHQYR_BENCH_TOKEN", "")
        deadline = time.monotonic() + wait
        while True:
            try:
                sock = socket.create_connection((host, port), timeout=timeout)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(0.5)
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        sock.settimeout(timeout)
        from . import __version__

        hello = {"proto": PROTOCOL, "token": token, "truth": bool(truth), "pace": bool(realtime)}
        hello["client"] = f"ushqyr-bench-py/{__version__}"
        _send(sock, HELLO, hello)
        kind, header, _ = _receive(sock)
        if kind == ERROR:
            sock.close()
            raise BenchError(str(header.get("code", "error")), str(header.get("message", "")))
        if kind != WELCOME:
            sock.close()
            raise BenchError("protocol", f"expected WELCOME, got message {kind}")
        return cls(sock, header)

    def close(self) -> None:
        try:
            self._sock.close()
        except OSError:
            pass

    def __enter__(self) -> Sim:
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()

    # ---------- the attempt ----------

    def reset(self, seed: Optional[int] = None) -> Observation:
        """Starts an attempt and returns the first observation.

        Training mode: puts the drone back on the start pad, as often as you like; `seed` picks the sensor noise.
        Competition mode: the attempt simply starts (once); `seed` is ignored.
        """
        if self.mode == "competition":
            _send(self._sock, START, {})
        else:
            _send(self._sock, RESET, {} if seed is None else {"seed": int(seed)})
        self._camera, self._tof, self._battery = Camera(), Tof(), Battery()
        self._started = True
        return self._observation()

    def step(self, rc: RC | _RawRC | None) -> Observation:
        """Sends one RC packet; the simulator advances one control period (1 / control_hz) and answers.

        `None` advances the simulation without a packet, as if this one was lost on the way.
        """
        if not self._started:
            raise BenchError("not_started", "call reset() first")
        _send(self._sock, STEP, {} if rc is None else {"rc": rc.channels()})
        return self._observation()

    def done(self) -> Observation:
        """Tells the simulator the program has finished; the attempt ends with status "finished"."""
        _send(self._sock, DONE, {})
        return self._observation()

    def ping(self, payload: int = 0) -> float:
        """Round-trip time in seconds for a message that comes back with `payload` bytes (training mode only)."""
        t0 = time.perf_counter()
        _send(self._sock, PING, {"payload": int(payload)})
        kind, header, _ = _receive(self._sock)
        if kind != PONG:
            raise BenchError(str(header.get("code", "protocol")), str(header.get("message", f"got message {kind}")))
        return time.perf_counter() - t0

    # ---------- internals ----------

    def _observation(self) -> Observation:
        kind, h, payload = _receive(self._sock)
        if kind == ERROR:
            raise BenchError(str(h.get("code", "error")), str(h.get("message", "")))
        if kind != OBSERVATION:
            raise BenchError("protocol", f"expected an observation, got message {kind}")

        n = int(h.get("imu_n", 0))
        imu_bytes = n * _IMU_FIELDS * 4
        if n > 0:
            raw = np.frombuffer(payload, dtype="<f4", count=n * _IMU_FIELDS).reshape(n, _IMU_FIELDS)
            imu = Imu(t=raw[:, 0].copy(), gyro=raw[:, 1:4].copy(), accel=raw[:, 4:7].copy())
        else:
            imu = Imu()

        cam = h.get("camera")
        if cam is not None:
            c = self.info["sensors"]["camera"]
            width, height = int(c["width"]), int(c["height"])
            size = int(cam["size"])
            image = np.frombuffer(payload, dtype=np.uint8, count=size, offset=imu_bytes).reshape(height, width, 3)
            self._camera = Camera(image=image, t=float(cam["t"]), seq=int(cam["seq"]), new=True)
        else:
            self._camera = Camera(self._camera.image, self._camera.t, self._camera.seq, False)

        tof = h.get("tof")
        if tof is not None:
            self._tof = Tof(float(tof["range"]), bool(tof["valid"]), float(tof["t"]), True)
        else:
            self._tof = Tof(self._tof.range, self._tof.valid, self._tof.t, False)

        battery = h.get("battery")
        if battery is not None:
            self._battery = Battery(float(battery["voltage"]), float(battery["t"]), True)
        else:
            self._battery = Battery(self._battery.voltage, self._battery.t, False)

        self.running = bool(h.get("running", False))
        self.status = str(h.get("status", ""))
        return Observation(
            t=float(h.get("t", 0.0)),
            tick=int(h.get("tick", 0)),
            running=self.running,
            status=self.status,
            camera=self._camera,
            imu=imu,
            tof=self._tof,
            battery=self._battery,
            truth=h.get("truth"),
        )


def _send(sock: socket.socket, kind: int, header: Dict[str, Any], payload: bytes = b"") -> None:
    h = msgpack.packb(header, use_bin_type=True)
    sock.sendall(struct.pack("<IBI", 5 + len(h) + len(payload), kind, len(h)) + h + payload)


def _read_exact(sock: socket.socket, count: int) -> bytearray:
    buf = bytearray(count)
    view = memoryview(buf)
    got = 0
    while got < count:
        n = sock.recv_into(view[got:], count - got)
        if n == 0:
            raise BenchError("disconnected", "the simulator closed the connection")
        got += n
    return buf


def _receive(sock: socket.socket) -> tuple[int, Dict[str, Any], memoryview]:
    try:
        (length,) = struct.unpack("<I", _read_exact(sock, 4))
        if length < 5 or length > _MAX_FRAME:
            raise BenchError("protocol", f"bad frame length {length}")
        body = _read_exact(sock, length)
    except socket.timeout as e:
        raise BenchError("timeout", "the simulator did not answer in time") from e
    except OSError as e:
        raise BenchError("disconnected", str(e)) from e
    kind = body[0]
    (header_length,) = struct.unpack_from("<I", body, 1)
    if header_length > length - 5:
        raise BenchError("protocol", "header longer than the frame")
    header = msgpack.unpackb(bytes(body[5 : 5 + header_length]), raw=False)
    if not isinstance(header, dict):
        raise BenchError("protocol", "header is not a map")
    return kind, header, memoryview(body)[5 + header_length :]
