# FPV Racing Sim — Project Plan

> **Platform (2026-11-02): desktop PC, no VR.** GateRush is now a Windows simulator played on a monitor with an RC radio,
> gamepad or keyboard. The XR packages, rigs and VR views were removed in T0 of `TRACK_GARAGE_PLAN.md`; the Quest/VR
> sections below (§2, §3 build profile, §6, M0/M3/M7 headset notes) are kept as history only.

Working title: **"GateRush"** (was "GateRush VR") (an original name, so this is a game *inspired by* VelociDrone, not a copy of its brand, tracks or art)

## 1. What we are cloning: VelociDrone's core loop
| VelociDrone pillar | What it means for us |
|---|---|
| Physics that feel like a real quad: tweakable, Betaflight-style rates, Acro/Angle/Horizon modes, Air Mode | **This is the product.** We need a custom flight model, not PhysX "push a rigidbody" |
| 20+ real racing quads (5" race, 5" freestyle, toothpick, micro/whoop, Mega class) | Data-driven `QuadProfile` assets (mass, motor kv, prop, thrust curve, drag, rates) |
| Real race-league tracks + the best track editor around | Gate prefabs in standard race sizes, JSON tracks, an in-VR editor |
| Time attack, global leaderboards, single player, online and offline multiplayer | Lap timer, checkpoints, ghosts, local leaderboard first, online later |
| Real RC radios (USB joystick or ELRS) | Unity Input System HID joystick + calibration screen, plus Touch controllers and gamepads |
| FPV camera settings: tilt, FOV, analog VTX noise | Camera tilt/FOV settings, optional analog-noise post effect |

## 2. VR design decisions (the part VelociDrone doesn't solve for us)
FPV in a 6DoF headset can make people sick: the image rotates with the drone, not with your head. Real goggles show one flat image to both eyes. Existing Quest FPV sims (DVR Simulator, Yue's VR-FPV-Sim) handle this with several view modes. We ship three:

1. **Goggles mode (default):** the drone camera renders to a RenderTexture shown on a large curved screen in a dark virtual room. It's mono, the same image in both eyes, like real goggles. The screen can follow the head smoothly or stay fixed in the world. This is the most comfortable mode and the most authentic one.
2. **Immersive mode (opt-in, "Intense" comfort rating):** the XR camera rig is parented to the drone with camera tilt applied, and head rotation adds on top of that. A vignette shows during high angular rates.
3. **Line-of-sight (LOS) mode:** the pilot stands on the field and watches the quad fly in 6DoF. This only makes sense in VR and is great for beginners.

Comfort extras: a vignette strength slider, an option to show the horizon line, a 3-2-1 countdown before arming, and recentering on the Meta button.

## 2b. BetaFPV controller support (first-class requirement)
BetaFPV radios are the cheapest way into FPV, so they are the **primary radio target** and must work on the Quest itself (Android). No PC is needed.

| Model | How it connects to the Quest | Notes |
|---|---|---|
| **LiteRadio 2 SIM** (sim-only controller) | USB-C cable (OTG). Joystick / Xbox / Dongle modes; appears as **"BETAFPV Joystick"** | 8 channels, switches A–D. Joystick mode is the default; Xbox mode shows up as a gamepad |
| **LiteRadio 2 SE / LiteRadio 3** | USB-C cable, HID joystick | Android compatibility may need a firmware/config change in **BETAFPV Configurator**. Some users report that LR2 joystick mode doesn't work on Android out of the box |
| **LiteRadio 4 / 4 SE** (ELRS) | **Bluetooth joystick mode** (wireless, best for VR) or USB-C cable | 10 channels. USB joystick mode only outputs a signal when the radio is **switched off** and powered by the cable |
| Any other radio (RadioMaster, Jumper, TBS, FrSky with EdgeTX/OpenTX) | USB HID joystick, ELRS Backpack/module Bluetooth joystick | Uses the same generic path |

**How we support it**
- **One generic HID-joystick path.** BetaFPV is a *preset* on top of it, not special-case code. The Unity Input System lists any Android joystick; we read all of its axes and buttons.
- **Device presets** (`ControllerPreset` ScriptableObject) matched by product name or regex (`BETAFPV Joystick`, `LiteRadio*`, `BETAFPV*BLE*`). Each preset has the default channel → axis map, inversions, which switch is ARM / MODE / RESET, and expected axis ranges. Ship presets for LR2 SIM (Joystick mode), LR2 SIM (Xbox mode), LR3, LR4 (USB) and LR4 (Bluetooth). The numbers in each preset get **verified on real hardware** with the diagnostics screen below. Start with sensible guesses and mark them `verified=false`.
- **Calibration wizard in VR:** move sticks to all corners → center → flip each switch. This detects axis min/center/max, the throttle axis, and switch positions (2-position and 3-position). It's saved per device name, and a preset can be the starting point.
- **Input diagnostics screen:** live bars for every raw axis and button of every connected device, plus device name, vendor/product ID and the interface it came from. This is how users (and we) debug any radio.
- **Fallback when Unity doesn't see all the axes:** an Android plugin (Kotlin/Java AAR) reads `MotionEvent` axes from `InputDevice`s with `SOURCE_JOYSTICK`, including `AXIS_X/Y/Z/RX/RY/RZ/THROTTLE/RUDDER/GAS/BRAKE/HAT_*` and `AXIS_GENERIC_1..16`, and pushes them to C#. If needed, there's a second fallback: raw USB HID through `UsbManager` (with USB host permission). Add `android.hardware.usb.host` (`required=false`) and Bluetooth permissions to the manifest.
- **In-game help page:** "Connecting a BetaFPV radio": which mode to pick, LR4 Bluetooth pairing through the Quest's Settings → Devices → Bluetooth, the USB-C OTG cable, and enabling Android compatibility in BETAFPV Configurator.
- **Hot-plug:** a radio connecting or disconnecting mid-flight → pause the race and show a "Controller disconnected" overlay; auto-resume after the sticks are centered and the radio is re-armed.

## 3. Tech stack
- **Unity 6 LTS** (6000.0.66f2 or newer, 6.1+ preferred) with the **URP** template
- **Build profile:** Meta Quest (Android, ARM64, IL2CPP, Vulkan)
- **XR:** XR Plugin Management + **Unity OpenXR Plugin** (the Oculus XR Plugin is deprecated), the **Meta XR Core SDK** (`com.meta.xr.sdk.core`), and Meta's Project Setup Tool. Use "Fix All" on the Android tab
- **Input:** Input System package (Touch controllers, gamepad, HID joystick/RC radio)
- **UI:** world-space uGUI + TextMeshPro, or UI Toolkit on a world-space panel
- **Later:** Netcode for GameObjects + Unity Multiplayer Services (sessions), and Unity Leaderboards
- **Testing:** Unity Test Framework (EditMode tests for the flight model), Meta XR Simulator for testing in the editor without a headset

## 4. Architecture
```
Assets/_Project/
  Scripts/
    Flight/        QuadSimulator (pure C#), RateProfile, PidController, MotorModel, Mixer, AeroModel, BatteryModel
    Input/         PilotInput (normalized sticks), InputSources: TouchControllers, Gamepad, RcJoystick, Calibration
    Vehicle/       QuadVehicle (MonoBehaviour bridge -> Transform), PropVisuals, MotorAudio, CrashHandler
    Camera/        FpvCamera (tilt/FOV), GogglesScreen, ImmersiveRig, LosRig, ComfortVignette
    Race/          Gate, Checkpoint, LapTimer, RaceSession, GhostRecorder/Player, Leaderboard (local JSON)
    Track/         TrackData (JSON), TrackLoader, TrackEditor (VR)
    UI/            MainMenu, QuadSelect, SettingsRates, HUD (OSD-style), Results
    Core/          GameState, SaveSystem, Settings
  Data/            QuadProfiles/*.asset, Tracks/*.json
  Prefabs/  Scenes/ (Boot, Menu, Track_*), Art/
Tests/EditMode/   Flight model and rates tests
```

**Key rule:** the flight model is plain C#. It has no dependency on UnityEngine physics and is deterministic given (state, input, dt). `QuadVehicle` runs it with sub-steps (e.g. 8 × 1 ms inside every frame) and writes the pose to the Transform. Collisions use a capsule/sphere cast along the swept path, and the response is either bounce or crash. This keeps it fast on the Quest CPU and makes it testable.

### Flight model (MVP → Pro)
1. **Rates:** Betaflight rates (RC Rate, Super Rate, Expo) and Actual Rates (Center, Max, Expo) → target deg/s per axis
2. **Rate PID** per axis (Acro). Angle and Horizon modes add an outer angle loop. Air Mode keeps PID authority at zero throttle
3. **Mixer** (Quad-X) → 4 motor commands; desaturation
4. **Motor model:** first-order lag (time constant ~20–40 ms), thrust ∝ RPM², torque for yaw, throttle curve
5. **Aero:** linear + quadratic drag per body axis, induced drag, a simple prop-wash disturbance when descending into your own wash
6. **Battery sag** (optional): voltage drops over the flight and under load
7. **Ground effect**, turtle mode (flip over after a crash)

## 5. Milestones and acceptance criteria
| # | Milestone | Done when |
|---|---|---|
| M0 ✅ (headset check pending) | Project bootstrap | Unity 6 URP project, Meta Quest profile, OpenXR + Meta XR Core, Project Setup Tool clean, git + LFS, blank scene runs at 72 Hz on headset |
| M1 ✅ | Flight model (headless) | `QuadSimulator` + rates + PID + mixer + motors + drag; EditMode tests pass (hover equilibrium, rate step response, rates math matches the Betaflight formula, determinism) |
| M2 ✅ | Flyable in editor | Flat test field, quad flies with gamepad/keyboard in Acro and Angle, FPV camera with tilt; debug overlay of rates and motors |
| M3 ✅ (headset check pending) | Quest + VR views | Goggles mode, LOS mode, Immersive mode; Touch controller Mode 2 mapping; arming switch; stable 72/90 fps |
| M4 ✅ | Racing | Gate prefabs (standard race-gate sizes), checkpoints, lap timer, 3-lap race, results, best lap + ghost replay, local leaderboard |
| M5 ✅ | Content + settings | 3 quad classes (5" race, 5" freestyle, whoop), 3 tracks (indoor hangar, outdoor field, industrial), rates/camera/physics settings UI, save/load |
| M6 ✅ (hardware check pending) | RC radio + **BetaFPV** support | Generic HID joystick over USB-C OTG and Bluetooth; diagnostics screen (✅ moved to M3); calibration wizard; BetaFPV presets (LR2 SIM, LR3, LR4 USB/BT) auto-applied by device name; Android axis fallback plugin; hot-plug handling; on headset: an LR2 SIM (USB) and an LR4 (Bluetooth) can arm and fly a full lap |
| M7 ✅ (headset check pending) | Track editor (VR) | Place, rotate and scale gates with controllers, snap, set checkpoint order, save/load JSON, share by file |
| M8 ✅ (headset perf check pending) | Polish + perf | Baked lighting, LODs, FFR, motor audio, crash/prop VFX, analog VTX noise effect, OSD HUD, comfort options |
| M9 | Online (stretch) | Session-based multiplayer race (up to 6–8 pilots), synced start, interpolated remote quads; online leaderboards |

## 6. Quest performance budget
- 72 Hz minimum (90 Hz target): about 11–13 ms per frame
- ≤ 100 draw calls and ≤ 300k triangles on screen; baked lighting, no realtime shadows, single-pass instanced rendering
- In Goggles mode the drone camera renders to a ~1280×720 RT (configurable); the room around it is nearly empty
- Fixed foveated rendering on, MSAA 4x, no post-processing in the eye buffer except a cheap vignette
- Flight sim sub-steps run on the main thread (cheap); budget < 0.5 ms

## 7. Risks
| Risk | Mitigation |
|---|---|
| Motion sickness | Goggles mode is the default; Immersive mode is opt-in with a warning |
| "Feel" isn't right | Expose every physics constant; tune with real FPV pilots; compare against Betaflight rate curves |
| RC radios on Quest (Android HID) vary; some BetaFPV units aren't recognized on Android with stock settings | Generic HID path + Android MotionEvent fallback plugin + calibration; diagnostics screen; help page for BETAFPV Configurator settings; buy an LR2 SIM + LR4 for testing |
| Touch controller sticks are short and spring-centered on both axes (throttle!) | Throttle modes: "spring-center = hover assist" or "trigger = throttle"; recommend a gamepad or radio |
| IP / trademark | Original name, quads, tracks and art; no VelociDrone or league logos |

## 8. Decisions
| Date | Decision | Why |
|---|---|---|
| 2026-10-30 | Build inside the existing `VRDrone6.6` project (Unity **6000.6.0f1**, URP 17.6) instead of a new `./GateRush` folder | It was already open, Quest-configured, and is the only installed editor with Android + SDK/NDK + OpenJDK. GateRush code lives in `Assets/_Project/`. Template content (XRI samples, `Assets/Scenes`) is left untouched for now and can be deleted later |
| 2026-10-30 | Use Unity's **Meta OpenXR** package (`com.unity.xr.meta-openxr`) for Quest features; **Meta XR Core SDK not installed yet** | The project already ships Unity's Meta Quest OpenXR feature group, display-refresh and foveation APIs. Adding `com.meta.xr.sdk.core` on top risks duplicate Quest features. Revisit if we need Meta-only features (passthrough API, Project Setup Tool, Meta XR Simulator) |
| 2026-10-30 | Android XR (non-Meta) OpenXR features disabled | Quest-only target |
| 2026-10-30 | minSdk 32, targetSdk 34 | Meta Horizon Store minimum |
| 2026-10-30 | Single quality level `Quest` with its own URP asset `Assets/_Project/Settings/URP_Quest.asset` (MSAA 4x, HDR off, shadows off, render scale 1.0) | §6 budget. Template URP assets left in place |
| 2026-10-30 | Refresh rate (72/90 Hz) and fixed foveation are set at runtime by `GateRush.Core.XrDisplayConfig` | Unity OpenXR exposes these through `XRDisplaySubsystem`, not project settings |
| 2026-10-30 | `PilotInput` lives in `GateRush.Flight` (not `Input/`) | The simulator consumes it and must not depend on input code. `GateRush.Input` will reference `GateRush.Flight` |
| 2026-10-30 | Flight structs (`QuadProfile`, `RateProfile`, `PhysicsSettings`) are plain `[Serializable]` structs in `GateRush.Flight`; the ScriptableObjects that wrap them will be `QuadProfileAsset` / `RateProfileAsset` / `PhysicsSettingsAsset` in the Vehicle layer (M2) | Keeps `GateRush.Flight` engine-free (`noEngineReferences: true`) |
| 2026-10-30 | Sim axes use Unity's convention (X right, Y up, Z forward, left-handed). Body rates: X = pitch (nose down +), Y = yaw (right +), Z = roll (left +). Motor order is Betaflight Quad-X, props-in | No conversion in the bridge |
| 2026-10-30 | `QuadSimulator.Step` has an overload taking `PhysicsSettings`; the 4-argument signature uses `PhysicsSettings.Default` | Keeps the requested signature while allowing gravity/step to be tuned |
| 2026-10-30 | EditMode tests run inside the open Editor through `GateRush.EditorTools.TestRunnerBridge` (writes `TestResults/editmode.xml`, JUnit) | `unity test` starts a batchmode Editor, which can't open a project that's already open. `unity test` still works when the Editor is closed |
| 2026-10-30 | Camera code uses namespace/assembly `GateRush.Cameras` (folder stays `Scripts/Camera/`) | A `GateRush.Camera` namespace would shadow `UnityEngine.Camera` inside every `GateRush.*` namespace |
| 2026-10-30 | Collisions run through a generic `IStepHook` struct passed to `QuadSimulator.Advance<THook>` (`Vehicle/CollisionHook`): one sphere cast per 1 ms sub-step against layer `TrackGeometry` (8), restitution + Coulomb friction, crash above 7 m/s normal impact or below y = -20, respawn after 1.5 s | Keeps collisions out of the engine-free sim without allocating delegates |
| 2026-10-30 | The pilot's own quad visuals are on layer `LocalQuad` (9), hidden from the FPV camera | Props filled the corners of a 120° view. LOS/spectator cameras will still render it |
| 2026-10-30 | Arming: Space/Start toggles, only with throttle ≤ 0.05; mode cycles Acro → Angle → Horizon (M / Select). Keyboard throttle ramps with W/S | Matches radio habits; keyboard is only an editor convenience |
| 2026-10-30 | Gamepad throttle defaults to spring-centre = hover (`ThrottleMapping.SpringCenterHover`); trigger and direct mappings available | §7 risk on spring-centred sticks |
| 2026-10-30 | `ScriptedInputSource` drives automated play-mode smoke tests through the Unity MCP | Simulated keyboard events from the MCP only reach the Editor's input state, not the player loop, while the Editor window is unfocused |
| 2026-10-30 | Input diagnostics (moved forward from M6 into M3): `InputDiagnostics` scene + `InputDeviceReport` logger. Every device change is logged to logcat with full description and capabilities JSON, and `input-devices.txt` is rewritten in the app's files dir | Lets the user plug a BetaFPV radio into the headset early and send back exact device names, VID/PID and axis readings |
| 2026-10-30 | Scene flow: `Boot` (persistent `App`: `AppBootstrap`, `XrDisplayConfig`, `PerfLogger`, `SceneSwitcher`) → `Track_TestField`. Temporary switch to `InputDiagnostics` with the left Touch menu button / F1 / gamepad right-stick click until the M5 main menu | No menu yet |
| 2026-10-30 | Input sources compete by `Priority` (scripted 100 > RC radio 30 > gamepad 20 > Touch 10 > keyboard 1) through `VehicleInputRouter`; switching source disarms | Extensible for the M6 radio path and hot-plug |
| 2026-10-30 | Touch Mode 2: left stick throttle/yaw, right stick pitch/roll; A = arm (throttle low, i.e. pull the left stick fully down when spring-centred), B = flight mode, X = reset, Y = view mode (hold Y = recentre goggles screen) | Leaves the menu button free for the scene switch |
| 2026-10-30 | Goggles view: drone camera → 1280×720 RT (MSAA 2x) on a 60° curved mesh at 2 m, layer `VRRoom` (10), head camera culls everything else and clears to near-black. The debug OSD is a Screen Space–Camera canvas on the drone camera, so it shows inside the feed | Mono feed, cheap eye buffer, authentic look |
| 2026-10-30 | Immersive view moves the XR Origin each `LateUpdate` so the head sits on the drone with uptilt; vignette is a camera-attached transparent URP Unlit quad with a generated radial mask (no post-processing) | §6 budget: no post in the eye buffer |
| 2026-10-30 | Without an XR device (Editor), Goggles/Immersive show the drone camera on the main display and LOS uses the rig camera looking at the quad | Desktop testing |
| 2026-10-30 | APK builds are started with `BuildScript.BuildQuestDeferred()` (via MCP eval) and report to `Builds/build-result.txt` | The CLI build can't run while the Editor has the project open, and a blocking eval would time out |
| 2026-10-30 | Race rules: gate 0 is start/finish; after GO the pilot flies gate 0 (holeshot), 1 … N-1, then 0 again = lap. Lap 1 runs from GO (includes the launch). Wrong gates don't count and show "MISSED GATE"; the pilot must go back | Common timing convention; simple to explain |
| 2026-10-30 | Gate timing uses the sim position each frame, a segment-vs-opening test in the gate's forward direction, and interpolates the crossing time inside the frame | Accurate to well under a frame without trigger colliders (the quad isn't a Rigidbody) |
| 2026-10-30 | Gate sizes: `Square` 1.52 m (5 ft, the common race-gate opening), `Large` 2 m, `Hurdle` 3 × 1.5 m open-bottom. Prefabs built from code (`GateBuilder`) into `Prefabs/Gates/`; frames collide (TrackGeometry). Next gate green, the one after orange | Original geometry, standard sizes |
| 2026-10-30 | Start: arming on the pad starts a 3-2-1 countdown with the quad held (`QuadVehicle.HoldAtSpawn`); disarming cancels. Crash/reset mid-race respawns 2.5 m before the last gate passed, held until re-armed + throttle > 0.12; the clock keeps running | Comfort countdown from §2; no free time for crashes |
| 2026-10-30 | Ghosts: the best lap is recorded at 30 Hz (preallocated, no GC) and replayed from the start of each lap; best ghost per track + quad saved to `persistentDataPath/ghosts/*.ghost` (binary) | Local first, as planned |
| 2026-10-30 | Local leaderboard: top 10 races per track by total time + the track lap record, JSON at `persistentDataPath/leaderboard.json`. Pilot name is "Pilot" until the M5 settings | §1 local leaderboard first |
| 2026-10-30 | Race HUD lives on the OSD canvas (drone camera), so it shows in Goggles view and on the desktop; Immersive/LOS don't show it yet (world/head-locked HUD planned for M8) | Keep M4 scope |
| 2026-10-30 | Restart race: T / gamepad left-stick click / left Touch stick click | Free buttons |
| 2026-10-30 | `RaceAutopilot` (Angle mode, drives a `ScriptedInputSource`) for automated race smoke tests and a future attract/demo mode. It's not precise enough for 1.52 m gates every lap: the M4 smoke test scaled the gates 2.5× | Tooling, not gameplay |
| 2026-10-30 | Quad classes: `Race5Inch` (650 g, ~10:1), `Freestyle5Inch` (750 g, ~7.5:1, Actual rates), `Whoop65` (32 g, ~3:1, ducted drag). PID gains sized per class from torque authority, inertia and motor lag (ωn ≈ 40 rad/s, ζ ≈ 0.7); every class has hover and rate-step tests | One sizing method instead of hand-tuning by feel; feel tuning comes from headset feedback |
| 2026-10-30 | Tracks are JSON (`TrackData`: gates in order, props, spawn, laps, environment id). Built-in courses are defined in `CourseLibrary` and written to `Data/Tracks/*.json`; the game only loads the JSON. Gate headings are computed from neighbouring gates | Same format the M7 editor will read/write and share |
| 2026-10-30 | One scene per **environment** (`Track_Field`, `Track_Hangar`, `Track_Industrial`); `TrackSceneController` loads the selected track's JSON into it at runtime. `Track_TestField` was removed | Environments are static art; courses are data |
| 2026-10-30 | Settings (`GameSettings`, JSON in `persistentDataPath/settings.json`, atomic save, clamped, missing fields keep defaults): pilot callsign, quad, track, custom rates (Betaflight/Actual) or the quad's own, camera tilt/FOV/view/goggles size/follow, Touch + gamepad throttle mapping, deadband, expo, vignette, 72/90 Hz, crash speed, drag scale, motor-lag scale. Applied live in track scenes (`SaveSystem.SettingsChanged`) | §5 "rates/camera/physics settings UI, save/load" |
| 2026-10-30 | New `GateRush.Game` assembly (catalogs, `GameFlow`, `TrackSceneController`) sits above Vehicle/Cameras/Race/Track; Core only holds data (`GameSettings`, `SaveSystem`) | Keeps lower assemblies free of cycles |
| 2026-10-30 | VR menu uses XR Interaction Toolkit 3.6 (already in the project): `XRRayInteractor` lasers on both Touch controllers (aim pose, trigger = click) + `XRUIInputModule` (mouse/gamepad too). Menu UI is built at runtime from code (`UiKit`, no sprites) | Standard XR UI path; less scene YAML |
| 2026-10-30 | Pilot name is a callsign picked from a list for now | No VR text entry yet (Meta system keyboard needs the Meta SDK) |
| 2026-10-30 | Boot → Menu; in any scene the left Touch menu button / Esc / F1 / gamepad R-stick click returns to the menu (`SceneSwitcher`) | Replaces the M3 flight↔diagnostics toggle |
| 2026-10-30 | Builder rule: create the new scene **before** loading assets it references (`EditorSceneManager.NewScene` unloads unused assets). `BootSceneBuilder.BuildAll` verifies every GateRush reference in every scene and fails loudly | A latent bug from M3/M4 that showed up with the new catalogs |
| 2026-10-30 | Tooling rule: don't write files under `Assets/` while an APK build is running | Files written as a build finished never compiled until their metas were regenerated |
| 2026-10-30 | **Radio pipeline**: `RadioDeviceProvider` finds the radio (any Input System joystick/HID, or the Android raw reader), matches a `ControllerPreset` by product-name regex, and resolves the mapping: saved calibration, else preset, else a generic AETR guess. `RcRadioSource` (priority 30) flies from it | §2b: one generic path; BetaFPV = presets on top |
| 2026-10-30 | Channels are numbered the same everywhere (Radio setup, Input diagnostics `chN`, presets): axes in HID order (x, y, z, rx, ry, rz, throttle, rudder, slider, dial, wheel, then by path), then buttons by path. Radio axes are read **unprocessed** | Stable preset indices; the joystick layout's default stick deadzone (~12%) would otherwise distort gimbals |
| 2026-10-30 | Shipped BetaFPV presets (LR2 SIM joystick, LR2 SIM Xbox → gamepad path with full-range throttle, LR3, LR4 USB, LR4 Bluetooth, generic BetaFPV) all use the same guess (AETR on ch0-3, arm ch4, 3-position mode ch5) and `Verified = false` | Waiting for real device names/readings from the user |
| 2026-10-30 | Arm/mode come from switches: Betaflight-style arming (switch on with throttle low; flipped on with throttle high blocks until cycled; off always disarms); mode switch sets Acro/Angle/Horizon directly. No arm switch → arms whenever throttle is low | Matches how pilots fly real radios |
| 2026-10-30 | **Calibration wizard** (`CalibrationWizard`, pure C#): stir sticks → centre/hold → throttle up → yaw right → pitch forward → roll right → arm switch → mode switch → optional 3rd position. Steps **auto-advance when the position is held 1 s**, so it works with both hands on the radio; Next/Skip/Cancel via laser pointer or Enter. Saved per device (`name|VID|PID`) in `persistentDataPath/radios.json` | Usable in VR without putting the radio down |
| 2026-10-30 | **Android fallback**: Java source in `Plugins/Android/GateRushRadio.androidlib` (a Gradle library module) instead of a prebuilt Kotlin AAR. It wraps the activity window's `Window.Callback` (works with GameActivity, no entry-point change), records joystick `MotionEvent` axes incl. `AXIS_GENERIC_1..16` and gamepad button keys into a direct `ByteBuffer`; C# reads it via `GetDirectBufferAddress` with a per-slot sequence counter (lock-free, no per-frame JNI or GC). Auto backend switches to it when it sees more axes than the Input System; Radio setup can force it | Same result, no separate Android Studio project to maintain. **Not yet compiled by Gradle**: APK builds are skipped at the user's request, so the first APK build will verify it |
| 2026-10-30 | Manifest additions via the androidlib manifest: `android.hardware.usb.host` (required=false), `android.hardware.bluetooth` (required=false), `BLUETOOTH` permission up to API 30. Bluetooth HID input itself needs no runtime permission | §2b |
| 2026-10-30 | **Hot-plug**: `VehicleInputRouter.SourceChanged` → `ControllerHotplugGuard` pauses the race (`Time.timeScale = 0`) when the active controller changes during the countdown/race, shows an OSD overlay, and resumes after the active controller has centred sticks and is armed for 0.4 s | §2b hot-plug |
| 2026-10-30 | APK builds are skipped from M6 on at the user's request; verification is compile + EditMode tests + Editor play-mode smoke tests | User preference |
| 2026-10-30 | Track editor = an edit mode of each environment scene: `GameFlow.Edit` hands the track to the scene, `TrackSceneController` hides the quad/race, switches the view to line-of-sight and starts `TrackEditorController`. Test fly loads the edited course into the race in place; the menu button returns to the editor (`SceneSwitcher.MenuOverride`) | One scene per environment, no separate editor scenes; instant test-fly |
| 2026-10-30 | Editing logic is pure (`TrackEditModel`: add/remove/move gates & props, spawn, race order, reverse direction, laps, 0.5 m / 15° snapping, scale 0.5–4×, 50-step undo/redo with a whole drag as one step); the view (`TrackEditorView`) mirrors it with prefabs, START/number labels and a course line | Testable without VR |
| 2026-10-30 | Controls: right ray + trigger select/drag (stick X rotate, stick Y push/pull, grip + stick Y scale), A add, B delete; left stick move, trigger/grip down/up, X undo, Y piece type; right stick snap-turn. Wrist panel (left hand) of physics-raycast 3D buttons: piece type, add, delete, snap, earlier/later, reverse, laps, undo/redo, save, test fly, spawn, exit. Desktop fallback with mouse/keyboard | Self-contained: no XRI in the track scenes, same pointer for world and panel |
| 2026-10-30 | User tracks: `TrackLibrary` = one JSON per track in `persistentDataPath/tracks`, ids `user-<name>` (unique). Sharing = copying files (adb/SideQuest); foreign files are re-keyed by file name so they never clash with built-ins; unreadable files are skipped. Built-ins can be copied and edited; user tracks appear in Choose track and keep their own leaderboard/ghosts | "Share by file" |
| 2026-10-30 | Cached XR controls are now checked with `device.added` everywhere (view input, Touch source, scene switcher, race restart, diagnostics, editor) | An XR runtime on the dev PC dropped the controllers without a change event and the old controls threw every frame |
| 2026-10-30 | Workflow rule: never edit scripts while the Editor is in play mode | The resulting domain reload wipes runtime state and looks like a code bug |
| 2026-10-30 | **Battery**: `BatteryModel` (LiPo open-circuit curve, I·R sag, current ∝ motor speed³, mAh drain); packs per class (4S 1300 race, 6S 1100 freestyle, 1S 300 whoop). With `PhysicsSettings.BatterySag` thrust scales with (V/V_full)². Off by default in the sim (tests), on by default in the game settings | Real punch-outs fade on a sagging pack; deterministic and testable |
| 2026-10-30 | **Motor audio** is procedural (`QuadAudio`, `OnAudioFilterRead`): per-motor saw at the blade-pass frequency + filtered airflow noise + crash thump + lap/start beeps. No audio assets. 2D in FPV views, 3D at the quad in LOS | No asset pipeline/licensing; reacts to every motor |
| 2026-10-30 | **VFX**: prop-blur discs that cross-fade with RPM; one pooled particle system (`Emit`) for crash/impact sparks | Pooled, no runtime instantiation |
| 2026-10-30 | **Analog VTX**: custom URP shader `GateRush/AnalogFeed` on the goggles screen (snow, colour loss, scanlines, line jitter, static bands; single-pass-instanced) driven by `VtxSignal` (distance from the pilot + raycast obstacles, crash burst); Off / Light / Realistic. Only in Goggles view (it is part of the screen, not eye-buffer post) | §6: no post-processing in the eye buffer |
| 2026-10-30 | **OSD**: Betaflight-style (voltage + per-cell, mAh, mode/DISARMED, armed timer, throttle, speed/altitude, LQ, crosshair, artificial horizon, LOW BATTERY / LINK warnings). Developer readout moved behind a setting. **Head HUD** (lap/time) for Immersive/LOS in XR | OSD only exists inside the FPV feed |
| 2026-10-30 | **Comfort/display**: foveation Off/Low/Med/High, horizon ring in Immersive, motor volume, VTX noise, debug readout, battery sag (new Feed tab + Comfort/Physics) | §2 comfort extras |
| 2026-10-30 | **LODs**: gate and prop prefabs get a culling LODGroup (0.4% / 0.6% screen height); static environment geometry is static-batched | Cheap far-field |
| 2026-10-30 | **Baked lighting**: per-environment LightingSettings (baked indirect, non-directional, compressed, sun Mixed so direct light stays realtime without shadows), emissive lamps baked, light-probe grids for runtime gates/props/quad; `LightingBaker` bakes all three in ~25 s. Rebuilding scenes drops the bake: run *Bake All Track Lighting* after *Build All Scenes* | Quest budget; runtime-loaded tracks can't be lightmapped |
| 2026-10-30 | Unity CLI is 1.0.0-beta.6 and not signed in (license is active). It wasn't needed: the live Editor is driven through the Unity Editor MCP | — |
| 2026-11-01 | **PC (Windows) platform**, branch `feature/pc-radiomaster-pocket`: Standalone is flat-screen. The OpenXR loader is removed from Standalone (Android keeps it), so `ViewModeManager` puts the drone camera on the monitor. Build with *GateRush/Build/Build Windows (PC)* → `Builds/Windows/GateRush.exe` | With the loader assigned, OpenXR probes the runtime natively during graphics init even with "Initialize on Startup" off: a ~6 s stall plus `XR_ERROR_FORM_FACTOR_UNAVAILABLE` when no headset is connected. PC VR (Link/SteamVR) = re-add the loader for Standalone |
| 2026-11-01 | Standalone scripting backend is **Mono** (Android stays IL2CPP) | Windows IL2CPP module isn't installed; Mono needs no extra module and is fine for the sim's CPU budget on PC |
| 2026-11-01 | **RadioMaster Pocket / EdgeTX** presets (`RM_Pocket`, `EdgeTX_Generic`, unverified). EdgeTX USB joystick = CH1-8 on X/Y/Z/Rx/Ry/Rz/Slider/Dial; the Input System HID fallback inverts Y and Ry, so the presets un-invert pitch (CH2) and flip the CH5 arm switch positions. No Windows driver needed (standard HID). Windows showing "STM32 BOOTLOADER" (0483:DF11) means the radio is in DFU/flash mode, not joystick mode | Second radio target alongside BetaFPV; same generic HID path |
| 2026-11-02 | **Desktop only (T0 of TRACK_GARAGE_PLAN.md)**: XR packages, VR template content, XR rigs, Touch input, goggles/immersive views and comfort settings removed; Cinemachine FPV / chase / line-of-sight cameras, screen-space OSD with a next-gate indicator, Low quality preset and a frame cap added | The owner dropped VR; the game is a PC sim for students with radios |
| 2026-11-03 | **Localization: Kazakh (default), Russian, English** with the Unity Localization package. Keys are the English text (`L.T("Quick race")`, `L.F("{0} laps", n)` in `Core/Localization/L.cs`); translations are kept in reviewable tab-separated sheets `Assets/_Project/Localization/Text/*.tsv` (en, kk, ru) and *GateRush > Localization > Import Strings* builds the "UI" string table collection from them. The language is a setting (`GameSettings.Language`, default `kk`), switched from the main menu button or Settings > Pilot. A test scans every `L.T`/`L.F` literal in the code and fails on a missing kk/ru row, mismatched placeholders or tags, duplicates, or tables behind the sheets. Liberation Sans already has every Kazakh letter, so no font swap | Code-built UI makes per-component LocalizeStringEvent bindings impractical; the English-key wrapper keeps the code readable and the sheets are easy for a native speaker to review |
| 2026-11-03 | **M9 online**: opt-in only (nothing connects until the Online page is opened; lap posting is a Settings > Pilot toggle, off by default). The host's PC runs the race and Unity brokers the connection (works behind home routers). Netcode for GameObjects with **named messages only** (no networked prefabs, no scene sync): each pilot flies their own quad locally and sends 20 Hz snapshots, the host forwards them, others show them 120 ms in the past (interpolated, short extrapolation). Start: the host waits until every pilot reports "on the grid" (25 s timeout) and sends one GO time on the shared server clock. Remote quads are visual only (no quad-to-quad collisions). Grid: kit pad slots, pilots 5-8 two metres behind. Built-in tracks only online. Standings = finishers by time, then gates passed + leg fraction (`RaceProgress`) | Simplest model that keeps the deterministic local flight sim untouched; client-authoritative is acceptable for a student sim. Verified with two builds on one PC (`-online-profile`, `-online-host`, `-online-join-any`, `-online-autopilot`, `-online-laps`, `-online-quit`): same GO time on both, finishes 0.1 s apart, identical results |

## 9. Notes for tuning
- `QuadProfile.Race5Inch`: 650 g, 15.7 N/motor (≈10:1 TWR), hover command ≈ 0.28, motor τ 30/40 ms, rate P 0.03 / I 0.1 / D 0.0004 (pitch/roll), yaw P 0.15 / I 0.3. Rate-step tests show < 100 ms rise time (pitch/roll) and < 200 ms (yaw) at half stick, under 25 % overshoot.
