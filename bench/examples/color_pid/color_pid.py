"""Sample solution for Ushqyr AI Bench: a colour detector plus PID.

It finds the gate's LED frame by colour, turns towards it, holds its height with the downward distance sensor and
flies through; when it sees no gate it turns on the spot until one appears. It uses only what the drone's sensors
give: the camera picture, the ToF range and (for nothing but the hover throttle) the battery voltage.

    python color_pid.py                 # fly until the time limit
    python color_pid.py --show          # also show what the detector sees
    python color_pid.py --points 6      # training mode: stop after 6 scored gates (one lap of H1_Oval)

It is a starting point, not a competitive pilot: about 2 m/s, one gate colour, no memory of the track.
"""
from __future__ import annotations

import argparse
import math
from dataclasses import dataclass
from typing import Optional, Tuple

import cv2
import numpy as np

from ushqyr_bench import RC, Observation, Sim


@dataclass
class Detection:
    """The gate the detector picked, in pixels."""

    cx: float
    cy: float
    width: float
    height: float
    clipped: bool
    """True when the frame's box touches the edge of the picture (part of the gate is out of view)."""


class GateDetector:
    """Colour threshold in HSV, then the largest outline. Default colour: the magenta LED of the public tracks."""

    def __init__(self, hue: Tuple[int, int] = (140, 172), min_saturation: int = 90, min_value: int = 90, min_pixels: int = 60):
        self.lower = np.array([hue[0], min_saturation, min_value], np.uint8)
        self.upper = np.array([hue[1], 255, 255], np.uint8)
        self.min_pixels = min_pixels
        self.kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5, 5))
        self.mask: Optional[np.ndarray] = None

    def detect(self, rgb: np.ndarray) -> Optional[Detection]:
        hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
        mask = cv2.inRange(hsv, self.lower, self.upper)
        # Close small gaps so one gate is one outline even through the noise.
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, self.kernel)
        self.mask = mask
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        best, best_area = None, 0.0
        for c in contours:
            x, y, w, h = cv2.boundingRect(c)
            # The nearest gate is the one whose frame covers the most picture. Thin slivers are gates seen edge-on.
            if w * h > best_area and cv2.contourArea(c) >= self.min_pixels and w >= 8 and h >= 8:
                best, best_area = (x, y, w, h), float(w * h)
        if best is None:
            return None
        x, y, w, h = best
        height, width = mask.shape
        clipped = x <= 1 or y <= 1 or x + w >= width - 1 or y + h >= height - 1
        return Detection(x + w / 2.0, y + h / 2.0, float(w), float(h), clipped)


class Pid:
    def __init__(self, kp: float, ki: float, kd: float, limit: float, integral_limit: float):
        self.kp, self.ki, self.kd, self.limit, self.integral_limit = kp, ki, kd, limit, integral_limit
        self.integral = 0.0
        self.previous: Optional[float] = None

    def reset(self) -> None:
        self.integral, self.previous = 0.0, None

    def update(self, error: float, dt: float, rate: Optional[float] = None) -> float:
        """`rate` is the measured rate of change of the error, when there is a better source than differencing."""
        self.integral = max(-self.integral_limit, min(self.integral_limit, self.integral + error * dt))
        if rate is None:
            rate = 0.0 if self.previous is None else (error - self.previous) / dt
        self.previous = error
        out = self.kp * error + self.ki * self.integral + self.kd * rate
        return max(-self.limit, min(self.limit, out))


class ColorPid:
    """The controller: call update(observation) every step, send the RC it returns."""

    GATE_WIDTH = 2.5  # outer size of the standard gate (m)
    GATE_CENTRE = 1.25  # height of the opening's centre above the floor (m)

    def __init__(self, info: dict, cruise_pitch: float = 0.2, max_angle_deg: float = 45.0):
        cam = info["sensors"]["camera"]
        self.width, self.height = int(cam["width"]), int(cam["height"])
        self.fx, self.fy = float(cam["fx"]), float(cam["fy"])
        self.cx0, self.cy0 = float(cam["cx"]), float(cam["cy"])
        self.tilt = math.radians(float(cam["tilt_deg"]))
        self.dt = 1.0 / float(info["control_hz"])
        self.cruise_pitch = cruise_pitch
        self.max_angle = math.radians(max_angle_deg)

        self.detector = GateDetector()
        self.altitude = Pid(kp=0.9, ki=0.5, kd=0.45, limit=0.35, integral_limit=0.4)
        self.yaw = Pid(kp=1.5, ki=0.0, kd=0.12, limit=0.75, integral_limit=0.0)

        self.state = "arm"
        self.state_time = 0.0
        self.time = 0.0
        self.height_estimate = 0.0
        self.climb_rate = 0.0
        self.target_height = self.GATE_CENTRE
        self.pitch_angle = 0.0  # estimate of the nose-down angle (rad), from our own commands
        self.detection: Optional[Detection] = None
        self.frames_without_gate = 0
        self.gates_passed = 0
        self.hover = 0.56
        self.yaw_rate = 0.0  # from the gyro, + = turning right (rad/s)
        self.last_bearing: Optional[float] = None
        self.last_frame_time = 0.0
        self.sight_rate = 0.0  # how fast the direction to the gate turns in the world (rad/s, + = to the right)

    # ---------- pieces ----------

    def _go(self, state: str) -> None:
        self.state, self.state_time = state, 0.0

    def _update_height(self, obs: Observation) -> None:
        if not (obs.tof.new and obs.tof.valid):
            return
        # The sensor looks along the drone's own "down": correct for the tilt we commanded.
        h = obs.tof.range * math.cos(self.pitch_angle)
        dt = 1.0 / 50.0
        # A sudden shorter reading is something under the drone (the gate's lower bar), not the floor: ignore it.
        if self.height_estimate > 0.3 and h < self.height_estimate - 0.2 and self.state in ("commit", "track"):
            return
        rate = (h - self.height_estimate) / dt
        self.climb_rate += 0.25 * (max(-3.0, min(3.0, rate)) - self.climb_rate)
        self.height_estimate = h

    def _throttle(self) -> float:
        error = self.target_height - self.height_estimate
        out = self.hover + self.altitude.update(error, self.dt, rate=-self.climb_rate)
        # Tilted thrust lifts less.
        out /= max(0.8, math.cos(self.pitch_angle))
        return max(0.0, min(1.0, out))

    def _aim(self, d: Detection) -> Tuple[float, float, float]:
        """Bearing to the gate (rad, + right), its elevation above the horizon (rad) and its distance (m)."""
        bearing = math.atan2(d.cx - self.cx0, self.fx)
        elevation = math.atan2(self.cy0 - d.cy, self.fy) + self.tilt - self.pitch_angle
        distance = self.fx * self.GATE_WIDTH / max(d.width, 1.0)
        return bearing, elevation, distance

    def _update_sight_rate(self, frame_time: float) -> None:
        """Sideways drift shows as the gate sliding across the world while we fly at it: measure that slide.

        The gate's bearing in the picture changes for two reasons: the drone turns (the gyro tells how fast) and the
        drone slides sideways. Bearing rate plus turn rate leaves the slide.
        """
        d = self.detection
        if d is None or d.clipped:
            self.last_bearing = None
            self.sight_rate *= 0.8
            return
        bearing = math.atan2(d.cx - self.cx0, self.fx)
        frame_dt = frame_time - self.last_frame_time
        if self.last_bearing is not None and 0.0 < frame_dt < 0.2:
            rate = (bearing - self.last_bearing) / frame_dt + self.yaw_rate
            self.sight_rate += 0.2 * (max(-1.5, min(1.5, rate)) - self.sight_rate)
        self.last_bearing, self.last_frame_time = bearing, frame_time

    # ---------- the controller ----------

    def update(self, obs: Observation) -> RC:
        dt = self.dt
        self.time += dt
        self.state_time += dt
        self._update_height(obs)
        if len(obs.imu):
            # The gyro's z axis is "yaw left +"; smooth it to about the camera's frame rate.
            self.yaw_rate += 0.3 * (-float(obs.imu.gyro[:, 2].mean()) - self.yaw_rate)
        if obs.camera.new and obs.camera.image is not None:
            self.detection = self.detector.detect(obs.camera.image)
            self.frames_without_gate = 0 if self.detection else self.frames_without_gate + 1
            self._update_sight_rate(obs.camera.t)

        pitch = 0.0
        yaw = 0.0
        roll = 0.0
        d = self.detection

        if self.state == "arm":
            # Switch off first, then on with the throttle down: that is what arms a flight controller.
            arm = self.state_time > 0.15
            if self.state_time > 0.3:
                self._go("takeoff")
            return RC(throttle=0.0, arm=arm)

        if self.state == "takeoff":
            if self.height_estimate > self.target_height - 0.25 or self.state_time > 4.0:
                self._go("search")

        elif self.state == "search":
            yaw = -0.45  # turn left until a gate shows up
            if d is not None and not d.clipped:
                self.yaw.reset()
                self._go("track")

        elif self.state == "track":
            if d is None:
                if self.frames_without_gate > 8:
                    self._go("search")
            else:
                bearing, elevation, distance = self._aim(d)
                yaw = self.yaw.update(bearing, dt)
                # Full speed only when the gate is ahead; nearly stop to turn when it is far to the side.
                centred = max(0.0, 1.0 - abs(bearing) / math.radians(35.0))
                pitch = self.cruise_pitch * (0.15 + 0.85 * centred)
                # Lean against the sideways slide, so the drone arrives at the gate instead of beside it.
                roll = max(-0.3, min(0.3, 0.9 * self.sight_rate))
                if not d.clipped:
                    # The gate's centre tells the height to fly at (gates need not all stand on the floor).
                    wanted = self.height_estimate + distance * math.tan(elevation)
                    wanted = max(0.5, min(3.0, wanted))
                    self.target_height += (wanted - self.target_height) * min(1.0, dt / 0.8)
                # Close enough that the frame no longer fits the picture: stop steering and fly through.
                if d.width > 0.62 * self.width or (d.clipped and d.width > 0.45 * self.width):
                    self._go("commit")

        elif self.state == "commit":
            pitch = self.cruise_pitch
            if self.state_time > 1.0:
                self.gates_passed += 1
                self.detection = None
                self._go("search")

        # Our own estimate of the nose-down angle: the command, through the lag of the attitude loop.
        self.pitch_angle += (pitch * self.max_angle - self.pitch_angle) * min(1.0, dt / 0.12)
        return RC(roll=roll, pitch=pitch, yaw=yaw, throttle=self._throttle(), arm=True)

    def debug_image(self, rgb: np.ndarray) -> np.ndarray:
        img = cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)
        d = self.detection
        if d is not None:
            p0 = (int(d.cx - d.width / 2), int(d.cy - d.height / 2))
            p1 = (int(d.cx + d.width / 2), int(d.cy + d.height / 2))
            cv2.rectangle(img, p0, p1, (0, 255, 0), 2)
            cv2.drawMarker(img, (int(d.cx), int(d.cy)), (0, 255, 0), cv2.MARKER_CROSS, 16, 2)
        text = f"{self.state}  h={self.height_estimate:.2f}/{self.target_height:.2f}  passed={self.gates_passed}"
        cv2.putText(img, text, (8, 20), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1, cv2.LINE_AA)
        return img


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default=None, help="simulator address (default: USHQYR_BENCH_HOST or 127.0.0.1)")
    parser.add_argument("--port", type=int, default=None, help="simulator port (default: USHQYR_BENCH_PORT or 5757)")
    parser.add_argument("--seed", type=int, default=None, help="training mode: seed of the sensor noise")
    parser.add_argument("--points", type=int, default=0, help="training mode: stop after this many scored gates")
    parser.add_argument("--seconds", type=float, default=0.0, help="stop after this much simulator time")
    parser.add_argument("--show", action="store_true", help="show the camera picture with the detection")
    parser.add_argument("--wait", type=float, default=60.0, help="seconds to wait for the simulator to start")
    args = parser.parse_args()

    # Ground truth is asked for only to know when to stop (--points); the controller never sees it.
    with Sim.connect(args.host, args.port, truth=args.points > 0, wait=args.wait) as sim:
        drone = sim.info["drone"]
        print(f"connected: mode {sim.mode}, drone {drone['name']} (calibrated={drone['calibrated']}), {sim.control_hz:.0f} Hz")
        pilot = ColorPid(sim.info)
        obs = sim.reset(args.seed)
        points = 0
        while sim.running:
            obs = sim.step(pilot.update(obs))
            if obs.truth is not None and obs.truth["points"] != points:
                points = obs.truth["points"]
                print(f"  t={obs.t:6.2f} s  gate scored, {points} point(s)")
                if args.points and points >= args.points:
                    obs = sim.done()
                    break
            if args.seconds and obs.t >= args.seconds and sim.running:
                obs = sim.done()
                break
            if args.show and obs.camera.new and obs.camera.image is not None:
                cv2.imshow("color_pid", pilot.debug_image(obs.camera.image))
                cv2.waitKey(1)
        print(f"attempt over: {obs.status}, simulator time {obs.t:.2f} s, the program counted {pilot.gates_passed} gate(s)")


if __name__ == "__main__":
    main()
