using Ushqyr.Flight;
using Ushqyr.Game;
using Ushqyr.Vehicle;
using UnityEditor;
using UnityEngine;

namespace Ushqyr.EditorTools.Builders
{
    /// <summary>Creates/updates the quad profile assets, the quad catalog and the track catalog.</summary>
    public static class CatalogBuilder
    {
        public const string QuadCatalogPath = "Assets/_Project/Data/QuadCatalog.asset";
        public const string TrackCatalogPath = "Assets/_Project/Data/TrackCatalog.asset";
        private const string ProfileDir = "Assets/_Project/Data/QuadProfiles";

        [MenuItem("Ushqyr/Builders/Build Catalogs")]
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
            catalog.Tracks.Add(Entry("field", "field", "Classic loop", "Open Field", "Big outdoor loop with a floating gate and a hurdle. Room to learn."));
            catalog.Tracks.Add(Entry("hangar", "hangar", "Hall circuit", "Hangar 7", "Indoor hall: tight turns between crates, columns and walls. Whoop heaven."));
            catalog.Tracks.Add(Entry("industrial", "industrial", "Container run", "Dockyard", "Container corridors and a gate above a container stack."));
            catalog.Tracks.Add(Entry("spline-test", "field", "Figure-eight", "Spline Test Loop", "Figure-eight lane track with an overpass (spline track system test)."));
            catalog.Tracks.Add(Entry("stadium-oval", "stadium", "Oval", "Stadium Oval", "Floodlit football stadium: an easy oval around the pitch. Learn the lanes and gates."));
            catalog.Tracks.Add(Entry("serpent-arena", "stadium", "Serpent", "Serpent Arena", "The stadium race track: loops round the landing pads, a figure-eight at the centre spot and four overpasses."));
            catalog.Tracks.Add(Entry("expo-v2-sprint", "expo", "Sprint", "Expo Sprint", "Beginner, 1.5 km: the courtyard hop, round the sphere at 3 m, a climb straight up through the Holo Halo and back down the axis. Trainer guidance recommended."));
            catalog.Tracks.Add(Entry("expo-v2-pro", "expo", "Pro", "Expo Pro", "Advanced, 2.9 km: over the top of the sphere, down through the Holo Halo, the needle dive, the ladder, a split-S, the ring road and a second courtyard."));
            catalog.Tracks.Add(Entry("expo-v2-free", "expo", "Free flight", "Expo Free Flight", "No gates: fly anywhere within 1.2 km of the sphere and up to 150 m. F / D-pad up switches between the south plaza, above the sphere, the park and the ring road; T resets."));
            catalog.Environments.Clear();
            catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "field", SceneName = "Track_Field", DisplayName = "Open Field", Description = "A big outdoor field: a floating gate, a hurdle and a figure-eight lane track." });
            catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "hangar", SceneName = "Track_Hangar", DisplayName = "Hangar 7", Description = "Indoor hall with crates, columns and walls." });
            catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "industrial", SceneName = "Track_Industrial", DisplayName = "Dockyard", Description = "Container corridors at the port." });
            catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "stadium", SceneName = "Track_Stadium", DisplayName = "Stadium", Description = "Floodlit football stadium with lane tracks round the pitch." });
            catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "expo", SceneName = "Track_Expo", DisplayName = "Expo Astana", Description = "The Expo district in Astana: the sphere, the pavilions, holo rings and vertical routes." });
            // P5: the Strike Range (steppe), one "variation" — a free-flight scene where StrikeSession runs the rounds.
            if (System.IO.File.Exists(Ushqyr.EditorTools.Range.RangeSteppeBuilder.TrackJsonPath))
            {
                catalog.Tracks.Add(Entry("strike-steppe", "steppe", "Strike rounds", "Strike Range", "Two-minute rounds on a 1.2 km steppe range: hit static, moving and evasive vehicles from three launch pads, alone or against bots."));
                catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = "steppe", SceneName = "Range_Steppe", DisplayName = "Strike Range", Description = "Steppe range with roads, berms and revetments: FPV strike training against static, moving and evasive targets." });
            }
            // Phase 3 P1a: the first Sphere District, frozen with its original routes and records.
            if (System.IO.File.Exists(Ushqyr.EditorTools.SphereDistrict.TrackFreezer.FrozenScene))
            {
                catalog.Tracks.Add(Frozen("expo-astana", "Classic Sprint", "Expo Classic Sprint", "The original Expo Sprint (Track_Expo_v1): the courtyard hop, the low sphere orbit and the park slalom, in the first look of the district."));
                catalog.Tracks.Add(Frozen("expo-pro", "Classic Pro", "Expo Classic Pro", "The original Expo Pro (Track_Expo_v1): helix to 90 m, the park dive, the ring-road sweeper and the second courtyard."));
                catalog.Tracks.Add(Frozen("expo-free", "Classic Free Flight", "Expo Classic Free Flight", "Free flight in the original district (Track_Expo_v1)."));
                catalog.Environments.Add(new TrackCatalog.EnvironmentScene { Environment = Ushqyr.EditorTools.SphereDistrict.TrackFreezer.Environment, SceneName = "Track_Expo_v1", DisplayName = "Expo Astana Classic", Description = "The first version of the Expo district, with its original routes and records." });
            }
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static TrackCatalog.Entry Frozen(string id, string variation, string name, string description) => new TrackCatalog.Entry
        {
            Id = id,
            Environment = Ushqyr.EditorTools.SphereDistrict.TrackFreezer.Environment,
            Variation = variation,
            Preview = PreviewFor(id),
            DisplayName = name,
            Description = description,
            Json = AssetDatabase.LoadAssetAtPath<TextAsset>(Ushqyr.EditorTools.SphereDistrict.TrackFreezer.TrackJson(id)),
        };

        private static TrackCatalog.Entry Entry(string id, string environment, string variation, string name, string description) => new TrackCatalog.Entry
        {
            Id = id,
            Environment = environment,
            Variation = variation,
            Preview = PreviewFor(id),
            DisplayName = name,
            Description = description,
            Json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{CourseLibrary.TrackDir}/{id}.json"),
        };

        public const string PreviewDir = "Assets/_Project/Art/Previews";

        private static Texture2D PreviewFor(string id) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{PreviewDir}/{id}.png");

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
