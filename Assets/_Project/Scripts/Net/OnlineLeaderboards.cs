using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace GateRush.Net
{
    /// <summary>One row of an online leaderboard.</summary>
    public struct OnlineScore
    {
        public int Rank;
        public string Pilot;
        public float LapTime;
        public bool IsMe;
    }

    /// <summary>
    /// Best-lap leaderboards per built-in track ("lap-" + track id, lowest time first, keeps each player's best). The
    /// boards are defined by the .lb files in Assets/_Project/Online/Leaderboards and deployed to the project's Unity
    /// Cloud environment. Posting only happens when the player turned it on in Settings > Pilot.
    /// </summary>
    public static class OnlineLeaderboards
    {
        public const string Prefix = "lap-";

        /// <summary>Tracks that have an online board (the built-in ones).</summary>
        public static readonly string[] Tracks = { "field", "hangar", "industrial", "spline-test", "stadium-oval", "serpent-arena" };

        public static string IdFor(string trackId) => Prefix + trackId;
        public static bool Has(string trackId) => Array.IndexOf(Tracks, trackId) >= 0;

        public static string LastError { get; private set; } = "";

        /// <summary>Posts a lap time (seconds); the board keeps it only if it beats this player's best.</summary>
        public static async Task<bool> PostLapAsync(string trackId, float lapSeconds, string quadName)
        {
            if (!Has(trackId) || lapSeconds <= 0f || float.IsInfinity(lapSeconds) || float.IsNaN(lapSeconds)) return false;
            if (!await OnlineServices.EnsureSignedInAsync())
            {
                LastError = OnlineServices.LastError;
                return false;
            }
            try
            {
                var options = new AddPlayerScoreOptions { Metadata = new Dictionary<string, string> { ["quad"] = quadName ?? "" } };
                await LeaderboardsService.Instance.AddPlayerScoreAsync(IdFor(trackId), Math.Round(lapSeconds, 3), options);
                LastError = "";
                Debug.Log($"[GateRush] Posted {lapSeconds:F3}s to the online leaderboard {IdFor(trackId)}");
                return true;
            }
            catch (Exception e)
            {
                LastError = OnlineServices.Describe(e);
                Debug.LogWarning("[GateRush] Posting the lap time failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Top <paramref name="count"/> laps, plus this player's row when outside them.</summary>
        public static async Task<List<OnlineScore>> TopAsync(string trackId, int count = 10)
        {
            var rows = new List<OnlineScore>();
            if (!await OnlineServices.EnsureSignedInAsync())
            {
                LastError = OnlineServices.LastError;
                return rows;
            }
            string me = OnlineServices.PlayerId;
            try
            {
                var page = await LeaderboardsService.Instance.GetScoresAsync(IdFor(trackId), new GetScoresOptions { Limit = count });
                bool meListed = false;
                foreach (var e in page.Results)
                {
                    bool isMe = e.PlayerId == me;
                    meListed |= isMe;
                    rows.Add(new OnlineScore { Rank = e.Rank + 1, Pilot = OnlineServices.DisplayName(e.PlayerName), LapTime = (float)e.Score, IsMe = isMe });
                }
                if (!meListed)
                {
                    try
                    {
                        var mine = await LeaderboardsService.Instance.GetPlayerScoreAsync(IdFor(trackId));
                        rows.Add(new OnlineScore { Rank = mine.Rank + 1, Pilot = OnlineServices.DisplayName(mine.PlayerName), LapTime = (float)mine.Score, IsMe = true });
                    }
                    catch (Exception)
                    {
                        // No lap posted yet.
                    }
                }
                LastError = "";
            }
            catch (Exception e)
            {
                LastError = OnlineServices.Describe(e);
            }
            return rows;
        }
    }
}
