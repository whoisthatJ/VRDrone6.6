using UnityEngine;

namespace GateRush.Race
{
    /// <summary>Pure helpers behind <see cref="RaceSession.Progress"/> (standings between pilots).</summary>
    public static class RaceProgress
    {
        /// <summary>Gates passed since the start: the holeshot counts as one, each lap adds the whole course.</summary>
        public static int GatesPassed(bool holeshotDone, int completedLaps, int nextCheckpoint, int gateCount)
        {
            if (!holeshotDone || gateCount <= 0) return 0;
            return completedLaps * gateCount + (nextCheckpoint == 0 ? gateCount : nextCheckpoint);
        }

        /// <summary>How far along the leg <paramref name="from"/> → <paramref name="to"/> the quad is, 0..0.99.</summary>
        public static float LegFraction(Vector3 from, Vector3 to, Vector3 quad)
        {
            float leg = Vector3.Distance(from, to);
            if (leg < 0.01f) return 0f;
            return Mathf.Clamp(1f - Vector3.Distance(quad, to) / leg, 0f, 0.99f);
        }
    }
}
