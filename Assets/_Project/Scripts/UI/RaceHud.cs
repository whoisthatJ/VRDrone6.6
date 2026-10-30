using System.Text;
using GateRush.Race;
using TMPro;
using UnityEngine;

namespace GateRush.UI
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
        [SerializeField] private GameObject resultsPanel;
        [SerializeField] private TMP_Text resultsText;
        [SerializeField] private float lapFlashSeconds = 2f;
        [SerializeField] private float goSeconds = 1f;

        private enum CentreMode { None, ArmToStart, Countdown, Go, Hold, MissedGate, Lap, Finish }

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

        private void OnPhaseChanged(RacePhase phase)
        {
            if (resultsPanel != null) resultsPanel.SetActive(phase == RacePhase.Finished);
            if (phase == RacePhase.Finished && resultsText != null) resultsText.SetText(BuildResults());
        }

        private void LateUpdate()
        {
            if (session == null) return;
            var timer = session.Timer;
            double now = session.Now;

            if (lapInfo != null)
            {
                float best = timer.HasBestLap ? (float)timer.BestLap : 0f;
                lapInfo.SetText("LAP {0:0}/{1:0}   {2:2}\nTOTAL {3:2}   BEST {4:2}",
                    timer.CurrentLap, timer.TotalLaps, (float)timer.CurrentLapTime(now), (float)timer.TotalTime(now), best);
            }

            if (centre == null) return;
            switch (session.Phase)
            {
                case RacePhase.Idle:
                    SetCentre(CentreMode.ArmToStart, "ARM TO START");
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
                    if (session.WaitingForRelease) SetCentre(CentreMode.Hold, "ARM + THROTTLE UP");
                    else if (now < session.WrongGateUntil) SetCentre(CentreMode.MissedGate, "MISSED GATE");
                    else if (now < _lapFlashUntil) ShowLapFlash();
                    else if (now - timer.StartTime < goSeconds) SetCentre(CentreMode.Go, "GO!");
                    else SetCentre(CentreMode.None, "");
                    break;
                case RacePhase.Finished:
                    SetCentre(CentreMode.Finish, "FINISH");
                    break;
            }
        }

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
            sb.AppendLine("<b>RESULTS</b>");
            for (int i = 0; i < t.CompletedLaps; i++)
                sb.AppendLine(Inv($"Lap {i + 1}   {t.LapTime(i):F2}{(i == t.BestLapIndex ? "  <color=#5f5>best</color>" : "")}"));
            sb.AppendLine(Inv($"Total   <b>{t.TotalTime(0):F2}</b>"));
            sb.AppendLine(session.LastRank > 0 ? $"Leaderboard #{session.LastRank}" : "Not in the top 10");
            if (session.LastRaceSetTrackBestLap) sb.AppendLine("<color=#5f5>New track lap record!</color>");
            sb.AppendLine();

            var board = session.Board;
            sb.AppendLine(Inv($"<b>{session.TrackName}</b>   lap record {board.BestLap:F2} ({board.BestLapPilot})"));
            int shown = Mathf.Min(5, board.Entries.Count);
            for (int i = 0; i < shown; i++)
            {
                var e = board.Entries[i];
                sb.AppendLine(Inv($"{i + 1}. {e.TotalTime:F2}   best {e.BestLap:F2}   {e.Pilot}   {e.Quad}   {e.Date}"));
            }
            sb.AppendLine();
            sb.AppendLine("<size=80%>Restart: T  /  gamepad L-stick click  /  left Touch stick click</size>");
            return sb.ToString();
        }

        // Same decimal point as the TMP numeric readouts, whatever the device locale.
        private static string Inv(System.FormattableString s) => System.FormattableString.Invariant(s);
    }
}
