using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ushqyr.Flight;

namespace Ushqyr.Tests
{
    /// <summary>
    /// EDU_PLAN F3: the built-in autopilot and its failsafes, flown on the real flight model over a flat floor. Nothing
    /// here moves the drone except stick commands going through the flight controller.
    /// </summary>
    public sealed class AutopilotTests
    {
        private sealed class World
        {
            public QuadProfile P = QuadProfile.Race5Inch;
            public PhysicsSettings Env = PhysicsSettings.Default;
            public QuadState S = QuadState.AtRest(Vec3.Zero, Quat.Identity);
            public readonly Autopilot Ap = new Autopilot();
            public AutopilotSensors Sens = AutopilotSensors.Healthy(0f);
            public PilotInput Sticks = new PilotInput { Arm = true, Throttle = 0.5f };
            public double T;
            public bool Armed = true;
            public float Touchdown;

            public void Run(double seconds, Action<World> each = null)
            {
                PilotInput input = default;
                int steps = (int)(seconds * 1000);
                for (int i = 0; i < steps; i++)
                {
                    if (i % 4 == 0)
                    {
                        input = Ap.Update(S, Sticks, AssistMode.Position, Sens, P, Env, 0.004f);
                        Armed = input.Arm;
                        each?.Invoke(this);
                    }
                    float vy = S.Velocity.y;
                    QuadSimulator.Step(ref S, input, P, Env, 0.001f);
                    if (S.Position.y <= 0f)
                    {
                        // A flat floor: the drone cannot go below it and loses its speed on it.
                        if (S.Position.y < 0f && vy < -0.05f) Touchdown = Math.Max(Touchdown, -vy);
                        S.Position = new Vec3(S.Position.x, 0f, S.Position.z);
                        if (S.Velocity.y < 0f) S.Velocity = new Vec3(S.Velocity.x * 0.5f, 0f, S.Velocity.z * 0.5f);
                        S.AngularVelocity = Vec3.Zero;
                        S.Orientation = Quat.Identity;
                    }
                    T += 0.001;
                }
            }

            public World Airborne(float height)
            {
                Ap.Takeoff(S, height, 0f);
                Run(5);
                return this;
            }
        }

        private static float Flat(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, 0f, a.z - b.z).Magnitude;

        [Test]
        public void Takeoff_GoTo_AndAStaleVelocityCommandEndsInAHold()
        {
            var w = new World();
            w.Ap.Takeoff(w.S, 3f, 0f);
            w.Run(6);
            Assert.AreEqual(3f, w.S.Position.y, 0.2f);
            Assert.AreEqual(GuidedMode.Hold, w.Ap.Mode);
            Assert.Less(Flat(w.S.Position, Vec3.Zero), 0.3f, "straight up");

            w.Ap.GoTo(new Vec3(10f, 5f, 10f), 45f);
            w.Run(10);
            Assert.Less((w.S.Position - new Vec3(10f, 5f, 10f)).Magnitude, 0.2f);
            Assert.AreEqual(45f, FlightAssist.HeadingDeg(w.S), 3f);

            w.Ap.SetVelocity(new Vec3(3f, 0f, 0f));
            w.Run(0.8);
            Assert.Greater(w.S.Velocity.x, 1.5f, "flown while the command is fresh");
            w.Run(4);
            Assert.AreEqual(GuidedMode.Hold, w.Ap.Mode, "a program that stopped sending commands does not fly on");
            Assert.Less(w.S.Velocity.Magnitude, 0.3f);
        }

        [Test]
        public void Orbit_KeepsTheRadius_AndFacesTheCentre()
        {
            World w = new World().Airborne(4f);
            Vec3 centre = w.S.Position + new Vec3(0f, 0f, 6f);
            float min = 99f, max = 0f, aim = 0f;
            w.Ap.Orbit(w.S, centre, 6f, 3f);
            double start = w.T;
            w.Run(30, x =>
            {
                if (x.T - start < 5) return;
                float r = Flat(x.S.Position, centre);
                min = Math.Min(min, r);
                max = Math.Max(max, r);
                Vec3 to = centre - x.S.Position;
                float want = MathF.Atan2(to.x, to.z) * 57.29578f;
                aim = Math.Max(aim, Math.Abs(((want - FlightAssist.HeadingDeg(x.S)) % 360f + 540f) % 360f - 180f));
            });
            Assert.Greater(min, 5.2f);
            Assert.Less(max, 6.8f);
            Assert.Less(aim, 12f, "nose towards the centre");
        }

        [Test]
        public void Return_ClimbsComesHomeLandsGently_AndStaysDisarmed()
        {
            World w = new World().Airborne(3f);
            w.Ap.GoTo(new Vec3(12f, 4f, 9f));
            w.Run(8);
            float highest = 0f;
            w.Ap.Return();
            w.Run(45, x => highest = Math.Max(highest, x.S.Position.y));
            Assert.Greater(highest, w.Ap.ReturnHeight - 1f, "it climbs before coming home");
            Assert.IsTrue(w.Ap.Landed);
            Assert.Less(Flat(w.S.Position, Vec3.Zero), 0.6f, "on the take-off point");
            Assert.Less(w.Touchdown, 1f, "touchdown speed, m/s");
            Assert.IsFalse(w.Armed, "motors stay off although the pilot's arm switch is still on");
            w.Sticks.Arm = false;
            w.Run(0.1);
            w.Sticks.Arm = true;
            w.Run(0.1);
            Assert.IsTrue(w.Armed, "switching off and on again arms it");
        }

        [Test]
        public void Mission_FliesTheWaypoints_DoesTheActions_AndLands()
        {
            World w = new World().Airborne(4f);
            var actions = new List<string>();
            Assert.IsFalse(w.Ap.StartMission(new Waypoint[0]));
            Assert.IsTrue(w.Ap.StartMission(new[]
            {
                new Waypoint { Position = new Vec3(0f, 6f, 15f), HeadingDeg = float.NaN },
                new Waypoint { Position = new Vec3(12f, 6f, 15f), HeadingDeg = float.NaN, Action = WaypointAction.Photo, HoldSeconds = 1f },
                new Waypoint { Position = new Vec3(12f, 6f, 0f), HeadingDeg = float.NaN, Speed = 3f, Action = WaypointAction.Drop },
                new Waypoint { Position = new Vec3(0f, 4f, 0f), HeadingDeg = float.NaN, Action = WaypointAction.Land },
            }));
            w.Run(60, x =>
            {
                while (x.Ap.TryTakeAction(out int index, out WaypointAction action))
                    actions.Add(index + ":" + action + "@" + x.S.Position.x.ToString("0") + "," + x.S.Position.z.ToString("0"));
            });
            CollectionAssert.AreEqual(new[] { "1:Photo@12,15", "2:Drop@12,0" }, actions, "each action once, at its waypoint");
            Assert.AreEqual(4, w.Ap.MissionIndex);
            Assert.IsTrue(w.Ap.Landed);
            Assert.IsFalse(w.Armed);
            Assert.Less(Flat(w.S.Position, Vec3.Zero), 0.6f);
        }

        [Test]
        public void Failsafe_RadioLost_ReturnsAndLands_ButAShortDropoutIsIgnored()
        {
            World w = new World().Airborne(3f);
            w.Ap.GoTo(new Vec3(20f, 3f, 0f));
            w.Run(6);
            w.Sens.RadioOk = false;
            w.Run(1);
            Assert.AreEqual(FailsafeReason.None, w.Ap.Failsafe, "one second without the radio");
            w.Run(40);
            Assert.AreEqual(FailsafeReason.RadioLost, w.Ap.Failsafe);
            Assert.IsTrue(w.Ap.Landed);
            Assert.Less(Flat(w.S.Position, Vec3.Zero), 0.6f);
        }

        [Test]
        public void Failsafe_Battery_WarnsThenReturnsThenLandsWhereItIs()
        {
            World w = new World().Airborne(3f);
            w.Ap.GoTo(new Vec3(15f, 3f, 0f));
            w.Run(5);
            w.Sens.Battery = 0.2f;
            w.Run(0.1);
            Assert.IsTrue(w.Ap.BatteryWarning);
            Assert.AreEqual(GuidedMode.GoTo, w.Ap.Mode, "a warning changes nothing");
            w.Sens.Battery = 0.14f;
            w.Run(1);
            Assert.AreEqual(GuidedMode.Return, w.Ap.Mode);
            Assert.AreEqual(FailsafeReason.BatteryLow, w.Ap.Failsafe);
            w.Sens.Battery = 0.07f;
            w.Run(25);
            Assert.AreEqual(FailsafeReason.BatteryCritical, w.Ap.Failsafe);
            Assert.IsTrue(w.Ap.Landed);
            Assert.Greater(Flat(w.S.Position, Vec3.Zero), 3f, "it did not try to reach home on an empty battery");
        }

        [Test]
        public void Geofence_CutsGuidedTargets_AndStopsAPilotAtTheEdge()
        {
            var w = new World();
            w.Ap.FenceRadius = 10f;
            w.Ap.FenceHeight = 8f;
            w.Airborne(3f);
            w.Ap.GoTo(new Vec3(30f, 20f, 0f));
            w.Run(15);
            float out1 = Flat(w.S.Position, Vec3.Zero);
            Assert.Less(out1, 10f);
            Assert.Greater(out1, 9f, "it goes as far as the fence allows");
            Assert.Less(w.S.Position.y, 8f);

            w.Ap.GoTo(new Vec3(0f, 3f, 0f));
            w.Run(8);
            w.Ap.Manual();
            w.Sticks.Pitch = 1f;
            double furthest = 0;
            w.Run(12, x => furthest = Math.Max(furthest, Flat(x.S.Position, Vec3.Zero)));
            Assert.Less(furthest, 11.5, "a pilot at full speed is stopped near the edge");
            Assert.AreEqual(FailsafeReason.Geofence, w.Ap.Failsafe);
        }

        [Test]
        public void PositionLost_KeepsTheHeightAndDrifts_ThenRecovers()
        {
            var w = new World();
            w.Env.Wind = new Vec3(3f, 0f, 0f);
            w.Airborne(4f);
            float x0 = w.S.Position.x;
            w.Sens.PositionOk = false;
            w.Run(8);
            Assert.AreEqual(FailsafeReason.PositionLost, w.Ap.Failsafe);
            Assert.AreEqual(4f, w.S.Position.y, 0.5f);
            Assert.Greater(w.S.Position.x - x0, 3f, "it goes with the wind");
            w.Sens.PositionOk = true;
            w.Run(6);
            Assert.AreEqual(FailsafeReason.None, w.Ap.Failsafe);
            Assert.Less(w.S.Velocity.Magnitude, 0.4f);
        }

        [Test]
        public void MotorLoss_AWeakMotorIsFlownThrough_AStoppedOneBringsTheQuadDown()
        {
            World healthy = new World().Airborne(5f);
            World zero = new World();
            zero.S.MotorLoss = new Motor4();
            zero.Airborne(5f);
            Assert.AreEqual(healthy.S.Position, zero.S.Position, "no loss changes nothing");

            World weak = new World().Airborne(5f);
            weak.S.MotorLoss = new Motor4 { M1 = 0.3f };
            float tilt = 0f;
            weak.Run(3, x => tilt = Math.Max(tilt, MathF.Acos(Math.Min(1f, x.S.Up.y)) * 57.29578f));
            Assert.Less(tilt, 25f, "the controller makes up for a motor that is 30% down");
            Assert.Less(Flat(weak.S.Position, Vec3.Zero), 2f);
            weak.Ap.Land();
            weak.Run(20);
            Assert.IsTrue(weak.Ap.Landed);
            Assert.Less(weak.Touchdown, 1.2f);

            World dead = new World().Airborne(5f);
            dead.S.MotorLoss = new Motor4 { M1 = 1f };
            dead.Run(6);
            Assert.Less(dead.S.Position.y, 0.5f, "a quadcopter cannot fly on three motors");
        }

        [Test]
        public void ThePilotsDisarm_IsAlwaysObeyed_AndTheSameFlightRepeatsExactly()
        {
            World w = new World().Airborne(3f);
            w.Sticks.Arm = false;
            w.Run(0.1);
            Assert.IsFalse(w.Armed);
            Assert.AreEqual(GuidedMode.Manual, w.Ap.Mode);

            Vec3 Fly()
            {
                World v = new World().Airborne(3f);
                v.Ap.GoTo(new Vec3(7f, 5f, -4f), 200f);
                v.Run(6);
                return v.S.Position;
            }
            Assert.AreEqual(Fly(), Fly(), "deterministic");
        }
    }
}
