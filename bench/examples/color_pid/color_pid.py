"""Sample solution for Ushqyr AI Bench: a colour detector plus PID.

It finds the gates' LED frames by colour, picks one, turns towards it, holds its height with the downward distance
sensor and flies through, stepping sideways when the LiDAR shows something in the way. It keeps a rough note of
where the last four gates it passed stood (a gate scores again only after four others) and takes the nearest gate
that is not one of them; when it sees none it turns on the spot until one appears. It uses only what the drone's
sensors give: the camera picture, the gyro, the ToF range and the LiDAR scan.

    python color_pid.py                 # fly until the time limit
    python color_pid.py --show          # also show what the detector sees
    python color_pid.py --points 6      # training mode: stop after 6 scored gates (one lap of H1_Oval)

It is a starting point, not a competitive pilot: about 2 m/s, one gate colour, and a memory of four gates.
"""
from __future__ import annotations

import argparse
import math
from collections import deque
from dataclasses import dataclass
from typing import List, Optional, Tuple

import cv2
import numpy as np
from ushqyr_bench import RC, Observation, Sim


@dataclass
class Detection:
    """One gate frame found in the picture, in pixels."""

    cx: float
    cy: float
    width: float
    height: float
    clipped: bool
    """True when the frame's box touches the edge of the picture (part of the gate is out of view)."""
    fill: float = 1.0
    """Share of the box covered by the frame's outline: about 1 for one gate, low for several merged into one blob."""


class GateDetector:
    """Colour threshold in HSV, then one box per outline. Default colour: the magenta LED of the public tracks."""

    def __init__(self, hue: Tuple[int, int] = (140, 172), min_saturation: int = 90, min_value: int = 90, min_pixels: int = 50):
        self.lower = np.array([hue[0], min_saturation, min_value], np.uint8)
        self.upper = np.array([hue[1], 255, 255], np.uint8)
        self.min_pixels = min_pixels
        # A small kernel on purpose: a larger one joins a gate to its reflection in the floor and to its neighbours.
        self.kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3))
        self.mask: Optional[np.ndarray] = None

    def find(self, rgb: np.ndarray) -> List[Detection]:
        """Every gate-coloured frame in the picture, largest first."""
        hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
        mask = cv2.inRange(hsv, self.lower, self.upper)
        # Close small gaps so one gate is one outline even through the noise.
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, self.kernel)
        self.mask = mask
        # Outer outlines only, including those of a gate seen through another gate's opening.
        contours, hierarchy = cv2.findContours(mask, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
        height, width = mask.shape
        found: List[Detection] = []
        for i, c in enumerate(contours):
            if hierarchy[0][i][3] != -1:
                continue  # a hole (the opening itself)
            x, y, w, h = cv2.boundingRect(c)
            area = cv2.contourArea(c)
            if w < 8 or h < 8 or area < self.min_pixels:
                continue
            clipped = x <= 1 or y <= 1 or x + w >= width - 1 or y + h >= height - 1
            found.append(Detection(x + w / 2.0, y + h / 2.0, float(w), float(h), clipped, area / float(w * h)))
        found.sort(key=lambda d: -d.width * d.height)
        return found


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


def _wrap(angle: float) -> float:
    """An angle folded into -pi..pi."""
    return (angle + math.pi) % (2.0 * math.pi) - math.pi


class ColorPid:
    """The controller: call update(observation) every step, send the RC it returns."""

    GATE_WIDTH = 2.5  # outer size of the standard gate (m)
    GATE_CENTRE = 1.25  # height of the opening's centre above the floor (m)
    LED_TOP = 1.075  # the LED strip's upper edge above the opening's centre (m)
    REPEAT_WINDOW = 4  # a gate scores again only after this many others
    SAME_GATE = 3.5  # two position estimates closer than this are taken for the same gate (m)
    NEAR_ENOUGH = 18.0  # a gate further than this is not flown at before looking around for a nearer one (m)
    DRAG = 0.63  # how fast the drone loses speed (1/s): about 2.5 m/s at 9 degrees of tilt

    def __init__(self, info: dict, cruise_pitch: float = 0.2, max_angle_deg: float = 45.0):
        cam = info["sensors"]["camera"]
        self.width, self.height = int(cam["width"]), int(cam["height"])
        self.fx, self.fy = float(cam["fx"]), float(cam["fy"])
        self.cx0, self.cy0 = float(cam["cx"]), float(cam["cy"])
        self.tilt = math.radians(float(cam["tilt_deg"]))
        self.k1 = float(cam.get("distortion", {}).get("k1", 0.0))
        self.dt = 1.0 / float(info["control_hz"])
        self.cruise_pitch = cruise_pitch
        self.max_angle = math.radians(max_angle_deg)

        self.detector = GateDetector()
        # The integral finds the hover throttle by itself, so the same code flies drones of different weight.
        self.altitude = Pid(kp=0.9, ki=0.5, kd=0.45, limit=0.5, integral_limit=0.9)
        self.yaw = Pid(kp=1.5, ki=0.0, kd=0.12, limit=0.75, integral_limit=0.0)

        self.state = "arm"
        self.state_time = 0.0
        self.time = 0.0
        self.height_estimate = 0.0
        self.jump_ignored = 0
        self.climb_rate = 0.0
        self.target_height = self.GATE_CENTRE
        self.pitch_angle = 0.0  # estimate of the nose-down angle (rad), from our own commands
        self.hover = 0.56
        self.gates_passed = 0

        self.yaw_rate = 0.0  # from the gyro, + = turning right (rad/s)
        self.heading = 0.0  # the gyro's turn rate added up: which way the nose points (rad, + = right of the start)
        self.detections: List[Detection] = []
        self.detection: Optional[Detection] = None  # the gate being flown at
        self.frames_lost = 0
        self.near = False  # the gate being flown at already filled much of the picture
        self.target_heading = 0.0  # direction in the world of the gate being flown at
        self.exit_heading = 0.0  # heading when the last gate was passed
        self.hint_heading: Optional[float] = None  # where another gate was last seen
        self.turned = 0.0  # how far the search has turned (rad)
        self.commit_time = 1.0
        # Dead reckoning: no position sensor exists, so the drone's place is guessed from its own commands. The guess
        # drifts, but over the few gates that matter for the repeat rule it is good enough to tell gates apart.
        self.gyro_bias = 0.0
        self.bias_samples = 0
        self.position = np.zeros(2)  # (east, north) of the start heading's frame, metres
        self.velocity = np.zeros(2)
        self.roll_angle = 0.0
        self.passed: deque = deque(maxlen=self.REPEAT_WINDOW)  # where the last gates we flew through stood
        self.target_position = np.zeros(2)
        self.last_bearing: Optional[float] = None
        self.last_frame_time = 0.0
        self.sight_rate = 0.0  # how fast the direction to the gate turns in the world (rad/s, + = to the right)

    # ---------- pieces ----------

    def _go(self, state: str) -> None:
        self.state, self.state_time = state, 0.0

    def _update_height(self, obs: Observation) -> None:
        if not obs.tof.new:
            return
        if not obs.tof.valid:
            # Out of the sensor's range. After low readings that means closer than it can measure: on the floor
            # (after a crash, say). Knowing that lets us take off again.
            if self.height_estimate < 0.5:
                self.height_estimate, self.climb_rate = 0.03, 0.0
            return
        # The sensor looks along the drone's own "down": correct for the tilt we commanded.
        h = obs.tof.range * math.cos(self.pitch_angle)
        dt = 1.0 / 50.0
        # A sudden shorter reading is something under the drone (the gate's lower bar), not the floor: ignore it,
        # but only for a moment. If it stays, it is the floor coming up.
        if self.height_estimate > 0.3 and h < self.height_estimate - 0.2 and self.jump_ignored < 15:
            self.jump_ignored += 1
            return
        self.jump_ignored = 0
        rate = (h - self.height_estimate) / dt
        self.climb_rate += 0.25 * (max(-3.0, min(3.0, rate)) - self.climb_rate)
        self.height_estimate = h

    def _throttle(self) -> float:
        error = self.target_height - self.height_estimate
        out = self.hover + self.altitude.update(error, self.dt, rate=-self.climb_rate)
        # Tilted thrust lifts less.
        out /= max(0.8, math.cos(self.pitch_angle))
        return max(0.0, min(1.0, out))

    def _ray(self, u: float, v: float) -> Tuple[float, float]:
        """A pixel as tangents of the true angles (right, down): the lens model of the handshake, x_u = x_d (1 + k1 r²)."""
        xd, yd = (u - self.cx0) / self.fx, (v - self.cy0) / self.fy
        scale = 1.0 + self.k1 * (xd * xd + yd * yd)
        return xd * scale, yd * scale

    def _bearing(self, d: Detection) -> float:
        return math.atan(self._ray(d.cx, d.cy)[0])

    def _aim(self, d: Detection) -> Tuple[float, float, float]:
        """Bearing to the gate (rad, + right), its elevation above the horizon (rad) and its distance (m)."""
        x, _ = self._ray(d.cx, d.cy)
        bearing = math.atan(x)
        # Distance from the angle the gate's width takes up. (Angles, not picture sizes: a wide lens stretches
        # things near the picture's edge.) The box's lower edge is not used anywhere: a shiny floor mirrors the
        # LEDs and makes the blob taller than the gate.
        left, _ = self._ray(d.cx - d.width / 2.0, d.cy)
        right, _ = self._ray(d.cx + d.width / 2.0, d.cy)
        angle = max(math.atan(right) - math.atan(left), 1e-3)
        distance = self.GATE_WIDTH * 0.5 / math.tan(angle * 0.5)
        # Elevation of the opening's centre, taken from the top edge of the LED strip.
        _, top = self._ray(d.cx, d.cy - d.height / 2.0)
        top_elevation = math.atan(-top * math.cos(bearing)) + self.tilt - self.pitch_angle
        elevation = math.atan(math.tan(top_elevation) - self.LED_TOP / distance)
        return bearing, elevation, distance

    def _dead_reckon(self, pitch: float, roll: float) -> None:
        """Moves the position guess on by one step: tilt pushes, the air brakes."""
        dt = self.dt
        self.roll_angle += (roll * self.max_angle - self.roll_angle) * min(1.0, dt / 0.12)
        forward, right = 9.81 * math.tan(self.pitch_angle), 9.81 * math.tan(self.roll_angle)
        c, s_ = math.cos(self.heading), math.sin(self.heading)
        # Heading 0 looks along "north" of our own frame; positive headings turn to the right (east).
        push = np.array([forward * s_ + right * c, forward * c - right * s_])
        self.velocity += (push - self.DRAG * self.velocity) * dt
        self.position += self.velocity * dt

    def _place(self, d: Detection) -> np.ndarray:
        """Where a gate seen in the picture stands, in our own frame."""
        bearing, _, distance = self._aim(d)
        direction = self.heading + bearing
        return self.position + distance * np.array([math.sin(direction), math.cos(direction)])

    def _recent(self, d: Detection) -> bool:
        """True when this looks like one of the last gates we flew through (it would not score yet)."""
        bearing, _, distance = self._aim(d)
        direction = self.heading + bearing
        for p in self.passed:
            to = p - self.position
            far = float(np.linalg.norm(to))
            if far < 1.5:
                continue  # we are standing in that gate: nothing whole in view can be it
            # Directions are measured well, distances roughly: the same gate lies in the same direction, at a
            # distance that is at least of the same order.
            off = abs(_wrap(math.atan2(to[0], to[1]) - direction))
            if off < max(math.radians(12.0), math.atan(self.SAME_GATE / far)) and 0.4 * far < distance < 2.5 * far:
                return True
        return False

    def _blocked(self, obs: Observation) -> float:
        """0 when the way ahead is free; otherwise which way to step aside: -1 left, +1 right.

        From the LiDAR scan: something nearer than a metre in the 30 degrees ahead is in the way, and the side with
        more room is the way round it. (Ray k looks k degrees to the left of straight ahead.)
        """
        r = obs.lidar.ranges
        if r is None or len(r) < 360:
            return 0.0
        ahead = np.concatenate([r[:16], r[-15:]])
        ahead = ahead[ahead > 0.0]
        if ahead.size == 0 or float(ahead.min()) > 1.0:
            return 0.0
        left, right = r[20:91], r[270:341]
        # No return means nothing within range: plenty of room.
        room_left = float(np.where(left > 0.0, left, 12.0).mean())
        room_right = float(np.where(right > 0.0, right, 12.0).mean())
        return -1.0 if room_left > room_right else 1.0

    def _whole_gates(self) -> List[Detection]:
        """Detections that look like one whole gate: in the picture, big enough, and not several frames merged."""
        return [d for d in self.detections if not d.clipped and d.width >= 12 and d.fill >= 0.55]

    def _follow(self) -> None:
        """Finds the gate being flown at in the new frame: the detection nearest to where it was."""
        old = self.detection
        if old is None:
            return
        # The gate stands still in the world while the picture swings with every turn of the drone, so compare
        # directions in the world: the heading from the gyro plus the bearing in the picture.
        half_width = math.atan(old.width * 0.5 / self.fx)
        allowed = max(math.radians(7.0), 1.2 * half_width)
        best, best_cost = None, 1.0
        for d in self.detections:
            ratio = d.width / max(old.width, 1.0)
            # A gate cut off by the picture's edge keeps its place but not its size.
            if not d.clipped and not 0.6 <= ratio <= 1.8:
                continue
            if d.fill < 0.45 and not d.clipped:
                continue
            cost = abs(_wrap(self.heading + self._bearing(d) - self.target_heading)) / allowed
            cost += abs(d.cy - old.cy) / max(old.height * 1.5, 60.0)
            if cost < best_cost:
                best, best_cost = d, cost
        if best is None:
            self.frames_lost += 1
            return
        self.frames_lost = 0
        self.detection = best
        self.target_heading = self.heading + self._bearing(best)
        if not best.clipped:
            # The nearer the gate, the better its distance is known: let the newest look count most.
            self.target_position += 0.4 * (self._place(best) - self.target_position)
        if not best.clipped and best.width > 0.3 * self.width:
            self.near = True
        # Any other whole gate in view is where to look next: remember the direction to the one nearest in angle.
        others = [d for d in self._whole_gates() if d is not best]
        if others:
            mine = self._bearing(best)
            other = min(others, key=lambda d: abs(self._bearing(d) - mine))
            self.hint_heading = self.heading + self._bearing(other)

    def _update_sight_rate(self, frame_time: float) -> None:
        """Sideways drift shows as the gate sliding across the world while we fly at it: measure that slide.

        The gate's bearing in the picture changes for two reasons: the drone turns (the gyro tells how fast) and the
        drone slides sideways. Bearing rate plus turn rate leaves the slide.
        """
        d = self.detection
        if d is None or d.clipped or self.state != "track":
            self.last_bearing = None
            self.sight_rate *= 0.8
            return
        bearing = self._bearing(d)
        frame_dt = frame_time - self.last_frame_time
        if self.last_bearing is not None and 0.0 < frame_dt < 0.2:
            rate = (bearing - self.last_bearing) / frame_dt + self.yaw_rate
            self.sight_rate += 0.2 * (max(-1.5, min(1.5, rate)) - self.sight_rate)
        self.last_bearing, self.last_frame_time = bearing, frame_time

    def _start_tracking(self, d: Detection) -> None:
        self.detection = d
        self.target_heading = self.heading + self._bearing(d)
        self.target_position = self._place(d)
        self.frames_lost = 0
        self.near = False
        self.last_bearing = None
        self.sight_rate = 0.0
        self.yaw.reset()
        self._go("track")

    # ---------- the controller ----------

    def update(self, obs: Observation) -> RC:
        dt = self.dt
        self.time += dt
        self.state_time += dt
        self._update_height(obs)
        if len(obs.imu):
            # The gyro's z axis is "yaw left +".
            rate = -float(obs.imu.gyro[:, 2].mean())
            if self.state == "arm":
                # Standing still on the pad: whatever the gyro reads now is its own offset.
                self.bias_samples += 1
                self.gyro_bias += (rate - self.gyro_bias) / self.bias_samples
            rate -= self.gyro_bias
            self.yaw_rate += 0.3 * (rate - self.yaw_rate)
            self.heading += rate * dt
        new_frame = obs.camera.new and obs.camera.image is not None
        if new_frame:
            self.detections = self.detector.find(obs.camera.image)
            if self.state == "track":
                self._follow()
            self._update_sight_rate(obs.camera.t)

        pitch = yaw = roll = 0.0

        if self.state == "arm":
            # Switch off first, then on with the throttle down: that is what arms a flight controller.
            arm = self.state_time > 0.35
            if self.state_time > 0.6:
                self._go("takeoff")
            return RC(throttle=0.0, arm=arm)

        if self.state == "takeoff":
            if self.height_estimate > self.target_height - 0.25 or self.state_time > 4.0:
                self.exit_heading = self.heading
                self._go("choose")

        elif self.state == "choose":
            # Just through a gate (or just airborne): without turning, take the nearest gate in view that is not one
            # of the last few we passed.
            pitch = self.cruise_pitch * 0.4
            gates = [d for d in self._whole_gates() if not self._recent(d) and self._aim(d)[2] < self.NEAR_ENOUGH]
            if gates and new_frame:
                self._start_tracking(min(gates, key=lambda d: self._aim(d)[2]))
            elif self.state_time > 0.4:
                self.turned = 0.0
                self._go("search")

        elif self.state == "search":
            # Nothing ahead: turn on the spot, towards where another gate was last seen if we know.
            direction = -1.0
            if self.hint_heading is not None:
                direction = 1.0 if _wrap(self.hint_heading - self.heading) > 0.0 else -1.0
                if abs(_wrap(self.hint_heading - self.heading)) < 0.15:
                    self.hint_heading = None
            yaw = 0.45 * direction
            self.turned += abs(self.yaw_rate) * dt
            if new_frame:
                for d in sorted(self._whole_gates(), key=lambda g: self._aim(g)[2]):
                    # First choice: a near gate we have not just come through. After a full turn without one, a far
                    # one will do; after two, anything.
                    fresh, near = not self._recent(d), self._aim(d)[2] < self.NEAR_ENOUGH
                    full_turns = self.turned / math.radians(370.0)
                    if (fresh and near) or (fresh and full_turns > 1.0) or full_turns > 2.0:
                        self._start_tracking(d)
                        break

        elif self.state == "track":
            d = self.detection
            if d is None or self.frames_lost > 10:
                # Lost close to the gate means we are in it; lost far away means look again.
                if self.near:
                    self.commit_time = 0.8
                    self._go("commit")
                else:
                    self.detection = None
                    self._go("choose")
            else:
                bearing, elevation, distance = self._aim(d)
                yaw = self.yaw.update(bearing, dt)
                # Full speed only when the gate is ahead; nearly stop to turn when it is far to the side.
                centred = max(0.0, 1.0 - abs(bearing) / math.radians(35.0))
                pitch = self.cruise_pitch * (0.15 + 0.85 * centred)
                # Lean against the sideways slide, so the drone arrives at the gate instead of beside it.
                roll = max(-0.3, min(0.3, 0.9 * self.sight_rate))
                # Something in the way that is not the gate itself (a column, a parked car): stop and step round it.
                side = self._blocked(obs) if distance > 2.5 else 0.0
                if side != 0.0:
                    pitch, roll = 0.0, 0.3 * side
                if not d.clipped:
                    # The gate's centre tells the height to fly at (gates need not all stand on the floor). Only a calm,
                    # centred view is trusted: in a turn the tilt estimate is off and so is the height read from it.
                    if abs(bearing) < math.radians(20.0) and abs(self.yaw_rate) < 0.6 and distance < 12.0:
                        wanted = self.height_estimate + distance * math.tan(elevation)
                        # Standard gates stand on the floor, so only a small trim around their centre is accepted:
                        # glare around bright LEDs makes the blob, and with it this estimate, less than exact.
                        wanted = max(self.GATE_CENTRE - 0.2, min(self.GATE_CENTRE + 0.2, wanted))
                        self.target_height += (wanted - self.target_height) * min(1.0, dt / 1.5)
                    if distance < 2.5:
                        self.near = True
                # Close and lined up: stop steering and fly through. Close is either a short measured distance, or a
                # frame that no longer fits the picture.
                close = (not d.clipped and distance < 1.5) or (d.clipped and d.width > 0.45 * self.width)
                if close and abs(bearing) < math.radians(30.0):
                    reach = distance if not d.clipped else 1.0
                    self.commit_time = max(0.6, min(1.4, (reach + 1.0) / 2.2))
                    self._go("commit")

        elif self.state == "commit":
            pitch = self.cruise_pitch
            if self.state_time > self.commit_time:
                self.gates_passed += 1
                self.passed.append(self.target_position.copy())
                self.detection = None
                self.exit_heading = self.heading
                self._go("choose")

        # Our own estimate of the nose-down angle: the command, through the lag of the attitude loop.
        self.pitch_angle += (pitch * self.max_angle - self.pitch_angle) * min(1.0, dt / 0.12)
        self._dead_reckon(pitch, roll)
        return RC(roll=roll, pitch=pitch, yaw=yaw, throttle=self._throttle(), arm=True)

    def debug_image(self, rgb: np.ndarray) -> np.ndarray:
        img = cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR)
        for d in self.detections:
            p0 = (int(d.cx - d.width / 2), int(d.cy - d.height / 2))
            p1 = (int(d.cx + d.width / 2), int(d.cy + d.height / 2))
            cv2.rectangle(img, p0, p1, (0, 255, 0) if d is self.detection else (160, 160, 160), 2 if d is self.detection else 1)
        d = self.detection
        if d is not None:
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
