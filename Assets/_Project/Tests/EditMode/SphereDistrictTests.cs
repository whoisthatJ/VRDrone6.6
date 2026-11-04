using System.Collections.Generic;
using System.Linq;
using Ushqyr.EditorTools.SphereDistrict;
using Ushqyr.Track.Geo;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>Sphere District E2: polygon tools behind the hero generators and the generated buildings.</summary>
    public class SphereDistrictTests
    {
        private static float MeshArea(Mesh m, int sub)
        {
            var v = m.vertices;
            var t = m.GetTriangles(sub);
            float a = 0f;
            for (int i = 0; i < t.Length; i += 3) a += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
            return a;
        }

        [Test]
        public void CapWithAHole_CoversTheRingArea()
        {
            var outer = PolyMesh.Oriented(new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 30), new Vector2(0, 30) }, true);
            var hole = PolyMesh.Oriented(new[] { new Vector2(10, 10), new Vector2(25, 10), new Vector2(25, 20), new Vector2(10, 20) }, false);
            var pm = new PolyMesh();
            pm.Cap(outer, new List<List<Vector2>> { hole }, 5f, 0);
            var mesh = pm.Build("test");
            Assert.AreEqual(40f * 30f - 15f * 10f, MeshArea(mesh, 0), 0.5f, "outer minus the courtyard");
            Assert.IsTrue(mesh.normals.All(n => n.y > 0.99f), "the roof faces up");
        }

        [Test]
        public void ScoopOpening_IsOnTheSouthWestTopOfTheSphere()
        {
            Assert.Less(SphereGenerator.ScoopMetric(SphereGenerator.ScoopAxis), 0.01f);
            Assert.Greater(SphereGenerator.ScoopMetric(Vector3.down), 1f);
            Assert.Greater(SphereGenerator.ScoopMetric(new Vector3(0.3f, 0.6f, 0.7f).normalized), 1f, "not on the north side");
            Assert.Less(SphereGenerator.ScoopAxis.x, 0f);
            Assert.Less(SphereGenerator.ScoopAxis.z, 0f);
        }

        [Test]
        public void PetalPavilions_FromOsm_HaveCourtyardsAndSkylights()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GeoDataAsset>(OsmImporter.DataPath);
            Assert.IsNotNull(asset, "run Ushqyr > Sphere District > Import OSM");
            var petals = asset.Data.Buildings.Where(b => b.HeroRole == "pavilion").ToList();
            Assert.AreEqual(4, petals.Count, "four petal pavilions");
            foreach (var b in petals)
            {
                Assert.AreEqual(1, b.Holes.Count, $"courtyard of {b.Id}");
                var mesh = PillowPavilionGenerator.Build(b, "test");
                Assert.AreEqual(4, mesh.subMeshCount);
                Assert.Greater(mesh.GetTriangles(2).Length, 0, "skylights on the roof");
                Assert.AreEqual(PillowPavilionGenerator.Height, mesh.bounds.max.y, 0.1f, "roof height (skylights sit 6 cm above it)");
                // Roof area ~ footprint minus courtyard (the pillow edge eats a band all round).
                float roof = MeshArea(mesh, 1);
                Assert.Greater(roof, 0.6f * (Mathf.Abs(PolyMesh.SignedArea(b.Footprint)) - Mathf.Abs(PolyMesh.SignedArea(b.Holes[0].Points))));
            }
            Assert.AreEqual(1, asset.Data.Buildings.Count(b => b.HeroRole == "arena"));
            Assert.AreEqual(3, asset.Data.Buildings.Count(b => b.HeroRole == "pod"));
        }

        [Test]
        public void Streetscape_KeepsPropsOffTheRaceCorridor_AndOffsetsLines()
        {
            var st = new StreetscapeGenerator(new GeoData(), new[] { new List<Vector3> { new Vector3(0, 0, 0), new Vector3(100, 0, 0), new Vector3(100, 0, 100) } });
            Assert.IsTrue(st.NearCorridor(new Vector2(50f, 3f)), "3 m from the corridor");
            Assert.IsFalse(st.NearCorridor(new Vector2(50f, 20f)), "20 m away");
            var line = StreetscapeGenerator.OffsetLine(new List<Vector2> { new Vector2(0, 0), new Vector2(10, 0) }, 2f);
            Assert.AreEqual(2f, line[0].y, 1e-4f, "left of an eastbound line is north");
            Assert.AreEqual(2f, line[1].y, 1e-4f);
        }
    
        [Test]
        public void ExpoCourses_AreAirCourses_EndInTheFinishCube_AndHaveTheirLengths()
        {
            foreach (var (t, min, max, top) in new[]
            {
                // P3: the Sprint climbs up through the Halo (≤ ~60 m), the Pro crosses the sphere (≤ ~115 m).
                (Ushqyr.EditorTools.Builders.RouteLibrary.ExpoSprint(), 1200f, 1800f, 65f),
                (Ushqyr.EditorTools.Builders.RouteLibrary.ExpoPro(), 2300f, 3300f, 116f),
            })
            {
                Assert.IsTrue(t.Route.AirCourse, t.Id);
                Assert.AreEqual("Finish", t.Route.Obstacles[t.Route.FinishObstacle].Type, t.Id);
                var route = new Ushqyr.Track.TrackRoute(t.Route);
                Assert.That(route.Length, Is.InRange(min, max), t.Id);
                float high = 0f;
                foreach (var k in t.Route.Knots) high = Mathf.Max(high, k.Position.y);
                Assert.That(high, Is.LessThanOrEqualTo(top), t.Id);
                Assert.IsEmpty(t.Validate(), t.Id);
            }
        }

        [Test]
        public void ExpoFreeFlight_HasNoGates_FourSpawns_AndValidates()
        {
            var t = Ushqyr.EditorTools.Builders.RouteLibrary.ExpoFreeFlight();
            Assert.IsTrue(t.FreeFlight);
            Assert.IsFalse(t.IsSplineTrack);
            Assert.AreEqual(3, t.ExtraSpawns.Count);
            Assert.IsEmpty(t.Validate());
            Assert.IsTrue(t.ExtraSpawns.Exists(s => s.Position.y > 100f), "a spawn above the sphere");
        }
    }
}
