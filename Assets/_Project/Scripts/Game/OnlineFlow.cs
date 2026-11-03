using GateRush.Core;
using GateRush.Garage;
using GateRush.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GateRush.Game
{
    /// <summary>
    /// Connects the online race (<see cref="NetRace"/>, <see cref="OnlineLobby"/>) to the game's scenes: the host's
    /// "load this track" opens the track for everyone, "back to lobby" returns to the menu's lobby page, and a lost
    /// connection lands in the menu with a notice. Nothing here runs until the player goes online.
    /// </summary>
    public static class OnlineFlow
    {
        /// <summary>Message for the menu after leaving a race involuntarily ("" when none).</summary>
        public static string Notice { get; set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            NetRace.Created -= Hook;
            NetRace.Created += Hook;
            OnlineLobby.Ended -= OnEnded;
            OnlineLobby.Ended += OnEnded;
            Notice = "";
        }

        private static void Hook(NetRace net)
        {
            PrepareLocalPilot();
            net.LoadRequested += OnLoad;
            net.ReturnedToLobby += OnLobby;
            net.Disconnected += OnDisconnected;
        }

        /// <summary>Callsign and quad this PC races with (call before creating or joining a race).</summary>
        public static void PrepareLocalPilot()
        {
            var net = NetRace.Instance;
            if (net == null) return;
            var s = SaveSystem.Settings;
            net.LocalName = string.IsNullOrWhiteSpace(s.PilotName) ? "Pilot" : s.PilotName;
            net.LocalBuildJson = LocalBuild(s.QuadId).ToJson();
        }

        /// <summary>The build for a quad id: a garage build, or the preset matching a stock quad.</summary>
        public static QuadBuild LocalBuild(string quadId)
        {
            if (BuildLibrary.IsBuildId(quadId))
            {
                var b = BuildLibrary.Default.Resolve(quadId);
                if (b != null && BuildComposer.Validate(b).Count == 0) return b;
            }
            return quadId switch
            {
                "freestyle5" => QuadPresets.Freestyle5,
                "whoop65" => QuadPresets.Whoop65,
                _ => QuadPresets.Race5,
            };
        }

        /// <summary>Laps the host picked in the lobby; 0 = the track's own.</summary>
        public static int Laps { get; set; }

        /// <summary>Host: everyone loads the lobby's track.</summary>
        public static void HostStart(string trackId)
        {
            var net = NetRace.Instance;
            if (net == null || !net.IsHost) return;
            _ = OnlineLobby.SetRacingAsync(true);
            net.StartRace(trackId, Laps);
        }

        /// <summary>Host: everyone back to the lobby page (the race is listed again).</summary>
        public static void HostBackToLobby()
        {
            var net = NetRace.Instance;
            if (net == null || !net.IsHost) return;
            _ = OnlineLobby.SetRacingAsync(false);
            net.BackToLobby();
        }

        /// <summary>Leaves the session and goes to the menu.</summary>
        public static void Leave()
        {
            _ = OnlineLobby.LeaveAsync();
            if (!InMenu) GameFlow.Menu();
        }

        private static bool InMenu => SceneManager.GetActiveScene().name == GameFlow.MenuScene;

        private static void OnLoad(string trackId)
        {
            var catalog = GameFlow.Catalog;
            if (catalog == null || !GameFlow.RaceOnline(catalog, trackId))
            {
                Notice = "Could not load the race track '" + trackId + "'.";
                Leave();
            }
        }

        private static void OnLobby()
        {
            if (!InMenu) GameFlow.Menu();
        }

        private static void OnDisconnected(string reason)
        {
            if (!OnlineLobby.InSession && InMenu) return;
            Notice = reason;
            Leave();
        }

        private static void OnEnded(string reason)
        {
            Notice = reason;
            if (!InMenu) GameFlow.Menu();
        }
    }
}
