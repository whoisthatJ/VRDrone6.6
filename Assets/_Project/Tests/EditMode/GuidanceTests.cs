using System.Linq;
using GateRush.Core;
using GateRush.Game;
using GateRush.Race;
using GateRush.Track;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GateRush.Tests
{
    /// <summary>T5 route guidance: racing-line colours, missing a gate, and what a built track exposes for the setting.</summary>
    public class GuidanceTests
    {
        /// <summary>A long oval: 120 m straights joined by tight 8 m half-circles.</summary>
        private static TrackRoute HairpinOval()
        {
            var r = new RouteData();
            for (int i = 0; i <= 12; i++) r.Knots.Add(new RouteKnot { Position = new Vector3(-60f + i * 10f, 1.5f, -8f) });
            for (int i = 1; i < 8; i++)
            {
                float a = -Mathf.PI / 2 + i * Mathf.PI / 8;
                r.Knots.Add(new RouteKnot { Position = new Vector3(60f + Mathf.Cos(a) * 8f, 1.5f, Mathf.Sin(a) * 8f) });
            }
            for (int i = 0; i <= 12; i++) r.Knots.Add(new RouteKnot { Position = new Vector3(60f - i * 10f, 1.5f, 8f) });
            for (int i = 1; i < 8; i++)
            {
                float a = Mathf.PI / 2 + i * Mathf.PI / 8;
                r.Knots.Add(new RouteKnot { Position = new Vector3(-60f + Mathf.Cos(a) * 8f, 1.5f, Mathf.Sin(a) * 8f) });
            }
            return new TrackRoute(r);
        }

        private static Color ColorAt(TrackRoute route, Color[] colors, Vector3 p) =>
            colors[Mathf.Clamp(Mathf.RoundToInt(route.ClosestDistance(p) / RacingLine.Step), 0, colors.Length - 1)];

        [Test]
        public void RacingLine_GreenOnStraights_YellowBeforeHairpins_RedInThem()
        {
            var route = HairpinOval();
            var colors = RacingLine.Colors(route);

            var straight = ColorAt(route, colors, new Vector3(-20f, 1.5f, -8f));
            Assert.Greater(straight.g, 0.8f, "middle of the straight is green");
            Assert.Less(straight.r, 0.4f);

            var hairpin = ColorAt(route, colors, new Vector3(68f, 1.5f, 0f));
            Assert.Greater(hairpin.r, 0.8f, "apex of the hairpin is red");
            Assert.Less(hairpin.g, 0.4f);

            // Entering the hairpin (~12 m before its apex, flying +X on the -Z straight): braking from 20 to ~10.6 m/s.
            var brake = ColorAt(route, colors, new Vector3(60f, 1.5f, -8f));
            Assert.Greater(brake.r, 0.5f, "braking zone is yellow");
            Assert.Greater(brake.g, 0.5f);
        }

        [Test]
        public void RecommendedSpeed_NeverNeedsMoreThanTheBrakingRate()
        {
            var route = HairpinOval();
            var v = RacingLine.RecommendedSpeeds(RacingLine.CornerSpeeds(route));
            for (int i = 0; i < v.Length; i++)
            {
                float next = v[(i + 1) % v.Length];
                float decel = (v[i] * v[i] - next * next) / (2f * RacingLine.Step);
                Assert.LessOrEqual(decel, RacingLine.Braking + 0.01f, $"sample {i}");
            }
        }

        [Test]
        public void Gate_PassedBeside_OnlyOutsideTheOpeningAndClose()
        {
            var go = new GameObject("gate");
            try
            {
                var gate = go.AddComponent<Gate>();
                Vector3 c = gate.OpeningCenterWorld;
                Assert.IsTrue(gate.PassedBeside(c + new Vector3(2.5f, 0f, -1f), c + new Vector3(2.5f, 0f, 1f), RaceSession.MissRadius), "beside");
                Assert.IsFalse(gate.PassedBeside(c + new Vector3(0f, 0f, -1f), c + new Vector3(0f, 0f, 1f), RaceSession.MissRadius), "through it");
                Assert.IsFalse(gate.PassedBeside(c + new Vector3(12f, 0f, -1f), c + new Vector3(12f, 0f, 1f), RaceSession.MissRadius), "far away");
                Assert.IsFalse(gate.PassedBeside(c + new Vector3(2.5f, 0f, 1f), c + new Vector3(2.5f, 0f, -1f), RaceSession.MissRadius), "backwards");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BuiltSplineTrack_HasHiddenRacingLineAndLaneColliders()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>("Assets/_Project/Data/TrackCatalog.asset");
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>("Assets/_Project/Data/TrackPrefabs.asset");
            var data = TrackData.FromJson(catalog.Tracks.First(t => t.Id == "serpent-arena").Json.text);
            var track = TrackLoader.Load(data, prefabs);
            try
            {
                Assert.IsNotNull(track.RacingLine, "racing line");
                Assert.IsFalse(track.RacingLine.activeSelf, "hidden until Trainer");
                var mesh = track.RacingLine.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.bounds.size.x, Is.GreaterThan(50f), "follows the whole route");
                Assert.IsNotNull(track.RacingLine.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap"), "colour strip");
                Assert.IsNotNull(track.LaneColliders, "lane colliders");
                Assert.IsNotEmpty(track.LaneColliders.GetComponents<BoxCollider>());
            }
            finally
            {
                Object.DestroyImmediate(track.Root.gameObject);
            }
        }

        [Test]
        public void GuidanceSettings_DefaultStandard_AndClamp()
        {
            var s = new GameSettings();
            Assert.AreEqual((int)RouteGuidanceLevel.Standard, s.Guidance.RouteGuidance);
            Assert.IsFalse(s.Guidance.GhostLanes);
            s.Guidance.RouteGuidance = 9;
            s.Clamp();
            Assert.AreEqual((int)RouteGuidanceLevel.Trainer, s.Guidance.RouteGuidance);
            var old = GameSettings.FromJson("{\"Version\":1,\"PilotName\":\"x\"}");
            Assert.IsNotNull(old.Guidance, "older settings files get the default");
        }
    }
}
