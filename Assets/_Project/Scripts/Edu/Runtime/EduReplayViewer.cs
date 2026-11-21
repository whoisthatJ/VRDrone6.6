using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using Ushqyr.Core;
using Ushqyr.Flight;
using Ushqyr.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The replay viewer (EDU_PLAN F2): a recorded flight on a timeline, at 0.1 to 4 times its speed, seen from five
    /// cameras, with the pilot's sticks, graphs of height, speed and throttle, markers for what happened, and a second
    /// flight (the reference of an activity) beside it with the time between the two.
    /// <para>
    /// The viewer flies copies of the drone's model; the real drone stands still and the game's clock is stopped, so
    /// nothing in the scene (gates, timers, bots) reacts to a flight that is only being watched.
    /// </para>
    /// Space plays and pauses, the arrow keys step (Shift: one second), C changes the camera, − and + the speed,
    /// R shows or hides the reference, V saves the flight as a video, Esc closes.
    /// </summary>
    public sealed class EduReplayViewer : MonoBehaviour
    {
        private const int GraphWidth = 600, GraphHeight = 64;
        private static readonly ReplaySeries[] Series = { ReplaySeries.Height, ReplaySeries.Speed, ReplaySeries.Throttle };
        private static readonly string[] SeriesName = { "Height", "Speed", "Throttle" };
        private static readonly Color[] SeriesColor = { new Color(0.4f, 0.8f, 1f), new Color(0.5f, 1f, 0.55f), new Color(1f, 0.7f, 0.3f) };
        private static readonly string[] ViewName = { "Chase", "Onboard", "Fixed", "From above", "Free" };

        public static EduReplayViewer Current { get; private set; }

        private QuadVehicle _vehicle;
        private bool _vehicleStepping;
        private float _timeScale;
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<Camera> _cameras = new List<Camera>();
        private readonly List<Canvas> _canvases = new List<Canvas>();
        private readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();
        private Transform _model, _ghost;
        private MaterialPropertyBlock _block;
        private LineRenderer _path, _referencePath;
        private string _title;
        private TextMeshProUGUI _titleText;
        private Button _close, _videoButton;
        private EduVideo _video;
        private float _exportStep;
        private bool _exportStop;
        private string _notice = "";
        private float _noticeUntil;
        private FlightRecording _reference;
        private ReplayComparison _gap;
        private List<KeyValuePair<float, FlightEvent>> _events;
        private float _groundY;
        private bool _jumped = true, _settingSlider;

        private Slider _slider;
        private Button _play, _cameraButton, _referenceButton;
        private TextMeshProUGUI _clock, _speed, _readout, _state, _event, _gapText;
        private readonly TextMeshProUGUI[] _graphValue = new TextMeshProUGUI[3];
        private readonly RectTransform[] _graphCursor = new RectTransform[3];
        private RectTransform _leftDot, _rightDot;

        public ReplayPlayback Playback { get; private set; }
        public ReplayCamera View { get; private set; }
        public bool HasReference => _reference != null;
        public bool ShowReference { get; private set; }
        /// <summary>Seconds behind the reference at the cursor (negative: ahead); NaN with no reference or off its path.</summary>
        public float Gap => _gap != null ? _gap.GapAt(Playback.Time) : float.NaN;
        public event Action Closed;
        /// <summary>True while a video is being written; the viewer plays by itself then.</summary>
        public bool Exporting => _video != null;
        /// <summary>The last video written (a path), or why the last try failed.</summary>
        public string LastVideo { get; private set; } = "";
        public bool LastVideoOk { get; private set; }

        /// <summary>
        /// Opens the viewer on <paramref name="flight"/>, with <paramref name="reference"/> to compare against if given.
        /// <paramref name="vehicle"/> is the drone in the scene whose model is shown; it is given back untouched on close.
        /// </summary>
        public static EduReplayViewer Open(QuadVehicle vehicle, FlightRecording flight, FlightRecording reference = null, string title = null)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (flight == null || flight.Samples.Count < 2) throw new ArgumentException("the recording is empty");
            if (Current != null) Current.Close();
            var viewer = new GameObject("Edu Replay Viewer").AddComponent<EduReplayViewer>();
            viewer.Begin(vehicle, flight, reference != null && reference.Samples.Count >= 2 ? reference : null, title);
            Current = viewer;
            return viewer;
        }

        private void Begin(QuadVehicle vehicle, FlightRecording flight, FlightRecording reference, string title)
        {
            _vehicle = vehicle;
            _reference = reference;
            Playback = new ReplayPlayback(flight);
            _events = Playback.Events();
            if (reference != null) _gap = new ReplayComparison(flight, reference);
            ShowReference = reference != null;
            _groundY = float.MaxValue;
            foreach (FlightSample s in flight.Samples) _groundY = Mathf.Min(_groundY, s.Y);

            // The real drone stands still and out of sight; the game's clock stops.
            _vehicleStepping = vehicle.ExternalStepping;
            vehicle.ExternalStepping = true;
            _timeScale = Time.timeScale;
            Time.timeScale = 0f;
            _model = CopyModel(vehicle.transform, "Replay Drone", null);
            _path = Path(flight, "Flight Path", new Color(1f, 0.6f, 0.2f, 0.9f));
            if (reference != null)
            {
                _ghost = CopyModel(vehicle.transform, "Reference Drone", new Color(0.35f, 0.85f, 1f, 0.45f));
                _referencePath = Path(reference, "Reference Path", new Color(0.35f, 0.85f, 1f, 0.9f));
            }
            foreach (Renderer r in vehicle.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                _hidden.Add(r);
            }

            Camera main = Camera.main;
            var camGo = new GameObject("Replay Camera");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            if (main != null)
            {
                cam.CopyFrom(main);
                cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            }
            cam.targetTexture = null;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 60f;
            cam.depth = 50f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            foreach (Camera other in Camera.allCameras)
            {
                if (other == cam || other.targetTexture != null) continue; // a camera drawing into a texture feeds something else
                other.enabled = false;
                _cameras.Add(other);
            }
            // The game's own screens (the flight display, the menu) would only lie over a flight that is being watched.
            foreach (Canvas other in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!other.isRootCanvas || !other.enabled) continue;
                other.enabled = false;
                _canvases.Add(other);
            }
            View = new ReplayCamera(cam);
            Bounds size = ModelBounds(_model);
            float reach = Mathf.Max(size.extents.x, size.extents.z) * 2f;
            View.ChaseDistance = Mathf.Clamp(reach * 5f, 0.8f, 6f);
            View.ChaseHeight = View.ChaseDistance * 0.3f;

            BuildUi(title, flight);
            L.Changed += Relabel;
            Playback.Playing = true;
        }

        public void Close()
        {
            if (_vehicle == null && Current != this) return;
            if (_video != null)
            {
                _video.Abort();
                _video = null;
            }
            L.Changed -= Relabel;
            foreach (Renderer r in _hidden) if (r != null) r.enabled = true;
            foreach (Camera c in _cameras) if (c != null) c.enabled = true;
            foreach (Canvas c in _canvases) if (c != null) c.enabled = true;
            foreach (UnityEngine.Object o in _made) if (o != null) Destroy(o);
            if (_model != null) Destroy(_model.gameObject);
            if (_ghost != null) Destroy(_ghost.gameObject);
            if (_path != null) Destroy(_path.gameObject);
            if (_referencePath != null) Destroy(_referencePath.gameObject);
            if (_vehicle != null) _vehicle.ExternalStepping = _vehicleStepping;
            Time.timeScale = _timeScale;
            _vehicle = null;
            if (Current == this) Current = null;
            Destroy(gameObject);
            Closed?.Invoke();
        }

        private void OnDestroy()
        {
            // Closed by a scene change: still give the game its clock and cameras back.
            if (_vehicle != null || Current == this) Close();
        }

        // ---------- the model ----------

        /// <summary>
        /// A still copy of the drone's visible meshes under one transform. <paramref name="tint"/> makes it a
        /// see-through, single-colour copy (the reference flight).
        /// </summary>
        private Transform CopyModel(Transform source, string name, Color? tint)
        {
            var root = new GameObject(name).transform;
            var skip = new HashSet<Renderer>();
            foreach (LODGroup group in source.GetComponentsInChildren<LODGroup>())
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers) if (r != null) skip.Add(r);
            }
            Material ghost = null;
            foreach (MeshRenderer r in source.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = r.GetComponent<MeshFilter>();
                if (!r.enabled || skip.Contains(r) || filter == null || filter.sharedMesh == null) continue;
                var part = new GameObject(r.name);
                part.transform.SetParent(root, false);
                part.transform.localPosition = source.InverseTransformPoint(r.transform.position);
                part.transform.localRotation = Quaternion.Inverse(source.rotation) * r.transform.rotation;
                Vector3 a = r.transform.lossyScale, b = source.lossyScale;
                part.transform.localScale = new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = part.AddComponent<MeshRenderer>();
                if (tint == null)
                {
                    copy.sharedMaterials = r.sharedMaterials;
                    // A model coloured through property blocks keeps its colours in the copy.
                    if (r.HasPropertyBlock())
                    {
                        _block ??= new MaterialPropertyBlock();
                        r.GetPropertyBlock(_block);
                        copy.SetPropertyBlock(_block);
                    }
                }
                if (tint != null)
                {
                    if (ghost == null && r.sharedMaterial != null) ghost = GhostMaterial(r.sharedMaterial, tint.Value);
                    var all = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < all.Length; i++) all[i] = ghost;
                    copy.sharedMaterials = all;
                    copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            return root;
        }

        private Material GhostMaterial(Material from, Color tint)
        {
            var m = new Material(from) { name = "Reference Drone" };
            _made.Add(m);
            foreach (string texture in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
                if (m.HasProperty(texture)) m.SetTexture(texture, null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", new Color(tint.r, tint.g, tint.b) * 0.6f);
                m.EnableKeyword("_EMISSION");
            }
            // See-through where the shader has that form in this build; a solid light-blue drone otherwise.
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m;
        }

        /// <summary>The whole flight as a line in the air: where the drone has been and where it is about to go.</summary>
        private LineRenderer Path(FlightRecording flight, string name, Color color)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            var material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
            _made.Add(material);
            line.sharedMaterial = material;
            line.startColor = line.endColor = color;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCornerVertices = 2;
            // Twenty points a second are plenty for a line; a ten-minute flight is 12 000 points.
            int step = Mathf.Max(1, Mathf.RoundToInt(flight.Rate / 20f));
            var points = new List<Vector3>(flight.Samples.Count / step + 2);
            for (int i = 0; i < flight.Samples.Count; i += step) points.Add(new Vector3(flight.Samples[i].X, flight.Samples[i].Y, flight.Samples[i].Z));
            FlightSample last = flight.Samples[flight.Samples.Count - 1];
            points.Add(new Vector3(last.X, last.Y, last.Z));
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
            return line;
        }

        private static Bounds ModelBounds(Transform model)
        {
            var bounds = new Bounds(model.position, Vector3.one * 0.1f);
            bool first = true;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                if (first) bounds = r.bounds; else bounds.Encapsulate(r.bounds);
                first = false;
            }
            return bounds;
        }

        // ---------- the screen ----------

        private void BuildUi(string title, FlightRecording flight)
        {
            Canvas canvas = EduUi.Canvas("Replay Screen", 900);
            canvas.transform.SetParent(transform, false);
            Transform root = canvas.transform;

            // Top left: what is being watched, and the numbers at the cursor.
            RectTransform info = EduUi.Panel(root, "Info", 20f, 20f, 560f, _reference != null ? 196f : 164f);
            flight.Notes.TryGetValue("drone", out string drone);
            flight.Notes.TryGetValue("started", out string started);
            _title = title;
            _titleText = EduUi.Text(info, "", 16f, 8f, 528f, 34f, 25f);
            EduUi.Text(info, ((drone ?? "") + "   " + LocalDate(started)).Trim(), 16f, 42f, 528f, 24f, 17f, EduUi.Dim);
            _readout = EduUi.Text(info, "", 16f, 70f, 528f, 28f, 21f);
            _state = EduUi.Text(info, "", 16f, 98f, 528f, 28f, 21f);
            _event = EduUi.Text(info, "", 16f, 128f, 528f, 28f, 19f, EduUi.Accent);
            if (_reference != null) _gapText = EduUi.Text(info, "", 16f, 160f, 528f, 28f, 21f, new Color(0.45f, 0.9f, 1f));

            // Bottom: the timeline with its markers, then the controls.
            RectTransform bar = EduUi.Panel(root, "Bar", 0f, 0f, 1880f, 112f);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(0f, 16f);
            _slider = EduUi.Slider(bar, 16f, 12f, 1848f, 22f, v =>
            {
                if (_settingSlider) return;
                Playback.Playing = false;
                Playback.Seek(v);
                _jumped = true;
            });
            _slider.minValue = 0f;
            _slider.maxValue = Mathf.Max(0.01f, Playback.End);
            foreach (KeyValuePair<float, FlightEvent> e in _events)
            {
                Color c = e.Value.Type == "crash" || e.Value.Type == "breach" ? new Color(1f, 0.3f, 0.25f)
                    : e.Value.Type == "gate" ? new Color(0.4f, 0.95f, 0.5f) : new Color(1f, 0.85f, 0.3f);
                EduUi.Panel(bar, "Marker " + e.Value.Type, 16f + 1848f * e.Key / _slider.maxValue - 2f, 6f, 4f, 34f, c, false);
            }
            _play = EduUi.Button(bar, "", 16f, 50f, 170f, 48f, () => Playback.Toggle());
            EduUi.Button(bar, "−", 198f, 50f, 48f, 48f, () => Playback.Slower(), 28f);
            _speed = EduUi.Text(bar, "", 250f, 50f, 84f, 48f, 23f, Color.white, TextAlignmentOptions.Center);
            EduUi.Button(bar, "+", 338f, 50f, 48f, 48f, () => Playback.Faster(), 28f);
            _cameraButton = EduUi.Button(bar, "", 400f, 50f, 300f, 48f, () => View.Next());
            if (_reference != null) _referenceButton = EduUi.Button(bar, "", 712f, 50f, 310f, 48f, ToggleReference);
            _videoButton = EduUi.Button(bar, "", 1034f, 50f, 150f, 48f, () => ExportVideo());
            _clock = EduUi.Text(bar, "", 1200f, 50f, 230f, 48f, 25f);
            EduUi.Text(bar, "Space  ← →  C  − +" + (_reference != null ? "  R" : "") + "  V  Esc", 1430f, 50f, 262f, 48f, 16f, EduUi.Dim, TextAlignmentOptions.MidlineRight);
            _close = EduUi.Button(bar, "", 1704f, 50f, 160f, 48f, Close);
            _close.name = "Close";

            // Bottom left: the sticks, as a mode 2 radio holds them.
            RectTransform sticks = EduUi.Panel(root, "Sticks", 0f, 0f, 344f, 176f, null, false);
            sticks.anchorMin = sticks.anchorMax = sticks.pivot = new Vector2(0.5f, 0f);
            sticks.anchoredPosition = new Vector2(-768f, 140f);
            _leftDot = StickBox(sticks, 14f);
            _rightDot = StickBox(sticks, 180f);

            // Bottom right: the graphs.
            RectTransform graphs = EduUi.Panel(root, "Graphs", 0f, 0f, 632f, 308f, null, false);
            graphs.anchorMin = graphs.anchorMax = graphs.pivot = new Vector2(0.5f, 0f);
            graphs.anchoredPosition = new Vector2(624f, 140f);
            for (int i = 0; i < 3; i++)
            {
                float y = 8f + i * 100f;
                ReplayGraph graph = ReplayGraph.Build(flight, Series[i], GraphWidth, _groundY);
                _graphValue[i] = EduUi.Text(graphs, "", 16f, y, 600f, 26f, 18f, SeriesColor[i]);
                RectTransform plot = EduUi.Rect(graphs, "Graph " + SeriesName[i], 16f, y + 28f, GraphWidth, GraphHeight);
                var image = plot.gameObject.AddComponent<RawImage>();
                image.texture = Draw(graph, SeriesColor[i]);
                image.raycastTarget = false;
                _graphCursor[i] = EduUi.Panel(plot, "Cursor", 0f, 0f, 2f, GraphHeight, Color.white, false);
            }
            Relabel();
        }

        private RectTransform StickBox(Transform parent, float x)
        {
            RectTransform box = EduUi.Panel(parent, "Stick", x, 14f, 150f, 150f, new Color(1f, 1f, 1f, 0.08f), false);
            EduUi.Panel(box, "Across", 0f, 74f, 150f, 2f, new Color(1f, 1f, 1f, 0.18f), false);
            EduUi.Panel(box, "Up", 74f, 0f, 2f, 150f, new Color(1f, 1f, 1f, 0.18f), false);
            RectTransform dot = EduUi.Panel(box, "Dot", 0f, 0f, 16f, 16f, EduUi.Accent, false);
            dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0.5f, 0.5f);
            return dot;
        }

        private Texture2D Draw(ReplayGraph graph, Color color)
        {
            var texture = new Texture2D(GraphWidth, GraphHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _made.Add(texture);
            var pixels = new Color32[GraphWidth * GraphHeight];
            var back = (Color32)new Color(1f, 1f, 1f, 0.06f);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = back;
            float range = Mathf.Max(1e-6f, graph.Max - graph.Min);
            Color32 line = color;
            int previous = -1;
            for (int x = 0; x < GraphWidth && x < graph.Low.Length; x++)
            {
                int lo = Mathf.Clamp(Mathf.RoundToInt((graph.Low[x] - graph.Min) / range * (GraphHeight - 2)), 0, GraphHeight - 2);
                int hi = Mathf.Clamp(Mathf.RoundToInt((graph.High[x] - graph.Min) / range * (GraphHeight - 2)), 0, GraphHeight - 2) + 1;
                // Join to the column before, so a steep climb is a line and not a row of dots.
                if (previous >= 0)
                {
                    if (previous < lo) lo = previous;
                    if (previous > hi) hi = previous;
                }
                for (int y = lo; y <= hi; y++) pixels[y * GraphWidth + x] = line;
                previous = (lo + hi) / 2;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void ToggleReference()
        {
            ShowReference = !ShowReference;
            Relabel();
        }

        private void Relabel()
        {
            _titleText.text = string.IsNullOrEmpty(_title) ? L.T("Recorded flight") : _title;
            EduUi.SetLabel(_close, L.T("Close"));
            EduUi.SetLabel(_videoButton, L.T("Video"));
            if (_referenceButton != null) EduUi.SetLabel(_referenceButton, L.T("Reference flight") + ": " + L.T(ShowReference ? "shown" : "hidden"));
        }

        private static string LocalDate(string utc)
        {
            return DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)
                ? t.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : "";
        }

        // ---------- video ----------

        /// <summary>
        /// Saves the whole flight as an MP4 of what the viewer shows (the chosen camera, the sticks, the graphs), at
        /// <paramref name="fps"/> pictures a second of flight time. The flight plays from the start by itself; Esc stops
        /// it. Returns false, with the reason in <see cref="LastVideo"/>, when it cannot start.
        /// </summary>
        public bool ExportVideo(string path = null, float fps = 30f)
        {
            if (Exporting) return false;
            LastVideoOk = false;
            string ffmpeg = EduVideo.Find();
            if (ffmpeg == null)
            {
                LastVideo = L.T("ffmpeg was not found, so a video cannot be made");
                Notice(LastVideo);
                return false;
            }
            if (string.IsNullOrEmpty(path))
                path = System.IO.Path.Combine(EduSession.Folder, "videos", "flight-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".mp4");
            try
            {
                // A texture's bytes start with the bottom row and a video's with the top one. Where the graphics system
                // counts rows from the top (Direct3D), the picture grabbed from the screen is already upside down, and
                // the two cancel; elsewhere ffmpeg turns it over.
                _video = new EduVideo(ffmpeg, Screen.width, Screen.height, fps, path, !SystemInfo.graphicsUVStartsAtTop);
            }
            catch (Exception e)
            {
                LastVideo = e.Message;
                Notice(L.F("The video was not saved: {0}", e.Message));
                return false;
            }
            _exportStep = 1f / fps;
            _exportStop = false;
            _notice = "";
            StartCoroutine(Export(path));
            return true;
        }

        private IEnumerator Export(string path)
        {
            int width = Screen.width, height = Screen.height;
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var bytes = new byte[width * height * 3];
            var endOfFrame = new WaitForEndOfFrame();
            Playback.Playing = false;
            Playback.Seek(0f);
            _jumped = true;
            int frames = Mathf.CeilToInt(Playback.End / _exportStep) + 1;
            for (int i = 0; i < frames && !_exportStop && !_video.Failed; i++)
            {
                Playback.Seek(i * _exportStep);
                yield return endOfFrame; // Update has put the drone and the screen at this moment; now the frame is drawn
                if (Screen.width != width || Screen.height != height) { _exportStop = true; break; } // the window was resized
                ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                RenderTexture before = RenderTexture.active;
                RenderTexture.active = target;
                picture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                RenderTexture.active = before;
                picture.GetRawTextureData<byte>().CopyTo(bytes);
                _video.Write(bytes, bytes.Length);
            }
            EduVideo video = _video;
            _video = null;
            Destroy(target);
            Destroy(picture);
            if (_exportStop)
            {
                video.Abort();
                LastVideo = L.T("The video was stopped");
                Notice(LastVideo);
            }
            else if (video.Close())
            {
                LastVideo = path;
                LastVideoOk = true;
                Notice(L.F("Video saved: {0}", path));
            }
            else
            {
                LastVideo = video.Errors.Length > 0 ? video.Errors : "ffmpeg";
                Notice(L.F("The video was not saved: {0}", LastVideo));
            }
        }

        private void Notice(string text)
        {
            _notice = text;
            _noticeUntil = Time.unscaledTime + 10f;
        }

        // ---------- every frame ----------

        private void Update()
        {
            if (Playback == null) return;
            Keyboard kb = Keyboard.current;
            if (Exporting)
            {
                // The video sets the time itself, one picture at a time; only Esc is listened to, and it stops the video.
                if (kb != null && kb.escapeKey.wasPressedThisFrame) _exportStop = true;
            }
            else if (kb != null)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
                if (kb.spaceKey.wasPressedThisFrame) Playback.Toggle();
                if (kb.rightArrowKey.wasPressedThisFrame) { Playback.Step(shift ? 1f : 0.05f); _jumped = shift; }
                if (kb.leftArrowKey.wasPressedThisFrame) { Playback.Step(shift ? -1f : -0.05f); _jumped = shift; }
                if (kb.cKey.wasPressedThisFrame) View.Next();
                if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) Playback.Slower();
                if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) Playback.Faster();
                if (kb.rKey.wasPressedThisFrame && _reference != null) ToggleReference();
                if (kb.vKey.wasPressedThisFrame) ExportVideo();
            }
            float dt = Exporting ? _exportStep : Time.unscaledDeltaTime;
            if (!Exporting) Playback.Advance(dt);
            float t = Playback.Time;
            FlightSample s = Playback.Now;
            var position = new Vector3(s.X, s.Y, s.Z);
            var rotation = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            _model.SetPositionAndRotation(position, rotation);
            if (_ghost != null)
            {
                bool show = ShowReference && t <= _reference.Duration;
                if (_ghost.gameObject.activeSelf != show) _ghost.gameObject.SetActive(show);
                if (show)
                {
                    FlightSample g = ReplayPlayback.SampleAt(_reference, t);
                    _ghost.SetPositionAndRotation(new Vector3(g.X, g.Y, g.Z), new Quaternion(g.Qx, g.Qy, g.Qz, g.Qw));
                }
            }
            View.Update(position, rotation, dt, _jumped);
            // The lines keep the same thickness on the screen from near and from far; from the drone itself they are
            // left out (the line runs through the camera).
            bool lines = View.View != ReplayView.Onboard;
            float width = Mathf.Clamp(Vector3.Distance(View.Position, position) * 0.004f, 0.008f, 0.6f);
            _path.enabled = lines;
            _path.widthMultiplier = width;
            if (_referencePath != null)
            {
                _referencePath.enabled = lines && ShowReference;
                _referencePath.widthMultiplier = width;
            }
            _jumped = false;
            Show(t, s);
        }

        private void Show(float t, in FlightSample s)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            _settingSlider = true;
            _slider.value = t;
            _settingSlider = false;
            EduUi.SetLabel(_play, L.T(Playback.Playing ? "Pause" : "Play"));
            EduUi.SetLabel(_cameraButton, L.T("Camera") + ": " + L.T(ViewName[(int)View.View]));
            _speed.text = "×" + Playback.Speed.ToString("0.##", inv);
            _clock.text = EduUi.Clock(t) + " / " + EduUi.Clock(Playback.End);

            float height = s.Y - _groundY;
            _readout.text = L.T("Height") + " " + L.F("{0} m", height.ToString("0.0", inv)) + "     " + L.T("Speed") + " " + L.F("{0} m/s", s.Speed.ToString("0.0", inv));
            _state.text = L.T("Mode") + " " + L.T(EduFlight.ModeNames[Mathf.Min(s.Mode, EduFlight.ModeNames.Length - 1)]) + "     " + L.T("Battery") + " " + Mathf.RoundToInt(s.Battery * 100f) + "%"
                          + (s.Armed ? "" : "     " + L.T("motors off"));

            // The last thing that happened, shown for three seconds of the flight.
            string happened = "";
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                if (_events[i].Key > t) continue;
                if (t - _events[i].Key <= 3f) happened = EduUi.Clock(_events[i].Key) + "   " + L.T(_events[i].Value.Type);
                break;
            }
            if (_notice.Length > 0 && !Exporting && Time.unscaledTime < _noticeUntil) happened = _notice;
            _event.text = happened;

            if (_gapText != null)
            {
                float gap = Gap;
                _gapText.text = !ShowReference || float.IsNaN(gap) ? ""
                    : gap >= 0.005f ? L.F("{0} s behind the reference", gap.ToString("0.00", inv))
                    : gap <= -0.005f ? L.F("{0} s ahead of the reference", (-gap).ToString("0.00", inv))
                    : L.T("level with the reference");
            }

            // Mode 2: throttle and yaw on the left stick, pitch and roll on the right.
            _leftDot.anchoredPosition = new Vector2(Mathf.Clamp(s.Yaw, -1f, 1f) * 67f, (Mathf.Clamp01(s.Throttle) * 2f - 1f) * 67f);
            _rightDot.anchoredPosition = new Vector2(Mathf.Clamp(s.Roll, -1f, 1f) * 67f, Mathf.Clamp(s.Pitch, -1f, 1f) * 67f);

            float x = Playback.End > 0f ? t / Playback.End * (GraphWidth - 2) : 0f;
            for (int i = 0; i < 3; i++)
            {
                _graphCursor[i].anchoredPosition = new Vector2(x, 0f);
                float v = Series[i] == ReplaySeries.Height ? height : ReplayGraph.Value(s, Series[i]);
                string value = Series[i] == ReplaySeries.Height ? L.F("{0} m", v.ToString("0.0", inv))
                    : Series[i] == ReplaySeries.Speed ? L.F("{0} m/s", v.ToString("0.0", inv)) : Mathf.RoundToInt(v * 100f) + "%";
                _graphValue[i].text = L.T(SeriesName[i]) + "   " + value;
            }
        }
    }
}
