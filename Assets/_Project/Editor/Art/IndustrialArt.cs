using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Track_Industrial (V6): an abandoned factory yard, overcast. Brick warehouses with rusty corrugated roofs,
    /// shutter doors and grimy windows; painted containers and a yellow gantry crane; cracked asphalt with oil,
    /// crack and tyre decals and glossy puddles; Jersey barriers, pallets, barrels and tyres; pole lamps with heads.
    /// The builder's water sat under the ground plane: the ground visual now stops at the quay and real water
    /// continues to the horizon (the original ground collider is unchanged).
    /// </summary>
    public static partial class IndustrialArt
    {
        private const float QuayZ = 120f;

        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "Warehouse", "M_Brick_Factory" },
            { "WarehouseRoof", "M_Iron_CorrugatedRusty" },
            { "Quay", "M_Concrete_Wall" },
            { "Crane", "M_Crane_Yellow" },
            { "M_Steel_PaintedYellow", "M_Crane_Yellow" }, // first art-pass revision
            { "YardContainer0", "M_Container_Blue" },
            { "YardContainer1", "M_Container_Red" },
            { "YardContainer2", "M_Container_Green" },
            { "YardContainer3", "M_Container_Yellow" },
            { "Pole", "M_Steel_Brushed" },
            { "PoleLamp", "" },
            { "Water", "" },
            { "GroundAsphalt", "" },
        };

        static partial void ApplyImpl(ArtContext ctx)
        {
            ArtPass.RemapEnvironment(ctx, Map);
            var look = new SceneLook
            {
                Hdri = "kloofendal_overcast_puresky",
                SkyExposure = 0.9f,
                AmbientIntensity = 1.15f,
                SunIntensity = 1.1f,
                SunTint = new Color(0.95f, 0.97f, 1f),
                ShadowStrength = 0.45f,
                FogDensity = 0.0022f,
                FogBrightness = 0.9f,
                PostExposure = 0.1f,
                Contrast = 6f,
                Saturation = -12f,
                Temperature = -5f,
                BloomIntensity = 0.2f,
                Vignette = 0.2f,
            };
            ArtPass.ApplyLook(ctx, look);

            GroundAndWater(ctx);
            Warehouses(ctx);
            Lamps(ctx);
            Dressing(ctx);
            Decals(ctx);
            ArtPass.AddProbe(ctx, "Yard Probe", new Vector3(40f, 10f, 40f), new Vector3(220f, 40f, 220f), false, 256);
            ArtPass.AddProbe(ctx, "Corridor Probe", new Vector3(0f, 4f, 48f), new Vector3(30f, 10f, 40f), true, 256);
        }

        private static void GroundAndWater(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "Ground");
            var asphalt = ProcMesh.Box(new Vector3(1200f, 0.01f, 600f + QuayZ), Vector3.zero, false, "Asphalt Apron");
            var apron = ArtPass.AddMesh(group, "Asphalt", asphalt, ArtPass.Mats("M_Asphalt_Old"), new Vector3(0f, -0.005f, (QuayZ - 600f) / 2f),
                Quaternion.identity, new Vector3(1200f, 0.01f, 600f + QuayZ), lightmapped: false, castShadows: false);
            apron.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;

            var quad = ArtPass.MeshAsset("UnitQuadXZ", ProcMesh.UnitQuadXZ);
            var water = ArtPass.AddMesh(group, "Water", quad, ArtPass.Mats("M_Water"), new Vector3(0f, -0.12f, QuayZ + 450f), Quaternion.identity, new Vector3(1600f, 1f, 900f), castShadows: false);
            var waterMat = MaterialLibrary.Get("M_Water");
            waterMat.SetTextureScale("_BaseMap", new Vector2(1600f / 6f, 900f / 6f));
            EditorUtility.SetDirty(waterMat);
            _ = water;
        }

        private static void Warehouses(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "Warehouse Details");
            int doors = 0;
            foreach (var r in ctx.Environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.name != "M_Brick_Factory") continue;
                var t = r.transform;
                var s = t.lossyScale;
                var b = r.bounds;
                // A roller shutter and a strip of windows on the two long faces.
                bool alongX = s.x >= s.z;
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 n = alongX ? new Vector3(0f, 0f, side) : new Vector3(side, 0f, 0f);
                    Vector3 face = b.center + Vector3.Scale(n, b.extents) + n * 0.03f;
                    Vector3 along = alongX ? Vector3.right : Vector3.forward;
                    float length = alongX ? s.x : s.z;
                    var rot = Quaternion.LookRotation(n, Vector3.up);
                    var parts = new List<(Mesh, Matrix4x4, int)>();
                    float doorW = Mathf.Min(6f, length * 0.2f), doorH = Mathf.Min(5f, s.y * 0.5f);
                    Vector3 doorPos = new Vector3(face.x, b.min.y + doorH / 2f, face.z) + along * length * 0.15f;
                    parts.Add((ProcMesh.Box(new Vector3(doorW, doorH, 0.06f)), Matrix4x4.TRS(doorPos, rot, new Vector3(doorW, doorH, 0.06f)), 0));
                    for (float x = -length / 2f + 3f; x < length / 2f - 3f; x += 5f)
                    {
                        Vector3 wp = new Vector3(face.x, b.max.y - 2.2f, face.z) + along * x;
                        parts.Add((ProcMesh.Box(new Vector3(3.2f, 1.4f, 0.05f)), Matrix4x4.TRS(wp, rot, new Vector3(3.2f, 1.4f, 0.05f)), 1));
                    }
                    var mesh = ProcMesh.Combine("Facade", parts);
                    ArtPass.AddMesh(group, $"{t.name} facade {side}", mesh, ArtPass.Mats("M_Metal_ShutterRusty", "M_Glass_Dirty"), Vector3.zero, Quaternion.identity, Vector3.one, castShadows: false);
                    ArtPass.AddModel(group, "security_light", new Vector3(doorPos.x, doorPos.y + doorH / 2f + 0.6f, doorPos.z) + n * 0.1f, Quaternion.LookRotation(n).eulerAngles.y);
                    doors++;
                }
            }
            ctx.Log.Append($" facades {doors};");
        }

        private static void Lamps(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "Pole Lamps");
            var head = ArtPass.MeshAsset("PoleLampHead", () => ProcMesh.Combine("LampHead", new List<(Mesh, Matrix4x4, int)>
            {
                ProcMesh.Part(new Vector3(0f, 0f, 0.45f), new Vector3(0.45f, 0.14f, 0.9f), Quaternion.identity, 0),
                ProcMesh.Part(new Vector3(0f, -0.075f, 0.55f), new Vector3(0.36f, 0.02f, 0.6f), Quaternion.identity, 1),
            }));
            foreach (var lamp in ctx.Environment.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Pole lamp")))
            {
                var top = lamp.position + Vector3.up * lamp.lossyScale.y / 2f;
                ArtPass.AddMesh(group, lamp.name + " head", head, ArtPass.Mats("M_Steel_Plate", "M_Glass_Dirty"), top, Quaternion.Euler(0f, 45f, 0f), Vector3.one, castShadows: false);
            }
        }

        private static void Dressing(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "Yard");
            var barrier = ArtPass.MeshAsset("JerseyBarrier", () => ProcMesh.JerseyBarrier());
            var pallet = ArtPass.MeshAsset("Pallet", ProcMesh.Pallet);
            // Barriers along the quay edge (gap where the crane stands).
            for (float x = -60f; x <= 110f; x += 3.1f)
            {
                if (x > 2f && x < 38f) continue;
                ArtPass.AddMesh(group, "Barrier", barrier, ArtPass.Mats("M_Concrete_Wall"), new Vector3(x, 0f, QuayZ - 2.5f), Quaternion.identity, Vector3.one, solid: true);
            }
            // Barrier chicane near the start and a line along warehouse C.
            for (int i = 0; i < 5; i++)
                ArtPass.AddMesh(group, "Barrier", barrier, ArtPass.Mats("M_Concrete_Wall"), new Vector3(-6f - i * 3.1f, 0f, -12f), Quaternion.identity, Vector3.one, solid: true);
            // Pallet stacks and barrels by the warehouses.
            Vector3[] stacks = { new Vector3(-36f, 0f, 30f), new Vector3(-36f, 0f, 34f), new Vector3(120f, 0f, 20f), new Vector3(68f, 0f, -40f) };
            foreach (var p in stacks)
                for (int i = 0; i < 4; i++)
                    ArtPass.AddMesh(group, "Pallet", pallet, ArtPass.Mats("M_Wood_Planks"), p + Vector3.up * i * 0.144f, Quaternion.Euler(0f, i % 2 * 4f, 0f), Vector3.one, solid: i == 0);
            Vector3[] barrels = { new Vector3(-34f, 0f, 38f), new Vector3(-33.3f, 0f, 38.6f), new Vector3(-34.2f, 0f, 39.3f), new Vector3(118f, 0f, 26f), new Vector3(118.7f, 0f, 26.6f) };
            foreach (var p in barrels) ArtPass.AddModel(group, "barrel_03", p, Random(p) * 360f, solid: true);
            Vector3[] tyres = { new Vector3(30f, 0f, 20f), new Vector3(31f, 0.25f, 20.4f), new Vector3(-30f, 0f, -8f), new Vector3(100f, 0f, 70f) };
            foreach (var p in tyres) ArtPass.AddModel(group, "old_tyre", p, Random(p) * 360f);
            ArtPass.AddModel(group, "utility_box_01", new Vector3(-20f, 0f, 22f), 90f, solid: true);
            ArtPass.AddModel(group, "metal_trash_can", new Vector3(36f, 0f, -6f), 20f, solid: true);
        }

        private static float Random(Vector3 p) => Mathf.Repeat(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f) * 43758.5453f, 1f);

        private static void Decals(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "Decals");
            var oil = DecalMaterial("M_Decal_Oil", "DecalOil", null);
            var crack = DecalMaterial("M_Decal_Crack", "DecalCrack", "DecalCrack");
            var tyre = DecalMaterial("M_Decal_Tyre", "DecalTyre", null);
            var rng = new System.Random(77);
            Vector3 Around(Vector3 c, float r) => c + new Vector3(((float)rng.NextDouble() - 0.5f) * 2f * r, 0f, ((float)rng.NextDouble() - 0.5f) * 2f * r);
            Vector3[] hotspots = { new Vector3(0f, 0f, 8f), new Vector3(0f, 0f, 48f), new Vector3(30f, 0f, 75f), new Vector3(70f, 0f, 70f), new Vector3(86f, 0f, 35f), new Vector3(60f, 0f, 5f), new Vector3(25f, 0f, -10f) };
            int n = 0;
            foreach (var h in hotspots)
            {
                for (int i = 0; i < 3; i++) Projector(group, "Crack", crack, Around(h, 9f), 4f + (float)rng.NextDouble() * 4f, (float)rng.NextDouble() * 360f);
                Projector(group, "Oil", oil, Around(h, 7f), 1.5f + (float)rng.NextDouble() * 2.5f, (float)rng.NextDouble() * 360f);
                Projector(group, "Tyre marks", tyre, Around(h, 6f), 6f, (float)rng.NextDouble() * 360f);
                n += 5;
            }
            // Puddles: glossy transparent patches that pick up the overcast sky.
            var quad = ArtPass.MeshAsset("UnitQuadXZ", ProcMesh.UnitQuadXZ);
            var puddle = MaterialLibrary.Get("M_Puddle");
            foreach (var h in hotspots.Take(5))
            {
                var p = Around(h, 6f);
                float s = 2f + (float)rng.NextDouble() * 3f;
                var go = ArtPass.AddMesh(group, "Puddle", quad, new[] { puddle }, p + Vector3.up * 0.006f, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), new Vector3(s, 1f, s * 0.7f), castShadows: false);
                go.GetComponent<MeshRenderer>().receiveShadows = true;
                n++;
            }
            ctx.Log.Append($" decals {n};");
        }

        private static void Projector(Transform parent, string name, Material m, Vector3 p, float size, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p + Vector3.up * 0.5f, Quaternion.Euler(90f, yaw, 0f));
            var d = go.AddComponent<DecalProjector>();
            d.material = m;
            d.size = new Vector3(size, size, 1.2f);
            d.pivot = new Vector3(0f, 0f, 0.6f);
            d.drawDistance = 120f;
            d.fadeFactor = 1f;
        }

        private static Material DecalMaterial(string name, string albedoSet, string normalSet)
        {
            string path = $"{MaterialLibrary.LibraryDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Shader Graphs/Decal"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("Base_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtProcedural.File(albedoSet, "Albedo")));
            if (normalSet != null)
            {
                m.SetTexture("Normal_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtProcedural.File(normalSet, "Normal")));
                m.SetFloat("Normal_Blend", 0.8f);
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
