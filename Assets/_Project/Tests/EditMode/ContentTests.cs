using System.Linq;
using GateRush.Game;
using GateRush.Track;
using NUnit.Framework;
using UnityEditor;

namespace GateRush.Tests
{
    /// <summary>Checks the shipped content: track JSON, catalogs and scenes fit together.</summary>
    public class ContentTests
    {
        private static TrackCatalog Tracks => AssetDatabase.LoadAssetAtPath<TrackCatalog>("Assets/_Project/Data/TrackCatalog.asset");
        private static QuadCatalog Quads => AssetDatabase.LoadAssetAtPath<QuadCatalog>("Assets/_Project/Data/QuadCatalog.asset");
        private static TrackPrefabs Prefabs => AssetDatabase.LoadAssetAtPath<TrackPrefabs>("Assets/_Project/Data/TrackPrefabs.asset");

        [Test]
        public void EveryCatalogTrack_ParsesValidatesAndHasAScene()
        {
            var catalog = Tracks;
            Assert.IsNotNull(catalog, "TrackCatalog asset missing (run GateRush/Builders/Build All Scenes)");
            Assert.That(catalog.Tracks.Count, Is.GreaterThanOrEqualTo(3));
            var buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToList();

            foreach (var entry in catalog.Tracks)
            {
                Assert.IsNotNull(entry.Json, $"{entry.Id}: no JSON");
                var data = TrackData.FromJson(entry.Json.text);
                Assert.AreEqual(entry.Id, data.Id, "catalog id matches the file");
                var errors = data.Validate(Prefabs.GateTypes(), Prefabs.PropTypes());
                Assert.IsEmpty(errors, $"{entry.Id}: {string.Join("; ", errors)}");
                Assert.That(data.Gates.Count, Is.GreaterThanOrEqualTo(4), $"{entry.Id}: too few gates");
                string scene = catalog.SceneFor(data.Environment);
                Assert.IsNotNull(scene, $"{entry.Id}: no scene for environment '{data.Environment}'");
                CollectionAssert.Contains(buildScenes, scene, $"{entry.Id}: scene '{scene}' not in build settings");
            }
        }

        [Test]
        public void QuadCatalog_HasThreeClassesWithProfiles()
        {
            var catalog = Quads;
            Assert.IsNotNull(catalog);
            CollectionAssert.AreEquivalent(new[] { "race5", "freestyle5", "whoop65" }, catalog.Quads.Select(q => q.Id).ToArray());
            foreach (var q in catalog.Quads)
            {
                Assert.IsNotNull(q.Profile, q.Id);
                Assert.That(q.CollisionRadius, Is.GreaterThan(0f), q.Id);
                Assert.That(q.Profile.Profile.Mass, Is.GreaterThan(0f), q.Id);
            }
        }

        [Test]
        public void BuildSettings_StartWithBootThenMenu()
        {
            var scenes = EditorBuildSettings.scenes;
            StringAssert.EndsWith("Boot.unity", scenes[0].path);
            StringAssert.EndsWith("Menu.unity", scenes[1].path);
        }
    }
}
