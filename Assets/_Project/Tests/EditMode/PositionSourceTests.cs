using System;
using NUnit.Framework;
using Ushqyr.Flight;

namespace Ushqyr.Tests
{
    /// <summary>
    /// EDU_PLAN F3 and F4: the autopilot flying on what the sensors say (GPS and barometer outdoors, optical flow
    /// indoors) while the physics runs on the truth, and what happens when a source fails.
    /// </summary>
    public sealed class PositionSourceTests
    {
        private sealed class World
        {
            public QuadProfile P = QuadProfile.Race5Inch;
            public PhysicsSettings Env = PhysicsSettings.Default;
            public QuadState S = QuadState.AtRest(Vec3.Zero, Quat.Identity);
            public readonly Autopilot Ap = new Autopilot();
            public readonly PositionEstimator Est;
            public PositionFaults Faults;
            public bool Armed = true;

            public World(PositionSourceSettings source, int seed) { Est = new PositionEstimator(source, seed); }

            public World Up(float height, double seconds)
            {
                Ap.Takeoff(S, height, 0f);
                Run(seconds);
                return this;
            }

            public void Run(double seconds, Action<World> each = null)
            {
                PilotInput input = default;
                int steps = (int)(seconds * 1000);
                for (int i = 0; i < steps; i++)
                {
                    if (i % 4 == 0)
                    {
                        QuadState believed = Est.Estimate(S, 0f, Faults, 0.004f);
                        var sensors = new AutopilotSensors { GroundY = 0f, Battery = -1f, RadioOk = true, PositionOk = Est.PositionOk };
                        input = Ap.Update(believed, new PilotInput { Arm = true, Throttle = 0.5f }, AssistMode.Position, sensors, P, Env, 0.004f);
                        Armed = input.Arm;
                        each?.Invoke(this);
                    }
                    QuadSimulator.Step(ref S, input, P, Env, 0.001f);
                    if (S.Position.y <= 0f)
                    {
                        S.Position = new Vec3(S.Position.x, 0f, S.Position.z);
                        if (S.Velocity.y < 0f) S.Velocity = new Vec3(S.Velocity.x * 0.5f, 0f, S.Velocity.z * 0.5f);
                        S.AngularVelocity = Vec3.Zero;
                        S.Orientation = Quat.Identity;
                    }
                }
            }
        }

        private static float Flat(Vec3 v) => new Vec3(v.x, 0f, v.z).Magnitude;

        [Test]
        public void Gps_HoverWandersButStaysNear_AndTheBarometerKeepsTheHeight()
        {
            double worst = 0, sum = 0;
            int n = 0;
            float low = 99f, high = 0f;
            for (int seed = 1; seed <= 3; seed++)
            {
                World w = new World(PositionSourceSettings.Outdoor, seed).Up(5f, 6);
                w.Run(90, x =>
                {
                    float d = Flat(x.S.Position);
                    worst = Math.Max(worst, d);
                    sum += d;
                    n++;
                    low = Math.Min(low, x.S.Position.y);
                    high = Math.Max(high, x.S.Position.y);
                });
            }
            Assert.Less(worst, 2.0, "furthest from the spot");
            Assert.Greater(sum / n, 0.1, "it does wander: this is not the perfect source");
            Assert.Greater(low, 4f);
            Assert.Less(high, 6f);
        }

        [Test]
        public void Errors_ComeFromTheSeed()
        {
            Vec3 Fly(int seed) => new World(PositionSourceSettings.Outdoor, seed).Up(5f, 20).S.Position;
            Assert.AreEqual(Fly(7), Fly(7));
            Assert.AreNotEqual(Fly(7), Fly(8));
        }

        [Test]
        public void Gps_Lost_GivesThePositionFailsafe_AndAShortGapDoesNot()
        {
            var w = new World(PositionSourceSettings.Outdoor, 3);
            w.Env.Wind = new Vec3(3f, 0f, 0f);
            w.Up(5f, 8);
            float x0 = w.S.Position.x;
            w.Faults.Lost = true;
            w.Run(0.5);
            Assert.AreEqual(FailsafeReason.None, w.Ap.Failsafe);
            w.Run(8);
            Assert.AreEqual(FailsafeReason.PositionLost, w.Ap.Failsafe);
            Assert.AreEqual(5f, w.S.Position.y, 1f, "height is kept on the barometer");
            Assert.Greater(w.S.Position.x - x0, 3f, "it goes with the wind");
            w.Faults.Lost = false;
            w.Run(6);
            Assert.AreEqual(FailsafeReason.None, w.Ap.Failsafe);
            Assert.Less(w.S.Velocity.Magnitude, 0.6f);
        }

        [Test]
        public void Gps_Degraded_WandersSeveralTimesFurther()
        {
            double Wander(float degraded)
            {
                var w = new World(PositionSourceSettings.Outdoor, 4);
                w.Faults.Degraded = degraded;
                w.Up(5f, 6);
                double worst = 0;
                w.Run(90, x => worst = Math.Max(worst, Flat(x.S.Position)));
                return worst;
            }
            Assert.Greater(Wander(5f), Wander(0f) * 2.5);
        }

        [Test]
        public void OpticalFlow_HoldsIndoors_AndIsBlindAboveItsRange()
        {
            var w = new World(PositionSourceSettings.Indoor, 2) { P = QuadProfile.Whoop65 };
            w.Up(1.5f, 5);
            Vec3 at = w.S.Position;
            double drift = 0;
            w.Run(30, x => drift = Math.Max(drift, Flat(x.S.Position - at)));
            Assert.Less(drift, 0.5, "drift in 30 s");
            Assert.Greater(drift, 0.02, "the sensor's bias shows as a slow drift");
            w.Ap.GoTo(new Vec3(0f, 5.5f, 0f));
            w.Run(8);
            Assert.AreEqual(FailsafeReason.PositionLost, w.Ap.Failsafe, "above 4 m the floor is out of range");
        }

        [Test]
        public void CompassInterference_TurnsTheDroneByTheError()
        {
            World w = new World(PositionSourceSettings.Perfect, 1).Up(4f, 5);
            w.Faults.HeadingOffsetDeg = 40f;
            w.Ap.GoTo(new Vec3(0f, 4f, 20f), 0f);
            w.Run(15);
            Assert.Less((w.S.Position - new Vec3(0f, 4f, 20f)).Magnitude, 0.5f, "position does not need the compass");
            Assert.AreEqual(320f, FlightAssist.HeadingDeg(w.S), 5f, "it believes it faces north; it faces 40 degrees off");
        }

        [Test]
        public void Landing_Works_WithAWrongIdeaOfTheHeight()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                World w = new World(PositionSourceSettings.Outdoor, seed).Up(4f, 30);
                w.Ap.Land();
                w.Run(25);
                Assert.IsTrue(w.Ap.Landed, "seed " + seed);
                Assert.IsFalse(w.Armed, "seed " + seed);
                Assert.Less(w.S.Position.y, 0.05f, "seed " + seed);
            }
        }
    }
}
