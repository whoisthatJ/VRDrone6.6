# Changelog

## M1: Flight model (headless), 2026-10-30
- `GateRush.Flight` (engine-free assembly): `Vec3`/`Quat`, `PilotInput`, `RateProfile` + `Rates` (Betaflight and Actual rates, 1998 °/s clamp), `PidController` (D on measurement with low-pass, I limit, FF), `Mixer` (Quad-X, desaturation, Air Mode), `MotorModel` (first-order lag, thrust ∝ ω², inflow falloff, reaction torque), `AeroModel` (linear + quadratic body drag, rotational damping), `QuadSimulator` (Acro/Angle/Horizon, fixed sub-step `Advance` with an accumulator and a 16-step cap).
- `QuadProfile.Race5Inch` default 5" race quad.
- 23 EditMode tests: Betaflight/Actual rates vs reference values, hover equilibrium, rate step response per axis, stick sign conventions, Angle-mode tilt and self-level, determinism, sub-step cap, mixer range and Air Mode, Unity quaternion convention.

## M0: Project bootstrap, 2026-10-30
- Quest settings applied from code (`GateRush/Setup/Apply Quest Project Settings`): `com.gaterush.vr`, IL2CPP, ARM64, Vulkan, minSdk 32, OpenXR Single Pass Instanced + Oculus Touch profile, Android XR features off, single `Quest` quality level with `URP_Quest`.
- `Boot` scene built from code (`GateRush/Builders/Build Boot Scene`): minimal XR Origin, ground, `XrDisplayConfig` (72 Hz + foveation) and `PerfLogger` (FPS to logcat).
- `BuildScript.BuildQuest` → `Builds/GateRush.apk`; `TestRunnerBridge` for in-Editor EditMode runs.
- Folder layout and assembly definitions per PLAN §4.
