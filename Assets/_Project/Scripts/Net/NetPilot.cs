using System.Collections.Generic;
using UnityEngine;

namespace GateRush.Net
{
    /// <summary>One pilot in an online race (the local one included).</summary>
    public sealed class NetPilot
    {
        public ulong ClientId;
        public string Name = "Pilot";
        /// <summary><see cref="GateRush.Garage.QuadBuild"/> JSON, so everyone sees the same quad.</summary>
        public string BuildJson = "";
        /// <summary>Start grid slot, 0..<see cref="OnlineRules.MaxPilots"/>-1.</summary>
        public int Slot;
        public bool Ready;
        public bool Finished;
        public float TotalTime;
        public float BestLap;
        public float Progress;
        public int Lap;
        public readonly SnapshotBuffer Buffer = new SnapshotBuffer();

        public void ResetRace()
        {
            Ready = Finished = false;
            TotalTime = BestLap = Progress = 0f;
            Lap = 0;
            Buffer.Clear();
        }
    }

    /// <summary>Pure rules of an online race: grid layout, standings.</summary>
    public static class OnlineRules
    {
        public const int MaxPilots = 8;
        /// <summary>Seconds from "everyone loaded" to GO (covers the 3-2-1 countdown and late packets).</summary>
        public const double StartLead = 4.0;
        /// <summary>The host starts anyway when someone takes longer than this to load.</summary>
        public const float LoadTimeout = 25f;

        /// <summary>
        /// Grid slot offset from the track spawn (x right, z forward). The kit start pad has four 1.5 m slots and the
        /// spawn is slot 2, so the first row fills that pad; pilots 5-8 line up 2 m behind.
        /// </summary>
        public static Vector3 GridOffset(int slot)
        {
            slot = Mathf.Clamp(slot, 0, MaxPilots - 1);
            float[] columns = { 0f, 1.5f, -1.5f, 3f };
            return new Vector3(columns[slot % 4], 0f, -2f * (slot / 4));
        }

        /// <summary>Lowest slot nobody uses.</summary>
        public static int FreeSlot(IEnumerable<NetPilot> pilots)
        {
            var used = new HashSet<int>();
            foreach (var p in pilots) used.Add(p.Slot);
            for (int i = 0; i < MaxPilots; i++)
                if (!used.Contains(i)) return i;
            return MaxPilots - 1;
        }

        /// <summary>Finishers by total time first, then everyone else by distance flown.</summary>
        public static List<NetPilot> Standings(IEnumerable<NetPilot> pilots)
        {
            var list = new List<NetPilot>(pilots);
            list.Sort((a, b) =>
            {
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                if (a.Finished) return a.TotalTime.CompareTo(b.TotalTime);
                int byProgress = b.Progress.CompareTo(a.Progress);
                return byProgress != 0 ? byProgress : a.Slot.CompareTo(b.Slot);
            });
            return list;
        }
    }
}
