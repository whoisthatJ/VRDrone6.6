using System.Collections.Generic;
using GateRush.Race;
using GateRush.Track;
using NUnit.Framework;
using UnityEngine;

namespace GateRush.Tests
{
    public class RouteTrackTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        /// <summary>A 12-knot oval (~250 m) raised to 4 m on its far side, with 6 checkpoint gates and one scenery obstacle.</summary>
        private static TrackData Oval(int finish = 2)
        {
            var t = new TrackData { Id = "test-oval", Name = "Test Oval", Laps = 2 };
            var r = t.Route;
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                float y = i >= 5 && i <= 7 ? 4f : 1.5f;
                r.Knots.Add(new RouteKnot { Position = new Vector3(Mathf.Sin(a) * 50f, y, Mathf.Cos(a) * 30f) });
            }
            // Deliberately out of order in the list.
            foreach (float d in new[] { 150f, 20f, 60f, 230f, 100f, 190f })
                r.Obstacles.Add(new RouteObstacle { Type = "Square", Distance = d });
            r.Obstacles.Add(new RouteObstacle { Type = "Crate", Distance = 120f, Lateral = 4f, IsCheckpoint = false });
            r.FinishObstacle = finish;
            return t;
        }

        private LoadedTrack Build(TrackData t)
        {
            var route = new TrackRoute(t.Route);
            // Start pad on the centre line 8 m before the finish.
            var pad = route.Sample(t.Route.Obstacles[Mathf.Max(0, t.Route.FinishObstacle)].Distance - 8f);
            t.Spawn.Position = new Vector3(pad.Position.x, 0.15f, pad.Position.z);
            t.Spawn.Yaw = Mathf.Atan2(pad.FlatForward.x, pad.FlatForward.z) * Mathf.Rad2Deg;
            var loaded = TrackBuilder.Build(t, null, null, 0);
            _spawned.Add(loaded.Root.gameObject);
            return loaded;
        }

        [Test]
        public void Json_RoundTrip_KeepsRouteAndObstacles()
        {
            var t = Oval();
            t.Route.Knots[3].TangentIn = new Vector3(-2f, 0f, 1f);
            t.Route.Knots[3].TangentOut = new Vector3(2f, 0f, -1f);
            t.Route.Obstacles[1].Color = "#ff8800";
            t.Route.Obstacles[1].Yaw = 15f;
            t.Route.CorridorWidth = 5f;

            var copy = TrackData.FromJson(t.ToJson());
            Assert.IsTrue(copy.IsSplineTrack);
            Assert.AreEqual(12, copy.Route.Knots.Count);
            Assert.AreEqual(t.Route.Knots[5].Position, copy.Route.Knots[5].Position);
            Assert.AreEqual(new Vector3(2f, 0f, -1f), copy.Route.Knots[3].TangentOut);
            Assert.AreEqual(7, copy.Route.Obstacles.Count);
            Assert.AreEqual("#ff8800", copy.Route.Obstacles[1].Color);
            Assert.AreEqual(15f, copy.Route.Obstacles[1].Yaw);
            Assert.IsFalse(copy.Route.Obstacles[6].IsCheckpoint);
            Assert.AreEqual(2, copy.Route.FinishObstacle);
            Assert.AreEqual(5f, copy.Route.CorridorWidth);
            Assert.IsEmpty(copy.Validate(new[] { "Square" }, new[] { "Crate" }));
        }

        [Test]
        public void Json_GateListTrack_HasNoRoute()
        {
            var old = TrackData.FromJson("{\"Id\":\"old\",\"Gates\":[{\"Type\":\"Square\"}]}");
            Assert.IsFalse(old.IsSplineTrack);
            Assert.IsNotNull(old.Route);
            Assert.IsEmpty(old.Validate());
        }

        [Test]
        public void Order_FinishFirst_ThenByDistanceAfterIt()
        {
            var d = new[] { 150f, 20f, 60f, 230f, 100f, 190f, 120f };
            var cp = new[] { true, true, true, true, true, true, false };
            var order = TrackCheckpoints.Order(d, cp, 2, 50f, 250f);
            // Finish = obstacle 2 (60 m); then 100, 150, 190, 230, and 20 (wrapped past the end of the lap).
            CollectionAssert.AreEqual(new[] { 2, 4, 0, 5, 3, 1 }, order);
        }

        [Test]
        public void Order_WithoutFinish_UsesFirstCheckpointAfterStart()
        {
            var d = new[] { 150f, 20f, 60f, 230f };
            var cp = new[] { true, true, true, true };
            CollectionAssert.AreEqual(new[] { 3, 1, 2, 0 }, TrackCheckpoints.Order(d, cp, -1, 200f, 250f));
            CollectionAssert.AreEqual(new[] { 1, 2, 0, 3 }, TrackCheckpoints.Order(d, cp, -1, 245f, 250f), "wraps past the end");
        }

        [Test]
        public void Build_CheckpointsAreSortedByRouteDistance()
        {
            var loaded = Build(Oval());
            var route = loaded.Route;
            Assert.AreEqual(6, loaded.Gates.Length, "the crate is scenery");
            float finish = route.ClosestDistance(loaded.Gates[0].transform.position + Vector3.up * 1.5f);
            Assert.AreEqual(60f, finish, 1f);
            float last = -1f;
            for (int i = 0; i < loaded.Gates.Length; i++)
            {
                Assert.AreEqual(i, loaded.Gates[i].Order);
                float ahead = TrackCheckpoints.Ahead(finish, route.ClosestDistance(loaded.Gates[i].transform.position + Vector3.up * 1.5f), route.Length);
                if (i > 0) Assert.Greater(ahead, last, $"gate {i} must be after gate {i - 1}");
                last = ahead;
            }
        }

        [Test]
        public void Build_GeneratesLanesChevronsAndSupportsOnlyWhereRaised()
        {
            var loaded = Build(Oval());
            var route = loaded.Route;
            var lanes = loaded.Root.Find("Route/Lane Colliders").GetComponents<BoxCollider>();
            Assert.AreEqual(4 * Mathf.CeilToInt(route.Length / TrackBuilder.PostSpacing), lanes.Length, 8, "a post every 2 m on 4 lines");
            var supports = loaded.Root.Find("Route/Support Colliders").GetComponents<BoxCollider>();
            Assert.Greater(supports.Length, 0, "the raised side has legs");
            foreach (var leg in supports)
            {
                var p = route.Sample(route.ClosestDistance(leg.bounds.center, -1f, 0f, horizontal: true));
                Assert.Greater(TrackBuilder.DeckHeight(route.Data, p), 0.1f, "legs only under the raised deck");
            }
            int segments = 0;
            foreach (Transform child in loaded.Root.Find("Route"))
                if (child.name.StartsWith("Segment")) segments++;
            Assert.AreEqual(Mathf.CeilToInt(route.Length / TrackBuilder.SegmentLength), segments, "one combined mesh per 20 m");
        }

        [Test]
        public void Build_IsDeterministic()
        {
            var a = Build(Oval());
            var b = Build(Oval());
            Assert.AreEqual(a.Route.Length, b.Route.Length);
            for (int i = 0; i < a.Gates.Length; i++) Assert.AreEqual(a.Gates[i].transform.position, b.Gates[i].transform.position);
            var ma = a.Root.Find("Route").GetComponentsInChildren<MeshFilter>();
            var mb = b.Root.Find("Route").GetComponentsInChildren<MeshFilter>();
            Assert.AreEqual(ma.Length, mb.Length);
            for (int i = 0; i < ma.Length; i++) Assert.AreEqual(ma[i].sharedMesh.vertexCount, mb[i].sharedMesh.vertexCount);
        }

        /// <summary>Flies the route from the start pad and feeds every gate crossing to a lap timer.</summary>
        private static LapTimer Fly(LoadedTrack track, float from, float metres, float direction, System.Func<float, bool> skip = null)
        {
            var timer = new LapTimer(track.Gates.Length, track.Data.Laps);
            timer.Start(0);
            var route = track.Route;
            Vector3 prev = route.Sample(from).Position;
            double time = 0;
            for (float m = 0.5f; m <= metres; m += 0.5f)
            {
                float d = from + direction * m;
                Vector3 next = route.Sample(d).Position;
                time += 0.02;
                if (skip != null && skip(m))
                {
                    // "Cut the course": jump sideways over this stretch instead of flying through it.
                    prev = next + Vector3.up * 20f;
                    continue;
                }
                for (int g = 0; g < track.Gates.Length; g++)
                    if (track.Gates[g].TryCross(prev, next, out _)) timer.Pass(g, time);
                prev = next;
            }
            return timer;
        }

        [Test]
        public void Lap_CountsOnlyWhenEveryCheckpointIsFlownInOrder()
        {
            var loaded = Build(Oval());
            float start = loaded.Route.ClosestDistance(loaded.Data.Spawn.Position + Vector3.up * 1.35f);
            float length = loaded.Route.Length;

            var clean = Fly(loaded, start, 8f + 2f * length + 4f, +1f);
            Assert.IsTrue(clean.Finished, "two clean laps finish the race");
            Assert.AreEqual(2, clean.CompletedLaps);

            var backwards = Fly(loaded, start, 2f * length + 20f, -1f);
            Assert.AreEqual(-1, backwards.LastCheckpoint, "flying the wrong way never counts a gate");
            Assert.AreEqual(0, backwards.CompletedLaps);

            // Skip the stretch around the third checkpoint on every lap (skipping it once just means the next lap counts).
            float third = TrackCheckpoints.Ahead(start, loaded.Route.ClosestDistance(loaded.Gates[2].transform.position + Vector3.up * 1.5f), length);
            var cut = Fly(loaded, start, 8f + 2f * length + 4f, +1f, m => Mathf.Abs(Mathf.DeltaAngle((m - third) / length * 360f, 0f)) < 3f / length * 360f);
            Assert.AreEqual(0, cut.CompletedLaps, "a lap with a missed checkpoint does not count");
            Assert.IsFalse(cut.Finished);
        }
    }
}
