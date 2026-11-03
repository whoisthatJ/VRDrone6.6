using GateRush.Cameras;
using GateRush.Core;
using GateRush.Flight;
using GateRush.Input;
using GateRush.Race;
using GateRush.Track;
using GateRush.Vehicle;
using UnityEngine;

namespace GateRush.Game
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
        [SerializeField] private GateRush.TrackEditor.TrackEditorController editor;

        [Header("Garage builds")]
        [Tooltip("Materials for a garage build's generated model (replaces the stock quad visual).")]
        [SerializeField] private GateRush.Garage.QuadModelMaterials buildMaterials = new GateRush.Garage.QuadModelMaterials();

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
            LoadTrack(GameFlow.TakeTrackToLoad());
            if (views != null) views.Apply((ViewMode)settings.Camera.ViewMode);
            if (GateRush.Net.NetRace.InRace && Track != null) gameObject.AddComponent<OnlineRaceDriver>().Configure(this, vehicle, race, buildMaterials);
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
                && GateRush.Net.OnlineLeaderboards.Has(race.TrackId))
                _ = GateRush.Net.OnlineLeaderboards.PostLapAsync(race.TrackId, (float)race.Timer.BestLap, vehicle.Profile.Name);
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
            Debug.Log($"[GateRush] Editing track '{data.Name}' ({data.Environment})");
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
            Debug.Log($"[GateRush] Test-flying '{data.Name}' (menu button returns to the editor)");
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
                Debug.LogError($"[GateRush] No track available for environment '{environment}'");
                return;
            }
            LoadData(data);
        }

        private void LoadData(TrackData data)
        {
            UnloadTrack();
            var errors = data.Validate(trackPrefabs.GateTypes(), trackPrefabs.PropTypes());
            foreach (var e in errors) Debug.LogWarning($"[GateRush] Track '{data.Id}': {e}");

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
            ApplyGuidance(SaveSystem.Settings);
            Debug.Log($"[GateRush] Track '{data.Name}' loaded: {Track.Gates.Length} gates, {data.Props.Count} props, {data.Laps} laps");
        }

        /// <summary>Applies everything from the settings to the scene. Safe to call any time.</summary>
        public void ApplySettings(GameSettings s)
        {
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
            var stadium = FindAnyObjectByType<StadiumLighting>();
            if (stadium != null && stadium.Current != (TimeOfDay)s.Feed.StadiumLighting) stadium.Apply((TimeOfDay)s.Feed.StadiumLighting);
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
            var build = GateRush.Garage.BuildLibrary.IsBuildId(s.QuadId) ? GateRush.Garage.BuildLibrary.Default.Resolve(s.QuadId) : null;
            if (build != null && GateRush.Garage.BuildComposer.Validate(build).Count > 0) build = null;
            var entry = build != null ? quads.Find(build.Size == GateRush.Garage.SizeClass.Whoop ? "whoop65" : "race5") : quads.Find(s.QuadId);
            if (entry == null || entry.Profile == null || vehicle == null) return;
            string quadKey = build != null ? s.QuadId + "|" + build.ToJson().GetHashCode() : entry.Id;

            QuadProfile p = build != null ? GateRush.Garage.BuildComposer.Compose(build) : entry.Profile.Profile;
            if (s.UseCustomRates) p.Rates = s.Rates;
            p.Aero.LinearDrag = p.Aero.LinearDrag * s.Physics.DragScale;
            p.Aero.QuadraticDrag = p.Aero.QuadraticDrag * s.Physics.DragScale;
            p.Motors.TimeConstantUp *= s.Physics.MotorLagScale;
            p.Motors.TimeConstantDown *= s.Physics.MotorLagScale;

            var collision = vehicle.BaseCollision;
            collision.Radius = build != null ? build.Frame.WheelbaseMm * 0.0005f + build.Prop.DiameterIn * 0.0254f * 0.25f : entry.CollisionRadius;
            collision.CrashSpeed = s.Physics.CrashSpeed;
            vehicle.Configure(p, collision);

            float visualScale = entry.VisualScale;
            if (build != null && _appliedQuadId != quadKey) ApplyBuildVisual(build);
            else if (build == null && _buildModel != null) RestoreStockVisual();
            if (build != null) visualScale = build.Frame.WheelbaseMm / 220f;
            if (quadVisual != null && build == null) quadVisual.localScale = Vector3.one * entry.VisualScale;
            if (ghostVisual != null) ghostVisual.localScale = Vector3.one * visualScale;
            if (fpvCamera != null)
                fpvCamera.transform.localPosition = build != null
                    ? new Vector3(0f, 0.004f, build.Frame.BodyLengthMm * 0.0005f + 0.005f) // front of the stack (model is centred on it)
                    : entry.CameraOffset;
            if (views != null) views.SetQuadScale(visualScale);
            if (quadAudio != null) quadAudio.MaxRpm = build != null ? GateRush.Garage.BuildComposer.FullThrottle(build.Motor, build.Prop, build.Battery).RevPerSec * 60f : entry.MotorRpm;

            // A different quad means a different ghost and a fresh start.
            if (_appliedQuadId != null && _appliedQuadId != quadKey && race != null)
            {
                race.ReloadGhost();
                race.Restart();
            }
            _appliedQuadId = quadKey;
        }

        private GameObject _buildModel;
        private Transform[] _stockProps;
        private Renderer[] _stockDiscs;

        private void RestoreStockVisual()
        {
            Destroy(_buildModel);
            _buildModel = null;
            foreach (var r in quadVisual.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            var spinner = FindAnyObjectByType<GateRush.Vehicle.PropVisuals>();
            if (spinner != null && _stockProps != null) spinner.Configure(_stockProps, _stockDiscs);
        }

        /// <summary>Hides the stock quad model and shows the build's generated one (props spin through PropVisuals).</summary>
        private void ApplyBuildVisual(GateRush.Garage.QuadBuild build)
        {
            if (quadVisual == null || buildMaterials == null || buildMaterials.Carbon == null) return;
            if (_buildModel != null) Destroy(_buildModel);
            quadVisual.localScale = Vector3.one;
            foreach (var r in quadVisual.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var model = GateRush.Garage.QuadModelBuilder.Build(build, buildMaterials, quadVisual, quadVisual.gameObject.layer);
            // The generated model's origin is the bottom plate; centre the stack on the body.
            model.Root.transform.localPosition = Vector3.down * (build.Frame.PlateMm * 0.001f + build.Frame.StackMm * 0.0005f);
            foreach (var t in model.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = quadVisual.gameObject.layer;
            _buildModel = model.Root;
            var spinner = vehicle != null ? vehicle.GetComponentInChildren<GateRush.Vehicle.PropVisuals>(true) : null;
            if (spinner == null) spinner = FindAnyObjectByType<GateRush.Vehicle.PropVisuals>();
            if (spinner != null)
            {
                _stockProps ??= spinner.Props;
                _stockDiscs ??= spinner.BlurDiscs;
                spinner.Configure(model.Props, null);
            }
        }
    }
}
