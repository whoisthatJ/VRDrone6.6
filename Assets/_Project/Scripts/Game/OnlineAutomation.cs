using System;
using System.Globalization;
using GateRush.Net;
using GateRush.Race;
using UnityEngine;

namespace GateRush.Game
{
    /// <summary>
    /// Command-line driven online race for smoke tests with two copies of the build on one PC:
    /// <c>-online-host TRACK -online-autostart N</c> hosts and starts once N pilots are in,
    /// <c>-online-join-any</c> joins the first open race, <c>-online-autopilot</c> flies the quad, and
    /// <c>-online-quit</c> closes the game after the race (the host waits for everyone), <c>-online-quad ID</c> picks the
    /// quad (default the stock racer the autopilot is tuned for), <c>-online-laps N</c> sets the laps. Use a different
    /// <c>-online-profile</c> per copy. Inactive without these arguments.
    /// </summary>
    public sealed class OnlineAutomation : MonoBehaviour
    {
        private string _hostTrack;
        private bool _joinAny, _quit;
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
            if (host == null && !join) return;
            var go = new GameObject("Online Automation");
            DontDestroyOnLoad(go);
            var auto = go.AddComponent<OnlineAutomation>();
            auto._hostTrack = host;
            auto._joinAny = join;
            auto._autostart = int.Parse(BenchmarkRunner.Arg(args, "-online-autostart", "2"), CultureInfo.InvariantCulture);
            OnlineFlow.Laps = int.Parse(BenchmarkRunner.Arg(args, "-online-laps", "0"), CultureInfo.InvariantCulture);
            auto._quit = Array.Exists(args, a => string.Equals(a, "-online-quit", StringComparison.OrdinalIgnoreCase));
            // The autopilot is tuned for the stock racer: fly that (in memory only, the saved choice is untouched).
            string quad = BenchmarkRunner.Arg(args, "-online-quad", "race5");
            if (!string.IsNullOrEmpty(quad)) GateRush.Core.SaveSystem.Settings.QuadId = quad;
        }

        private async void Start()
        {
            Log("starting " + (_hostTrack != null ? "host on " + _hostTrack : "join-any"));
            bool ok;
            if (_hostTrack != null)
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

        private static void Log(string message) => Debug.Log("[Online] " + message);
    }
}
