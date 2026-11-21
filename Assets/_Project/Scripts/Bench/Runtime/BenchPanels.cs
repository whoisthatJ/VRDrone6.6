using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ushqyr.Core;
using Ushqyr.Flight;
using Ushqyr.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ushqyr.Bench
{
    /// <summary>Small code-built uGUI pieces shared by the bench screens.</summary>
    public static class BenchUi
    {
        public static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.88f);
        public static readonly Color ButtonColor = new Color(0.18f, 0.2f, 0.26f, 1f);
        public static readonly Color Accent = new Color(1f, 0.55f, 0.15f);
        public static readonly Color Dim = new Color(0.72f, 0.76f, 0.82f);

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>A rectangle placed by its top-left corner inside <paramref name="parent"/> (y grows downwards).</summary>
        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Panel(Transform parent, string name, float x, float y, float w, float h, Color? color = null)
        {
            RectTransform rt = Rect(parent, name, x, y, w, h);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color ?? PanelColor;
            return rt;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float x, float y, float w, float h, float size, Color? color = null,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            RectTransform rt = Rect(parent, "Text", x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color ?? Color.white;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string text, float x, float y, float w, float h, Action onClick, float size = 22f)
        {
            RectTransform rt = Panel(parent, "Button " + text, x, y, w, h, ButtonColor);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.GetComponent<Image>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            var label = Text(rt, text, 8f, 0f, w - 16f, h, size, Color.white, TextAlignmentOptions.MidlineLeft);
            // A button's text is fixed once shown, and its length depends on the language: let it shrink to fit
            // (down to 60%) instead of being cut with "…".
            label.enableAutoSizing = true;
            label.fontSizeMax = size;
            label.fontSizeMin = size * 0.6f;
            label.name = "Label";
            return button;
        }

        public static void SetLabel(Button b, string text) => b.GetComponentInChildren<TextMeshProUGUI>().text = text;

        public static Slider Slider(Transform parent, float x, float y, float w, float h, Action<float> onChange)
        {
            RectTransform rt = Panel(parent, "Slider", x, y, w, h, new Color(0.14f, 0.15f, 0.19f, 1f));
            var slider = rt.gameObject.AddComponent<Slider>();
            RectTransform fillArea = Rect(rt, "Fill Area", 0f, 0f, w, h);
            fillArea.anchorMin = Vector2.zero; fillArea.anchorMax = Vector2.one; fillArea.offsetMin = fillArea.offsetMax = Vector2.zero;
            RectTransform fill = Panel(fillArea, "Fill", 0f, 0f, 0f, 0f, new Color(1f, 0.55f, 0.15f, 0.55f));
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
            RectTransform handleArea = Rect(rt, "Handle Area", 0f, 0f, w, h);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one; handleArea.offsetMin = handleArea.offsetMax = Vector2.zero;
            RectTransform handle = Panel(handleArea, "Handle", 0f, 0f, 10f, 0f, Color.white);
            handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f); handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(10f, 0f);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        public static string Seconds(double t) => t.ToString("F2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The leaderboard screen (Tab): this track's board and the board over all tracks, the latest attempts with a
    /// button to open each in the judge replay, and CSV export. It reads the result files of the output folder.
    /// </summary>
    public sealed class BenchLeaderboardPanel : MonoBehaviour
    {
        private BenchSession _session;
        private RectTransform _root, _content;
        private bool _overall;
        private TextMeshProUGUI _note;
        private string _noteText = "";

        public bool Visible => _root != null && _root.gameObject.activeSelf;

        public void Build(Transform canvas, BenchSession session)
        {
            _session = session;
            _root = BenchUi.Panel(canvas, "Leaderboard", 0f, 0f, 1180f, 800f, new Color(0.04f, 0.05f, 0.07f, 1f));
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            _root.gameObject.SetActive(!_root.gameObject.activeSelf);
            if (!_root.gameObject.activeSelf) return;
            _root.SetAsLastSibling();
            Refresh();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame && _root != null) Toggle();
        }

        public void Refresh()
        {
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            string folder = BenchLauncher.OutputFolder;
            List<AttemptResult> all = Leaderboard.Load(folder);
            string track = _session.Track != null ? _session.Track.id : null;

            BenchUi.Text(_root, L.T("LEADERBOARD"), 28f, 18f, 500f, 40f, 30f, BenchUi.Accent);
            BenchUi.Button(_root, _overall ? L.T("All tracks") : L.F("Track {0}", track), 420f, 18f, 250f, 40f, () => { _overall = !_overall; Refresh(); });
            BenchUi.Button(_root, L.T("Export CSV"), 690f, 18f, 190f, 40f, () => Export(all, ','));
            BenchUi.Button(_root, L.T("Export CSV ( ; )"), 890f, 18f, 190f, 40f, () => Export(all, ';'));
            BenchUi.Button(_root, "X", 1110f, 18f, 44f, 40f, Toggle);

            List<LeaderboardRow> rows = Leaderboard.Rank(all, _overall ? null : track);
            float y = 80f;
            BenchUi.Text(_root, "#", 30f, y, 50f, 28f, 20f, BenchUi.Dim);
            BenchUi.Text(_root, L.T("Team"), 90f, y, 400f, 28f, 20f, BenchUi.Dim);
            BenchUi.Text(_root, L.T("Points"), 520f, y, 120f, 28f, 20f, BenchUi.Dim);
            BenchUi.Text(_root, L.T("Time, s"), 660f, y, 160f, 28f, 20f, BenchUi.Dim);
            y += 30f;
            if (rows.Count == 0) BenchUi.Text(_root, L.T("No recorded attempts yet."), 30f, y, 900f, 30f, 22f);
            for (int i = 0; i < rows.Count && i < 10; i++, y += 30f)
            {
                LeaderboardRow r = rows[i];
                BenchUi.Text(_root, r.Rank.ToString(CultureInfo.InvariantCulture), 30f, y, 50f, 28f, 22f);
                BenchUi.Text(_root, r.Team, 90f, y, 410f, 28f, 22f);
                BenchUi.Text(_root, r.Points.ToString(CultureInfo.InvariantCulture), 520f, y, 120f, 28f, 22f);
                BenchUi.Text(_root, BenchUi.Seconds(r.Time), 660f, y, 160f, 28f, 22f);
            }

            y = 430f;
            BenchUi.Text(_root, L.T("Latest attempts"), 28f, y, 500f, 30f, 22f, BenchUi.Accent);
            y += 34f;
            for (int i = all.Count - 1, shown = 0; i >= 0 && shown < 8; i--, shown++, y += 34f)
            {
                AttemptResult a = all[i];
                string line = $"{a.date_utc.Replace("T", " ").Replace("Z", "")}   {a.team}   {a.track_id}   #{a.attempt}   " +
                              $"{a.points}   {BenchUi.Seconds(a.time)} s   {a.status}";
                BenchUi.Text(_root, line, 30f, y + 2f, 930f, 28f, 19f, a.Counts ? Color.white : BenchUi.Dim);
                string attemptFolder = a.Folder;
                BenchUi.Button(_root, L.T("Replay"), 990f, y, 160f, 30f, () => BenchLauncher.Replay(attemptFolder), 19f);
            }
            _note = BenchUi.Text(_root, _noteText.Length > 0 ? _noteText : folder, 28f, 756f, 1120f, 28f, 17f, BenchUi.Dim);
        }

        private void Export(List<AttemptResult> all, char separator)
        {
            try
            {
                string folder = BenchLauncher.OutputFolder;
                Directory.CreateDirectory(folder);
                string results = Path.Combine(folder, "results.csv"), board = Path.Combine(folder, "leaderboard.csv");
                Leaderboard.WriteCsvFile(results, Leaderboard.ToCsv(all, separator));
                Leaderboard.WriteCsvFile(board, Leaderboard.RankedCsv(Leaderboard.Rank(all, _overall ? null : _session.Track.id), separator));
                _noteText = L.F("Saved: {0}", results) + "   +   leaderboard.csv";
            }
            catch (Exception e) { _noteText = e.Message; }
            if (_note != null) _note.text = _noteText;
        }
    }

    /// <summary>The track list (key T): public tracks and the user's own, each with "fly" and "edit"; new tracks.</summary>
    public sealed class BenchTrackPanel : MonoBehaviour
    {
        private RectTransform _root;

        public void Build(Transform canvas)
        {
            _root = BenchUi.Panel(canvas, "Tracks", 0f, 0f, 900f, 760f, new Color(0.04f, 0.05f, 0.07f, 1f));
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                          && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
            if (kb != null && kb.tKey.wasPressedThisFrame && _root != null && !typing) Toggle();
        }

        public void Toggle()
        {
            _root.gameObject.SetActive(!_root.gameObject.activeSelf);
            if (!_root.gameObject.activeSelf) return;
            _root.SetAsLastSibling();
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            BenchUi.Text(_root, L.T("TRACKS"), 28f, 18f, 300f, 40f, 30f, BenchUi.Accent);
            BenchUi.Button(_root, "X", 830f, 18f, 44f, 40f, Toggle);
#if !USHQYR_BENCH_SERVER
            BenchUi.Button(_root, L.T("New hall track"), 330f, 18f, 230f, 40f, () => BenchLauncher.EditNew("hall"), 20f);
            BenchUi.Button(_root, L.T("New parking track"), 570f, 18f, 250f, 40f, () => BenchLauncher.EditNew("parking"), 20f);
#endif
            float y = 80f;
            foreach (BenchLauncher.TrackEntry t in BenchLauncher.ListTracks())
            {
                if (y > 700f) break;
                string path = t.Path;
                BenchUi.Text(_root, t.Name, 30f, y + 3f, 330f, 30f, 21f, t.Public ? Color.white : new Color(0.75f, 0.95f, 1f));
                BenchUi.Text(_root, L.T(t.Venue) + "   " + L.F("{0} gates", t.Gates) + (t.Public ? "" : "   " + L.T("mine")), 370f, y + 5f, 250f, 28f, 18f, BenchUi.Dim);
                BenchUi.Button(_root, L.T("Fly"), 560f, y, 90f, 32f, () => BenchLauncher.Launch(path), 19f);
#if !USHQYR_BENCH_SERVER
                BenchUi.Button(_root, L.T("Edit"), 656f, y, 100f, 32f, () => BenchLauncher.Edit(path), 19f);
                // 500 labelled images of this track's gates, into the results folder.
                BenchUi.Button(_root, L.T("Dataset"), 762f, y, 110f, 32f, () => BenchLauncher.MakeDataset(path, 500, 1), 19f);
#endif
                y += 38f;
            }
        }
    }

    /// <summary>
    /// Judge replay of an attempt folder: integrity check first, then the recorded flight on a timeline with every
    /// scoring decision and its reason. Space plays and pauses, C changes the camera, the arrow keys step.
    /// </summary>
    public sealed class BenchReplay : MonoBehaviour
    {
        [Serializable]
        private sealed class EventLine
        {
            public double t;
            public string type, gate, result, reason, detail;
            public int points;
        }

        private BenchSession _session;
        private BenchSpectatorCamera _camera;
        private readonly List<float> _t = new List<float>();
        private readonly List<Vector3> _pos = new List<Vector3>();
        private readonly List<Quaternion> _rot = new List<Quaternion>();
        private readonly List<EventLine> _events = new List<EventLine>();
        private readonly List<Button> _eventButtons = new List<Button>();
        private AttemptResult _result;
        private float _time, _end, _speed = 1f;
        private bool _playing, _loaded, _settingSlider;
        private Slider _slider;
        private Button _play, _speedButton, _cameraButton;
        private TextMeshProUGUI _clock;
        private int _highlighted = -2;

        public string LoadError { get; private set; }
        public ManifestState Integrity { get; private set; }
        public string IntegrityDetail { get; private set; } = "";
        public double CurrentTime => _time;
        public int CurrentPoints { get; private set; }

        public void Build(Transform canvas, BenchSession session)
        {
            _session = session;
            _camera = FindAnyObjectByType<BenchSpectatorCamera>();
            string folder = BenchLauncher.ReplayFolder;
            try
            {
                byte[] key = null;
                try { key = BenchLauncher.SigningKey(); } catch (Exception) { /* no usable key here: checksums only */ }
                Integrity = AttemptManifest.Verify(folder, key, out string detail);
                IntegrityDetail = detail;
                Load(folder);
                _loaded = true;
            }
            catch (Exception e)
            {
                LoadError = e.Message;
                Debug.LogError("[Bench] replay: " + e.Message);
            }
            BuildUi(canvas);
            if (_camera != null)
            {
                _camera.AllViews = true;
                if (session.Rules != null) _camera.OnboardTilt = session.Rules.camera_tilt;
            }
            _playing = _loaded;
        }

        private void Load(string folder)
        {
            var inv = CultureInfo.InvariantCulture;
            _result = JsonUtility.FromJson<AttemptResult>(File.ReadAllText(Path.Combine(folder, "result.json")));
            string[] lines = File.ReadAllLines(Path.Combine(folder, "truth.csv"));
            for (int i = 1; i < lines.Length; i++)
            {
                string[] c = lines[i].Split(',');
                if (c.Length < 9) continue;
                _t.Add(float.Parse(c[1], inv));
                _pos.Add(new Vector3(float.Parse(c[2], inv), float.Parse(c[3], inv), float.Parse(c[4], inv)));
                _rot.Add(new Quaternion(float.Parse(c[5], inv), float.Parse(c[6], inv), float.Parse(c[7], inv), float.Parse(c[8], inv)));
            }
            foreach (string line in File.ReadAllLines(Path.Combine(folder, "events.jsonl")))
                if (line.Trim().Length > 0) _events.Add(JsonUtility.FromJson<EventLine>(line));
            _end = _t.Count > 0 ? _t[_t.Count - 1] : 0f;
        }

        private void BuildUi(Transform canvas)
        {
            // Left: integrity, result, events.
            RectTransform left = BenchUi.Panel(canvas, "Replay Info", 30f, 300f, 600f, 640f);
            Color state = Integrity == ManifestState.Verified ? new Color(0.35f, 0.9f, 0.45f)
                : Integrity == ManifestState.ChecksumsOnly ? new Color(1f, 0.8f, 0.25f) : new Color(1f, 0.35f, 0.3f);
            string stateText = Integrity == ManifestState.Verified ? L.T("VERIFIED")
                : Integrity == ManifestState.ChecksumsOnly ? L.T("CHECKSUMS ONLY") : Integrity == ManifestState.Missing ? L.T("NO MANIFEST") : L.T("MODIFIED");
            BenchUi.Text(left, stateText, 20f, 12f, 560f, 34f, 26f, state);
            BenchUi.Text(left, IntegrityDetail, 20f, 46f, 560f, 26f, 17f, BenchUi.Dim);
            if (LoadError != null)
            {
                BenchUi.Text(left, LoadError, 20f, 80f, 560f, 60f, 18f, new Color(1f, 0.4f, 0.35f));
                return;
            }
            BenchUi.Text(left, $"{_result.team}   {_result.track_id}   #{_result.attempt}", 20f, 78f, 560f, 30f, 22f);
            BenchUi.Text(left, L.F("Gates: {0}", _result.points) + "   " + L.F("Time: {0} s", BenchUi.Seconds(_result.time)) + "   " + _result.status,
                20f, 108f, 560f, 28f, 20f);
            BenchUi.Text(left, L.T("Scoring decisions (click to jump)"), 20f, 146f, 560f, 26f, 18f, BenchUi.Accent);
            float y = 176f;
            for (int i = 0; i < _events.Count && _eventButtons.Count < 15; i++)
            {
                EventLine e = _events[i];
                if (e.type != "gate" && e.type != "end" && e.type != "failsafe" && e.type != "collision") continue;
                string text = e.type == "gate"
                    ? $"{BenchUi.Seconds(e.t)}   {e.gate}   " + (e.result == "scored" ? $"+1 ({e.points})" : e.reason)
                    : $"{BenchUi.Seconds(e.t)}   {e.type}: {e.detail}";
                float at = (float)e.t;
                Button b = BenchUi.Button(left, text, 20f, y, 560f, 28f, () => Seek(at - 1.5f), 17f);
                b.name = "event " + i;
                _eventButtons.Add(b);
                y += 30f;
            }

            // Bottom: transport.
            RectTransform bar = BenchUi.Panel(canvas, "Replay Bar", 0f, 0f, 1240f, 64f);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(-150f, 24f);
            _play = BenchUi.Button(bar, "", 12f, 12f, 110f, 40f, () => _playing = !_playing);
            _speedButton = BenchUi.Button(bar, "", 130f, 12f, 90f, 40f, NextSpeed);
            _cameraButton = BenchUi.Button(bar, "", 228f, 12f, 290f, 40f, () => { if (_camera != null) _camera.Next(); });
            _slider = BenchUi.Slider(bar, 532f, 22f, 518f, 20f, v => { if (!_settingSlider) { _time = v; _playing = false; } });
            _slider.minValue = 0f;
            _slider.maxValue = Mathf.Max(0.01f, _end);
            _clock = BenchUi.Text(bar, "", 1062f, 12f, 170f, 40f, 22f, Color.white, TextAlignmentOptions.MidlineLeft);
        }

        private void NextSpeed() => _speed = _speed >= 4f ? 0.25f : _speed * 2f;

        public void Seek(float t)
        {
            _time = Mathf.Clamp(t, 0f, _end);
        }

        private void Update()
        {
            if (!_loaded || _t.Count == 0) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) _playing = !_playing;
                if (kb.rightArrowKey.wasPressedThisFrame) { _playing = false; Seek(_time + (kb.leftShiftKey.isPressed ? 1f : 0.05f)); }
                if (kb.leftArrowKey.wasPressedThisFrame) { _playing = false; Seek(_time - (kb.leftShiftKey.isPressed ? 1f : 0.05f)); }
            }
            if (_playing)
            {
                _time += Time.unscaledDeltaTime * _speed;
                if (_time >= _end) { _time = _end; _playing = false; }
            }

            // Pose at the cursor: the two recorded steps around it, blended.
            int hi = _t.BinarySearch(_time);
            if (hi < 0) hi = ~hi;
            hi = Mathf.Clamp(hi, 0, _t.Count - 1);
            int lo = Mathf.Max(0, hi - 1);
            float span = _t[hi] - _t[lo];
            float k = span > 1e-6f ? Mathf.Clamp01((_time - _t[lo]) / span) : 1f;
            _session.Quad.Teleport(Vector3.LerpUnclamped(_pos[lo], _pos[hi], k), Quaternion.SlerpUnclamped(_rot[lo], _rot[hi], k));

            int points = 0, current = -1;
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].t > _time) break;
                if (_events[i].type == "gate") points = _events[i].points;
                current = i;
            }
            CurrentPoints = points;
            if (current != _highlighted)
            {
                _highlighted = current;
                foreach (Button b in _eventButtons)
                    ((Image)b.targetGraphic).color = b.name == "event " + current ? new Color(0.45f, 0.3f, 0.12f, 1f) : BenchUi.ButtonColor;
            }

            _settingSlider = true;
            _slider.value = _time;
            _settingSlider = false;
            BenchUi.SetLabel(_play, _playing ? L.T("Pause") : L.T("Play"));
            BenchUi.SetLabel(_speedButton, "x" + _speed.ToString("0.##", CultureInfo.InvariantCulture));
            if (_camera != null) BenchUi.SetLabel(_cameraButton, L.T("Camera") + ": " + L.T(_camera.View.ToString()));
            _clock.text = BenchUi.Seconds(_time) + " / " + BenchUi.Seconds(_end);
        }
    }
}
