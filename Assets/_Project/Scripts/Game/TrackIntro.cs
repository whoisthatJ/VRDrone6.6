using System;
using GateRush.Cameras;
using GateRush.Core;
using GateRush.Race;
using GateRush.Track;
using GateRush.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GateRush.Game
{
    /// <summary>
    /// Track showdown before a race: a ~8 s cutscene from three angles, then the pilot's own view.
    /// <list type="number">
    /// <item>a high, slow orbit over the whole course;</item>
    /// <item>a low run along the flight line through a gate in the middle of the lap;</item>
    /// <item>a close orbit round the quad on the start pad, ending behind it.</item>
    /// </list>
    /// Letterbox bars and the track name on screen; short fades between the shots. While it plays the quad is held on
    /// the pad and the race can't be armed. Any key or gamepad button skips it.
    /// </summary>
    public sealed class TrackIntro : MonoBehaviour
    {
        public const float ShotSeconds = 2.8f;
        public const int Shots = 3;
        public const float FadeSeconds = 0.18f;

        /// <summary>Off for automated runs (benchmarks, autopilot tests, online automation).</summary>
        public static bool Disabled { get; set; }

        private ViewModeManager _views;
        private QuadVehicle _vehicle;
        private RaceSession _race;
        private LoadedTrack _track;
        private GameObject _hud;
        private Action _done;
        private float _t;
        private bool _raceWasEnabled, _hudWasActive;
        private Canvas _canvas;
        private Image _fade;
        private TMP_Text _title;
        private Vector3 _centre;
        private float _radius;
        private Func<float, (Vector3 pos, Vector3 fwd)> _run;
        private float _runLength;

        /// <summary>True while the cutscene is on screen.</summary>
        public bool Playing { get; private set; }

        /// <summary>Plays the intro for a freshly loaded track; <paramref name="done"/> runs when it ends (or is skipped).</summary>
        public static TrackIntro Play(GameObject host, ViewModeManager views, QuadVehicle vehicle, RaceSession race, LoadedTrack track,
            GameObject hud, TMP_FontAsset font, Action done = null)
        {
            if (Disabled || Application.isBatchMode || views == null || views.OutputCamera == null || track == null)
            {
                done?.Invoke();
                return null;
            }
            var intro = host.GetComponent<TrackIntro>();
            if (intro == null) intro = host.AddComponent<TrackIntro>();
            intro.Begin(views, vehicle, race, track, hud, font, done);
            return intro;
        }

        private void Begin(ViewModeManager views, QuadVehicle vehicle, RaceSession race, LoadedTrack track, GameObject hud, TMP_FontAsset font, Action done)
        {
            if (Playing) Finish();
            _views = views;
            _vehicle = vehicle;
            _race = race;
            _track = track;
            _hud = hud;
            _done = done;
            _t = 0f;
            Frame();
            BuildOverlay(font, track.Data != null ? L.T(track.Data.Name) : "");

            _raceWasEnabled = race != null && race.enabled;
            if (race != null) race.enabled = false;
            _hudWasActive = hud != null && hud.activeSelf;
            if (hud != null) hud.SetActive(false);
            if (vehicle != null) vehicle.HoldAtSpawn = true;
            views.SetFreeCamera(true);
            Playing = true;
            Place(0f);
        }

        // The course's extent, and a stretch of flight line through a gate in the middle of the lap for shot 2.
        private void Frame()
        {
            var b = new Bounds(_track.Spawn.position, Vector3.one);
            foreach (var g in _track.Gates)
                if (g != null) b.Encapsulate(g.AimPointWorld);
            var route = _track.Route;
            if (route != null)
                for (float d = 0f; d < route.Length; d += Mathf.Max(5f, route.Length / 64f)) b.Encapsulate(route.Sample(d).Position);
            _centre = b.center;
            _radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 20f, 600f);

            var gates = _track.Gates;
            Gate gate = gates.Length > 0 ? gates[Mathf.Clamp(gates.Length / 2, 0, gates.Length - 1)] : null;
            if (route != null && gate != null)
            {
                float at = route.ClosestDistance(gate.AimPointWorld);
                _runLength = 30f;
                bool air = _track.Data != null && _track.Data.Route.AirCourse;
                _run = u =>
                {
                    float d = route.Wrap(at - 24f + u * _runLength);
                    var p = air ? route.Frame(d) : route.Sample(d);
                    return (p.Position + p.Up * 0.6f, p.Forward);
                };
            }
            else if (gate != null)
            {
                Vector3 c = gate.AimPointWorld, f = gate.transform.forward;
                _run = u => (c - f * (24f - u * 30f) + Vector3.up * 0.4f, f);
            }
            else _run = null;
        }

        private void Update()
        {
            if (!Playing) return;
            _t += Time.unscaledDeltaTime;
            if (_t >= ShotSeconds * Shots || SkipPressed())
            {
                Finish();
                return;
            }
            Place(_t);
        }

        private static bool SkipPressed()
        {
            var k = Keyboard.current;
            if (k != null && k.anyKey.wasPressedThisFrame) return true;
            var m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame);
        }

        private void Place(float t)
        {
            int shot = Mathf.Min(Shots - 1, (int)(t / ShotSeconds));
            float u = Mathf.Clamp01((t - shot * ShotSeconds) / ShotSeconds);
            float e = u * u * (3f - 2f * u);
            Vector3 pos, look;
            var cam = _views.OutputCamera.transform;
            Transform spawn = _track.Spawn;
            Vector3 quad = _vehicle != null ? _vehicle.transform.position : spawn.position;

            switch (shot)
            {
                case 0:
                {
                    // High orbit over the whole course.
                    float yaw = spawn.eulerAngles.y + 200f + 30f * e;
                    Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    pos = _centre - dir * (_radius * 1.05f) + Vector3.up * (_radius * 0.55f + 12f);
                    look = _centre;
                    break;
                }
                case 1 when _run != null:
                {
                    // Down the flight line through a gate.
                    var (p, f) = _run(e);
                    pos = p;
                    look = p + f * 10f;
                    break;
                }
                default:
                {
                    // Round the quad on the pad, ending behind it.
                    float a = Mathf.Lerp(150f, 180f + 10f, e);
                    float dist = Mathf.Lerp(1.6f, 0.9f, e);
                    Vector3 off = spawn.rotation * (Quaternion.Euler(0f, a, 0f) * Vector3.forward) * dist;
                    pos = quad + off + Vector3.up * Mathf.Lerp(0.7f, 0.3f, e);
                    look = quad + Vector3.up * 0.05f + spawn.forward * Mathf.Lerp(0f, 1.2f, e);
                    break;
                }
            }
            cam.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));

            // Fade through black at each cut and at the end; the title in the first two shots.
            float edge = Mathf.Min(t - shot * ShotSeconds, (shot + 1) * ShotSeconds - t);
            float fade = 1f - Mathf.Clamp01(edge / FadeSeconds);
            if (shot == 0 && t < FadeSeconds) fade = 1f - t / FadeSeconds;
            if (_fade != null) _fade.color = new Color(0f, 0f, 0f, fade);
            if (_title != null) _title.alpha = Mathf.Clamp01(Mathf.Min(t / 0.4f, (ShotSeconds * 2f - t) / 0.4f));
        }

        private void Finish()
        {
            if (!Playing) return;
            Playing = false;
            if (_views != null) _views.SetFreeCamera(false);
            if (_vehicle != null) _vehicle.HoldAtSpawn = false;
            if (_race != null && _raceWasEnabled) _race.enabled = true;
            if (_hud != null && _hudWasActive) _hud.SetActive(true);
            if (_canvas != null) Destroy(_canvas.gameObject);
            var done = _done;
            _done = null;
            done?.Invoke();
        }

        private void OnDisable() => Finish();

        // ---------- overlay ----------

        private void BuildOverlay(TMP_FontAsset font, string title)
        {
            var go = new GameObject("Track Intro Overlay");
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            Bar(go.transform, 0f);
            Bar(go.transform, 1f);
            _fade = Rect(go.transform, "Fade", Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            _fade.color = Color.black;
            _fade.raycastTarget = false;

            var tr = Rect(go.transform, "Title", new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.115f));
            _title = tr.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) _title.font = font;
            _title.text = title;
            _title.fontSize = 46f;
            _title.characterSpacing = 6f;
            _title.alignment = TextAlignmentOptions.Center;
            _title.color = Color.white;
            _title.alpha = 0f;
            _title.raycastTarget = false;
        }

        private static void Bar(Transform parent, float y)
        {
            var r = Rect(parent, y > 0.5f ? "Bar Top" : "Bar Bottom", new Vector2(0f, y > 0.5f ? 0.88f : 0f), new Vector2(1f, y > 0.5f ? 1f : 0.12f));
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }
    }
}
