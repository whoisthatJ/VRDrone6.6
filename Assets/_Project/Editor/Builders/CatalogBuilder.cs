using GateRush.Flight;
using GateRush.Game;
using GateRush.Vehicle;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    /// <summary>Creates/updates the quad profile assets, the quad catalog and the track catalog.</summary>
    public static class CatalogBuilder
    {
        public const string QuadCatalogPath = "Assets/_Project/Data/QuadCatalog.asset";
        public const string TrackCatalogPath = "Assets/_Project/Data/TrackCatalog.asset";
        private const string ProfileDir = "Assets/_Project/Data/QuadProfiles";

        [MenuItem("GateRush/Builders/Build Catalogs")]
        public static void BuildAll()
        {
            CourseLibrary.WriteAll();
            RouteLibrary.WriteAll();
            BuildQuads();
            BuildTracks();
            AssetDatabase.SaveAssets();
        }

        public static QuadCatalog BuildQuads()
        {
            var race = Profile("Race5", QuadProfile.Race5Inch);
            var free = Profile("Freestyle5", QuadProfile.Freestyle5Inch);
            var whoop = Profile("Whoop65", QuadProfile.Whoop65);

            var catalog = LoadOrCreate<QuadCatalog>(QuadCatalogPath);
            catalog.Quads.Clear();
            catalog.Quads.Add(new QuadCatalog.Entry
            {
                Id = "race5", DisplayName = "5\" Race", Profile = race,
                Description = "650 g, ~10:1 thrust-to-weight. Light, fast and twitchy: the race class.",
                VisualScale = 1f, CollisionRadius = 0.12f, CameraOffset = new Vector3(0f, 0.02f, 0.095f), MotorRpm = 28000f,
            });
            catalog.Quads.Add(new QuadCatalog.Entry
            {
                Id = "freestyle5", DisplayName = "5\" Freestyle", Profile = free,
                Description = "750 g with an action cam, ~7.5:1. More momentum, smoother, Actual rates.",
                VisualScale = 1.05f, CollisionRadius = 0.13f, CameraOffset = new Vector3(0f, 0.025f, 0.1f), MotorRpm = 25000f,
            });
            catalog.Quads.Add(new QuadCatalog.Entry
            {
                Id = "whoop65", DisplayName = "65mm Whoop", Profile = whoop,
                Description = "32 g ducted tiny whoop, ~3:1. Slow, floaty and forgiving: great indoors and for learning.",
                VisualScale = 0.3f, CollisionRadius = 0.045f, CameraOffset = new Vector3(0f, 0.012f, 0.028f), MotorRpm = 45000f,
            });
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        public static TrackCatalog BuildTracks()
        {
            var catalog = LoadOrCreate<TrackCatalog>(TrackCatalogPath);
            catalog.Tracks.Clear();
            catalog.Tracks.Add(Entry("field", "Open Field", "Big outdoor loop with a floating gate and a hurdle. Room to learn."));
            catalog.Tracks.Add(Entry("hangar", "Hangar 7", "Indoor hall: tight turns between crates, columns and walls. Whoop heaven."));
            catalog.Tracks.Add(Entry("industrial", "Dockyard", "Container corridors and a gate above a container stack."));
            catalog.Tracks.Add(Entry("spline-test", "Spline Test Loop", "Figure-eight lane track with an overpass (spline track system test)."));
            catalog.Environments.Clear();
            catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "field", SceneName = "Track_Field" });
            catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "hangar", SceneName = "Track_Hangar" });
            catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "industrial", SceneName = "Track_Industrial" });
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static TrackCatalog.Entry Entry(string id, string name, string description) => new TrackCatalog.Entry
        {
            Id = id,
            DisplayName = name,
            Description = description,
            Json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{CourseLibrary.TrackDir}/{id}.json"),
        };

        private static QuadProfileAsset Profile(string file, QuadProfile values)
        {
            var asset = LoadOrCreate<QuadProfileAsset>($"{ProfileDir}/{file}.asset");
            asset.Profile = values;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
