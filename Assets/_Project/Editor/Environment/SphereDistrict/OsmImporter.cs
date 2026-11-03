using System.IO;
using System.Linq;
using GateRush.Track.Geo;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.SphereDistrict
{
    /// <summary>
    /// Sphere District E1: converts the cached Overpass extract (Data/Geo/SphereDistrict/osm_extract.json, fetched once,
    /// © OpenStreetMap contributors, ODbL) into <see cref="GeoDataAsset"/> in local metres around the sphere centre, and
    /// applies <see cref="GeoOverrides"/>. On the first import it marks the hero footprints (the sphere, the petal
    /// pavilions and the pods inside the ring road) so the generic extruder leaves them to the hero generators.
    /// Menu: GateRush > Sphere District > Import OSM; batch: -executeMethod GateRush.EditorTools.SphereDistrict.OsmImporter.ImportCli
    /// </summary>
    public static class OsmImporter
    {
        public const string Dir = "Assets/_Project/Data/Geo/SphereDistrict";
        public const string ExtractPath = Dir + "/osm_extract.json";
        public const string DataPath = Dir + "/SphereDistrict_Geo.asset";
        public const string OverridesPath = Dir + "/SphereDistrict_Overrides.asset";
        /// <summary>The sphere centre (centroid of its OSM outline): the district origin.</summary>
        public const double OriginLat = 51.08931, OriginLon = 71.41615;
        /// <summary>Footprints whose centre lies inside this radius belong to the complex (hero generators).</summary>
        public const float HeroRadius = 300f;

        public static GeoProjection Projection => new GeoProjection(OriginLat, OriginLon);

        [MenuItem("GateRush/Sphere District/Import OSM")]
        public static string Import()
        {
            var data = OsmParser.Parse(File.ReadAllText(ExtractPath), Projection);
            var overrides = AssetDatabase.LoadAssetAtPath<GeoOverrides>(OverridesPath);
            if (overrides == null)
            {
                overrides = ScriptableObject.CreateInstance<GeoOverrides>();
                AssetDatabase.CreateAsset(overrides, OverridesPath);
            }
            int added = SeedHeroes(data, overrides);
            overrides.Apply(data);
            EditorUtility.SetDirty(overrides);

            var asset = AssetDatabase.LoadAssetAtPath<GeoDataAsset>(DataPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<GeoDataAsset>();
                AssetDatabase.CreateAsset(asset, DataPath);
            }
            asset.Data = data;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            int heroes = data.Buildings.Count(b => b.Hero), estimated = data.Buildings.Count(b => b.HeightEstimated);
            string report = $"OSM {data.Timestamp}: {data.Buildings.Count} buildings ({heroes} hero, {estimated} with default heights), " +
                            $"{data.Roads.Count} roads, {data.Areas.Count} areas, {data.Trees.Count} trees; {added} hero overrides added";
            Debug.Log("[GateRush] Sphere District " + report);
            return report;
        }

        public static void ImportCli() => Import();

        /// <summary>Adds an override for each complex footprint that has none yet (sphere / pod / pavilion by distance).</summary>
        private static int SeedHeroes(GeoData data, GeoOverrides overrides)
        {
            int added = 0;
            foreach (var b in data.Buildings)
            {
                if (overrides.Find(b.Id) != null) continue;
                var c = Centroid(b.Footprint);
                float r = c.magnitude;
                if (r > HeroRadius) continue;
                float area = Mathf.Abs(Area(b.Footprint));
                string role;
                if (b.Kind == "roof") role = "canopy";
                else if (r < 45f) role = "sphere";
                else if (r < 120f) role = "pod";
                else if (area > 12000f || b.Name.StartsWith("Павильон")) role = "pavilion";
                else continue;                                         // small park buildings: generic extruder
                float bearing = (Mathf.Atan2(c.x, c.y) * Mathf.Rad2Deg + 360f) % 360f;
                overrides.Entries.Add(new GeoOverrides.Entry
                {
                    Id = b.Id,
                    Hero = true,
                    HeroRole = role,
                    Note = $"{(b.Name.Length > 0 ? b.Name : b.Kind)}: {r:0} m at {bearing:0} deg",
                });
                added++;
            }
            return added;
        }

        /// <summary>Signed polygon area (m²).</summary>
        public static float Area(System.Collections.Generic.List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) a += p[i].x * p[(i + 1) % p.Count].y - p[(i + 1) % p.Count].x * p[i].y;
            return a * 0.5f;
        }

        public static Vector2 Centroid(System.Collections.Generic.List<Vector2> p)
        {
            var c = Vector2.zero;
            foreach (var q in p) c += q;
            return p.Count > 0 ? c / p.Count : c;
        }

        [MenuItem("GateRush/Sphere District/Show OSM Preview")]
        public static void ShowPreview()
        {
            var existing = Object.FindAnyObjectByType<GeoPreview>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }
            // Not saved with the scene: a Scene-view aid only.
            var go = new GameObject("OSM Preview") { hideFlags = HideFlags.DontSave };
            go.AddComponent<GeoPreview>().Data = AssetDatabase.LoadAssetAtPath<GeoDataAsset>(DataPath);
            Selection.activeGameObject = go;
        }
    }
}
