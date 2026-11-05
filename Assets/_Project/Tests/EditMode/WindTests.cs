using Ushqyr.Flight;
using Ushqyr.Garage;
using Ushqyr.Strike;
using NUnit.Framework;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>P7: wind on the Strike Range — deterministic from the seed and clock, zero by default everywhere else.</summary>
    public sealed class WindTests
    {
        [Test]
        public void RangeWind_ByDifficulty_AndDeterministic()
        {
            Assert.AreEqual(Vector3.zero, StrikeRules.WindAt(RangeDifficulty.Easy, 12, 40f));
            float normal = 0f, hard = 0f, hardMax = 0f;
            for (float t = 0f; t < 120f; t += 0.5f)
            {
                normal += StrikeRules.WindAt(RangeDifficulty.Normal, 12, t).magnitude;
                float h = StrikeRules.WindAt(RangeDifficulty.Hard, 12, t).magnitude;
                hard += h;
                hardMax = Mathf.Max(hardMax, h);
                Assert.AreEqual(StrikeRules.WindAt(RangeDifficulty.Hard, 12, t), StrikeRules.WindAt(RangeDifficulty.Hard, 12, t));
                Assert.AreEqual(0f, StrikeRules.WindAt(RangeDifficulty.Hard, 12, t).y, "horizontal");
            }
            normal /= 240f;
            hard /= 240f;
            Assert.AreEqual(2f, normal, 0.6f, "Normal: about 2 m/s");
            Assert.AreEqual(4f, hard, 1.2f, "Hard: about 4 m/s");
            Assert.Greater(hardMax, 6f, "Hard has gusts");
            Assert.AreNotEqual(StrikeRules.WindAt(RangeDifficulty.Hard, 12, 10f), StrikeRules.WindAt(RangeDifficulty.Hard, 13, 10f), "the seed picks the wind");
        }

        private static Vec3 Drift(Vec3 wind)
        {
            var profile = BuildComposer.Compose(QuadPresets.Race5);
            var env = PhysicsSettings.Default;
            env.Wind = wind;
            var s = QuadState.AtRest(new Vec3(0f, 20f, 0f), Quat.Identity);
            s.Armed = true;
            float hover = MotorModel.HoverCommand(profile.Motors, profile.Mass * env.Gravity);
            var input = PilotInput.Armed(hover, FlightMode.Angle);
            for (int i = 0; i < 3000; i++) QuadSimulator.Step(ref s, input, profile, env, env.FixedStep);
            return s.Velocity;
        }

        [Test]
        public void Wind_PushesAHoveringQuadDownwind_AndZeroWindChangesNothing()
        {
            var calm = Drift(Vec3.Zero);
            var windy = Drift(new Vec3(6f, 0f, 0f));
            Assert.Greater(windy.x, calm.x + 1f, "after 3 s in 6 m/s of wind the quad drifts downwind");
            Assert.Less(Mathf.Abs(windy.z - calm.z), 0.5f, "and not sideways");
        }
    }
}
