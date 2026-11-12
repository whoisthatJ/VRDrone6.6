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
    /// layout (so bots and targets are unchanged) with the earth banks sculpted in, four blended layers (green grass,
    /// dry grass, bare earth, packed dusty track), field roads painted in (wheel tracks, a grassy middle, ragged
    /// dusty edges), grass tufts, bushes and stones as detail, a power line and a muted post-processing look.
    /// </summary>
    public static class RangeSteppeTerrain
    {
        private const string Dir = "Assets/_Project/Art/Strike";
        private const int HeightRes = 1025, SplatRes = 2048, DetailRes = 1024;
        private const float BaseY = -30f, Span = 60f;

        /// <summary>
        /// An earth bank sculpted into the terrain: a long low ridge with a soft crest and toes, a slightly wandering
        /// line, uneven width and height and rounded ends.
        /// </summary>
        public struct Mound
        {
            public Vector2 Centre;
            public float Yaw, HalfLength, HalfWidth, Height;
            public int Seed;

            public float Reach => Mathf.Max(HalfLength, HalfWidth * 1.3f) + 2f;

            public float HeightAt(Vector2 w)
            {
                Vector2 d = w - Centre;
                if (d.sqrMagnitude > Reach * Reach) return 0f;
                float yr = Yaw * Mathf.Deg2Rad;
                var along = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr));
                float u = Vector2.Dot(d, along), v = d.x * along.y - d.y * along.x;
                float e = Mathf.Abs(u) / HalfLength;
                if (e >= 1f) return 0f;
                float s = Seed * 0.731f;
                v -= (Mathf.PerlinNoise(s + u / 9f, s * 0.5f) - 0.5f) * 1.6f;
                float hw = HalfWidth * (0.8f + 0.4f * Mathf.PerlinNoise(u / 7f + s, s * 1.3f + 4f));
                float ht = Height * (0.82f + 0.36f * Mathf.PerlinNoise(u / 11f + s * 2f, 3.1f));
                float end = Mathf.Max(0f, e - 0.5f) / 0.5f;
                float round = Mathf.Sqrt(Mathf.Max(0f, 1f - end * end));
                hw *= Mathf.Max(round, 0.001f);
                ht *= round; // height falls with width: no fin at the tips
                float t = Mathf.Clamp01(1f - Mathf.Abs(v) / hw);
                float prof = t * t * (3f - 2f * t);
                float lumps = 1f + 0.12f * (Mathf.PerlinNoise(w.x / 2.7f + s, w.y / 2.7f) - 0.5f);
                return ht * prof * lumps;
            }
        }

        private static IList<Mound> _mounds = new Mound[0];

        private static float MoundHeight(Vector2 w)
        {
            float h = 0f;
            foreach (var m in _mounds) h = Mathf.Max(h, m.HeightAt(w));
            return h;
        }

        public static void Build(Transform root, int layer, RangeLayout layout, IList<Mound> mounds)
        {
            _mounds = mounds ?? new Mound[0];
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
            t.heightmapPixelError = 3f;
            t.basemapDistance = 2200f; // the low-res base map stripes the tiled layers at a distance
            t.detailObjectDistance = 110f;
            t.detailObjectDensity = 1f;
            t.shadowCastingMode = ShadowCastingMode.Off; // near-flat ground under a low sun: self-shadowing only makes stripes
            go.GetComponent<TerrainCollider>().terrainData = data;

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
            // A light breeze: small, slow sway and no colour pulsing (0.5 grey is neutral in the grass shader).
            data.wavingGrassStrength = 0.12f;
            data.wavingGrassSpeed = 0.18f;
            data.wavingGrassAmount = 0.1f;
            data.wavingGrassTint = new Color(0.5f, 0.5f, 0.5f, 1f);
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
                    h[z, x] = Mathf.Clamp01((RangeSteppeBuilder.Height(w.x, w.y) + MoundHeight(w) - BaseY) / Span);
                }
            data.SetHeights(0, 0, h);
        }

        // ---------- distance to the roads (for gravel, ruts and keeping detail off the roads) ----------

        // Distance from each splat texel to the nearest road centreline (segments, not just points), up to 18 m.
        private static float[,] RoadDistance(RangeLayout layout)
        {
            var d = new float[SplatRes, SplatRes];
            for (int z = 0; z < SplatRes; z++)
                for (int x = 0; x < SplatRes; x++) d[z, x] = 999f;
            float cell = RangeSteppeBuilder.TerrainSize / (SplatRes - 1), half = RangeSteppeBuilder.TerrainSize * 0.5f;
            const float reach = 18f;
            foreach (var road in layout.Roads)
                for (int i = 0; i + 1 < road.Points.Length; i++)
                {
                    var a = new Vector2(road.Points[i].x, road.Points[i].z);
                    var b = new Vector2(road.Points[i + 1].x, road.Points[i + 1].z);
                    Vector2 ab = b - a;
                    float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                    int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach + half) / cell), x1 = Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach + half) / cell);
                    int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach + half) / cell), z1 = Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach + half) / cell);
                    for (int z = Mathf.Max(z0, 0); z <= Mathf.Min(z1, SplatRes - 1); z++)
                        for (int x = Mathf.Max(x0, 0); x <= Mathf.Min(x1, SplatRes - 1); x++)
                        {
                            var p = new Vector2(x * cell - half, z * cell - half);
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                            float dist = (p - (a + ab * t)).magnitude;
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
            // The mottled wild-grass photo is ~10 % brighter on one side: tiled, that shows as bands across the whole
            // plain. T_Steppe_GrassWild is the same photo with the large-scale gradient divided out.
            l.diffuseTexture = folder == "Grass_Mossy"
                ? AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Terrain/T_Steppe_GrassWild.jpg")
                : AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_Albedo.jpg");
            // No normal map on the grass: its broad bumps repeat with the tile and, under a low sun, light up as a grid
            // of bands across the whole plain. The grass itself gives the ground its relief.
            l.normalMapTexture = folder.StartsWith("Grass") ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_Normal.jpg");
            l.tileSize = new Vector2(tile, tile);
            // JPGs have no alpha: the terrain shader would read it as full smoothness (a sky-blue glare). Remap it to matte.
            l.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 0.04f);
            l.diffuseRemapMin = Vector4.zero;
            l.normalScale = 0.3f; // gentle: under a low sun a strong tiled normal map shows as bands
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
                // Patchy wild grass with moss and bare specks, and dry straw-brown grass: real steppe, not a lawn.
                Layer("SteppeGreen", "Grass_Field", 4.6f, new Color(1.22f, 1.0f, 0.74f, 1f)),
                Layer("SteppeDry", "Grass_Mossy", 7.5f, new Color(1.14f, 1.0f, 0.68f, 1f)), // mottled wild grass, large so its repeat does not read as a grid from the air,
                Layer("SteppeEarth", "Dirt_Field", 4f, new Color(0.7f, 0.66f, 0.58f, 1f)),
                Layer("SteppeTrack", "Dirt_Field", 3f, new Color(0.98f, 0.97f, 0.9f, 1f)), // packed, dusty wheel tracks
            };
            var a = new float[SplatRes, SplatRes, 4];
            for (int z = 0; z < SplatRes; z++)
                for (int x = 0; x < SplatRes; x++)
                {
                    var w = World(x, z, SplatRes);
                    float big = Mathf.PerlinNoise(w.x / 160f + 5.3f, w.y / 160f + 2.1f);
                    float mid = Mathf.PerlinNoise(w.x / 45f + 1.7f, w.y / 45f + 8.2f);
                    float fine = Mathf.PerlinNoise(w.x / 9f + 3.1f, w.y / 9f + 4.4f);
                    // Green and dry grass drift into each other over several scales: wide soft fields, then streaks
                    // and specks, never a clean boundary.
                    float streak = Mathf.PerlinNoise(w.x / 34f + 9.4f, w.y / 41f + 3.8f);
                    float speck = Mathf.PerlinNoise(w.x / 2.4f + 4.1f, w.y / 2.4f + 6.3f);
                    float green = Mathf.Clamp01(Mathf.InverseLerp(0.2f, 0.8f, big * 0.5f + mid * 0.2f + streak * 0.15f + fine * 0.1f + speck * 0.05f));
                    green = Mathf.Lerp(0.15f, 0.9f, green);
                    float dry = 1f - green;
                    // Bare patches with ragged, broken edges (a finer noise eats into them), not flat blobs.
                    float ragged = Mathf.PerlinNoise(w.x / 3f + 2.2f, w.y / 3f + 5.5f);
                    float earth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 0.95f, mid * 0.6f + ragged * 0.25f + fine * 0.15f)) * 0.55f; // rare scuffs only
                    float track = 0f, extraDry = 0f;
                    float rd = roadDist[z, x];
                    if (rd < 18f)
                    {
                        // A field road: a ragged edge that wanders in and out, packed wheel tracks, grass down the
                        // middle of each lane and between the lanes, dust spilling into the grass at the sides.
                        float n1 = Mathf.PerlinNoise(w.x / 6f + 11.3f, w.y / 6f + 2.9f), n2 = Mathf.PerlinNoise(w.x / 2.2f + 0.7f, w.y / 2.2f + 9.1f);
                        float patch = Mathf.PerlinNoise(w.x / 4f + 6.6f, w.y / 4f + 1.2f);
                        float edge = 5f + (n1 - 0.5f) * 2.6f + (n2 - 0.5f) * 0.9f;
                        float road = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge + 1.6f, edge - 0.8f, rd));
                        float wheels = Mathf.Max(Mathf.Exp(-Sq((rd - 1.5f) / 0.7f)), Mathf.Exp(-Sq((rd - 3.3f) / 0.7f))) * 0.85f;
                        float middle = Mathf.Max(Mathf.Exp(-Sq(rd / 0.6f)), Mathf.Exp(-Sq((rd - 2.4f) / 0.4f))) * Mathf.Lerp(0.25f, 1f, patch);
                        track = road * Mathf.Clamp01(0.5f + wheels * 0.45f - middle * 0.7f);
                        extraDry = road * middle * 0.8f;
                        float dust = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge + 4f + n2 * 4f, edge, rd)) * Mathf.Lerp(0.2f, 0.75f, patch);
                        earth = Mathf.Max(earth * (1f - road), Mathf.Max(dust * (1f - road), road * 0.3f * (1f - wheels)));
                    }
                    // Packed ground round the pads.
                    float dp = new Vector2(w.x, w.y - RangeSteppeBuilder.PadZ).magnitude;
                    dp += (Mathf.PerlinNoise(w.x / 7f + 1.1f, w.y / 7f + 5.2f) - 0.5f) * 22f;
                    if (dp < 62f)
                    {
                        // Trodden ground: packed earth with worn grass showing through, fading out raggedly.
                        float camp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(62f, 30f, dp));
                        float worn = Mathf.PerlinNoise(w.x / 3.3f + 8.8f, w.y / 3.3f + 1.4f);
                        track = Mathf.Max(track, camp * Mathf.Lerp(0.25f, 0.8f, worn));
                        earth = Mathf.Max(earth, camp * 0.25f * (1f - worn));
                    }
                    // Earth banks: bare earth where the sides are steep, grass on the crest, a little spill at the toes.
                    float mh = MoundHeight(w);
                    if (mh > 0.02f)
                    {
                        float slope = Mathf.Max(Mathf.Abs(MoundHeight(w + new Vector2(0.5f, 0f)) - mh), Mathf.Abs(MoundHeight(w + new Vector2(0f, 0.5f)) - mh)) / 0.5f;
                        float patch = Mathf.PerlinNoise(w.x / 2.5f + 3.3f, w.y / 2.5f + 7.7f);
                        earth = Mathf.Max(earth, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.1f, slope)) * Mathf.Lerp(0.1f, 0.65f, patch));
                        earth = Mathf.Max(earth, Mathf.Clamp01(1f - mh / 0.3f) * patch * 0.35f);
                    }
                    float rest = Mathf.Clamp01(1f - earth - track);
                    float g = green * rest, dr = dry * rest + extraDry;
                    float sum = g + dr + earth + track;
                    a[z, x, 0] = g / sum;
                    a[z, x, 1] = dr / sum;
                    a[z, x, 2] = earth / sum;
                    a[z, x, 3] = track / sum;
                }
            data.SetAlphamaps(0, 0, a);
        }

        private static float Sq(float v) => v * v;

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
                // Short dense grass that covers the ground.
                new DetailPrototype
                {
                    prototypeTexture = grassTex, renderMode = DetailRenderMode.Grass, usePrototypeMesh = false,
                    minWidth = 0.45f, maxWidth = 0.8f, minHeight = 0.16f, maxHeight = 0.32f, noiseSpread = 0.25f,
                    healthyColor = new Color(0.62f, 0.66f, 0.36f), dryColor = new Color(0.8f, 0.74f, 0.46f), useInstancing = false,
                },
                // Taller dry tufts standing out of it.
                new DetailPrototype
                {
                    prototypeTexture = grassTex, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false,
                    minWidth = 0.5f, maxWidth = 0.9f, minHeight = 0.5f, maxHeight = 0.95f, noiseSpread = 0.5f,
                    healthyColor = new Color(0.78f, 0.74f, 0.44f), dryColor = new Color(0.92f, 0.84f, 0.56f), useInstancing = false,
                },
                new DetailPrototype
                {
                    prototype = Prefab("P_Steppe_Bush", RangeSteppeDressing.ShrubMesh(11), RangeSteppeDressing.LeafMaterial()), renderMode = DetailRenderMode.VertexLit,
                    usePrototypeMesh = true, useInstancing = true, minWidth = 0.8f, maxWidth = 1.6f, minHeight = 0.7f, maxHeight = 1.4f,
                    healthyColor = Color.white, dryColor = new Color(0.9f, 0.86f, 0.7f),
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
            var tall = new int[DetailRes, DetailRes];
            var bush = new int[DetailRes, DetailRes];
            var stone = new int[DetailRes, DetailRes];
            var rng = new System.Random(4411);
            for (int z = 0; z < DetailRes; z++)
                for (int x = 0; x < DetailRes; x++)
                {
                    var w = World(x, z, DetailRes);
                    float rd = roadDist[Mathf.Min(z * SplatRes / DetailRes, SplatRes - 1), Mathf.Min(x * SplatRes / DetailRes, SplatRes - 1)];
                    // Grass thins out towards the road along a ragged line (no hard edge), and grows in the middle strips.
                    float edgeN = Mathf.PerlinNoise(w.x / 6f + 11.3f, w.y / 6f + 2.9f);
                    float edge = 5f + (edgeN - 0.5f) * 2.6f;
                    float nearRoad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 0.5f, edge + 3f, rd));
                    if (rd < 1.2f || Mathf.Abs(rd - 2.4f) < 0.5f) nearRoad = Mathf.Max(nearRoad, 0.3f);
                    double r = rng.NextDouble();
                    if (nearRoad <= 0.01f) continue;
                    float dp = new Vector2(w.x, w.y - RangeSteppeBuilder.PadZ).magnitude;
                    dp += (Mathf.PerlinNoise(w.x / 7f + 1.1f, w.y / 7f + 5.2f) - 0.5f) * 22f; // a ragged edge to the trodden camp ground
                    if (dp < 34f) continue;
                    float pad = Mathf.InverseLerp(34f, 62f, dp);
                    if (NearSpot(layout, w, 7f)) continue;
                    float clump = Mathf.PerlinNoise(w.x / 12f + 7.7f, w.y / 12f + 0.3f);
                    float tuft = Mathf.PerlinNoise(w.x / 5f + 2.7f, w.y / 5f + 8.3f);
                    float mh = MoundHeight(w);
                    float bank = mh > 0.05f ? Mathf.Abs(MoundHeight(w + new Vector2(1f, 0f)) - mh) + Mathf.Abs(MoundHeight(w + new Vector2(0f, 1f)) - mh) : 0f;
                    float onBank = Mathf.Clamp01(1f - bank * 0.6f);
                    float cover = nearRoad * onBank * pad;
                    grass[z, x] = Mathf.RoundToInt(Mathf.Lerp(2f, 5f, clump) * cover);
                    if (tuft > 0.55f && r > 0.5) tall[z, x] = Mathf.RoundToInt(Mathf.Lerp(1f, 2.4f, Mathf.InverseLerp(0.55f, 0.85f, tuft)) * cover);
                    if (mh > 0.05f) continue;
                    if (r < 0.0012 && rd > 20f) bush[z, x] = 1 + (clump > 0.6f ? 1 : 0);
                    else if (r < 0.002 && rd > 12f) stone[z, x] = 1;
                }
            data.SetDetailLayer(0, 0, 0, grass);
            data.SetDetailLayer(0, 0, 1, tall);
            data.SetDetailLayer(0, 0, 2, bush);
            data.SetDetailLayer(0, 0, 3, stone);
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
            string path = Dir + "/Terrain/T_Steppe_Grass3.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int w = 256, h = 256;
            var px = new Color32[w * h];
            var rng = new System.Random(77);
            for (int b = 0; b < 34; b++)
            {
                float x0 = 20f + (float)rng.NextDouble() * (w - 40), lean = ((float)rng.NextDouble() - 0.5f) * 90f;
                float height = h * (0.35f + 0.6f * (float)rng.NextDouble()), width = 1.6f + (float)rng.NextDouble() * 2.6f;
                var col = Color.Lerp(new Color(0.8f, 0.86f, 0.62f), new Color(1f, 0.95f, 0.75f), (float)rng.NextDouble());
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

        private static Mesh StoneMesh() => Blob("Steppe Stone", 23, 0.25f, new Vector3(0.5f, 0.3f, 0.45f), 1);

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
            color.postExposure.Override(0.35f);
            color.contrast.Override(12f);
            color.saturation.Override(-8f);
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
