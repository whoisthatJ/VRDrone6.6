using System;
using System.Collections.Generic;
using GateRush.Flight;
using NUnit.Framework;

namespace GateRush.Tests
{
    /// <summary>Every shipped quad class must hover cleanly and track rate steps (catches bad PID sizing).</summary>
    public class QuadProfilesTests
    {
        private const float Dt = 0.001f;

        public static IEnumerable<TestCaseData> Profiles()
        {
            yield return new TestCaseData(QuadProfile.Race5Inch).SetName("Race5");
            yield return new TestCaseData(QuadProfile.Freestyle5Inch).SetName("Freestyle5");
            yield return new TestCaseData(QuadProfile.Whoop65).SetName("Whoop65");
        }

        public static IEnumerable<TestCaseData> ProfileAxes()
        {
            foreach (var p in new[] { QuadProfile.Race5Inch, QuadProfile.Freestyle5Inch, QuadProfile.Whoop65 })
            foreach (var axis in new[] { "roll", "pitch", "yaw" })
                yield return new TestCaseData(p, axis).SetName($"{p.Name.Replace("\"", "in")}_{axis}");
        }

        private static float Weight(in QuadProfile p) => p.Mass * PhysicsSettings.Default.Gravity;

        private static QuadState Hovering(in QuadProfile p)
        {
            var s = QuadState.AtRest(new Vec3(0f, 10f, 0f), Quat.Identity);
            float speed = MotorModel.HoverSpeed(p.Motors, Weight(p));
            for (int i = 0; i < 4; i++) s.MotorSpeed[i] = speed;
            s.Armed = true;
            return s;
        }

        [TestCaseSource(nameof(Profiles))]
        public void Hover_HoldsAltitudeAndAttitude(QuadProfile p)
        {
            var s = Hovering(p);
            var input = PilotInput.Armed(MotorModel.HoverCommand(p.Motors, Weight(p)));
            for (int i = 0; i < 3000; i++) QuadSimulator.Step(ref s, input, p, Dt);
            Assert.That(MathF.Abs(s.Position.y - 10f), Is.LessThan(0.05f));
            Assert.That(s.Up.y, Is.GreaterThan(0.9999f));
        }

        [TestCaseSource(nameof(Profiles))]
        public void HoverThrottle_IsInAFlyableRange(QuadProfile p)
        {
            Assert.That(MotorModel.HoverCommand(p.Motors, Weight(p)), Is.InRange(0.15f, 0.6f));
        }

        [TestCaseSource(nameof(ProfileAxes))]
        public void RateStep_TracksSetpoint(QuadProfile p, string axis)
        {
            var s = Hovering(p);
            var input = PilotInput.Armed(MotorModel.HoverCommand(p.Motors, Weight(p)));
            if (axis == "roll") input.Roll = 0.5f;
            else if (axis == "pitch") input.Pitch = 0.5f;
            else input.Yaw = 0.5f;

            Vec3 target = QuadSimulator.ComputeSetpoint(s, input, p);
            float mag = target.Magnitude;
            float rise = -1f, peak = 0f;
            for (int i = 0; i < 500; i++)
            {
                QuadSimulator.Step(ref s, input, p, Dt);
                float along = Vec3.Dot(s.AngularVelocity, target) / mag;
                if (rise < 0f && along >= 0.9f * mag) rise = (i + 1) * Dt;
                peak = MathF.Max(peak, along);
            }

            float limit = axis == "yaw" ? 0.25f : 0.12f;
            Assert.That(rise, Is.GreaterThan(0f).And.LessThan(limit), $"90% rise {rise * 1000f:F0} ms");
            Assert.That(peak / mag, Is.LessThan(1.3f), "overshoot");
            float final = Vec3.Dot(s.AngularVelocity, target) / mag;
            Assert.That(final / mag, Is.InRange(0.95f, 1.05f), "steady state");
        }

        [Test]
        public void Classes_DifferInTheExpectedWays()
        {
            var race = QuadProfile.Race5Inch;
            var free = QuadProfile.Freestyle5Inch;
            var whoop = QuadProfile.Whoop65;
            float Twr(in QuadProfile q) => 4f * q.Motors.MaxThrustPerMotor / Weight(q);
            Assert.That(Twr(race), Is.GreaterThan(Twr(free)));
            Assert.That(Twr(free), Is.GreaterThan(Twr(whoop)));
            Assert.That(whoop.Mass, Is.LessThan(0.05f));
        }
    }
}
