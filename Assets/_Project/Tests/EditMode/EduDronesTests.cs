using System;
using NUnit.Framework;
using Ushqyr.Flight;

namespace Ushqyr.Tests
{
    /// <summary>
    /// EDU_PLAN F3: every drone class of the Education Suite flies within its specification on the real flight model:
    /// a clean answer to a stick step, no drift in calm air, a stop at a point without flying past, a hold in wind,
    /// and a landing. The limits of the assist come from each drone's own profile.
    /// </summary>
    public sealed class EduDronesTests
    {
        private sealed class World
        {
            public QuadProfile P;
            public PhysicsSettings Env = PhysicsSettings.Default;
            public QuadState S = QuadState.AtRest(Vec3.Zero, Quat.Identity);
            public readonly Autopilot Ap = new Autopilot();
            public AutopilotSensors Sens = AutopilotSensors.Healthy(0f);
            public PilotInput Sticks = new PilotInput { Arm = true, Throttle = 0.5f };
            public double T;
            /// <summary>The sticks go straight to the flight controller (a person in Angle mode).</summary>
            public bool Raw;

            public World(QuadProfile p)
            {
                P = p;
                Ap.Settings = AssistTuning.For(p);
                Ap.CruiseSpeed = MathF.Min(5f, Ap.Settings.MaxSpeed * 0.7f);
            }

            public void Run(double seconds, Action<World> each = null)
            {
                PilotInput input = default;
                int steps = (int)(seconds * 1000);
                for (int i = 0; i < steps; i++)
                {
                    if (i % 4 == 0)
                    {
                        input = Raw ? Sticks : Ap.Update(S, Sticks, AssistMode.Position, Sens, P, Env, 0.004f);
                        each?.Invoke(this);
                    }
                    QuadSimulator.Step(ref S, input, P, Env, 0.001f);
                    if (S.Position.y <= 0f)
                    {
                        // A flat floor.
                        S.Position = new Vec3(S.Position.x, 0f, S.Position.z);
                        if (S.Velocity.y < 0f) S.Velocity = new Vec3(S.Velocity.x * 0.5f, 0f, S.Velocity.z * 0.5f);
                        S.AngularVelocity = Vec3.Zero;
                        S.Orientation = Quat.Identity;
                    }
                    T += 0.001;
                }
            }
        }

        private static float Flat(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, 0f, a.z - b.z).Magnitude;

        private static float RollDeg(in QuadState s)
        {
            Vec3 right = s.Orientation * new Vec3(1f, 0f, 0f);
            return -MathF.Asin(Math.Clamp(right.y, -1f, 1f)) * 57.29578f;
        }

        /// <summary>Each class with the wind it must hold position in (m/s): the camera drone's 8 m/s is the plan's own figure.</summary>
        private static readonly object[] Classes =
        {
            new object[] { "Camera 900", 8f },
            new object[] { "Camera 250", 5f },
            new object[] { "Delivery", 8f },
            new object[] { "Delivery + 1 kg", 8f },
            new object[] { "Edu", 3f },
        };

        private static QuadProfile Profile(string name)
        {
            if (name == "Delivery + 1 kg") return EduDrones.WithPayload(EduDrones.Delivery, 1f);
            foreach (QuadProfile p in EduDrones.All)
                if (p.Name == name) return p;
            throw new ArgumentException(name);
        }

        [TestCaseSource(nameof(Classes))]
        public void AngleStep_IsQuickAndDoesNotSwingPast(string name, float wind)
        {
            QuadProfile p = Profile(name);
            var w = new World(p);
            w.Ap.Takeoff(w.S, 5f, 0f);
            w.Run(8);
            Assert.AreEqual(5f, w.S.Position.y, 0.3f, "takes off to 5 m");

            w.Raw = true;
            float want = 0.5f * p.AngleMode.MaxAngleDeg, peak = 0f, rise = -1f, last = 0f;
            w.Sticks = new PilotInput { Arm = true, Throttle = FlightAssist.ThrottleFor(p, p.Mass * 9.81f), Roll = 0.5f, Mode = FlightMode.Angle };
            double t0 = w.T;
            w.Run(1.5, x =>
            {
                float roll = MathF.Abs(RollDeg(x.S));
                peak = MathF.Max(peak, roll);
                if (rise < 0f && roll >= 0.9f * want) rise = (float)(x.T - t0);
                last = roll;
            });
            Assert.That(rise, Is.InRange(0f, 0.6f), "reaches 90% of the angle within 0.6 s");
            Assert.Less(peak, want * 1.15f, "overshoot under 15%");
            Assert.AreEqual(want, last, 1.5f, "settles on the angle");
        }

        [TestCaseSource(nameof(Classes))]
        public void HoversGoesHoldsInWindAndLands(string name, float wind)
        {
            QuadProfile p = Profile(name);
            var w = new World(p);
            w.Ap.Takeoff(w.S, 3f, 0f);
            w.Run(8);
            Vec3 at = w.S.Position;
            float drift = 0f, bob = 0f;
            w.Run(30, x =>
            {
                drift = MathF.Max(drift, Flat(x.S.Position, at));
                bob = MathF.Max(bob, MathF.Abs(x.S.Position.y - 3f));
            });
            Assert.Less(drift, 0.3f, "hover drift in calm air");
            Assert.Less(bob, 0.3f, "height in calm air");

            var target = new Vec3(0f, 3f, 25f);
            float beyond = 0f;
            w.Ap.GoTo(target);
            w.Run(25, x => beyond = MathF.Max(beyond, x.S.Position.z - 25f));
            Assert.Less((w.S.Position - target).Magnitude, 0.3f, "arrives at a point 25 m away");
            Assert.Less(beyond, 1f, "does not fly past it by a metre");

            w.Env.Wind = new Vec3(wind, 0f, 0f);
            w.Ap.Hold(w.S);
            w.Run(15); // time to lean into the wind
            at = w.S.Position;
            drift = 0f;
            w.Run(15, x => drift = MathF.Max(drift, Flat(x.S.Position, at)));
            Assert.Less(drift, 0.5f, $"holds position in {wind} m/s wind");

            w.Env.Wind = Vec3.Zero;
            w.Ap.Land();
            w.Run(15);
            Assert.IsTrue(w.Ap.Landed, "lands");
            Assert.Less(w.S.Position.y, 0.05f);
        }

        [Test]
        public void AssistLimits_FollowTheDrone()
        {
            AssistSettings camera = AssistTuning.For(EduDrones.Camera900), edu = AssistTuning.For(EduDrones.EduSmall);
            AssistSettings loaded = AssistTuning.For(EduDrones.WithPayload(EduDrones.Delivery, 1f)), empty = AssistTuning.For(EduDrones.Delivery);
            Assert.Greater(camera.MaxSpeed, edu.MaxSpeed, "the camera drone is faster than the small one");
            Assert.LessOrEqual(edu.MaxTiltDeg, EduDrones.EduSmall.AngleMode.MaxAngleDeg, "never leans further than the drone allows");
            Assert.Less(loaded.MaxClimb, empty.MaxClimb, "a loaded drone climbs slower");
            Assert.Greater(EduDrones.ThrustToWeight(EduDrones.WithPayload(EduDrones.Delivery, 1f)), 1.5f, "the delivery drone lifts its payload with thrust to spare");
            Assert.AreEqual(EduDrones.Delivery.Mass, EduDrones.WithPayload(EduDrones.Delivery, 0f).Mass, "no payload, no change");

            // A drone with barely enough thrust is kept nearly level, and a made-up profile does not break the sums.
            QuadProfile weak = EduDrones.Camera250;
            weak.Motors.MaxThrustPerMotor = weak.Mass * 9.81f * 1.2f / 4f;
            Assert.AreEqual(8f, AssistTuning.For(weak).MaxTiltDeg, 0.01f);
            Assert.IsFalse(float.IsNaN(AssistTuning.For(default).MaxSpeed));
        }

        [Test]
        public void EveryClass_StaysInTheAirLongEnoughForALesson()
        {
            // Hover current from the battery model's own load curve must give minutes, not seconds.
            foreach (QuadProfile p in EduDrones.All)
            {
                var w = new World(p);
                w.Ap.Takeoff(w.S, 3f, 0f);
                w.Run(60);
                float perMinute = w.S.BatteryUsedMah;
                Assert.Greater(perMinute, 0f, p.Name);
                float minutes = p.Battery.CapacityMah / perMinute;
                Assert.That(minutes, Is.InRange(8f, 60f), p.Name + " hover time in minutes");
            }
        }
    }
}
