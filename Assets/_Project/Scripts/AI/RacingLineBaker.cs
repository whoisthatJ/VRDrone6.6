using System;
using System.Collections.Generic;
using Ushqyr.Flight;
using Ushqyr.Race;
using Ushqyr.Track;
using UnityEngine;

namespace Ushqyr.AI
{
    /// <summary>One checkpoint gate as the baker sees it: where to fly through, which way, and how far off centre is safe.</summary>
    [Serializable]
    public struct GateSpec
    {
        public Vector3 Centre;
        public Vector3 Normal;
        /// <summary>Safe radius round the centre (half the smaller opening side minus the quad and a margin).</summary>
        public float Radius;
    }

    /// <summary>The corridor the line must stay in: centre points every <see cref="RacingLineBaker.GuideStep"/> m with their side and up axes and radii.</summary>
    public sealed class GuideLine
    {
        public Vector3[] Points;
        public Vector3[] Right;
        public Vector3[] Up;
        public float[] Lateral;
        public float[] Vertical;
        public int Count => Points.Length;
    }

    /// <summary>
    /// Racing-line baker (P4). Starts from the course's flight line (the route, or a spline through the gates on old
    /// gate-only tracks), pins it through every gate opening, then relaxes it towards minimum curvature (a mix of
    /// second- and fourth-difference smoothing) while keeping every point inside the corridor and every gate pass inside
    /// the opening. A forward / backward pass then gives the fastest speed profile the quad's <see cref="FlightEnvelope"/>
    /// allows: corner speed from the sideways grip, acceleration and braking from thrust, drag and the climb angle.
    /// </summary>
    public static class RacingLineBaker
    {
        public const int Version = 17;
        public const float GuideStep = 1f;
        public const float FineStep = 0.5f;
        /// <summary>Clearance of the air-course flight lines (CourseClearance: 2 m) minus the quad and a margin.</summary>
        public const float AirCorridor = 1.2f;
        public const float GateMargin = 0.45f;
        /// <summary>Fractions of the envelope used by the profile (bots fly at most this hard; Elite = 1 x the profile).</summary>
        public const float Grip = 0.25f, AccelFraction = 0.75f, BrakeFraction = 0.55f;

        // ---------- inputs ----------

        /// <summary>
        /// The gates as the baker sees them. With a <paramref name="mask"/>, each opening is probed with sphere checks
        /// against those colliders (the gate's own frame, rungs and bars), and the line is pinned to the clearest point
        /// near the aim point, with a radius that keeps the quad clear of them.
        /// </summary>
        public static GateSpec[] Specs(IReadOnlyList<Gate> gates, int mask = 0)
        {
            var specs = new GateSpec[gates.Count];
            for (int i = 0; i < specs.Length; i++)
            {
                var g = gates[i];
                Vector3 n = g.transform.forward;
                Quaternion rot = g.transform.rotation;
                Vector3 aim = g.AimPointWorld, centre = g.OpeningCenterWorld;
                Vector2 half = g.OpeningSize * 0.5f;
                Vector3 local = Quaternion.Inverse(rot) * (aim - centre);
                float rect = Mathf.Min(half.x - Mathf.Abs(local.x), half.y - Mathf.Abs(local.y)) - GateMargin;
                var spec = new GateSpec { Centre = aim, Normal = n, Radius = Mathf.Max(0.12f, rect) };
                if (mask != 0) Probe(ref spec, rot, centre, half, aim, mask);
                specs[i] = spec;
            }
            return specs;
        }

        // Grid search in the opening for the point with the most clearance (a little preference for the aim point).
        private static void Probe(ref GateSpec spec, Quaternion rot, Vector3 centre, Vector2 half, Vector3 aim, int mask)
        {
            const float step = 0.1f, quad = 0.3f, max = 2.5f;
            float bestScore = float.MinValue, bestClear = 0f;
            Vector3 best = aim;
            bool blocked = false;
            for (float y = -half.y + step; y < half.y; y += step)
                for (float x = -half.x + step; x < half.x; x += step)
                {
                    Vector3 p = centre + rot * new Vector3(x, y, 0f);
                    float edge = Mathf.Min(half.x - Mathf.Abs(x), half.y - Mathf.Abs(y));
                    float clear = Mathf.Min(edge, Clearance(p, max, mask));
                    if (clear < max) blocked = true;
                    float score = clear - 0.15f * Vector3.Distance(p, aim);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClear = clear;
                        best = p;
                    }
                }
            if (!blocked) return; // nothing physical in the opening (holo rings): keep the aim point and the rectangle
            spec.Centre = best;
            spec.Radius = Mathf.Max(0.1f, bestClear - quad);
        }

        private static float Clearance(Vector3 p, float max, int mask)
        {
            if (!Physics.CheckSphere(p, max, mask, QueryTriggerInteraction.Ignore)) return max;
            float lo = 0f, hi = max;
            for (int k = 0; k < 7; k++)
            {
                float mid = 0.5f * (lo + hi);
                if (Physics.CheckSphere(p, mid, mask, QueryTriggerInteraction.Ignore)) hi = mid;
                else lo = mid;
            }
            return lo;
        }

        /// <summary>Corridor along a spline route, starting at the point closest to <paramref name="start"/>.</summary>
        public static GuideLine FromRoute(TrackRoute route, RouteData data, Vector3 start)
        {
            float d0 = route.ClosestDistance(start);
            int n = Mathf.Max(8, Mathf.RoundToInt(route.Length / GuideStep));
            float step = route.Length / n;
            var g = NewGuide(n);
            bool air = data.AirCourse;
            for (int i = 0; i < n; i++)
            {
                float d = route.Wrap(d0 + i * step);
                var p = air ? route.Frame(d) : route.Sample(d);
                g.Points[i] = p.Position;
                g.Right[i] = p.Right;
                g.Up[i] = air ? p.Up : Vector3.up;
                // Ground courses: stay between the inner lane lines and near the flight height above the deck.
                g.Lateral[i] = air ? AirCorridor : Mathf.Max(0.3f, data.CorridorWidth * 0.5f - 0.7f);
                g.Vertical[i] = air ? AirCorridor : 0.5f;
            }
            return g;
        }

        /// <summary>Corridor for gate-only tracks: a closed spline through each gate's approach, centre and exit.</summary>
        public static GuideLine FromGates(GateSpec[] gates, Vector3 start, float radius = 1f)
        {
            var way = new List<Vector3>();
            foreach (var s in gates)
            {
                way.Add(s.Centre - s.Normal * 4f);
                way.Add(s.Centre);
                way.Add(s.Centre + s.Normal * 4f);
            }
            // Dense Catmull-Rom, then even spacing.
            var dense = new List<Vector3>();
            int m = way.Count;
            for (int i = 0; i < m; i++)
            {
                Vector3 p0 = way[(i - 1 + m) % m], p1 = way[i], p2 = way[(i + 1) % m], p3 = way[(i + 2) % m];
                for (int k = 0; k < 20; k++) dense.Add(CatmullRom(p0, p1, p2, p3, k / 20f));
            }
            var pts = Resample(dense.ToArray(), GuideStep, out _);
            int startIndex = 0;
            float best = float.MaxValue;
            for (int i = 0; i < pts.Length; i++)
            {
                float e = (pts[i] - start).sqrMagnitude;
                if (e < best)
                {
                    best = e;
                    startIndex = i;
                }
            }
            int n = pts.Length;
            var g = NewGuide(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = pts[(startIndex + i) % n];
                Vector3 t = (pts[(startIndex + i + 1) % n] - pts[(startIndex + i - 1 + n) % n]).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, t);
                if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
                g.Points[i] = p;
                g.Right[i] = right.normalized;
                g.Up[i] = Vector3.Cross(t, g.Right[i]).normalized;
                g.Lateral[i] = radius;
                g.Vertical[i] = radius;
            }
            return g;
        }

        /// <summary>
        /// Fits the corridor to the real geometry (props, obstacle frames, scenery on <paramref name="mask"/>): where
        /// something is closer than the corridor radius the radius shrinks, and where the centre itself is too close it
        /// moves sideways to the nearest clear spot. Gates are left alone (their tubes come from the gate probe).
        /// </summary>
        public static void ApplyClearance(GuideLine g, GateSpec[] gates, int mask)
        {
            const float quad = 0.45f, want = 0.9f, max = 2.5f;
            int n = g.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = g.Points[i];
                // Around the gates the gate probe and tubes decide (slaloms need their full corridor to weave).
                if (NearGate(p, gates, RepairGateZone)) continue;
                float clear = Clearance(p, max, mask);
                if (clear < want && !NearGate(p, gates))
                {
                    // Search round the line for the nearest clear spot (up to 6 m off: a container is 2.4 m wide).
                    Vector3 best = p;
                    float bestClear = clear;
                    for (float r = 0.25f; r <= 6f && bestClear < want; r += 0.25f)
                        for (int a = 0; a < 16; a++)
                        {
                            float ang = a * Mathf.PI / 8f;
                            Vector3 q = p + (g.Right[i] * Mathf.Cos(ang) + g.Up[i] * Mathf.Sin(ang)) * r;
                            if (q.y < 0.6f) continue;
                            float c = Clearance(q, max, mask);
                            if (c > bestClear + 0.05f)
                            {
                                bestClear = c;
                                best = q;
                            }
                        }
                    g.Points[i] = best;
                    clear = bestClear;
                }
                float room = Mathf.Max(0.05f, clear - quad);
                g.Lateral[i] = Mathf.Min(g.Lateral[i], room);
                g.Vertical[i] = Mathf.Min(g.Vertical[i], room);
            }
            // Smooth the moved centre a little so the pushes don't leave kinks.
            var tmp = (Vector3[])g.Points.Clone();
            for (int it = 0; it < 3; it++)
            {
                for (int i = 0; i < n; i++) tmp[i] = (g.Points[Wrap(i - 1, n)] + 2f * g.Points[i] + g.Points[Wrap(i + 1, n)]) * 0.25f;
                for (int i = 0; i < n; i++)
                    if (Clearance(tmp[i], 0.5f, mask) >= Mathf.Min(0.5f, Clearance(g.Points[i], 0.5f, mask))) g.Points[i] = tmp[i];
            }
        }

        private static bool NearGate(Vector3 p, GateSpec[] gates, float radius = 2.5f)
        {
            foreach (var gs in gates)
                if ((p - gs.Centre).sqrMagnitude < radius * radius) return true;
            return false;
        }

        /// <summary>The repair leaves the line alone this close to a gate (slalom gates are 5 m apart).</summary>
        private const float RepairGateZone = 4f;

        /// <summary>Bakes the line for a built course: the route if it has one, else the gates.</summary>
        public static BotLine Bake(TrackRoute route, RouteData data, IReadOnlyList<Gate> gates, Vector3 start, in QuadProfile profile, int mask = 0, bool tight = false)
        {
            var specs = Specs(gates, mask);
            var guide = route != null && data != null && data.HasRoute ? FromRoute(route, data, start) : FromGates(specs, start);
            var env = new FlightEnvelope(profile);
            // The hash describes the inputs (before the clearance fit), so HashFor can check a cached line cheaply.
            int hash = HashOf(guide, specs, env);
            if (mask != 0) ApplyClearance(guide, specs, mask);
            var line = Bake(guide, specs, env, tight, 800, mask);
            line.Hash = hash;
            return line;
        }

        /// <summary>The hash a fresh bake of this course would get (to check a cached line).</summary>
        public static int HashFor(TrackRoute route, RouteData data, IReadOnlyList<Gate> gates, Vector3 start, in QuadProfile profile, int mask = 0)
        {
            var specs = Specs(gates, mask);
            var guide = route != null && data != null && data.HasRoute ? FromRoute(route, data, start) : FromGates(specs, start);
            return HashOf(guide, specs, new FlightEnvelope(profile));
        }

        // ---------- the bake ----------

        /// <param name="tight">
        /// False: the shape relaxed at 2 m spacing only (smoothest; best for most courses). True: then refined at 1 m
        /// (needed where gates are a few metres apart on a bend, e.g. slaloms in a turn). The editor bake flies both and
        /// keeps the first one an Elite bot completes cleanly.
        /// </param>
        public static BotLine Bake(GuideLine guide, GateSpec[] gates, FlightEnvelope env, bool tight = false, int iterations = 800, int mask = 0)
        {
            // The shape is relaxed on every second guide sample (2 m: long-range smoothness, cheap); a tight line then
            // refines it on all of them (1 m: slaloms and close gates on a bend).
            var coarse = Decimate(guide);
            var xc = Solve(coarse, gates, 2f * GuideStep, null, iterations, out var cc, out var cylc);
            Vector3[] x, c;
            int[] cylinder;
            if (tight)
            {
                int nf = guide.Count, m = coarse.Count;
                var init = new Vector3[nf];
                for (int j = 0; j < nf; j++)
                    init[j] = (j & 1) == 0 ? xc[(j / 2) % m] : 0.5f * (xc[(j / 2) % m] + xc[(j / 2 + 1) % m]);
                x = Solve(guide, gates, GuideStep, init, iterations / 2, out c, out cylinder);
            }
            else
            {
                guide = coarse;
                x = xc;
                c = cc;
                cylinder = cylc;
            }
            int n = guide.Count;

            // 3. Fine, evenly spaced samples.
            var dense = new List<Vector3>(n * 8);
            for (int j = 0; j < n; j++)
            {
                Vector3 p0 = x[Wrap(j - 1, n)], p1 = x[j], p2 = x[Wrap(j + 1, n)], p3 = x[Wrap(j + 2, n)];
                for (int k = 0; k < 8; k++) dense.Add(CatmullRom(p0, p1, p2, p3, k / 8f));
            }
            var pts = Resample(dense.ToArray(), FineStep, out float step);
            if (mask != 0) RepairClearance(pts, gates, mask);
            int fn = pts.Length;
            var line = new BotLine { Step = step, Points = pts };
            line.Tangents = new Vector3[fn];
            line.Curvature = new Vector3[fn];
            for (int i = 0; i < fn; i++) line.Tangents[i] = (pts[Wrap(i + 1, fn)] - pts[Wrap(i - 1, fn)]).normalized;
            const int kw = 4; // +-2 m
            for (int i = 0; i < fn; i++) line.Curvature[i] = (line.Tangents[Wrap(i + kw, fn)] - line.Tangents[Wrap(i - kw, fn)]) / (2f * kw * step);

            // Slack: the room left in the corridor or the gate, from the coarse samples.
            var coarseSlack = new float[n];
            for (int j = 0; j < n; j++) coarseSlack[j] = Room(x[j], j, c, guide, gates, cylinder);
            line.Slack = new float[fn];
            float ratio = (float)n / fn;
            for (int i = 0; i < fn; i++)
            {
                float f = i * ratio;
                int j = (int)f;
                line.Slack[i] = Mathf.Max(0f, Mathf.Lerp(coarseSlack[j % n], coarseSlack[(j + 1) % n], f - j));
            }

            line.GateSamples = MatchGates(pts, gates);
            SpeedProfile(line, env, gates);
            line.Hash = HashOf(guide, gates, env);
            return line;
        }

        /// <summary>Line clearance the repair keeps from course geometry (m): the quad's radius and a margin.</summary>
        public const float LineClearance = 0.45f;

        /// <summary>
        /// The smoothed line can cut a corner the corridor kept clear (a container's top edge between two corridor
        /// points). Points closer than <see cref="LineClearance"/> to geometry are pushed out along the penetration
        /// direction, then the stretch is relaxed a little; a few rounds. Points at the gates stay (the gate probe placed
        /// them).
        /// </summary>
        public static int RepairClearance(Vector3[] pts, GateSpec[] gates, int mask, int rounds = 8)
        {
            int n = pts.Length, moved = 0;
            var probe = new GameObject("__ClearanceProbe") { hideFlags = HideFlags.HideAndDontSave };
            var sphere = probe.AddComponent<SphereCollider>();
            sphere.radius = LineClearance;
            var hits = new Collider[16];
            var near = new bool[n];
            try
            {
                for (int r = 0; r < rounds; r++)
                {
                    int count = 0;
                    for (int i = 0; i < n; i++)
                    {
                        near[i] = false;
                        if (NearGate(pts[i], gates, RepairGateZone)) continue;
                        int k = Physics.OverlapSphereNonAlloc(pts[i], LineClearance, hits, mask, QueryTriggerInteraction.Ignore);
                        for (int h = 0; h < k; h++)
                        {
                            var other = hits[h];
                            if (Physics.ComputePenetration(sphere, pts[i], Quaternion.identity, other, other.transform.position, other.transform.rotation, out Vector3 dir, out float dist))
                            {
                                pts[i] += dir * (dist + 0.02f);
                                near[i] = true;
                            }
                        }
                        if (near[i]) count++;
                    }
                    if (count == 0) break;
                    moved += count;
                    // Relax the pushed stretch and its neighbours so the push leaves no kink.
                    var tmp = (Vector3[])pts.Clone();
                    for (int i = 0; i < n; i++)
                    {
                        bool touch = false;
                        for (int o = -6; o <= 6 && !touch; o++) touch = near[Wrap(i + o, n)];
                        if (!touch || NearGate(pts[i], gates, RepairGateZone)) continue;
                        tmp[i] = (pts[Wrap(i - 1, n)] + 2f * pts[i] + pts[Wrap(i + 1, n)]) * 0.25f;
                    }
                    for (int i = 0; i < n; i++) pts[i] = tmp[i];
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            return moved;
        }

        private static GuideLine Decimate(GuideLine g)
        {
            int m = g.Count / 2;
            var d = NewGuide(m);
            for (int i = 0; i < m; i++)
            {
                d.Points[i] = g.Points[2 * i];
                d.Right[i] = g.Right[2 * i];
                d.Up[i] = g.Up[2 * i];
                d.Lateral[i] = g.Lateral[2 * i];
                d.Vertical[i] = g.Vertical[2 * i];
            }
            return d;
        }

        /// <summary>
        /// Pins the corridor through the gates and relaxes the line towards minimum curvature inside it.
        /// <paramref name="c"/> is the pinned corridor centre, <paramref name="cylinder"/> the gate each sample belongs to (or -1).
        /// </summary>
        private static Vector3[] Solve(GuideLine guide, GateSpec[] gates, float step, Vector3[] init, int iterations, out Vector3[] c, out int[] cylinder)
        {
            int n = guide.Count;
            c = (Vector3[])guide.Points.Clone();
            int[] gateSample = MatchGates(guide.Points, gates);

            // Pin the corridor centre through each gate: blend the offset in over +-10 m.
            int span = Mathf.CeilToInt(10f / step);
            var shift = new Vector3[n];
            var weight = new float[n];
            for (int k = 0; k < gates.Length; k++)
            {
                Vector3 delta = gates[k].Centre - guide.Points[gateSample[k]];
                for (int o = -span; o <= span; o++)
                {
                    float w = 0.5f * (1f + Mathf.Cos(Mathf.PI * Mathf.Abs(o) / (span + 1)));
                    int j = Wrap(gateSample[k] + o, n);
                    shift[j] += delta * w;
                    weight[j] += w;
                }
            }
            for (int j = 0; j < n; j++)
                if (weight[j] > 0f) c[j] += shift[j] / Mathf.Max(1f, weight[j]);

            // Gate tubes: the samples within +-4 m of a gate stay close to its axis; with gates close together
            // (slaloms) each sample belongs to the nearer gate.
            cylinder = new int[n];
            for (int j = 0; j < n; j++) cylinder[j] = -1;
            var cylinderOff = new int[n];
            int reach = Mathf.Max(1, Mathf.RoundToInt(4f / step));
            for (int k = 0; k < gates.Length; k++)
                for (int o = -reach; o <= reach; o++)
                {
                    int j = Wrap(gateSample[k] + o, n);
                    if (cylinder[j] < 0 || Mathf.Abs(o) < cylinderOff[j])
                    {
                        cylinder[j] = k;
                        cylinderOff[j] = Mathf.Abs(o);
                    }
                }

            var x = init != null ? (Vector3[])init.Clone() : (Vector3[])c.Clone();
            for (int j = 0; j < n; j++) x[j] = Constrain(x[j], j, c, guide, gates, cylinder);
            var tmp = new Vector3[n];
            const float alpha = 0.12f, beta = 0.06f;
            for (int it = 0; it < iterations; it++)
            {
                for (int j = 0; j < n; j++)
                {
                    Vector3 m2 = x[Wrap(j - 2, n)], m1 = x[Wrap(j - 1, n)], p1 = x[Wrap(j + 1, n)], p2 = x[Wrap(j + 2, n)], x0 = x[j];
                    Vector3 lap = m1 + p1 - 2f * x0;
                    Vector3 bi = m2 - 4f * m1 + 6f * x0 - 4f * p1 + p2;
                    tmp[j] = x0 + alpha * lap - beta * bi;
                }
                for (int j = 0; j < n; j++) x[j] = Constrain(tmp[j], j, c, guide, gates, cylinder);
            }
            return x;
        }

        private static Vector3 Constrain(Vector3 p, int j, Vector3[] c, GuideLine g, GateSpec[] gates, int[] cylinder)
        {
            // Corridor ellipse round the (pinned) centre, across the guide's direction.
            Vector3 d = p - c[j];
            float lat = Vector3.Dot(d, g.Right[j]), ver = Vector3.Dot(d, g.Up[j]);
            Vector3 along = d - g.Right[j] * lat - g.Up[j] * ver;
            float e = (lat * lat) / (g.Lateral[j] * g.Lateral[j]) + (ver * ver) / (g.Vertical[j] * g.Vertical[j]);
            if (e > 1f)
            {
                float s = 1f / Mathf.Sqrt(e);
                lat *= s;
                ver *= s;
            }
            p = c[j] + along + g.Right[j] * lat + g.Up[j] * ver;
            int k = cylinder[j];
            if (k >= 0)
            {
                var gs = gates[k];
                Vector3 q = p - gs.Centre;
                float ax = Vector3.Dot(q, gs.Normal);
                Vector3 radial = q - gs.Normal * ax;
                // The line itself keeps to the middle of the safe radius: the rest is the bot's tracking margin.
                float rr = gs.Radius * LineShare;
                if (radial.magnitude > rr) p = gs.Centre + gs.Normal * ax + radial.normalized * rr;
            }
            return p;
        }

        private static float Room(Vector3 p, int j, Vector3[] c, GuideLine g, GateSpec[] gates, int[] cylinder)
        {
            Vector3 d = p - c[j];
            float lat = Vector3.Dot(d, g.Right[j]), ver = Vector3.Dot(d, g.Up[j]);
            float room = Mathf.Min(g.Lateral[j] - Mathf.Abs(lat), g.Vertical[j] - Mathf.Abs(ver));
            int k = cylinder[j];
            if (k >= 0)
            {
                var gs = gates[k];
                Vector3 q = p - gs.Centre;
                Vector3 radial = q - gs.Normal * Vector3.Dot(q, gs.Normal);
                room = Mathf.Min(room, gs.Radius - radial.magnitude);
            }
            return room;
        }

        /// <summary>For each gate in order, the sample where the line passes it: the first close approach after the previous gate.</summary>
        public static int[] MatchGates(Vector3[] pts, GateSpec[] gates)
        {
            int n = pts.Length;
            var result = new int[gates.Length];
            int cursor = 0;
            for (int k = 0; k < gates.Length; k++)
            {
                Vector3 c = gates[k].Centre;
                float near = Mathf.Max(5f, gates[k].Radius + 3f);
                int best = -1, globalBest = cursor;
                float bestE = float.MaxValue, globalE = float.MaxValue;
                // The first pass that comes close wins (take its closest point); otherwise the closest point overall.
                for (int o = 0; o < n; o++)
                {
                    int j = (cursor + o) % n;
                    float e = (pts[j] - c).sqrMagnitude;
                    if (e < globalE)
                    {
                        globalE = e;
                        globalBest = j;
                    }
                    if (e < near * near)
                    {
                        if (e < bestE)
                        {
                            bestE = e;
                            best = j;
                        }
                    }
                    else if (best >= 0) break;
                }
                result[k] = best >= 0 ? best : globalBest;
                cursor = result[k];
            }
            return result;
        }

        private static void SpeedProfile(BotLine line, FlightEnvelope env, GateSpec[] gates)
        {
            int n = line.Count;
            float ds = line.Step;
            var corner = new float[n];
            var slope = new float[n];
            var kappa = new float[n];
            float top = env.TopSpeed * 0.97f;
            for (int i = 0; i < n; i++)
            {
                kappa[i] = line.Curvature[i].magnitude;
                slope[i] = Mathf.Asin(Mathf.Clamp(line.Tangents[i].y, -1f, 1f));
                float v = top;
                for (int it = 0; it < 4; it++) v = kappa[i] > 1e-4f ? Mathf.Min(top, Mathf.Sqrt(Grip * env.Lateral(v) / kappa[i])) : top;
                corner[i] = Mathf.Max(2f, v);
            }
            // Turn-in rate: the attitude can only swing so fast, so a quickly changing curvature (slaloms, S-bends)
            // limits the speed by the lateral jerk it needs (v^3 * dk/ds).
            float jerk = Grip * env.Lateral(10f) * JerkRate;
            for (int i = 0; i < n; i++)
            {
                const int w = 2;
                float dk = (line.Curvature[Wrap(i + w, n)] - line.Curvature[Wrap(i - w, n)]).magnitude / (2f * w * ds);
                if (dk > 1e-4f) corner[i] = Mathf.Min(corner[i], Mathf.Max(4f, Mathf.Pow(jerk / dk, 1f / 3f)));
            }
            // Tight openings: the follower needs time to be exactly on the line, so slow down for small radii.
            for (int k = 0; k < line.GateSamples.Length && k < gates.Length; k++)
            {
                float vg = GateSpeed(gates[k].Radius);
                int span = Mathf.CeilToInt(6f / ds);
                for (int o = -span; o <= span; o++)
                {
                    int j = Wrap(line.GateSamples[k] + o, n);
                    corner[j] = Mathf.Min(corner[j], vg);
                }
            }
            var s = (float[])corner.Clone();
            // Forward (accelerating) and backward (braking) passes, twice round the closed loop.
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < n; k++)
                {
                    int i = k, j = (k + 1) % n;
                    float a = Mathf.Min(MaxAccel, AccelFraction * env.Accel(s[i], slope[i])) * Friction(s[i], kappa[i], env);
                    s[j] = Mathf.Min(s[j], Mathf.Sqrt(Mathf.Max(4f, s[i] * s[i] + 2f * a * ds)));
                }
            for (int pass = 0; pass < 2; pass++)
                for (int k = n - 1; k >= 0; k--)
                {
                    int i = k, j = (k + 1) % n;
                    float b = Mathf.Min(MaxBrake, BrakeFraction * env.Brake(s[j], slope[j])) * Friction(s[j], kappa[j], env);
                    s[i] = Mathf.Min(s[i], Mathf.Sqrt(Mathf.Max(4f, s[j] * s[j] + 2f * b * ds)));
                }
            line.Speed = s;
            line.Accel = new float[n];
            float lap = 0f;
            for (int i = 0; i < n; i++)
            {
                float vp = s[(i + 1) % n], vm = s[(i - 1 + n) % n];
                line.Accel[i] = (vp * vp - vm * vm) / (4f * ds);
                lap += ds / Mathf.Max(0.5f, s[i]);
            }
            line.LapTime = lap;
        }

        /// <summary>Fastest pass through a gate with this safe radius (m/s).</summary>
        public static float GateSpeed(float radius) => GateSpeedBase + GateSpeedPerMetre * radius;
        public const float GateSpeedBase = 12f, GateSpeedPerMetre = 22f;
        /// <summary>
        /// Along-path acceleration / braking the profile asks for at most (m/s^2). A quad could brake far harder, but only
        /// by swinging its thrust nearly backwards, and the line is lost during that swing.
        /// </summary>
        public const float MaxAccel = 15f, MaxBrake = 12f;
        /// <summary>Share of a gate's safe radius the line may use (the rest is left for tracking errors).</summary>
        public const float LineShare = 0.5f;
        /// <summary>Lateral jerk allowed, as a multiple of the usable lateral acceleration (1/s).</summary>
        public const float JerkRate = 4f;

        // Share of the grip left for speeding up / braking while turning (friction circle).
        private static float Friction(float v, float kappa, FlightEnvelope env)
        {
            float used = v * v * kappa / Mathf.Max(0.1f, Grip * env.Lateral(v));
            return Mathf.Sqrt(Mathf.Max(0.05f, 1f - used * used));
        }

        // ---------- helpers ----------

        public static int HashOf(GuideLine g, GateSpec[] gates, FlightEnvelope env)
        {
            unchecked
            {
                int h = (int)2166136261 ^ Version;
                void Add(float v) => h = (h ^ Mathf.RoundToInt(v * 100f)) * 16777619;
                Add(g.Count);
                for (int i = 0; i < g.Count; i += 5)
                {
                    Add(g.Points[i].x); Add(g.Points[i].y); Add(g.Points[i].z);
                    Add(g.Lateral[i]); Add(g.Vertical[i]);
                }
                foreach (var s in gates)
                {
                    Add(s.Centre.x); Add(s.Centre.y); Add(s.Centre.z);
                    Add(s.Normal.x * 10f); Add(s.Normal.y * 10f); Add(s.Normal.z * 10f);
                    Add(s.Radius);
                }
                Add(env.Mass * 100f); Add(env.StaticThrust); Add(env.TopSpeed);
                return h;
            }
        }

        private static GuideLine NewGuide(int n) => new GuideLine
        {
            Points = new Vector3[n], Right = new Vector3[n], Up = new Vector3[n], Lateral = new float[n], Vertical = new float[n],
        };

        private static int Wrap(int i, int n)
        {
            i %= n;
            return i < 0 ? i + n : i;
        }

        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>A closed polyline resampled to an even spacing close to <paramref name="target"/>.</summary>
        public static Vector3[] Resample(Vector3[] closed, float target, out float step)
        {
            int m = closed.Length;
            var cum = new float[m + 1];
            for (int i = 0; i < m; i++) cum[i + 1] = cum[i] + Vector3.Distance(closed[i], closed[(i + 1) % m]);
            float total = cum[m];
            int n = Mathf.Max(8, Mathf.RoundToInt(total / target));
            step = total / n;
            var outPts = new Vector3[n];
            int seg = 0;
            for (int k = 0; k < n; k++)
            {
                float d = k * step;
                while (seg < m - 1 && cum[seg + 1] < d) seg++;
                float len = cum[seg + 1] - cum[seg];
                float t = len > 1e-6f ? (d - cum[seg]) / len : 0f;
                outPts[k] = Vector3.Lerp(closed[seg], closed[(seg + 1) % m], t);
            }
            return outPts;
        }
    }
}
