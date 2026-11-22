using System;
using System.Globalization;
using Ushqyr.Net;
using Ushqyr.Race;
using UnityEngine;

namespace Ushqyr.Game
{
    /// <summary>
    /// Command-line driven online race for smoke tests with two copies of the build on one PC:
    /// <c>-online-host TRACK -online-autostart N</c> hosts and starts once N pilots are in,
    /// <c>-online-join-any</c> joins the first open race, <c>-online-autopilot</c> flies the quad, and
    /// <c>-online-quit</c> closes the game after the race (the host waits for everyone), <c>-online-quad ID</c> picks the
    /// quad (default the stock racer the autopilot is tuned for), <c>-online-laps N</c> sets the laps. Use a different
    /// <c>-online-profile</c> per copy. P6: <c>-lan-host TRACK</c> / <c>-lan-join ADDRESS[:PORT]</c> do the same over a
    /// direct LAN connection (no Unity services), and Strike Range rounds are logged and quit like races.
    /// Inactive without these arguments.
    /// </summary>
    public sealed class OnlineAutomation : MonoBehaviour
    {
        private string _hostTrack, _lanJoin;
        private bool _joinAny, _quit, _lan;
        private int _autostart;
        private bool _started;
        private float _finishedAt = -1f;
        private float _nextStatus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            string host = BenchmarkRunner.Arg(args, "-online-host", null);
            bool join = Array.Exists(args, a => string.Equals(a, "-online-join-any", StringComparison.OrdinalIgnoreCase));
            string lanHost = BenchmarkRunner.Arg(args, "-lan-host", null);
            string lanJoin = BenchmarkRunner.Arg(args, "-lan-join", null);
            if (host == null && !join && lanHost == null && lanJoin == null) return;
            var go = new GameObject("Online Automation");
            DontDestroyOnLoad(go);
            var auto = go.AddComponent<OnlineAutomation>();
            auto._hostTrack = host ?? lanHost;
            auto._joinAny = join;
            auto._lan = lanHost != null || lanJoin != null;
            auto._lanJoin = lanJoin;
            auto._autostart = int.Parse(BenchmarkRunner.Arg(args, "-online-autostart", "2"), CultureInfo.InvariantCulture);
            OnlineFlow.Laps = int.Parse(BenchmarkRunner.Arg(args, "-online-laps", "0"), CultureInfo.InvariantCulture);
            auto._quit = Array.Exists(args, a => string.Equals(a, "-online-quit", StringComparison.OrdinalIgnoreCase));
            // -online-fai: the race is started and timed by the FAI rules (the host's choice).
            OnlineFlow.Fai = Array.Exists(args, a => string.Equals(a, "-online-fai", StringComparison.OrdinalIgnoreCase));
            // The autopilot is tuned for the stock racer: fly that (in memory only, the saved choice is untouched).
            string quad = BenchmarkRunner.Arg(args, "-online-quad", "race5");
            if (!string.IsNullOrEmpty(quad)) Ushqyr.Core.SaveSystem.Settings.QuadId = quad;
        }

        private async void Start()
        {
            Log("starting " + (_hostTrack != null ? "host on " + _hostTrack : "join-any"));
            bool ok;
            if (_lan)
            {
                await System.Threading.Tasks.Task.Delay(1500); // let the menu set the catalogue up
                OnlineFlow.PrepareLocalPilot();
                if (_hostTrack != null) ok = OnlineLobby.HostDirect(_hostTrack, _hostTrack);
                else
                {
                    ok = false;
                    for (int attempt = 0; attempt < 15 && !ok; attempt++)
                    {
                        if (!OnlineLobby.InSession && !OnlineLobby.JoinDirect(_lanJoin)) break;
                        OnlineFlow.PrepareLocalPilot();
                        for (int w = 0; w < 20 && !ok; w++)
                        {
                            await System.Threading.Tasks.Task.Delay(250);
                            ok = NetRace.Active && NetRace.Instance.Local != null;
                        }
                        if (!ok) await OnlineLobby.LeaveAsync();
                    }
                }
            }
            else if (_hostTrack != null)
            {
                ok = await OnlineLobby.CreateAsync(_hostTrack, _hostTrack, false);
            }
            else
            {
                ok = false;
                for (int attempt = 0; attempt < 30 && !ok; attempt++)
                {
                    var races = await OnlineLobby.ListAsync();
                    if (races.Count > 0) ok = await OnlineLobby.JoinAsync(races[0].Id);
                    else await System.Threading.Tasks.Task.Delay(2000);
                }
            }
            Log(ok ? "in session " + OnlineLobby.Name + " code " + OnlineLobby.Code : "FAILED: " + OnlineLobby.LastError);
            if (!ok && _quit) Application.Quit(2);
        }

        private void Update()
        {
            var net = NetRace.Instance;
            if (net == null || !NetRace.Active) return;

            if (_hostTrack != null && !_started && net.IsHost && net.Pilots.Count >= _autostart)
            {
                _started = true;
                Log($"{net.Pilots.Count} pilots in, starting the race");
                OnlineFlow.HostStart(_hostTrack);
            }

            if (OnlineStrikeDriver.Current != null)
            {
                StrikeStatus(net);
                return;
            }
            var driver = OnlineRaceDriver.Current;
            if (driver == null || driver.Race == null) return;
            if (Time.unscaledTime >= _nextStatus)
            {
                _nextStatus = Time.unscaledTime + 5f;
                var r = driver.Race;
                var ap = FindAnyObjectByType<RaceAutopilot>();
                Log($"status net={net.Phase} race={r.Phase} lap={r.Timer.CurrentLap} next={r.Timer.NextCheckpoint} progress={r.Progress:F2} " +
                    $"countdown={r.CountdownRemaining:F1} rescues={(ap != null ? ap.Rescues : -1)} {(ap != null ? ap.LastReport : "")}");
            }
            if (driver.Race.Phase == RacePhase.Finished && _finishedAt < 0f)
            {
                _finishedAt = Time.unscaledTime;
                Log($"finished in {driver.Race.Timer.TotalTime(driver.Race.Now):F2}s");
            }
            if (!_quit || _finishedAt < 0f) return;
            bool everyone = true;
            foreach (var p in net.Pilots.Values) everyone &= p.Finished;
            float waited = Time.unscaledTime - _finishedAt;
            if ((everyone && waited > 3f) || waited > 240f)
            {
                var order = OnlineRules.Standings(net.Pilots.Values);
                for (int i = 0; i < order.Count; i++)
                    Log($"result {i + 1}. {order[i].Name} {(order[i].Finished ? order[i].TotalTime.ToString("F2", CultureInfo.InvariantCulture) + "s" : "DNF")}");
                Application.Quit(0);
            }
        }

        // P6: a Strike Range round, logged every 5 s; quits a few seconds after the round (the host last).
        private void StrikeStatus(NetRace net)
        {
            var session = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
            if (session == null) return;
            if (Time.unscaledTime >= _nextStatus)
            {
                _nextStatus = Time.unscaledTime + 5f;
                var sb = new System.Text.StringBuilder($"strike phase={session.Phase} t={session.RoundTime:F0} pilots={net.Pilots.Count} sent={net.ExtraBytesSent} recv={net.ExtraBytesReceived} |");
                foreach (var sc in session.Standings()) sb.Append($" {sc.Pilot} {sc.Score} h{sc.Hits} c{sc.Crashes};");
                int active = 0;
                if (session.Director != null) foreach (var t in session.Director.Targets) if (t.Active) active++;
                sb.Append($" targets up {active} gen-sum {GenerationSum(session)}");
                Log(sb.ToString());
            }
            if (session.Phase != Ushqyr.Strike.StrikePhase.Finished) return;
            if (_finishedAt < 0f)
            {
                _finishedAt = Time.unscaledTime;
                foreach (var sc in session.Standings()) Log($"result {sc.Pilot} {sc.Score} hits {sc.Hits} crashes {sc.Crashes}");
                Log($"targets gen-sum {GenerationSum(session)}");
            }
            if (_quit && Time.unscaledTime - _finishedAt > (net.IsHost ? 8f : 3f)) Application.Quit(0);
        }

        // A fingerprint of the target state: the sum of every slot's life count (equal on every PC when in sync).
        private static int GenerationSum(Ushqyr.Strike.StrikeSession session)
        {
            int sum = 0;
            if (session.Director != null) foreach (var t in session.Director.Targets) sum += t.Plan.Generation * 31 + (t.Active ? 1 : 0) + t.HitBy * 7;
            return sum;
        }

        private static void Log(string message) => Debug.Log("[Online] " + message);
    }
}
