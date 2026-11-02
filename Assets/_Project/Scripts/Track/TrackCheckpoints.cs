using System.Collections.Generic;

namespace GateRush.Track
{
    /// <summary>
    /// Race order of a spline track's checkpoints. Race gate 0 is the start/finish (the finish obstacle, or the first
    /// checkpoint after the start pad); the others follow in route order after it, so a lap is every checkpoint in
    /// order ending back through the finish (see LapTimer). Pure C#.
    /// </summary>
    public static class TrackCheckpoints
    {
        /// <param name="distances">Each obstacle's distance along the route.</param>
        /// <param name="isCheckpoint">Which obstacles count.</param>
        /// <param name="finish">Obstacle index of the finish, or -1 to use the first checkpoint after <paramref name="startDistance"/>.</param>
        /// <param name="startDistance">Distance along the route of the start pad.</param>
        /// <param name="length">Route length (one lap).</param>
        /// <returns>Obstacle indices in race order (element 0 = start/finish).</returns>
        public static int[] Order(IReadOnlyList<float> distances, IReadOnlyList<bool> isCheckpoint, int finish, float startDistance, float length)
        {
            var candidates = new List<int>();
            for (int i = 0; i < distances.Count; i++)
                if (isCheckpoint[i]) candidates.Add(i);
            if (candidates.Count == 0) return new int[0];

            if (finish < 0 || finish >= distances.Count || !isCheckpoint[finish])
            {
                finish = candidates[0];
                float best = float.MaxValue;
                foreach (int i in candidates)
                {
                    float ahead = Ahead(startDistance, distances[i], length);
                    if (ahead < best)
                    {
                        best = ahead;
                        finish = i;
                    }
                }
            }

            float origin = distances[finish];
            candidates.Remove(finish);
            // Stable by distance after the finish; ties keep list order.
            candidates.Sort((a, b) =>
            {
                int c = Ahead(origin, distances[a], length).CompareTo(Ahead(origin, distances[b], length));
                return c != 0 ? c : a.CompareTo(b);
            });
            candidates.Insert(0, finish);
            return candidates.ToArray();
        }

        /// <summary>How far ahead of <paramref name="from"/> the distance <paramref name="to"/> is, going forward around the loop.</summary>
        public static float Ahead(float from, float to, float length)
        {
            if (length <= 0f) return to - from;
            float d = (to - from) % length;
            return d < 0f ? d + length : d;
        }
    }
}
