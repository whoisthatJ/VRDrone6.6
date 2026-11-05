using System.Text;
using Ushqyr.Core;
using Ushqyr.Race;
using TMPro;
using UnityEngine;

namespace Ushqyr.UI
{
    /// <summary>
    /// Race OSD: lap counter and times, big centre messages (countdown, GO, missed gate, lap times),
    /// and the results panel with the local leaderboard. Per-frame updates are allocation-free.
    /// </summary>
    public sealed class RaceHud : MonoBehaviour
    {

        [SerializeField] private RaceSession session;
        [SerializeField] private TMP_Text lapInfo;
        [SerializeField] private TMP_Text centre;
        [Tooltip("Full-screen red tint that flashes when a gate is missed.")]
        [SerializeField] private UnityEngine.UI.Image missedFlash;
        [SerializeField] private float missedFlashAlpha = 0.14f;
        [SerializeField] private GameObject resultsPanel;
        [SerializeField] private TMP_Text resultsText;
        [SerializeField] private float lapFlashSeconds = 2f;
        [SerializeField] private float goSeconds = 1f;

        private enum CentreMode { None, ArmToStart, Countdown, Go, Hold, MissedGate, Lap, Finish, Boundary, FreeFlight, RimTouched }

        private CentreMode _centreMode = (CentreMode)(-1);
        private int _shownCount = -1;
        private double _lapFlashUntil;
        private bool _lapFlashBest;

        private void OnEnable()
        {
            if (session == null) return;
            session.PhaseChanged += OnPhaseChanged;
            session.GatePassed += OnGatePassed;
        }

        private void OnDisable()
        {
            if (session == null) return;
            session.PhaseChanged -= OnPhaseChanged;
            session.GatePassed -= OnGatePassed;
        }

        private void Start() => OnPhaseChanged(session != null ? session.Phase : RacePhase.Idle);

        private void OnGatePassed(int gate, PassResult result)
        {
            if (result != PassResult.LapCompleted && result != PassResult.RaceFinished) return;
            _lapFlashUntil = session.Now + lapFlashSeconds;
            _lapFlashBest = session.LastLapWasPersonalBest;
        }

        // The T6 finish screen replaces the plain results panel for offline races.
        private bool FinishScreen => _finish != null && !session.ExternalStart;
        private FinishSequence _finish;

        private void Awake() => _finish = GetComponent<FinishSequence>();

        private void OnPhaseChanged(RacePhase phase)
        {
            if (resultsPanel != null) resultsPanel.SetActive(phase == RacePhase.Finished && !FinishScreen);
            if (phase == RacePhase.Finished && resultsText != null) resultsText.SetText(BuildResults());
        }

        private void LateUpdate()
        {
            // The race object can be inactive (track editor) before RaceSession.Awake has run.
            if (session == null || session.Timer == null) return;
            var area = Ushqyr.Game.FlightArea.Current;
            if (!session.isActiveAndEnabled)
            {
                FreeFlightHud(area);
                return;
            }
            var timer = session.Timer;
            double now = session.Now;

            Standings(now);

            if (lapInfo != null)
            {
                float best = timer.HasBestLap ? (float)timer.BestLap : 0f;
                lapInfo.SetText(L.T("LAP {0:0}/{1:0}   {2:2}\nTOTAL {3:2}   BEST {4:2}"),
                    timer.CurrentLap, timer.TotalLaps, (float)timer.CurrentLapTime(now), (float)timer.TotalTime(now), best);
            }

            if (missedFlash != null)
            {
                // Two quick pulses fading over the hint time.
                float left = (float)(session.WrongGateUntil - now);
                float a = left > 0f ? missedFlashAlpha * Mathf.Clamp01(left / 1.5f) * (0.6f + 0.4f * Mathf.Cos(left * 12f)) : 0f;
                var c = missedFlash.color;
                if (!Mathf.Approximately(c.a, a))
                {
                    c.a = a;
                    missedFlash.color = c;
                    missedFlash.enabled = a > 0.001f;
                }
            }

            if (centre == null) return;
            if (area != null && area.Warning)
            {
                SetCentre(CentreMode.Boundary, L.T("<color=#ff4a3d>LEAVING THE FLYING AREA - TURN BACK</color>"));
                return;
            }
            switch (session.Phase)
            {
                case RacePhase.Idle:
                    SetCentre(CentreMode.ArmToStart, L.T("ARM TO START"));
                    break;
                case RacePhase.Countdown:
                    int n = Mathf.CeilToInt(session.CountdownRemaining);
                    if (_centreMode != CentreMode.Countdown || n != _shownCount)
                    {
                        _centreMode = CentreMode.Countdown;
                        _shownCount = n;
                        centre.SetText("{0:0}", n);
                    }
                    break;
                case RacePhase.Racing:
                    if (session.WaitingForRelease) SetCentre(CentreMode.Hold, L.T("ARM + THROTTLE UP"));
                    else if (now < session.WrongGateUntil) SetCentre(CentreMode.MissedGate, L.T("<color=#ff4a3d>MISSED GATE - GO BACK</color>"));
                    else if (now < session.RimTouchedUntil) SetCentre(CentreMode.RimTouched, L.T("<color=#ff5ad8>RING TOUCHED +0.5 s</color>"));
                    else if (now < _lapFlashUntil) ShowLapFlash();
                    else if (now - timer.StartTime < goSeconds) SetCentre(CentreMode.Go, L.T("GO!"));
                    else SetCentre(CentreMode.None, "");
                    break;
                case RacePhase.Finished:
                    if (FinishScreen && _finish.ResultsShown) SetCentre(CentreMode.None, "");
                    else SetCentre(CentreMode.Finish, L.T("FINISH"));
                    break;
            }
        }

        // ---------- P4: race positions ----------

        private Ushqyr.AI.BotRace _bots;
        private TMP_Text _standings;
        private bool _botsLooked;
        private float _standingsTimer;

        private Ushqyr.AI.BotRace Bots
        {
            get
            {
                if (_bots == null && !_botsLooked)
                {
                    _bots = FindAnyObjectByType<Ushqyr.AI.BotRace>();
                    _botsLooked = _bots != null;
                }
                return _bots;
            }
        }

        /// <summary>Live positions and gaps (top right) while racing bots; refreshed 5 times a second.</summary>
        private void Standings(double now)
        {
            var bots = Bots;
            bool on = bots != null && bots.Active && lapInfo != null;
            if (!on)
            {
                if (_standings != null && _standings.gameObject.activeSelf) _standings.gameObject.SetActive(false);
                return;
            }
            if (_standings == null)
            {
                _standings = Instantiate(lapInfo, lapInfo.transform.parent);
                _standings.name = "Race Standings";
                var rt = _standings.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-30f, -30f);
                rt.sizeDelta = new Vector2(420f, 400f);
                _standings.alignment = TextAlignmentOptions.TopRight;
                _standings.fontSize = lapInfo.fontSize * 0.8f;
            }
            if (!_standings.gameObject.activeSelf) _standings.gameObject.SetActive(true);
            _standingsTimer -= Time.unscaledDeltaTime;
            if (_standingsTimer > 0f) return;
            _standingsTimer = 0.2f;
            var sb = new StringBuilder();
            foreach (var r in bots.Standings)
            {
                string colour = r.IsPlayer ? "#ffffff" : "#" + ColorUtility.ToHtmlStringRGB(r.Tint);
                string gap = r.Position == 1 ? "" : r.Gap > 0 ? Inv($"  +{r.Gap:F1}") : "";
                string mark = r.Finished ? "  <size=80%>" + L.T("FIN") + "</size>" : "";
                sb.Append(r.IsPlayer ? "<b>" : "").Append(r.Position).Append(". <color=").Append(colour).Append('>').Append(r.Name).Append("</color>")
                    .Append(gap).Append(mark).Append(r.IsPlayer ? "</b>" : "").Append('\n');
            }
            _standings.SetText(sb.ToString());
        }

        private void AppendRaceResults(StringBuilder sb)
        {
            var bots = Bots;
            if (bots == null || !bots.Active) return;
            sb.AppendLine("<b>" + L.T("RACE") + "</b>");
            foreach (var r in bots.Standings)
            {
                string time = r.Finished ? Inv($"{r.TotalTime:F2}") : L.T("racing");
                string line = Inv($"{r.Position}. ") + r.Name + "   " + time;
                sb.AppendLine(r.IsPlayer ? "<color=#5f5>" + line + "</color>" : line);
            }
            sb.AppendLine("<size=80%>" + L.T("Watch winner replay: R  /  gamepad Y") + "</size>");
            sb.AppendLine();
        }

        // E7 free flight: no timing, the spawn point and the keys instead of the lap counter.
        private void FreeFlightHud(Ushqyr.Game.FlightArea area)
        {
            if (lapInfo != null)
            {
                string spawn = HudFlags.HideFreeFlightInfo ? "-" : area != null ? L.T(area.SpawnName) : "";
                if (spawn != _shownSpawn && HudFlags.HideFreeFlightInfo)
                {
                    _shownSpawn = spawn;
                    lapInfo.SetText("");
                }
                else if (spawn != _shownSpawn)
                {
                    _shownSpawn = spawn;
                    lapInfo.SetText(L.F("FREE FLIGHT   {0}", spawn) + "\n" + L.T("F / D-pad up: next spawn   T: reset"));
                }
            }
            if (centre == null) return;
            if (area != null && area.Warning) SetCentre(CentreMode.Boundary, L.T("<color=#ff4a3d>LEAVING THE FLYING AREA - TURN BACK</color>"));
            else if (area != null && area.Holding) SetCentre(CentreMode.Hold, L.T("ARM + THROTTLE UP"));
            else SetCentre(CentreMode.FreeFlight, "");
        }

        private string _shownSpawn;

        private void ShowLapFlash()
        {
            if (_centreMode == CentreMode.Lap) return;
            _centreMode = CentreMode.Lap;
            centre.SetText(_lapFlashBest ? "<color=#5f5>{0:2}</color>" : "{0:2}", session.LastLapTime);
        }

        private void SetCentre(CentreMode mode, string text)
        {
            if (_centreMode == mode) return;
            _centreMode = mode;
            centre.SetText(text);
        }

        private string BuildResults()
        {
            var t = session.Timer;
            var sb = new StringBuilder();
            sb.AppendLine("<b>" + L.T("RESULTS") + "</b>");
            AppendRaceResults(sb);
            for (int i = 0; i < t.CompletedLaps; i++)
                sb.AppendLine(L.F("Lap {0}   {1:F2}", i + 1, t.LapTime(i)) + (i == t.BestLapIndex ? "  <color=#5f5>" + L.T("best") + "</color>" : ""));
            sb.AppendLine(L.F("Total   {0}", Inv($"<b>{t.TotalTime(0):F2}</b>")));
            sb.AppendLine(session.LastRank > 0 ? L.F("Leaderboard #{0}", session.LastRank) : L.T("Not in the top 10"));
            if (session.LastRaceSetTrackBestLap) sb.AppendLine("<color=#5f5>" + L.T("New track lap record!") + "</color>");
            sb.AppendLine();

            var board = session.Board;
            sb.AppendLine("<b>" + L.T(session.TrackName) + "</b>   " + L.F("lap record {0:F2} ({1})", board.BestLap, board.BestLapPilot));
            int shown = Mathf.Min(5, board.Entries.Count);
            for (int i = 0; i < shown; i++)
            {
                var e = board.Entries[i];
                sb.AppendLine(Inv($"{i + 1}. {e.TotalTime:F2}   ") + L.F("best {0:F2}", e.BestLap) + $"   {e.Pilot}   {L.T(e.Quad)}   {e.Date}");
            }
            sb.AppendLine();
            sb.AppendLine("<size=80%>" + L.T("Restart: T  /  gamepad L-stick click     Menu: Esc") + "</size>");
            return sb.ToString();
        }

        // Same decimal point as the TMP numeric readouts, whatever the device locale.
        private static string Inv(System.FormattableString s) => System.FormattableString.Invariant(s);
    }
}
