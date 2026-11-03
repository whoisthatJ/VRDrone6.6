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

            // Urban paving everywhere OSM maps no other surface; dry steppe beyond the mapped 3 km.
            var ground = ArtPass.TileGround(ctx, "M_Expo_Paving");
            ctx.Log.Append(ground != null ? " ground;" : " no ground;");
            BuildSteppe(ctx);
            // Hero buildings from the OSM footprints (Sphere District E2 generators).
            ctx.Log.Append(GateRush.EditorTools.SphereDistrict.SphereDistrictHeroes.Build(ctx));
            BuildStreetscape(ctx);
            BuildOutside(ctx);
            BuildCity(ctx);
            BuildSkyline(ctx);
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


        // ---------- ground and streetscape (Sphere District E3, from OSM) ----------

        private static void BuildStreetscape(ArtContext ctx)
        {
            var geo = AssetDatabase.LoadAssetAtPath<GateRush.Track.Geo.GeoDataAsset>(GateRush.EditorTools.SphereDistrict.OsmImporter.DataPath);
            if (geo == null)
            {
                ctx.Log.Append(" no geo data;");
                return;
            }
            var st = new GateRush.EditorTools.SphereDistrict.StreetscapeGenerator(geo.Data, GateRush.EditorTools.Builders.RouteLibrary.ExpoKnots());
            st.BuildRoads();
            st.BuildAreas();
            st.BuildFootwayProps();
            st.BuildTrees();
            st.BuildSolar();
            st.Write(ctx, G(ctx, "Sphere District Streetscape"));
            var sb = new System.Text.StringBuilder(" streetscape:");
            foreach (var kv in st.Counts) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value);
            ctx.Log.Append(sb.Append(';'));
        }

        /// <summary>A band of dry steppe grass beyond the mapped area (1.5-3 km), fading the city out.</summary>
        private static void BuildSteppe(ArtContext ctx)
        {
            var mb = new MB();
            Ring(mb, Vector2.zero, 1520f, 3000f, 0.01f, 128, 0);
            Add(ctx, "Ground", "Steppe", mb.Build("Steppe"), ArtPass.Mats("M_Expo_Steppe"), false);
        }

        // ---------- outside the ring: neighbours ----------

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
