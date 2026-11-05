using System.Collections.Generic;
using System.Text;
using TMPro;
using Ushqyr.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Ushqyr.Strike
{
    /// <summary>
    /// Strike Range HUD (P5), built in code on its own overlay canvas: timer, score and streak, the last scoring event,
    /// live standings, a radar of the targets around the quad (north up), off-screen markers to the nearest targets,
    /// the 3-2-1, and the end-of-round scoreboard (hits per class, accuracy, best streak, local leaderboard).
    /// </summary>
    public sealed class StrikeHud : MonoBehaviour
    {
        public const float RadarRange = 600f;
        private const int Markers = 3;

        private StrikeSession _session;
        private Transform _quad;
        private Camera _camera;
        private TMP_Text _timer, _score, _event, _standings, _centre, _results;
        private RectTransform _radar;
        private readonly List<Image> _dots = new List<Image>();
        private Image _self;
        private readonly RectTransform[] _markers = new RectTransform[Markers];
        private readonly TMP_Text[] _markerText = new TMP_Text[Markers];
        private GameObject _resultsPanel;
        private TMP_FontAsset _font;
        private float _refresh;

        public void Configure(StrikeSession session, Transform quad, Camera camera, TMP_FontAsset font)
        {
            _session = session;
            _quad = quad;
            _camera = camera;
            _font = font;
            if (_timer == null) Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _timer = Text("Timer", new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(400f, 90f), 64f, TextAlignmentOptions.Center);
            _score = Text("Score", new Vector2(0f, 1f), new Vector2(40f, -150f), new Vector2(520f, 120f), 40f, TextAlignmentOptions.TopLeft);
            _event = Text("Event", new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(900f, 80f), 52f, TextAlignmentOptions.Center);
            _standings = Text("Standings", new Vector2(1f, 1f), new Vector2(-40f, -150f), new Vector2(460f, 260f), 30f, TextAlignmentOptions.TopRight);
            _centre = Text("Centre", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 220f), 160f, TextAlignmentOptions.Center);

            // Radar, bottom right.
            var radarGo = new GameObject("Radar", typeof(RectTransform), typeof(Image));
            radarGo.transform.SetParent(transform, false);
            _radar = (RectTransform)radarGo.transform;
            _radar.anchorMin = _radar.anchorMax = _radar.pivot = new Vector2(1f, 0f);
            _radar.anchoredPosition = new Vector2(-40f, 110f); // above the OSD speed / altitude line
            _radar.sizeDelta = new Vector2(300f, 300f);
            radarGo.GetComponent<Image>().color = new Color(0.02f, 0.06f, 0.04f, 0.6f);
            _self = Dot(new Color(1f, 1f, 1f), 12f);

            for (int i = 0; i < Markers; i++)
            {
                var go = new GameObject("Marker " + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                _markers[i] = (RectTransform)go.transform;
                _markers[i].sizeDelta = new Vector2(34f, 34f);
                go.GetComponent<Image>().color = new Color(1f, 0.55f, 0.1f, 0.9f);
                _markerText[i] = Text("Marker Distance", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 40f), 24f, TextAlignmentOptions.Center);
                _markerText[i].transform.SetParent(go.transform, false);
                ((RectTransform)_markerText[i].transform).anchoredPosition = new Vector2(0f, -36f);
            }

            _resultsPanel = new GameObject("Results", typeof(RectTransform), typeof(Image));
            _resultsPanel.transform.SetParent(transform, false);
            var rp = (RectTransform)_resultsPanel.transform;
            rp.anchorMin = rp.anchorMax = rp.pivot = new Vector2(0.5f, 0.5f);
            rp.sizeDelta = new Vector2(1100f, 760f);
            _resultsPanel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 0.88f);
            _results = Text("Results Text", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1020f, 700f), 32f, TextAlignmentOptions.TopLeft);
            _results.transform.SetParent(_resultsPanel.transform, false);
            _resultsPanel.SetActive(false);
        }

        private TMP_Text Text(string name, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.outlineWidth = 0.15f;
            t.outlineColor = new Color32(0, 0, 0, 200);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return t;
        }

        private Image Dot(Color c, float size)
        {
            var go = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_radar, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private void LateUpdate()
        {
            if (_session == null || _session.Director == null) return;
            var phase = _session.Phase;
            _timer.text = phase == StrikePhase.Round ? Clock(_session.TimeLeft) : "";
            var ps = _session.PlayerScore;
            _score.text = L.F("SCORE {0}", ps.Score) + "\n" + L.F("STREAK x{0:0.0}  HITS {1}", ps.Multiplier, ps.Hits);
            float since = Time.time - _session.LastEventTime;
            _event.text = since < 1.6f ? _session.LastEvent : "";
            _event.alpha = Mathf.Clamp01(1.6f - since);
            _centre.text = phase == StrikePhase.Countdown ? Mathf.CeilToInt(_session.CountdownLeft).ToString() : phase == StrikePhase.Round && _session.RoundTime < 0.8f ? L.T("GO!") : "";

            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f)
            {
                _refresh = 0.25f;
                var sb = new StringBuilder();
                int rank = 1;
                foreach (var s in _session.Standings())
                {
                    bool me = s == ps;
                    sb.Append(me ? "<b><color=#ffd24a>" : "").Append(rank++).Append(". ").Append(s.Pilot).Append("   ").Append(s.Score)
                        .Append(me ? "</color></b>" : "").Append('\n');
                }
                _standings.text = _session.Bots.Count > 0 ? sb.ToString() : "";
            }

            Radar();
            OffScreen();
            bool done = phase == StrikePhase.Finished;
            if (_resultsPanel.activeSelf != done)
            {
                _resultsPanel.SetActive(done);
                if (done) _results.text = Results();
            }
        }

        private void Radar()
        {
            var director = _session.Director;
            while (_dots.Count < director.Targets.Count) _dots.Add(Dot(Color.red, 10f));
            Vector3 c = _quad != null ? _quad.position : Vector3.zero;
            float half = _radar.sizeDelta.x * 0.5f;
            float t = _session.RoundTime;
            for (int i = 0; i < _dots.Count; i++)
            {
                bool show = i < director.Targets.Count && director.Targets[i].Active;
                _dots[i].enabled = show;
                if (!show) continue;
                director.Pose(i, t, out var p, out _, out _);
                Vector2 d = new Vector2(p.x - c.x, p.z - c.z) / RadarRange;
                if (d.sqrMagnitude > 1f) d.Normalize();
                ((RectTransform)_dots[i].transform).anchoredPosition = d * half;
                var cls = director.Targets[i].Plan.Class;
                _dots[i].color = cls == TargetClass.Static ? new Color(1f, 0.85f, 0.2f) : cls == TargetClass.Moving ? new Color(1f, 0.5f, 0.1f) : new Color(1f, 0.15f, 0.15f);
            }
        }

        // Arrows at the screen edge pointing to the three nearest targets that are off screen.
        private void OffScreen()
        {
            for (int i = 0; i < Markers; i++) _markers[i].gameObject.SetActive(false);
            if (_camera == null || _quad == null || _session.Phase != StrikePhase.Round) return;
            var director = _session.Director;
            float t = _session.RoundTime;
            var list = new List<(float d, Vector3 p)>();
            for (int i = 0; i < director.Targets.Count; i++)
            {
                if (!director.Targets[i].Active) continue;
                director.Pose(i, t, out var p, out _, out _);
                Vector3 vp = _camera.WorldToViewportPoint(p + Vector3.up * 1.5f);
                if (vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f) continue;
                list.Add((Vector3.Distance(_quad.position, p), p));
            }
            list.Sort((a, b) => a.d.CompareTo(b.d));
            var size = ((RectTransform)transform).rect.size;
            for (int i = 0; i < Markers && i < list.Count; i++)
            {
                Vector3 vp = _camera.WorldToViewportPoint(list[i].p + Vector3.up * 1.5f);
                Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                if (vp.z < 0f) dir = -dir;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
                dir.Normalize();
                float s = Mathf.Min(0.45f / Mathf.Max(Mathf.Abs(dir.x), 1e-3f), 0.42f / Mathf.Max(Mathf.Abs(dir.y), 1e-3f));
                Vector2 edge = new Vector2(0.5f, 0.5f) + dir * s;
                var m = _markers[i];
                m.gameObject.SetActive(true);
                m.anchorMin = m.anchorMax = new Vector2(0.5f, 0.5f);
                m.anchoredPosition = new Vector2((edge.x - 0.5f) * size.x, (edge.y - 0.5f) * size.y);
                m.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                _markerText[i].text = Mathf.RoundToInt(list[i].d) + " m";
                _markerText[i].transform.rotation = Quaternion.identity;
            }
        }

        private string Results()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<size=130%><b>" + L.T("ROUND OVER") + "</b></size>");
            sb.AppendLine();
            int rank = 1;
            foreach (var s in _session.Standings())
            {
                bool me = s == _session.PlayerScore;
                sb.AppendLine((me ? "<color=#ffd24a>" : "") + Inv($"{rank++}. {s.Pilot}   {s.Score}   ") +
                              L.F("hits {0} ({1} / {2} / {3})   accuracy {4:0}%   best streak {5}", s.Hits, s.HitsByClass[0], s.HitsByClass[1], s.HitsByClass[2], s.Accuracy * 100f, s.BestStreak) +
                              (me ? "</color>" : ""));
            }
            sb.AppendLine();
            sb.AppendLine("<b>" + L.F("Best rounds ({0})", L.T(_session.Options.Difficulty.ToString())) + "</b>" + (_session.LastRank > 0 ? "   " + L.F("you are #{0}", _session.LastRank) : ""));
            int n = 0;
            foreach (var e in _session.Board)
            {
                if (n++ >= 5) break;
                sb.AppendLine(Inv($"{n}. {e.Score}   {e.Pilot}   ") + L.F("hits {0}", e.Hits) + "   " + e.Date);
            }
            sb.AppendLine();
            sb.AppendLine("<size=80%>" + L.T("New round: T  /  gamepad L-stick click     Menu: Esc") + "</size>");
            return sb.ToString();
        }

        private static string Clock(float s)
        {
            int t = Mathf.CeilToInt(Mathf.Max(0f, s));
            return $"{t / 60}:{t % 60:00}";
        }

        private static string Inv(System.FormattableString s) => System.FormattableString.Invariant(s);
    }
}
