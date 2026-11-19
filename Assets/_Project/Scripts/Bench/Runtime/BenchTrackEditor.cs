#if !USHQYR_BENCH_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ushqyr.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The track editor: gates are placed on the floor plan with the mouse, each with its own LED settings; the track
    /// is checked as it changes, test-flown by the oracle pilot, saved as a track file and exported as a map picture.
    ///
    /// Mouse: click selects, drag moves (snaps to 0.25 m; Alt = free). Wheel zooms, right-drag pans (top view) or turns
    /// (3D view). Keys: N new gate at the pointer, Q / E turn by 15° (Shift: 5°), Ctrl+D duplicate, Delete remove,
    /// V top / 3D view.
    /// </summary>
    public sealed class BenchTrackEditor : MonoBehaviour
    {
        private static readonly string[] Palette = { "#FF00C8", "#00E5FF", "#FFD400", "#3CFF4E", "#FF5A1F", "#FFFFFF" };

        private BenchSession _session;
        private BenchTrack _track;
        private Venue _venue;
        private Camera _camera;
        private Transform _root;
        private RectTransform _panel;
        private LineRenderer _path;
        private readonly List<BenchGate> _gates = new List<BenchGate>();
        private int _selected = -1; // gate index; -2 = start pad
        private bool _dragging, _top = true, _dirty;
        private Vector3 _dragOffset, _focus;
        private float _zoom = 12f, _orbitYaw = 20f, _orbitPitch = 50f;
        private string _message = "";
        private List<TrackIssue> _issues = new List<TrackIssue>();
        private long _seed = 1;
        private int _difficulty = 1;
        private TMP_InputField _name;

        public void Build(Transform canvas, BenchSession session)
        {
            _session = session;
            _track = session.Track;
            _venue = Venue.Get(_track.venue);
            var spectator = FindAnyObjectByType<BenchSpectatorCamera>();
            _camera = spectator.GetComponent<Camera>();
            spectator.enabled = false;
            _root = new GameObject("Editor Track").transform;
            _panel = BenchUi.Panel(canvas, "Editor Panel", 0f, 0f, 470f, 1020f);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1f, 1f);
            _panel.anchoredPosition = new Vector2(-20f, -30f);

            var lineGo = new GameObject("Oracle Path");
            _path = lineGo.AddComponent<LineRenderer>();
            _path.material = new Material(Shader.Find("Sprites/Default"));
            _path.startColor = _path.endColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            _path.widthMultiplier = 0.06f;
            _path.positionCount = 0;
            Rebuild();
        }

        // ---------- the track in the scene ----------

        private void Rebuild()
        {
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            _gates.Clear();
            for (int i = 0; i < _track.gates.Count; i++)
            {
                TrackGate g = _track.gates[i];
                BenchGate gate = Instantiate(_session.GatePrefab, BenchTrack.V(g.position), Quaternion.Euler(0f, g.yaw, 0f), _root);
                gate.SetLed(BenchTrack.LedColor(g.led), g.led.brightness, false);
                _gates.Add(gate);
                Label((i + 1).ToString(CultureInfo.InvariantCulture), BenchTrack.V(g.position) + Vector3.up * 2.62f, gate.transform, i == _selected,
                    BenchTrack.LedColor(g.led));
                // A strip in the LED colour along the top bar, so the gate's colour shows on the floor plan.
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(strip.GetComponent<Collider>());
                strip.transform.SetParent(gate.transform, false);
                strip.transform.localPosition = new Vector3(0f, GateGeometry.Outer + 0.011f, 0f);
                strip.transform.localScale = new Vector3(GateGeometry.Outer, 0.02f, 0.12f);
                var sr = strip.GetComponent<Renderer>();
                sr.sharedMaterial = _path.sharedMaterial;
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", BenchTrack.LedColor(g.led));
                sr.SetPropertyBlock(block);
            }
            BenchSession.BuildProps(_track, _root, _session.GatePrefab);
            _session.Spawn.SetPositionAndRotation(BenchTrack.V(_track.start.position) + Vector3.up * 0.047f, Quaternion.Euler(0f, _track.start.yaw, 0f));
            _session.Quad.Teleport(_session.Spawn.position, _session.Spawn.rotation);
            var lights = FindAnyObjectByType<BenchVenueLights>();
            if (lights != null) lights.Apply(_track.lighting);
            _issues = TrackValidator.Validate(_track);
            _path.positionCount = 0;
            BuildPanel();
        }

        private void Label(string text, Vector3 at, Transform parent, bool highlight, Color colour)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(90f, 0f, 0f));
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = highlight ? 13f : 9f;
            t.fontStyle = highlight ? FontStyles.Bold : FontStyles.Normal;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.Lerp(colour, Color.white, 0.55f);
            t.rectTransform.sizeDelta = new Vector2(3f, 1.5f);
        }

        // ---------- panel ----------

        private void BuildPanel()
        {
            for (int i = _panel.childCount - 1; i >= 0; i--) Destroy(_panel.GetChild(i).gameObject);
            float y = 12f;
            BenchUi.Text(_panel, L.T("TRACK EDITOR"), 16f, y, 300f, 34f, 26f, BenchUi.Accent);
            BenchUi.Button(_panel, _top ? L.T("View: top") : L.T("View: 3D"), 300f, y, 154f, 34f, () => { _top = !_top; BuildPanel(); }, 18f);
            y += 44f;
            _name = InputField(_panel, _track.id, 16f, y, 438f, 36f, v => { _track.id = Clean(v); _track.name = v; _dirty = true; });
            y += 44f;
            BenchUi.Button(_panel, L.T("Venue") + ": " + L.T(_track.venue), 16f, y, 215f, 34f, SwitchVenue, 18f);
            BenchUi.Button(_panel, L.T("Light") + ": " + L.T(_track.lighting), 239f, y, 215f, 34f, () =>
            {
                int i = Array.IndexOf(_venue.Lighting, _track.lighting);
                _track.lighting = _venue.Lighting[(i + 1) % _venue.Lighting.Length];
                Changed();
            }, 18f);
            y += 42f;
            BenchUi.Text(_panel, L.F("Time limit: {0} s", _track.rules.time_limit), 16f, y + 4f, 240f, 30f, 18f);
            BenchUi.Button(_panel, "-", 300f, y, 70f, 34f, () => { _track.rules.time_limit = Mathf.Max(20f, _track.rules.time_limit - 10f); Changed(); });
            BenchUi.Button(_panel, "+", 384f, y, 70f, 34f, () => { _track.rules.time_limit = Mathf.Min(600f, _track.rules.time_limit + 10f); Changed(); });
            y += 48f;

            // Selection.
            if (_selected >= 0 && _selected < _track.gates.Count)
            {
                TrackGate g = _track.gates[_selected];
                BenchUi.Text(_panel, L.F("Gate {0}", _selected + 1) + "   " + g.id + "   " +
                                     FormattableString.Invariant($"x {g.position[0]:F2}  z {g.position[2]:F2}  {g.yaw:F0}°"), 16f, y, 438f, 28f, 18f, BenchUi.Accent);
                y += 32f;
                for (int c = 0; c < Palette.Length; c++)
                {
                    string colour = Palette[c];
                    RectTransform swatch = BenchUi.Panel(_panel, "Colour", 16f + c * 74f, y, 66f, 34f, BenchTrack.LedColor(new GateLed { color = colour }));
                    var b = swatch.gameObject.AddComponent<Button>();
                    b.targetGraphic = swatch.GetComponent<Image>();
                    b.onClick.AddListener(() => { g.led.color = colour; Changed(); });
                    if (string.Equals(g.led.color, colour, StringComparison.OrdinalIgnoreCase))
                        BenchUi.Text(swatch, "●", 0f, 0f, 66f, 34f, 20f, Color.black, TextAlignmentOptions.Center);
                }
                y += 42f;
                BenchUi.Button(_panel, L.F("Brightness {0}%", Mathf.RoundToInt(g.led.brightness * 100f)), 16f, y, 215f, 34f, () =>
                {
                    g.led.brightness = g.led.brightness <= 0.3f ? 1f : g.led.brightness - 0.25f;
                    Changed();
                }, 18f);
                BenchUi.Button(_panel, g.led.mode == "blink" ? L.T("LED: blinking") : L.T("LED: steady"), 239f, y, 215f, 34f, () =>
                {
                    g.led.mode = g.led.mode == "blink" ? "solid" : "blink";
                    Changed();
                }, 18f);
                y += 42f;
                BenchUi.Button(_panel, g.direction == "front" ? L.T("Pass: front only") : L.T("Pass: any direction"), 16f, y, 215f, 34f, () =>
                {
                    g.direction = g.direction == "front" ? "any" : "front";
                    Changed();
                }, 18f);
                BenchUi.Button(_panel, "< 15°", 239f, y, 104f, 34f, () => Turn(-15f), 18f);
                BenchUi.Button(_panel, "15° >", 350f, y, 104f, 34f, () => Turn(15f), 18f);
                y += 42f;
                BenchUi.Button(_panel, L.T("Duplicate"), 16f, y, 140f, 34f, Duplicate, 18f);
                BenchUi.Button(_panel, L.T("Delete"), 165f, y, 140f, 34f, DeleteSelected, 18f);
                BenchUi.Button(_panel, L.T("Earlier"), 314f, y, 66f, 34f, () => Reorder(-1), 16f);
                BenchUi.Button(_panel, L.T("Later"), 388f, y, 66f, 34f, () => Reorder(1), 16f);
                y += 48f;
            }
            else
            {
                string what = _selected == -2 ? L.T("Start pad selected: drag to move, Q / E to turn") : L.T("Click a gate or the start pad. N: new gate at the pointer");
                BenchUi.Text(_panel, what, 16f, y, 438f, 28f, 17f, BenchUi.Dim);
                y += 40f;
            }

            // Generator.
            BenchUi.Text(_panel, L.T("Random track"), 16f, y + 4f, 150f, 28f, 18f, BenchUi.Accent);
            BenchUi.Button(_panel, L.F("Seed {0}", _seed), 150f, y, 120f, 34f, () => { _seed = _seed % 999 + 1; BuildPanel(); }, 18f);
            BenchUi.Button(_panel, L.F("Level {0}", _difficulty), 276f, y, 86f, 34f, () => { _difficulty = _difficulty % 3 + 1; BuildPanel(); }, 18f);
            BenchUi.Button(_panel, L.T("Make"), 368f, y, 86f, 34f, Generate, 18f);
            y += 48f;

            // Checks.
            int errors = _issues.FindAll(i => i.Error).Count;
            BenchUi.Text(_panel, errors == 0 ? L.F("Checks passed ({0} gates)", _track.gates.Count) : L.F("{0} problem(s) to fix", errors), 16f, y, 438f, 28f, 19f,
                errors == 0 ? new Color(0.35f, 0.9f, 0.45f) : new Color(1f, 0.4f, 0.35f));
            y += 28f;
            for (int i = 0; i < _issues.Count && i < 7; i++, y += 22f)
                BenchUi.Text(_panel, _issues[i].ToString(), 16f, y, 438f, 22f, 14f, _issues[i].Error ? new Color(1f, 0.55f, 0.5f) : BenchUi.Dim);
            y += 8f;

            BenchUi.Button(_panel, L.T("Test flight (oracle pilot)"), 16f, y, 438f, 38f, TestFly, 19f);
            y += 44f;
            BenchUi.Button(_panel, L.T("Save"), 16f, y, 140f, 38f, () => Save(true), 19f);
            BenchUi.Button(_panel, L.T("Export map"), 165f, y, 140f, 38f, ExportMap, 19f);
            BenchUi.Button(_panel, L.T("Fly it"), 314f, y, 140f, 38f, FlyIt, 19f);
            y += 46f;
            BenchUi.Text(_panel, _message, 16f, y, 438f, 70f, 15f, BenchUi.Dim).textWrappingMode = TextWrappingModes.Normal;
        }

        private static TMP_InputField InputField(Transform parent, string value, float x, float y, float w, float h, Action<string> onEnd)
        {
            RectTransform rt = BenchUi.Panel(parent, "Name", x, y, w, h, new Color(0.14f, 0.15f, 0.19f, 1f));
            var field = rt.gameObject.AddComponent<TMP_InputField>();
            RectTransform area = BenchUi.Rect(rt, "Text Area", 8f, 2f, w - 16f, h - 4f);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = BenchUi.Text(area, "", 0f, 0f, w - 16f, h - 4f, 20f, Color.white, TextAlignmentOptions.MidlineLeft);
            text.raycastTarget = true;
            field.textViewport = area;
            field.textComponent = text;
            field.targetGraphic = rt.GetComponent<Image>();
            field.characterLimit = 40;
            field.text = value;
            field.onEndEdit.AddListener(v => onEnd(v));
            return field;
        }

        private static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length == 0 ? "track" : sb.ToString();
        }

        // ---------- edits ----------

        private void Changed()
        {
            _dirty = true;
            Rebuild();
        }

        private void Turn(float degrees)
        {
            if (_selected >= 0) _track.gates[_selected].yaw = Mathf.Repeat(_track.gates[_selected].yaw + degrees + 180f, 360f) - 180f;
            else if (_selected == -2) _track.start.yaw = Mathf.Repeat(_track.start.yaw + degrees + 180f, 360f) - 180f;
            else return;
            Changed();
        }

        private string FreeId()
        {
            for (int n = 1; ; n++)
            {
                string id = "G" + n;
                if (!_track.gates.Exists(g => g.id == id)) return id;
            }
        }

        private void AddGate(Vector3 at, float yaw, GateLed like)
        {
            _track.gates.Add(new TrackGate
            {
                id = FreeId(), position = new[] { Snap(at.x), 0f, Snap(at.z) }, yaw = yaw,
                led = new GateLed { color = like != null ? like.color : Palette[0], brightness = like != null ? like.brightness : 1f, mode = like != null ? like.mode : "solid" },
                direction = "any",
            });
            _selected = _track.gates.Count - 1;
            Changed();
        }

        private void Duplicate()
        {
            if (_selected < 0) return;
            TrackGate g = _track.gates[_selected];
            Vector2 n = TrackValidator.Forward(g.yaw);
            AddGate(new Vector3(g.position[0] + n.x * 4f, 0f, g.position[2] + n.y * 4f), g.yaw, g.led);
        }

        private void DeleteSelected()
        {
            if (_selected < 0) return;
            _track.gates.RemoveAt(_selected);
            _selected = -1;
            Changed();
        }

        private void Reorder(int by)
        {
            int to = _selected + by;
            if (_selected < 0 || to < 0 || to >= _track.gates.Count) return;
            (_track.gates[_selected], _track.gates[to]) = (_track.gates[to], _track.gates[_selected]);
            _selected = to;
            Changed();
        }

        private void Generate()
        {
            try
            {
                BenchTrack made = TrackGenerator.Generate(_track.venue, _seed, _difficulty);
                made.lighting = _track.lighting;
                _session.SetEditedTrack(made);
                _track = made;
                _selected = -1;
                _message = L.F("Generated: {0}", made.id);
                Changed();
            }
            catch (Exception e) { _message = e.Message; BuildPanel(); }
        }

        private void SwitchVenue()
        {
            _track.venue = _track.venue == "hall" ? "parking" : "hall";
            string path = Save(false);
            if (path != null) BenchLauncher.Edit(path);
        }

        private static float Snap(float v) => Mathf.Round(v * 4f) / 4f;

        // ---------- test flight, save, map ----------

        private void TestFly()
        {
            if (TrackValidator.HasErrors(_issues))
            {
                _message = L.T("Fix the problems first.");
                BuildPanel();
                return;
            }
            Physics.SyncTransforms();
            OracleResult r = BenchOracle.Fly(_track, _session.SolidLayers, _track.rules.time_limit);
            _path.positionCount = r.Path.Count;
            _path.SetPositions(r.Path.ToArray());
            var inv = CultureInfo.InvariantCulture;
            _message = r.Lap
                ? L.F("Oracle pilot: full lap in {0} s, {1} collision(s).", r.LapTime.ToString("F1", inv), r.Collisions)
                : L.F("Oracle pilot: NO full lap in {0} s ({1} of {2} gates).", _track.rules.time_limit.ToString("F0", inv), r.Points, _track.gates.Count);
            BuildPanel();
        }

        /// <summary>Writes the track into the user's track folder; returns the file's path.</summary>
        private string Save(bool report)
        {
            try
            {
                if (_name != null) { _track.id = Clean(_name.text); _track.name = _name.text; }
                if (Array.Exists(BenchLauncher.PublicTrackNames(), n => n == _track.id)) _track.id += "_edit";
                _track.author = string.IsNullOrEmpty(_track.author) || _track.author == "Ushqyr" || _track.author == "generator" ? "user" : _track.author;
                Directory.CreateDirectory(BenchLauncher.UserTracksFolder);
                string path = Path.Combine(BenchLauncher.UserTracksFolder, _track.id + BenchLauncher.TrackExtension);
                File.WriteAllText(path, _track.ToJson() + "\n");
                _dirty = false;
                if (report) { _message = L.F("Saved: {0}", path); BuildPanel(); }
                return path;
            }
            catch (Exception e)
            {
                _message = e.Message;
                BuildPanel();
                return null;
            }
        }

        private void FlyIt()
        {
            if (TrackValidator.HasErrors(_issues))
            {
                _message = L.T("Fix the problems first.");
                BuildPanel();
                return;
            }
            string path = Save(false);
            if (path != null) BenchLauncher.Launch(path);
        }

        /// <summary>A top-down picture of the whole venue with the gates numbered, next to the track file.</summary>
        private void ExportMap()
        {
            string trackPath = Save(false);
            if (trackPath == null) return;
            int width = 2048, height = Mathf.RoundToInt(2048f * _venue.Width / _venue.Length);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Vector3 keepPos = _camera.transform.position;
            Quaternion keepRot = _camera.transform.rotation;
            bool keepOrtho = _camera.orthographic;
            float keepSize = _camera.orthographicSize, keepNear = _camera.nearClipPlane, keepAspect = _camera.aspect;
            PlaceTop(Vector3.zero, _venue.Width * 0.5f + 0.5f);
            _camera.aspect = (float)width / height;
            _camera.targetTexture = rt;
            _camera.Render();
            _camera.targetTexture = null;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            string png = trackPath.Substring(0, trackPath.Length - BenchLauncher.TrackExtension.Length) + "_map.png";
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Destroy(tex);
            rt.Release();
            Destroy(rt);
            _camera.orthographic = keepOrtho;
            _camera.orthographicSize = keepSize;
            _camera.nearClipPlane = keepNear;
            _camera.ResetAspect();
            if (keepAspect > 0f) _camera.aspect = keepAspect;
            _camera.ResetAspect();
            _camera.transform.SetPositionAndRotation(keepPos, keepRot);
            _message = L.F("Map: {0}", png);
            BuildPanel();
        }

        // ---------- camera and mouse ----------

        private void PlaceTop(Vector3 focus, float halfHeight)
        {
            _camera.orthographic = true;
            _camera.orthographicSize = halfHeight;
            // Under the roof, looking straight down; the near plane cuts away beams and lamps.
            _camera.nearClipPlane = _venue.Height > 5f ? 0.85f : 0.3f;
            _camera.transform.SetPositionAndRotation(new Vector3(focus.x, _venue.Height - 0.05f, focus.z), Quaternion.Euler(90f, 0f, 0f));
        }

        private bool PointerOnFloor(out Vector3 point)
        {
            point = default;
            var mouse = Mouse.current;
            if (mouse == null) return false;
            Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f) return false;
            point = ray.origin + ray.direction * t;
            return true;
        }

        private void Update()
        {
            if (_track == null || _camera == null) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || kb == null) return;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool typing = _name != null && _name.isFocused;

            // View.
            if (!typing && kb.vKey.wasPressedThisFrame) { _top = !_top; BuildPanel(); }
            float scroll = overUi ? 0f : mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) _zoom = Mathf.Clamp(_zoom * (scroll > 0f ? 0.88f : 1.14f), 2.5f, 40f);
            Vector2 delta = mouse.delta.ReadValue();
            if (mouse.rightButton.isPressed && !overUi)
            {
                if (_top) _focus += new Vector3(-delta.x, 0f, -delta.y) * (_zoom * 2f / Screen.height);
                else
                {
                    _orbitYaw += delta.x * 0.2f;
                    _orbitPitch = Mathf.Clamp(_orbitPitch - delta.y * 0.2f, 12f, 85f);
                }
            }
            if (_top) PlaceTop(_focus, _zoom);
            else
            {
                _camera.orthographic = false;
                _camera.nearClipPlane = 0.05f;
                Quaternion rot = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
                Vector3 centre = _selected >= 0 ? BenchTrack.V(_track.gates[_selected].position) : _focus;
                Vector3 pos = centre + Vector3.up * 1.2f - rot * Vector3.forward * Mathf.Min(_zoom * 1.2f, 30f);
                pos.y = Mathf.Min(pos.y, _venue.Height - 0.3f);
                _camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(centre + Vector3.up * 1.2f - pos, Vector3.up));
            }

            if (typing) return;
            bool hasPoint = PointerOnFloor(out Vector3 point);

            // Select and drag.
            if (mouse.leftButton.wasPressedThisFrame && !overUi && hasPoint)
            {
                int pick = -1;
                float best = 1.6f;
                for (int i = 0; i < _track.gates.Count; i++)
                {
                    float d = Vector3.Distance(point, BenchTrack.V(_track.gates[i].position));
                    if (d < best) { best = d; pick = i; }
                }
                if (pick < 0 && Vector3.Distance(point, BenchTrack.V(_track.start.position)) < 0.8f) pick = -2;
                if (pick != _selected) { _selected = pick; Rebuild(); }
                _dragging = pick != -1;
                if (_dragging) _dragOffset = (pick >= 0 ? BenchTrack.V(_track.gates[pick].position) : BenchTrack.V(_track.start.position)) - point;
            }
            if (_dragging && mouse.leftButton.isPressed && hasPoint)
            {
                Vector3 p = point + _dragOffset;
                bool free = kb.leftAltKey.isPressed;
                float x = free ? p.x : Snap(p.x), z = free ? p.z : Snap(p.z);
                float[] target = _selected >= 0 ? _track.gates[_selected].position : _track.start.position;
                if (Mathf.Abs(target[0] - x) > 1e-4f || Mathf.Abs(target[2] - z) > 1e-4f)
                {
                    target[0] = x;
                    target[2] = z;
                    // Move the object now; the full rebuild (checks, panel) waits for the button to come up.
                    if (_selected >= 0) _gates[_selected].transform.position = new Vector3(x, 0f, z);
                    else _session.Spawn.position = new Vector3(x, 0.047f, z);
                    _dirty = true;
                }
            }
            if (_dragging && mouse.leftButton.wasReleasedThisFrame)
            {
                _dragging = false;
                Rebuild();
            }

            // Keys.
            float turn = kb.leftShiftKey.isPressed ? 5f : 15f;
            if (kb.qKey.wasPressedThisFrame) Turn(-turn);
            if (kb.eKey.wasPressedThisFrame) Turn(turn);
            if (kb.deleteKey.wasPressedThisFrame) DeleteSelected();
            if (kb.dKey.wasPressedThisFrame && kb.leftCtrlKey.isPressed) Duplicate();
            if (kb.nKey.wasPressedThisFrame && hasPoint && !overUi)
                AddGate(point, _selected >= 0 ? _track.gates[_selected].yaw : 0f, _selected >= 0 ? _track.gates[_selected].led : null);
        }
    }
}
#endif
