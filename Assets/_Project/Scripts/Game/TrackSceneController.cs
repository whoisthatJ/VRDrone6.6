using Ushqyr.Cameras;
using Ushqyr.Core;
using Ushqyr.Flight;
using Ushqyr.Input;
using Ushqyr.Race;
using Ushqyr.Track;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Game
{
    /// <summary>
    /// Sits in every track scene: loads the selected track's JSON (gates, props, spawn) into the environment,
    /// configures the quad from the selected class, and applies the player's settings (again whenever they change).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class TrackSceneController : MonoBehaviour
    {
        [Tooltip("Environment id of this scene (\"field\", \"hangar\", \"industrial\").")]
        [SerializeField] private string environment = "field";
        [SerializeField] private QuadCatalog quads;
        [SerializeField] private TrackCatalog tracks;
        [SerializeField] private TrackPrefabs trackPrefabs;

        [Header("Scene references")]
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private RaceSession race;
        [SerializeField] private Transform quadVisual;
        [SerializeField] private Transform ghostVisual;
        [SerializeField] private FpvCamera fpvCamera;
        [SerializeField] private ViewModeManager views;
        [SerializeField] private KeyboardGamepadSource keyboardGamepad;
        [SerializeField] private RcRadioSource radio;
        [SerializeField] private RadioDeviceProvider radioProvider;

        [Header("Track editor")]
        [SerializeField] private Ushqyr.TrackEditor.TrackEditorController editor;

        [Header("Garage builds")]
        [Tooltip("Materials for a garage build's generated model (replaces the stock quad visual).")]
        [SerializeField] private Ushqyr.Garage.QuadModelMaterials buildMaterials = new Ushqyr.Garage.QuadModelMaterials();

        [Header("Feed and audio")]
        [SerializeField] private QuadAudio quadAudio;
        [SerializeField] private VtxSignal vtx;
        [SerializeField] private GameObject debugOverlay;
        [Tooltip("Race HUD / OSD canvas, hidden while editing a track.")]
        [SerializeField] private GameObject hud;

        private string _appliedQuadId;
        private bool _testFlying;

        public LoadedTrack Track { get; private set; }
        public bool Editing => editor != null && editor.isActiveAndEnabled && !_testFlying;

        private void Start()
        {
            GameFlow.Catalog = tracks;
            HudFlags.HideFreeFlightInfo = false;
            var settings = SaveSystem.Settings;
            ApplySettings(settings);
            SaveSystem.SettingsChanged += ApplySettings;
            if (radioProvider != null) radioProvider.Changed += OnRadioChanged;
            if (views != null) views.Changed += OnViewChanged;
            if (race != null)
            {
                race.GatePassed += OnGatePassed;
                race.PhaseChanged += OnPhaseChanged;
            }

            var edit = GameFlow.TakePendingEdit();
            if (edit != null && editor != null)
            {
                editor.TestFlyRequested += TestFly;
                editor.ExitRequested += GameFlow.Menu;
                EnterEditor(edit);
                return;
            }
            if (editor != null) editor.gameObject.SetActive(false);
            // An activity of the Education Suite (an exercise from the training hub) takes the scene over.
            _activity = Ushqyr.Edu.EduActivityRunner.TakePending();
            if (_activity != null)
            {
                StartActivity(settings);
                return;
            }
            LoadTrack(GameFlow.TakeTrackToLoad());
            if (views != null) views.Apply((ViewMode)settings.Camera.ViewMode);
            if (Ushqyr.Net.NetRace.InRace && Track != null && PrepareStrike(settings))
                gameObject.AddComponent<OnlineStrikeDriver>().Configure(this, vehicle, Strike, buildMaterials, QuadSize(settings));
            else if (Ushqyr.Net.NetRace.InRace && Track != null) gameObject.AddComponent<OnlineRaceDriver>().Configure(this, vehicle, race, buildMaterials);
            // A short showdown of the track before the pilot's view (not for online races: the host starts those).
            else if (Track != null && SetupStrike(settings)) { }
            else if (Track != null)
            {
                SetupBots(settings);
                var hudText = hud != null ? hud.GetComponentInChildren<TMPro.TMP_Text>(true) : null;
                if (settings.Race.TrackIntro) TrackIntro.Play(gameObject, views, vehicle, race, Track, hud, hudText != null ? hudText.font : null);
            }
        }

        private void OnDestroy()
        {
            SaveSystem.SettingsChanged -= ApplySettings;
            if (radioProvider != null) radioProvider.Changed -= OnRadioChanged;
            if (views != null) views.Changed -= OnViewChanged;
            if (race != null)
            {
                race.GatePassed -= OnGatePassed;
                race.PhaseChanged -= OnPhaseChanged;
            }
            SceneSwitcher.MenuOverride = null;
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }

        private void OnRadioChanged() => ApplySettings(SaveSystem.Settings);

        // ---------- feed / audio ----------

        private void OnViewChanged(ViewMode mode)
        {
            // Motor sound is "in your ears" in FPV, and comes from the quad in the chase and line-of-sight views.
            if (quadAudio != null) quadAudio.SetSpatial(mode != ViewMode.Fpv);
        }

        private void OnGatePassed(int gate, PassResult result)
        {
            if (quadAudio != null && (result == PassResult.LapCompleted || result == PassResult.RaceFinished)) quadAudio.Beep();
        }

        private void OnPhaseChanged(RacePhase phase)
        {
            if (quadAudio != null && phase == RacePhase.Racing) quadAudio.Beep();
            // Opt-in (Settings > Pilot): the race's best lap goes to the track's online leaderboard.
            if (phase == RacePhase.Finished && SaveSystem.Settings.Online.PostLapTimes && race.Timer.HasBestLap
                && Ushqyr.Net.OnlineLeaderboards.Has(race.TrackId))
                _ = Ushqyr.Net.OnlineLeaderboards.PostLapAsync(race.TrackId, (float)race.Timer.BestLap, vehicle.Profile.Name);
        }

        // ---------- editor / test fly ----------

        private void EnterEditor(TrackData data)
        {
            _testFlying = false;
            SceneSwitcher.MenuOverride = null;
            UnloadTrack();
            Time.timeScale = 1f;
            race.gameObject.SetActive(false);
            vehicle.gameObject.SetActive(false);
            // The editor flies the output camera itself.
            if (views != null) views.SetFreeCamera(true);
            if (hud != null) hud.SetActive(false);
            editor.Begin(data);
            Debug.Log($"[Ushqyr] Editing track '{data.Name}' ({data.Environment})");
        }

        private void TestFly(TrackData data)
        {
            editor.End();
            editor.gameObject.SetActive(false);
            _testFlying = true;
            vehicle.gameObject.SetActive(true);
            race.gameObject.SetActive(true);
            LoadData(data);
            if (views != null)
            {
                views.SetFreeCamera(false);
                views.Apply((ViewMode)SaveSystem.Settings.Camera.ViewMode);
            }
            if (hud != null) hud.SetActive(true);
            // While test-flying, the menu button goes back to the editor instead of the main menu.
            SceneSwitcher.MenuOverride = () =>
            {
                EnterEditor(data);
                return true;
            };
            Debug.Log($"[Ushqyr] Test-flying '{data.Name}' (menu button returns to the editor)");
        }

        // ---------- P4: bots ----------

        /// <summary>Race mode bots for an offline race (none in free flight, online or the track editor).</summary>
        public Ushqyr.AI.BotRace Bots { get; private set; }

        private void SetupBots(GameSettings s)
        {
            if (Track == null || Track.Data.FreeFlight || !race.enabled || s.Race.BotCount <= 0)
            {
                if (Bots != null) Bots.Clear();
                return;
            }
            if (Bots == null) Bots = new GameObject("Bots").AddComponent<Ushqyr.AI.BotRace>();
            var size = QuadSize(s);
            var build = Ushqyr.AI.BotRoster.ReferenceBuild(size);
            var profile = Ushqyr.Garage.BuildComposer.Compose(build);
            int mask = vehicle.BaseCollision.TrackGeometry;
            var line = Ushqyr.AI.RacingLineAsset.Get(Track.Data.Id, (int)size, Track.Route, Track.Data.Route, Track.Gates,
                Track.Spawn.position, profile, mask, out bool rebaked);
            if (rebaked) Debug.Log($"[Ushqyr][Bots] Racing line for '{Track.Data.Id}' baked at load (no up-to-date cached line)");
            var options = new Ushqyr.AI.BotRaceOptions
            {
                Count = Mathf.Clamp(s.Race.BotCount, 0, 7),
                Level = (Ushqyr.AI.BotLevel)Mathf.Clamp(s.Race.BotLevel, 0, 5),
                Collisions = s.Race.BotCollisions,
                PlayerPace = RecentLap(),
                Seed = Random.Range(0, 1000),
            };
            Bots.Configure(race, vehicle, Track, line, options, size, buildMaterials, s.PilotName, 0);
            var replay = GetComponent<WinnerReplay>();
            if (replay == null) replay = gameObject.AddComponent<WinnerReplay>();
            replay.Configure(Bots, views, buildMaterials);
            Debug.Log($"[Ushqyr][Bots] {options.Count} {options.Level} bots, collisions {(options.Collisions ? "on" : "off")}, line {line.Length:F0} m / {line.LapTime:F1} s");
        }

        /// <summary>The P5 Strike Range round, when this scene has a <see cref="Ushqyr.Strike.StrikeSession"/>.</summary>
        public Ushqyr.Strike.StrikeSession Strike { get; private set; }

        /// <summary>A Strike Range scene: light and HUD ready, the round not started yet (offline or online).</summary>
        private bool PrepareStrike(GameSettings s)
        {
            Strike = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
            HudFlags.HideFreeFlightInfo = Strike != null && Strike.Layout != null;
            if (!HudFlags.HideFreeFlightInfo) return false;
            var weather = (Ushqyr.Strike.RangeWeather)s.Strike.Weather;
            Ushqyr.Strike.RangeLighting.Requested = weather;
            var lighting = FindAnyObjectByType<Ushqyr.Strike.RangeLighting>();
            if (lighting != null) lighting.Apply(weather);
            var hudText = hud != null ? hud.GetComponentInChildren<TMPro.TMP_Text>(true) : null;
            var cam = views != null ? views.OutputCamera : null;
            if (cam == null) cam = Camera.main;
            var strikeHud = Strike.GetComponent<Ushqyr.Strike.StrikeHud>();
            if (strikeHud == null) strikeHud = Strike.gameObject.AddComponent<Ushqyr.Strike.StrikeHud>();
            strikeHud.Configure(Strike, vehicle.transform, cam, hudText != null ? hudText.font : null);
            return true;
        }

        private bool SetupStrike(GameSettings s)
        {
            if (!PrepareStrike(s)) return false;
            var options = new Ushqyr.Strike.StrikeOptions
            {
                Difficulty = (Ushqyr.Strike.RangeDifficulty)s.Strike.Difficulty,
                Bots = s.Strike.Bots,
                BotLevel = (Ushqyr.AI.BotLevel)s.Strike.BotLevel,
                PilotName = s.PilotName,
            };
            Strike.Begin(vehicle, options, buildMaterials, QuadSize(s));
            Debug.Log($"[Ushqyr][Strike] Round: {options.Difficulty}, {options.Bots} {options.BotLevel} bots, {(Ushqyr.Strike.RangeWeather)s.Strike.Weather}");
            return true;
        }

        private static Ushqyr.Garage.SizeClass QuadSize(GameSettings s)
        {
            var build = Ushqyr.Garage.BuildLibrary.IsBuildId(s.QuadId) ? Ushqyr.Garage.BuildLibrary.Default.Resolve(s.QuadId) : null;
            if (build != null) return build.Size;
            return s.QuadId == "whoop65" ? Ushqyr.Garage.SizeClass.Whoop : Ushqyr.Garage.SizeClass.Five;
        }

        /// <summary>The player's recent lap time on this track (mean of the last races), or 0.</summary>
        private float RecentLap()
        {
            var board = race.Board;
            if (board == null || board.History.Count == 0 || Track == null) return 0f;
            int n = Mathf.Min(5, board.History.Count);
            float sum = 0f;
            for (int i = board.History.Count - n; i < board.History.Count; i++) sum += board.History[i];
            return sum / n / Mathf.Max(1, Track.Data.Laps);
        }

        private void UnloadTrack()
        {
            if (Track != null && Track.Root != null) Destroy(Track.Root.gameObject);
            Track = null;
        }

        // ---------- racing ----------

        private void LoadTrack(string trackId)
        {
            var data = GameFlow.Resolve(tracks, trackId);
            if (data == null || data.Environment != environment) data = tracks.Load(tracks.FirstFor(environment));
            if (data == null)
            {
                Debug.LogError($"[Ushqyr] No track available for environment '{environment}'");
                return;
            }
            LoadData(data);
        }

        private void LoadData(TrackData data)
        {
            UnloadTrack();
            var errors = data.Validate(trackPrefabs.GateTypes(), trackPrefabs.PropTypes());
            foreach (var e in errors) Debug.LogWarning($"[Ushqyr] Track '{data.Id}': {e}");

            Track = TrackLoader.Load(data, trackPrefabs);
            vehicle.SpawnPoint = Track.Spawn;
            var losStand = views != null ? views.LosStand : null;
            if (losStand != null)
            {
                // Stand 4 m left of and 6 m behind the pad, facing down the start straight.
                var s = Track.Spawn;
                Vector3 pos = s.position + s.rotation * new Vector3(-4f, 0f, -6f);
                losStand.SetPositionAndRotation(new Vector3(pos.x, 0f, pos.z), Quaternion.LookRotation(s.rotation * new Vector3(4f, 0f, 14f)));
            }
            race.SetCourse(Track.Gates, Track.Spawn, data.Id, data.Laps, data.Name);
            var beacon = race.GetComponent<NextGateBeacon>();
            if (beacon != null)
            {
                beacon.Show = data.Route.AirCourse;
                // P3: holo chevrons along the flight line (the route's 3D frame).
                var line = Track.Route;
                if (data.Route.AirCourse && line != null)
                    beacon.SetRoute(p => line.ClosestDistance(p), d =>
                    {
                        var f = line.Frame(d);
                        return (f.Position, Quaternion.LookRotation(f.Forward, f.Up));
                    }, line.Length);
                else beacon.ClearRoute();
            }
            // E7: open venues have a flyable area with a soft boundary; free-flight tracks have no race at all.
            var area = GetComponent<FlightArea>();
            if (area == null) area = gameObject.AddComponent<FlightArea>();
            area.Configure(vehicle, data, Track.Spawn, environment.StartsWith("expo"));
            race.enabled = !data.FreeFlight;
            ApplyGuidance(SaveSystem.Settings);
            Debug.Log($"[Ushqyr] Track '{data.Name}' loaded: {Track.Gates.Length} gates, {data.Props.Count} props, {data.Laps} laps");
        }

        private Ushqyr.Edu.ActivityDefinition _activity;

        /// <summary>
        /// The scene with nothing in it but what the activity brings: no race, the activity's drone and view, and the
        /// runner that shows the briefing, records, grades and shows the result.
        /// </summary>
        private void StartActivity(GameSettings settings)
        {
            // The place's own start point, with no gates: the first track of this place lends its spawn.
            var first = tracks.Load(tracks.FirstFor(environment));
            var data = new TrackData { Id = "edu-" + _activity.Id, Name = _activity.Title, Environment = environment, FreeFlight = true, Laps = 1 };
            if (first != null) data.Spawn = first.Spawn;
            LoadData(data);
            HudFlags.HideFreeFlightInfo = true;
            var area = GetComponent<FlightArea>();
            if (area != null) area.enabled = false; // no spawn switching or reset keys in the middle of an attempt
            ApplySettings(settings);
            string view = _activity.Setup.View;
            if (views != null) views.Apply(view == "fpv" ? ViewMode.Fpv : view == "los" ? ViewMode.LineOfSight : view == "chase" ? ViewMode.Chase : (ViewMode)settings.Camera.ViewMode);
            Ushqyr.Edu.EduActivityRunner.Begin(vehicle, _activity, GameFlow.Menu);
        }

        /// <summary>The pilot's settings with what the activity fixes: its drone, and the assist it asks for.</summary>
        private GameSettings ForActivity(GameSettings s)
        {
            if (_activity == null || string.IsNullOrEmpty(_activity.Setup.Drone)) return s;
            GameSettings mine = s.Clone();
            mine.QuadId = _activity.Setup.Drone;
            return mine;
        }

        /// <summary>Applies everything from the settings to the scene. Safe to call any time.</summary>
        public void ApplySettings(GameSettings s)
        {
            s = ForActivity(s);
            ApplyQuad(s);

            if (fpvCamera != null)
            {
                fpvCamera.TiltDegrees = s.Camera.TiltDegrees;
                fpvCamera.HorizontalFov = s.Camera.HorizontalFov;
            }
            if (views != null) views.LensDistortion = s.Camera.LensDistortion;

            var shaping = new StickShaping
            {
                Deadband = s.Input.Deadband, Expo = s.Input.Expo, InvertPitch = s.Input.InvertPitch, InvertRoll = s.Input.InvertRoll,
            };
            if (radioProvider != null) radioProvider.Backend = (RadioBackend)s.Input.RadioBackend;
            if (radio != null) radio.Shaping = shaping;
            if (keyboardGamepad != null)
            {
                keyboardGamepad.Shaping = shaping;
                // A radio in Xbox mode is a gamepad with a non-centring throttle stick.
                bool radioPad = radioProvider != null && radioProvider.GamepadPreset != null;
                keyboardGamepad.GamepadThrottle = radioPad ? ThrottleMapping.Direct : (ThrottleMapping)s.Input.GamepadThrottle;
            }

            if (quadAudio != null) quadAudio.Volume = s.Feed.MotorVolume;
            if (vtx != null) vtx.Level = (VtxNoiseLevel)s.Feed.VtxNoise;
            if (debugOverlay != null) debugOverlay.SetActive(s.Feed.ShowDebugOverlay);
            var venue = FindAnyObjectByType<StadiumLighting>();
            if (venue != null)
            {
                var time = (TimeOfDay)(venue.Venue == "expo" ? s.Feed.ExpoLighting : s.Feed.StadiumLighting);
                if (venue.Current != time) venue.Apply(time);
            }
            if (vehicle != null) vehicle.SetBatterySag(s.Physics.BatterySag);
            if (views != null) OnViewChanged(views.Mode);

            if (race != null) race.PilotName = s.PilotName;
            ApplyGuidance(s);
        }

        /// <summary>Route guidance (gate highlights, racing line, HUD arrow) and ghost lanes for the loaded track.</summary>
        private void ApplyGuidance(GameSettings s)
        {
            var level = (RouteGuidanceLevel)s.Guidance.RouteGuidance;
            if (race != null) race.Guidance = level;
            if (Track == null) return;
            if (Track.RacingLine != null) Track.RacingLine.SetActive(level == RouteGuidanceLevel.Trainer);
            if (Track.LaneColliders != null) Track.LaneColliders.SetActive(!s.Guidance.GhostLanes);
        }

        private void ApplyQuad(GameSettings s)
        {
            // A garage build flies its composed profile and its own model.
            var build = Ushqyr.Garage.BuildLibrary.IsBuildId(s.QuadId) ? Ushqyr.Garage.BuildLibrary.Default.Resolve(s.QuadId) : null;
            if (build != null && Ushqyr.Garage.BuildComposer.Validate(build).Count > 0) build = null;
            var entry = build != null ? quads.Find(build.Size == Ushqyr.Garage.SizeClass.Whoop ? "whoop65" : "race5") : quads.Find(s.QuadId);
            if (entry == null || entry.Profile == null || vehicle == null) return;
            string quadKey = build != null ? s.QuadId + "|" + build.ToJson().GetHashCode() : entry.Id;
            // The Education Suite's drones (camera, delivery, education) bring their own model.
            bool edu = build == null && Ushqyr.Garage.EduDroneModel.TryKind(entry.Id, out _eduKind);

            QuadProfile p = build != null ? Ushqyr.Garage.BuildComposer.Compose(build) : entry.Profile.Profile;
            if (s.UseCustomRates) p.Rates = s.Rates;
            p.Aero.LinearDrag = p.Aero.LinearDrag * s.Physics.DragScale;
            p.Aero.QuadraticDrag = p.Aero.QuadraticDrag * s.Physics.DragScale;
            p.Motors.TimeConstantUp *= s.Physics.MotorLagScale;
            p.Motors.TimeConstantDown *= s.Physics.MotorLagScale;

            var collision = vehicle.BaseCollision;
            collision.Radius = build != null ? build.Frame.WheelbaseMm * 0.0005f + build.Prop.DiameterIn * 0.0254f * 0.25f : entry.CollisionRadius;
            if (edu && _appliedQuadId != quadKey) ApplyEduVisual(_eduKind);
            if (edu && _eduModel != null) collision.Radius = _eduModel.Radius;
            collision.CrashSpeed = s.Physics.CrashSpeed;
            vehicle.Configure(p, collision);

            float visualScale = entry.VisualScale;
            if (build != null && _appliedQuadId != quadKey) ApplyBuildVisual(build);
            else if (build == null && !edu && _buildModel != null) RestoreStockVisual();
            if (build != null) visualScale = build.Frame.WheelbaseMm / 220f;
            if (quadVisual != null && build == null && !edu) quadVisual.localScale = Vector3.one * entry.VisualScale;
            if (edu && _eduModel != null) visualScale = _eduModel.Radius / 0.16f;
            if (ghostVisual != null) ghostVisual.localScale = Vector3.one * visualScale;
            if (fpvCamera != null)
                fpvCamera.transform.localPosition = build != null
                    ? new Vector3(0f, 0.004f, build.Frame.BodyLengthMm * 0.0005f + 0.005f) // front of the stack (model is centred on it)
                    : edu && _eduModel != null && _eduModel.Lens != null ? quadVisual.InverseTransformPoint(_eduModel.Lens.position)
                    : entry.CameraOffset;
            // Flight assist for the person at the sticks, the recording of the last flight and its replay.
            // While an activity runs, the assist and the flight mode are the activity's, not the pilot's setting.
            if (_activity == null) Ushqyr.Edu.EduFreeFlight.Attach(vehicle, s.Input.FlightAssist, edu);
            else if (Ushqyr.Edu.EduActivityRunner.Current != null) Ushqyr.Edu.EduActivityRunner.Current.ApplyFlightSetup();
            // Equipment of the drone classes: a stabilised camera on the camera drones, a box on the delivery drone.
            bool gimbal = edu && _eduModel != null && _eduModel.Gimbal != null && fpvCamera != null;
            if (gimbal) Ushqyr.Edu.EduGimbal.Attach(vehicle, fpvCamera, _eduModel.Gimbal);
            else Ushqyr.Edu.EduGimbal.Detach(vehicle);
            bool payload = edu && _eduModel != null && _eduModel.Payload != null;
            if (payload) Ushqyr.Edu.EduPayload.Attach(vehicle, _eduModel.Payload, 1f);
            else Ushqyr.Edu.EduPayload.Detach(vehicle);
            if (views != null) views.SetQuadScale(visualScale);
            if (quadAudio != null) quadAudio.MaxRpm = build != null ? Ushqyr.Garage.BuildComposer.FullThrottle(build.Motor, build.Prop, build.Battery).RevPerSec * 60f : entry.MotorRpm;

            // A different quad means a different ghost and a fresh start.
            if (_appliedQuadId != null && _appliedQuadId != quadKey && race != null)
            {
                race.ReloadGhost();
                race.Restart();
            }
            _appliedQuadId = quadKey;
        }

        private GameObject _buildModel;
        private Ushqyr.Garage.EduDroneKind _eduKind;
        private Ushqyr.Garage.EduDroneModel.Result _eduModel;

        /// <summary>Hides the stock quad model and shows one of the Education Suite's drones in its place.</summary>
        private void ApplyEduVisual(Ushqyr.Garage.EduDroneKind kind)
        {
            if (quadVisual == null || buildMaterials == null || buildMaterials.Plastic == null) return;
            if (_buildModel != null) Destroy(_buildModel);
            quadVisual.localScale = Vector3.one;
            foreach (var r in quadVisual.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            _eduModel = Ushqyr.Garage.EduDroneModel.Build(kind, buildMaterials, quadVisual, quadVisual.gameObject.layer);
            _buildModel = _eduModel.Root;
            var spinner = vehicle != null ? vehicle.GetComponentInChildren<Ushqyr.Vehicle.PropVisuals>(true) : null;
            if (spinner == null) spinner = FindAnyObjectByType<Ushqyr.Vehicle.PropVisuals>();
            if (spinner != null)
            {
                _stockProps ??= spinner.Props;
                _stockDiscs ??= spinner.BlurDiscs;
                spinner.Configure(_eduModel.Props, null);
            }
        }
        private Transform[] _stockProps;
        private Renderer[] _stockDiscs;

        private void RestoreStockVisual()
        {
            Destroy(_buildModel);
            _buildModel = null;
            _eduModel = null;
            foreach (var r in quadVisual.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            var spinner = FindAnyObjectByType<Ushqyr.Vehicle.PropVisuals>();
            if (spinner != null && _stockProps != null) spinner.Configure(_stockProps, _stockDiscs);
        }

        /// <summary>Hides the stock quad model and shows the build's generated one (props spin through PropVisuals).</summary>
        private void ApplyBuildVisual(Ushqyr.Garage.QuadBuild build)
        {
            if (quadVisual == null || buildMaterials == null || buildMaterials.Carbon == null) return;
            if (_buildModel != null) Destroy(_buildModel);
            quadVisual.localScale = Vector3.one;
            foreach (var r in quadVisual.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var model = Ushqyr.Garage.QuadModelBuilder.Build(build, buildMaterials, quadVisual, quadVisual.gameObject.layer);
            // The generated model's origin is the bottom plate; centre the stack on the body.
            model.Root.transform.localPosition = Vector3.down * (build.Frame.PlateMm * 0.001f + build.Frame.StackMm * 0.0005f);
            foreach (var t in model.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = quadVisual.gameObject.layer;
            _buildModel = model.Root;
            var spinner = vehicle != null ? vehicle.GetComponentInChildren<Ushqyr.Vehicle.PropVisuals>(true) : null;
            if (spinner == null) spinner = FindAnyObjectByType<Ushqyr.Vehicle.PropVisuals>();
            if (spinner != null)
            {
                _stockProps ??= spinner.Props;
                _stockDiscs ??= spinner.BlurDiscs;
                spinner.Configure(model.Props, model.BlurDiscs);
            }
        }
    }
}
