"""Checking a team's drone file (class AI CORE) before it is sent in.

The simulator runs the same checks when it loads the file and refuses a drone that fails them; this tool only lets a
team find out earlier. The limits of the class are the organisers' (class_limits.json); the defaults below are the
simulator's built-in ones.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any, Dict, List, Optional

DEFAULT_LIMITS: Dict[str, Any] = {
    "max_width_mm": 300.0, "max_length_mm": 300.0, "max_height_mm": 180.0,
    "min_mass_g": 25.0, "max_mass_g": 1500.0,
    "max_thrust_to_weight": 8.0, "min_thrust_to_weight": 1.5,
    "max_cells": 6, "max_capacity_mah": 2200.0,
    "min_motor_time_constant_ms": 10.0,
    "max_rate_deg_s": 1000.0, "max_angle_deg": 70.0,
    "min_camera_tilt_deg": 0.0, "max_camera_tilt_deg": 60.0,
    "min_camera_hfov_deg": 60.0, "max_camera_hfov_deg": 150.0,
    "sensors": ["camera", "imu", "tof", "lidar", "battery"],
}

_KNOWN = {"schema", "name", "class", "size_mm", "mass_g", "motors", "props", "battery", "controller", "camera", "sensors"}


def load_limits(path: Optional[Path]) -> Dict[str, Any]:
    limits = dict(DEFAULT_LIMITS)
    if path is not None:
        limits.update(json.loads(path.read_text(encoding="utf-8")))
    return limits


def _number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def validate(text: str, limits: Optional[Dict[str, Any]] = None) -> List[str]:
    """Returns the list of problems in plain words; empty means the drone is accepted."""
    import yaml  # PyYAML; only this command needs it

    limits = limits or DEFAULT_LIMITS
    errors: List[str] = []
    if "\t" in text:
        return ["use spaces, not tabs"]
    try:
        root = yaml.safe_load(text)
    except yaml.YAMLError as e:
        return [f"not valid YAML: {e}"]
    if not isinstance(root, dict):
        return ["the file must be a set of keys"]

    for key in root:
        if key not in _KNOWN:
            errors.append(f'unknown key "{key}"')
    if root.get("schema", None) != 1:
        errors.append("schema must be 1")
    if "class" in root and root["class"] != "ai_core":
        errors.append("class must be ai_core")
    name = root.get("name")
    if not isinstance(name, str) or not 1 <= len(name) <= 40:
        errors.append("name: 1 to 40 characters")

    def num(section: Optional[dict], key: str, default: float, required: bool, label: Optional[str] = None) -> float:
        label = label or key
        if isinstance(section, dict) and key in section:
            if _number(section[key]):
                return float(section[key])
            errors.append(f"{label}: a number is expected")
            return default
        if required:
            errors.append(f"{label}: missing")
        return default

    def section(key: str, required: bool) -> Optional[dict]:
        if key in root:
            if isinstance(root[key], dict):
                return root[key]
            errors.append(f"{key}: nested keys are expected")
        elif required:
            errors.append(f"{key}: missing")
        return None

    size = root.get("size_mm")
    if isinstance(size, list) and len(size) == 3 and all(_number(v) for v in size):
        width, length, height = (float(v) for v in size)
    else:
        errors.append("size_mm: three numbers [width, length, height]")
        width = length = height = 0.0
    mass = num(root, "mass_g", 0.0, True)
    motors, props, battery = section("motors", True), section("props", False), section("battery", True)
    controller, camera = section("controller", False), section("camera", False)
    if num(motors, "count", 4, False, "motors.count") != 4:
        errors.append("motors.count: only 4 motors are supported")
    thrust = num(motors, "max_thrust_g", 0.0, True, "motors.max_thrust_g")
    tau = num(motors, "time_constant_ms", 35.0, False, "motors.time_constant_ms")
    arm_x = num(motors, "arm_x_mm", 0.0, False, "motors.arm_x_mm")
    arm_z = num(motors, "arm_z_mm", 0.0, False, "motors.arm_z_mm")
    prop = num(props, "diameter_in", 3.0, False, "props.diameter_in")
    num(props, "pitch_in", 3.0, False, "props.pitch_in")
    cells = num(battery, "cells", 0.0, True, "battery.cells")
    capacity = num(battery, "capacity_mah", 0.0, True, "battery.capacity_mah")
    rates = [num(controller, f"{axis}_rate_deg_s", d, False, f"controller.{axis}_rate_deg_s")
             for axis, d in (("roll", 500.0), ("pitch", 500.0), ("yaw", 400.0))]
    max_angle = num(controller, "max_angle_deg", 45.0, False, "controller.max_angle_deg")
    tilt = num(camera, "tilt_deg", 20.0, False, "camera.tilt_deg")
    hfov = num(camera, "hfov_deg", 120.0, False, "camera.hfov_deg")

    sensors = root.get("sensors")
    fitted: List[str] = []
    if isinstance(sensors, list):
        for s in sensors:
            if s not in limits["sensors"]:
                errors.append(f'sensors: "{s}" is not in the catalogue ({", ".join(limits["sensors"])})')
            elif s not in fitted:
                fitted.append(s)
    else:
        errors.append("sensors: a list such as [camera, imu, tof, battery]")
    if "camera" not in fitted and not errors:
        errors.append("sensors: a camera is required")
    if errors:
        return errors

    def rng(what: str, v: float, lo: float, hi: float, unit: str) -> None:
        if v < lo or v > hi:
            errors.append(f"{what} = {v:g} {unit}: allowed {lo:g} to {hi:g}".replace("  ", " "))

    rng("width", width, 30, limits["max_width_mm"], "mm")
    rng("length", length, 30, limits["max_length_mm"], "mm")
    rng("height", height, 15, limits["max_height_mm"], "mm")
    rng("mass_g", mass, limits["min_mass_g"], limits["max_mass_g"], "g")
    rng("battery.cells", cells, 1, limits["max_cells"], "")
    rng("battery.capacity_mah", capacity, 100, limits["max_capacity_mah"], "mAh")
    rng("motors.time_constant_ms", tau, limits["min_motor_time_constant_ms"], 200, "ms")
    for axis, rate in zip(("roll", "pitch", "yaw"), rates):
        rng(f"controller.{axis}_rate_deg_s", rate, 100, limits["max_rate_deg_s"], "deg/s")
    rng("controller.max_angle_deg", max_angle, 10, limits["max_angle_deg"], "deg")
    rng("camera.tilt_deg", tilt, limits["min_camera_tilt_deg"], limits["max_camera_tilt_deg"], "deg")
    rng("camera.hfov_deg", hfov, limits["min_camera_hfov_deg"], limits["max_camera_hfov_deg"], "deg")
    rng("props.diameter_in", prop, 1, 8, "in")
    if mass > 0:
        rng("thrust-to-weight (4 x motors.max_thrust_g / mass_g)", 4 * thrust / mass,
            limits["min_thrust_to_weight"], limits["max_thrust_to_weight"], "")
    if arm_x < 0 or arm_x > width / 2 or arm_z < 0 or arm_z > length / 2:
        errors.append("motors.arm_x_mm / arm_z_mm: the motors must be inside the declared size")
    if prop * 25.4 > min(width, length):
        errors.append("props.diameter_in: the propeller is wider than the drone")
    return errors


def config_hash(text: str) -> str:
    """The hash the simulator writes into every result for this file."""
    return hashlib.sha256(text.encode("utf-8")).hexdigest()
