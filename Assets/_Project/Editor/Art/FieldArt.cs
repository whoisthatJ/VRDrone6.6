using System.Collections.Generic;
using System.IO;
using System.Linq;
using GateRush.EditorTools.Builders;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Track_Field (V4): a sports field at golden hour. Unity Terrain (flat across the whole play area so the existing
    /// ground collider still matches, rolling hills outside), height-blended grass/worn/dirt layers painted along the
    /// racing line, detail grass, an impostor tree line, a chain-link perimeter fence, a pilot pit with canopies and
    /// flags, pitch chalk lines and a launch pad. Lit by the hilly_terrain HDRI with a matching low sun.
    /// </summary>
    public static class FieldArt
    {
        public const string TerrainDataPath = "Assets/_Project/Art/Terrain/TD_Field.asset";
        private static readonly Rect PlayArea = Rect.MinMaxRect(-125f, -125f, 140f, 135f);
        private const float TerrainSize = 1000f, TerrainHeight = 60f;

        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "FieldPillar", "M_Concrete_Blocks" },
            { "M_Concrete_Wall", "M_Concrete_Blocks" },   // earlier art-pass revisions
            { "M_Concrete_Pillar", "M_Concrete_Blocks" },
            { "TreeLine", "" },
            { "GroundField", "" },
        };

        public static void Apply(ArtContext ctx)
        {
            ArtPass.RemapEnvironment(ctx, Map);
            var course = CourseLibrary.Field();
            var line = CourseLine(course);
            var pillars = ctx.Environment.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Pillar")).Select(t => t.position).ToList();

            var look = new SceneLook
            {
                Hdri = "hilly_terrain_01_puresky",
                SkyExposure = 0.8f,
                AmbientIntensity = 1.1f,
                SunIntensity = 3.2f,
                SunTint = new Color(1f, 0.93f, 0.85f),
                ShadowStrength = 0.9f,
                FogDensity = 0.0009f,
                FogBrightness = 0.85f,
                PostExposure = 0.15f,
                Contrast = 10f,
                Saturation = 6f,
                Temperature = 0f,
                BloomIntensity = 0.3f,
            };
            ArtPass.ApplyLook(ctx, look);

            var terrain = BuildTerrain(ctx, course, line);
            BuildTrees(ctx, terrain);
            BuildFence(ctx, pillars);
            BuildPit(ctx, pillars);
            BuildPitchLines(ctx);
            BuildLaunchPad(ctx, course.Spawn.Position);
            ArtPass.AddProbe(ctx, "Field Probe", new Vector3(35f, 8f, 15f), new Vector3(320f, 60f, 320f), false, 256);
            ctx.Log.Append($" pillars {pillars.Count};");
        }

        /// <summary>Spawn → every gate in order → back to the first gate.</summary>
        public static List<Vector3> CourseLine(GateRush.Track.TrackData t)
        {
            var pts = new List<Vector3> { t.Spawn.Position };
            pts.AddRange(t.Gates.Select(g => g.Position));
            if (t.Gates.Count > 0) pts.Add(t.Gates[0].Position);
            return pts;
        }

        private static float DistanceToLine(List<Vector3> line, float x, float z)
        {
            float best = float.MaxValue;
            var p = new Vector2(x, z);
            for (int i = 1; i < line.Count; i++)
            {
                var a = new Vector2(line[i - 1].x, line[i - 1].z);
                var b = new Vector2(line[i].x, line[i].z);
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        private static float OutsidePlay(float x, float z)
        {
            float dx = Mathf.Max(PlayArea.xMin - x, 0f, x - PlayArea.xMax);
            float dz = Mathf.Max(PlayArea.yMin - z, 0f, z - PlayArea.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float Smooth(float e0, float e1, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(e0, e1, x));

        private static float HeightAt(float x, float z)
        {
            float d = OutsidePlay(x, z);
            if (d <= 0f) return 0f;
            float n1 = Mathf.PerlinNoise(x * 0.012f + 31f, z * 0.012f + 7f);
            float n2 = Mathf.PerlinNoise(x * 0.004f + 3f, z * 0.004f + 91f);
            float near = Smooth(0f, 70f, d) * (0.8f + 3.5f * n1);
            float far = Smooth(160f, 420f, d) * (6f + 22f * n2);
            return near + far;
        }

        private static Terrain BuildTerrain(ArtContext ctx, GateRush.Track.TrackData course, List<Vector3> line)
        {
            AssetDatabase.DeleteAsset(TerrainDataPath);
            var td = new TerrainData { heightmapResolution = 513 };
            td.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            int hr = td.heightmapResolution;
            var heights = new float[hr, hr];
            for (int iz = 0; iz < hr; iz++)
            for (int ix = 0; ix < hr; ix++)
            {
                float x = -TerrainSize / 2 + ix / (float)(hr - 1) * TerrainSize;
                float z = -TerrainSize / 2 + iz / (float)(hr - 1) * TerrainSize;
                heights[iz, ix] = HeightAt(x, z) / TerrainHeight;
            }
            td.SetHeights(0, 0, heights);

            td.terrainLayers = EnvironmentAssets.FieldLayers.Select(l => EnvironmentAssets.Layer(l.Name)).ToArray();
            const int ar = 512;
            td.alphamapResolution = ar;
            var alpha = new float[ar, ar, td.terrainLayers.Length];
            var gateFeet = course.Gates.Select(g => g.Position).ToList();
            var wornMap = new float[ar, ar];
            for (int iz = 0; iz < ar; iz++)
            for (int ix = 0; ix < ar; ix++)
            {
                float x = -TerrainSize / 2 + (ix + 0.5f) / ar * TerrainSize;
                float z = -TerrainSize / 2 + (iz + 0.5f) / ar * TerrainSize;
                float noise = Mathf.PerlinNoise(x * 0.08f + 5f, z * 0.08f + 3f);
                float worn = Smooth(7f, 2.5f, DistanceToLine(line, x, z)) * (0.55f + 0.45f * noise);
                float dirt = 0f;
                dirt = Mathf.Max(dirt, Smooth(4.5f, 1.5f, Vector2.Distance(new Vector2(x, z), new Vector2(course.Spawn.Position.x, course.Spawn.Position.z))));
                foreach (var g in gateFeet) dirt = Mathf.Max(dirt, Smooth(3.5f, 1f, Vector2.Distance(new Vector2(x, z), new Vector2(g.x, g.z))) * 0.8f);
                float outside = OutsidePlay(x, z);
                dirt = Mathf.Max(dirt, outside > 8f ? Smooth(0.62f, 0.75f, Mathf.PerlinNoise(x * 0.02f + 77f, z * 0.02f + 11f)) * 0.7f : 0f);
                // Pit area and the path to it are worn.
                worn = Mathf.Max(worn, Smooth(9f, 4f, Vector2.Distance(new Vector2(x, z), new Vector2(-45f, 4f))));
                worn = Mathf.Clamp01(worn - dirt * 0.5f);
                float lawn = Mathf.Clamp01(1f - worn - dirt);
                float sum = lawn + worn + dirt;
                alpha[iz, ix, 0] = lawn / sum;
                alpha[iz, ix, 1] = worn / sum;
                alpha[iz, ix, 2] = dirt / sum;
                if (alpha.GetLength(2) > 3) alpha[iz, ix, 3] = 0f;
                wornMap[iz, ix] = Mathf.Clamp01(worn + dirt);
            }
            td.SetAlphamaps(0, 0, alpha);

            // Detail grass: short lawn grass on the field, taller rough grass outside.
            var grass = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtProcedural.File("GrassBlades", "Albedo"));
            var lawnGrass = new DetailPrototype
            {
                prototypeTexture = grass, renderMode = DetailRenderMode.Grass, usePrototypeMesh = false,
                minWidth = 0.18f, maxWidth = 0.3f, minHeight = 0.08f, maxHeight = 0.16f, noiseSpread = 0.4f,
                healthyColor = new Color(0.6f, 0.72f, 0.42f), dryColor = new Color(0.74f, 0.7f, 0.44f),
            };
            var roughGrass = new DetailPrototype
            {
                prototypeTexture = grass, renderMode = DetailRenderMode.Grass, usePrototypeMesh = false,
                minWidth = 0.3f, maxWidth = 0.55f, minHeight = 0.35f, maxHeight = 0.8f, noiseSpread = 0.6f,
                healthyColor = new Color(0.6f, 0.68f, 0.4f), dryColor = new Color(0.82f, 0.72f, 0.42f),
            };
            td.detailPrototypes = new[] { lawnGrass, roughGrass };
            const int dr = 1024;
            td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            td.SetDetailResolution(dr, 32);
            var lawnLayer = new int[dr, dr];
            var roughLayer = new int[dr, dr];
            var mown = RouteCorridors(dr);
            for (int iz = 0; iz < dr; iz++)
            for (int ix = 0; ix < dr; ix++)
            {
                float x = -TerrainSize / 2 + (ix + 0.5f) / dr * TerrainSize;
                float z = -TerrainSize / 2 + (iz + 0.5f) / dr * TerrainSize;
                float worn = wornMap[iz * ar / dr, ix * ar / dr];
                float outside = OutsidePlay(x, z);
                if (outside > 380f || mown[iz, ix]) continue;
                float n = Mathf.PerlinNoise(x * 0.15f + 13f, z * 0.15f + 29f);
                if (outside < 12f) lawnLayer[iz, ix] = Mathf.RoundToInt((1f - worn) * (9f + 8f * n));
                else roughLayer[iz, ix] = Mathf.RoundToInt((1f - worn) * (3f + 6f * n));
            }
            td.SetDetailLayer(0, 0, 0, lawnLayer);
            td.SetDetailLayer(0, 0, 1, roughLayer);
            td.wavingGrassStrength = 0.25f;
            td.wavingGrassAmount = 0.3f;
            td.wavingGrassSpeed = 0.4f;
            td.wavingGrassTint = new Color(0.85f, 0.9f, 0.75f);
            AssetDatabase.CreateAsset(td, TerrainDataPath);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(ctx.Dressing, false);
            go.transform.position = new Vector3(-TerrainSize / 2, 0f, -TerrainSize / 2);
            go.layer = ArtPass.TrackGeometryLayer;
            GameObjectUtility.SetStaticEditorFlags(go, ArtPass.LightmappedStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = AssetDatabase.LoadAssetAtPath<Material>(EnvironmentAssets.TerrainMaterialPath);
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 800f;
            terrain.detailObjectDistance = 120f;
            terrain.detailObjectDensity = 0.85f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrain.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            var so = new SerializedObject(terrain);
            var scale = so.FindProperty("m_ScaleInLightmap");
            if (scale != null) scale.floatValue = 0.06f;
            so.ApplyModifiedPropertiesWithoutUndo();
            ctx.Log.Append(" terrain;");
            return terrain;
        }

        private static void BuildTrees(ArtContext ctx, Terrain terrain)
        {
            var mat = ImpostorBaker.Bake();
            var card = ImpostorBaker.CardSize;
            var mesh = ArtPass.MeshAsset("TreeImpostorCard", () => ProcMesh.CrossedQuads(ImpostorBaker.Views, card.x, card.y));
            var rng = new System.Random(4242);
            var chunks = new List<(Mesh, Matrix4x4, int)>[8];
            for (int i = 0; i < chunks.Length; i++) chunks[i] = new List<(Mesh, Matrix4x4, int)>();
            var colliders = ArtPass.Group(ctx, "Tree Colliders");
            int count = 0;
            for (int attempt = 0; attempt < 6000 && count < 520; attempt++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * 470f;
                float z = (float)(rng.NextDouble() * 2 - 1) * 470f;
                float d = OutsidePlay(x, z);
                if (d < 22f || d > 330f) continue;
                float density = Mathf.PerlinNoise(x * 0.01f + 50f, z * 0.01f + 50f);
                float want = d < 120f ? 0.75f : 0.45f;
                if (density < 1f - want) continue;
                float y = terrain.SampleHeight(new Vector3(x, 0f, z)) - 0.3f;
                float s = 0.75f + (float)rng.NextDouble() * 0.55f;
                float yaw = (float)rng.NextDouble() * 360f;
                int sector = Mathf.Clamp((int)((Mathf.Atan2(z, x) + Mathf.PI) / (Mathf.PI * 2f) * chunks.Length), 0, chunks.Length - 1);
                chunks[sector].Add((mesh, Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * s), 0));
                var col = new GameObject("Tree") { layer = ArtPass.TrackGeometryLayer };
                col.transform.SetParent(colliders, false);
                col.transform.position = new Vector3(x, y + card.y * s * 0.55f, z);
                col.AddComponent<BoxCollider>().size = new Vector3(card.x * s * 0.6f, card.y * s * 0.8f, card.x * s * 0.6f);
                count++;
            }
            var group = ArtPass.Group(ctx, "Tree Line");
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Count == 0) continue;
                var combined = ProcMesh.Combine("Trees " + i, chunks[i]);
                var go = ArtPass.AddMesh(group, "Trees " + i, combined, new[] { mat }, Vector3.zero, Quaternion.identity, Vector3.one);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            }
            ctx.Log.Append($" trees {count};");
        }

        private static void BuildFence(ArtContext ctx, List<Vector3> pillars)
        {
            var group = ArtPass.Group(ctx, "Fence");
            var r = Rect.MinMaxRect(PlayArea.xMin - 6f, PlayArea.yMin - 6f, PlayArea.xMax + 6f, PlayArea.yMax + 6f);
            Vector3[] corners = { new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMax, 0, r.yMin), new Vector3(r.xMax, 0, r.yMax), new Vector3(r.xMin, 0, r.yMax) };
            const float height = 2.2f, span = 3f;
            for (int side = 0; side < 4; side++)
            {
                Vector3 a = corners[side], b = corners[(side + 1) % 4];
                float len = Vector3.Distance(a, b);
                int panels = Mathf.CeilToInt(len / span);
                var parts = new List<(Mesh, Matrix4x4, int)>();
                for (int i = 0; i <= panels; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, i / (float)panels);
                    parts.Add(ProcMesh.Part(p + Vector3.up * height / 2, new Vector3(0.05f, height, 0.05f), Quaternion.identity, 0));
                }
                Vector3 dir = (b - a).normalized;
                parts.Add(ProcMesh.Part((a + b) / 2 + Vector3.up * (height - 0.03f), new Vector3(0.04f, 0.04f, len), Quaternion.LookRotation(dir), 0));
                parts.Add(ProcMesh.Part((a + b) / 2 + Vector3.up * height / 2, new Vector3(0.01f, height - 0.1f, len), Quaternion.LookRotation(dir), 1));
                var mesh = ProcMesh.Combine("Fence " + side, parts);
                var go = ArtPass.AddMesh(group, "Fence " + side, mesh, ArtPass.Mats("M_Steel_Brushed", "M_Fence_Chainlink"), Vector3.zero, Quaternion.identity, Vector3.one);
                var col = new GameObject("Fence collider " + side) { layer = ArtPass.TrackGeometryLayer };
                col.transform.SetParent(group, false);
                col.transform.SetPositionAndRotation((a + b) / 2 + Vector3.up * height / 2, Quaternion.LookRotation(dir));
                col.AddComponent<BoxCollider>().size = new Vector3(0.1f, height, len);
                _ = go;
            }
        }

        /// <summary>
        /// Detail cells with no grass: the corridors (lanes + a margin) and start pads of the built-in spline tracks in
        /// the Field, so the chevron boards and pads are not buried in grass.
        /// </summary>
        private static bool[,] RouteCorridors(int dr)
        {
            var mown = new bool[dr, dr];
            float cell = TerrainSize / dr;
            void Mark(Vector3 p, float radius)
            {
                int cx = Mathf.FloorToInt((p.x + TerrainSize / 2) / cell), cz = Mathf.FloorToInt((p.z + TerrainSize / 2) / cell);
                int r = Mathf.CeilToInt(radius / cell);
                for (int iz = Mathf.Max(0, cz - r); iz <= Mathf.Min(dr - 1, cz + r); iz++)
                for (int ix = Mathf.Max(0, cx - r); ix <= Mathf.Min(dr - 1, cx + r); ix++)
                {
                    float x = -TerrainSize / 2 + (ix + 0.5f) * cell - p.x, z = -TerrainSize / 2 + (iz + 0.5f) * cell - p.z;
                    if (x * x + z * z <= radius * radius) mown[iz, ix] = true;
                }
            }
            foreach (var t in RouteLibrary.All())
            {
                if (t.Environment != "field") continue;
                var route = new GateRush.Track.TrackRoute(t.Route);
                float radius = t.Route.CorridorWidth * 0.5f + t.Route.OuterLineGap + 0.6f;
                for (float d = 0f; d < route.Length; d += 0.5f) Mark(route.Sample(d).Position, radius);
                Mark(t.Spawn.Position, 3.6f);
                foreach (var prop in t.Props) Mark(prop.Position, prop.Type == "LandingPad" ? 1.8f : 0.8f);
            }
            return mown;
        }

        private static Vector3 FreeSpot(Vector3 want, List<Vector3> pillars, float clearance)
        {
            var p = want;
            for (int i = 0; i < 20 && pillars.Any(q => Vector2.Distance(new Vector2(q.x, q.z), new Vector2(p.x, p.z)) < clearance); i++)
                p += new Vector3(0f, 0f, 3f);
            return p;
        }

        private static void BuildPit(ArtContext ctx, List<Vector3> pillars)
        {
            var group = ArtPass.Group(ctx, "Pilot Pit");
            var canopy = ArtPass.MeshAsset("Canopy3m", () => ProcMesh.Canopy());
            var flag = ArtPass.MeshAsset("FeatherFlag", () => ProcMesh.FeatherFlag());
            var cone = ArtPass.MeshAsset("TrafficCone", () => ProcMesh.Cone());
            Vector3 pit = FreeSpot(new Vector3(-45f, 0f, 2f), pillars, 6f);
            string[] fabric = { "M_Fabric_CanvasRed", "M_Fabric_Canvas" };
            for (int i = 0; i < 2; i++)
            {
                var pos = pit + new Vector3(0f, 0f, i * 3.4f);
                ArtPass.AddMesh(group, "Canopy " + i, canopy, ArtPass.Mats("M_Aluminium_Brushed", fabric[i]), pos, Quaternion.Euler(0f, 90f, 0f), Vector3.one, solid: true);
                ArtPass.AddModel(group, "painted_wooden_bench", pos + new Vector3(-0.6f, 0f, 0.4f), 90f);
                ArtPass.AddModel(group, "plastic_monobloc_chair_01", pos + new Vector3(0.6f, 0f, -0.6f), 110f + i * 30f);
            }
            ArtPass.AddModel(group, "metal_toolbox", pit + new Vector3(-0.9f, 0.45f, 0.2f), 80f);
            ArtPass.AddModel(group, "plastic_crate_02", pit + new Vector3(1.2f, 0f, 5.2f), 15f);
            for (int i = 0; i < 4; i++)
            {
                var pos = pit + new Vector3(2.2f + (i % 2) * 0.6f, 0f, -2.5f + i * 3.2f);
                ArtPass.AddMesh(group, "Flag " + i, flag, ArtPass.Mats("M_Steel_Brushed", i % 2 == 0 ? "M_Fabric_GateOrange" : "M_Fabric_GateBlue"), pos, Quaternion.Euler(0f, 90f + i * 7f, 0f), Vector3.one);
            }
            // Cones mark the pilot box.
            for (int i = 0; i < 6; i++)
            {
                var pos = pit + new Vector3(3.5f, 0f, -2f + i * 2.2f);
                ArtPass.AddMesh(group, "Cone " + i, cone, ArtPass.Mats("M_Plastic_ConeOrange", "M_Reflective_White"), pos, Quaternion.Euler(0f, i * 23f, 0f), Vector3.one);
            }
        }

        private static void BuildPitchLines(ArtContext ctx)
        {
            var parts = new List<(Mesh, Matrix4x4, int)>();
            var c = new Vector3(35f, 0f, 15f);
            float hx = 50f, hz = 32f, w = 0.12f;
            Vector3 P(float x, float z) => c + new Vector3(x, 0f, z);
            ProcMesh.Strip(parts, P(-hx, -hz), P(hx, -hz), w);
            ProcMesh.Strip(parts, P(hx, -hz), P(hx, hz), w);
            ProcMesh.Strip(parts, P(hx, hz), P(-hx, hz), w);
            ProcMesh.Strip(parts, P(-hx, hz), P(-hx, -hz), w);
            ProcMesh.Strip(parts, P(0f, -hz), P(0f, hz), w);
            ProcMesh.Ring(parts, c, 9.15f, w);
            foreach (float s in new[] { -1f, 1f })
            {
                ProcMesh.Strip(parts, P(s * hx, -20f), P(s * (hx - 16.5f), -20f), w);
                ProcMesh.Strip(parts, P(s * (hx - 16.5f), -20f), P(s * (hx - 16.5f), 20f), w);
                ProcMesh.Strip(parts, P(s * (hx - 16.5f), 20f), P(s * hx, 20f), w);
            }
            var mesh = ProcMesh.Combine("Pitch Lines", parts);
            var chalk = new Material(Shader.Find(MatDef.EnvLit)) { name = "M_Chalk" };
            string path = MaterialLibrary.LibraryDir + "/M_Chalk.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) AssetDatabase.CreateAsset(chalk, path);
            else chalk = existing;
            chalk.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtProcedural.File("Chalk", "Albedo")));
            chalk.SetTextureScale("_BaseMap", Vector2.one * 2f);
            chalk.SetColor("_BaseColor", Color.white);
            chalk.SetFloat("_Smoothness", 0.1f);
            chalk.SetFloat("_Metallic", 0f);
            MaterialLibrary.ConfigureSurface(chalk, new MatDef());
            EditorUtility.SetDirty(chalk);
            var go = ArtPass.AddMesh(ArtPass.Group(ctx, "Pitch"), "Pitch Lines", mesh, new[] { chalk }, Vector3.up * 0.003f, Quaternion.identity, Vector3.one, castShadows: false);
            go.GetComponent<MeshRenderer>().receiveShadows = true;
        }

        private static void BuildLaunchPad(ArtContext ctx, Vector3 spawn)
        {
            var parts = new List<(Mesh, Matrix4x4, int)>
            {
                ProcMesh.Part(new Vector3(spawn.x, 0.008f, spawn.z), new Vector3(1.4f, 0.016f, 1.4f), Quaternion.identity, 0),
            };
            float e = 0.75f;
            parts.Add(ProcMesh.Part(new Vector3(spawn.x, 0.009f, spawn.z - e), new Vector3(1.6f, 0.018f, 0.12f), Quaternion.identity, 1));
            parts.Add(ProcMesh.Part(new Vector3(spawn.x, 0.009f, spawn.z + e), new Vector3(1.6f, 0.018f, 0.12f), Quaternion.identity, 1));
            parts.Add(ProcMesh.Part(new Vector3(spawn.x - e, 0.009f, spawn.z), new Vector3(0.12f, 0.018f, 1.6f), Quaternion.identity, 1));
            parts.Add(ProcMesh.Part(new Vector3(spawn.x + e, 0.009f, spawn.z), new Vector3(0.12f, 0.018f, 1.6f), Quaternion.identity, 1));
            var mesh = ProcMesh.Combine("Launch Pad", parts);
            ArtPass.AddMesh(ArtPass.Group(ctx, "Start"), "Launch Pad", mesh, ArtPass.Mats("M_Rubber_Mat", "M_Fabric_GateOrange"), Vector3.zero, Quaternion.identity, Vector3.one, castShadows: false);
        }
    }
}
