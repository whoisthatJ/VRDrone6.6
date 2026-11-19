using Ushqyr.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ushqyr.Bench
{
    /// <summary>
    /// What the people in the room see during a bench run: gate counter, attempt time, state, and a small preview of the
    /// exact frames the program receives. Built in code like the rest of the game's UI. None of this is drawn into the
    /// program's camera.
    /// </summary>
    public sealed class BenchHud : MonoBehaviour
    {
        [SerializeField] private BenchSession session;

        private TextMeshProUGUI _title, _gates, _time, _status, _drone, _previewCaption;
        private RawImage _preview;
        private Texture2D _previewTexture;
        private long _shownFrame = -1;
        private int _shownPoints = -1;
        private string _shownStatus;
        private float _nextTimeText;
        private BenchLeaderboardPanel _board;
        private BenchReplay _replay;
        private Transform _canvas;
        private GameObject _previewFrame;

        public void Configure(BenchSession s) => session = s;

        private void Start()
        {
            var canvasGo = new GameObject("Bench HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            BenchUi.EnsureEventSystem();
            _canvas = canvasGo.transform;
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var panel = Panel(canvasGo.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(600f, 250f));
            _title = Text(panel, new Vector2(24f, -16f), new Vector2(560f, 40f), 26f, new Color(1f, 0.55f, 0.15f));
            _gates = Text(panel, new Vector2(24f, -56f), new Vector2(560f, 70f), 58f, Color.white);
            _time = Text(panel, new Vector2(24f, -128f), new Vector2(560f, 46f), 34f, Color.white);
            _status = Text(panel, new Vector2(24f, -176f), new Vector2(560f, 34f), 24f, new Color(0.8f, 0.84f, 0.9f));
            _drone = Text(panel, new Vector2(24f, -210f), new Vector2(560f, 30f), 20f, new Color(0.62f, 0.66f, 0.72f));

            var frame = Panel(canvasGo.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(496f, 410f));
            _previewFrame = frame.gameObject;
            _previewCaption = Text(frame, new Vector2(8f, -6f), new Vector2(480f, 30f), 20f, new Color(0.8f, 0.84f, 0.9f));
            var img = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)img.transform;
            rt.SetParent(frame, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(8f, 8f);
            rt.sizeDelta = new Vector2(480f, 360f);
            _preview = img.GetComponent<RawImage>();
            _preview.color = Color.black;
            _preview.raycastTarget = false;
            // Frames are stored with the top row first; textures start at the bottom.
            _preview.uvRect = new Rect(0f, 1f, 1f, -1f);

            // The leaderboard (Tab) and, when an attempt folder is being shown, the judge replay.
            _board = gameObject.AddComponent<BenchLeaderboardPanel>();
            BenchUi.Button(panel, L.T("Leaderboard (Tab)"), 400f, 12f, 190f, 34f, () => _board.Toggle(), 17f);

            L.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            L.Changed -= Refresh;
            if (_previewTexture != null) Destroy(_previewTexture);
        }

        private void Refresh()
        {
            _shownPoints = -1;
            _shownStatus = null;
            if (_previewCaption != null) _previewCaption.text = L.T("Onboard camera: what the program receives");
        }

        private void Update()
        {
            if (session == null || _title == null) return;
            if (session.LoadError != null)
            {
                _title.text = "AI BENCH";
                _status.text = session.LoadError;
                return;
            }
            if (session.ReplayMode)
            {
                if (session.Track == null) return;
                if (_replay == null)
                {
                    _replay = gameObject.AddComponent<BenchReplay>();
                    _replay.Build(_canvas, session);
                    _board.Build(_canvas, session);
                    _previewFrame.SetActive(false);
                    _title.text = "AI BENCH  ·  " + L.T("REPLAY") + "  ·  " + session.Track.id;
                    _status.text = L.T("Space: play / pause    C: camera    arrows: step");
                    _drone.text = System.IO.Path.GetFileName(BenchLauncher.ReplayFolder.TrimEnd('/', '\\'));
                }
                _gates.text = L.F("Gates: {0}", _replay.CurrentPoints);
                _time.text = L.F("Time: {0} s", BenchUi.Seconds(_replay.CurrentTime));
                return;
            }
            if (!session.Ready) return;
            if (!_boardBuilt)
            {
                _boardBuilt = true;
                _board.Build(_canvas, session);
            }

            string status = session.State + "|" + session.Status + "|" + session.ProgramConnected + "|" + session.WritingFiles;
            if (status != _shownStatus)
            {
                _shownStatus = status;
                bool competition = session.Rules.Competition;
                _title.text = "AI BENCH  ·  " + (competition ? L.T("COMPETITION") : L.T("TRAINING")) + "  ·  " + session.Track.id;
                _status.text = StatusText();
                _drone.text = session.DroneName + (session.DroneCalibrated ? "" : "  ·  " + L.T("placeholder model, not calibrated"));
            }
            if (session.Points != _shownPoints)
            {
                _shownPoints = session.Points;
                _gates.text = L.F("Gates: {0}", session.Points);
            }
            if (Time.unscaledTime >= _nextTimeText)
            {
                _nextTimeText = Time.unscaledTime + 0.05f;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                _time.text = L.F("Time: {0} s", session.SimTime.ToString("F2", inv))
                             + (session.Points > 0 ? "   (" + L.F("last gate {0} s", session.LastScoreTime.ToString("F2", inv)) + ")" : "");
            }

            if (session.LastFrame != null && session.LastFrameSeq != _shownFrame)
            {
                _shownFrame = session.LastFrameSeq;
                if (_previewTexture == null)
                {
                    _previewTexture = new Texture2D(session.CameraWidth, session.CameraHeight, TextureFormat.RGB24, false, false) { filterMode = FilterMode.Bilinear };
                    _preview.texture = _previewTexture;
                    _preview.color = Color.white;
                }
                _previewTexture.LoadRawTextureData(session.LastFrame);
                _previewTexture.Apply(false);
            }
        }

        private bool _boardBuilt;

        private string StatusText()
        {
            if (session.WritingFiles) return L.T("Attempt over: writing logs and video");
            if (!session.ProgramConnected) return L.F("Waiting for a program on port {0}", session.Port);
            switch (session.State)
            {
                case AttemptState.Waiting: return L.T("Program connected, waiting for the start");
                case AttemptState.Running: return L.T("Attempt running");
                default:
                    switch (session.Status)
                    {
                        case "finished": return L.T("Attempt over: the program finished");
                        case "time_limit": return L.T("Attempt over: time limit");
                        case "disconnected": return L.T("Attempt over: the program disconnected");
                        case "stuck": return L.T("Attempt over: the drone stopped moving");
                        default: return L.T("Attempt over");
                    }
            }
        }

        private static RectTransform Panel(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.05f, 0.06f, 0.08f, 0.82f);
            image.raycastTarget = false;
            return rt;
        }

        private static TextMeshProUGUI Text(Transform parent, Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }
    }
}
