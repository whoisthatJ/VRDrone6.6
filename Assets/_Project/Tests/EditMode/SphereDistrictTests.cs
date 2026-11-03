using System.Collections.Generic;
using System.Linq;
using GateRush.EditorTools.SphereDistrict;
using GateRush.Track.Geo;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GateRush.Tests
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
            Assert.IsNotNull(asset, "run GateRush > Sphere District > Import OSM");
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
    }
}
