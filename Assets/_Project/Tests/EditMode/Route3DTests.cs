using System.Linq;
using Ushqyr.Race;
using Ushqyr.Track;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>P3: routes with a full 3D frame (vertical and inverted sections) and gates aligned to it.</summary>
    public class Route3DTests
    {
        private static TrackPrefabs Prefabs => AssetDatabase.LoadAssetAtPath<TrackPrefabs>("Assets/_Project/Data/TrackPrefabs.asset");

        /// <summary>A closed route in the x = 0 plane that climbs straight up at z = 60.</summary>
        private static TrackData Climb()
        {
            var t = new TrackData { Id = "climb", Name = "Climb", Environment = "field", Laps = 1 };
            t.Route.AirCourse = true;
            foreach (var p in new[] { new Vector3(0, 5, 0), new Vector3(0, 5, 40), new Vector3(0, 20, 60), new Vector3(0, 45, 62), new Vector3(0, 70, 60),
                         new Vector3(0, 85, 40), new Vector3(0, 85, 0), new Vector3(0, 45, -20) })
                t.Route.Knots.Add(new RouteKnot { Position = p });
            return t;
        }

        /// <summary>A vertical loop in the x = 0 plane; the knots along the top are rolled 180°.</summary>
        private static TrackData Loop()
        {
            var t = new TrackData { Id = "loop", Name = "Loop", Environment = "field", Laps = 1 };
            t.Route.AirCourse = true;
            (Vector3 p, float roll)[] knots =
            {
                (new Vector3(0, 5, 0), 0f), (new Vector3(0, 5, 50), 0f), (new Vector3(0, 30, 75), 90f),
                (new Vector3(0, 55, 50), 180f), (new Vector3(0, 55, 0), 180f), (new Vector3(0, 30, -25), 90f),
            };
            foreach (var (p, roll) in knots) t.Route.Knots.Add(new RouteKnot { Position = p, Roll = roll });
            return t;
        }

        [Test]
        public void Frame_OnAVerticalClimb_PointsUp_AndStaysOrthonormal()
        {
            var route = new TrackRoute(Climb().Route);
            var f = route.Frame(route.ClosestDistance(new Vector3(0, 45, 62)));
            Assert.Greater(f.Forward.y, 0.9f, "the tangent points up the climb");
            Assert.That(Vector3.Dot(f.Forward, f.Up), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(f.Up.magnitude, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(Vector3.Dot(f.Right, f.Forward), Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void Frame_RolledKnots_InvertTheTopOfALoop()
        {
            var route = new TrackRoute(Loop().Route);
            var top = route.Frame(route.ClosestDistance(new Vector3(0, 55, 25)));
            Assert.Less(top.Up.y, -0.9f, "upside down along the top");
            var bottom = route.Frame(route.ClosestDistance(new Vector3(0, 5, 25)));
            Assert.Greater(bottom.Up.y, 0.9f, "upright along the bottom");
        }

        [Test]
        public void Frame_WithoutRoll_KeepsTheOldLevelFrameOnFlatRoutes()
        {
            var t = Climb();
            var route = new TrackRoute(t.Route);
            var flat = route.ClosestDistance(new Vector3(0, 5, 20));
            Assert.That(Vector3.Angle(route.Frame(flat).Up, route.Sample(flat).Up), Is.LessThan(1f));
        }

        [Test]
        public void Align3DGate_OnTheClimb_FacesUp_AndOnlyCountsUpwards()
        {
            var t = Climb();
            var route = new TrackRoute(t.Route);
            t.Route.Obstacles.Add(new RouteObstacle { Type = "Air", Distance = route.ClosestDistance(new Vector3(0, 45, 62)), Align3D = true });
            var parent = new GameObject("climb");
            try
            {
                var loaded = TrackBuilder.Build(t, Prefabs, parent.transform, 0);
                var gate = loaded.Gates.Single();
                Assert.Greater(gate.transform.forward.y, 0.9f, "the opening faces up the climb");
                var line = route.Frame(route.ClosestDistance(new Vector3(0, 45, 62))).Position;
                Assert.That(Vector3.Distance(gate.OpeningCenterWorld, line), Is.LessThan(0.05f), "centred on the flight line");
                Vector3 c = gate.OpeningCenterWorld;
                Assert.IsTrue(gate.TryCross(c - Vector3.up, c + Vector3.up, out _), "climbing through counts");
                Assert.IsFalse(gate.TryCross(c + Vector3.up, c - Vector3.up, out _), "dropping back through doesn't");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
