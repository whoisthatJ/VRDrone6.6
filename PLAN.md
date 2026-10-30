# FPV Racing Sim for Meta Quest — Project Plan
Working title: **"GateRush VR"** (an original name, so this is a game *inspired by* VelociDrone, not a copy of its brand, tracks or art)

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
| M3 | Quest + VR views | Goggles mode, LOS mode, Immersive mode; Touch controller Mode 2 mapping; arming switch; stable 72/90 fps |
| M4 | Racing | Gate prefabs (standard race-gate sizes), checkpoints, lap timer, 3-lap race, results, best lap + ghost replay, local leaderboard |
| M5 | Content + settings | 3 quad classes (5" race, 5" freestyle, whoop), 3 tracks (indoor hangar, outdoor field, industrial), rates/camera/physics settings UI, save/load |
| M6 | RC radio + **BetaFPV** support | Generic HID joystick over USB-C OTG and Bluetooth; diagnostics screen; calibration wizard; BetaFPV presets (LR2 SIM, LR3, LR4 USB/BT) auto-applied by device name; Android axis fallback plugin; hot-plug handling; on headset: an LR2 SIM (USB) and an LR4 (Bluetooth) can arm and fly a full lap |
| M7 | Track editor (VR) | Place, rotate and scale gates with controllers, snap, set checkpoint order, save/load JSON, share by file |
| M8 | Polish + perf | Baked lighting, LODs, FFR, motor audio, crash/prop VFX, analog VTX noise effect, OSD HUD, comfort options |
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
| 2026-10-30 | Unity CLI is 1.0.0-beta.6 and not signed in (license is active). It wasn't needed: the live Editor is driven through the Unity Editor MCP | — |

## 9. Notes for tuning
- `QuadProfile.Race5Inch`: 650 g, 15.7 N/motor (≈10:1 TWR), hover command ≈ 0.28, motor τ 30/40 ms, rate P 0.03 / I 0.1 / D 0.0004 (pitch/roll), yaw P 0.15 / I 0.3. Rate-step tests show < 100 ms rise time (pitch/roll) and < 200 ms (yaw) at half stick, under 25 % overshoot.
