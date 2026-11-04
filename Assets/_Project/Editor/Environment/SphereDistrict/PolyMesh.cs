using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.EditorTools.SphereDistrict
{
    /// <summary>
    /// Mesh building blocks for the Sphere District generators: a small builder with metric UVs and submeshes, plan
    /// polygons (x east, y north) with winding, insets, loop clean-up, point tests, and ear-clipping triangulation that
    /// takes courtyard holes (bridged into the outer ring).
    /// </summary>
    public sealed class PolyMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<List<int>> Sub = new List<List<int>>();

        private List<int> S(int sub)
        {
            while (Sub.Count <= sub) Sub.Add(new List<int>());
            return Sub[sub];
        }

        public int Vert(Vector3 p, Vector2 uv)
        {
            V.Add(p);
            UV.Add(uv);
            return V.Count - 1;
        }

        /// <summary>Quad a-b-c-d, counter-clockwise seen from its front.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, int sub)
        {
            int i = Vert(a, ua), j = Vert(b, ub), k = Vert(c, uc), l = Vert(d, ud);
            S(sub).AddRange(new[] { i, k, j, i, l, k });
        }

        /// <summary>Triangle a-b-c, counter-clockwise seen from its front.</summary>
        public void Tri(int a, int b, int c, int sub) => S(sub).AddRange(new[] { a, c, b });

        public void EnsureSubmeshes(int count)
        {
            while (Sub.Count < count) Sub.Add(new List<int>());
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(V);
            m.SetUVs(0, UV);
            m.subMeshCount = Sub.Count;
            for (int i = 0; i < Sub.Count; i++) m.SetTriangles(Sub[i], i);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        // ---------- walls and floors ----------

        /// <summary>
        /// Vertical wall along a closed ring between heights y0 and y1, facing the walker's right (outwards for an
        /// anticlockwise outer ring, into the courtyard for a clockwise hole). u = metres along the ring, v = height.
        /// </summary>
        public void Wall(IList<Vector2> ring, float y0, float y1, int sub)
        {
            float u = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % ring.Count];
                float len = Vector2.Distance(a, b);
                Quad(new Vector3(a.x, y0, a.y), new Vector3(b.x, y0, b.y), new Vector3(b.x, y1, b.y), new Vector3(a.x, y1, a.y),
                    new Vector2(u, y0), new Vector2(u + len, y0), new Vector2(u + len, y1), new Vector2(u, y1), sub);
                u += len;
            }
        }

        /// <summary>Band between two rings with the same vertex count (an eave step), from (ring0, y0) to (ring1, y1).</summary>
        public void Band(IList<Vector2> ring0, float y0, IList<Vector2> ring1, float y1, int sub)
        {
            for (int i = 0; i < ring0.Count; i++)
            {
                int j = (i + 1) % ring0.Count;
                Vector3 a = new Vector3(ring0[i].x, y0, ring0[i].y), b = new Vector3(ring0[j].x, y0, ring0[j].y);
                Vector3 c = new Vector3(ring1[j].x, y1, ring1[j].y), d = new Vector3(ring1[i].x, y1, ring1[i].y);
                Quad(a, b, c, d, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), new Vector2(d.x, d.z), sub);
            }
        }

        /// <summary>A flat cap at height y over an anticlockwise outer ring with clockwise holes, facing up.</summary>
        public void Cap(List<Vector2> outer, List<List<Vector2>> holes, float y, int sub, bool faceDown = false)
        {
            var poly = holes != null && holes.Count > 0 ? Bridge(outer, holes) : outer;
            var tris = Triangulate(poly);
            int first = V.Count;
            foreach (var q in poly) Vert(new Vector3(q.x, y, q.y), q);
            for (int i = 0; i < tris.Count; i += 3)
            {
                if (faceDown) Tri(first + tris[i], first + tris[i + 2], first + tris[i + 1], sub);
                else Tri(first + tris[i], first + tris[i + 1], first + tris[i + 2], sub);
            }
        }

        // ---------- polygons ----------

        public static float SignedArea(IList<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) a += p[i].x * p[(i + 1) % p.Count].y - p[(i + 1) % p.Count].x * p[i].y;
            return a * 0.5f;
        }

        public static List<Vector2> Oriented(IEnumerable<Vector2> pts, bool anticlockwise)
        {
            var r = new List<Vector2>(pts);
            if ((SignedArea(r) > 0f) != anticlockwise) r.Reverse();
            return r;
        }

        public static Vector2 Centroid(IList<Vector2> p)
        {
            var c = Vector2.zero;
            foreach (var q in p) c += q;
            return p.Count > 0 ? c / p.Count : c;
        }

        /// <summary>Removes points closer than <paramref name="minStep"/> to their predecessor.</summary>
        public static List<Vector2> Clean(IList<Vector2> p, float minStep = 0.3f)
        {
            var r = new List<Vector2>();
            foreach (var q in p)
                if (r.Count == 0 || Vector2.Distance(r[r.Count - 1], q) > minStep) r.Add(q);
            if (r.Count > 2 && Vector2.Distance(r[0], r[r.Count - 1]) <= minStep) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>Densifies so no edge is longer than <paramref name="maxStep"/> (smoother eaves on long edges).</summary>
        public static List<Vector2> Densify(IList<Vector2> p, float maxStep)
        {
            var r = new List<Vector2>();
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / maxStep));
                for (int k = 0; k < n; k++) r.Add(Vector2.Lerp(a, b, k / (float)n));
            }
            return r;
        }

        /// <summary>Moves every vertex <paramref name="d"/> metres to the left of the ring (into the solid).</summary>
        public static List<Vector2> Offset(IList<Vector2> p, float d)
        {
            var r = new List<Vector2>(p.Count);
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 prev = p[(i - 1 + p.Count) % p.Count], cur = p[i], next = p[(i + 1) % p.Count];
                Vector2 e0 = (cur - prev).normalized, e1 = (next - cur).normalized;
                Vector2 n0 = new Vector2(-e0.y, e0.x), n1 = new Vector2(-e1.y, e1.x);
                Vector2 n = (n0 + n1).normalized;
                float k = Mathf.Max(0.5f, Vector2.Dot(n, n1));
                r.Add(cur + n * (d / k));
            }
            return r;
        }

        /// <summary>Removes the small loops an inset leaves at tight corners.</summary>
        public static List<Vector2> RemoveLoops(List<Vector2> p)
        {
            var r = new List<Vector2>(p);
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 400)
            {
                changed = false;
                for (int i = 0; i < r.Count && !changed; i++)
                    for (int k = 2; k <= 24 && k < r.Count - 1 && !changed; k++)
                    {
                        int j = (i + k) % r.Count;
                        if (!SegmentHit(r[i], r[(i + 1) % r.Count], r[j], r[(j + 1) % r.Count], out var hit)) continue;
                        var next = new List<Vector2>();
                        for (int m = 0; m < r.Count; m++)
                        {
                            int off = (m - i - 1 + r.Count) % r.Count;
                            if (off < k) continue;
                            next.Add(r[m]);
                            if (m == i) next.Add(hit);
                        }
                        r = next;
                        changed = true;
                    }
            }
            return r;
        }

        public static bool SegmentHit(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 hit)
        {
            hit = default;
            Vector2 r = b - a, s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / den, u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / den;
            if (t <= 0f || t >= 1f || u <= 0f || u >= 1f) return false;
            hit = a + r * t;
            return true;
        }

        public static bool Inside(IList<Vector2> poly, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > q.y) != (poly[j].y > q.y) && q.x < (poly[j].x - poly[i].x) * (q.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>Inside the outer ring and outside every hole.</summary>
        public static bool InsideSolid(IList<Vector2> outer, List<List<Vector2>> holes, Vector2 q)
        {
            if (!Inside(outer, q)) return false;
            if (holes != null)
                foreach (var h in holes)
                    if (Inside(h, q)) return false;
            return true;
        }

        /// <summary>
        /// Joins clockwise holes into an anticlockwise outer ring with zero-width bridges (from each hole's
        /// right-most vertex to the nearest outer vertex it can see), giving one simple polygon to triangulate.
        /// </summary>
        public static List<Vector2> Bridge(List<Vector2> outer, List<List<Vector2>> holes)
        {
            var poly = new List<Vector2>(outer);
            var ordered = new List<List<Vector2>>(holes);
            ordered.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
            foreach (var hole in ordered)
            {
                int hi = 0;
                for (int i = 1; i < hole.Count; i++)
                    if (hole[i].x > hole[hi].x) hi = i;
                Vector2 h = hole[hi];
                // Nearest polygon vertex whose segment to h crosses no edge.
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < poly.Count; i++)
                {
                    float d = (poly[i] - h).sqrMagnitude;
                    if (d >= bestD) continue;
                    bool blocked = false;
                    for (int e = 0; e < poly.Count && !blocked; e++)
                    {
                        int f = (e + 1) % poly.Count;
                        if (e == i || f == i) continue;
                        blocked = SegmentHit(h, poly[i], poly[e], poly[f], out _);
                    }
                    for (int e = 0; e < hole.Count && !blocked; e++)
                    {
                        int f = (e + 1) % hole.Count;
                        if (e == hi || f == hi) continue;
                        blocked = SegmentHit(h, poly[i], hole[e], hole[f], out _);
                    }
                    if (blocked) continue;
                    best = i;
                    bestD = d;
                }
                if (best < 0) continue;
                var spliced = new List<Vector2>(poly.Count + hole.Count + 2);
                for (int i = 0; i <= best; i++) spliced.Add(poly[i]);
                for (int k = 0; k <= hole.Count; k++) spliced.Add(hole[(hi + k) % hole.Count]);
                for (int i = best; i < poly.Count; i++) spliced.Add(poly[i]);
                poly = spliced;
            }
            return poly;
        }

        private static float MaxX(List<Vector2> p)
        {
            float m = float.MinValue;
            foreach (var q in p) m = Mathf.Max(m, q.x);
            return m;
        }

        /// <summary>Ear clipping of a simple anticlockwise polygon (bridged holes allowed); returns index triples.</summary>
        public static List<int> Triangulate(List<Vector2> poly)
        {
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            var tris = new List<int>();
            int guard = poly.Count * poly.Count;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i - 1 + idx.Count) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (Mathf.Abs(cross) < 1e-4f && (b - a).sqrMagnitude < 1e-6f)
                    {
                        idx.RemoveAt(i);
                        clipped = true;
                        break;
                    }
                    if (cross <= 1e-4f) continue;
                    bool any = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        var q = poly[j];
                        // Bridge duplicates sit exactly on a corner: they don't block the ear.
                        if ((q - a).sqrMagnitude < 1e-6f || (q - b).sqrMagnitude < 1e-6f || (q - c).sqrMagnitude < 1e-6f) continue;
                        if (InTriangle(q, a, b, c)) { any = true; break; }
                    }
                    if (any) continue;
                    tris.AddRange(new[] { ia, ib, ic });
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // Degenerate remainder (collinear run): drop the flattest vertex and carry on.
                    int flat = 0;
                    float best = float.MaxValue;
                    for (int i = 0; i < idx.Count; i++)
                    {
                        Vector2 a = poly[idx[(i - 1 + idx.Count) % idx.Count]], b = poly[idx[i]], c = poly[idx[(i + 1) % idx.Count]];
                        float cr = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                        if (cr < best) { best = cr; flat = i; }
                    }
                    idx.RemoveAt(flat);
                }
            }
            if (idx.Count == 3) tris.AddRange(new[] { idx[0], idx[1], idx[2] });
            return tris;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
