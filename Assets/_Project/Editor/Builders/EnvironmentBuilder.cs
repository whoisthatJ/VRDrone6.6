using GateRush.Track;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Static world geometry for each environment (everything except the course, which comes from track JSON).
    /// All solid parts are on TrackGeometry; no realtime shadows; lights are baked-ready emissive panels plus one sun.
    /// </summary>
    public static class EnvironmentBuilder
    {
        public static void Build(string environment, Transform parent, int layer, Material grid)
        {
            switch (environment)
            {
                case "hangar": Hangar(parent, layer, grid); break;
                case "industrial": Industrial(parent, layer, grid); break;
                default: Field(parent, layer, grid); break;
            }
            ConfigureLighting(environment, parent);
        }

        // ---------- baked lighting ----------

        /// <summary>
        /// Baked indirect GI for the static world (sun stays realtime for direct light, no shadows), emissive lamps
        /// baked in, and a light-probe grid so runtime-loaded gates, props and the quad pick up the bounce light.
        /// Bake with GateRush/Lighting/Bake All Track Lighting (after building the scenes).
        /// </summary>
        private static void ConfigureLighting(string environment, Transform parent)
        {
            string path = $"Assets/_Project/Settings/Lighting_{environment}.lighting";
            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (s == null)
            {
                s = new LightingSettings { name = "Lighting_" + environment };
                UnityEditor.AssetDatabase.CreateAsset(s, path);
            }
            s.bakedGI = true;
            s.realtimeGI = false;
            s.mixedBakeMode = MixedLightingMode.IndirectOnly;
            s.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            s.lightmapResolution = environment == "hangar" ? 4f : 1.5f;
            s.lightmapMaxSize = 1024;
            s.lightmapPadding = 2;
            s.directSampleCount = 16;
            s.indirectSampleCount = 128;
            s.environmentSampleCount = 64;
            s.maxBounces = 2;
            s.compressLightmaps = true;
            s.ao = false;
            // Non-directional: one lightmap texture per atlas, cheapest to sample on Quest.
            s.directionalityMode = LightmapsMode.NonDirectional;
            s.lightmapCompression = LightmapCompression.NormalQuality;
            UnityEditor.EditorUtility.SetDirty(s);
            UnityEditor.Lightmapping.lightingSettings = s;

            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                light.lightmapBakeType = LightmapBakeType.Mixed;

            // Huge ground planes get a reduced lightmap share (they are mostly flat and evenly lit).
            foreach (var r in parent.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.gameObject.name != "Ground") continue;
                var so = new UnityEditor.SerializedObject(r);
                so.FindProperty("m_ScaleInLightmap").floatValue = environment == "hangar" ? 0.25f : 0.05f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Light probes over the flyable volume.
            Vector3 min, max;
            float spacing;
            switch (environment)
            {
                case "hangar": min = new Vector3(-46f, 0.5f, -22f); max = new Vector3(46f, 14f, 22f); spacing = 8f; break;
                case "industrial": min = new Vector3(-30f, 0.5f, -40f); max = new Vector3(120f, 20f, 110f); spacing = 20f; break;
                default: min = new Vector3(-60f, 0.5f, -60f); max = new Vector3(120f, 20f, 100f); spacing = 20f; break;
            }
            float[] heights = { min.y, 4f, max.y };
            var positions = new System.Collections.Generic.List<Vector3>();
            for (float x = min.x; x <= max.x + 0.01f; x += spacing)
            for (float z = min.z; z <= max.z + 0.01f; z += spacing)
                foreach (float y in heights) positions.Add(new Vector3(x, y, z));
            var probes = new GameObject("Light Probes").AddComponent<LightProbeGroup>();
            probes.transform.SetParent(parent, false);
            probes.probePositions = positions.ToArray();
        }

        // ---------- open field ----------

        private static void Field(Transform parent, int layer, Material grid)
        {
            Sun(new Vector3(45f, -35f, 0f), 1.2f, new Color(1f, 0.97f, 0.92f));
            Ambient(new Color(0.55f, 0.65f, 0.8f), new Color(0.45f, 0.5f, 0.5f), new Color(0.25f, 0.25f, 0.22f));
            Ground(parent, layer, Tinted(grid, "GroundField", new Color(0.35f, 0.45f, 0.3f)), 2000f);

            var pillar = Materials.Lit("FieldPillar", new Color(0.75f, 0.72f, 0.68f));
            var course = CourseLibrary.Field();
            var rng = new System.Random(1234);
            for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
            {
                float h = 3f + (float)rng.NextDouble() * 9f;
                var pos = new Vector3(x * 25f + (float)rng.NextDouble() * 8f - 4f, h * 0.5f, z * 25f + (float)rng.NextDouble() * 8f - 4f);
                if (NearCourse(course, pos, 9f)) continue;
                Box(parent, $"Pillar {x},{z}", pos, new Vector3(1.2f, h, 1.2f), pillar, layer);
            }
            // Tree-line silhouettes far away for a horizon.
            var tree = Materials.Lit("TreeLine", new Color(0.18f, 0.3f, 0.17f));
            for (int i = 0; i < 36; i++)
            {
                float a = i * 10f * Mathf.Deg2Rad;
                float h = 10f + (float)rng.NextDouble() * 12f;
                Box(parent, $"Treeline {i}", new Vector3(Mathf.Sin(a) * 320f, h * 0.5f, Mathf.Cos(a) * 320f), new Vector3(60f, h, 12f), tree, layer)
                    .transform.rotation = Quaternion.Euler(0f, i * 10f, 0f);
            }
        }

        // ---------- indoor hangar ----------

        private const float HallX = 48f, HallZ = 24f, HallH = 16f;

        private static void Hangar(Transform parent, int layer, Material grid)
        {
            Sun(new Vector3(70f, 20f, 0f), 0.6f, new Color(1f, 0.95f, 0.85f));
            Ambient(new Color(0.7f, 0.72f, 0.78f), new Color(0.55f, 0.56f, 0.6f), new Color(0.35f, 0.35f, 0.36f));
            Ground(parent, layer, Tinted(grid, "GroundHangar", new Color(0.55f, 0.56f, 0.58f)), 400f);

            var wall = Materials.Lit("HangarWall", new Color(0.36f, 0.42f, 0.5f));
            var roof = Materials.Lit("HangarRoof", new Color(0.25f, 0.27f, 0.3f));
            var steel = Materials.Lit("HangarSteel", new Color(0.55f, 0.55f, 0.58f), 0.4f);
            var lamp = Materials.Emissive("HangarLamp", new Color(1f, 0.97f, 0.9f), 2.5f);
            var stripe = Materials.Emissive("HangarStripe", new Color(1f, 0.75f, 0.1f), 1.2f);

            const float t = 0.5f;
            Box(parent, "Wall N", new Vector3(0f, HallH * 0.5f, HallZ + t * 0.5f), new Vector3(HallX * 2f + 2f * t, HallH, t), wall, layer);
            Box(parent, "Wall S", new Vector3(0f, HallH * 0.5f, -HallZ - t * 0.5f), new Vector3(HallX * 2f + 2f * t, HallH, t), wall, layer);
            Box(parent, "Wall E", new Vector3(HallX + t * 0.5f, HallH * 0.5f, 0f), new Vector3(t, HallH, HallZ * 2f), wall, layer);
            Box(parent, "Wall W", new Vector3(-HallX - t * 0.5f, HallH * 0.5f, 0f), new Vector3(t, HallH, HallZ * 2f), wall, layer);
            Box(parent, "Roof", new Vector3(0f, HallH + t * 0.5f, 0f), new Vector3(HallX * 2f + 2f * t, t, HallZ * 2f + 2f * t), roof, layer);

            // Centre row of columns and roof trusses.
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 20f;
                Box(parent, $"Column {i}", new Vector3(x, HallH * 0.5f, 0f), new Vector3(0.8f, HallH, 0.8f), steel, layer);
            }
            for (int i = -4; i <= 4; i++)
            {
                float x = i * 10f;
                Box(parent, $"Truss {i}", new Vector3(x, HallH - 1f, 0f), new Vector3(0.4f, 0.6f, HallZ * 2f), steel, layer);
                Box(parent, $"Lamp {i} N", new Vector3(x + 5f, HallH - 1.5f, 10f), new Vector3(3f, 0.15f, 0.8f), lamp, layer);
                Box(parent, $"Lamp {i} S", new Vector3(x + 5f, HallH - 1.5f, -10f), new Vector3(3f, 0.15f, 0.8f), lamp, layer);
            }
            // Floor safety stripes along the walls (visual only, flat).
            Box(parent, "Stripe N", new Vector3(0f, 0.005f, HallZ - 1f), new Vector3(HallX * 2f - 2f, 0.01f, 0.2f), stripe, layer);
            Box(parent, "Stripe S", new Vector3(0f, 0.005f, -HallZ + 1f), new Vector3(HallX * 2f - 2f, 0.01f, 0.2f), stripe, layer);
            // A big hangar door outline on the east wall.
            Box(parent, "Door", new Vector3(HallX - 0.05f, 6f, 0f), new Vector3(0.1f, 12f, 30f), Materials.Lit("HangarDoor", new Color(0.62f, 0.64f, 0.66f)), layer);
        }

        // ---------- industrial dockyard ----------

        private static void Industrial(Transform parent, int layer, Material grid)
        {
            Sun(new Vector3(35f, 120f, 0f), 1.1f, new Color(1f, 0.9f, 0.8f));
            Ambient(new Color(0.6f, 0.62f, 0.7f), new Color(0.5f, 0.48f, 0.45f), new Color(0.22f, 0.21f, 0.2f));
            Ground(parent, layer, Tinted(grid, "GroundAsphalt", new Color(0.3f, 0.3f, 0.32f)), 1200f);

            var warehouse = Materials.Lit("Warehouse", new Color(0.55f, 0.52f, 0.48f));
            var roof = Materials.Lit("WarehouseRoof", new Color(0.3f, 0.32f, 0.35f));
            var crane = Materials.Lit("Crane", new Color(0.9f, 0.65f, 0.1f), 0.3f);
            var water = Materials.Lit("Water", new Color(0.1f, 0.22f, 0.3f), 0.8f);

            // Warehouses around the yard.
            Building(parent, "Warehouse A", new Vector3(-60f, 0f, 60f), new Vector3(40f, 14f, 70f), warehouse, roof, layer);
            Building(parent, "Warehouse B", new Vector3(140f, 0f, 40f), new Vector3(30f, 18f, 90f), warehouse, roof, layer);
            Building(parent, "Warehouse C", new Vector3(40f, 0f, -60f), new Vector3(100f, 12f, 30f), warehouse, roof, layer);

            // Quay and water to the north.
            Box(parent, "Water", new Vector3(40f, -0.3f, 170f), new Vector3(600f, 0.2f, 100f), water, layer);
            Box(parent, "Quay edge", new Vector3(40f, 0.25f, 120f), new Vector3(600f, 0.5f, 1f), Materials.Lit("Quay", new Color(0.5f, 0.5f, 0.5f)), layer);

            // Gantry crane by the quay.
            for (int s = -1; s <= 1; s += 2)
            {
                Box(parent, $"Crane leg {s} A", new Vector3(20f + s * 12f, 15f, 108f), new Vector3(1.2f, 30f, 1.2f), crane, layer);
                Box(parent, $"Crane leg {s} B", new Vector3(20f + s * 12f, 15f, 96f), new Vector3(1.2f, 30f, 1.2f), crane, layer);
            }
            Box(parent, "Crane beam", new Vector3(20f, 30.5f, 102f), new Vector3(26f, 1.5f, 14f), crane, layer);
            Box(parent, "Crane boom", new Vector3(20f, 31f, 125f), new Vector3(3f, 1.2f, 50f), crane, layer);

            // Static container stacks away from the course.
            var colors = new[] { new Color(0.16f, 0.32f, 0.6f), new Color(0.62f, 0.16f, 0.12f), new Color(0.2f, 0.45f, 0.25f), new Color(0.7f, 0.55f, 0.15f) };
            var rng = new System.Random(77);
            for (int row = 0; row < 4; row++)
            for (int i = 0; i < 8; i++)
            {
                int height = 1 + rng.Next(3);
                for (int h = 0; h < height; h++)
                {
                    var m = Materials.Lit("YardContainer" + ((row + i + h) % 4), colors[(row + i + h) % 4]);
                    Box(parent, $"Yard container {row},{i},{h}", new Vector3(-40f - row * 3f, 1.3f + h * 2.59f, -40f + i * 6.5f),
                        new Vector3(2.44f, 2.59f, 6.06f), m, layer);
                }
            }
            // Light poles.
            var pole = Materials.Lit("Pole", new Color(0.4f, 0.4f, 0.42f));
            var lamp = Materials.Emissive("PoleLamp", new Color(1f, 0.9f, 0.7f), 2f);
            foreach (var p in new[] { new Vector3(-20f, 0f, 20f), new Vector3(40f, 0f, 40f), new Vector3(100f, 0f, 90f), new Vector3(110f, 0f, -10f) })
            {
                Box(parent, "Pole", p + new Vector3(0f, 8f, 0f), new Vector3(0.4f, 16f, 0.4f), pole, layer);
                Box(parent, "Pole lamp", p + new Vector3(0f, 16.2f, 0f), new Vector3(1.6f, 0.3f, 0.8f), lamp, layer);
            }
        }

        private static void Building(Transform parent, string name, Vector3 basePos, Vector3 size, Material walls, Material roof, int layer)
        {
            Box(parent, name, basePos + new Vector3(0f, size.y * 0.5f, 0f), size, walls, layer);
            Box(parent, name + " roof", basePos + new Vector3(0f, size.y + 0.25f, 0f), new Vector3(size.x + 1f, 0.5f, size.z + 1f), roof, layer);
        }

        // ---------- helpers ----------

        private static bool NearCourse(TrackData course, Vector3 p, float clearance)
        {
            var q = new Vector2(p.x, p.z);
            if (q.magnitude < clearance) return true;
            int n = course.Gates.Count;
            for (int i = 0; i < n; i++)
            {
                var a3 = course.Gates[i].Position;
                var b3 = course.Gates[(i + 1) % n].Position;
                var a = new Vector2(a3.x, a3.z);
                var ab = new Vector2(b3.x, b3.z) - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                if (Vector2.Distance(q, a + ab * t) < clearance) return true;
            }
            return false;
        }

        private static Material Tinted(Material grid, string name, Color tint)
        {
            var m = Materials.Lit(name, tint);
            m.SetTexture("_BaseMap", grid.GetTexture("_BaseMap"));
            return m;
        }

        private static void Ground(Transform parent, int layer, Material mat, float size)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = mat;
            // 5 m grid cells.
            mat.SetTextureScale("_BaseMap", new Vector2(size / 5f, size / 5f));
            ground.layer = layer;
            ground.isStatic = true;
        }

        private static void Sun(Vector3 euler, float intensity, Color color)
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.intensity = intensity;
            light.color = color;
            light.transform.rotation = Quaternion.Euler(euler);
        }

        private static void Ambient(Color sky, Color equator, Color ground)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
        }

        public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.layer = layer;
            go.isStatic = true;
            return go;
        }
    }
}
