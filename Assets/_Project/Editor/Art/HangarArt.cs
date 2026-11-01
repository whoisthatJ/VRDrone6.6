using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Track_Hangar (V5): an aircraft hangar at night. Polished concrete floor with painted safety lines, corrugated
    /// steel walls and roof, I-beam columns and deep lattice trusses (with matching colliders), LED high-bay fixtures
    /// with real spot lights, night clerestory windows, workshop dressing along the walls and box-projected
    /// reflection probes. The sun is off; lamps and emissive lenses light the hall (baked indirect + realtime direct).
    /// </summary>
    public static partial class HangarArt
    {
        private const float HallX = 48f, HallZ = 24f, HallH = 16f;

        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "HangarWall", "M_Steel_Corrugated" },
            { "HangarRoof", "M_Steel_CorrugatedDark" },
            { "HangarDoor", "M_Metal_ShutterPainted" },
            { "HangarSteel", "" },  // columns and trusses get real profiles below
            { "HangarLamp", "" },   // replaced by high-bay fixtures
            { "HangarStripe", "" }, // replaced by painted floor lines
        };

        static partial void ApplyImpl(ArtContext ctx)
        {
            ArtPass.RemapEnvironment(ctx, Map);
            ArtPass.TileGround(ctx, "M_Concrete_HangarFloor");
            var look = new SceneLook
            {
                Hdri = "qwantani_night_puresky",
                SkyExposure = 0.6f,
                AmbientIntensity = 0.5f,
                ReflectionIntensity = 0.6f,
                SunEnabled = false,
                Fog = true,
                FogMode = FogMode.Exponential,
                FogDensity = 0.004f,
                FogColorOverride = new Color(0.06f, 0.065f, 0.075f, 1f),
                PostExposure = 0.35f,
                Contrast = 12f,
                Saturation = -4f,
                Temperature = -4f,
                BloomIntensity = 0.55f,
                BloomThreshold = 1.1f,
            };
            ArtPass.ApplyLook(ctx, look);

            Structure(ctx);
            Lamps(ctx);
            Windows(ctx);
            FloorPaint(ctx);
            Dressing(ctx);
            ArtPass.AddProbe(ctx, "Probe West", new Vector3(-32f, 7f, 0f), new Vector3(32f, 16f, 48f), true, 256);
            ArtPass.AddProbe(ctx, "Probe Centre", new Vector3(0f, 7f, 0f), new Vector3(32f, 16f, 48f), true, 256);
            ArtPass.AddProbe(ctx, "Probe East", new Vector3(32f, 7f, 0f), new Vector3(32f, 16f, 48f), true, 256);
        }

        private static IEnumerable<Transform> Named(ArtContext ctx, string prefix) =>
            ctx.Environment.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(prefix));

        private static void Structure(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "Structure");
            var beam = ArtPass.MeshAsset("HangarIBeam", () => ProcMesh.IBeam(HallH, 0.45f, 0.8f, 0.03f, 0.04f));
            foreach (var col in Named(ctx, "Column"))
            {
                var p = col.position;
                ArtPass.AddMesh(group, col.name + " beam", beam, ArtPass.Mats("M_Steel_Truss"), new Vector3(p.x, HallH / 2, p.z), Quaternion.identity, Vector3.one, lightmapped: true);
            }

            const float depth = 1.8f;
            var truss = ArtPass.MeshAsset("HangarTruss", () => ProcMesh.Truss(HallZ * 2f - 0.6f, depth, 0.16f, 0.07f, 1.6f));
            foreach (var t in Named(ctx, "Truss"))
            {
                var p = t.position;
                float top = p.y + t.lossyScale.y / 2f;
                var centre = new Vector3(p.x, top - depth / 2f, p.z);
                var go = ArtPass.AddMesh(group, t.name + " lattice", truss, ArtPass.Mats("M_Steel_Truss"), centre, Quaternion.identity, Vector3.one, lightmapped: true);
                // The lattice hangs lower than the builder's 0.6 m box: give it a matching collider so it is solid.
                var col = new GameObject(t.name + " lattice collider") { layer = ArtPass.TrackGeometryLayer };
                col.transform.SetParent(group, false);
                col.transform.position = centre;
                col.AddComponent<BoxCollider>().size = new Vector3(0.3f, depth, HallZ * 2f - 0.6f);
                _ = go;
            }
        }

        private static void Lamps(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "High Bays");
            var housing = ArtPass.MeshAsset("HighBayHousing", () => ProcMesh.Combine("HighBay", new List<(Mesh, Matrix4x4, int)>
            {
                ProcMesh.Part(new Vector3(0f, 0.12f, 0f), new Vector3(0.62f, 0.16f, 0.62f), Quaternion.identity, 0),
                ProcMesh.Part(new Vector3(0f, 0.25f, 0f), new Vector3(0.22f, 0.1f, 0.22f), Quaternion.identity, 0),
                ProcMesh.Part(new Vector3(0f, 0.02f, 0f), new Vector3(0.56f, 0.03f, 0.56f), Quaternion.identity, 1),
            }));
            int index = 0;
            foreach (var lamp in Named(ctx, "Lamp").OrderBy(l => l.position.x).ThenBy(l => l.position.z))
            {
                var p = lamp.position;
                var pos = new Vector3(p.x, 13.4f, p.z);
                ArtPass.AddMesh(group, lamp.name + " fixture", housing, ArtPass.Mats("M_Plastic_Grey", "M_Lamp_Emitter"), pos, Quaternion.identity, Vector3.one, lightmapped: false, castShadows: false);
                var lgo = new GameObject(lamp.name + " light");
                lgo.transform.SetParent(group, false);
                lgo.transform.SetPositionAndRotation(pos - Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                var light = lgo.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = new Color(1f, 0.95f, 0.88f);
                light.intensity = 260f;
                light.range = 30f;
                light.spotAngle = 125f;
                light.innerSpotAngle = 70f;
                light.lightmapBakeType = LightmapBakeType.Mixed;
                bool shadows = Mathf.Abs(p.x) < 25f && index % 2 == 0; // a few shadowed lamps around the course centre
                light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                light.shadowStrength = 0.75f;
                light.bounceIntensity = 1f;
                index++;
            }
            ctx.Log.Append($" high-bays {index};");
        }

        private static void Windows(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "Windows");
            var mats = ArtPass.Mats("M_Glass_Night", "M_Steel_Plate");
            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * (HallZ - 0.02f);
                var parts = new List<(Mesh, Matrix4x4, int)>();
                for (float x = -HallX + 4f; x < HallX - 3f; x += 6f)
                {
                    parts.Add(ProcMesh.Part(new Vector3(x + 2f, 12.6f, z), new Vector3(4.4f, 1.6f, 0.04f), Quaternion.identity, 0));
                    parts.Add(ProcMesh.Part(new Vector3(x + 2f, 11.75f, z - side * 0.03f), new Vector3(4.6f, 0.1f, 0.08f), Quaternion.identity, 1));
                    parts.Add(ProcMesh.Part(new Vector3(x + 2f, 13.45f, z - side * 0.03f), new Vector3(4.6f, 0.1f, 0.08f), Quaternion.identity, 1));
                }
                ArtPass.AddMesh(group, side < 0 ? "Windows S" : "Windows N", ProcMesh.Combine("Windows", parts), mats, Vector3.zero, Quaternion.identity, Vector3.one, lightmapped: false, castShadows: false);
            }
        }

        private static void FloorPaint(ArtContext ctx)
        {
            var yellow = new List<(Mesh, Matrix4x4, int)>();
            // Walkways along both long walls, a pilot box around the spawn, a hatched no-fly zone at the door.
            foreach (float z in new[] { -HallZ + 2.2f, HallZ - 2.2f })
                ProcMesh.Strip(yellow, new Vector3(-HallX + 1.5f, 0f, z), new Vector3(HallX - 1.5f, 0f, z), 0.12f);
            Vector3 c = new Vector3(-10f, 0f, -14f);
            float hx = 2.2f, hz = 1.8f;
            ProcMesh.Strip(yellow, c + new Vector3(-hx, 0, -hz), c + new Vector3(hx, 0, -hz), 0.1f);
            ProcMesh.Strip(yellow, c + new Vector3(hx, 0, -hz), c + new Vector3(hx, 0, hz), 0.1f);
            ProcMesh.Strip(yellow, c + new Vector3(hx, 0, hz), c + new Vector3(-hx, 0, hz), 0.1f);
            ProcMesh.Strip(yellow, c + new Vector3(-hx, 0, hz), c + new Vector3(-hx, 0, -hz), 0.1f);
            var mesh = ProcMesh.Combine("Floor Paint", yellow);
            ArtPass.AddMesh(ArtPass.Group(ctx, "Floor"), "Floor Paint", mesh, ArtPass.Mats("M_Paint_Yellow"), Vector3.up * 0.002f, Quaternion.identity, Vector3.one, castShadows: false);

            var hatch = new List<(Mesh, Matrix4x4, int)> { ProcMesh.Part(new Vector3(HallX - 3f, 0.002f, 0f), new Vector3(5f, 0.004f, 30f), Quaternion.identity) };
            ArtPass.AddMesh(ArtPass.Group(ctx, "Floor"), "Door Zone", ProcMesh.Combine("Door Zone", hatch), ArtPass.Mats("M_SafetyStripes"), Vector3.zero, Quaternion.identity, Vector3.one, castShadows: false);

            var mat = ProcMesh.Combine("Pilot Mat", new List<(Mesh, Matrix4x4, int)> { ProcMesh.Part(c + Vector3.up * 0.008f, new Vector3(3.6f, 0.016f, 2.8f), Quaternion.identity) });
            ArtPass.AddMesh(ArtPass.Group(ctx, "Floor"), "Pilot Mat", mat, ArtPass.Mats("M_Rubber_Mat"), Vector3.zero, Quaternion.identity, Vector3.one, castShadows: false);
        }

        private static void Dressing(ArtContext ctx)
        {
            var group = ArtPass.Group(ctx, "Workshop");
            float zn = HallZ - 1.2f, zs = -HallZ + 1.2f;
            ArtPass.AddModel(group, "metal_tool_chest", new Vector3(-30f, 0f, zn), 180f, solid: true);
            ArtPass.AddModel(group, "metal_tool_chest", new Vector3(-28.6f, 0f, zn), 180f, solid: true);
            ArtPass.AddModel(group, "wooden_ladder", new Vector3(-24f, 0f, zn + 0.4f), 180f, solid: true);
            ArtPass.AddModel(group, "plastic_crate_02", new Vector3(-19f, 0f, zn), 170f);
            ArtPass.AddModel(group, "plastic_crate_02", new Vector3(-18.4f, 0f, zn - 0.2f), 195f);
            ArtPass.AddModel(group, "metal_trash_can", new Vector3(10f, 0f, zn), 0f, solid: true);
            ArtPass.AddModel(group, "barrel_03", new Vector3(16f, 0f, zn), 0f, solid: true);
            ArtPass.AddModel(group, "barrel_03", new Vector3(16.8f, 0f, zn - 0.3f), 40f, solid: true);
            ArtPass.AddModel(group, "metal_toolbox", new Vector3(-12.5f, 0f, -12.6f), 20f);
            ArtPass.AddModel(group, "plastic_monobloc_chair_01", new Vector3(-11.6f, 0f, -15.2f), 70f);
            ArtPass.AddModel(group, "WetFloorSign_01", new Vector3(30f, 0f, zs + 1f), 200f);
            ArtPass.AddModel(group, "utility_box_01", new Vector3(-HallX + 0.4f, 1.2f, 6f), 90f);
            var pallet = ArtPass.MeshAsset("Pallet", ProcMesh.Pallet);
            for (int i = 0; i < 3; i++)
                ArtPass.AddMesh(group, "Pallet " + i, pallet, ArtPass.Mats("M_Wood_Planks"), new Vector3(34f, i * 0.144f, zs + 0.6f), Quaternion.Euler(0f, 90f, 0f), Vector3.one, solid: i == 0);
        }
    }
}
