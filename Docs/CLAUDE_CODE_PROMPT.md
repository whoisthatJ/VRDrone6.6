# Prompt for the Claude Code agent (Unity CLI)

> How to use: put `PLAN.md` in the root of your (empty) project folder, open Claude Code there with the Unity plugin/skills enabled, and paste everything below the line.

---

You are a senior Unity XR engineer and FPV flight-dynamics programmer. Build **"GateRush VR"**: an FPV drone racing simulator for **Meta Quest 3/3S/2**, inspired by VelociDrone (realistic Betaflight-style acro physics, race gates, lap timing, ghosts, track editor). Use original names, art and tracks only. Do not use VelociDrone, MultiGP or other brand names or logos in the game.

`PLAN.md` in this folder is the source of truth for scope, architecture, milestones and acceptance criteria. Read it first. If you deviate from it, update it in the same commit.

## Tooling rules
1. Use the **Unity CLI** (`unity`) for everything it covers: installing editors/modules, creating the project, opening it, building, and running tests. Load the `unity-cli` skill first. Use `unity-package-management` for UPM packages and `new-unity-project` for bootstrapping. Always use `--format json` when you parse output.
2. Before touching any scene, prefab or asset, run `unity status`. If an Editor is connected, drive it live with `unity command …` / `unity command eval '<C#>'`. Run `unity command` first to see which commands it exposes. **Never hand-edit `.unity`/`.prefab`/`.asset` YAML while an Editor is reachable.** If none is reachable, prefer building scenes from code with Editor scripts (`Assets/_Project/Editor/Builders/*.cs`, run through `-executeMethod`) over writing YAML.
3. If the connection fails, check for Safe Mode (`unity pipeline list`), fix the compile errors, then reopen.
4. After every code change, compile (keep the console at zero errors and zero warnings in our code) and run the EditMode tests: `unity test <path> --mode EditMode --report-format junit --output ./TestResults/editmode.xml`.
5. Use Git with Unity's standard `.gitignore` and Git LFS for binaries (`*.fbx *.png *.wav *.psd *.tga *.exr`). Commit at the end of each milestone, with a message like `M<n>: <summary>`.

## Project setup (M0)
- Editor: the latest **Unity 6 LTS** (≥ 6000.0.66f2; prefer 6.1+ if it's available as LTS), with modules **Android Build Support + OpenJDK + Android SDK & NDK**.
- Template: URP 3D (look up the real template id with `unity templates list`).
- Project path: `./GateRush`. Build profile: **Meta Quest** (fall back to Android if it's missing), ARM64, IL2CPP, Vulkan, min API per Meta's current requirement.
- Packages: `com.unity.xr.management`, `com.unity.xr.openxr`, `com.unity.inputsystem`, `com.unity.textmeshpro` (or the built-in UGUI in Unity 6), `com.unity.test-framework`, and the **Meta XR Core SDK** `com.meta.xr.sdk.core`. Get it through Meta's scoped registry `https://npm.developer.oculus.com/` (verify this first). If that doesn't work, stop and ask me to install it from the Asset Store. Do **not** install the deprecated Oculus XR Plugin.
- OpenXR: enable the Meta Quest feature group on Android, interaction profile = Oculus Touch Controller Profile, render mode = Single Pass Instanced, foveation on. Then run Meta's Project Setup Tool fixes through code if an API exists (e.g. `OVRProjectSetup`); otherwise list the remaining items for me.
- Quality: one Quest quality level (MSAA 4x, no realtime shadows, HDR off, render scale 1.0), target 72 Hz with a 90 Hz option.

## Engineering rules
- Folder layout and namespaces exactly as in PLAN.md §4 (`GateRush.Flight`, `GateRush.Input`, …), with assembly definitions per area. `GateRush.Flight` must have **no reference to UnityEngine** except `Mathf`-free math. Use `System.Numerics` or our own `Vec3`/`Quat` structs so it is fully unit-testable and deterministic.
- `QuadSimulator.Step(ref QuadState s, in PilotInput input, in QuadProfile p, float dt)` with fixed sub-steps (default 1 ms, max 16 per frame, with an accumulator). The MonoBehaviour bridge copies the state to the Transform and interpolates for rendering.
- Implement **Betaflight rates** and **Actual rates** exactly per the Betaflight formulas, with unit tests against known values (e.g. RC Rate 1.0 / Super 0.7 / Expo 0 → ~667 °/s at full stick).
- Rate PID per axis with D-term low-pass and an I-term limit; Air Mode; Quad-X mixer with desaturation; first-order motor lag; thrust ∝ ω²; yaw from motor torque; linear+quadratic body-axis drag; gravity 9.81. Angle/Horizon modes use an outer attitude loop.
- Every tunable lives in ScriptableObjects (`QuadProfile`, `RateProfile`, `PhysicsSettings`) and can be exposed to the in-game settings UI. Never hard-code magic numbers in the logic.
- Collisions: sphere-cast the swept motion each sub-step against the `TrackGeometry` layer; apply restitution and friction; crash when impact speed exceeds a threshold → respawn at the last checkpoint after 1.5 s.
- Input: a single `PilotInput` struct (throttle 0..1, yaw/pitch/roll −1..1, arm, mode, reset). Sources: Touch controllers (Mode 2; throttle options: left stick Y spring-centered with hover assist, OR left trigger), gamepad, generic HID joystick/RC radio with a calibration wizard (min/center/max per axis, invert, channel mapping) saved per device name. Add deadband and stick expo settings.
- **BetaFPV radios are a first-class requirement** (PLAN.md §2b): LiteRadio 2 SIM (USB-C; Joystick and Xbox modes; shows up as "BETAFPV Joystick"), LiteRadio 2 SE / 3 (USB HID), LiteRadio 4 / 4 SE (Bluetooth joystick or USB; USB only works with the radio switched off). Build it as:
  1. **Diagnostics first.** Before writing any mappings, build an `InputDiagnostics` scene and panel that lists every Input System device (name, layout, interface, VID/PID, capabilities JSON) and shows live bars for every control. Also log `InputSystem.onDeviceChange`.
  2. **Generic joystick reader.** Read all axes and buttons from any `Joystick`/`HID` device, with no hard-coded layout.
  3. **`ControllerPreset` ScriptableObjects**, auto-selected by product-name regex (`BETAFPV`, `LiteRadio`). Ship presets for LR2 SIM-Joystick, LR2 SIM-Xbox (falls through to the Gamepad path), LR3, LR4-USB and LR4-BT. Mark each one `verified=false` until I confirm it on hardware with the diagnostics screen.
  4. **Calibration wizard** usable in VR (corners → center → flip each switch), stored per device.
  5. **Android fallback.** If Unity exposes fewer axes than the radio sends, add a small Kotlin AAR in `Assets/Plugins/Android` that reads `MotionEvent` joystick axes (including `AXIS_GENERIC_1..16`) and sends them to C# through a lock-free buffer. Work with Unity 6's default GameActivity (or document switching the entry point). Add `uses-feature android.hardware.usb.host required=false` and the Bluetooth permissions needed for HID gamepads.
  6. **Hot-plug** pause/resume, and an in-game "Connect your BetaFPV radio" help page (OTG cable, LR4 Bluetooth pairing in Quest Settings, Android compatibility in BETAFPV Configurator).
  Unit-test the mapping, calibration math and deadband/expo in EditMode with simulated devices (`InputTestFixture`).
- VR views as in PLAN.md §2: **Goggles** (mono RenderTexture on a curved screen, head-follow smoothing), **Immersive** (rig on the drone + camera tilt + rate-based vignette), **LOS** (standing pilot). Switch modes from the menu and with a controller button. Camera tilt 0–60°, FOV 90–150°.
- Performance: no per-frame allocations in Update/sim paths (verify with the Profiler/GC alloc), pool VFX, bake the lighting. Hold 72 fps on Quest 2.

## Workflow
Work milestone by milestone (M0 → M8 in PLAN.md; M9 is a stretch goal). For each milestone:
1. Post a short plan (files and classes you will touch).
2. Implement it, with tests for anything in `Flight/`, `Race/` (lap timing, checkpoint order) and `Track/` (JSON round-trip).
3. Verify: compile clean, tests green, play-mode smoke test through `unity command editor_play` (or an equivalent the Editor exposes), and read the console logs. From M3 on, also build the APK: `unity build ./GateRush --target Android --execute-method GateRush.EditorTools.BuildScript.BuildQuest` → `Builds/GateRush.apk`.
4. Update `PLAN.md` (✅ the milestone, note deviations) and `CHANGELOG.md`, then commit.
5. Tell me what to test on the headset (`adb install -r Builds/GateRush.apk`) and what to look for.

Move the input-diagnostics part of M6 forward to **M3**, so I can plug my BetaFPV radio into the headset early and send you the device names and axis readings. Then you can fill in the presets.

Stop and ask me only for things you can't do yourself: Unity sign-in or license, Asset Store purchases, the Meta developer account / App ID, testing on the physical headset, and subjective tuning feedback ("does it feel floaty?"). For everything else, make a reasonable decision, note it in `PLAN.md` under "Decisions", and continue.

Start now with M0. First confirm `unity --version`, `unity auth status`, and `unity license status`.
