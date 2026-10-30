# Changelog

## M2: Flyable in editor, 2026-10-30
- `GateRush.Input`: `PilotInputSource` base (arming with throttle-low check, mode cycling, deadband/expo via `StickShaping`), `KeyboardGamepadSource` (gamepad Mode 2 + keyboard), `ScriptedInputSource` (automation).
- `GateRush.Vehicle`: `QuadProfileAsset`, `RateProfileAsset`, `PhysicsSettingsAsset` (sim + `CollisionSettings`), `QuadVehicle` bridge (fixed sub-steps, render interpolation, crash → 1.5 s → respawn, out-of-bounds), `CollisionHook` (swept sphere cast, bounce + friction), `PropVisuals`.
- `GateRush.Cameras.FpvCamera`: uptilt 0–60°, horizontal FOV 90–150°.
- `GateRush.UI.FlightDebugOverlay`: arm/mode, sticks, setpoint vs gyro per axis, motor bars, speed/altitude/impact (allocation-free TMP `SetText`).
- `Track_TestField` scene built from code (`GateRush/Builders/Build Test Field Scene`): 2 km grid field, pillars, reference arches, spawn pad, quad, overlay. Default data assets in `Assets/_Project/Data/`.
- `QuadSimulator.Advance<THook>` sub-step hook; 6 new `StickShaping` tests (29 total).
- Verified in play mode: arm, climb, Angle mode settles at the commanded tilt, crash at 11.9 m/s → respawn disarmed. Measured 0 bytes allocated per frame in input, vehicle, overlay, props and FPV camera updates.
## M1: Flight model (headless), 2026-10-30
- `GateRush.Flight` (engine-free assembly): `Vec3`/`Quat`, `PilotInput`, `RateProfile` + `Rates` (Betaflight and Actual rates, 1998 °/s clamp), `PidController` (D on measurement with low-pass, I limit, FF), `Mixer` (Quad-X, desaturation, Air Mode), `MotorModel` (first-order lag, thrust ∝ ω², inflow falloff, reaction torque), `AeroModel` (linear + quadratic body drag, rotational damping), `QuadSimulator` (Acro/Angle/Horizon, fixed sub-step `Advance` with an accumulator and a 16-step cap).
- `QuadProfile.Race5Inch` default 5" race quad.
- 23 EditMode tests: Betaflight/Actual rates vs reference values, hover equilibrium, rate step response per axis, stick sign conventions, Angle-mode tilt and self-level, determinism, sub-step cap, mixer range and Air Mode, Unity quaternion convention.

## M0: Project bootstrap, 2026-10-30
- Quest settings applied from code (`GateRush/Setup/Apply Quest Project Settings`): `com.gaterush.vr`, IL2CPP, ARM64, Vulkan, minSdk 32, OpenXR Single Pass Instanced + Oculus Touch profile, Android XR features off, single `Quest` quality level with `URP_Quest`.
- `Boot` scene built from code (`GateRush/Builders/Build Boot Scene`): minimal XR Origin, ground, `XrDisplayConfig` (72 Hz + foveation) and `PerfLogger` (FPS to logcat).
- `BuildScript.BuildQuest` → `Builds/GateRush.apk`; `TestRunnerBridge` for in-Editor EditMode runs.
- Folder layout and assembly definitions per PLAN §4.
