using System.Linq;
using GateRush.Core;
using GateRush.Game;
using GateRush.Track;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GateRush.Tests
{
    /// <summary>T3 stadium: the scene's dressing, the runtime time-of-day presets and the Stadium Oval track.</summary>
    public class StadiumTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Track_Stadium.unity";
        // 400 m athletics track: inner straight 84.39 m, inner radius 36.5 m, 8 lanes of 1.22 m.
        private const float HalfStraight = 42.195f, InnerRadius = 36.5f, OuterRadius = 36.5f + 8 * 1.22f;

        private static TrackCatalog Tracks => AssetDatabase.LoadAssetAtPath<TrackCatalog>("Assets/_Project/Data/TrackCatalog.asset");

        /// <summary>Distance of a point from the track's skeleton (the segment between the two bend centres).</summary>
        private static float OvalRadius(Vector3 p) => new Vector2(Mathf.Max(Mathf.Abs(p.x) - HalfStraight, 0f), p.z).magnitude;

        private static Scene OpenStadium(out Scene previous)
        {
            previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            return scene;
        }

        private static void Close(Scene scene, Scene previous)
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }

        private static T Find<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<T>(true)).FirstOrDefault(c => c != null);

        [Test]
        public void StadiumScene_HasSeatsBoardsFloodlightsAndSolidStructures()
        {
            Assert.IsTrue(System.IO.File.Exists(ScenePath), "Track_Stadium missing (run Build All Scenes + art pass)");
            var scene = OpenStadium(out var previous);
            try
            {
                var seats = Find<InstancedSeats>(scene);
                Assert.IsNotNull(seats, "instanced seats");
                Assert.That(seats.Count, Is.GreaterThan(5000), "two 24-row stands");

                var boards = Find<LedBoardScroller>(scene);
                Assert.IsNotNull(boards, "LED board scroller");

                var lighting = Find<StadiumLighting>(scene);
                Assert.IsNotNull(lighting, "StadiumLighting");
                Assert.AreEqual(3, lighting.Presets.Length, "night, dusk, day");
                foreach (var p in lighting.Presets)
                {
                    Assert.IsNotNull(p.Sky, $"{p.Name} sky");
                    Assert.IsNotNull(p.Volume, $"{p.Name} post volume");
                }

                var spots = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).Where(l => l.type == LightType.Spot).ToList();
                Assert.That(spots.Count, Is.GreaterThanOrEqualTo(4), "a spot light per floodlight tower");

                // Stands, roofs, towers and boards collide (TrackGeometry), and nothing on the flight line does.
                var solids = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)).Where(c => c.gameObject.layer == 8).ToList();
                Assert.That(solids.Count, Is.GreaterThan(30));
            }
            finally
            {
                Close(scene, previous);
            }
        }

        [Test]
        public void TimeOfDay_SwitchesFloodlightsAndVolumes()
        {
            var scene = OpenStadium(out var previous);
            try
            {
                var lighting = Find<StadiumLighting>(scene);
                var spots = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).Where(l => l.type == LightType.Spot).ToList();

                lighting.Apply(TimeOfDay.Night);
                Assert.AreEqual(TimeOfDay.Night, lighting.Current);
                Assert.IsTrue(spots.All(l => l.enabled && l.intensity > 0f), "floodlights on at night");
                Assert.AreEqual(1f, lighting.Presets[0].Volume.weight);
                Assert.AreEqual(lighting.Presets[0].Sky, RenderSettings.skybox);

                lighting.Apply(TimeOfDay.Day);
                Assert.AreEqual(TimeOfDay.Day, lighting.Current);
                Assert.IsTrue(spots.All(l => !l.enabled), "floodlights off by day");
                Assert.AreEqual(0f, lighting.Presets[0].Volume.weight);
                Assert.AreEqual(1f, lighting.Presets[2].Volume.weight);
                Assert.AreEqual(lighting.Presets[2].Sky, RenderSettings.skybox);
            }
            finally
            {
                Close(scene, previous);
            }
        }

        [Test]
        public void StadiumOval_FollowsTheRunningTrack()
        {
            var entry = Tracks.Tracks.FirstOrDefault(t => t.Id == "stadium-oval");
            Assert.IsNotNull(entry, "stadium-oval in the catalog");
            var data = TrackData.FromJson(entry.Json.text);
            Assert.AreEqual("stadium", data.Environment);
            Assert.IsTrue(data.IsSplineTrack);

            var route = new TrackRoute(data.Route);
            Assert.That(route.Length, Is.InRange(400f, 480f), "one lap along the middle lanes");
            foreach (var o in data.Route.Obstacles)
            {
                float r = OvalRadius(route.Sample(o.Distance).Position);
                Assert.That(r, Is.InRange(InnerRadius, OuterRadius), $"{o.Type} at {o.Distance:F0} m sits on the lanes");
            }
            float spawn = OvalRadius(data.Spawn.Position);
            Assert.That(spawn, Is.InRange(InnerRadius, OuterRadius), "start pad on the track");
        }

        [Test]
        public void StadiumLightingSetting_IsClamped()
        {
            var s = new GameSettings();
            s.Feed.StadiumLighting = 7;
            s.Clamp();
            Assert.AreEqual(2, s.Feed.StadiumLighting);
            s.Feed.StadiumLighting = -1;
            s.Clamp();
            Assert.AreEqual(0, s.Feed.StadiumLighting);
        }
    }
}
