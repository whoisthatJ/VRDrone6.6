using System.Collections.Generic;
using GateRush.Core;
using GateRush.EditorTools.Art;
using GateRush.Track.Geo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.SphereDistrict
{
    /// <summary>
    /// Sphere District E3: the ground and streetscape from the OSM data. Roads as asphalt ribbons with edge, centre
    /// and lane markings and kerbs; footways, cycleways and paths as paving ribbons with zebra crossings; areas (grass,
    /// parks, woods, parking, pedestrian squares, playgrounds, water); and GPU-instanced props: street and park lamps,
    /// benches, parked cars, trees (OSM trees, tree rows, woods, park planting) and the solar field. Everything is cut
    /// into 600 m cells (few draw submissions); props keep clear of the race corridor. Colliders: kerbs are low, lamps and cars get combined
    /// collider meshes per cell on TrackGeometry.
    /// </summary>
    public sealed class StreetscapeGenerator
    {
        public const float Cell = 600f, Extent = 1500f;
        private const int SubAsphalt = 0, SubPaint = 1, SubKerb = 2, SubPaving = 3, SubLawn = 4, SubPitch = 5, SubRubber = 6, SubWater = 7, SubDirt = 8;
        private static readonly string[] SurfaceMats =
            { "M_Expo_Asphalt", "M_Stadium_LinePaint", "M_Concrete_Wall", "M_Expo_Paving", "M_Expo_Lawn", "M_Stadium_Grass_Dark", "M_Stadium_Tartan", "M_Expo_Water", "M_Dirt_Field" };

        private readonly GeoData _data;
        private readonly List<Vector2> _corridor;
        private readonly Dictionary<Vector2Int, List<Vector2>> _corridorGrid = new Dictionary<Vector2Int, List<Vector2>>();
        private readonly Dictionary<Vector2Int, PolyMesh> _surfaces = new Dictionary<Vector2Int, PolyMesh>();
        private readonly Dictionary<Vector2Int, List<GeoBuilding>> _buildingGrid = new Dictionary<Vector2Int, List<GeoBuilding>>();
        private readonly System.Random _rng = new System.Random(2017);

        // Instanced prop lists, by (prop id, cell).
        private readonly Dictionary<(string, Vector2Int), List<Matrix4x4>> _props = new Dictionary<(string, Vector2Int), List<Matrix4x4>>();
        private readonly Dictionary<Vector2Int, List<(Mesh, Matrix4x4, int)>> _colliders = new Dictionary<Vector2Int, List<(Mesh, Matrix4x4, int)>>();
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

        /// <param name="corridors">Race flight lines, each a closed loop (props keep 7 m clear of them).</param>
        public StreetscapeGenerator(GeoData data, IEnumerable<IList<Vector3>> corridors)
        {
            _data = data;
            _corridor = new List<Vector2>();
            foreach (var corridor in corridors)
            for (int i = 0; i < corridor.Count; i++)
            {
                Vector3 a = corridor[i], b = corridor[(i + 1) % corridor.Count];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 2f));
                for (int k = 0; k < n; k++)
                {
                    var p = Vector3.Lerp(a, b, k / (float)n);
                    var q = new Vector2(p.x, p.z);
                    _corridor.Add(q);
                    var key = Key(q, 20f);
                    if (!_corridorGrid.TryGetValue(key, out var list)) _corridorGrid[key] = list = new List<Vector2>();
                    list.Add(q);
                }
            }
            foreach (var bld in data.Buildings)
            {
                var c = PolyMesh.Centroid(bld.Footprint);
                var key = Key(c, 200f);
                if (!_buildingGrid.TryGetValue(key, out var list)) _buildingGrid[key] = list = new List<GeoBuilding>();
                list.Add(bld);
            }
        }

        private static Vector2Int Key(Vector2 p, float size) => new Vector2Int(Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.y / size));

        private bool InRange(Vector2 p) => Mathf.Abs(p.x) < Extent && Mathf.Abs(p.y) < Extent;

        /// <summary>Within <paramref name="clear"/> metres of the race corridor.</summary>
        public bool NearCorridor(Vector2 p, float clear = 7f)
        {
            var k = Key(p, 20f);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (_corridorGrid.TryGetValue(new Vector2Int(k.x + dx, k.y + dy), out var list))
                        foreach (var q in list)
                            if ((q - p).sqrMagnitude < clear * clear) return true;
            return false;
        }

        private bool InBuilding(Vector2 p)
        {
            var k = Key(p, 200f);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (_buildingGrid.TryGetValue(new Vector2Int(k.x + dx, k.y + dy), out var list))
                        foreach (var b in list)
                            if (PolyMesh.Inside(b.Footprint, p)) return true;
            return false;
        }

        private PolyMesh Surface(Vector2 at)
        {
            var key = Key(at, Cell);
            if (!_surfaces.TryGetValue(key, out var pm))
            {
                _surfaces[key] = pm = new PolyMesh();
                pm.EnsureSubmeshes(SurfaceMats.Length);
            }
            return pm;
        }

        private void Prop(string id, Vector2 at, float yaw, float scale = 1f, float y = 0f)
        {
            var key = (id, Key(at, Cell));
            if (!_props.TryGetValue(key, out var list)) _props[key] = list = new List<Matrix4x4>();
            list.Add(Matrix4x4.TRS(new Vector3(at.x, y, at.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale));
            Counts.TryGetValue(id, out int n);
            Counts[id] = n + 1;
        }

        private void Collider(Vector2 at, Vector3 centre, Vector3 size, float yaw)
        {
            var key = Key(at, Cell);
            if (!_colliders.TryGetValue(key, out var list)) _colliders[key] = list = new List<(Mesh, Matrix4x4, int)>();
            list.Add(ProcMesh.Part(centre, size, Quaternion.Euler(0f, yaw, 0f)));
        }

        // ---------- roads and paths ----------

        private static float RoadHeight(string kind) => kind switch
        {
            "primary" or "primary_link" or "trunk" => 0.07f,
            "secondary" or "secondary_link" => 0.066f,
            "tertiary" or "tertiary_link" => 0.062f,
            "residential" or "unclassified" or "living_street" or "busway" => 0.058f,
            "service" => 0.054f,
            _ => 0.045f,
        };

        private static bool IsFoot(string kind) => kind is "footway" or "path" or "cycleway" or "pedestrian" or "steps" or "track" or "bridleway";

        public void BuildRoads()
        {
            foreach (var r in _data.Roads)
            {
                if (r.Points.Count < 2 || !InRange(r.Points[0])) continue;
                if (r.Kind is "construction" or "proposed" or "services" or "platform") continue;
                bool foot = IsFoot(r.Kind);
                float w = foot ? (r.Kind == "cycleway" ? 2.5f : r.Kind == "pedestrian" ? 6f : 3f) : Mathf.Clamp(r.Width, 5f, 26f);
                float y = foot ? 0.04f : RoadHeight(r.Kind);
                if (foot && r.Crossing)
                {
                    Zebra(r, 4f);
                    continue;
                }
                Ribbon(r.Points, w, y, foot ? SubPaving : SubAsphalt);
                if (foot) continue;
                // Markings: edge lines on wide roads, dashed centre / lane lines.
                if (w >= 7f)
                {
                    Ribbon(OffsetLine(r.Points, w * 0.5f - 0.35f), 0.15f, y + 0.004f, SubPaint);
                    Ribbon(OffsetLine(r.Points, -(w * 0.5f - 0.35f)), 0.15f, y + 0.004f, SubPaint);
                }
                int lanes = r.Lanes > 0 ? r.Lanes : Mathf.Max(1, Mathf.RoundToInt(w / 3.5f));
                if (lanes >= 2)
                    for (int l = 1; l < lanes; l++)
                    {
                        float off = -w * 0.5f + w * l / lanes;
                        bool centre = !r.OneWay && l * 2 == lanes;
                        if (centre) Ribbon(OffsetLine(r.Points, off), 0.15f, y + 0.004f, SubPaint);
                        else Dashes(OffsetLine(r.Points, off), 0.13f, y + 0.004f, 3f, 9f);
                    }
                if (r.Kind is "primary" or "secondary" or "tertiary" or "primary_link" or "secondary_link")
                {
                    Kerb(OffsetLine(r.Points, w * 0.5f + 0.12f));
                    Kerb(OffsetLine(r.Points, -(w * 0.5f + 0.12f)));
                    StreetLights(r, w);
                }
            }
        }

        private void Ribbon(List<Vector2> pts, float width, float y, int sub)
        {
            if (pts.Count < 2) return;
            var left = OffsetLine(pts, width * 0.5f);
            var right = OffsetLine(pts, -width * 0.5f);
            float u = 0f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var pm = Surface(pts[i]);
                float len = Vector2.Distance(pts[i], pts[i + 1]);
                Vector3 a = new Vector3(right[i].x, y, right[i].y), b = new Vector3(right[i + 1].x, y, right[i + 1].y);
                Vector3 c = new Vector3(left[i + 1].x, y, left[i + 1].y), d = new Vector3(left[i].x, y, left[i].y);
                pm.Quad(a, b, c, d, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), new Vector2(d.x, d.z), sub);
                u += len;
            }
        }

        private void Dashes(List<Vector2> pts, float width, float y, float dash, float period)
        {
            float acc = 0f;
            var run = new List<Vector2>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = Vector2.Distance(a, b);
                for (float t = 0f; t < len; t += 1f)
                {
                    bool on = (acc + t) % period < dash;
                    var p = Vector2.Lerp(a, b, t / len);
                    if (on) run.Add(p);
                    else if (run.Count > 0)
                    {
                        if (run.Count >= 2) Ribbon(run, width, y, SubPaint);
                        run = new List<Vector2>();
                    }
                }
                acc += len;
            }
            if (run.Count >= 2) Ribbon(run, width, y, SubPaint);
        }

        private void Kerb(List<Vector2> pts)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var pm = Surface(pts[i]);
                Vector2 a = pts[i], b = pts[i + 1];
                var dir = (b - a).normalized;
                var n = new Vector2(-dir.y, dir.x) * 0.12f;
                // A 0.24 m wide, 0.15 m high kerb (top + both sides).
                Vector3 A(Vector2 p, float h) => new Vector3(p.x, h, p.y);
                pm.Quad(A(a - n, 0.15f), A(b - n, 0.15f), A(b + n, 0.15f), A(a + n, 0.15f), a, b, b, a, SubKerb);
                pm.Quad(A(a + n, 0f), A(a + n, 0.15f), A(b + n, 0.15f), A(b + n, 0f), Vector2.zero, Vector2.up, Vector2.one, Vector2.right, SubKerb);
                pm.Quad(A(b - n, 0f), A(b - n, 0.15f), A(a - n, 0.15f), A(a - n, 0f), Vector2.zero, Vector2.up, Vector2.one, Vector2.right, SubKerb);
            }
        }

        private void Zebra(GeoRoad r, float width)
        {
            var pts = r.Points;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = Vector2.Distance(a, b);
                var dir = (b - a) / Mathf.Max(0.01f, len);
                var n = new Vector2(-dir.y, dir.x) * (width * 0.5f);
                for (float t = 0.3f; t + 0.5f < len; t += 1f)
                {
                    Vector2 p = a + dir * t, q = a + dir * (t + 0.5f);
                    var pm = Surface(p);
                    const float y = 0.075f;
                    pm.Quad(new Vector3((p - n).x, y, (p - n).y), new Vector3((q - n).x, y, (q - n).y), new Vector3((q + n).x, y, (q + n).y), new Vector3((p + n).x, y, (p + n).y),
                        p, q, q, p, SubPaint);
                }
            }
        }

        /// <summary>The polyline moved <paramref name="d"/> metres to its left (mitred).</summary>
        public static List<Vector2> OffsetLine(List<Vector2> p, float d)
        {
            var r = new List<Vector2>(p.Count);
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 e0 = i > 0 ? (p[i] - p[i - 1]).normalized : (p[1] - p[0]).normalized;
                Vector2 e1 = i + 1 < p.Count ? (p[i + 1] - p[i]).normalized : e0;
                Vector2 n0 = new Vector2(-e0.y, e0.x), n1 = new Vector2(-e1.y, e1.x);
                Vector2 n = (n0 + n1).normalized;
                float k = Mathf.Max(0.4f, Vector2.Dot(n, n1));
                r.Add(p[i] + n * (d / k));
            }
            return r;
        }

        // ---------- areas ----------

        public void BuildAreas()
        {
            foreach (var a in _data.Areas)
            {
                if (a.Polygon.Count < 3 || !InRange(a.Polygon[0])) continue;
                int sub;
                float y;
                switch (a.Kind)
                {
                    case "grass": case "park": case "garden": case "recreation_ground": case "meadow": case "village_green": sub = SubLawn; y = 0.015f; break;
                    case "wood": case "forest": case "scrub": sub = SubLawn; y = 0.016f; break;
                    case "pitch": sub = SubPitch; y = 0.02f; break;
                    case "playground": sub = SubRubber; y = 0.022f; break;
                    case "parking": sub = SubAsphalt; y = 0.025f; break;
                    case "pedestrian": sub = SubPaving; y = 0.02f; break;
                    case "water": case "fountain": sub = SubWater; y = 0.03f; break;
                    case "construction": case "brownfield": sub = SubDirt; y = 0.012f; break;
                    default: continue;
                }
                var poly = PolyMesh.Oriented(PolyMesh.Clean(a.Polygon), true);
                if (poly.Count < 3 || Mathf.Abs(PolyMesh.SignedArea(poly)) < 4f) continue;
                Surface(PolyMesh.Centroid(poly)).Cap(poly, null, y, sub);
                Counts.TryGetValue("area_" + sub, out int n);
                Counts["area_" + sub] = n + 1;
                if (a.Kind == "parking") ParkedCars(poly);
                if (a.Kind is "wood" or "forest") Planting(poly, 70f, 0.75f, 1.05f);
                if (a.Kind is "park" or "garden" or "grass" && PolyMesh.Centroid(poly).magnitude > 330f && Mathf.Abs(PolyMesh.SignedArea(poly)) > 400f)
                    Planting(poly, 500f, 0.6f, 0.95f);
            }
        }

        /// <summary>Scattered trees inside a polygon, one per <paramref name="areaPerTree"/> m².</summary>
        private void Planting(List<Vector2> poly, float areaPerTree, float s0, float s1)
        {
            float area = Mathf.Abs(PolyMesh.SignedArea(poly));
            int n = Mathf.Min(400, Mathf.FloorToInt(area / areaPerTree));
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in poly)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            for (int i = 0, tries = 0; i < n && tries < n * 6; tries++)
            {
                var p = new Vector2(Mathf.Lerp(min.x, max.x, (float)_rng.NextDouble()), Mathf.Lerp(min.y, max.y, (float)_rng.NextDouble()));
                if (!PolyMesh.Inside(poly, p) || NearCorridor(p) || InBuilding(p)) continue;
                Tree(p, Mathf.Lerp(s0, s1, (float)_rng.NextDouble()));
                i++;
            }
        }

        private void ParkedCars(List<Vector2> poly)
        {
            // Rows along the polygon's longest edge, 2.6 m bays, 6 m aisles every second row.
            int longest = 0;
            float best = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                float l = Vector2.Distance(poly[i], poly[(i + 1) % poly.Count]);
                if (l > best) { best = l; longest = i; }
            }
            var along = (poly[(longest + 1) % poly.Count] - poly[longest]).normalized;
            var across = new Vector2(-along.y, along.x);
            float yaw = Mathf.Atan2(across.x, across.y) * Mathf.Rad2Deg;
            var inner = PolyMesh.RemoveLoops(PolyMesh.Offset(poly, 2f));
            float aMin = float.MaxValue, aMax = float.MinValue, cMin = float.MaxValue, cMax = float.MinValue;
            foreach (var p in poly)
            {
                aMin = Mathf.Min(aMin, Vector2.Dot(p, along)); aMax = Mathf.Max(aMax, Vector2.Dot(p, along));
                cMin = Mathf.Min(cMin, Vector2.Dot(p, across)); cMax = Mathf.Max(cMax, Vector2.Dot(p, across));
            }
            int row = 0;
            for (float c = cMin + 3f; c < cMax - 2f; c += row % 2 == 0 ? 5.2f : 6.5f, row++)
                for (float a = aMin + 1.5f; a < aMax - 1.5f; a += 2.6f)
                {
                    var p = along * a + across * c;
                    if (!PolyMesh.Inside(inner, p) || _rng.NextDouble() > 0.72 || NearCorridor(p, 5f)) continue;
                    float carYaw = yaw + (row % 2 == 0 ? 0f : 180f) + (float)(_rng.NextDouble() - 0.5) * 4f;
                    Prop("car" + _rng.Next(0, CarColours.Length), p, carYaw);
                    Collider(p, new Vector3(p.x, 0.75f, p.y), new Vector3(1.85f, 1.5f, 4.4f), carYaw);
                }
        }

        // ---------- props ----------

        private void StreetLights(GeoRoad r, float width)
        {
            float acc = 17f;
            for (int i = 0; i + 1 < r.Points.Count; i++)
            {
                Vector2 a = r.Points[i], b = r.Points[i + 1];
                float len = Vector2.Distance(a, b);
                var dir = (b - a) / Mathf.Max(0.01f, len);
                var n = new Vector2(-dir.y, dir.x);
                for (; acc < len; acc += 36f)
                    foreach (float side in new[] { 1f, -1f })
                    {
                        var p = a + dir * acc + n * side * (width * 0.5f + 1.4f);
                        if (NearCorridor(p) || InBuilding(p)) continue;
                        float yaw = Mathf.Atan2(-n.x * side, -n.y * side) * Mathf.Rad2Deg;
                        Prop("streetlight", p, yaw);
                        Collider(p, new Vector3(p.x, 5f, p.y), new Vector3(0.3f, 10f, 0.3f), 0f);
                    }
                acc -= len;
            }
        }

        public void BuildFootwayProps()
        {
            foreach (var r in _data.Roads)
            {
                if (!(r.Kind is "footway" or "path" or "pedestrian") || r.Crossing || r.Points.Count < 2 || !InRange(r.Points[0])) continue;
                float acc = 14f, benchAcc = 26f;
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    Vector2 a = r.Points[i], b = r.Points[i + 1];
                    float len = Vector2.Distance(a, b);
                    var dir = (b - a) / Mathf.Max(0.01f, len);
                    var n = new Vector2(-dir.y, dir.x);
                    for (; acc < len; acc += 30f)
                    {
                        var p = a + dir * acc + n * 2.4f;
                        if (NearCorridor(p) || InBuilding(p)) continue;
                        Prop("parklamp", p, 0f);
                        Collider(p, new Vector3(p.x, 2.25f, p.y), new Vector3(0.2f, 4.5f, 0.2f), 0f);
                    }
                    for (; benchAcc < len; benchAcc += 52f)
                    {
                        var p = a + dir * benchAcc - n * 2.4f;
                        if (NearCorridor(p) || InBuilding(p) || p.magnitude > 900f) continue;
                        Prop("bench", p, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + 90f);
                    }
                    acc -= len;
                    benchAcc -= len;
                }
            }
        }

        public void BuildTrees()
        {
            foreach (var t in _data.Trees)
                if (InRange(t) && !NearCorridor(t) && !InBuilding(t)) Tree(t, 0.8f + (float)_rng.NextDouble() * 0.3f);
            foreach (var a in _data.Areas)
            {
                if (a.Kind != "tree_row" && a.Kind != "tree") continue;
                for (int i = 0; i + 1 < a.Polygon.Count; i++)
                {
                    Vector2 p = a.Polygon[i], q = a.Polygon[i + 1];
                    float len = Vector2.Distance(p, q);
                    for (float t = 0f; t < len; t += 7f)
                    {
                        var x = Vector2.Lerp(p, q, t / len);
                        if (InRange(x) && !NearCorridor(x) && !InBuilding(x)) Tree(x, 0.75f + (float)_rng.NextDouble() * 0.25f);
                    }
                }
            }
        }

        private void Tree(Vector2 p, float scale)
        {
            Prop("tree" + _rng.Next(0, TreeVariants), p, (float)_rng.NextDouble() * 360f, scale);
        }

        public void BuildSolar()
        {
            // The solar field just outside the ring to the south (reference photo); OSM has no solar polygons here.
            for (int row = 0; row < 5; row++)
                for (float b = 168f; b <= 205f; b += 3.4f)
                {
                    float r = 345f + row * 9f, a = b * Mathf.Deg2Rad;
                    var p = new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r);
                    if (InBuilding(p) || NearCorridor(p)) continue;
                    Prop("solar", p, b, 1f, 1.1f);
                }
        }

        public const int TreeVariants = 4;
        public static readonly Color[] CarColours =
        {
            // Mostly white and silver, like the car parks in the reference photo.
            new Color(0.92f, 0.92f, 0.92f), new Color(0.72f, 0.74f, 0.76f), new Color(0.45f, 0.47f, 0.5f), new Color(0.07f, 0.07f, 0.08f),
        };

        // ---------- writing to the scene ----------

        public void Write(ArtContext ctx, Transform group)
        {
            int c = 0;
            foreach (var kv in _surfaces)
            {
                string name = $"SD_Ground_{kv.Key.x}_{kv.Key.y}";
                var mesh = ArtPass.MeshAsset(name, () => kv.Value.Build(name));
                var go = ArtPass.AddMesh(group, name, mesh, ArtPass.Mats(SurfaceMats), Vector3.zero, Quaternion.identity, Vector3.one, false, false, false);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                c++;
            }
            Counts["ground cells"] = c;

            // Instanced props.
            var protos = Prototypes();
            var groups = new List<InstancedProps.Group>();
            foreach (var kv in _props)
            {
                if (!protos.TryGetValue(kv.Key.Item1, out var proto)) continue;
                var bounds = new Bounds(new Vector3((kv.Key.Item2.x + 0.5f) * Cell, 10f, (kv.Key.Item2.y + 0.5f) * Cell), new Vector3(Cell + 30f, 40f, Cell + 30f));
                for (int s = 0; s < proto.mats.Length; s++)
                    groups.Add(new InstancedProps.Group { Mesh = proto.mesh, Submesh = s, Material = proto.mats[s], CastShadows = proto.shadows, Bounds = bounds, Matrices = kv.Value.ToArray() });
            }
            var holder = new GameObject("SD_Props");
            holder.transform.SetParent(group, false);
            holder.AddComponent<InstancedProps>().Configure(groups.ToArray(), 0);

            // Collider meshes per cell (not rendered).
            foreach (var kv in _colliders)
            {
                string name = $"SD_PropColliders_{kv.Key.x}_{kv.Key.y}";
                var mesh = ArtPass.MeshAsset(name, () => ProcMesh.Combine(name, kv.Value));
                var go = new GameObject(name) { layer = ArtPass.TrackGeometryLayer };
                go.transform.SetParent(group, false);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }

        private static Dictionary<string, (Mesh mesh, Material[] mats, bool shadows)> Prototypes()
        {
            var d = new Dictionary<string, (Mesh, Material[], bool)>();
            Material Inst(string name)
            {
                var m = MaterialLibrary.Get(name);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                return m;
            }
            for (int i = 0; i < TreeVariants; i++)
            {
                int seed = 100 + i;
                d["tree" + i] = (ArtPass.MeshAsset("ExpoTree_" + i, () => GateRush.EditorTools.Art.ExpoArt.TreeMesh(seed, 8f)), new[] { Inst("M_Expo_Bark"), Inst("M_Expo_Leaves") }, true);
            }
            d["streetlight"] = (ArtPass.MeshAsset("SD_StreetLight", StreetLightMesh), new[] { Inst("M_Expo_Steel"), Inst("M_Lamp_Emitter") }, false);
            d["parklamp"] = (ArtPass.MeshAsset("SD_ParkLamp", ParkLampMesh), new[] { Inst("M_Plastic_Black"), Inst("M_Lamp_Warm") }, false);
            d["bench"] = (ArtPass.MeshAsset("SD_Bench", BenchMesh), new[] { Inst("M_Wood_Planks"), Inst("M_Plastic_Black") }, false);
            d["solar"] = (ArtPass.MeshAsset("SD_Solar", SolarMesh), new[] { Inst("M_Expo_Solar"), Inst("M_Expo_Steel") }, false);
            var carMesh = ArtPass.MeshAsset("SD_Car", CarMesh);
            for (int i = 0; i < CarColours.Length; i++)
                d["car" + i] = (carMesh, new[] { CarPaint(i), Inst("M_Glass_Night"), Inst("M_Rubber_Black") }, false);
            return d;
        }

        private static Material CarPaint(int i)
        {
            string path = $"{MaterialLibrary.LibraryDir}/M_SD_CarPaint_{i}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(MaterialLibrary.Get("M_Paint_White"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(MaterialLibrary.Get("M_Paint_White"));
            m.SetColor("_BaseColor", CarColours[i]);
            m.SetFloat("_Smoothness", 0.75f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- prop meshes ----------

        private static Mesh StreetLightMesh() => ProcMesh.Combine("SD_StreetLight", new List<(Mesh, Matrix4x4, int)>
        {
            ProcMesh.Part(new Vector3(0f, 5f, 0f), new Vector3(0.22f, 10f, 0.22f), Quaternion.identity, 0),
            ProcMesh.Part(new Vector3(0f, 9.9f, 1.4f), new Vector3(0.12f, 0.12f, 2.8f), Quaternion.identity, 0),
            ProcMesh.Part(new Vector3(0f, 9.8f, 2.7f), new Vector3(0.45f, 0.18f, 0.8f), Quaternion.identity, 1),
        });

        private static Mesh ParkLampMesh() => ProcMesh.Combine("SD_ParkLamp", new List<(Mesh, Matrix4x4, int)>
        {
            ProcMesh.Part(new Vector3(0f, 2.1f, 0f), new Vector3(0.12f, 4.2f, 0.12f), Quaternion.identity, 0),
            ProcMesh.Part(new Vector3(0f, 4.35f, 0f), new Vector3(0.35f, 0.3f, 0.35f), Quaternion.identity, 1),
        });

        private static Mesh BenchMesh() => ProcMesh.Combine("SD_Bench", new List<(Mesh, Matrix4x4, int)>
        {
            ProcMesh.Part(new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.06f, 0.45f), Quaternion.identity, 0),
            ProcMesh.Part(new Vector3(0f, 0.72f, -0.22f), new Vector3(1.8f, 0.4f, 0.05f), Quaternion.Euler(-12f, 0f, 0f), 0),
            ProcMesh.Part(new Vector3(-0.75f, 0.22f, 0f), new Vector3(0.06f, 0.45f, 0.45f), Quaternion.identity, 1),
            ProcMesh.Part(new Vector3(0.75f, 0.22f, 0f), new Vector3(0.06f, 0.45f, 0.45f), Quaternion.identity, 1),
        });

        private static Mesh SolarMesh() => ProcMesh.Combine("SD_Solar", new List<(Mesh, Matrix4x4, int)>
        {
            ProcMesh.Part(Vector3.zero, new Vector3(18f, 0.08f, 3.4f), Quaternion.Euler(-30f, 0f, 0f), 0),
            ProcMesh.Part(new Vector3(0f, -0.6f, 0f), new Vector3(17f, 1.1f, 0.12f), Quaternion.identity, 1),
        });

        private static Mesh CarMesh()
        {
            var parts = new List<(Mesh, Matrix4x4, int)>
            {
                ProcMesh.Part(new Vector3(0f, 0.62f, 0f), new Vector3(1.8f, 0.62f, 4.4f), Quaternion.identity, 0),       // body
                ProcMesh.Part(new Vector3(0f, 1.15f, -0.25f), new Vector3(1.6f, 0.5f, 2.2f), Quaternion.identity, 1),   // cabin glass
                ProcMesh.Part(new Vector3(0f, 1.42f, -0.25f), new Vector3(1.5f, 0.06f, 1.9f), Quaternion.identity, 0),  // roof
            };
            foreach (float x in new[] { -0.82f, 0.82f })
                foreach (float z in new[] { -1.35f, 1.35f })
                    parts.Add(ProcMesh.Part(new Vector3(x, 0.33f, z), new Vector3(0.24f, 0.66f, 0.66f), Quaternion.identity, 2));
            return ProcMesh.Combine("SD_Car", parts);
        }
    }
}
