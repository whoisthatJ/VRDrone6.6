using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Menu (V7): the pilot's pit tent at dusk. A canopy over the player, a folding table with LiPo packs, a charger
    /// and tools, warm string and task lights, a race gate and a tree line in the field beyond, under the dusk HDRI.
    /// The world-space menu panel stays where it was (0, 1.45, 1.6) with nothing between it and the camera.
    /// </summary>
    public static partial class MenuArt
    {
        static partial void ApplyImpl(ArtContext ctx)
        {
            foreach (var root in ctx.Scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.name == "Floor" || r.name == "Floor Ring")
                    {
                        r.enabled = false;
                        r.sharedMaterial = MaterialLibrary.Get("M_Plastic_Grey"); // keep stock Lit out of the build
                    }

            var sunLight = ctx.Scene.GetRootGameObjects().Select(g => g.GetComponent<Light>()).FirstOrDefault(l => l != null && l.type == LightType.Directional);
            ctx.Sun = sunLight;
            var look = new SceneLook
            {
                Hdri = "qwantani_dusk_2_puresky",
                SkyExposure = 0.5f,
                AmbientIntensity = 1.4f,
                SunIntensity = 0.7f,
                SunTint = new Color(1f, 0.85f, 0.75f),
                ShadowStrength = 0.6f,
                FogDensity = 0.0015f,
                FogBrightness = 0.8f,
                PostExposure = 0.1f,
                Contrast = 10f,
                Saturation = 4f,
                BloomIntensity = 0.6f,
                BloomThreshold = 0.95f,
                Vignette = 0.22f,
            };
            ArtPass.ApplyLook(ctx, look);

            // The head camera used to clear to a flat colour; show the dusk sky instead.
            foreach (var root in ctx.Scene.GetRootGameObjects())
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                    cam.clearFlags = CameraClearFlags.Skybox;

            var group = ArtPass.Group(ctx, "Pit");
            var quad = ArtPass.MeshAsset("UnitQuadXZ", ProcMesh.UnitQuadXZ);
            var grass = MaterialLibrary.TiledVariant(MaterialLibrary.Get("M_Grass_Lawn"), $"{MaterialLibrary.LibraryDir}/Variants/M_Grass_Lawn_Menu.mat", 160f, 2f);
            ArtPass.AddMesh(group, "Field", quad, new[] { grass }, new Vector3(0f, -0.01f, 20f), Quaternion.identity, new Vector3(160f, 1f, 160f), lightmapped: false, castShadows: false);
            var apron = ProcMesh.Box(new Vector3(9f, 0.02f, 7f), Vector3.zero, true, "Pit Apron");
            ArtPass.AddMesh(group, "Apron", apron, ArtPass.Mats("M_Asphalt_PitLane"), new Vector3(0.5f, 0f, 0.8f), Quaternion.identity, new Vector3(9f, 0.02f, 7f), lightmapped: true, castShadows: false);

            // Canopy over the player; the panel sits just inside its front edge.
            var canopy = ArtPass.MeshAsset("Canopy3m", () => ProcMesh.Canopy());
            ArtPass.AddMesh(group, "Canopy", canopy, ArtPass.Mats("M_Aluminium_Brushed", "M_Fabric_CanvasRed"), new Vector3(0.3f, 0f, 0.4f), Quaternion.identity, Vector3.one, lightmapped: false);

            // Folding table to the right with batteries, a charger and tools.
            var table = ProcMesh.Combine("Table", new List<(Mesh, Matrix4x4, int)>
            {
                ProcMesh.Part(new Vector3(0f, 0.74f, 0f), new Vector3(1.6f, 0.03f, 0.7f), Quaternion.identity, 0),
                ProcMesh.Part(new Vector3(-0.72f, 0.36f, -0.28f), new Vector3(0.03f, 0.72f, 0.03f), Quaternion.identity, 1),
                ProcMesh.Part(new Vector3(0.72f, 0.36f, -0.28f), new Vector3(0.03f, 0.72f, 0.03f), Quaternion.identity, 1),
                ProcMesh.Part(new Vector3(-0.72f, 0.36f, 0.28f), new Vector3(0.03f, 0.72f, 0.03f), Quaternion.identity, 1),
                ProcMesh.Part(new Vector3(0.72f, 0.36f, 0.28f), new Vector3(0.03f, 0.72f, 0.03f), Quaternion.identity, 1),
            });
            var tablePos = new Vector3(1.55f, 0f, 0.9f);
            ArtPass.AddMesh(group, "Table", table, ArtPass.Mats("M_Wood_Plywood", "M_Aluminium_Brushed"), tablePos, Quaternion.Euler(0f, -70f, 0f), Vector3.one);
            var lipo = ProcMesh.Box(new Vector3(0.14f, 0.045f, 0.045f), Vector3.zero, false, "Lipo");
            for (int i = 0; i < 5; i++)
                ArtPass.AddMesh(group, "LiPo " + i, lipo, ArtPass.Mats("M_Lipo"), tablePos + new Vector3(-0.1f + i * 0.055f, 0.778f, -0.25f + (i % 2) * 0.03f), Quaternion.Euler(0f, 20f + i * 4f, 0f), new Vector3(0.14f, 0.045f, 0.045f));
            var charger = ProcMesh.Combine("Charger", new List<(Mesh, Matrix4x4, int)>
            {
                ProcMesh.Part(Vector3.zero, new Vector3(0.18f, 0.07f, 0.14f), Quaternion.identity, 0),
                ProcMesh.Part(new Vector3(0f, 0.01f, -0.071f), new Vector3(0.1f, 0.035f, 0.004f), Quaternion.identity, 1),
            });
            ArtPass.AddMesh(group, "Charger", charger, ArtPass.Mats("M_Plastic_Black", "M_LED_Blue"), tablePos + new Vector3(0.05f, 0.79f, 0.15f), Quaternion.Euler(0f, -60f, 0f), Vector3.one);
            ArtPass.AddModel(group, "metal_toolbox", tablePos + new Vector3(0.4f, 0.755f, 0.1f), -80f);
            ArtPass.AddModel(group, "plastic_monobloc_chair_01", new Vector3(-1.3f, 0f, 0.6f), 140f);
            ArtPass.AddModel(group, "plastic_crate_02", new Vector3(-1.4f, 0f, 1.8f), 30f);

            // Warm practical light: string lights along the canopy and a task lamp over the table.
            var bulb = ArtPass.MeshAsset("Bulb", () => ProcMesh.Box(new Vector3(0.05f, 0.07f, 0.05f), Vector3.zero, false));
            var strings = ArtPass.Group(ctx, "String Lights");
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                var p = new Vector3(-1.2f + t * 3f, 2.05f - Mathf.Sin(t * Mathf.PI) * 0.12f, 1.92f);
                // Box meshes live in the unit cube: the transform scale is the real size.
                ArtPass.AddMesh(strings, "Bulb " + i, bulb, ArtPass.Mats("M_Lamp_Warm"), p, Quaternion.identity, new Vector3(0.05f, 0.07f, 0.05f), castShadows: false);
            }
            Point(ctx, "String Light L", new Vector3(-0.6f, 1.95f, 1.8f), 2.2f, 5f);
            Point(ctx, "String Light R", new Vector3(1.2f, 1.95f, 1.8f), 2.2f, 5f);
            Point(ctx, "Task Lamp", tablePos + new Vector3(0f, 1.6f, 0f), 3.5f, 4f);

            // The field beyond: a race gate, flags, cones and the tree line.
            var gatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Gates/Gate_Large.prefab");
            if (gatePrefab != null)
            {
                var gate = (GameObject)PrefabUtility.InstantiatePrefab(gatePrefab, group);
                PrefabUtility.UnpackPrefabInstance(gate, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var mb in gate.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
                foreach (var c in gate.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                gate.transform.SetPositionAndRotation(new Vector3(-3f, 0f, 16f), Quaternion.Euler(0f, 18f, 0f));
                gate.name = "Gate (decor)";
            }
            var flag = ArtPass.MeshAsset("FeatherFlag", () => ProcMesh.FeatherFlag());
            for (int i = 0; i < 3; i++)
                ArtPass.AddMesh(group, "Flag " + i, flag, ArtPass.Mats("M_Steel_Brushed", i == 1 ? "M_Fabric_GateBlue" : "M_Fabric_GateOrange"), new Vector3(4f + i * 2.5f, 0f, 9f + i), Quaternion.Euler(0f, 200f + i * 10f, 0f), Vector3.one);
            var cone = ArtPass.MeshAsset("TrafficCone", () => ProcMesh.Cone());
            for (int i = 0; i < 5; i++)
                ArtPass.AddMesh(group, "Cone " + i, cone, ArtPass.Mats("M_Plastic_ConeOrange", "M_Reflective_White"), new Vector3(-6f + i * 3f, 0f, 7f), Quaternion.Euler(0f, i * 31f, 0f), Vector3.one);

            var trees = ArtPass.Group(ctx, "Tree Line");
            var treeMat = ImpostorBaker.Bake();
            var card = ImpostorBaker.CardSize;
            var cardMesh = ArtPass.MeshAsset("TreeImpostorCard", () => ProcMesh.CrossedQuads(ImpostorBaker.Views, card.x, card.y));
            var rng = new System.Random(9);
            var parts = new List<(Mesh, Matrix4x4, int)>();
            for (int i = 0; i < 70; i++)
            {
                float ang = Mathf.Lerp(-80f, 80f, (float)rng.NextDouble()) * Mathf.Deg2Rad;
                float dist = 45f + (float)rng.NextDouble() * 35f;
                var p = new Vector3(Mathf.Sin(ang) * dist, -0.2f, Mathf.Cos(ang) * dist);
                parts.Add((cardMesh, Matrix4x4.TRS(p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * (0.8f + (float)rng.NextDouble() * 0.5f)), 0));
            }
            ArtPass.AddMesh(trees, "Trees", ProcMesh.Combine("Menu Trees", parts), new[] { treeMat }, Vector3.zero, Quaternion.identity, Vector3.one);

            // Light probes and a reflection probe for the tent area.
            var probes = new GameObject("Light Probes");
            probes.transform.SetParent(ctx.Dressing, false);
            var lpg = probes.AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            for (int x = -3; x <= 3; x++)
                for (int z = -2; z <= 4; z++)
                    foreach (float y in new[] { 0.3f, 1.4f, 2.6f })
                        positions.Add(new Vector3(x * 1.5f, y, z * 1.5f));
            lpg.probePositions = positions.ToArray();
            ArtPass.AddProbe(ctx, "Tent Probe", new Vector3(0.3f, 1.4f, 0.6f), new Vector3(8f, 4f, 7f), true, 128);
            ctx.Log.Append(" pit tent;");
        }

        private static void Point(ArtContext ctx, string name, Vector3 p, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(ArtPass.Group(ctx, "String Lights"), false);
            go.transform.position = p;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.72f, 0.42f);
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Mixed;
        }
    }
}
