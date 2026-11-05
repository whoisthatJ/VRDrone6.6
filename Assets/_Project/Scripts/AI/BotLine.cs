using System;
using UnityEngine;

namespace Ushqyr.AI
{
    /// <summary>
    /// A baked racing line (P4): a closed loop of points every <see cref="Step"/> metres through every gate opening, with
    /// its tangent, curvature vector (dT/ds), the speed profile, the along-path acceleration that profile asks for, and
    /// the room left to each side (how far a bot may wander and stay inside the corridor and the openings).
    /// Sample 0 is next to the start pad.
    /// </summary>
    [Serializable]
    public sealed class BotLine
    {
        public float Step = 0.5f;
        public Vector3[] Points = new Vector3[0];
        public Vector3[] Tangents = new Vector3[0];
        public Vector3[] Curvature = new Vector3[0];
        public float[] Speed = new float[0];
        public float[] Accel = new float[0];
        public float[] Slack = new float[0];
        /// <summary>Sample index of each checkpoint gate, in race order.</summary>
        public int[] GateSamples = new int[0];
        /// <summary>Profile time for one flying lap (s).</summary>
        public float LapTime;
        /// <summary>Hash of what the line was baked from (gates, corridor, quad), to tell when it is out of date.</summary>
        public int Hash;

        public int Count => Points.Length;
        public float Length => Count * Step;
        public bool IsValid => Points != null && Points.Length > 8 && Speed != null && Speed.Length == Points.Length;

        public float Wrap(float d)
        {
            float l = Length;
            d %= l;
            return d < 0f ? d + l : d;
        }

        public int Index(int i)
        {
            int n = Count;
            i %= n;
            return i < 0 ? i + n : i;
        }

        /// <summary>Distance along the line of a sample index.</summary>
        public float DistanceOf(int sample) => Index(sample) * Step;

        /// <summary>Interpolated line point at distance <paramref name="d"/>.</summary>
        public void Sample(float d, out Vector3 position, out Vector3 tangent, out Vector3 curvature, out float speed, out float accel, out float slack)
        {
            float f = Wrap(d) / Step;
            int i = (int)f;
            float u = f - i;
            int a = Index(i), b = Index(i + 1);
            position = Vector3.LerpUnclamped(Points[a], Points[b], u);
            tangent = Vector3.LerpUnclamped(Tangents[a], Tangents[b], u).normalized;
            curvature = Vector3.LerpUnclamped(Curvature[a], Curvature[b], u);
            speed = Mathf.LerpUnclamped(Speed[a], Speed[b], u);
            accel = Mathf.LerpUnclamped(Accel[a], Accel[b], u);
            slack = Mathf.LerpUnclamped(Slack[a], Slack[b], u);
        }

        public Vector3 PointAt(float d)
        {
            float f = Wrap(d) / Step;
            int i = (int)f;
            return Vector3.LerpUnclamped(Points[Index(i)], Points[Index(i + 1)], f - i);
        }

        /// <summary>
        /// Distance along the line closest to <paramref name="p"/>: searched from <paramref name="back"/> m behind to
        /// <paramref name="ahead"/> m ahead of <paramref name="hint"/>, or the whole loop when the hint is negative.
        /// <paramref name="distance"/> is how far <paramref name="p"/> is from the line.
        /// </summary>
        public float Closest(Vector3 p, float hint, out float distance, float back = 6f, float ahead = 30f)
        {
            int n = Count;
            int from, count;
            if (hint < 0f)
            {
                from = 0;
                count = n;
            }
            else
            {
                from = Mathf.FloorToInt((hint - back) / Step);
                count = Mathf.CeilToInt((back + ahead) / Step);
            }
            float best = float.MaxValue;
            int bi = 0;
            for (int k = 0; k < count; k++)
            {
                int i = Index(from + k);
                float e = (Points[i] - p).sqrMagnitude;
                if (e < best)
                {
                    best = e;
                    bi = from + k;
                }
            }
            // Refine on the two segments next to the best sample.
            float bestD = bi * Step;
            float bestE = best;
            for (int s = -1; s <= 0; s++)
            {
                Vector3 a = Points[Index(bi + s)], b = Points[Index(bi + s + 1)];
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float e = (a + ab * t - p).sqrMagnitude;
                if (e < bestE)
                {
                    bestE = e;
                    bestD = (bi + s + t) * Step;
                }
            }
            distance = Mathf.Sqrt(bestE);
            return Wrap(bestD);
        }

        /// <summary>How far ahead <paramref name="to"/> is from <paramref name="from"/> going round the loop (0..Length).</summary>
        public float Ahead(float from, float to) => Wrap(to - from);
    }
}
