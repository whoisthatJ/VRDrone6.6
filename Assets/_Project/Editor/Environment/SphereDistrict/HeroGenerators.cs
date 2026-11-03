using System.Collections.Generic;
using GateRush.Track.Geo;
using UnityEngine;

namespace GateRush.EditorTools.SphereDistrict
{
    /// <summary>
    /// Pillow pavilions (Sphere District E2): the four petal pavilions from their OSM outlines and courtyard holes. A
    /// dark glazed ground floor, curtain-wall upper floors, a rounded "pillow" roof edge (quarter round in five steps),
    /// a flat metal roof and radial skylight strips clipped to the roof. Submeshes: 0 curtain wall, 1 metal roof,
    /// 2 skylights, 3 dark ground-floor glazing.
    /// </summary>
    public static class PillowPavilionGenerator
    {
        public const float Height = 24f, EdgeRadius = 8f, GroundFloor = 4.5f;

        public static Mesh Build(GeoBuilding b, string name)
        {
            var outer = PolyMesh.Oriented(PolyMesh.Densify(PolyMesh.Clean(b.Footprint), 4f), true);
            var holes = new List<List<Vector2>>();
            foreach (var h in b.Holes) holes.Add(PolyMesh.Oriented(PolyMesh.Densify(PolyMesh.Clean(h.Points), 4f), false));

            var pm = new PolyMesh();
            pm.EnsureSubmeshes(4);
            float wallTop = Height - EdgeRadius;
            foreach (var ring in Rings(outer, holes))
            {
                pm.Wall(ring, 0f, GroundFloor, 3);
                pm.Wall(ring, GroundFloor, wallTop, 0);
            }
            // Pillow edge: every ring moves into the solid while rising.
            var curOuter = outer;
            var curHoles = holes;
            float prevY = wallTop;
            for (int s = 1; s <= 5; s++)
            {
                float a = s / 5f * Mathf.PI * 0.5f;
                float inset = EdgeRadius * (1f - Mathf.Cos(a)), y = wallTop + EdgeRadius * Mathf.Sin(a);
                var nextOuter = PolyMesh.Offset(outer, inset);
                pm.Band(curOuter, prevY, nextOuter, y, 1);
                var nextHoles = new List<List<Vector2>>();
                for (int k = 0; k < holes.Count; k++)
                {
                    var nh = PolyMesh.Offset(holes[k], inset);
                    pm.Band(curHoles[k], prevY, nh, y, 1);
                    nextHoles.Add(nh);
                }
                curOuter = nextOuter;
                curHoles = nextHoles;
                prevY = y;
            }
            var roofOuter = PolyMesh.RemoveLoops(curOuter);
            var roofHoles = new List<List<Vector2>>();
            foreach (var h in curHoles) roofHoles.Add(PolyMesh.RemoveLoops(h));
            pm.Cap(roofOuter, roofHoles, Height, 1);

            // Skylight strips on lines from the sphere (radial), 2.4 m wide, every 4.5 degrees, kept 6 m off the edges.
            var keepOuter = PolyMesh.RemoveLoops(PolyMesh.Offset(roofOuter, 6f));
            var keepHoles = new List<List<Vector2>>();
            foreach (var h in roofHoles) keepHoles.Add(PolyMesh.RemoveLoops(PolyMesh.Offset(h, 6f)));
            var c = PolyMesh.Centroid(outer);
            float bearing0 = Mathf.Atan2(c.x, c.y) * Mathf.Rad2Deg;
            for (float db = -40f; db <= 40f; db += 5.5f)
            {
                float a = (bearing0 + db) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                var side = new Vector2(dir.y, -dir.x) * 2.6f;
                float runStart = -1f;
                for (float r = 60f; r <= 360f; r += 2f)
                {
                    bool inside = PolyMesh.InsideSolid(keepOuter, keepHoles, dir * r - side) && PolyMesh.InsideSolid(keepOuter, keepHoles, dir * r + side);
                    if (inside && runStart < 0f) runStart = r;
                    if ((!inside || r >= 360f) && runStart >= 0f)
                    {
                        float r1 = r - 2f;
                        if (r1 - runStart > 4f)
                        {
                            Vector2 p0 = dir * runStart - side, p1 = dir * runStart + side, p2 = dir * r1 + side, p3 = dir * r1 - side;
                            float y = Height + 0.06f;
                            pm.Quad(new Vector3(p0.x, y, p0.y), new Vector3(p1.x, y, p1.y), new Vector3(p2.x, y, p2.y), new Vector3(p3.x, y, p3.y), p0, p1, p2, p3, 2);
                        }
                        runStart = -1f;
                    }
                }
            }
            return pm.Build(name);
        }

        private static IEnumerable<List<Vector2>> Rings(List<Vector2> outer, List<List<Vector2>> holes)
        {
            yield return outer;
            foreach (var h in holes) yield return h;
        }
    }

    /// <summary>
    /// Lens pods and the podium (E2): an extruded footprint with a dark ground band, a glass band and a pillow dome
    /// roof that rises <paramref name="dome"/> metres while shrinking. Submeshes: 0 glass, 1 metal roof, 2 dark glass.
    /// </summary>
    public static class PodGenerator
    {
        public static Mesh Build(GeoBuilding b, string name, float wall, float dome, float shrink)
        {
            var outer = PolyMesh.Oriented(PolyMesh.Densify(PolyMesh.Clean(b.Footprint), 3f), true);
            var pm = new PolyMesh();
            pm.EnsureSubmeshes(3);
            pm.Wall(outer, 0f, 3.2f, 2);
            pm.Wall(outer, 3.2f, wall, 0);
            var c = PolyMesh.Centroid(outer);
            var prev = outer;
            float prevY = wall;
            const int steps = 6;
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                float k = 1f - shrink * (1f - Mathf.Cos(t * Mathf.PI * 0.5f));
                float y = wall + dome * Mathf.Sin(t * Mathf.PI * 0.5f);
                var ring = new List<Vector2>(outer.Count);
                foreach (var p in outer) ring.Add(c + (p - c) * k);
                pm.Band(prev, prevY, ring, y, 1);
                prev = ring;
                prevY = y;
            }
            pm.Cap(prev, null, prevY, 1);
            return pm.Build(name);
        }
    }

    /// <summary>
    /// The canopy south of the sphere (E2): a thin glazed slab at 6.5 m over the OSM "roof" footprint with a steel
    /// fascia, a white soffit and radial ribs, on slender columns: open underneath (flyable). Submeshes: 0 glass,
    /// 1 steel, 2 soffit.
    /// </summary>
    public static class CanopyGenerator
    {
        public const float Height = 6.5f, Thickness = 0.6f;

        public static Mesh Build(GeoBuilding b, string name, out List<Vector2> columns)
        {
            var outer = PolyMesh.Oriented(PolyMesh.Densify(PolyMesh.Clean(b.Footprint), 3f), true);
            var pm = new PolyMesh();
            pm.EnsureSubmeshes(3);
            pm.Cap(outer, null, Height + Thickness, 0);
            pm.Cap(outer, null, Height, 2, faceDown: true);
            pm.Wall(outer, Height, Height + Thickness, 1);
            // Radial ribs from the sphere across the slab.
            for (float bearing = 0f; bearing < 360f; bearing += 6f)
            {
                float a = bearing * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                var side = new Vector2(dir.y, -dir.x) * 0.2f;
                float r0 = -1f;
                for (float r = 20f; r <= 140f; r += 1f)
                {
                    bool inside = PolyMesh.Inside(outer, dir * r);
                    if (inside && r0 < 0f) r0 = r;
                    if ((!inside || r >= 140f) && r0 >= 0f)
                    {
                        float r1 = r - 1f, y = Height + Thickness + 0.25f;
                        if (r1 - r0 > 2f)
                        {
                            Vector2 p0 = dir * r0 - side, p1 = dir * r0 + side, p2 = dir * r1 + side, p3 = dir * r1 - side;
                            pm.Quad(new Vector3(p0.x, y, p0.y), new Vector3(p1.x, y, p1.y), new Vector3(p2.x, y, p2.y), new Vector3(p3.x, y, p3.y), p0, p1, p2, p3, 1);
                        }
                        r0 = -1f;
                    }
                }
            }
            // Columns on a 14 m grid inside the slab (kept 3 m from its edge).
            columns = new List<Vector2>();
            var inner = PolyMesh.RemoveLoops(PolyMesh.Offset(outer, 3f));
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in outer)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            for (float x = min.x; x <= max.x; x += 14f)
                for (float z = min.y; z <= max.y; z += 14f)
                {
                    var p = new Vector2(x, z);
                    // Keep the course's orbit round the sphere clear (5 m either side).
                    if (Mathf.Abs(p.magnitude - GateRush.EditorTools.Builders.RouteLibrary.OrbitRadius) < 5f) continue;
                    if (PolyMesh.Inside(inner, p)) columns.Add(p);
                }
            return pm.Build(name);
        }
    }

    /// <summary>
    /// The central glass sphere (E2): 80 m, centre 60 m above the plinth, rising out of a glazed base drum. The north
    /// hemisphere is clear (the eight floor slabs show through), the south one fritted, with photovoltaic tiles high
    /// up; horizontal mullions at every floor with LED strips, vertical mullions every 7.5 degrees, and the oval scoop
    /// near the top facing south-west with its recessed bowl. Submeshes: 0 clear glass, 1 fritted glass, 2 PV tiles,
    /// 3 dark scoop glass, 4 steel, 5 LED strips, 6 floor slabs.
    /// </summary>
    public static class SphereGenerator
    {
        public const float Radius = 40f, CentreY = 60f, BaseRadius = 26f, BaseHeight = 24f;
        public static readonly float[] Floors = { 24f, 33f, 42f, 51f, 60f, 69f, 78f, 87f };
        /// <summary>Scoop axis: bearing and elevation (degrees) of the opening's centre, and its half-sizes (degrees).</summary>
        public const float ScoopBearing = 210f, ScoopElevation = 58f, ScoopHalfA = 17f, ScoopHalfB = 11f;

        public static Vector3 Centre => new Vector3(0f, CentreY, 0f);

        public static Vector3 ScoopAxis
        {
            get
            {
                float b = ScoopBearing * Mathf.Deg2Rad, e = ScoopElevation * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(b) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(b) * Mathf.Cos(e));
            }
        }

        /// <summary>Where a unit direction from the centre falls in the scoop ellipse (&lt; 1 inside).</summary>
        public static float ScoopMetric(Vector3 dir)
        {
            var axis = ScoopAxis;
            var right = Vector3.Cross(Vector3.up, axis).normalized;
            var up = Vector3.Cross(axis, right);
            float along = Vector3.Dot(dir, axis);
            if (along <= 0f) return float.MaxValue;
            float x = Mathf.Atan2(Vector3.Dot(dir, right), along) * Mathf.Rad2Deg / ScoopHalfA;
            float y = Mathf.Atan2(Vector3.Dot(dir, up), along) * Mathf.Rad2Deg / ScoopHalfB;
            return x * x + y * y;
        }

        public static Mesh Build(string name)
        {
            var pm = new PolyMesh();
            pm.EnsureSubmeshes(7);
            var c = Centre;
            const int lat = 48, lon = 120;
            float minLat = Mathf.Asin((BaseHeight - 0.5f - CentreY) / Radius);
            Vector3 Dir(float t, float p) => new Vector3(Mathf.Cos(t) * Mathf.Sin(p), Mathf.Sin(t), Mathf.Cos(t) * Mathf.Cos(p));
            for (int i = 0; i < lat; i++)
            {
                float t0 = Mathf.Lerp(minLat, Mathf.PI / 2f, i / (float)lat), t1 = Mathf.Lerp(minLat, Mathf.PI / 2f, (i + 1) / (float)lat);
                for (int j = 0; j < lon; j++)
                {
                    float p0 = 2f * Mathf.PI * j / lon, p1 = 2f * Mathf.PI * (j + 1) / lon;
                    Vector3 mid = Dir((t0 + t1) * 0.5f, (p0 + p1) * 0.5f);
                    if (ScoopMetric(mid) < 1f) continue;          // the scoop opening
                    int sub = mid.z > 0f ? 0 : (t0 > 0.75f && (i + j) % 3 != 0 ? 2 : 1);
                    Vector3 a = c + Dir(t0, p0) * Radius, b = c + Dir(t0, p1) * Radius, d = c + Dir(t1, p1) * Radius, e = c + Dir(t1, p0) * Radius;
                    Vector2 Uv(float t, float p) => new Vector2(p * Radius * Mathf.Cos((t0 + t1) * 0.5f), t * Radius);
                    pm.Quad(a, e, d, b, Uv(t0, p0), Uv(t1, p0), Uv(t1, p1), Uv(t0, p1), sub);
                }
            }
            // Recessed bowl inside the scoop: the same patch on a smaller sphere, plus the side walls.
            const float inner = Radius - 5f;
            for (int i = 0; i < lat; i++)
            {
                float t0 = Mathf.Lerp(0f, Mathf.PI / 2f, i / (float)lat), t1 = Mathf.Lerp(0f, Mathf.PI / 2f, (i + 1) / (float)lat);
                for (int j = 0; j < lon; j++)
                {
                    float p0 = 2f * Mathf.PI * j / lon, p1 = 2f * Mathf.PI * (j + 1) / lon;
                    if (ScoopMetric(Dir((t0 + t1) * 0.5f, (p0 + p1) * 0.5f)) >= 1f) continue;
                    Vector3 a = c + Dir(t0, p0) * inner, b = c + Dir(t0, p1) * inner, d = c + Dir(t1, p1) * inner, e = c + Dir(t1, p0) * inner;
                    pm.Quad(a, e, d, b, new Vector2(p0 * inner, t0 * inner), new Vector2(p0 * inner, t1 * inner), new Vector2(p1 * inner, t1 * inner), new Vector2(p1 * inner, t0 * inner), 3);
                    // Side walls where a scoop cell borders the shell.
                    foreach (var (q0, q1, nt, np) in new[] { (Dir(t0, p0), Dir(t0, p1), t0 - 0.01f, (p0 + p1) * 0.5f), (Dir(t1, p1), Dir(t1, p0), t1 + 0.01f, (p0 + p1) * 0.5f), (Dir(t0, p1), Dir(t1, p1), (t0 + t1) * 0.5f, p1 + 0.01f), (Dir(t1, p0), Dir(t0, p0), (t0 + t1) * 0.5f, p0 - 0.01f) })
                    {
                        if (ScoopMetric(Dir(nt, np)) < 1f) continue;
                        pm.Quad(c + q0 * inner, c + q1 * inner, c + q1 * Radius, c + q0 * Radius, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 4);
                    }
                }
            }
            // Horizontal mullions at every floor (a 0.35 m steel band) with an LED strip just below.
            foreach (float y in Floors)
            {
                if (y <= BaseHeight) continue;
                float r = Mathf.Sqrt(Radius * Radius - (y - CentreY) * (y - CentreY)) + 0.12f;
                Ring(pm, r, y, 0.35f, 4);
                Ring(pm, r + 0.02f, y - 0.25f, 0.15f, 5);
            }
            // Vertical mullions every 7.5 degrees.
            for (int m = 0; m < 48; m++)
            {
                float p = m * Mathf.PI * 2f / 48f, w = 0.18f / Radius;
                for (int i = 0; i < lat; i++)
                {
                    float t0 = Mathf.Lerp(minLat, Mathf.PI / 2f - 0.05f, i / (float)lat), t1 = Mathf.Lerp(minLat, Mathf.PI / 2f - 0.05f, (i + 1) / (float)lat);
                    if (ScoopMetric(Dir((t0 + t1) * 0.5f, p)) < 1.1f) continue;
                    float r = Radius + 0.1f;
                    Vector3 a = c + Dir(t0, p - w / Mathf.Cos(t0)) * r, b = c + Dir(t0, p + w / Mathf.Cos(t0)) * r;
                    Vector3 d = c + Dir(t1, p + w / Mathf.Cos(t1)) * r, e = c + Dir(t1, p - w / Mathf.Cos(t1)) * r;
                    pm.Quad(a, e, d, b, Vector2.zero, Vector2.up, Vector2.one, Vector2.right, 4);
                }
            }
            // Floor slabs inside (seen through the clear hemisphere).
            foreach (float y in Floors)
            {
                float r = Mathf.Sqrt(Mathf.Max(0f, Radius * Radius - (y - CentreY) * (y - CentreY))) - 1.5f;
                if (r < 4f) continue;
                var disc = new List<Vector2>();
                for (int k = 0; k < 48; k++) disc.Add(new Vector2(Mathf.Sin(-k * Mathf.PI * 2f / 48f), Mathf.Cos(-k * Mathf.PI * 2f / 48f)) * r);
                disc = PolyMesh.Oriented(disc, true);
                pm.Cap(disc, null, y + 0.2f, 6);
                pm.Cap(disc, null, y - 0.2f, 6, faceDown: true);
            }
            return pm.Build(name);
        }

        private static void Ring(PolyMesh pm, float r, float y, float h, int sub)
        {
            const int n = 120;
            for (int k = 0; k < n; k++)
            {
                float a0 = -k * Mathf.PI * 2f / n, a1 = -(k + 1) * Mathf.PI * 2f / n;
                var p = new Vector3(Mathf.Sin(a0) * r, 0f, Mathf.Cos(a0) * r);
                var q = new Vector3(Mathf.Sin(a1) * r, 0f, Mathf.Cos(a1) * r);
                pm.Quad(p + Vector3.up * (y - h * 0.5f), q + Vector3.up * (y - h * 0.5f), q + Vector3.up * (y + h * 0.5f), p + Vector3.up * (y + h * 0.5f),
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, sub);
            }
        }

        /// <summary>The glazed base drum the sphere rises from. Submeshes: 0 glass, 1 roof.</summary>
        public static Mesh BuildBase(string name)
        {
            var pm = new PolyMesh();
            pm.EnsureSubmeshes(2);
            var ring = new List<Vector2>();
            for (int k = 0; k < 72; k++) ring.Add(new Vector2(Mathf.Sin(k * Mathf.PI * 2f / 72f), Mathf.Cos(k * Mathf.PI * 2f / 72f)) * BaseRadius);
            ring = PolyMesh.Oriented(ring, true);
            pm.Wall(ring, 0f, BaseHeight, 0);
            pm.Cap(ring, null, BaseHeight, 1);
            return pm.Build(name);
        }

        /// <summary>A wind turbine for the scoop: tower stub + nacelle (submesh 0) and a three-blade rotor (separate mesh).</summary>
        public static (Mesh body, Mesh rotor) Turbine()
        {
            var body = new List<(Mesh, Matrix4x4, int)>
            {
                GateRush.EditorTools.Art.ProcMesh.Part(new Vector3(0f, 2f, 0f), new Vector3(0.6f, 4f, 0.6f), Quaternion.identity),
                GateRush.EditorTools.Art.ProcMesh.Part(new Vector3(0f, 4.2f, -0.5f), new Vector3(1.1f, 1.1f, 2.6f), Quaternion.identity),
            };
            var blades = new List<(Mesh, Matrix4x4, int)>();
            for (int k = 0; k < 3; k++)
                blades.Add(GateRush.EditorTools.Art.ProcMesh.Part(Quaternion.Euler(0f, 0f, k * 120f) * new Vector3(0f, 3.4f, 0f), new Vector3(0.7f, 6.6f, 0.12f), Quaternion.Euler(0f, 0f, k * 120f)));
            blades.Add(GateRush.EditorTools.Art.ProcMesh.Part(Vector3.zero, new Vector3(0.9f, 0.9f, 0.9f), Quaternion.identity));
            return (GateRush.EditorTools.Art.ProcMesh.Combine("SD_TurbineBody", body), GateRush.EditorTools.Art.ProcMesh.Combine("SD_TurbineRotor", blades));
        }
    }

    /// <summary>
    /// Landmark buildings round the complex (E2), from their OSM footprints: the round congress arena (glass drum,
    /// low dome with star ribs), the long exhibition hall (metal cladding, pillow-edged roof), the hotel (glass) and the
    /// independence monument needle on the park axis.
    /// </summary>
    public static class LandmarkGenerator
    {
        public static Mesh Arena(GeoBuilding b, string name)
        {
            var outer = PolyMesh.Oriented(PolyMesh.Densify(PolyMesh.Clean(b.Footprint), 3f), true);
            var pm = new PolyMesh();
            pm.EnsureSubmeshes(3);
            pm.Wall(outer, 0f, 4f, 2);
            pm.Wall(outer, 4f, 20f, 0);
            var c = PolyMesh.Centroid(outer);
            var prev = outer;
            float prevY = 20f;
            for (int s = 1; s <= 6; s++)
            {
                float t = s / 6f;
                float k = Mathf.Cos(t * Mathf.PI * 0.5f) * 0.95f + 0.05f;
                float y = 20f + 9f * Mathf.Sin(t * Mathf.PI * 0.5f);
                var ring = new List<Vector2>();
                foreach (var p in outer) ring.Add(c + (p - c) * k);
                pm.Band(prev, prevY, ring, y, 1);
                prev = ring;
                prevY = y;
            }
            pm.Cap(prev, null, prevY, 1);
            return pm.Build(name);
        }

        public static Mesh Extruded(GeoBuilding b, string name, float height, float edge, int wallSub = 0, int roofSub = 1)
        {
            var outer = PolyMesh.Oriented(PolyMesh.Densify(PolyMesh.Clean(b.Footprint), 4f), true);
            var pm = new PolyMesh();
            pm.EnsureSubmeshes(3);
            pm.Wall(outer, 0f, 4f, 2);
            pm.Wall(outer, 4f, height - edge, wallSub);
            var prev = outer;
            float prevY = height - edge;
            for (int s = 1; s <= 4 && edge > 0f; s++)
            {
                float a = s / 4f * Mathf.PI * 0.5f;
                var ring = PolyMesh.Offset(outer, edge * (1f - Mathf.Cos(a)));
                float y = height - edge + edge * Mathf.Sin(a);
                pm.Band(prev, prevY, ring, y, roofSub);
                prev = ring;
                prevY = y;
            }
            pm.Cap(PolyMesh.RemoveLoops(prev), null, height, roofSub);
            return pm.Build(name);
        }

        public static Mesh Needle(Vector2 at, float height, string name)
        {
            var parts = new List<(Mesh, Matrix4x4, int)>
            {
                (GateRush.EditorTools.Art.ProcMesh.Cone(height - 3f, 1.5f, 0.12f, 16), Matrix4x4.Translate(new Vector3(at.x, 1.2f, at.y)), 0),
                (GateRush.EditorTools.Art.ProcMesh.Cone(3.2f, 0.32f, 0.02f, 12), Matrix4x4.Translate(new Vector3(at.x, height - 1.9f, at.y)), 1),
                GateRush.EditorTools.Art.ProcMesh.Part(new Vector3(at.x, 0.6f, at.y), new Vector3(12f, 1.2f, 12f), Quaternion.Euler(0f, 12f, 0f), 0),
            };
            return GateRush.EditorTools.Art.ProcMesh.Combine(name, parts);
        }
    }
}
