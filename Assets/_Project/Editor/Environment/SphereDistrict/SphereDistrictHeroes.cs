using System.Collections.Generic;
using GateRush.Core;
using GateRush.EditorTools.Art;
using GateRush.Track.Geo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.SphereDistrict
{
    /// <summary>
    /// Builds the hero buildings of the Sphere District (E2) into a scene's art dressing from the imported OSM data:
    /// every footprint whose override marks it as a hero goes to its generator (sphere, pavilion, pod, canopy, arena,
    /// hall, hotel, monument). Mesh assets are written to Art/Meshes (SD_*), colliders on TrackGeometry.
    /// </summary>
    public static class SphereDistrictHeroes
    {
        public const string GroupName = "Sphere District Heroes";

        public static string Build(ArtContext ctx)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GeoDataAsset>(OsmImporter.DataPath);
            if (asset == null) return " no geo data (run Import OSM);";
            var group = ArtPass.Group(ctx, GroupName);
            var counts = new Dictionary<string, int>();
            foreach (var b in asset.Data.Buildings)
            {
                if (!b.Hero) continue;
                counts.TryGetValue(b.HeroRole, out int n);
                counts[b.HeroRole] = n + 1;
                string name = $"SD_{b.HeroRole}_{b.Id}";
                switch (b.HeroRole)
                {
                    case "pavilion":
                        Solid(Add(group, name, PillowPavilionGenerator.Build(b, name), "M_Expo_CurtainWall", "M_Expo_Roof", "M_Expo_Skylight", "M_Glass_Dirty"));
                        break;
                    case "pod":
                        Solid(Add(group, name, PodGenerator.Build(b, name, 7f, 4f, 0.55f), "M_Expo_CurtainWall", "M_Expo_Roof", "M_Glass_Dirty"));
                        break;
                    case "sphere":
                        // The podium footprint round the sphere (the sphere itself is built once, below).
                        Solid(Add(group, name, PodGenerator.Build(b, name, 6f, 1.2f, 0.12f), "M_Expo_CurtainWall", "M_Expo_Roof", "M_Glass_Dirty"));
                        break;
                    case "canopy":
                    {
                        var mesh = CanopyGenerator.Build(b, name, out var columns);
                        Solid(Add(group, name, mesh, "M_Expo_Skylight", "M_Expo_Steel", "M_Paint_White"));
                        var parts = new List<(Mesh, Matrix4x4, int)>();
                        foreach (var c in columns)
                            parts.Add(ProcMesh.Part(new Vector3(c.x, CanopyGenerator.Height * 0.5f, c.y), new Vector3(0.45f, CanopyGenerator.Height, 0.45f), Quaternion.identity));
                        if (parts.Count > 0) Solid(Add(group, name + "_Columns", ProcMesh.Combine(name + "_Columns", parts), "M_Expo_Steel"));
                        break;
                    }
                    case "arena":
                        Solid(Add(group, name, LandmarkGenerator.Arena(b, name), "M_Expo_CurtainWall", "M_Expo_Roof", "M_Glass_Dirty"));
                        break;
                    case "hall":
                        Solid(Add(group, name, LandmarkGenerator.Extruded(b, name, 15f, 4f), "M_Expo_Cladding", "M_Expo_Roof", "M_Glass_Dirty"));
                        break;
                    case "hotel":
                        Solid(Add(group, name, LandmarkGenerator.Extruded(b, name, 24f, 2f), "M_Expo_CurtainWall", "M_Expo_Roof", "M_Glass_Dirty"));
                        break;
                    case "monument":
                    {
                        var c = PolyMesh.Centroid(b.Footprint);
                        Solid(Add(group, name, LandmarkGenerator.Needle(c, b.Height > 0f ? b.Height : 46f, name), "M_Paint_White", "M_Garage_Gold"));
                        break;
                    }
                }
            }
            BuildSphere(group);
            var sb = new System.Text.StringBuilder(" heroes:");
            foreach (var kv in counts) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value);
            return sb.Append(';').ToString();
        }

        private static void BuildSphere(Transform group)
        {
            var sphere = Add(group, "SD_Sphere", SphereGenerator.Build("SD_Sphere"),
                "M_Sphere_Clear", "M_Sphere_Frit", "M_Sphere_PV", "M_Glass_Night", "M_Expo_Tower", "M_Sphere_LED", "M_Expo_Floor");
            var col = new GameObject("Sphere Collider") { layer = ArtPass.TrackGeometryLayer };
            col.transform.SetParent(sphere.transform, false);
            var sc = col.AddComponent<SphereCollider>();
            sc.center = SphereGenerator.Centre;
            sc.radius = SphereGenerator.Radius;
            Solid(Add(group, "SD_SphereBase", SphereGenerator.BuildBase("SD_SphereBase"), "M_Expo_CurtainWall", "M_Expo_Roof"));

            // Two wind turbines in the scoop, facing out of it (south-west), rotors spinning.
            var (body, rotor) = SphereGenerator.Turbine();
            var bodyAsset = ArtPass.MeshAsset("SD_TurbineBody", () => body);
            var rotorAsset = ArtPass.MeshAsset("SD_TurbineRotor", () => rotor);
            var axis = SphereGenerator.ScoopAxis;
            var flat = new Vector3(axis.x, 0f, axis.z).normalized;
            var right = Vector3.Cross(Vector3.up, flat).normalized;
            for (int i = -1; i <= 1; i += 2)
            {
                var dir = (axis + right * (i * 0.12f)).normalized;
                var basePos = SphereGenerator.Centre + dir * (SphereGenerator.Radius - 5.5f);
                var t = ArtPass.AddMesh(group, "SD_Turbine " + (i < 0 ? "L" : "R"), bodyAsset, ArtPass.Mats("M_Paint_White"), basePos, Quaternion.LookRotation(flat), Vector3.one);
                var r = ArtPass.AddMesh(t.transform, "Rotor", rotorAsset, ArtPass.Mats("M_Paint_White"), Vector3.zero, Quaternion.identity, Vector3.one);
                r.transform.localPosition = new Vector3(0f, 4.2f, 1f);
                GameObjectUtility.SetStaticEditorFlags(r, 0);
                r.AddComponent<Spinner>().Configure(Vector3.forward, 16f + i * 2f);
            }
        }

        private static GameObject Add(Transform group, string name, Mesh mesh, params string[] mats)
        {
            var asset = ArtPass.MeshAsset(name, () => mesh);
            var go = ArtPass.AddMesh(group, name, asset, ArtPass.Mats(mats), Vector3.zero, Quaternion.identity, Vector3.one);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        private static GameObject Solid(GameObject go)
        {
            go.layer = ArtPass.TrackGeometryLayer;
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            return go;
        }
    }
}
