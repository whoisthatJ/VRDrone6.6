using System.Collections.Generic;
using System.IO;
using Ushqyr.EditorTools.Art;
using Ushqyr.Strike;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ushqyr.EditorTools.Range
{
    /// <summary>
    /// The Strike Range ground (references: FPV strike sims): a Unity terrain on the same height function as the
    /// layout (so physics, bots and targets are unchanged), four blended layers (green grass, dry grass, bare earth,
    /// gravel by the roads), grass tufts, bushes and stones as detail, wheel ruts on the dirt roads, a power line and a
    /// muted post-processing look.
    /// </summary>
    public static class RangeSteppeTerrain
    {
        private const string Dir = "Assets/_Project/Art/Strike";
        private const int HeightRes = 513, SplatRes = 1024, DetailRes = 512;
        private const float BaseY = -30f, Span = 60f;

        public static void Build(Transform root, int layer, RangeLayout layout)
        {
            Directory.CreateDirectory(Dir + "/Terrain");
            var roadDist = RoadDistance(layout);
            var data = TerrainDataAsset();
            Heights(data);
            Layers(data, roadDist, layout);
            Details(data, roadDist, layout);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.layer = layer;
            go.isStatic = true;
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(-RangeSteppeBuilder.TerrainSize * 0.5f, BaseY, -RangeSteppeBuilder.TerrainSize * 0.5f);
            var t = go.GetComponent<Terrain>();
            t.materialTemplate = TerrainMaterial();
            t.drawInstanced = true;
            t.heightmapPixelError = 4f;
            t.basemapDistance = 400f;
            t.detailObjectDistance = 120f;
            t.detailObjectDensity = 1f;
            t.shadowCastingMode = ShadowCastingMode.TwoSided;
            go.GetComponent<TerrainCollider>().terrainData = data;

            Ruts(root, layer, layout);
            PowerLine(root, layer, layout);
            Post(root);
        }

        // ---------- heights ----------

        private static TerrainData TerrainDataAsset()
        {
            string path = Dir + "/Terrain/TD_Steppe.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, path);
            }
            data.heightmapResolution = HeightRes;
            data.alphamapResolution = SplatRes;
            data.baseMapResolution = 1024;
            // Detail values are counts of tufts / bushes / stones per cell (not a coverage fraction).
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(DetailRes, 16);
            data.size = new Vector3(RangeSteppeBuilder.TerrainSize, Span, RangeSteppeBuilder.TerrainSize);
            return data;
        }

        private static Vector2 World(int x, int z, int res)
        {
            float s = RangeSteppeBuilder.TerrainSize;
            return new Vector2(-s * 0.5f + x * s / (res - 1), -s * 0.5f + z * s / (res - 1));
        }

        private static void Heights(TerrainData data)
        {
            var h = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                {
                    var w = World(x, z, HeightRes);
                    h[z, x] = Mathf.Clamp01((RangeSteppeBuilder.Height(w.x, w.y) - BaseY) / Span);
                }
            data.SetHeights(0, 0, h);
        }

        // ---------- distance to the roads (for gravel, ruts and keeping detail off the roads) ----------

        private static float[,] RoadDistance(RangeLayout layout)
        {
            var d = new float[SplatRes, SplatRes];
            for (int z = 0; z < SplatRes; z++)
                for (int x = 0; x < SplatRes; x++) d[z, x] = 999f;
            float cell = RangeSteppeBuilder.TerrainSize / (SplatRes - 1), half = RangeSteppeBuilder.TerrainSize * 0.5f;
            const float reach = 14f;
            int r = Mathf.CeilToInt(reach / cell);
            foreach (var road in layout.Roads)
                for (int i = 0; i < road.Points.Length; i++)
                {
                    Vector3 p = road.Points[i];
                    int cx = Mathf.RoundToInt((p.x + half) / cell), cz = Mathf.RoundToInt((p.z + half) / cell);
                    for (int z = cz - r; z <= cz + r; z++)
                        for (int x = cx - r; x <= cx + r; x++)
                        {
                            if (x < 0 || z < 0 || x >= SplatRes || z >= SplatRes) continue;
                            float dist = new Vector2(x * cell - half - p.x, z * cell - half - p.z).magnitude;
                            if (dist < d[z, x]) d[z, x] = dist;
                        }
                }
            return d;
        }

        // ---------- texture layers ----------

        private static TerrainLayer Layer(string name, string folder, float tile, Color tint)
        {
            string path = $"{Dir}/Terrain/TL_{name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l == null)
            {
                l = new TerrainLayer();
                AssetDatabase.CreateAsset(l, path);
            }
            string tex = $"Assets/_Project/Art/Materials/Ground/{folder}/{folder}";
            l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_Albedo.jpg");
            l.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_Normal.jpg");
            l.tileSize = new Vector2(tile, tile);
            // JPGs have no alpha: the terrain shader would read it as full smoothness (a sky-blue glare). Remap it to matte.
            l.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 0.04f);
            l.diffuseRemapMin = Vector4.zero;
            l.normalScale = 0.8f;
            // A matte mask map (metallic 0, occlusion 1, height 0.5, smoothness low): the shader then ignores the JPG's alpha.
            l.maskMapTexture = MatteMask();
            l.maskMapRemapMin = Vector4.zero;
            l.maskMapRemapMax = Vector4.one;
            l.smoothness = 0.05f;
            EditorUtility.SetDirty(l);
            return l;
        }

        private static Texture2D MatteMask()
        {
            string path = Dir + "/Terrain/T_Steppe_MatteMask.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(0, 255, 128, 18);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false;
            imp.mipmapEnabled = false;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void Layers(TerrainData data, float[,] roadDist, RangeLayout layout)
        {
            data.terrainLayers = new[]
            {
                Layer("SteppeGreen", "Grass_Field", 5f, new Color(0.86f, 0.92f, 0.7f, 1f)),
                Layer("SteppeDry", "Grass_Field", 6.5f, new Color(1.2f, 1.02f, 0.58f, 1f)), // straw-gold dry steppe
                Layer("SteppeEarth", "Dirt_Field", 4f, new Color(0.82f, 0.78f, 0.7f, 1f)),
                Layer("SteppeGravel", "Gravel", 3f, new Color(0.9f, 0.88f, 0.82f, 1f)),
            };
            var a = new float[SplatRes, SplatRes, 4];
            for (int z = 0; z < SplatRes; z++)
                for (int x = 0; x < SplatRes; x++)
                {
                    var w = World(x, z, SplatRes);
                    float big = Mathf.PerlinNoise(w.x / 160f + 5.3f, w.y / 160f + 2.1f);
                    float mid = Mathf.PerlinNoise(w.x / 45f + 1.7f, w.y / 45f + 8.2f);
                    float fine = Mathf.PerlinNoise(w.x / 9f + 3.1f, w.y / 9f + 4.4f);
                    float green = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, big * 0.8f + fine * 0.2f));
                    float dry = 1f - green;
                    float earth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.82f, mid * 0.85f + fine * 0.15f)) * 0.85f;
                    float gravel = 0f;
                    float rd = roadDist[z, x];
                    earth = Mathf.Max(earth, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 5.5f, rd)) * 0.9f);
                    // Road edges: dusty earth (gravel there read as kerbs); gravel only round the pads.
                    float dp = new Vector2(w.x, w.y - RangeSteppeBuilder.PadZ).magnitude;
                    if (dp < 60f) gravel = Mathf.Max(gravel, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 35f, dp)) * 0.85f);
                    float rest = Mathf.Clamp01(1f - earth - gravel);
                    float g = green * rest, dr = dry * rest;
                    float sum = g + dr + earth + gravel;
                    a[z, x, 0] = g / sum;
                    a[z, x, 1] = dr / sum;
                    a[z, x, 2] = earth / sum;
                    a[z, x, 3] = gravel / sum;
                }
            data.SetAlphamaps(0, 0, a);
        }

        private static Material TerrainMaterial()
        {
            string path = Dir + "/Terrain/M_Steppe_Terrain.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")) { name = "M_Steppe_Terrain" };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------- grass, bushes, stones ----------

        private static void Details(TerrainData data, float[,] roadDist, RangeLayout layout)
        {
            var grassTex = GrassTexture();
            var protos = new List<DetailPrototype>
            {
                new DetailPrototype
                {
                    prototypeTexture = grassTex, renderMode = DetailRenderMode.Grass, usePrototypeMesh = false,
                    minWidth = 0.5f, maxWidth = 0.9f, minHeight = 0.35f, maxHeight = 0.75f, noiseSpread = 0.4f,
                    healthyColor = new Color(0.58f, 0.62f, 0.36f), dryColor = new Color(0.72f, 0.64f, 0.42f), useInstancing = false,
                },
                new DetailPrototype
                {
                    prototype = Prefab("P_Steppe_Bush", BushMesh(), MaterialLibrary.Get("M_Expo_Leaves")), renderMode = DetailRenderMode.VertexLit,
                    usePrototypeMesh = true, useInstancing = true, minWidth = 0.7f, maxWidth = 1.5f, minHeight = 0.6f, maxHeight = 1.3f,
                    healthyColor = Color.white, dryColor = new Color(0.85f, 0.8f, 0.6f),
                },
                new DetailPrototype
                {
                    prototype = Prefab("P_Steppe_Stone", StoneMesh(), MaterialLibrary.Get("M_Concrete_Damaged")), renderMode = DetailRenderMode.VertexLit,
                    usePrototypeMesh = true, useInstancing = true, minWidth = 0.5f, maxWidth = 1.4f, minHeight = 0.4f, maxHeight = 1.0f,
                    healthyColor = new Color(0.75f, 0.72f, 0.66f), dryColor = new Color(0.62f, 0.6f, 0.55f),
                },
            };
            data.detailPrototypes = protos.ToArray();
            var grass = new int[DetailRes, DetailRes];
            var bush = new int[DetailRes, DetailRes];
            var stone = new int[DetailRes, DetailRes];
            var rng = new System.Random(4411);
            for (int z = 0; z < DetailRes; z++)
                for (int x = 0; x < DetailRes; x++)
                {
                    var w = World(x, z, DetailRes);
                    float rd = roadDist[Mathf.Min(z * SplatRes / DetailRes, SplatRes - 1), Mathf.Min(x * SplatRes / DetailRes, SplatRes - 1)];
                    if (rd < 7f) continue;
                    float dp = new Vector2(w.x, w.y - RangeSteppeBuilder.PadZ).magnitude;
                    if (dp < 45f) continue;
                    if (NearSpot(layout, w, 9f)) continue;
                    float mid = Mathf.PerlinNoise(w.x / 45f + 1.7f, w.y / 45f + 8.2f);
                    float clump = Mathf.PerlinNoise(w.x / 12f + 7.7f, w.y / 12f + 0.3f);
                    if (mid < 0.66f) grass[z, x] = Mathf.RoundToInt(Mathf.Lerp(2f, 9f, clump));
                    double r = rng.NextDouble();
                    if (r < 0.0035 && rd > 20f) bush[z, x] = 1 + (clump > 0.6f ? 1 : 0);
                    else if (r < 0.006 && rd > 12f) stone[z, x] = 1;
                }
            data.SetDetailLayer(0, 0, 0, grass);
            data.SetDetailLayer(0, 0, 1, bush);
            data.SetDetailLayer(0, 0, 2, stone);
        }

        private static bool NearSpot(RangeLayout layout, Vector2 w, float r)
        {
            foreach (var s in layout.Spots)
                if ((new Vector2(s.Position.x, s.Position.z) - w).sqrMagnitude < r * r) return true;
            return false;
        }

        // Tufts of grass blades on a transparent background (bottom = ground).
        private static Texture2D GrassTexture()
        {
            string path = Dir + "/Terrain/T_Steppe_Grass.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int w = 256, h = 256;
            var px = new Color32[w * h];
            var rng = new System.Random(77);
            for (int b = 0; b < 46; b++)
            {
                float x0 = 20f + (float)rng.NextDouble() * (w - 40), lean = ((float)rng.NextDouble() - 0.5f) * 90f;
                float height = h * (0.45f + 0.5f * (float)rng.NextDouble()), width = 3f + (float)rng.NextDouble() * 4f;
                var col = Color.Lerp(new Color(0.75f, 0.8f, 0.45f), new Color(0.95f, 0.85f, 0.55f), (float)rng.NextDouble());
                for (int y = 0; y < height; y++)
                {
                    float f = y / height;
                    float cx = x0 + lean * f * f, half = width * (1f - f) * 0.5f + 0.4f;
                    for (int x = Mathf.FloorToInt(cx - half); x <= Mathf.CeilToInt(cx + half); x++)
                    {
                        if (x < 0 || x >= w) continue;
                        float a = Mathf.Clamp01(half - Mathf.Abs(x - cx) + 0.5f);
                        var c = col * Mathf.Lerp(0.7f, 1.05f, f);
                        int i = y * w + x;
                        byte alpha = (byte)Mathf.Max(px[i].a, a * 255f);
                        px[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), alpha);
                    }
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static GameObject Prefab(string name, Mesh mesh, Material mat)
        {
            string meshPath = $"{Dir}/Terrain/{name}_Mesh.asset", prefabPath = $"{Dir}/Terrain/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, meshPath);
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // A lumpy low-poly blob (rough sphere with noise), base at y = 0.
        private static Mesh Blob(string name, int seed, float rough, Vector3 scale, int lumps)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var rng = new System.Random(seed);
            for (int l = 0; l < lumps; l++)
            {
                Vector3 c = l == 0 ? Vector3.zero : new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.3f, (float)rng.NextDouble() - 0.5f) * 0.9f;
                float s = l == 0 ? 1f : 0.55f + 0.3f * (float)rng.NextDouble();
                int baseIndex = verts.Count;
                const int rings = 6, segs = 9;
                for (int i = 0; i <= rings; i++)
                    for (int j = 0; j < segs; j++)
                    {
                        float th = Mathf.PI * i / rings, ph = 2f * Mathf.PI * j / segs;
                        var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        float k = 1f + rough * (Mathf.PerlinNoise(n.x * 2.3f + seed + l, n.z * 2.3f + n.y * 1.7f) - 0.5f) * 2f;
                        var p = c + Vector3.Scale(n * k * s, scale);
                        p.y = Mathf.Max(p.y + scale.y * s * 0.55f, 0f);
                        verts.Add(p);
                    }
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < segs; j++)
                    {
                        int a = baseIndex + i * segs + j, b = baseIndex + i * segs + (j + 1) % segs, c2 = a + segs, d = b + segs;
                        tris.AddRange(new[] { a, b, c2, b, d, c2 });
                    }
            }
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            var uv = new Vector2[verts.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(verts[i].x + verts[i].y, verts[i].z + verts[i].y);
            m.uv = uv;
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BushMesh() => Blob("Steppe Bush", 11, 0.35f, new Vector3(0.6f, 0.45f, 0.6f), 5);
        private static Mesh StoneMesh() => Blob("Steppe Stone", 23, 0.25f, new Vector3(0.5f, 0.3f, 0.45f), 1);

        // ---------- wheel ruts on the dirt roads ----------

        private static void Ruts(Transform root, int layer, RangeLayout layout)
        {
            string path = Dir + "/M_Strike_Ruts.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(MaterialLibrary.Get("M_Dirt_Field")) { name = "M_Strike_Ruts" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", new Color(0.93f, 0.88f, 0.83f));
            EditorUtility.SetDirty(mat);
            int r = 0;
            foreach (var road in layout.Roads)
            {
                // Two lanes, two wheel tracks each.
                foreach (float off in new[] { -3.3f, -1.5f, 1.5f, 3.3f })
                {
                    var v = new List<Vector3>();
                    var t = new List<int>();
                    var uv = new List<Vector2>();
                    float along = 0f;
                    for (int i = 0; i < road.Points.Length; i++)
                    {
                        Vector3 f = road.Points[Mathf.Min(i + 1, road.Points.Length - 1)] - road.Points[Mathf.Max(i - 1, 0)];
                        f.y = 0f;
                        Vector3 right = new Vector3(f.z, 0f, -f.x).normalized;
                        if (i > 0) along += Vector3.Distance(road.Points[i - 1], road.Points[i]);
                        foreach (float s in new[] { -0.17f, 0.17f })
                        {
                            Vector3 p = road.Points[i] + right * (off + s);
                            p.y = RangeSteppeBuilder.Height(p.x, p.z) + 0.08f;
                            v.Add(p);
                            uv.Add(new Vector2(s > 0 ? 0.25f : 0f, along / 4f));
                        }
                        if (i > 0)
                        {
                            int b = v.Count - 4;
                            t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                        }
                    }
                    var mesh = new Mesh { name = $"Ruts {r}", indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(v);
                    mesh.SetUVs(0, uv);
                    mesh.SetTriangles(t, 0);
                    mesh.RecalculateNormals();
                    mesh.RecalculateTangents();
                    string meshPath = $"{Dir}/Meshes/Steppe_Ruts_{r}.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
                    else AssetDatabase.CreateAsset(mesh, meshPath);
                    var go = new GameObject($"Wheel Ruts {r}") { layer = layer, isStatic = true };
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    r++;
                }
            }
        }

        // ---------- a power line across the range ----------

        private static void PowerLine(Transform root, int layer, RangeLayout layout)
        {
            if (layout.Roads.Count < 2) return;
            var wood = MaterialLibrary.Get("M_Wood_Planks");
            var wire = MaterialLibrary.Get("M_Plastic_Black");
            var road = layout.Roads[1];
            var poles = new List<Vector3>();
            // Every ~45 m, 34 m to the side of the road: clear of the lanes and of wrecks rolled off it.
            for (float d = 30f; d < road.Length - 30f; d += 45f)
            {
                road.Sample(d, out var p, out var f);
                f.y = 0f;
                Vector3 right = new Vector3(f.z, 0f, -f.x).normalized;
                Vector3 q = p + right * 34f;
                if (NearSpot(layout, new Vector2(q.x, q.z), 14f)) continue;
                q.y = RangeSteppeBuilder.Height(q.x, q.z);
                poles.Add(q);
            }
            var kit = new VehicleKit { Tile = 2f };
            var wires = new VehicleKit();
            for (int i = 0; i < poles.Count; i++)
            {
                Vector3 p = poles[i];
                kit.Cylinder(VehicleKit.Slot.Paint, p + Vector3.down * 0.5f, p + Vector3.up * 9f, 0.14f, 8);
                Vector3 next = i + 1 < poles.Count ? poles[i + 1] : poles[Mathf.Max(0, i - 1)];
                Vector3 dir = (next - p); dir.y = 0f;
                Vector3 across = Vector3.Cross(Vector3.up, dir.normalized);
                kit.Box(VehicleKit.Slot.Paint, p + Vector3.up * 8.4f, new Vector3(0.12f, 0.12f, 2.2f), 0.02f, Quaternion.LookRotation(across));
                if (i + 1 >= poles.Count) continue;
                foreach (float s in new[] { -0.95f, 0f, 0.95f })
                {
                    Vector3 a = p + Vector3.up * 8.55f + across * s, b = poles[i + 1] + Vector3.up * 8.55f + across * s;
                    // A little sag: two segments through a lower midpoint.
                    Vector3 mid = (a + b) * 0.5f + Vector3.down * 0.9f;
                    wires.Cylinder(VehicleKit.Slot.Dark, a, mid, 0.015f, 4, false);
                    wires.Cylinder(VehicleKit.Slot.Dark, mid, b, 0.015f, 4, false);
                }
            }
            Place(root, layer, "Power Line Poles", kit.Build("Steppe Poles")[(int)VehicleKit.Slot.Paint], wood, "Steppe_Poles");
            Place(root, layer, "Power Line Wires", wires.Build("Steppe Wires")[(int)VehicleKit.Slot.Dark], wire, "Steppe_Wires");
        }

        private static void Place(Transform root, int layer, string name, Mesh mesh, Material mat, string asset)
        {
            if (mesh == null) return;
            string path = $"{Dir}/Meshes/{asset}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name) { layer = layer, isStatic = true };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ---------- muted, slightly hazy look ----------

        private static void Post(Transform root)
        {
            string path = "Assets/_Project/Art/Volumes/VP_Steppe.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet<T>(out var c))
                {
                    c = profile.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
                return c;
            }
            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var color = Get<ColorAdjustments>();
            color.postExposure.Override(0.25f);
            color.contrast.Override(12f);
            color.saturation.Override(-14f);
            color.colorFilter.Override(new Color(1f, 0.98f, 0.94f));
            var bloom = Get<Bloom>();
            bloom.intensity.Override(0.18f);
            bloom.threshold.Override(1.1f);
            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            var go = new GameObject("Post Processing");
            go.transform.SetParent(root, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 1f;
            v.sharedProfile = profile;
        }
    }
}
