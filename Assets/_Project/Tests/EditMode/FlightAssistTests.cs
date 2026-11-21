using System;
using NUnit.Framework;
using Ushqyr.Flight;

namespace Ushqyr.Tests
{
    /// <summary>
    /// EDU_PLAN F3: assisted flight (Position, Altitude hold) and guided flight (velocity, go to a point) flown on the
    /// real flight model at 1 kHz with the assist called 250 times a second. The drone's true state is used; position
    /// sources with errors come later in the plan.
    /// </summary>
    public sealed class FlightAssistTests
    {
        private delegate PilotInput Control(ref AssistState a, in QuadState s, double t);

        private static QuadState Run(in QuadProfile p, in PhysicsSettings env, QuadState s, double seconds, Control control, Action<QuadState, double> watch = null)
        {
            var a = new AssistState();
            PilotInput input = default;
            int steps = (int)(seconds * 1000);
            for (int i = 0; i < steps; i++)
            {
                if (i % 4 == 0) input = control(ref a, s, i * 0.001);
                QuadSimulator.Step(ref s, input, p, env, 0.001f);
                if (watch != null && i % 10 == 0) watch(s, i * 0.001);
            }
            return s;
        }

        private static QuadState Start(Vec3 velocity)
        {
            QuadState s = QuadState.AtRest(new Vec3(0f, 10f, 0f), Quat.Identity);
            s.Armed = true;
            s.Velocity = velocity;
            return s;
        }

        private static QuadProfile Profile(string name)
        {
            switch (name)
            {
                case "race5": return QuadProfile.Race5Inch;
                case "freestyle5": return QuadProfile.Freestyle5Inch;
                default: return QuadProfile.Whoop65;
            }
        }

        private static readonly PilotInput HandsOff = new PilotInput { Arm = true, Throttle = 0.5f };

        [TestCase("race5")]
        [TestCase("freestyle5")]
        [TestCase("whoop65")]
        public void Position_HandsOff_StopsAndDriftsUnder30cm(string quad)
        {
            QuadProfile p = Profile(quad);
            PhysicsSettings env = PhysicsSettings.Default;
            AssistSettings c = AssistSettings.Default;
            Vec3 hold = default;
            double drift = 0;
            QuadState s = Run(p, env, Start(new Vec3(3f, 0f, 4f)), 40,
                (ref AssistState a, in QuadState q, double t) => FlightAssist.Pilot(ref a, q, HandsOff, AssistMode.Position, p, c, env, 0.004f),
                (q, t) =>
                {
                    if (t > 8 && t < 8.02) hold = q.Position;
                    if (t > 8.02) drift = Math.Max(drift, (q.Position - hold).Magnitude);
                });
            Assert.Less(drift, 0.3, "drift over 32 s in calm air");
            Assert.AreEqual(10f, s.Position.y, 1f, "height kept through the stop");
            Assert.Less(new Vec3(s.Position.x, 0f, s.Position.z).Magnitude, 5f, "stopped within 5 m from 5 m/s");
        }

        // A 32 g whoop cannot stand still in 8 m/s of wind at 30 degrees of tilt; that is physics, not a fault of the assist.
        [TestCase("race5", 8f)]
        [TestCase("freestyle5", 8f)]
        [TestCase("whoop65", 4f)]
        public void Position_HoldsTheSpotInWind(string quad, float wind)
        {
            QuadProfile p = Profile(quad);
            PhysicsSettings env = PhysicsSettings.Default;
            env.Wind = new Vec3(wind, 0f, 0f);
            AssistSettings c = AssistSettings.Default;
            double furthest = 0;
            Run(p, env, Start(Vec3.Zero), 40,
                (ref AssistState a, in QuadState q, double t) => FlightAssist.Pilot(ref a, q, HandsOff, AssistMode.Position, p, c, env, 0.004f),
                (q, t) => { if (t > 15) furthest = Math.Max(furthest, new Vec3(q.Position.x, q.Position.y - 10f, q.Position.z).Magnitude); });
            Assert.Less(furthest, 0.5, "distance from the spot after it has leaned into the wind");
        }

        [TestCase("race5")]
        [TestCase("freestyle5")]
        [TestCase("whoop65")]
        public void GoTo_ArrivesStaysAndTurnsToTheHeading(string quad)
        {
            QuadProfile p = Profile(quad);
            PhysicsSettings env = PhysicsSettings.Default;
            AssistSettings c = AssistSettings.Default;
            var target = new Vec3(12f, 15f, 16f);
            double arrived = -1, after = 0;
            QuadState s = Run(p, env, Start(Vec3.Zero), 30,
                (ref AssistState a, in QuadState q, double t) => FlightAssist.GoTo(ref a, q, target, 90f, 6f, p, c, env, 0.004f),
                (q, t) =>
                {
                    double d = (q.Position - target).Magnitude;
                    if (arrived < 0 && d < 0.3) arrived = t;
                    if (arrived >= 0) after = Math.Max(after, d);
                });
            Assert.Greater(arrived, 0, "it arrives");
            Assert.Less(arrived, 12, "in reasonable time for 20 m");
            Assert.Less(after, 0.5, "no wandering off after arriving");
            Assert.Less((s.Position - target).Magnitude, 0.15f);
            Assert.AreEqual(90f, FlightAssist.HeadingDeg(s), 3f);
        }

        [TestCase("race5")]
        [TestCase("whoop65")]
        public void Velocity_IsFlownAsAsked(string quad)
        {
            QuadProfile p = Profile(quad);
            PhysicsSettings env = PhysicsSettings.Default;
            AssistSettings c = AssistSettings.Default;
            QuadState s = Run(p, env, Start(Vec3.Zero), 12,
                (ref AssistState a, in QuadState q, double t) => FlightAssist.Velocity(ref a, q, new Vec3(0f, 1f, 4f), 0f, p, c, env, 0.004f));
            Assert.AreEqual(4f, s.Velocity.z, 0.3f);
            Assert.AreEqual(1f, s.Velocity.y, 0.2f);
            Assert.AreEqual(0f, s.Velocity.x, 0.2f);
        }

        [TestCase("race5")]
        [TestCase("whoop65")]
        public void AltitudeHold_KeepsTheHeightWhileThePilotFliesForward(string quad)
        {
            QuadProfile p = Profile(quad);
            PhysicsSettings env = PhysicsSettings.Default;
            AssistSettings c = AssistSettings.Default;
            var push = new PilotInput { Arm = true, Throttle = 0.5f, Pitch = 0.4f };
            double worst = 0;
            QuadState s = Run(p, env, Start(Vec3.Zero), 15,
                (ref AssistState a, in QuadState q, double t) => FlightAssist.Pilot(ref a, q, push, AssistMode.AltitudeHold, p, c, env, 0.004f),
                (q, t) => { if (t > 1) worst = Math.Max(worst, Math.Abs(q.Position.y - 10)); });
            Assert.Less(worst, 0.5);
            Assert.Greater(s.Velocity.z, 3f, "the tilt is the pilot's: it flies forward");
        }

        [Test]
        public void Position_FullStickGivesTheTopSpeed_AndReleaseStops()
        {
            QuadProfile p = QuadProfile.Race5Inch;
            PhysicsSettings env = PhysicsSettings.Default;
            AssistSettings c = AssistSettings.Default;
            Vec3 top = default;
            QuadState s = Run(p, env, Start(Vec3.Zero), 10,
                (ref AssistState a, in QuadState q, double t) =>
                    FlightAssist.Pilot(ref a, q, new PilotInput { Arm = true, Throttle = 0.5f, Pitch = t < 6 ? 1f : 0f }, AssistMode.Position, p, c, env, 0.004f),
                (q, t) => { if (t > 5.9 && t < 5.92) top = q.Velocity; });
            Assert.AreEqual(c.MaxSpeed, top.z, 1f);
            Assert.Less(s.Velocity.Magnitude, 0.3f, "4 s after letting go");
        }

        [Test]
        public void Off_PassesTheSticksThrough_AndTheSameInputsGiveTheSameFlight()
        {
            QuadProfile p = QuadProfile.Race5Inch;
            PhysicsSettings env = PhysicsSettings.Default;
            AssistSettings c = AssistSettings.Default;
            var sticks = new PilotInput { Arm = true, Throttle = 0.37f, Pitch = 0.4f, Roll = -0.2f, Yaw = 0.1f, Mode = FlightMode.Acro };
            var a = new AssistState();
            PilotInput o = FlightAssist.Pilot(ref a, Start(Vec3.Zero), sticks, AssistMode.Off, p, c, env, 0.004f);
            Assert.AreEqual(sticks.Throttle, o.Throttle);
            Assert.AreEqual(sticks.Pitch, o.Pitch);
            Assert.AreEqual(FlightMode.Acro, o.Mode);

            Control fly = (ref AssistState st, in QuadState q, double t) => FlightAssist.GoTo(ref st, q, new Vec3(5f, 12f, 5f), float.NaN, 5f, p, c, env, 0.004f);
            Assert.AreEqual(Run(p, env, Start(Vec3.Zero), 5, fly).Position, Run(p, env, Start(Vec3.Zero), 5, fly).Position, "deterministic");
        }

        [Test]
        public void ThrottleAndYawStick_InvertTheControllersCurves()
        {
            foreach (QuadProfile p in new[] { QuadProfile.Race5Inch, QuadProfile.Freestyle5Inch, QuadProfile.Whoop65 })
            {
                float weight = p.Mass * 9.81f;
                float stick = FlightAssist.ThrottleFor(p, weight);
                float command = MotorModel.ThrottleCurve(p.Motors, stick);
                Assert.AreEqual(MotorModel.HoverCommand(p.Motors, weight), command, 1e-3f, p.Name + ": the hover throttle");
                foreach (float rate in new[] { 30f, 90f, -120f })
                    Assert.AreEqual(rate, Rates.DegPerSec(p.Rates.Type, p.Rates.Yaw, FlightAssist.YawStick(p, rate)), 0.5f, p.Name + ": yaw rate");
            }
        }
    }
}
