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
        [SerializeField] private GogglesScreen gogglesScreen;
        [SerializeField] private ComfortVignette vignette;
        [SerializeField] private KeyboardGamepadSource keyboardGamepad;
        [SerializeField] private TouchControllerSource touch;
        [SerializeField] private RcRadioSource radio;
        [SerializeField] private RadioDeviceProvider radioProvider;
        [Tooltip("Moved next to the track's spawn so the line-of-sight pilot stands by the pad.")]
        [SerializeField] private Transform losStand;

        [Header("Track editor")]
        [SerializeField] private GateRush.TrackEditor.TrackEditorController editor;

        private string _appliedQuadId;
        private bool _testFlying;

        public LoadedTrack Track { get; private set; }
        public bool Editing => editor != null && editor.isActiveAndEnabled && !_testFlying;

        private void Start()
        {
            var settings = SaveSystem.Settings;
            ApplySettings(settings);
            SaveSystem.SettingsChanged += ApplySettings;
            if (radioProvider != null) radioProvider.Changed += OnRadioChanged;

            var edit = GameFlow.TakePendingEdit();
            if (edit != null && editor != null)
            {
                editor.TestFlyRequested += TestFly;
                editor.ExitRequested += GameFlow.Menu;
                EnterEditor(edit);
                return;
            }
            if (editor != null) editor.gameObject.SetActive(false);
            LoadTrack(settings.TrackId);
            if (views != null) views.Apply((ViewMode)settings.Camera.ViewMode);
        }

        private void OnDestroy()
        {
            SaveSystem.SettingsChanged -= ApplySettings;
            if (radioProvider != null) radioProvider.Changed -= OnRadioChanged;
            SceneSwitcher.MenuOverride = null;
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }

        private void OnRadioChanged() => ApplySettings(SaveSystem.Settings);

        // ---------- editor / test fly ----------

        private void EnterEditor(TrackData data)
        {
            _testFlying = false;
            SceneSwitcher.MenuOverride = null;
            UnloadTrack();
            Time.timeScale = 1f;
            race.gameObject.SetActive(false);
            vehicle.gameObject.SetActive(false);
            if (views != null)
            {
                // Stand in the world (line of sight) and let the editor move the rig.
                views.Apply(ViewMode.LineOfSight);
                views.enabled = false;
            }
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
                views.enabled = true;
                views.Apply((ViewMode)SaveSystem.Settings.Camera.ViewMode);
            }
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
            if (losStand != null)
            {
                // Stand 4 m left of and 6 m behind the pad, facing down the start straight.
                var s = Track.Spawn;
                Vector3 pos = s.position + s.rotation * new Vector3(-4f, 0f, -6f);
                losStand.SetPositionAndRotation(new Vector3(pos.x, 0f, pos.z), Quaternion.LookRotation(s.rotation * new Vector3(4f, 0f, 14f)));
            }
            race.SetCourse(Track.Gates, Track.Spawn, data.Id, data.Laps, data.Name);
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
            if (gogglesScreen != null)
            {
                gogglesScreen.ArcDegrees = s.Camera.GogglesArcDegrees;
                gogglesScreen.FollowMode = (ScreenFollowMode)s.Camera.GogglesFollow;
            }
            if (vignette != null) vignette.Strength = s.Comfort.VignetteStrength;

            var shaping = new StickShaping { Deadband = s.Input.Deadband, Expo = s.Input.Expo };
            if (radioProvider != null) radioProvider.Backend = (RadioBackend)s.Input.RadioBackend;
            if (radio != null) radio.Shaping = shaping;
            if (keyboardGamepad != null)
            {
                keyboardGamepad.Shaping = shaping;
                // A radio in Xbox mode is a gamepad with a non-centring throttle stick.
                bool radioPad = radioProvider != null && radioProvider.GamepadPreset != null;
                keyboardGamepad.GamepadThrottle = radioPad ? ThrottleMapping.Direct : (ThrottleMapping)s.Input.GamepadThrottle;
            }
            if (touch != null)
            {
                touch.Shaping = shaping;
                touch.ThrottleMapping = (ThrottleMapping)s.Input.TouchThrottle;
            }

            var display = FindAnyObjectByType<XrDisplayConfig>();
            if (display != null && display.TargetRefreshRate != s.Comfort.RefreshRate) display.SetRefreshRate(s.Comfort.RefreshRate);

            if (race != null) race.PilotName = s.PilotName;
        }

        private void ApplyQuad(GameSettings s)
        {
            var entry = quads.Find(s.QuadId);
            if (entry == null || entry.Profile == null || vehicle == null) return;

            QuadProfile p = entry.Profile.Profile;
            if (s.UseCustomRates) p.Rates = s.Rates;
            p.Aero.LinearDrag = p.Aero.LinearDrag * s.Physics.DragScale;
            p.Aero.QuadraticDrag = p.Aero.QuadraticDrag * s.Physics.DragScale;
            p.Motors.TimeConstantUp *= s.Physics.MotorLagScale;
            p.Motors.TimeConstantDown *= s.Physics.MotorLagScale;

            var collision = vehicle.BaseCollision;
            collision.Radius = entry.CollisionRadius;
            collision.CrashSpeed = s.Physics.CrashSpeed;
            vehicle.Configure(p, collision);

            if (quadVisual != null) quadVisual.localScale = Vector3.one * entry.VisualScale;
            if (ghostVisual != null) ghostVisual.localScale = Vector3.one * entry.VisualScale;
            if (fpvCamera != null) fpvCamera.transform.localPosition = entry.CameraOffset;

            // A different quad means a different ghost and a fresh start.
            if (_appliedQuadId != null && _appliedQuadId != entry.Id && race != null)
            {
                race.ReloadGhost();
                race.Restart();
            }
            _appliedQuadId = entry.Id;
        }
    }
}
