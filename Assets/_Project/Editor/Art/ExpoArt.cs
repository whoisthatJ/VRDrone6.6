using System.Collections.Generic;
using GateRush.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Track_Expo: the Astana EXPO 2017 site (Adrian Smith + Gordon Gill) at real scale, laid out from OpenStreetMap
    /// footprints: sphere at the origin, x east, z north, bearings clockwise from north. The Nur Alem glass sphere
    /// (80 m, 100 m tall) on its glazed base and low podium, with the flat canopy round its south side (open underneath);
    /// four crescent pavilions fanned at 41/97/161/227 degrees (curtain walls, rounded metal roofs with skylight strips,
    /// courtyards opening towards the sphere) leaving the north-west open; four lens pavilions round the hub; lawns and
    /// walkways; the ring road (two lanes each way, interrupted by the park to the north); the Alley of Peace and Accord
    /// park running north-north-east with the 46 m independence spire; the Congress Centre, Hilton and Mega Silk Way to
    /// the west; parking and a solar field outside the ring; apartment blocks and the distant skyline (Abu Dhabi Plaza,
    /// Baiterek, Ak Orda, Khan Shatyr). Midday light, realtime (not baked).
    /// </summary>
    public static class ExpoArt
    {
        // Paved hub round the sphere (to the petal tips).
        public const float HubRadius = 132f;
        // Walkways from the hub to the ring road through the gaps (and the north-west lawn).
        public static readonly float[] WalkwayBearings = { 129f, 194f, 291f, 316f };
        // Ring road: two carriageways of two 3.5 m lanes and a 4 m median, interrupted by the park.
        public const float RoadInner = 321f, LaneWidth = 3.5f, Median = 4f, RoadGapFrom = 336f, RoadGapTo = 16f;
        public static float RoadOuter => RoadInner + 4f * LaneWidth + Median;
        // The Alley of Peace and Accord: park axis to the north-north-east, the 46 m spire on it.
        public const float ParkBearing = 12f, ParkFrom = 290f, ParkTo = 840f, ParkHalfWidth = 150f, SpireDistance = 505f, SpireHeight = 46f;
        public static Vector3 SpirePosition => Polar(Vector2.zero, ParkBearing, SpireDistance);

        public static void Apply(ArtContext ctx)
        {
            var look = new SceneLook
            {
                Hdri = "hilly_terrain_01_puresky",
                SkyExposure = 0.8f,                 // shared sky material with the Field: keep the same exposure
                AmbientIntensity = 1.05f,
                ReflectionIntensity = 1f,
                SunIntensity = 3.3f,
                SunTint = new Color(1f, 0.95f, 0.88f),
                ShadowStrength = 0.85f,
                FogDensity = 0.00024f,
                FogBrightness = 0.95f,
                PostExposure = 0.12f,
                Contrast = 9f,
                Saturation = 6f,
                BloomIntensity = 0.3f,
            };
            ArtPass.ApplyLook(ctx, look);
            if (ctx.Sun != null)
            {
                // Midday summer sun from the south-south-west (the reference photo), not the HDRI's low sun.
                ctx.Sun.transform.rotation = Quaternion.Euler(52f, 20f, 0f);
                ctx.Sun.shadows = LightShadows.Soft;
                ctx.Sun.lightmapBakeType = LightmapBakeType.Realtime;
            }

            var ground = ArtPass.TileGround(ctx, "M_Expo_Lawn");
            ctx.Log.Append(ground != null ? " ground;" : " no ground;");
            BuildGrounds(ctx);
            // Hero buildings from the OSM footprints (Sphere District E2 generators).
            ctx.Log.Append(GateRush.EditorTools.SphereDistrict.SphereDistrictHeroes.Build(ctx));
            BuildRoad(ctx);
            BuildPark(ctx);
            BuildOutside(ctx);
            BuildCity(ctx);
            BuildSkyline(ctx);
            BuildTrees(ctx);
            ArtPass.AddProbe(ctx, "Hub Probe", new Vector3(0f, 20f, 0f), new Vector3(700f, 120f, 700f), false, 256);
        }

        // ---------- helpers ----------

        private static Transform G(ArtContext ctx, string name) => ArtPass.Group(ctx, name);

        private static GameObject Add(ArtContext ctx, string group, string name, Mesh mesh, Material[] mats, bool castShadows = true)
        {
            var asset = ArtPass.MeshAsset("Expo_" + name.Replace(' ', '_'), () => mesh);
            return ArtPass.AddMesh(G(ctx, group), name, asset, mats, Vector3.zero, Quaternion.identity, Vector3.one, false, false, castShadows);
        }

        /// <summary>A mesh collider on TrackGeometry (quads crash into the real shape, courtyards and gaps stay open).</summary>
        private static void Solid(GameObject go)
        {
            go.layer = ArtPass.TrackGeometryLayer;
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        }

        private static Vector3 Polar(Vector2 centre, float azimuthDeg, float r, float y = 0f)
        {
            float a = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(centre.x + Mathf.Sin(a) * r, y, centre.y + Mathf.Cos(a) * r);
        }

        /// <summary>Small mesh builder: flat-shaded quads and fans with metric UVs, one submesh per material.</summary>
        private sealed class MB
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

            /// <summary>Quad a-b-c-d, counter-clockwise seen from the front.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, int sub)
            {
                int i = Vert(a, ua), j = Vert(b, ub), k = Vert(c, uc), l = Vert(d, ud);
                S(sub).AddRange(new[] { i, k, j, i, l, k });
            }

            public void Tri(int a, int b, int c, int sub) => S(sub).AddRange(new[] { a, c, b });

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
        }

        /// <summary>Flat annulus (or disc for r0 = 0) at height y; UVs in metres (world xz).</summary>
        private static void Ring(MB mb, Vector2 c, float r0, float r1, float y, int segments, int sub, float a0 = 0f, float a1 = 360f)
        {
            for (int i = 0; i < segments; i++)
            {
                float s0 = Mathf.Lerp(a0, a1, (float)i / segments), s1 = Mathf.Lerp(a0, a1, (float)(i + 1) / segments);
                Vector3 p0 = Polar(c, s0, r0, y), p1 = Polar(c, s1, r0, y), p2 = Polar(c, s1, r1, y), p3 = Polar(c, s0, r1, y);
                mb.Quad(p0, p1, p2, p3, Xz(p0), Xz(p1), Xz(p2), Xz(p3), sub);
            }
        }

        private static Vector2 Xz(Vector3 p) => new Vector2(p.x, p.z);

        /// <summary>Vertical wall along a polyline (outward normal on the left when walking it), u = metres along, v = height.</summary>
        private static void Wall(MB mb, IList<Vector3> line, float y0, float y1, int sub, bool closed = false)
        {
            float u = 0f;
            int n = closed ? line.Count : line.Count - 1;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = line[i], b = line[(i + 1) % line.Count];
                float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
                mb.Quad(new Vector3(a.x, y0, a.z), new Vector3(b.x, y0, b.z), new Vector3(b.x, y1, b.z), new Vector3(a.x, y1, a.z),
                    new Vector2(u, y0), new Vector2(u + len, y0), new Vector2(u + len, y1), new Vector2(u, y1), sub);
                u += len;
            }
        }


        // ---------- ground: hub plaza, walkways, lawns and paths ----------

        /// <summary>A straight paved strip from <paramref name="r0"/> to <paramref name="r1"/> along a bearing.</summary>
        private static void Strip(MB mb, float bearing, float r0, float r1, float halfWidth, float y, int sub)
        {
            var dir = Polar(Vector2.zero, bearing, 1f);
            var right = new Vector3(dir.z, 0f, -dir.x);
            Vector3 a = dir * r0, b = dir * r1;
            Vector3 p0 = a - right * halfWidth, p1 = a + right * halfWidth, p2 = b + right * halfWidth, p3 = b - right * halfWidth;
            p0.y = p1.y = p2.y = p3.y = y;
            mb.Quad(p0, p1, p2, p3, Xz(p0), Xz(p1), Xz(p2), Xz(p3), sub);
        }

        private static void BuildGrounds(ArtContext ctx)
        {
            var mb = new MB();
            // 0 plaza paving, 1 path paving
            Ring(mb, Vector2.zero, 0f, HubRadius, 0.02f, 120, 0);
            foreach (float b in WalkwayBearings)
                Strip(mb, b, HubRadius - 4f, b == 291f || b == 316f ? RoadOuter + 40f : RoadInner, b == 194f ? 14f : 9f, 0.025f, 0);
            // The path ringing the lawn just inside the road.
            Ring(mb, Vector2.zero, RoadInner - 9f, RoadInner - 5f, 0.022f, 180, 1, RoadGapTo, RoadGapFrom);
            // Curving paths across the open north-west lawn.
            Ring(mb, new Vector2(-150f, 150f), 70f, 73f, 0.022f, 48, 1, 60f, 250f);
            Ring(mb, new Vector2(-210f, 40f), 55f, 58f, 0.022f, 40, 1, -40f, 140f);
            Add(ctx, "Ground", "Plaza", mb.Build("Plaza"), ArtPass.Mats("M_Expo_Paving", "M_Concrete_PaintedFloor"), false);
            // The round pool on the north-west lawn.
            var pool = new MB();
            Ring(pool, Xz(Polar(Vector2.zero, 318f, 190f)), 0f, 17f, 0.06f, 48, 0);
            Ring(pool, Xz(Polar(Vector2.zero, 318f, 190f)), 17f, 19f, 0.08f, 48, 1);
            Add(ctx, "Ground", "Pool", pool.Build("ExpoPool"), ArtPass.Mats("M_Water", "M_Concrete_Wall"), false);
            ctx.Log.Append(" plaza;");
        }

        // ---------- ring road ----------

        private static void BuildRoad(ArtContext ctx)
        {
            var mb = new MB();
            float from = RoadGapTo, to = RoadGapFrom;                    // the road runs the long way round, clear of the park
            const int segs = 220;
            float inner1 = RoadInner, outer1 = RoadInner + 2f * LaneWidth;
            float inner2 = outer1 + Median, outer2 = RoadOuter;
            Ring(mb, Vector2.zero, inner1, outer1, 0.03f, segs, 0, from, to);
            Ring(mb, Vector2.zero, inner2, outer2, 0.03f, segs, 0, from, to);
            Ring(mb, Vector2.zero, outer1, inner2, 0.12f, segs, 2, from, to);
            foreach (float r in new[] { inner1 + 0.3f, outer1 - 0.3f, inner2 + 0.3f, outer2 - 0.3f })
                Ring(mb, Vector2.zero, r - 0.08f, r + 0.08f, 0.035f, segs, 1, from, to);
            foreach (float r in new[] { inner1 + LaneWidth, inner2 + LaneWidth })
            {
                float circ = 2f * Mathf.PI * r;
                int dashes = Mathf.RoundToInt(circ * (to - from) / 360f / 12f);
                for (int i = 0; i < dashes; i++)
                {
                    float s0 = from + (to - from) * i / dashes, s1 = s0 + 360f * 4f / circ;
                    Ring(mb, Vector2.zero, r - 0.07f, r + 0.07f, 0.035f, 1, 1, s0, s1);
                }
            }
            // Zebra crossings where the walkways meet the road.
            foreach (float az in WalkwayBearings)
                for (int k = -5; k <= 5; k++)
                {
                    float off = k * 1.2f;
                    var dir = Polar(Vector2.zero, az, 1f);
                    var right = new Vector3(dir.z, 0f, -dir.x);
                    Vector3 a = dir * inner1 + right * (off - 0.3f), b = dir * outer2 + right * (off - 0.3f);
                    Vector3 d = dir * outer2 + right * (off + 0.3f), e = dir * inner1 + right * (off + 0.3f);
                    a.y = b.y = d.y = e.y = 0.036f;
                    mb.Quad(a, e, d, b, Xz(a), Xz(e), Xz(d), Xz(b), 1);
                }
            // Mangilik El avenue to the east, running north-north-east.
            var ave = new MB();
            Vector3 s = Polar(Vector2.zero, 95f, 600f) + Polar(Vector2.zero, 193f, 2500f), f = Polar(Vector2.zero, 95f, 600f) + Polar(Vector2.zero, 13f, 4500f);
            var side = Polar(Vector2.zero, 103f, 1f);
            Vector3 q0 = s - side * 16f, q1 = s + side * 16f, q2 = f + side * 16f, q3 = f - side * 16f;
            q0.y = q1.y = q2.y = q3.y = 0.03f;
            ave.Quad(q0, q3, q2, q1, Xz(q0), Xz(q3), Xz(q2), Xz(q1), 0);
            Add(ctx, "Road", "Ring Road", mb.Build("RingRoad"), ArtPass.Mats("M_Asphalt_Old", "M_Stadium_LinePaint", "M_Expo_Lawn"), false);
            Add(ctx, "Road", "Mangilik El", ave.Build("MangilikEl"), ArtPass.Mats("M_Asphalt_Old"), false);

            var poles = new List<(Mesh, Matrix4x4, int)>();
            for (float az = from + 3f; az < to - 2f; az += 5.5f)
            {
                Vector3 p = Polar(Vector2.zero, az, RoadInner - 1.5f);
                poles.Add(ProcMesh.Part(p + Vector3.up * 5f, new Vector3(0.22f, 10f, 0.22f), Quaternion.identity));
                Vector3 arm = Polar(Vector2.zero, az, RoadInner + 0.5f, 10f);
                poles.Add(ProcMesh.Part(arm, new Vector3(0.18f, 0.15f, 4f), Quaternion.LookRotation(new Vector3(arm.x, 0f, arm.z))));
            }
            Add(ctx, "Road", "Street Lights", ProcMesh.Combine("ExpoStreetLights", poles), ArtPass.Mats("M_Expo_Steel"));
            ctx.Log.Append(" road;");
        }

        // ---------- the park to the north-north-east ----------

        /// <summary>A point in park coordinates: <paramref name="along"/> the axis from the sphere, <paramref name="across"/> to its right.</summary>
        private static Vector3 Park(float along, float across, float y = 0f)
        {
            var p = Polar(Vector2.zero, ParkBearing, along) + Polar(Vector2.zero, ParkBearing + 90f, across);
            p.y = y;
            return p;
        }

        private static void BuildPark(ArtContext ctx)
        {
            var mb = new MB();
            Strip(mb, ParkBearing, ParkFrom - 20f, ParkTo, 12f, 0.025f, 0);
            var sp = Xz(SpirePosition);
            Ring(mb, sp, 0f, 40f, 0.03f, 64, 0);
            // Flowing paths: arcs either side of the axis.
            for (int k = 0; k < 6; k++)
            {
                float along = ParkFrom + 40f + k * 85f;
                Ring(mb, Xz(Park(along, -70f)), 52f, 55f, 0.026f, 40, 1, ParkBearing + 20f, ParkBearing + 160f);
                Ring(mb, Xz(Park(along + 40f, 70f)), 52f, 55f, 0.026f, 40, 1, ParkBearing + 200f, ParkBearing + 340f);
            }
            Add(ctx, "Park", "Park Paths", mb.Build("ParkPaths"), ArtPass.Mats("M_Expo_Paving", "M_Concrete_PaintedFloor"), false);

            ctx.Log.Append(" park;");
        }

        // ---------- outside the ring: neighbours, parking, solar field ----------

        private static void Superellipse(MB mb, Vector2 c, float halfX, float halfZ, float rotDeg, float h, int wallSub, int roofSub, float power = 4f)
        {
            var outline = new List<Vector3>();
            var rot = Quaternion.Euler(0f, rotDeg, 0f);
            for (int i = 0; i < 64; i++)
            {
                float t = 2f * Mathf.PI * i / 64f;
                float cx = Mathf.Sign(Mathf.Cos(t)) * Mathf.Pow(Mathf.Abs(Mathf.Cos(t)), 2f / power) * halfX;
                float cz = Mathf.Sign(Mathf.Sin(t)) * Mathf.Pow(Mathf.Abs(Mathf.Sin(t)), 2f / power) * halfZ;
                var p = rot * new Vector3(cx, 0f, cz);
                outline.Add(new Vector3(c.x + p.x, 0f, c.y + p.z));
            }
            Wall(mb, outline, 0f, h, wallSub, true);
            int top = mb.Vert(new Vector3(c.x, h, c.y), c);
            for (int i = 0; i < outline.Count; i++)
            {
                Vector3 a = outline[i] + Vector3.up * h, b = outline[(i + 1) % outline.Count] + Vector3.up * h;
                mb.Tri(top, mb.Vert(a, Xz(a)), mb.Vert(b, Xz(b)), roofSub);
            }
        }

        private static void BuildOutside(ArtContext ctx)
        {
            // The Mega Silk Way mall (W); the Congress Centre and the hotel are hero landmarks now.
            var big = new MB();
            Superellipse(big, Xz(Polar(Vector2.zero, 267f, 640f)), 120f, 245f, 0f, 16f, 2, 1, 8f);
            Add(ctx, "Outside", "Congress And Mall", big.Build("ExpoCongressMall"), ArtPass.Mats("M_Expo_CurtainWall", "M_Expo_Roof", "M_Concrete_Wall"));

            // Offices along the east side towards Mangilik El.
            var east = new List<(Mesh, Matrix4x4, int)>
            {
                ProcMesh.Part(Polar(Vector2.zero, 60f, 430f, 30f), new Vector3(36f, 60f, 36f), Quaternion.Euler(0f, 30f, 0f), 0),
                ProcMesh.Part(Polar(Vector2.zero, 120f, 450f, 18f), new Vector3(70f, 36f, 40f), Quaternion.Euler(0f, -15f, 0f), 0),
                ProcMesh.Part(Polar(Vector2.zero, 62f, 500f, 9f), new Vector3(170f, 18f, 70f), Quaternion.Euler(0f, 150f, 0f), 1),
            };
            Add(ctx, "Outside", "East Buildings", ProcMesh.Combine("ExpoEast", east), ArtPass.Mats("M_Expo_Tower", "M_Concrete_Wall"));

            // Car parks east and south-west of the ring, with bay lines.
            var lots = new MB();
            foreach (var (bearing, r0, r1, half) in new[] { (95f, 350f, 420f, 70f), (222f, 355f, 450f, 80f) })
            {
                Strip(lots, bearing, r0, r1, half, 0.02f, 0);
                for (float k = -half + 2f; k < half; k += 2.6f)
                    for (int row = 0; row < 3; row++)
                    {
                        float a0 = r0 + 6f + row * 22f;
                        var dir = Polar(Vector2.zero, bearing, 1f);
                        var right = new Vector3(dir.z, 0f, -dir.x);
                        Vector3 p = dir * a0 + right * (k - 0.06f), q = dir * a0 + right * (k + 0.06f), r = dir * (a0 + 5f) + right * (k + 0.06f), t = dir * (a0 + 5f) + right * (k - 0.06f);
                        p.y = q.y = r.y = t.y = 0.025f;
                        lots.Quad(p, q, r, t, Xz(p), Xz(q), Xz(r), Xz(t), 1);
                    }
            }
            Add(ctx, "Outside", "Car Parks", lots.Build("CarParks"), ArtPass.Mats("M_Expo_Asphalt", "M_Stadium_LinePaint"), false);

            // Solar field just outside the ring to the south (seen in the reference photo).
            var solar = new List<(Mesh, Matrix4x4, int)>();
            for (int row = 0; row < 5; row++)
                for (float b = 168f; b <= 205f; b += 3.4f)
                {
                    var p = Polar(Vector2.zero, b, RoadOuter + 22f + row * 9f, 1.1f);
                    var yaw = Quaternion.Euler(0f, b, 0f);                 // rows run tangentially
                    solar.Add(ProcMesh.Part(p, new Vector3(18f, 0.08f, 3.4f), yaw * Quaternion.Euler(-30f, 0f, 0f), 0));
                    solar.Add(ProcMesh.Part(p + Vector3.down * 0.6f, new Vector3(17f, 1.1f, 0.12f), yaw, 1));
                }
            Add(ctx, "Outside", "Solar Field", ProcMesh.Combine("ExpoSolar", solar), ArtPass.Mats("M_Glass_Night", "M_Expo_Steel"));
            ctx.Log.Append(" outside;");
        }

        // ---------- trees: the park and along the ring road ----------

        /// <summary>
        /// A procedural deciduous tree (~100 triangles): a tapered six-sided trunk, three to five branches and a crown of
        /// leaf-cluster cards scattered through an ellipsoid. Crown normals point out from the crown centre so the cards
        /// shade as one soft volume. Submesh 0 bark, 1 leaves; base at the origin.
        /// </summary>
        public static Mesh TreeMesh(int seed, float height)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var bark = new List<int>();
            var leaves = new List<int>();

            void Limb(Vector3 a, Vector3 b, float ra, float rb)
            {
                var axis = (b - a).normalized;
                var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var side2 = Vector3.Cross(axis, side);
                int start = v.Count;
                const int sides = 6;
                for (int i = 0; i <= sides; i++)
                {
                    float t = i * Mathf.PI * 2f / sides;
                    var dir = side * Mathf.Cos(t) + side2 * Mathf.Sin(t);
                    v.Add(a + dir * ra); n.Add(dir); uv.Add(new Vector2(i / (float)sides, 0f));
                    v.Add(b + dir * rb); n.Add(dir); uv.Add(new Vector2(i / (float)sides, 1f));
                }
                for (int i = 0; i < sides; i++)
                {
                    int a0 = start + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    bark.AddRange(new[] { a0, b0, a1, b0, b1, a1 });
                }
            }

            float trunkTop = height * R(0.42f, 0.52f);
            float trunkR = height * 0.022f;
            Limb(Vector3.zero, new Vector3(R(-0.2f, 0.2f), trunkTop, R(-0.2f, 0.2f)), trunkR, trunkR * 0.6f);
            var crown = new Vector3(0f, height * 0.66f, 0f);
            var crownSize = new Vector3(height * R(0.26f, 0.34f), height * R(0.3f, 0.36f), height * R(0.26f, 0.34f));
            int branches = rng.Next(3, 6);
            for (int i = 0; i < branches; i++)
            {
                float yaw = (i + R(-0.3f, 0.3f)) * Mathf.PI * 2f / branches;
                var from = new Vector3(0f, trunkTop * R(0.75f, 1f), 0f);
                var to = from + new Vector3(Mathf.Cos(yaw) * crownSize.x * 0.7f, height * R(0.18f, 0.28f), Mathf.Sin(yaw) * crownSize.z * 0.7f);
                Limb(from, to, trunkR * 0.55f, trunkR * 0.18f);
            }

            int cards = 36;
            for (int i = 0; i < cards; i++)
            {
                // Point in the ellipsoid, biased outwards so the silhouette is full.
                var dir = new Vector3(R(-1f, 1f), R(-0.6f, 1f), R(-1f, 1f)).normalized;
                float d = Mathf.Pow(R(0.35f, 1f), 0.6f);
                var c = crown + Vector3.Scale(dir * d, crownSize);
                float size = height * R(0.2f, 0.28f);
                var rot = Quaternion.Euler(R(-60f, 60f), R(0f, 360f), R(-30f, 30f));
                Vector3 right = rot * Vector3.right * size * 0.5f, up = rot * Vector3.up * size * 0.5f;
                int s = v.Count;
                foreach (var (p, t) in new[] { (c - right - up, new Vector2(0f, 0f)), (c + right - up, new Vector2(1f, 0f)), (c + right + up, new Vector2(1f, 1f)), (c - right + up, new Vector2(0f, 1f)) })
                {
                    v.Add(p);
                    // Soft volume shading: normal from the crown centre (squashed), slightly up.
                    n.Add((Vector3.Scale(p - crown, new Vector3(1f / crownSize.x, 1f / crownSize.y, 1f / crownSize.z)) + Vector3.up * 0.35f).normalized);
                    uv.Add(t);
                }
                leaves.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }

            var m = new Mesh { name = "ExpoTree_" + seed };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.subMeshCount = 2;
            m.SetTriangles(bark, 0);
            m.SetTriangles(leaves, 1);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        private static void BuildTrees(ArtContext ctx)
        {
            const int variants = 6;
            var meshes = new Mesh[variants];
            for (int i = 0; i < variants; i++)
            {
                int seed = 100 + i;
                meshes[i] = ArtPass.MeshAsset("ExpoTree_" + i, () => TreeMesh(seed, 8f));
            }
            var rng = new System.Random(2017);
            // Chunks so whole groups are culled when out of view.
            var chunks = new Dictionary<Vector2Int, List<(Mesh, Matrix4x4, int)>>();
            int count = 0;
            void Tree(Vector3 p, float scale)
            {
                var key = new Vector2Int(Mathf.FloorToInt(p.x / 200f), Mathf.FloorToInt(p.z / 200f));
                if (!chunks.TryGetValue(key, out var list)) chunks[key] = list = new List<(Mesh, Matrix4x4, int)>();
                var mesh = meshes[rng.Next(variants)];
                var m = Matrix4x4.TRS(new Vector3(p.x, 0f, p.z), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * scale);
                list.Add((mesh, m, 0));
                list.Add((mesh, m, 1));
                count++;
            }
            // Park: young trees in loose groups either side of the promenade.
            for (int i = 0; i < 1800 && count < 460; i++)
            {
                float along = ParkFrom + 10f + (float)rng.NextDouble() * (ParkTo - ParkFrom - 10f);
                float across = (float)(rng.NextDouble() * 2 - 1) * ParkHalfWidth;
                var p = Park(along, across);
                if (Mathf.Abs(across) < 20f || Vector3.Distance(p, SpirePosition) < 48f) continue;
                if (Mathf.PerlinNoise(along * 0.02f + 7f, across * 0.02f + 3f) < 0.5f) continue;
                Tree(p, 0.7f + (float)rng.NextDouble() * 0.35f);
            }
            // The north-west lawn: scattered groups clear of the race corridor (bearing 268-276).
            for (int i = 0; i < 900 && count < 620; i++)
            {
                float b = 255f + (float)rng.NextDouble() * 95f, r = 150f + (float)rng.NextDouble() * 150f;
                if (Mathf.Abs(Mathf.DeltaAngle(b, 272f)) < 9f || Mathf.Abs(Mathf.DeltaAngle(b, 291f)) < 4f || Mathf.Abs(Mathf.DeltaAngle(b, 316f)) < 4f) continue;
                if (r > RoadInner - 22f && b < 300f) continue;                           // the lawn loop runs here
                if (Vector3.Distance(Polar(Vector2.zero, b, r), Polar(Vector2.zero, 318f, 190f)) < 26f) continue;
                if (Mathf.PerlinNoise(b * 0.05f, r * 0.02f) < 0.55f) continue;
                Tree(Polar(Vector2.zero, b, r), 0.6f + (float)rng.NextDouble() * 0.3f);
            }
            // A row along the outer edge of the ring road.
            for (float az = RoadGapTo + 2f; az < RoadGapFrom - 2f; az += 2.6f)
            {
                bool walkway = false;
                foreach (float b in WalkwayBearings) walkway |= Mathf.Abs(Mathf.DeltaAngle(az, b)) < 3f;
                if (walkway) continue;
                Tree(Polar(Vector2.zero, az, RoadOuter + 9f), 0.75f + (float)rng.NextDouble() * 0.25f);
            }
            var group = G(ctx, "Trees");
            int c = 0;
            foreach (var kv in chunks)
            {
                var go = ArtPass.AddMesh(group, "Trees " + c, ArtPass.MeshAsset("ExpoTrees_" + c, () => ProcMesh.Combine("ExpoTrees_" + c, kv.Value)),
                    ArtPass.Mats("M_Expo_Bark", "M_Expo_Leaves"), Vector3.zero, Quaternion.identity, Vector3.one);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
                c++;
            }
            ctx.Log.Append($" trees {count} in {c} chunks;");
        }

        // ---------- the city round the site ----------

        private static bool KeepClear(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r < 560f) return true;
            // The park corridor and its continuation towards the Baiterek axis.
            var axis = Polar(Vector2.zero, ParkBearing, 1f);
            float along = x * axis.x + z * axis.z, across = Mathf.Abs(x * axis.z - z * axis.x);
            if (along > 0f && along < 1100f && across < ParkHalfWidth + 30f) return true;
            // Mega Silk Way and the Congress Centre.
            if (Vector2.Distance(new Vector2(x, z), Xz(Polar(Vector2.zero, 267f, 640f))) < 280f) return true;
            if (Vector2.Distance(new Vector2(x, z), Xz(Polar(Vector2.zero, 316f, 480f))) < 110f) return true;
            return false;
        }

        private static void BuildCity(ArtContext ctx)
        {
            var rng = new System.Random(2017);
            var blocks = new List<(Mesh, Matrix4x4, int)>();
            var towers = new List<(Mesh, Matrix4x4, int)>();
            var roofs = new List<(Mesh, Matrix4x4, int)>();
            for (float x = -1700f; x <= 1700f; x += 110f)
                for (float z = -1500f; z <= 1900f; z += 110f)
                {
                    float jx = (float)(rng.NextDouble() - 0.5) * 20f, jz = (float)(rng.NextDouble() - 0.5) * 20f;
                    if (KeepClear(x + jx, z + jz) || rng.NextDouble() < 0.18) continue;
                    float h = (9 + rng.Next(0, 9)) * 3f;
                    float w = 50f + (float)rng.NextDouble() * 30f, d = 14f + (float)rng.NextDouble() * 4f;
                    // Two slabs forming an L (the typical Astana courtyard block).
                    blocks.Add(ProcMesh.Part(new Vector3(x + jx, h / 2f, z + jz), new Vector3(w, h, d), Quaternion.identity, 0));
                    blocks.Add(ProcMesh.Part(new Vector3(x + jx - w / 2f + d / 2f, h / 2f, z + jz - 20f), new Vector3(d, h, 40f), Quaternion.identity, 0));
                    roofs.Add(ProcMesh.Part(new Vector3(x + jx, h + 0.4f, z + jz), new Vector3(w + 0.4f, 0.8f, d + 0.4f), Quaternion.identity, 0));
                    if (rng.NextDouble() < 0.14)
                    {
                        float th = 60f + (float)rng.NextDouble() * 80f;
                        towers.Add(ProcMesh.Part(new Vector3(x + jx + 30f, th / 2f, z + jz + 30f), new Vector3(24f, th, 24f), Quaternion.identity, 0));
                    }
                }
            Add(ctx, "City", "Apartments", ProcMesh.Combine("ExpoApartments", blocks), ArtPass.Mats("M_Expo_Apartment"), true);
            Add(ctx, "City", "Apartment Roofs", ProcMesh.Combine("ExpoApartmentRoofs", roofs), ArtPass.Mats("M_Concrete_Wall"), false);
            Add(ctx, "City", "Towers", ProcMesh.Combine("ExpoTowers", towers), ArtPass.Mats("M_Expo_Tower"), true);
            ctx.Log.Append($" city {blocks.Count / 2} blocks;");
        }

        /// <summary>
        /// The distant skyline up the axis: Abu Dhabi Plaza (311 m, 3.7 km NNE), Baiterek (97 m tower and golden ball,
        /// 4.2 km), Ak Orda (blue-domed palace, 4.6 km NE), Khan Shatyr (150 m tent, 4.9 km N) and the left-bank cluster.
        /// </summary>
        private static void BuildSkyline(ArtContext ctx)
        {
            var glass = new List<(Mesh, Matrix4x4, int)>();
            var white = new List<(Mesh, Matrix4x4, int)>();
            var gold = new List<(Mesh, Matrix4x4, int)>();
            var blue = new List<(Mesh, Matrix4x4, int)>();
            Vector3 At(float bearing, float distance) => Polar(Vector2.zero, bearing, distance);
            // Abu Dhabi Plaza: a tall faceted glass tower.
            var adp = At(13f, 3700f);
            glass.Add(ProcMesh.Part(adp + Vector3.up * 140f, new Vector3(40f, 280f, 40f), Quaternion.Euler(0f, 45f, 0f)));
            glass.Add(ProcMesh.Part(adp + Vector3.up * 295f, new Vector3(24f, 32f, 24f), Quaternion.Euler(0f, 45f, 0f)));
            // Baiterek: white lattice shaft (as a slim cylinder-ish box stack) with the golden sphere at 97 m.
            var bt = At(13.6f, 4200f);
            white.Add(ProcMesh.Part(bt + Vector3.up * 40f, new Vector3(9f, 80f, 9f), Quaternion.identity));
            white.Add(ProcMesh.Part(bt + Vector3.up * 40f, new Vector3(9f, 80f, 9f), Quaternion.Euler(0f, 45f, 0f)));
            gold.Add((ProcMesh.Cone(22f, 11f, 11f, 20), Matrix4x4.Translate(bt + Vector3.up * 78f), 0));
            // Ak Orda: palace block with a blue dome and spire.
            var ao = At(28f, 4600f);
            white.Add(ProcMesh.Part(ao + Vector3.up * 20f, new Vector3(120f, 40f, 60f), Quaternion.Euler(0f, 13f, 0f)));
            blue.Add((ProcMesh.Cone(28f, 18f, 2f, 20), Matrix4x4.Translate(ao + Vector3.up * 40f), 0));
            gold.Add((ProcMesh.Cone(18f, 1f, 0.1f, 8), Matrix4x4.Translate(ao + Vector3.up * 68f), 0));
            // Khan Shatyr: the tilted transparent tent.
            var ks = At(351f, 4900f);
            glass.Add((ProcMesh.Cone(150f, 100f, 4f, 24), Matrix4x4.TRS(ks, Quaternion.Euler(0f, 0f, 4f), Vector3.one), 0));
            // Left-bank cluster round the axis between the park and Baiterek.
            var rng = new System.Random(4242);
            for (int i = 0; i < 26; i++)
            {
                float b = 5f + (float)rng.NextDouble() * 18f, d = 2400f + (float)rng.NextDouble() * 1800f;
                float h = 70f + (float)rng.NextDouble() * 130f;
                glass.Add(ProcMesh.Part(At(b, d) + Vector3.up * h / 2f, new Vector3(30f, h, 30f), Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, 0f)));
            }
            Add(ctx, "Skyline", "Skyline Glass", ProcMesh.Combine("ExpoSkylineGlass", glass), ArtPass.Mats("M_Expo_Tower"), false);
            Add(ctx, "Skyline", "Skyline White", ProcMesh.Combine("ExpoSkylineWhite", white), ArtPass.Mats("M_Paint_White"), false);
            Add(ctx, "Skyline", "Skyline Gold", ProcMesh.Combine("ExpoSkylineGold", gold), ArtPass.Mats("M_Garage_Gold"), false);
            Add(ctx, "Skyline", "Skyline Blue", ProcMesh.Combine("ExpoSkylineBlue", blue), ArtPass.Mats("M_Container_Blue"), false);
            ctx.Log.Append(" skyline;");
        }
    }
}
