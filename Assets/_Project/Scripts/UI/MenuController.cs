using System.Globalization;
using GateRush.Core;
using GateRush.Flight;
using GateRush.Game;
using GateRush.Race;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GateRush.UI
{
    /// <summary>
    /// World-space main menu built at runtime: fly, choose quad, choose track, settings (rates, camera, input,
    /// comfort, physics, pilot), input diagnostics. Edits <see cref="SaveSystem.Settings"/> and commits on leaving a page.
    /// </summary>
    public sealed partial class MenuController : MonoBehaviour
    {
        private const float Width = 1600f;
        private const float Height = 1000f;
        private const float Margin = 60f;
        private const float ContentWidth = Width - 2f * Margin;

        private static readonly string[] Callsigns =
            { "Pilot", "Ace", "Falcon", "Viper", "Comet", "Nova", "Raptor", "Ghost", "Blaze", "Orbit", "Spark", "Drift" };

        [SerializeField] private RectTransform root;
        [SerializeField] private QuadCatalog quads;
        [SerializeField] private TrackCatalog tracks;

        private RectTransform _page;
        private TMP_Text _summary;
        private string _settingsTab = "Rates";
        private bool _dirty;

        private GameSettings S => SaveSystem.Settings;

        private void Start()
        {
            UiKit.Image(UiKit.Fill(root, "Panel"), UiKit.PanelColor);
            ShowMain();
        }

        // ---------- pages ----------

        private RectTransform NewPage(string title)
        {
            if (_dirty)
            {
                SaveSystem.Commit();
                _dirty = false;
            }
            if (_page != null) Destroy(_page.gameObject);
            ClearRadioPage();
            _page = UiKit.Fill(root, "Page " + title);
            UiKit.Label(_page, title, new Vector2(Margin, -30f), new Vector2(ContentWidth, 80f), 56f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            return _page;
        }

        private void BackButton(RectTransform page, UnityEngine.Events.UnityAction back = null) =>
            UiKit.Button(page, "Back", new Vector2(Margin, -(Height - 110f)), new Vector2(240f, 70f), back ?? ShowMain);

        private void ShowMain()
        {
            var p = NewPage("GATERUSH");
            _summary = UiKit.Label(p, "", new Vector2(Margin, -120f), new Vector2(ContentWidth, 120f), 32f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
            UpdateSummary();

            float y = -260f;
            UiKit.Button(p, "FLY", new Vector2(Margin, y), new Vector2(ContentWidth, 110f), Fly, 52f);
            y -= 140f;
            float half = (ContentWidth - 30f) * 0.5f;
            UiKit.Button(p, "Choose quad", new Vector2(Margin, y), new Vector2(half, 90f), () =>
            {
                CommitIfDirty();
                GameFlow.Garage();
            });
            UiKit.Button(p, "Choose track", new Vector2(Margin + half + 30f, y), new Vector2(half, 90f), ShowTracks);
            y -= 120f;
            UiKit.Button(p, "Settings", new Vector2(Margin, y), new Vector2(half, 90f), ShowSettings);
            UiKit.Button(p, "Radio setup", new Vector2(Margin + half + 30f, y), new Vector2(half, 90f), ShowRadio);
            y -= 120f;
            UiKit.Button(p, "Track editor", new Vector2(Margin, y), new Vector2(half, 90f), ShowEditorHome);
            UiKit.Button(p, "Input diagnostics", new Vector2(Margin + half + 30f, y), new Vector2(half, 90f), () => { CommitIfDirty(); GameFlow.Diagnostics(); });
            y -= 120f;
            UiKit.Button(p, "Quit", new Vector2(Margin, y), new Vector2(half, 90f), GameFlow.Quit);

            UiKit.Label(p, "Mouse, keyboard or gamepad. In flight: V switches the camera, Esc returns here.",
                new Vector2(Margin, -(Height - 100f)), new Vector2(ContentWidth, 60f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
        }

        private void UpdateSummary()
        {
            if (_summary == null) return;
            var quad = quads.Find(S.QuadId);
            var track = GameFlow.Resolve(tracks, S.TrackId);
            string trackName = track != null ? track.Name : tracks.Tracks.Count > 0 ? tracks.Tracks[0].DisplayName : "-";
            _summary.text = $"Pilot  <color=#fff>{S.PilotName}</color>\n" +
                            $"Quad   <color=#fff>{(quad != null ? quad.DisplayName : "-")}</color>\n" +
                            $"Track  <color=#fff>{trackName}</color>";
        }

        private void Fly()
        {
            CommitIfDirty();
            string id = GameFlow.Resolve(tracks, S.TrackId) != null ? S.TrackId : tracks.Tracks[0].Id;
            GameFlow.Race(tracks, id);
        }

        private void CommitIfDirty()
        {
            if (!_dirty) return;
            SaveSystem.Commit();
            _dirty = false;
        }

        private void ShowQuads()
        {
            var p = NewPage("Choose quad");
            float y = -140f;
            foreach (var q in quads.Quads)
            {
                var entry = q;
                var b = UiKit.Button(p, "", new Vector2(Margin, y), new Vector2(ContentWidth, 170f), () =>
                {
                    S.QuadId = entry.Id;
                    _dirty = true;
                    ShowQuads();
                });
                var label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.fontSize = 30f;
                label.text = $"<size=40><b>{entry.DisplayName}</b></size>\n<color=#bbb>{entry.Description}</color>";
                if (entry.Id == S.QuadId) UiKit.SetButtonColor(b, UiKit.SelectedColor);
                y -= 190f;
            }
            BackButton(p);
        }

        private const int TracksPerPage = 5;
        private int _trackPage;

        private void ShowTracks()
        {
            var p = NewPage("Choose track");
            var board = new LocalLeaderboard();
            board.Load(LocalLeaderboard.DefaultPath);

            // Built-in tracks first, then your own (newest first).
            var rows = new System.Collections.Generic.List<(string id, string name, string info)>();
            foreach (var t in tracks.Tracks) rows.Add((t.Id, t.DisplayName, t.Description));
            foreach (var u in GateRush.Track.TrackLibrary.Default.LoadAll())
                rows.Add((u.Id, u.Name, $"Your track: {EnvironmentName(u.Environment)}, {u.Gates.Count} gates, {u.Laps} laps"));

            int pages = Mathf.Max(1, (rows.Count + TracksPerPage - 1) / TracksPerPage);
            _trackPage = Mathf.Clamp(_trackPage, 0, pages - 1);
            float y = -130f;
            for (int i = _trackPage * TracksPerPage; i < rows.Count && i < (_trackPage + 1) * TracksPerPage; i++)
            {
                var row = rows[i];
                var tb = board.Board(row.id);
                string record = tb.BestLap > 0f
                    ? string.Format(CultureInfo.InvariantCulture, "Lap record {0:F2}s ({1})", tb.BestLap, tb.BestLapPilot)
                    : "No lap record yet";
                var b = UiKit.Button(p, "", new Vector2(Margin, y), new Vector2(ContentWidth - 250f, 125f), () =>
                {
                    S.TrackId = row.id;
                    _dirty = true;
                    ShowTracks();
                });
                var label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.fontSize = 26f;
                label.text = $"<size=36><b>{row.name}</b></size>   <color=#ffb347>{record}</color>\n<color=#bbb>{row.info}</color>";
                if (row.id == S.TrackId) UiKit.SetButtonColor(b, UiKit.SelectedColor);
                UiKit.Button(p, "Fly", new Vector2(Margin + ContentWidth - 230f, y), new Vector2(230f, 125f), () =>
                {
                    CommitIfDirty();
                    GameFlow.Race(tracks, row.id);
                }, 38f);
                y -= 140f;
            }

            if (pages > 1)
            {
                float py = -(Height - 110f);
                UiKit.Button(p, "<", new Vector2(Width - Margin - 420f, py), new Vector2(90f, 70f), () => { _trackPage = (_trackPage - 1 + pages) % pages; ShowTracks(); });
                UiKit.Label(p, $"{_trackPage + 1}/{pages}", new Vector2(Width - Margin - 320f, py), new Vector2(120f, 70f), 30f, TextAlignmentOptions.Center);
                UiKit.Button(p, ">", new Vector2(Width - Margin - 190f, py), new Vector2(90f, 70f), () => { _trackPage = (_trackPage + 1) % pages; ShowTracks(); });
            }
            BackButton(p);
        }

        // ---------- settings ----------

        private static readonly string[] Tabs = { "Rates", "Camera", "Input", "Graphics", "Feed", "Physics", "Pilot" };

        private void ShowSettings()
        {
            var p = NewPage("Settings");
            float tabWidth = (ContentWidth - (Tabs.Length - 1) * 12f) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                string tab = Tabs[i];
                var b = UiKit.Button(p, tab, new Vector2(Margin + i * (tabWidth + 12f), -120f), new Vector2(tabWidth, 70f), () =>
                {
                    _settingsTab = tab;
                    ShowSettings();
                }, 30f);
                if (tab == _settingsTab) UiKit.SetButtonColor(b, UiKit.SelectedColor);
            }

            var content = UiKit.Rect(p, "Content", new Vector2(Margin, -220f), new Vector2(ContentWidth, 640f));
            switch (_settingsTab)
            {
                case "Rates": BuildRates(content); break;
                case "Camera": BuildCamera(content); break;
                case "Input": BuildInput(content); break;
                case "Graphics": BuildGraphics(content); break;
                case "Feed": BuildFeed(content); break;
                case "Physics": BuildPhysics(content); break;
                default: BuildPilot(content); break;
            }

            BackButton(p);
            UiKit.Button(p, "Reset all to defaults", new Vector2(Width - Margin - 420f, -(Height - 110f)), new Vector2(420f, 70f), () =>
            {
                var fresh = new GameSettings { QuadId = S.QuadId, TrackId = S.TrackId, PilotName = S.PilotName, Graphics = S.Graphics };
                SaveSystem.Replace(fresh);
                ShowSettings();
            }, 28f);
        }

        private void Changed() => _dirty = true;

        private static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

        private void BuildRates(RectTransform c)
        {
            var quad = quads.Find(S.QuadId);
            RateProfile quadRates = quad != null && quad.Profile != null ? quad.Profile.Profile.Rates : RateProfile.BetaflightDefault;

            UiKit.StepperRow(c, "Rates", 0f, new[] { "Quad default", "Custom" }, S.UseCustomRates ? 1 : 0, i =>
            {
                S.UseCustomRates = i == 1;
                if (S.UseCustomRates) S.Rates = quadRates;
                Changed();
                ShowSettings();
            }, ContentWidth);
            if (!S.UseCustomRates)
            {
                UiKit.Label(c, $"Flying {(quad != null ? quad.DisplayName : "the quad")}'s own rates:\n{Describe(quadRates)}",
                    new Vector2(0f, -100f), new Vector2(ContentWidth, 200f), 30f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
                return;
            }

            UiKit.StepperRow(c, "Rates type", -75f, new[] { "Betaflight", "Actual" }, (int)S.Rates.Type, i =>
            {
                S.Rates = i == 1 ? RateProfile.ActualDefault : RateProfile.BetaflightDefault;
                Changed();
                ShowSettings();
            }, ContentWidth);

            var summary = UiKit.Label(c, Describe(S.Rates), new Vector2(0f, -150f), new Vector2(ContentWidth, 50f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            float y = -210f;
            AxisSliders(c, "Roll", y, () => S.Rates.Roll, a => { var r = S.Rates; r.Roll = a; S.Rates = r; }, summary);
            AxisSliders(c, "Pitch", y - 140f, () => S.Rates.Pitch, a => { var r = S.Rates; r.Pitch = a; S.Rates = r; }, summary);
            AxisSliders(c, "Yaw", y - 280f, () => S.Rates.Yaw, a => { var r = S.Rates; r.Yaw = a; S.Rates = r; }, summary);
        }

        /// <summary>Three compact sliders for one axis on two rows.</summary>
        private void AxisSliders(RectTransform c, string axis, float y, System.Func<AxisRates> get, System.Action<AxisRates> set, TMP_Text summary)
        {
            bool actual = S.Rates.Type == RatesType.Actual;
            var a = get();
            float w = ContentWidth * 0.5f - 20f;
            var left = UiKit.Rect(c, axis + " A", new Vector2(0f, y), new Vector2(w, 60f));
            var right = UiKit.Rect(c, axis + " B", new Vector2(w + 40f, y), new Vector2(w, 60f));
            var below = UiKit.Rect(c, axis + " C", new Vector2(0f, y - 65f), new Vector2(w, 60f));

            void Push(System.Func<AxisRates, AxisRates> edit)
            {
                set(edit(get()));
                Changed();
                summary.text = Describe(S.Rates);
            }

            if (actual)
            {
                UiKit.SliderRow(left, axis + " centre", 0f, 10f, 400f, a.RcRate, v => F(v, "0"), v => Push(x => { x.RcRate = v; return x; }), w, true);
                UiKit.SliderRow(right, axis + " max", 0f, 100f, 1998f, a.Rate, v => F(v, "0"), v => Push(x => { x.Rate = v; return x; }), w, true);
            }
            else
            {
                UiKit.SliderRow(left, axis + " RC rate", 0f, 0.01f, 2.55f, a.RcRate, v => F(v, "0.00"), v => Push(x => { x.RcRate = v; return x; }), w);
                UiKit.SliderRow(right, axis + " super", 0f, 0f, 0.99f, a.Rate, v => F(v, "0.00"), v => Push(x => { x.Rate = v; return x; }), w);
            }
            UiKit.SliderRow(below, axis + " expo", 0f, 0f, 1f, a.Expo, v => F(v, "0.00"), v => Push(x => { x.Expo = v; return x; }), w);
        }

        private static string Describe(RateProfile r)
        {
            float roll = Rates.DegPerSec(r.Type, r.Roll, 1f);
            float pitch = Rates.DegPerSec(r.Type, r.Pitch, 1f);
            float yaw = Rates.DegPerSec(r.Type, r.Yaw, 1f);
            return string.Format(CultureInfo.InvariantCulture, "{0} rates, max roll {1:0} / pitch {2:0} / yaw {3:0} deg/s", r.Type, roll, pitch, yaw);
        }

        private void BuildCamera(RectTransform c)
        {
            var cam = S.Camera;
            UiKit.SliderRow(c, "Camera tilt", 0f, 0f, 60f, cam.TiltDegrees, v => F(v, "0") + " deg", v => { cam.TiltDegrees = v; Changed(); }, ContentWidth, true);
            UiKit.SliderRow(c, "FOV (horizontal)", -90f, 90f, 150f, cam.HorizontalFov, v => F(v, "0") + " deg", v => { cam.HorizontalFov = v; Changed(); }, ContentWidth, true);
            UiKit.StepperRow(c, "View", -180f, new[] { "FPV (drone camera)", "Chase", "Line of sight" }, cam.ViewMode, i => { cam.ViewMode = i; Changed(); }, ContentWidth);
            UiKit.StepperRow(c, "Lens distortion (FPV)", -270f, new[] { "Off", "On" }, cam.LensDistortion ? 1 : 0, i => { cam.LensDistortion = i == 1; Changed(); }, ContentWidth);
            UiKit.Label(c, "Switch views in flight with V, gamepad LB or the radio's view switch (Radio setup).",
                new Vector2(0f, -370f), new Vector2(ContentWidth, 60f), 26f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
        }

        private void BuildInput(RectTransform c)
        {
            var input = S.Input;
            string[] throttle = { "Full stick (radio-style)", "Centre = hover", "Trigger" };
            UiKit.StepperRow(c, "Gamepad throttle", 0f, throttle, input.GamepadThrottle, i => { input.GamepadThrottle = i; Changed(); }, ContentWidth);
            UiKit.SliderRow(c, "Stick deadband", -90f, 0f, 0.3f, input.Deadband, v => F(v * 100f, "0") + " %", v => { input.Deadband = v; Changed(); }, ContentWidth);
            UiKit.SliderRow(c, "Stick expo", -180f, 0f, 1f, input.Expo, v => F(v, "0.00"), v => { input.Expo = v; Changed(); }, ContentWidth);
            UiKit.StepperRow(c, "Invert pitch (right stick)", -270f, new[] { "Off", "On" }, input.InvertPitch ? 1 : 0, i => { input.InvertPitch = i == 1; Changed(); }, ContentWidth);
            UiKit.StepperRow(c, "Invert roll (right stick)", -360f, new[] { "Off", "On" }, input.InvertRoll ? 1 : 0, i => { input.InvertRoll = i == 1; Changed(); }, ContentWidth);
        }

        private void BuildGraphics(RectTransform c)
        {
            var g = S.Graphics;
            if (string.IsNullOrEmpty(g.Preset)) g.Preset = GraphicsQuality.Current != null ? GraphicsQuality.Current.Preset : "High";
            int preset = Mathf.Max(0, System.Array.IndexOf(GraphicsQuality.Presets, g.Preset));
            UiKit.StepperRow(c, "Quality preset", 0f, GraphicsQuality.Presets, preset, i =>
            {
                S.Graphics = GraphicsPrefs.ForPreset(GraphicsQuality.Presets[i]);
                ApplyGraphics();
                ShowSettings();
            }, ContentWidth);
            UiKit.SliderRow(c, "Render scale", -75f, 50f, 100f, g.RenderScale * 100f, v => F(v, "0") + " %", v => { g.RenderScale = v / 100f; ApplyGraphics(); }, ContentWidth, true);
            UiKit.StepperRow(c, "Anti-aliasing", -150f, new[] { "Off", "SMAA", "MSAA 2x", "MSAA 4x" }, g.AntiAliasing, i => { g.AntiAliasing = i; ApplyGraphics(); }, ContentWidth);
            UiKit.StepperRow(c, "Shadows", -225f, new[] { "Off", "Low", "Medium", "High" }, g.Shadows, i => { g.Shadows = i; ApplyGraphics(); }, ContentWidth);
            UiKit.StepperRow(c, "Ambient occlusion", -300f, new[] { "Off", "On" }, g.Ssao ? 1 : 0, i => { g.Ssao = i == 1; ApplyGraphics(); }, ContentWidth);
            UiKit.SliderRow(c, "Grass density", -375f, 0f, 100f, g.Vegetation * 100f, v => F(v, "0") + " %", v => { g.Vegetation = v / 100f; ApplyGraphics(); }, ContentWidth, true);
            UiKit.StepperRow(c, "Post-processing", -450f, new[] { "Off", "On" }, g.PostProcessing ? 1 : 0, i => { g.PostProcessing = i == 1; ApplyGraphics(); }, ContentWidth);
            // Frame limit: VSync, or VSync off with an optional cap (GraphicsPrefs.FrameCaps).
            var caps = GraphicsPrefs.FrameCaps;
            var limits = new string[caps.Length + 1];
            limits[0] = "VSync";
            for (int k = 0; k < caps.Length; k++) limits[k + 1] = caps[k] == 0 ? "Uncapped" : caps[k] + " fps";
            int limit = g.VSync ? 0 : 1 + Mathf.Max(0, System.Array.IndexOf(caps, g.FrameCap));
            UiKit.StepperRow(c, "Frame limit", -525f, limits, limit, i =>
            {
                g.VSync = i == 0;
                g.FrameCap = i == 0 ? 0 : caps[i - 1];
                ApplyGraphics();
            }, ContentWidth);
        }

        private void ApplyGraphics()
        {
            GraphicsQuality.Apply(S.Graphics);
            Changed();
        }

        private void BuildFeed(RectTransform c)
        {
            var feed = S.Feed;
            UiKit.SliderRow(c, "Motor volume", 0f, 0f, 1f, feed.MotorVolume, v => F(v * 100f, "0") + " %", v => { feed.MotorVolume = v; Changed(); }, ContentWidth);
            UiKit.StepperRow(c, "Debug readout", -90f, new[] { "Off", "On" }, feed.ShowDebugOverlay ? 1 : 0, i => { feed.ShowDebugOverlay = i == 1; Changed(); }, ContentWidth);
            UiKit.StepperRow(c, "Stadium lighting", -180f, new[] { "Night (floodlights)", "Dusk", "Day" }, feed.StadiumLighting, i => { feed.StadiumLighting = i; Changed(); }, ContentWidth);
            var guide = S.Guidance;
            UiKit.StepperRow(c, "Route guidance", -270f, new[] { "Off", "Standard", "Trainer" }, guide.RouteGuidance, i => { guide.RouteGuidance = i; Changed(); }, ContentWidth);
            UiKit.StepperRow(c, "Ghost lanes", -360f, new[] { "Off", "On" }, guide.GhostLanes ? 1 : 0, i => { guide.GhostLanes = i == 1; Changed(); }, ContentWidth);
            UiKit.Label(c, "Trainer adds a racing line (green: fast, yellow: brake, red: slow) and an arrow to the next gate. Ghost lanes: fly through the lane posts.",
                new Vector2(0f, -450f), new Vector2(ContentWidth, 60f), 26f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
        }

        private void BuildPhysics(RectTransform c)
        {
            var ph = S.Physics;
            UiKit.SliderRow(c, "Crash speed", 0f, 3f, 20f, ph.CrashSpeed, v => F(v, "0.0") + " m/s", v => { ph.CrashSpeed = v; Changed(); }, ContentWidth);
            UiKit.SliderRow(c, "Air drag", -90f, 0.5f, 2f, ph.DragScale, v => "x" + F(v, "0.00"), v => { ph.DragScale = v; Changed(); }, ContentWidth);
            UiKit.SliderRow(c, "Motor lag", -180f, 0.5f, 2f, ph.MotorLagScale, v => "x" + F(v, "0.00"), v => { ph.MotorLagScale = v; Changed(); }, ContentWidth);
            UiKit.StepperRow(c, "Battery sag", -270f, new[] { "Off", "On" }, ph.BatterySag ? 1 : 0, i => { ph.BatterySag = i == 1; Changed(); }, ContentWidth);
            UiKit.Label(c, "Lower drag = floatier and faster. Higher motor lag = softer, slower throttle response.",
                new Vector2(0f, -370f), new Vector2(ContentWidth, 60f), 26f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
        }

        private void BuildPilot(RectTransform c)
        {
            int index = System.Array.IndexOf(Callsigns, S.PilotName);
            if (index < 0) index = 0;
            UiKit.StepperRow(c, "Callsign", 0f, Callsigns, index, i => { S.PilotName = Callsigns[i]; Changed(); }, ContentWidth);
            UiKit.Label(c, "Shown on the leaderboard. (Free text entry comes with a VR keyboard later.)",
                new Vector2(0f, -100f), new Vector2(ContentWidth, 60f), 26f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
        }

        private void OnDestroy()
        {
            if (_dirty) SaveSystem.Commit();
        }
    }
}
