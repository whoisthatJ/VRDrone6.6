using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ushqyr.Race
{
    [Serializable]
    public sealed class LeaderboardEntry
    {
        public string Pilot;
        public string Quad;
        /// <summary>Total race time (s).</summary>
        public float TotalTime;
        /// <summary>Best lap within that race (s).</summary>
        public float BestLap;
        /// <summary>ISO-8601 date.</summary>
        public string Date;
    }

    [Serializable]
    public sealed class TrackBoard
    {
        public string TrackId;
        public List<LeaderboardEntry> Entries = new List<LeaderboardEntry>();
        /// <summary>Fastest single lap ever on this track (0 = none).</summary>
        public float BestLap;
        public string BestLapPilot;
        public string BestLapQuad;
        /// <summary>Total times of every finished race here (newest last, capped), for "faster than N% of your races".</summary>
        public List<float> History = new List<float>();
    }

    [Serializable]
    public sealed class LeaderboardData
    {
        public List<TrackBoard> Tracks = new List<TrackBoard>();
    }

    /// <summary>Local (offline) leaderboard: top N races per track by total time, plus the track's best lap. JSON on disk.</summary>
    public sealed class LocalLeaderboard
    {
        public const int MaxEntries = 10;
        public const int MaxHistory = 500;

        public LeaderboardData Data { get; private set; } = new LeaderboardData();

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "leaderboard.json");

        public TrackBoard Board(string trackId)
        {
            foreach (var t in Data.Tracks)
                if (t.TrackId == trackId) return t;
            var board = new TrackBoard { TrackId = trackId };
            Data.Tracks.Add(board);
            return board;
        }

        /// <summary>Adds a finished race. Returns its 1-based rank, or -1 if it didn't make the top <see cref="MaxEntries"/>.</summary>
        public int Submit(string trackId, LeaderboardEntry entry, out bool newBestLap) => Submit(trackId, entry, out newBestLap, out _);

        /// <summary>
        /// As <see cref="Submit(string, LeaderboardEntry, out bool)"/>; <paramref name="percentile"/> is the share (0–100)
        /// of the earlier races on this track that were slower, or -1 when this is the first one.
        /// </summary>
        public int Submit(string trackId, LeaderboardEntry entry, out bool newBestLap, out float percentile)
        {
            var board = Board(trackId);
            board.History ??= new List<float>();
            percentile = Percentile(board.History, entry.TotalTime);
            board.History.Add(entry.TotalTime);
            if (board.History.Count > MaxHistory) board.History.RemoveRange(0, board.History.Count - MaxHistory);
            newBestLap = board.BestLap <= 0f || entry.BestLap < board.BestLap;
            if (newBestLap)
            {
                board.BestLap = entry.BestLap;
                board.BestLapPilot = entry.Pilot;
                board.BestLapQuad = entry.Quad;
            }

            int index = board.Entries.Count;
            for (int i = 0; i < board.Entries.Count; i++)
            {
                if (entry.TotalTime < board.Entries[i].TotalTime)
                {
                    index = i;
                    break;
                }
            }
            if (index >= MaxEntries) return -1;
            board.Entries.Insert(index, entry);
            if (board.Entries.Count > MaxEntries) board.Entries.RemoveRange(MaxEntries, board.Entries.Count - MaxEntries);
            return index + 1;
        }

        /// <summary>Share (0–100) of <paramref name="history"/> slower than <paramref name="time"/>; -1 for an empty history.</summary>
        public static float Percentile(List<float> history, float time)
        {
            if (history == null || history.Count == 0) return -1f;
            int slower = 0;
            foreach (float t in history)
                if (t > time) slower++;
            return 100f * slower / history.Count;
        }

        public string ToJson() => JsonUtility.ToJson(Data, true);

        public void FromJson(string json)
        {
            Data = string.IsNullOrEmpty(json) ? new LeaderboardData() : JsonUtility.FromJson<LeaderboardData>(json) ?? new LeaderboardData();
        }

        public void Load(string path)
        {
            try
            {
                FromJson(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Debug.LogWarning($"[Ushqyr] Leaderboard unreadable, starting fresh: {e.Message}");
                Data = new LeaderboardData();
            }
        }

        public void Save(string path)
        {
            try
            {
                File.WriteAllText(path, ToJson());
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[Ushqyr] Could not save leaderboard: {e.Message}");
            }
        }
    }
}
