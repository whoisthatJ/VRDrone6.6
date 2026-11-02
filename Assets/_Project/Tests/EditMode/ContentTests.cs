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
                int gates = data.IsSplineTrack ? data.Route.Obstacles.Count(o => o.IsCheckpoint) : data.Gates.Count;
                Assert.That(gates, Is.GreaterThanOrEqualTo(4), $"{entry.Id}: too few gates");
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

        [TestCase("BETAFPV Joystick", "BetaFPV LiteRadio 2 SIM (joystick mode)")]
        [TestCase("BETAFPV LiteRadio 3", "BetaFPV LiteRadio 3")]
        [TestCase("LiteRadio 4 SE", "BetaFPV LiteRadio 4 / 4 SE (USB)")]
        [TestCase("BETAFPV LiteRadio 4 BLE", "BetaFPV LiteRadio 4 / 4 SE (Bluetooth)")]
        [TestCase("BETAFPV Xbox Controller", "BetaFPV LiteRadio 2 SIM (Xbox mode)")]
        [TestCase("BETAFPV Nano Radio", "BetaFPV radio (generic)")]
        [TestCase("RadioMaster TX16S Joystick", "EdgeTX / OpenTX radio (generic)")]
        [TestCase("Thrustmaster T.16000M", null)]
        public void RadioPresets_MatchExpectedDeviceNames(string deviceName, string expectedPreset)
        {
            var lib = AssetDatabase.LoadAssetAtPath<GateRush.Input.ControllerPresetLibrary>("Assets/_Project/Data/Radios/ControllerPresets.asset");
            Assert.IsNotNull(lib);
            var preset = lib.Find(deviceName, deviceName);
            Assert.AreEqual(expectedPreset, preset != null ? preset.DisplayName : null);
            if (preset != null) Assert.IsFalse(preset.Verified, "shipped presets stay unverified until checked on hardware");
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
