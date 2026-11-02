using System;
using System.Linq;
using GateRush.Flight;
using GateRush.Garage;
using NUnit.Framework;
using UnityEngine;

namespace GateRush.Tests
{
    /// <summary>T7/T8: parts catalog, build composer (physics → profile + stats), presets fly, model and saving.</summary>
    public class GarageTests
    {
        private const float Dt = 0.001f;

        private static QuadBuild[] Presets => QuadPresets.All.ToArray();

        [Test]
        public void Catalog_IdsUnique_AndEverySizeHasAFullSet()
        {
            var ids = PartCatalog.All.Select(p => p.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "unique part ids");
            foreach (SizeClass size in Enum.GetValues(typeof(SizeClass)))
            {
                Assert.IsTrue(PartCatalog.Frames.Any(f => f.Size == size), $"frame for {size}");
                Assert.IsTrue(PartCatalog.Motors.Any(m => m.Fits(size)), $"motor for {size}");
                Assert.IsTrue(PartCatalog.Props.Any(p => p.Fits(size)), $"prop for {size}");
                Assert.IsTrue(PartCatalog.Batteries.Any(b => b.Fits(size)), $"battery for {size}");
            }
        }

        [Test]
        public void SixPresets_AreValid([ValueSource(nameof(Presets))] QuadBuild b)
        {
            CollectionAssert.IsEmpty(BuildComposer.Validate(b), b.Name);
        }

        [Test]
        public void Validate_BlocksPropTooBigForFrame()
        {
            var b = QuadPresets.Toothpick3;
            b.PropId = "prop-5143-3";
            Assert.IsNotEmpty(BuildComposer.Validate(b));
        }

        [Test]
        public void Race5Preset_MatchesTheHandTunedRacer()
        {
            var s = BuildComposer.Stats(QuadPresets.Race5);
            var p = BuildComposer.Compose(QuadPresets.Race5);
            Assert.That(s.ThrustToWeight, Is.InRange(9f, 16f), "race thrust-to-weight");
            Assert.That(s.TopSpeedKmh, Is.InRange(105f, 160f), "top speed");
            Assert.That(s.Rpm, Is.InRange(22000f, 33000f), "4S 2400KV under load");
            Assert.That(p.Motors.MaxThrustPerMotor, Is.EqualTo(QuadProfile.Race5Inch.Motors.MaxThrustPerMotor).Within(3f));
            Assert.That(p.Aero.QuadraticDrag.z, Is.EqualTo(QuadProfile.Race5Inch.Aero.QuadraticDrag.z).Within(0.006f));
        }

        [Test]
        public void Stats_FollowThePhysics()
        {
            var race = QuadPresets.Race5;
            var heavy = race.With(PartCatalog.Find("extra-actioncam"));
            var a = BuildComposer.Stats(race);
            var b = BuildComposer.Stats(heavy);
            Assert.AreEqual(a.WeightG + 121f, b.WeightG, 0.01f, "the camera's mass adds up");
            Assert.Less(b.TopSpeedKmh, a.TopSpeedKmh, "more drag and weight: slower");
            Assert.Less(b.ThrustToWeight, a.ThrustToWeight);
            Assert.AreEqual(a.ThrustG, b.ThrustG, 0.01f, "same power train, same static thrust");

            var sixS = race.With(PartCatalog.Find("motor-raptor2207-1950")).With(PartCatalog.Find("lipo-6s-1100"));
            Assert.Greater(BuildComposer.Stats(sixS).Rpm, a.Rpm, "6S spins a 1950KV motor faster than 4S spins 2400KV");

            var lowPitch = race.With(PartCatalog.Find("prop-5136-3"));
            Assert.Less(BuildComposer.Compose(lowPitch).Motors.PropPitchSpeed, BuildComposer.Compose(race).Motors.PropPitchSpeed, "lower pitch: lower pitch speed");
        }

        [Test]
        public void SwappingTheFrameRefitsTheRest()
        {
            var b = QuadPresets.Race5.With(PartCatalog.Find("frame-kestrel65"));
            CollectionAssert.IsEmpty(BuildComposer.Validate(b));
            Assert.AreEqual(SizeClass.Whoop, b.Size);
            Assert.AreEqual("motor-gnat0702", b.MotorId);
        }

        [Test]
        public void EveryPreset_HoversAndHoldsAngleModeInTheSimulator([ValueSource(nameof(Presets))] QuadBuild b)
        {
            var p = BuildComposer.Compose(b);
            float weight = p.Mass * PhysicsSettings.Default.Gravity;
            var s = QuadState.AtRest(new Vec3(0f, 10f, 0f), Quat.Identity);
            float speed = MotorModel.HoverSpeed(p.Motors, weight);
            for (int i = 0; i < 4; i++) s.MotorSpeed[i] = speed;
            s.Armed = true;
            float hover = MotorModel.HoverCommand(p.Motors, weight);

            // Tilt, then let Angle mode level it; then hold a pitch command.
            s.Orientation = Quat.EulerDegrees(20f, 0f, -25f);
            var input = PilotInput.Armed(hover, FlightMode.Angle);
            for (int i = 0; i < 2000; i++) QuadSimulator.Step(ref s, input, p, Dt);
            Assert.That(s.Up.y, Is.GreaterThan(0.995f), $"{b.Name} levels itself");
            Assert.That(s.AngularVelocity.Magnitude, Is.LessThan(0.5f), $"{b.Name} settles (no oscillation)");

            input.Pitch = 0.5f;
            for (int i = 0; i < 1500; i++) QuadSimulator.Step(ref s, input, p, Dt);
            float pitchDeg = -MathF.Asin(s.Forward.y) * 180f / MathF.PI;
            Assert.That(pitchDeg, Is.EqualTo(0.5f * p.AngleMode.MaxAngleDeg).Within(4f), $"{b.Name} holds the commanded tilt");
            Assert.IsFalse(float.IsNaN(s.Position.x), "finite state");
        }

        [Test]
        public void Model_HasFourPropsAndScalesWithTheFrame()
        {
            var parent = new GameObject("test");
            try
            {
                var race = QuadModelBuilder.Build(QuadPresets.Race5, new QuadModelMaterials(), parent.transform);
                Assert.IsTrue(race.Props.All(p => p != null), "four props");
                Assert.IsTrue(race.Parts.ContainsKey("Frame") && race.Parts.ContainsKey("Battery"));
                var whoop = QuadModelBuilder.Build(QuadPresets.Whoop65, new QuadModelMaterials(), parent.transform);
                float r = Vector3.Distance(race.Props[0].localPosition, race.Props[3].localPosition);
                float w = Vector3.Distance(whoop.Props[0].localPosition, whoop.Props[3].localPosition);
                Assert.That(r / w, Is.EqualTo(220f / 65f).Within(0.2f), "motor spacing follows the wheelbase");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Builds_SaveLoadAndResolve()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gaterush-builds-" + Guid.NewGuid().ToString("N"));
            try
            {
                var lib = new BuildLibrary(dir);
                var b = QuadPresets.Freestyle5;
                b.Name = "Test build";
                lib.Save(b);
                StringAssert.DoesNotStartWith("preset-", b.Id, "saving a preset makes a new build");
                var back = lib.Resolve(BuildLibrary.IdPrefix + b.Id);
                Assert.IsNotNull(back);
                Assert.AreEqual("Test build", back.Name);
                CollectionAssert.AreEqual(b.ExtraIds, back.ExtraIds);
                Assert.AreEqual(1, lib.LoadAll().Count);
                Assert.IsNotNull(lib.Resolve(BuildLibrary.IdPrefix + "preset-race5"), "presets resolve by id");
            }
            finally
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
