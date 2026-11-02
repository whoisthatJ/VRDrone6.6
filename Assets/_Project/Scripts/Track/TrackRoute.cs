using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace GateRush.Track
{
    /// <summary>A point on the route with a level frame (right is horizontal, up is never upside down).</summary>
    public struct RoutePoint
    {
        public float Distance;
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Right;
        public Vector3 Up;
        /// <summary>Forward with the slope removed (for vertical things: gates, posts, supports).</summary>
        public Vector3 FlatForward;
    }

    /// <summary>
    /// The route as a Unity <see cref="Spline"/> (Bezier knots; zero tangents = auto-smooth) re-sampled by arc length,
    /// so everything is placed by metres along the route. Deterministic: same data, same samples.
    /// </summary>
    public sealed class TrackRoute
    {
        /// <summary>Spacing of the arc-length table (m).</summary>
        public const float Step = 0.25f;
        private const int SamplesPerKnot = 96;

        private readonly Vector3[] _points;

        public TrackRoute(RouteData data)
        {
            if (data == null || !data.HasRoute) throw new ArgumentException("A route needs at least 3 knots");
            Data = data;
            Closed = data.Closed;
            Spline = BuildSpline(data);

            // Dense parametric samples → cumulative length → uniform re-sampling every Step metres.
            int n = Mathf.Max(64, data.Knots.Count * SamplesPerKnot);
            var dense = new Vector3[n + 1];
            var cumulative = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                dense[i] = (Vector3)Spline.EvaluatePosition((float)i / n);
                if (i > 0) cumulative[i] = cumulative[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);
            }
            Length = cumulative[n];
            int count = Mathf.Max(2, Mathf.CeilToInt(Length / Step) + 1);
            _points = new Vector3[count];
            int j = 0;
            for (int k = 0; k < count; k++)
            {
                float d = Mathf.Min(k * Step, Length);
                while (j < n - 1 && cumulative[j + 1] < d) j++;
                float seg = cumulative[j + 1] - cumulative[j];
                float f = seg > 1e-6f ? (d - cumulative[j]) / seg : 0f;
                _points[k] = Vector3.LerpUnclamped(dense[j], dense[j + 1], f);
            }
        }

        public RouteData Data { get; }
        public Spline Spline { get; }
        public bool Closed { get; }
        /// <summary>Total length in metres (one lap for a closed route).</summary>
        public float Length { get; }

        /// <summary>Distance wrapped into [0, Length) on a closed route, clamped on an open one.</summary>
        public float Wrap(float distance)
        {
            if (!Closed) return Mathf.Clamp(distance, 0f, Length);
            float d = distance % Length;
            return d < 0f ? d + Length : d;
        }

        /// <summary>Position, heading and level frame at <paramref name="distance"/> metres along the route.</summary>
        public RoutePoint Sample(float distance)
        {
            float d = Wrap(distance);
            Vector3 pos = PositionAt(d);
            // Central difference over ±1 m gives a smooth heading.
            Vector3 fwd = PositionAt(d + 1f) - PositionAt(d - 1f);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;
            Vector3 up = Vector3.Cross(fwd, right).normalized;
            return new RoutePoint { Distance = d, Position = pos, Forward = fwd, Right = right, Up = up, FlatForward = flat };
        }

        private Vector3 PositionAt(float distance)
        {
            float d = Closed ? Wrap(distance) : Mathf.Clamp(distance, 0f, Length);
            float x = d / Step;
            int i = Mathf.Min((int)x, _points.Length - 2);
            float f = x - i;
            if (Closed && i >= _points.Length - 1) return _points[_points.Length - 1];
            return Vector3.LerpUnclamped(_points[i], _points[i + 1], f);
        }

        /// <summary>Distance along the route of the closest route point to <paramref name="p"/>.</summary>
        public float ClosestDistance(Vector3 p) => ClosestDistance(p, -1f, 0f);

        /// <summary>
        /// Like <see cref="ClosestDistance(Vector3)"/>, ignoring route points within <paramref name="excludeRadius"/> metres
        /// (along the route) of <paramref name="excludeAround"/>; used to find other branches of a self-crossing route.
        /// </summary>
        public float ClosestDistance(Vector3 p, float excludeAround, float excludeRadius, bool horizontal = false)
        {
            float best = float.MaxValue, bestD = 0f;
            for (int i = 0; i < _points.Length; i++)
            {
                float d = i * Step;
                if (excludeAround >= 0f && AlongGap(d, excludeAround) < excludeRadius) continue;
                Vector3 delta = _points[i] - p;
                if (horizontal) delta.y = 0f;
                float sq = delta.sqrMagnitude;
                if (sq < best)
                {
                    best = sq;
                    bestD = d;
                }
            }
            LastClosestSqr = best;
            return Mathf.Min(bestD, Length);
        }

        /// <summary>Squared distance found by the last <see cref="ClosestDistance(Vector3, float, float, bool)"/> call.</summary>
        public float LastClosestSqr { get; private set; }

        /// <summary>Shortest gap between two distances along the route (wraps on a closed route).</summary>
        public float AlongGap(float a, float b)
        {
            float g = Mathf.Abs(a - b);
            return Closed ? Mathf.Min(g, Length - g) : g;
        }

        private static Spline BuildSpline(RouteData data)
        {
            var spline = new Spline();
            foreach (var k in data.Knots)
            {
                var knot = new BezierKnot((float3)k.Position, (float3)k.TangentIn, (float3)k.TangentOut, quaternion.identity);
                spline.Add(knot, k.AutoTangents ? TangentMode.AutoSmooth : TangentMode.Broken);
            }
            spline.Closed = data.Closed;
            // Closing changes the neighbours of the first/last knots: recompute the auto tangents.
            for (int i = 0; i < data.Knots.Count; i++)
                if (data.Knots[i].AutoTangents) spline.SetTangentMode(i, TangentMode.AutoSmooth);
            return spline;
        }
    }
}
