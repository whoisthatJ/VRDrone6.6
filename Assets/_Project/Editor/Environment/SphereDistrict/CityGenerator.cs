using System.Collections.Generic;
using GateRush.EditorTools.Art;
using GateRush.Track.Geo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.SphereDistrict
{
    /// <summary>
    /// Sphere District E4: the city round the complex. Every OSM building that no hero generator owns is extruded to
    /// its height (tag, levels × 3.2 m or the per-kind default) with a facade by kind (residential window rhythm,
    /// glass for offices and hotels, concrete for schools, hospitals and the rest) and a roof cap, combined per 400 m
    /// cell with a culling LOD and a mesh collider. Beyond the mapped area a far ring of low blocks fills the horizon.
    /// </summary>
    public static class CityGenerator
    {
        public const float Cell = 400f;
        public const float ShadowRadius = 700f;
        private const int SubResidential = 0, SubGlass = 1, SubConcrete = 2, SubRoof = 3, SubGround = 4;
        private static readonly string[] Mats =
        {
            "M_Expo_Apartment", "M_Expo_CurtainWall", "M_City_Concrete", "M_City_Roof", "M_Glass_Dirty",
            "M_City_Beige", "M_City_Cream", "M_City_Pastel", "M_City_GlassBlue", "M_City_RoofDark",
        };
        // P1: palette variants per kind, picked by the OSM id so a building keeps its colour between rebuilds.
        private static readonly int[] ResidentialVariants = { SubResidential, 5, 6, 7 };
        private static readonly int[] GlassVariants = { SubGlass, 8 };
        private static readonly int[] ConcreteVariants = { SubConcrete, 5 };
        private static readonly int[] RoofVariants = { SubRoof, 9 };

        private static int Pick(int[] variants, long id) => variants[(int)((id * 2654435761L >> 8) & 0x7fffffff) % variants.Length];

        public static int FacadeFor(GeoBuilding b) => b.Kind switch
        {
            "apartments" or "residential" or "house" or "dormitory" => Pick(ResidentialVariants, b.Id),
            "office" or "hotel" or "commercial" or "retail" or "government" => Pick(GlassVariants, b.Id),
            _ => b.Height >= 28f ? Pick(ResidentialVariants, b.Id) : Pick(ConcreteVariants, b.Id),
        };

        public static string Build(ArtContext ctx, GeoData data, Transform group)
        {
            var cells = new Dictionary<Vector2Int, PolyMesh>();
            int count = 0;
            foreach (var b in data.Buildings)
            {
                if (b.Hero || b.Footprint.Count < 3) continue;
                var outer = PolyMesh.Oriented(PolyMesh.Clean(b.Footprint, 0.4f), true);
                if (outer.Count < 3 || Mathf.Abs(PolyMesh.SignedArea(outer)) < 12f) continue;
                var c = PolyMesh.Centroid(outer);
                var key = new Vector2Int(Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.y / Cell));
                if (!cells.TryGetValue(key, out var pm))
                {
                    cells[key] = pm = new PolyMesh();
                    pm.EnsureSubmeshes(Mats.Length);
                }
                float h = Mathf.Clamp(b.Height, 3f, 200f);
                int facade = FacadeFor(b);
                // A darker glazed ground floor on taller buildings, then the facade to the roof.
                if (h > 9f)
                {
                    pm.Wall(outer, 0f, 3.6f, SubGround);
                    pm.Wall(outer, 3.6f, h, facade);
                }
                else pm.Wall(outer, 0f, h, facade);
                pm.Cap(outer, null, h, Pick(RoofVariants, b.Id + 7));
                count++;
            }

            int cellCount = 0;
            foreach (var kv in cells)
            {
                string name = $"SD_City_{kv.Key.x}_{kv.Key.y}";
                var mesh = ArtPass.MeshAsset(name, () => kv.Value.Build(name));
                var go = ArtPass.AddMesh(group, name, mesh, ArtPass.Mats(Mats), Vector3.zero, Quaternion.identity, Vector3.one);
                // Only cells near the course cast shadows; beyond that the shadow cascades cost CPU for nothing visible.
                var centre = new Vector2((kv.Key.x + 0.5f) * Cell, (kv.Key.y + 0.5f) * Cell);
                go.GetComponent<MeshRenderer>().shadowCastingMode = centre.magnitude < ShadowRadius ? ShadowCastingMode.On : ShadowCastingMode.Off;
                go.layer = ArtPass.TrackGeometryLayer;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.015f, new Renderer[] { go.GetComponent<MeshRenderer>() }) });
                cellCount++;
            }
            return $" city {count} buildings in {cellCount} cells;";
        }

        /// <summary>
        /// Low blocks from the edge of the mapped area (1.5 km) out to 4.5 km: an Astana-style mix of 9-25 floor slabs
        /// and some towers, thinner with distance, kept off the park axis. Combined into a few meshes, no colliders.
        /// </summary>
        public static string BuildFarRing(Transform group, float parkBearing)
        {
            var rng = new System.Random(4501);
            var near = new List<(Mesh, Matrix4x4, int)>();
            var far = new List<(Mesh, Matrix4x4, int)>();
            int n = 0;
            for (float x = -4500f; x <= 4500f; x += 140f)
                for (float z = -4500f; z <= 4500f; z += 140f)
                {
                    var p = new Vector2(x + (float)(rng.NextDouble() - 0.5) * 60f, z + (float)(rng.NextDouble() - 0.5) * 60f);
                    float r = p.magnitude;
                    if (r < 1580f || r > 4500f) continue;
                    float bearing = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg;
                    // Keep the axis towards Baiterek fairly open (wide boulevard).
                    if (Mathf.Abs(Mathf.DeltaAngle(bearing, parkBearing)) < 2.5f) continue;
                    if (rng.NextDouble() < Mathf.InverseLerp(1500f, 4500f, r) * 0.55f) continue;
                    bool tower = rng.NextDouble() < 0.12;
                    float h = tower ? 60f + (float)rng.NextDouble() * 70f : (9 + rng.Next(0, 17)) * 3f;
                    float w = tower ? 26f : 50f + (float)rng.NextDouble() * 40f, d = tower ? 26f : 15f + (float)rng.NextDouble() * 4f;
                    // Towers are glass; slabs take one of the four residential palettes.
                    int sub = tower ? 1 : new[] { 0, 2, 3, 4 }[rng.Next(0, 4)];
                    var part = ProcMesh.Part(new Vector3(p.x, h * 0.5f, p.y), new Vector3(w, h, d), Quaternion.Euler(0f, rng.Next(0, 4) * 90f + (float)(rng.NextDouble() - 0.5) * 10f, 0f), sub);
                    (r < 2800f ? near : far).Add(part);
                    n++;
                }
            var mats = ArtPass.Mats("M_Expo_Apartment", "M_Expo_Tower", "M_City_Beige", "M_City_Cream", "M_City_Pastel");
            foreach (var (name, parts) in new[] { ("SD_FarRing_Near", near), ("SD_FarRing_Far", far) })
            {
                var mesh = ArtPass.MeshAsset(name, () => ProcMesh.Combine(name, parts));
                var go = ArtPass.AddMesh(group, name, mesh, mats, Vector3.zero, Quaternion.identity, Vector3.one);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            return $" far ring {n} blocks;";
        }
    }
}
