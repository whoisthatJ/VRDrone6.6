using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GateRush.Core;
using GateRush.Game;
using GateRush.Net;
using GateRush.Race;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GateRush.UI
{
    /// <summary>
    /// Online race overlay: live positions, name tags over the other quads, "waiting for pilots" while everyone
    /// loads, and the race results. The host ends the race from the results ([Enter] back to the lobby, [F] race the
    /// same track again). Installs itself in track scenes loaded for an online race.
    /// </summary>
    public sealed class OnlineRaceHud : MonoBehaviour
    {
        private TMP_Text _standings, _centre, _results;
        private RectTransform _canvas, _tagRoot;
        private readonly Dictionary<ulong, TMP_Text> _tags = new Dictionary<ulong, TMP_Text>();
        private readonly StringBuilder _sb = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!NetRace.InRace || Object.FindAnyObjectByType<RaceSession>() == null) return;
            new GameObject("Online Race HUD").AddComponent<OnlineRaceHud>();
        }

        private void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            _canvas = (RectTransform)transform;

            _tagRoot = UiKit.Fill(_canvas, "Name tags");
            var panel = UiKit.Rect(_canvas, "Standings", Vector2.zero, new Vector2(430f, 420f));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-24f, -24f);
            _standings = UiKit.Label(panel, "", Vector2.zero, new Vector2(430f, 420f), 28f, TextAlignmentOptions.TopRight);
            _standings.textWrappingMode = TextWrappingModes.NoWrap;

            _centre = UiKit.Label(_canvas, "", Vector2.zero, new Vector2(1200f, 80f), 40f, TextAlignmentOptions.Center, UiKit.AccentColor);
            var crt = _centre.rectTransform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(0f, -170f);

            var box = UiKit.Rect(_canvas, "Online results", Vector2.zero, new Vector2(760f, 560f));
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(1f, 0.5f);
            box.anchoredPosition = new Vector2(-24f, -40f);
            UiKit.Image(box, new Color(0.05f, 0.06f, 0.09f, 0.82f));
            _results = UiKit.Label(box, "", new Vector2(28f, -24f), new Vector2(704f, 512f), 30f, TextAlignmentOptions.TopLeft);
            _results.textWrappingMode = TextWrappingModes.NoWrap;
            box.gameObject.SetActive(false);
        }

        private void Update()
        {
            var net = NetRace.Instance;
            var driver = OnlineRaceDriver.Current;
            if (net == null || !NetRace.Active)
            {
                Destroy(gameObject);
                return;
            }
            var race = driver != null ? driver.Race : null;
            var order = OnlineRules.Standings(net.Pilots.Values);

            _centre.text = net.Phase == OnlinePhase.Loading ? L.T("Waiting for all pilots to reach the grid...") : "";
            UpdateStandings(net, order);
            UpdateTags(net, driver);

            bool done = race != null && race.Phase == RacePhase.Finished;
            _results.transform.parent.gameObject.SetActive(done);
            if (done) UpdateResults(net, order);
            if (done && net.IsHost) HostKeys(net);
        }

        private void UpdateStandings(NetRace net, List<NetPilot> order)
        {
            _sb.Clear();
            int me = order.FindIndex(p => p.ClientId == net.LocalClientId);
            if (me >= 0) _sb.Append("<size=56><color=#fa3>P").Append(me + 1).Append("</color></size><size=34>/").Append(order.Count).Append("</size>\n");
            for (int i = 0; i < order.Count; i++)
            {
                var p = order[i];
                bool isMe = p.ClientId == net.LocalClientId;
                _sb.Append(isMe ? "<color=#fa3>" : "<color=#fff>").Append(i + 1).Append(". ").Append(p.Name);
                _sb.Append(p.Finished ? "  <color=#8f8>" + L.T("FIN") + "</color>" : "  <color=#aab>" + L.F("L{0}", Mathf.Max(1, p.Lap)) + "</color>").Append("</color>\n");
            }
            _standings.text = _sb.ToString();
        }

        private void UpdateResults(NetRace net, List<NetPilot> order)
        {
            _sb.Clear();
            _sb.Append("<size=40><color=#fa3>").Append(L.T("ONLINE RESULTS")).Append("</color></size>\n\n");
            for (int i = 0; i < order.Count; i++)
            {
                var p = order[i];
                string colour = p.ClientId == net.LocalClientId ? "#fa3" : "#fff";
                _sb.Append("<color=").Append(colour).Append(">").Append(i + 1).Append(". ").Append(p.Name).Append("</color>");
                _sb.Append("<pos=55%>").Append(p.Finished ? Time(p.TotalTime) : "<color=#aab>" + L.T("racing...") + "</color>");
                if (p.Finished && p.BestLap > 0f) _sb.Append("<pos=78%><color=#aab>").Append(L.F("lap {0}", Time(p.BestLap))).Append("</color>");
                _sb.Append('\n');
            }
            _sb.Append('\n');
            _sb.Append("<color=#aab>").Append(net.IsHost
                ? L.T("[Enter] back to the lobby    [F] race again")
                : L.T("Waiting for the host.  [Esc] menu")).Append("</color>");
            _results.text = _sb.ToString();
        }

        private static void HostKeys(NetRace net)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) OnlineFlow.HostBackToLobby();
            else if (kb.fKey.wasPressedThisFrame) OnlineFlow.HostStart(net.TrackId);
        }

        /// <summary>Callsigns floating over the other quads.</summary>
        private void UpdateTags(NetRace net, OnlineRaceDriver driver)
        {
            var cam = Camera.main;
            var seen = new HashSet<ulong>();
            if (driver != null && cam != null)
            {
                foreach (var view in driver.Views)
                {
                    if (view == null || view.Pilot == null) continue;
                    ulong id = view.Pilot.ClientId;
                    seen.Add(id);
                    if (!_tags.TryGetValue(id, out var tag))
                    {
                        tag = UiKit.Label(_tagRoot, "", Vector2.zero, new Vector2(300f, 40f), 24f, TextAlignmentOptions.Center);
                        tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = Vector2.zero;
                        tag.rectTransform.pivot = new Vector2(0.5f, 0f);
                        _tags[id] = tag;
                    }
                    Vector3 sp = cam.WorldToScreenPoint(view.transform.position + Vector3.up * 0.25f);
                    float distance = Vector3.Distance(cam.transform.position, view.transform.position);
                    bool show = view.Visible && sp.z > 0.1f && distance < 120f;
                    tag.gameObject.SetActive(show);
                    if (!show) continue;
                    tag.text = view.Pilot.Name;
                    tag.alpha = Mathf.Clamp01(1.4f - distance / 100f);
                    float scale = _canvas.rect.height / Mathf.Max(1f, Screen.height);
                    tag.rectTransform.anchoredPosition = new Vector2(sp.x * scale, sp.y * scale);
                }
            }
            var gone = new List<ulong>();
            foreach (var kv in _tags)
                if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (ulong id in gone)
            {
                Destroy(_tags[id].gameObject);
                _tags.Remove(id);
            }
        }

        private static string Time(float seconds) =>
            seconds >= 60f
                ? ((int)(seconds / 60f)).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60f).ToString("00.00", CultureInfo.InvariantCulture)
                : seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";
    }
}
