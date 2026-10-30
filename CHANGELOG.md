# Changelog

## M6: RC radio + BetaFPV support, 2026-10-30
- Generic radio path (`GateRush.Input`): `IChannelReader` with `InputSystemChannelReader` (any joystick/HID, canonical channel order, unprocessed values) and `AndroidChannelReader`; `RadioDeviceProvider` (selection, presets, calibrations, hot-plug); `RcRadioSource` (switch arming, absolute mode switch, reset switch); `RadioMapping` math (asymmetric centred axes, full-range throttle, 2/3-position switches); `SwitchArming`.
- `ControllerPreset` / `ControllerPresetLibrary` + 6 BetaFPV presets (all unverified) built by `PresetBuilder`.
- `CalibrationWizard` with auto-advance, `RadioCalibrationStore` (radios.json).
- Android raw-joystick fallback: `GateRushRadio.androidlib` (Java window-callback hook, lock-free direct buffer, manifest features/permissions) + `AndroidJoystickBridge` (C#, unsafe pointer reads).
- Hot-plug: `VehicleInputRouter.SourceChanged`, `ControllerHotplugGuard` (pause/resume), `HotplugOverlay`.
- Menu: **Radio setup** (status, preset/verified/calibrated, live channels with roles, reader backend, calibrate, use preset), calibration wizard page, "Connecting a BetaFPV radio" help page. Input diagnostics now numbers channels like the radio mapping. Settings: `Input.RadioBackend`.
- Tests (103 total): mapping math, switch arming, preset matching, calibration store, wizard flow (full, skip/timeout, incomplete, cancel), `InputTestFixture` simulated radios (channel reader, `RcRadioSource` with arm/mode switches, calibration over preset, hot-unplug), shipped preset name matching.
- Play-mode check with a virtual "BETAFPV Joystick": preset auto-selected, router switches to the radio, arm switch starts the countdown, unplugging mid-race pauses with the overlay, re-plug + arm resumes with the clock intact.
- No APK built (user preference); the Android Java module is not Gradle-verified yet.
## M5: Content + settings, 2026-10-30
- **Quad classes**: 5" Race, 5" Freestyle, 65 mm Whoop (`QuadProfile` presets + assets + `QuadCatalog` with visual scale, collision radius, FPV camera offset).
- **Tracks as JSON**: `GateRush.Track` (`TrackData`, `TrackLoader`, `TrackPrefabs`), built-in courses Open Field, Hangar 7, Dockyard (`Data/Tracks/*.json`), `TrackCatalog`.
- **Environments**: outdoor field (pillars kept off the race line, tree line), indoor hangar (walls, roof, columns, trusses, emissive lamps, crates), industrial dockyard (container corridors, warehouses, gantry crane, quay, light poles). Prop prefabs: crate, barrel, pillar, wall, three container colours.
- **Settings + save system**: `GameSettings` / `SaveSystem` (JSON, atomic, clamped), applied live by `TrackSceneController`.
- **VR main menu** (`Menu` scene): Fly, choose quad, choose track (with lap records), settings tabs (Rates with live max deg/s, Camera, Input, Comfort, Physics, Pilot), input diagnostics, quit. XRI laser pointers + mouse.
- Scene flow Boot → Menu → Track_*; menu button returns to the menu.
- `RaceSession.SetCourse` (runtime course), `TrackName`; leaderboard/ghosts keyed by track id.
- Builders: `PropBuilder`, `CourseLibrary`, `CatalogBuilder`, `EnvironmentBuilder`, `TrackSceneBuilder` (replaces `TestFieldBuilder`), `MenuSceneBuilder`; `BuildAll` verifies scene references.
- Tests: profile tests for all three classes, settings (round trip, defaults, clamping, file save/load), track JSON (round trip, validation, loader), content (catalogs ↔ JSON ↔ build scenes): 79 total.
## M4: Racing, 2026-10-30
- `GateRush.Race`: `LapTimer` (checkpoint order, holeshot, laps, best lap), `GateCrossing` (forward segment-through-opening test with sub-frame fraction), `Gate` (opening, respawn pose, next/upcoming colours), `RaceSession` (countdown, hold, checkpoint respawn, finish, leaderboard), `GhostTrack` + `GhostSystem` (record/replay/save best lap), `LocalLeaderboard` (top 10 per track + lap record, JSON), `RaceAutopilot` (test pilot).
- `QuadVehicle.HoldAtSpawn` for the countdown and mid-air checkpoint respawns.
- Gate prefabs (Square 1.52 m, Large 2 m, Hurdle 3 × 1.5 m) and an 8-gate, ~285 m course on the test field (one floating gate); pillars kept off the race line.
- `RaceHud`: lap counter, lap/total/best times, countdown, GO, missed gate, lap flash (green on a personal best), results with leaderboard.
- 18 new tests (lap timing/order, crossing, leaderboard, ghost serialization): 50 total.
- Play-mode check: an autopilot flew a full 3-lap race (gates scaled 2.5×), producing lap times, the best-lap ghost file, leaderboard rank #1 and the results screen. Race, HUD, ghost and vehicle updates measured at 0 bytes allocated per frame.
## M3: Quest + VR views, 2026-10-30
- VR views (`GateRush.Cameras`): `ViewModeManager` (Goggles / Immersive / Line-of-sight, desktop fallback), `GogglesScreen` (curved mono screen, smooth head-follow or fixed, recentre), `ComfortVignette` (rate-based), `ViewModeInput` (Y / V / LB).
- `TouchControllerSource` (Mode 2, spring-centre hover assist or trigger throttle), `VehicleInputRouter` (priority-based source selection).
- Input diagnostics moved forward from M6: `InputDiagnostics` scene with `InputDiagnosticsPanel` (all devices, layout, interface, VID/PID, capabilities, live bars with min/max for every axis/button, save report, 72/90 Hz toggle); `InputDeviceReport` logs every device change to logcat and writes `input-devices.txt`.
- Boot flow with persistent `App` services and `SceneSwitcher`; `BootSceneBuilder.BuildAll()` builds all three scenes in order.
- OSD (debug overlay) now renders inside the drone camera feed.
- 3 new tests for VID/PID parsing (32 total).
- First Quest APK build: `Builds/GateRush.apk`.
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
