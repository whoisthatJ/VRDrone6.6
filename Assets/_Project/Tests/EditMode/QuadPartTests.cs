using System.Linq;
using GateRush.Garage;
using NUnit.Framework;
using UnityEngine;

namespace GateRush.Tests
{
    /// <summary>P2: the Blender-generated quad parts match the garage part data the physics uses.</summary>
    public class QuadPartTests
    {
        private static QuadPartLibrary Lib
        {
            get
            {
                var lib = QuadPartLibrary.Instance;
                Assert.IsNotNull(lib, "no QuadPartLibrary in Resources (GateRush > Quads > Generate Quad Parts)");
                return lib;
            }
        }

        [Test]
        public void EveryCataloguePart_HasAGeneratedMesh()
        {
            foreach (var id in PartCatalog.Frames.Select(p => p.Id).Concat(PartCatalog.Motors.Select(p => p.Id))
                         .Concat(PartCatalog.Props.Select(p => p.Id)).Concat(PartCatalog.Props.Select(p => p.Id + "-blur"))
                         .Concat(PartCatalog.Batteries.Select(p => p.Id)))
                Assert.IsNotNull(Lib.Find(id), id);
        }

        [Test]
        public void FrameSockets_MatchTheWheelbase()
        {
            foreach (var f in PartCatalog.Frames)
            {
                var e = Lib.Find(f.Id);
                Assert.IsTrue(e.TryGetSocket("motor0", out var a), f.Id);
                Assert.IsTrue(e.TryGetSocket("motor3", out var b), f.Id);
                float diagonalMm = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)) * 1000f;
                Assert.That(diagonalMm, Is.EqualTo(f.WheelbaseMm).Within(f.WheelbaseMm * 0.01f), $"{f.Id} wheelbase");
                Assert.That(a.y, Is.EqualTo(f.PlateMm * 0.001f).Within(1e-4f), $"{f.Id} motors sit on the arm plate");
            }
        }

        [Test]
        public void PropMeshes_HaveTheirDiameter()
        {
            foreach (var p in PartCatalog.Props)
            {
                // Tip radius from the hub (a 3-blade prop's bounding box spans only ~0.87 of the disc).
                float tip = Lib.Find(p.Id).Mesh.vertices.Max(v => new Vector2(v.x, v.z).magnitude);
                float diameter = tip * 2f;
                float expected = p.DiameterIn * 0.0254f;
                // Blade tips round off, so the swept disc is a little under the nominal diameter.
                Assert.That(diameter, Is.InRange(expected * 0.9f, expected * 1.02f), p.Id);
                var disc = Lib.Find(p.Id + "-blur").Mesh.bounds;
                Assert.That(disc.size.x, Is.EqualTo(expected).Within(expected * 0.02f), p.Id + " blur disc");
            }
        }

        [Test]
        public void ShippedBuilds_PropsDontOverlap_AndSitOnTheMotors()
        {
            var parent = new GameObject("quads");
            try
            {
                foreach (var b in QuadPresets.All)
                {
                    var m = QuadModelBuilder.Build(b, new QuadModelMaterials(), parent.transform);
                    Assert.IsTrue(m.Props.All(p => p != null), b.Name + " four props");
                    Assert.AreEqual(4, m.BlurDiscs.Length, b.Name + " blur discs");
                    float r = b.Prop.DiameterIn * 0.0254f * 0.5f;
                    for (int i = 0; i < 4; i++)
                        for (int j = i + 1; j < 4; j++)
                        {
                            var pi = m.Props[i].localPosition;
                            var pj = m.Props[j].localPosition;
                            float gap = Vector2.Distance(new Vector2(pi.x, pi.z), new Vector2(pj.x, pj.z)) - 2f * r;
                            Assert.Greater(gap, 0f, $"{b.Name}: props {i} and {j} overlap");
                        }
                    var frame = Lib.Find(b.FrameId);
                    for (int i = 0; i < 4; i++)
                    {
                        frame.TryGetSocket("motor" + i, out var mount);
                        var p = m.Props[i].localPosition;
                        Assert.That(new Vector2(p.x - mount.x, p.z - mount.z).magnitude, Is.LessThan(1e-4f), $"{b.Name}: prop {i} on its motor socket");
                        Assert.Greater(p.y, mount.y, $"{b.Name}: prop {i} above the mount");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void PartMasses_SumToTheFlightProfileMass()
        {
            foreach (var b in QuadPresets.All)
            {
                float parts = b.Frame.MassG + 4f * (b.Motor.MassG + b.Prop.MassG) + b.Battery.MassG + BuildComposer.ElectronicsMassG(b.Size) + b.Extras.Sum(e => e.MassG);
                Assert.That(BuildComposer.Compose(b).Mass * 1000f, Is.EqualTo(parts).Within(0.01f), b.Name);
            }
        }
    }
}
