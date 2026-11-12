using System.Collections.Generic;
using System.IO;
using Ushqyr.EditorTools.Art;
using Ushqyr.Strike;
using Ushqyr.Track;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.EditorTools.Range
{
    /// <summary>
    /// The Strike Range environment `Range_Steppe` (P5): ~1.4 km of steppe with low hills (the playable range is
    /// 1.2 × 1.2 km), four dirt roads, berms, earth revetments, old hulks, a watchtower and a command tent, three launch
    /// pads with countdown lights at the south edge, and the <see cref="RangeLayout"/> (pads, static spots, roads,
    /// heightfield) the targets, bots and tests use. Deterministic: the same build every time.
    /// </summary>
    public static class RangeSteppeBuilder
    {
        public const string LayoutPath = "Assets/_Project/Data/Strike/RangeLayout_Steppe.asset";
        public const string TrackJsonPath = "Assets/_Project/Data/Tracks/strike-steppe.json";
        public const float TerrainSize = 1400f, RangeSize = 1200f;
        public const float PadZ = -520f;
        private const int TerrainRes = 281; // 5 m cells

        public static void Build(Transform parent, int layer)
        {
            var root = new GameObject("Range_Steppe").transform;
            root.SetParent(parent, false);
            Lighting(root);

            var layout = LoadOrCreateLayout();
            layout.Size = RangeSize;
            layout.HeightRes = 241;
            layout.Heights = new float[layout.HeightRes * layout.HeightRes];
            float cell = RangeSize / (layout.HeightRes - 1);
            for (int z = 0; z < layout.HeightRes; z++)
                for (int x = 0; x < layout.HeightRes; x++)
                    layout.Heights[z * layout.HeightRes + x] = Height(-RangeSize * 0.5f + x * cell, -RangeSize * 0.5f + z * cell);

            Skirt(root, layer);
            layout.Pads = Pads(root, layer, out var lights);
            layout.Roads = Roads(root, layer, layout);
            Mounds.Clear();
            layout.Spots = Spots(root, layer, layout);
            Berms(layout);
            // The ground: a terrain on the same height function plus the earthworks, with layers, field roads,
            // grass, bushes and stones.
            RangeSteppeTerrain.Build(root, layer, layout, Mounds);
            Hulks(root, layer, layout);
            Tower(root, layer);
            Tent(root, layer);
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();

            var sessionGo = new GameObject("Strike Session");
            sessionGo.transform.SetParent(root, false);
            var session = sessionGo.AddComponent<StrikeSession>();
            session.Configure(layout, lights, EffectMaterial("M_Strike_Smoke", RangeTargetView.NewSmokeMaterial()), EffectMaterial("M_Strike_Scorch", RangeTargetView.NewScorchMaterial()), VehicleMaterials(), EffectMaterial("M_Strike_Flash", RangeTargetView.NewFlashMaterial()));
            sessionGo.AddComponent<RangeLighting>();
            WriteTrackJson(layout);
        }

        // ---------- terrain ----------

        /// <summary>Low steppe hills; flat at the launch pads.</summary>
        public static float Height(float x, float z)
        {
            float h = 11f * (Mathf.PerlinNoise(x / 260f + 13.1f, z / 260f + 7.7f) - 0.5f)
                    + 4f * (Mathf.PerlinNoise(x / 70f + 3.3f, z / 70f + 1.9f) - 0.5f)
                    + 0.8f * (Mathf.PerlinNoise(x / 18f, z / 18f) - 0.5f);
            // Flatten round the pads.
            float dp = new Vector2(x, z - PadZ).magnitude;
            float flat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 140f, dp));
            float padLevel = 11f * (Mathf.PerlinNoise(13.1f, PadZ / 260f + 7.7f) - 0.5f);
            h = Mathf.Lerp(padLevel, h, flat);
            // Settle to the skirt level towards the terrain's edge, so the skirt plane carries on seamlessly.
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(620f, TerrainSize * 0.5f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z))));
            return Mathf.Lerp(h, SkirtLevel, edge);
        }

        public const float SkirtLevel = -1.5f;

        // Flat steppe from the terrain edge to the horizon (under the fog): a square ring round the terrain, so no edge
        // or below-horizon sky shows and nothing pokes through the valleys.
        private static void Skirt(Transform root, int layer)
        {
            float a = TerrainSize * 0.5f - 1f, b = 6000f, y = SkirtLevel - 0.02f;
            var v = new[]
            {
                new Vector3(-a, y, -a), new Vector3(a, y, -a), new Vector3(a, y, a), new Vector3(-a, y, a),
                new Vector3(-b, y, -b), new Vector3(b, y, -b), new Vector3(b, y, b), new Vector3(-b, y, b),
            };
            var uv = new Vector2[8];
            for (int i = 0; i < 8; i++) uv[i] = new Vector2(v[i].x, v[i].z) / 12f;
            var t = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int i0 = i, i1 = (i + 1) % 4, o0 = i + 4, o1 = (i + 1) % 4 + 4;
                t.AddRange(new[] { i0, o1, o0, i0, i1, o1 }); // clockwise from above = facing up
            }
            var mesh = new Mesh { name = "Steppe Skirt", vertices = v, uv = uv, triangles = t.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var go = new GameObject("Steppe Skirt") { layer = layer, isStatic = true };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, "Steppe_Skirt");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.Get("M_Expo_Steppe");
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void Terrain(Transform root, int layer)
        {
            int n = TerrainRes;
            float cell = TerrainSize / (n - 1);
            var v = new Vector3[n * n];
            var uv = new Vector2[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float wx = -TerrainSize * 0.5f + x * cell, wz = -TerrainSize * 0.5f + z * cell;
                    v[z * n + x] = new Vector3(wx, Height(wx, wz), wz);
                    uv[z * n + x] = new Vector2(wx, wz) / 12f;
                }
            var t = new int[(n - 1) * (n - 1) * 6];
            int k = 0;
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                {
                    int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                    t[k++] = a; t[k++] = c; t[k++] = b;
                    t[k++] = b; t[k++] = c; t[k++] = d;
                }
            var mesh = new Mesh { name = "Steppe Terrain", indexFormat = IndexFormat.UInt32, vertices = v, uv = uv, triangles = t };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var mesh2 = SaveMesh(mesh, "Steppe_Terrain");
            var go = new GameObject("Terrain") { layer = layer, isStatic = true };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh2;
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("M_Expo_Steppe");
            go.AddComponent<MeshCollider>().sharedMesh = mesh2;
        }

        // ---------- pads ----------

        private static PadSpot[] Pads(Transform root, int layer, out Renderer[] lights)
        {
            var pads = new PadSpot[StrikeRules.Pads];
            var list = new List<Renderer>();
            var lamp = Emissive("M_Strike_Lamp");
            var steel = MaterialLibrary.Get("M_Steel_Brushed");
            for (int i = 0; i < pads.Length; i++)
            {
                float x = (i - 1) * 24f;
                float y = Height(x, PadZ);
                pads[i] = new PadSpot { Position = new Vector3(x, y + 0.2f, PadZ), Yaw = 0f };
                Box(root, $"Launch Pad {i + 1}", new Vector3(x, y + 0.1f, PadZ), new Vector3(5f, 0.2f, 5f), MaterialLibrary.Get("M_Concrete_Blocks"), layer);
                Box(root, $"Pad {i + 1} Marking", new Vector3(x, y + 0.205f, PadZ), new Vector3(3.2f, 0.01f, 3.2f), MaterialLibrary.Get("M_Kit_LandingPad"), layer, collider: false);
                // Countdown light tree beside the pad: red, yellow, green.
                Vector3 pole = new Vector3(x + 3.3f, y, PadZ - 2f);
                Cyl(root, $"Pad {i + 1} Light Pole", pole + Vector3.up * 1.4f, 0.08f, 2.8f, steel, layer);
                for (int l = 0; l < 3; l++)
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    g.name = $"Pad {i + 1} Lamp {l}";
                    Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.transform.SetParent(root, false);
                    g.transform.position = pole + Vector3.up * (2.9f - l * 0.45f) + Vector3.back * 0.12f;
                    g.transform.localScale = Vector3.one * 0.36f;
                    var r = g.GetComponent<MeshRenderer>();
                    r.sharedMaterial = lamp;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    list.Add(r);
                }
            }
            lights = list.ToArray();
            return pads;
        }

        // ---------- roads ----------

        private static List<RangeRoad> Roads(Transform root, int layer, RangeLayout layout)
        {
            var controls = new[]
            {
                // West-east through the middle of the range, with bends.
                new[] { new Vector2(-560f, -80f), new Vector2(-300f, 20f), new Vector2(-80f, -40f), new Vector2(150f, 60f), new Vector2(380f, 0f), new Vector2(560f, 90f) },
                // From the south-west up to the north.
                new[] { new Vector2(-420f, -420f), new Vector2(-330f, -200f), new Vector2(-360f, 80f), new Vector2(-240f, 300f), new Vector2(-120f, 540f) },
                // North loop road.
                new[] { new Vector2(-500f, 380f), new Vector2(-200f, 440f), new Vector2(80f, 360f), new Vector2(320f, 430f), new Vector2(540f, 330f) },
                // East, from the south-east to the north-east.
                new[] { new Vector2(260f, -470f), new Vector2(360f, -240f), new Vector2(300f, -40f), new Vector2(430f, 200f), new Vector2(380f, 520f) },
            };
            var roads = new List<RangeRoad>();
            for (int r = 0; r < controls.Length; r++)
            {
                var pts = new List<Vector3>();
                var c = controls[r];
                for (int i = 0; i < c.Length - 1; i++)
                {
                    Vector2 p0 = c[Mathf.Max(0, i - 1)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(c.Length - 1, i + 2)];
                    float len = Vector2.Distance(p1, p2);
                    int steps = Mathf.Max(2, Mathf.CeilToInt(len / 5f));
                    for (int s = 0; s < steps; s++)
                    {
                        float t = s / (float)steps;
                        Vector2 q = CatmullRom(p0, p1, p2, p3, t);
                        pts.Add(new Vector3(q.x, Height(q.x, q.y), q.y));
                    }
                }
                var last = c[c.Length - 1];
                pts.Add(new Vector3(last.x, Height(last.x, last.y), last.y));
                roads.Add(new RangeRoad { Points = pts.ToArray() });
                // No mesh: the road is painted into the terrain (RangeSteppeTerrain), two lanes (TargetMotion.LaneOffset).
            }
            return roads;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        // ---------- static spots, revetments, berms, hulks ----------

        private static StaticSpot[] Spots(Transform root, int layer, RangeLayout layout)
        {
            var rng = new System.Random(5511);
            var spots = new List<StaticSpot>();
            Vector3 pads = new Vector3(0f, 0f, PadZ);
            int guard = 0;
            while (spots.Count < 26 && guard++ < 5000)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * 540f, z = (float)(rng.NextDouble() * 2 - 1) * 540f;
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(pads.x, pads.z));
                if (d < 150f || d > 700f) continue;
                if (NearRoad(layout, x, z, 28f)) continue; // clear of the lanes and of wrecks pulled off the road
                bool clash = false;
                foreach (var s in spots) if (Vector2.Distance(new Vector2(s.Position.x, s.Position.z), new Vector2(x, z)) < 30f) clash = true;
                if (clash) continue;
                float yaw = (float)rng.NextDouble() * 360f;
                bool revet = spots.Count % 3 == 0;
                var spot = new StaticSpot { Position = new Vector3(x, Height(x, z), z), Yaw = yaw, Revetment = revet };
                spots.Add(spot);
                if (revet) Revetment(spot);
            }
            return spots.ToArray();
        }

        /// <summary>Earthworks sculpted into the terrain (berms and revetments), filled while building.</summary>
        public static readonly List<RangeSteppeTerrain.Mound> Mounds = new List<RangeSteppeTerrain.Mound>();

        // A U of earth banks round a static spot, open to the front.
        private static void Revetment(StaticSpot s)
        {
            var rot = Quaternion.Euler(0f, s.Yaw, 0f);
            int seed = Mounds.Count * 31 + 7;
            void Bank(Vector3 local, float halfLen, float halfWid, float height, float yaw)
            {
                Vector3 p = s.Position + rot * local;
                Mounds.Add(new RangeSteppeTerrain.Mound { Centre = new Vector2(p.x, p.z), Yaw = s.Yaw + yaw, HalfLength = halfLen, HalfWidth = halfWid, Height = height, Seed = seed++ });
            }
            Bank(new Vector3(0f, 0f, -8f), 7.5f, 2.8f, 2.3f, 90f);
            Bank(new Vector3(-6f, 0f, -1f), 7f, 2.6f, 2.1f, 0f);
            Bank(new Vector3(6f, 0f, -1f), 7f, 2.6f, 2.1f, 0f);
        }

        // Long low earth banks across the range, clear of the roads (and the wrecks rolled off them) and the spots.
        private static void Berms(RangeLayout layout)
        {
            var rng = new System.Random(7341);
            int made = 0, guard = 0;
            while (made < 22 && guard++ < 4000)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * 560f, z = (float)(rng.NextDouble() * 2 - 1) * 560f;
                float halfLen = 8f + (float)rng.NextDouble() * 11f, halfWid = 3f + (float)rng.NextDouble() * 2f;
                float height = 1.4f + (float)rng.NextDouble() * 1.6f, yaw = (float)rng.NextDouble() * 180f;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(0f, PadZ)) < 120f) continue;
                // Check the whole bank, not just its middle.
                var along = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
                bool bad = false;
                for (int i = -2; i <= 2 && !bad; i++)
                {
                    var p = new Vector2(x, z) + along * (halfLen * i / 2f);
                    if (NearRoad(layout, p.x, p.y, 30f + halfWid) || NearSpot(layout, p.x, p.y, 16f + halfWid)) bad = true;
                }
                foreach (var m in Mounds)
                    if (Vector2.Distance(m.Centre, new Vector2(x, z)) < m.HalfLength + halfLen + 6f) bad = true;
                if (bad) continue;
                Mounds.Add(new RangeSteppeTerrain.Mound { Centre = new Vector2(x, z), Yaw = yaw, HalfLength = halfLen, HalfWidth = halfWid, Height = height, Seed = 1000 + made });
                made++;
            }
        }

        /// <summary>
        /// Vehicle materials per <see cref="VehicleKit.Slot"/>, from the material library: weathered plate steel for the
        /// paint (tinted per vehicle), black rubber, worn steel, dark plastic, dark glass, canvas and lamp glass.
        /// </summary>
        public static Material[] VehicleMaterials()
        {
            Directory.CreateDirectory("Assets/_Project/Art/Strike");
            Material Copy(string name, string from, Color color, float smooth, float metal = 0f)
            {
                string path = $"Assets/_Project/Art/Strike/{name}.mat";
                var src = MaterialLibrary.Get(from);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(src) { name = name };
                    AssetDatabase.CreateAsset(m, path);
                }
                else m.CopyPropertiesFromMaterial(src);
                m.enableInstancing = true;
                m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
                EditorUtility.SetDirty(m);
                return m;
            }
            var mats = new Material[VehicleKit.Slots];
            // Paint: the plate's surface detail (normal map) under a clean colour; the per-vehicle tint is the paint.
            mats[(int)VehicleKit.Slot.Paint] = Copy("M_Strike_VehiclePaint", "M_Steel_Plate", Color.white, 0.18f, 0.05f);
            // Weathering: a light, stained wall texture under the paint colour (dust and grime streaks).
            var grime = GrimeTexture();
            mats[(int)VehicleKit.Slot.Paint].SetTexture("_BaseMap", grime);
            mats[(int)VehicleKit.Slot.Paint].mainTexture = grime;
            mats[(int)VehicleKit.Slot.Paint].SetColor("_BaseColor", Color.white);
            mats[(int)VehicleKit.Slot.Rubber] = Copy("M_Strike_VehicleRubber", "M_Rubber_Black", new Color(0.32f, 0.3f, 0.28f), 0.12f);
            mats[(int)VehicleKit.Slot.Metal] = Copy("M_Strike_VehicleMetal", "M_Steel_Plate", new Color(0.62f, 0.62f, 0.6f), 0.35f, 0.5f);
            mats[(int)VehicleKit.Slot.Dark] = Copy("M_Strike_VehicleDark", "M_Plastic_Black", new Color(0.35f, 0.35f, 0.33f), 0.2f);
            mats[(int)VehicleKit.Slot.Glass] = Copy("M_Strike_VehicleGlass", "M_City_GlassBlue", new Color(0.12f, 0.15f, 0.17f), 0.92f, 0.2f);
            mats[(int)VehicleKit.Slot.Canvas] = Copy("M_Strike_VehicleCanvas", "M_Fabric_Canvas", new Color(0.62f, 0.6f, 0.45f), 0.08f);
            mats[(int)VehicleKit.Slot.Light] = Copy("M_Strike_VehicleLight", "M_Lamp_Warm", new Color(1f, 0.95f, 0.85f), 0.9f);
            AssetDatabase.SaveAssets();
            return mats;
        }

        private static void Hulks(Transform root, int layer, RangeLayout layout)
        {
            RangeVehicles.MaterialsOverride = VehicleMaterials();
            var rng = new System.Random(9917);
            int made = 0, guard = 0;
            while (made < 7 && guard++ < 2000)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * 520f, z = (float)(rng.NextDouble() * 2 - 1) * 500f;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(0f, PadZ)) < 150f || NearRoad(layout, x, z, 24f) || NearSpot(layout, x, z, 20f) || NearMound(x, z, 6f)) continue;
                var kind = (VehicleKind)(made % 5);
                var go = RangeVehicles.Build(kind, root, layer, new Color(0.12f, 0.1f, 0.09f));
                go.name = "Hulk " + kind;
                foreach (var hb in go.GetComponentsInChildren<StrikeHitbox>()) Object.DestroyImmediate(hb);
                // Generated meshes must be assets, or the scene loses them when it is saved.
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (!AssetDatabase.Contains(mf.sharedMesh)) mf.sharedMesh = SaveMesh(Object.Instantiate(mf.sharedMesh), $"Hulk_{kind}_{mf.gameObject.name}");
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>()) mr.SetPropertyBlock(null);
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
                    if (mr.gameObject.name == nameof(VehicleKit.Slot.Paint)) mr.sharedMaterial = HulkPaint();
                go.transform.SetPositionAndRotation(new Vector3(x, Height(x, z) - 0.25f, z), Quaternion.Euler((float)rng.NextDouble() * 6f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 8f - 4f));
                made++;
            }
        }

        /// <summary>
        /// A light grey grime map for vehicle paint: the stained wall texture, made grey, centred on 0.85 brightness
        /// with gentle contrast, so the paint keeps its colour with dust and streaks over it.
        /// </summary>
        private static Texture2D GrimeTexture()
        {
            const string path = "Assets/_Project/Art/Strike/T_Strike_VehicleGrime.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            var src = MaterialLibrary.Get("M_Concrete_Wall").GetTexture("_BaseMap");
            const int size = 1024;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            float mean = 0f;
            foreach (var c in px) mean += c.grayscale;
            mean /= px.Length;
            for (int i = 0; i < px.Length; i++)
            {
                float g = Mathf.Clamp01(0.85f + (px[i].grayscale - mean) * 0.9f);
                px[i] = new Color(g, g, g);
            }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Burnt-out paint for the old hulks.
        private static Material HulkPaint()
        {
            const string path = "Assets/_Project/Art/Strike/M_Strike_HulkPaint.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(MaterialLibrary.Get("M_Steel_Rusty")) { name = "M_Strike_HulkPaint" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", new Color(0.32f, 0.24f, 0.18f));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- watchtower, tent ----------

        private static void Tower(Transform root, int layer)
        {
            var steel = MaterialLibrary.Get("M_Steel_Rusty");
            var wood = MaterialLibrary.Get("M_Wood_Planks");
            float x = 70f, z = PadZ - 25f, y = Height(x, z);
            foreach (float sx in new[] { -1.6f, 1.6f })
                foreach (float sz in new[] { -1.6f, 1.6f })
                    Box(root, "Watchtower Leg", new Vector3(x + sx, y + 4f, z + sz), new Vector3(0.25f, 8f, 0.25f), steel, layer);
            Box(root, "Watchtower Deck", new Vector3(x, y + 8.1f, z), new Vector3(4.2f, 0.25f, 4.2f), wood, layer);
            Box(root, "Watchtower Cabin", new Vector3(x, y + 9.4f, z), new Vector3(3.6f, 2.4f, 3.6f), wood, layer);
            Box(root, "Watchtower Roof", new Vector3(x, y + 10.8f, z), new Vector3(4.6f, 0.3f, 4.6f), steel, layer);
        }

        private static void Tent(Transform root, int layer)
        {
            var canvas = MaterialLibrary.Get("M_Fabric_Canvas");
            float x = -75f, z = PadZ - 22f, y = Height(x, z);
            Box(root, "Command Tent", new Vector3(x, y + 1.25f, z), new Vector3(8f, 2.5f, 5f), canvas, layer);
            foreach (float s in new[] { -1f, 1f })
                Box(root, "Command Tent Roof", new Vector3(x, y + 3.05f, z + s * 1.3f), new Vector3(8.3f, 0.12f, 3.2f), canvas, layer, rot: Quaternion.Euler(s * 28f, 0f, 0f));
        }

        // ---------- lighting ----------

        private static void Lighting(Transform root)
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(root, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "Steppe Sky" };
            Directory.CreateDirectory("Assets/_Project/Art/Strike");
            string skyPath = "Assets/_Project/Art/Strike/Steppe_Sky.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(sky, skyPath);
                existing = sky;
            }
            if (existing.HasProperty("_GroundColor")) existing.SetColor("_GroundColor", new Color(0.42f, 0.4f, 0.3f));
            RenderSettings.skybox = existing;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 300f;
            RenderSettings.fogEndDistance = 2600f;
        }

        // ---------- helpers ----------

        private static bool NearRoad(RangeLayout layout, float x, float z, float r)
        {
            foreach (var road in layout.Roads)
                foreach (var p in road.Points)
                    if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < r * r) return true;
            return false;
        }

        private static bool NearMound(float x, float z, float r)
        {
            foreach (var m in Mounds)
                if (Vector2.Distance(m.Centre, new Vector2(x, z)) < m.HalfLength + r) return true;
            return false;
        }

        private static bool NearSpot(RangeLayout layout, float x, float z, float r)
        {
            foreach (var s in layout.Spots)
                if ((s.Position.x - x) * (s.Position.x - x) + (s.Position.z - z) * (s.Position.z - z) < r * r) return true;
            return false;
        }

        private static GameObject Box(Transform root, string name, Vector3 c, Vector3 size, Material mat, int layer, bool collider = true, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.isStatic = true;
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(c, rot ?? Quaternion.identity);
            go.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static void Cyl(Transform root, string name, Vector3 c, float radius, float height, Material mat, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.layer = layer;
            go.isStatic = true;
            go.transform.SetParent(root, false);
            go.transform.position = c;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Material Emissive(string name)
        {
            string path = $"Assets/_Project/Art/Strike/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Directory.CreateDirectory("Assets/_Project/Art/Strike");
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f));
            m.SetColor("_EmissionColor", Color.black);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // A smoke / scorch material as an asset, with its soft-disc texture saved beside it.
        private static Material EffectMaterial(string name, Material fresh)
        {
            Directory.CreateDirectory("Assets/_Project/Art/Strike");
            string texPath = "Assets/_Project/Art/Strike/T_Strike_SoftDisc.png";
            if (!File.Exists(texPath))
            {
                File.WriteAllBytes(texPath, RangeTargetView.SoftDisc.EncodeToPNG());
                AssetDatabase.ImportAsset(texPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            fresh.SetTexture("_BaseMap", tex);
            fresh.mainTexture = tex;
            string path = $"Assets/_Project/Art/Strike/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            existing.shader = fresh.shader;
            existing.CopyPropertiesFromMaterial(fresh);
            existing.shaderKeywords = fresh.shaderKeywords;
            existing.renderQueue = fresh.renderQueue;
            existing.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            Directory.CreateDirectory("Assets/_Project/Art/Strike/Meshes");
            string path = $"Assets/_Project/Art/Strike/Meshes/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static RangeLayout LoadOrCreateLayout()
        {
            var l = AssetDatabase.LoadAssetAtPath<RangeLayout>(LayoutPath);
            if (l != null) return l;
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
            l = ScriptableObject.CreateInstance<RangeLayout>();
            AssetDatabase.CreateAsset(l, LayoutPath);
            return l;
        }

        // The catalogue entry: a free-flight "track" for the steppe scene, spawning on pad 1.
        private static void WriteTrackJson(RangeLayout layout)
        {
            var pad = layout.Pads[0];
            var t = new TrackData
            {
                Id = "strike-steppe", Name = "Strike Range", Author = "Ushqyr", Environment = "steppe", Laps = 1, FreeFlight = true,
                Spawn = new SpawnData { Position = pad.Position + Vector3.up * 0.15f, Yaw = pad.Yaw, Name = "Pad 1" },
            };
            File.WriteAllText(TrackJsonPath, t.ToJson());
            AssetDatabase.ImportAsset(TrackJsonPath);
        }
    }
}
