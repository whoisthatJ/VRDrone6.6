using System.Collections;
using Ushqyr.Core;
using Ushqyr.Game;
using Ushqyr.Race;
using Ushqyr.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Ushqyr.UI
{
    /// <summary>
    /// The finish moment (T6): crossing the finish → 0.5 s of slow motion → the quad auto-hovers in front of the finish
    /// cube and the view cuts to a cinematic camera inside the cube, looking out through the hexagon (slight depth of
    /// field) → the results screen: race time, personal best / rank, crashes and resets, top speed, how the run ranks in
    /// your history, lap bars (fastest green, slowest red), distance, and Restart / Exit / Next.
    /// Tracks without a finish cube get the slow motion and the results screen without the camera cut. Offline races
    /// only (online races keep their own standings screen).
    /// </summary>
    public sealed class FinishSequence : MonoBehaviour
    {
        [SerializeField] private RaceSession session;
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private float slowMotionScale = 0.3f;
        [SerializeField] private float slowMotionSeconds = 0.5f;
        [SerializeField] private float focusDistance = 4.9f;

        private static readonly Color Good = new Color(0.3f, 0.95f, 0.45f);
        private static readonly Color Bad = new Color(1f, 0.33f, 0.28f);

        private RectTransform _panel;
        private Camera _cinematic, _hidden;
        private Volume _dof;
        private Coroutine _run;

        /// <summary>True while the results screen is up (the race HUD hides its own centre message).</summary>
        public bool ResultsShown => _panel != null && _panel.gameObject.activeSelf;

        public void Configure(RaceSession race, QuadVehicle quad)
        {
            session = race;
            vehicle = quad;
        }

        private void OnEnable()
        {
            if (session != null) session.PhaseChanged += OnPhaseChanged;
        }

        private void OnDisable()
        {
            if (session != null) session.PhaseChanged -= OnPhaseChanged;
            EndCinematic();
        }

        private void OnPhaseChanged(RacePhase phase)
        {
            if (phase == RacePhase.Finished && !session.ExternalStart)
            {
                if (_run != null) StopCoroutine(_run);
                _run = StartCoroutine(Run());
                return;
            }
            if (_run != null) StopCoroutine(_run);
            _run = null;
            EndCinematic();
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        private IEnumerator Run()
        {
            Time.timeScale = slowMotionScale;
            yield return new WaitForSecondsRealtime(slowMotionSeconds);
            Time.timeScale = 1f;

            var cube = session.FinishGate != null ? session.FinishGate.GetComponentInParent<FinishCube>() : null;
            if (cube != null && cube.HoverAnchor != null && cube.CameraAnchor != null)
            {
                vehicle.SpawnPoint = cube.HoverAnchor;
                vehicle.HoldAtSpawn = true;
                StartCinematic(cube.CameraAnchor);
            }
            ShowResults();
            _run = null;
        }

        private void Update()
        {
            if (!ResultsShown) return;
            var kb = Keyboard.current;
            if ((kb != null && kb.nKey.wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame)) Next();
        }

        // ---------- camera ----------

        private void StartCinematic(Transform anchor)
        {
            if (_cinematic == null)
            {
                var go = new GameObject("Finish Cinematic Camera");
                go.transform.SetParent(transform, false);
                _cinematic = go.AddComponent<Camera>();
                _cinematic.fieldOfView = 40f;
                _cinematic.nearClipPlane = 0.05f;
                _cinematic.farClipPlane = 6000f;
                _cinematic.depth = 50f;
                go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

                var vol = new GameObject("Finish Depth Of Field");
                vol.transform.SetParent(transform, false);
                _dof = vol.AddComponent<Volume>();
                _dof.isGlobal = true;
                _dof.priority = 100f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                var dof = profile.Add<DepthOfField>(true);
                dof.mode.Override(DepthOfFieldMode.Bokeh);
                dof.focusDistance.Override(focusDistance);
                dof.focalLength.Override(55f);
                dof.aperture.Override(4f);
                _dof.sharedProfile = profile;
            }
            var main = Camera.main;
            int mask = main != null ? main.cullingMask : ~0;
            int quadLayer = LayerMask.NameToLayer("LocalQuad");
            if (quadLayer >= 0) mask |= 1 << quadLayer;
            _cinematic.cullingMask = mask;
            if (main != null)
            {
                _cinematic.clearFlags = main.clearFlags;
                _cinematic.backgroundColor = main.backgroundColor;
                _hidden = main;
                main.enabled = false;
            }
            _cinematic.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            _cinematic.gameObject.SetActive(true);
            _dof.gameObject.SetActive(true);
        }

        private void EndCinematic()
        {
            Time.timeScale = 1f;
            if (_cinematic != null) _cinematic.gameObject.SetActive(false);
            if (_dof != null) _dof.gameObject.SetActive(false);
            if (_hidden != null) _hidden.enabled = true;
            _hidden = null;
        }

        // ---------- results ----------

        private void ShowResults()
        {
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<InputSystemUIInputModule>();
            }
            if (_panel != null) Destroy(_panel.gameObject);

            const float W = 760f, H = 900f;
            _panel = UiKit.Rect(transform, "Finish Results", new Vector2(-W - 40f, -130f), new Vector2(W, H));
            _panel.anchorMin = _panel.anchorMax = new Vector2(1f, 1f);
            UiKit.Image(_panel, new Color(0.04f, 0.05f, 0.07f, 0.94f), true);

            var t = session.Timer;
            float total = (float)t.TotalTime(0);
            float y = -30f;
            UiKit.Label(_panel, L.T(session.TrackName).ToUpperInvariant(), new Vector2(40f, y), new Vector2(W - 80f, 40f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            y -= 44f;
            UiKit.Label(_panel, Clock(total), new Vector2(40f, y), new Vector2(W - 80f, 100f), 84f, TextAlignmentOptions.MidlineLeft).fontStyle = FontStyles.Bold;
            y -= 104f;

            if (session.LastRank == 1)
            {
                UiKit.Label(_panel, L.T("NEW PERSONAL BEST"), new Vector2(40f, y), new Vector2(W - 80f, 44f), 32f, TextAlignmentOptions.MidlineLeft, Good).fontStyle = FontStyles.Bold;
                y -= 46f;
            }
            string rank = session.LastRank > 0 ? L.F("Leaderboard #{0}", session.LastRank) : L.T("Not in the top 10");
            UiKit.Label(_panel, rank, new Vector2(40f, y), new Vector2(W - 80f, 40f), 28f, TextAlignmentOptions.MidlineLeft);
            y -= 46f;
            if (session.LastRaceSetTrackBestLap)
            {
                UiKit.Label(_panel, L.T("New track lap record!"), new Vector2(40f, y), new Vector2(W - 80f, 36f), 26f, TextAlignmentOptions.MidlineLeft, Good);
                y -= 40f;
            }
            y -= 16f;

            // Stat tiles: two columns.
            string history = session.LastPercentile < 0f ? L.T("first race here") : L.F("faster than {0:0}% of your races", session.LastPercentile);
            var stats = new (string label, string value)[]
            {
                (L.T("Crashes"), session.Crashes.ToString()),
                (L.T("Resets"), session.Resets.ToString()),
                (L.T("Top speed"), L.F("{0:0} km/h", session.TopSpeed * 3.6f)),
                (L.T("Distance"), L.F("{0:0} m", session.Distance)),
            };
            float tileW = (W - 80f - 20f) / 2f;
            for (int i = 0; i < stats.Length; i++)
            {
                var tile = UiKit.Rect(_panel, "Stat " + i, new Vector2(40f + (i % 2) * (tileW + 20f), y - (i / 2) * 104f), new Vector2(tileW, 92f));
                UiKit.Image(tile, new Color(1f, 1f, 1f, 0.06f));
                UiKit.Label(tile, stats[i].label, new Vector2(20f, -8f), new Vector2(tileW - 40f, 32f), 22f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                UiKit.Label(tile, stats[i].value, new Vector2(20f, -40f), new Vector2(tileW - 40f, 46f), 36f, TextAlignmentOptions.MidlineLeft).fontStyle = FontStyles.Bold;
            }
            y -= 2 * 104f + 6f;
            UiKit.Label(_panel, history, new Vector2(40f, y), new Vector2(W - 80f, 36f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            y -= 54f;

            // Lap bars: length ∝ lap time, fastest green, slowest red.
            int laps = t.CompletedLaps;
            float max = 0f;
            int slowest = -1;
            for (int i = 0; i < laps; i++)
                if ((float)t.LapTime(i) > max) { max = (float)t.LapTime(i); slowest = i; }
            float barMax = W - 80f - 120f - 140f;
            for (int i = 0; i < laps; i++)
            {
                float lap = (float)t.LapTime(i);
                UiKit.Label(_panel, L.F("Lap {0}", i + 1), new Vector2(40f, y), new Vector2(110f, 40f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                var bar = UiKit.Rect(_panel, "Lap Bar " + i, new Vector2(160f, y - 8f), new Vector2(Mathf.Max(8f, barMax * lap / Mathf.Max(0.01f, max)), 24f));
                Color c = laps > 1 && i == t.BestLapIndex ? Good : laps > 1 && i == slowest ? Bad : new Color(0.55f, 0.6f, 0.68f);
                UiKit.Image(bar, c);
                UiKit.Label(_panel, Clock(lap), new Vector2(W - 40f - 130f, y), new Vector2(130f, 40f), 26f, TextAlignmentOptions.MidlineRight);
                y -= 46f;
            }

            float by = -H + 40f + 74f;
            float bw = (W - 80f - 40f) / 3f;
            UiKit.Button(_panel, L.T("Restart (T)"), new Vector2(40f, by), new Vector2(bw, 74f), session.Restart, 28f);
            UiKit.Button(_panel, L.T("Exit (Esc)"), new Vector2(40f + bw + 20f, by), new Vector2(bw, 74f), GameFlow.Menu, 28f);
            var next = UiKit.Button(_panel, L.T("Next (N)"), new Vector2(40f + 2f * (bw + 20f), by), new Vector2(bw, 74f), Next, 28f);
            UiKit.SetButtonColor(next, new Color(0.11f, 0.38f, 0.2f));
            EventSystem.current.SetSelectedGameObject(next.gameObject);
        }

        /// <summary>The next track in the catalogue after this one (wraps round); restarts this one if it is the only one.</summary>
        private void Next()
        {
            var catalog = GameFlow.Catalog;
            if (catalog == null || catalog.Tracks.Count < 2)
            {
                session.Restart();
                return;
            }
            int i = catalog.Tracks.FindIndex(e => e.Id == session.TrackId);
            GameFlow.Race(catalog, catalog.Tracks[(i + 1) % catalog.Tracks.Count].Id);
        }

        private static string Clock(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            float s = seconds - m * 60f;
            return m > 0 ? System.FormattableString.Invariant($"{m}:{s:00.00}") : System.FormattableString.Invariant($"{s:0.00}");
        }
    }
}
