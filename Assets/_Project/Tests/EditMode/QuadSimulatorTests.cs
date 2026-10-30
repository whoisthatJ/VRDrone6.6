using System;
using GateRush.Flight;
using NUnit.Framework;

namespace GateRush.Tests
{
    public class QuadSimulatorTests
    {
        private const float Dt = 0.001f;
        private const float RadToDeg = 180f / MathF.PI;

        private static QuadProfile Profile => QuadProfile.Race5Inch;

        private static float HoverThrottle(in QuadProfile p) =>
            MotorModel.HoverCommand(p.Motors, p.Mass * PhysicsSettings.Default.Gravity);

        /// <summary>Starts armed at the hover point with motors already spooled.</summary>
        private static QuadState Hovering(in QuadProfile p)
        {
            var s = QuadState.AtRest(new Vec3(0f, 10f, 0f), Quat.Identity);
            float speed = MotorModel.HoverSpeed(p.Motors, p.Mass * PhysicsSettings.Default.Gravity);
            for (int i = 0; i < 4; i++) s.MotorSpeed[i] = speed;
            s.Armed = true;
            return s;
        }

        private static void Run(ref QuadState s, PilotInput input, in QuadProfile p, float seconds)
        {
            int n = (int)MathF.Round(seconds / Dt);
            for (int i = 0; i < n; i++) QuadSimulator.Step(ref s, input, p, Dt);
        }

        [Test]
        public void Disarmed_FallsWithGravity()
        {
            var p = Profile;
            var s = QuadState.AtRest(new Vec3(0f, 100f, 0f), Quat.Identity);
            Run(ref s, default, p, 0.5f);
            // 0.5 * 9.81 * 0.25 = 1.226 m without drag (semi-implicit Euler gives 1.229); drag only reduces it.
            Assert.That(100f - s.Position.y, Is.InRange(1.15f, 1.235f));
            Assert.That(s.MotorSpeed.M0, Is.EqualTo(0f));
        }

        [Test]
        public void HoverThrottle_HoldsAltitudeAndAttitude()
        {
            var p = Profile;
            var s = Hovering(p);
            Run(ref s, PilotInput.Armed(HoverThrottle(p)), p, 3f);

            Assert.That(MathF.Abs(s.Position.y - 10f), Is.LessThan(0.05f), "altitude drift");
            Assert.That(s.Velocity.Magnitude, Is.LessThan(0.05f), "velocity");
            Assert.That(s.Up.y, Is.GreaterThan(0.9999f), "attitude");
            Assert.That(s.AngularVelocity.Magnitude, Is.LessThan(0.01f), "rotation");
        }

        [Test]
        public void HoverThrottle_IsPlausibleFor5InchRacer()
        {
            // A ~10:1 thrust-to-weight race quad hovers well under half throttle.
            Assert.That(HoverThrottle(Profile), Is.InRange(0.2f, 0.4f));
        }

        [TestCase(1f, 0f, 0f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, 0f, 1f)]
        public void RateStep_ReachesSetpointQuicklyWithoutLargeOvershoot(float roll, float pitch, float yaw)
        {
            var p = Profile;
            var s = Hovering(p);
            var input = PilotInput.Armed(HoverThrottle(p));
            input.Roll = roll * 0.5f;
            input.Pitch = pitch * 0.5f;
            input.Yaw = yaw * 0.5f;

            Vec3 target = QuadSimulator.ComputeSetpoint(s, input, p);
            float targetMag = target.Magnitude;
            Assert.That(targetMag * RadToDeg, Is.GreaterThan(100f));

            float riseTime = -1f;
            float peak = 0f;
            for (int i = 0; i < 400; i++)
            {
                QuadSimulator.Step(ref s, input, p, Dt);
                float along = Vec3.Dot(s.AngularVelocity, target) / targetMag;
                if (riseTime < 0f && along >= 0.9f * targetMag) riseTime = (i + 1) * Dt;
                peak = MathF.Max(peak, along);
            }

            float limit = yaw != 0f ? 0.2f : 0.1f;
            Assert.That(riseTime, Is.GreaterThan(0f).And.LessThan(limit), $"90% rise time {riseTime * 1000f:F0} ms");
            Assert.That(peak / targetMag, Is.LessThan(1.25f), "overshoot");

            float final = Vec3.Dot(s.AngularVelocity, target) / targetMag;
            Assert.That(final / targetMag, Is.InRange(0.95f, 1.05f), "steady-state");
        }

        [Test]
        public void RateSigns_MatchStickDirections()
        {
            var p = Profile;

            var s = Hovering(p);
            var input = PilotInput.Armed(HoverThrottle(p));
            input.Roll = 0.5f;
            Run(ref s, input, p, 0.2f);
            Assert.That(s.Right.y, Is.LessThan(-0.3f), "roll right lowers the right side");

            s = Hovering(p);
            input = PilotInput.Armed(HoverThrottle(p));
            input.Pitch = 0.5f;
            Run(ref s, input, p, 0.2f);
            Assert.That(s.Forward.y, Is.LessThan(-0.3f), "pitch forward lowers the nose");

            s = Hovering(p);
            input = PilotInput.Armed(HoverThrottle(p));
            input.Yaw = 0.5f;
            Run(ref s, input, p, 0.3f);
            Assert.That(s.Forward.x, Is.GreaterThan(0.3f), "yaw right turns the nose to +X");
        }

        [Test]
        public void PitchForward_AcceleratesForward()
        {
            var p = Profile;
            var s = Hovering(p);
            s.Orientation = Quat.EulerDegrees(30f, 0f, 0f);
            var input = PilotInput.Armed(0.5f);
            Run(ref s, input, p, 1f);
            Assert.That(s.Velocity.z, Is.GreaterThan(3f));
        }

        [Test]
        public void AngleMode_SettlesAtCommandedTilt()
        {
            var p = Profile;
            var s = Hovering(p);
            var input = PilotInput.Armed(HoverThrottle(p), FlightMode.Angle);
            input.Pitch = 0.5f;
            Run(ref s, input, p, 1.5f);

            float pitchDeg = -MathF.Asin(s.Forward.y) * RadToDeg;
            Assert.That(pitchDeg, Is.EqualTo(0.5f * p.AngleMode.MaxAngleDeg).Within(2f));
            Assert.That(MathF.Abs(s.Right.y), Is.LessThan(0.02f), "no roll");
        }

        [Test]
        public void AngleMode_CentreSticksLevelsFromTilt()
        {
            var p = Profile;
            var s = Hovering(p);
            s.Orientation = Quat.EulerDegrees(25f, 0f, -35f);
            Run(ref s, PilotInput.Armed(HoverThrottle(p), FlightMode.Angle), p, 1.5f);
            Assert.That(s.Up.y, Is.GreaterThan(0.999f));
        }

        [Test]
        public void Simulation_IsDeterministic()
        {
            var p = Profile;
            var a = Hovering(p);
            var b = Hovering(p);
            for (int i = 0; i < 3000; i++)
            {
                var input = PilotInput.Armed(0.3f + 0.2f * MathF.Sin(i * 0.01f));
                input.Roll = MathF.Sin(i * 0.003f);
                input.Pitch = MathF.Cos(i * 0.002f) * 0.7f;
                input.Yaw = 0.2f;
                QuadSimulator.Step(ref a, input, p, Dt);
                QuadSimulator.Step(ref b, input, p, Dt);
            }
            Assert.That(a.Position, Is.EqualTo(b.Position));
            Assert.That(a.Orientation, Is.EqualTo(b.Orientation));
            Assert.That(a.Velocity, Is.EqualTo(b.Velocity));
            Assert.That(a.AngularVelocity, Is.EqualTo(b.AngularVelocity));
        }

        [Test]
        public void Advance_UsesFixedSubStepsAndCapsThem()
        {
            var p = Profile;
            var env = PhysicsSettings.Default;
            var s = Hovering(p);
            float acc = 0f;

            int steps = QuadSimulator.Advance(ref s, PilotInput.Armed(0.3f), p, env, ref acc, 0.0139f);
            Assert.That(steps, Is.EqualTo(13));
            Assert.That(acc, Is.InRange(0f, env.FixedStep));

            steps = QuadSimulator.Advance(ref s, PilotInput.Armed(0.3f), p, env, ref acc, 0.1f);
            Assert.That(steps, Is.EqualTo(env.MaxSubSteps));
            Assert.That(acc, Is.EqualTo(0f), "a hitch drops the backlog");
        }

        [Test]
        public void Mixer_OutputsStayInRange_AndAirModeKeepsAuthorityAtZeroThrottle()
        {
            var big = Mixer.Mix(0.9f, new Vec3(0.8f, 0.5f, -0.9f), true);
            for (int i = 0; i < 4; i++) Assert.That(big[i], Is.InRange(0f, 1f));

            var air = Mixer.Mix(0f, new Vec3(0.2f, 0f, 0f), true);
            // Rear motors (0, 2) must be above front motors (1, 3) even at zero throttle.
            Assert.That(air.M0 - air.M1, Is.EqualTo(0.4f).Within(1e-5f));

            var noAir = Mixer.Mix(0f, new Vec3(0.2f, 0f, 0f), false);
            Assert.That(noAir.M1, Is.EqualTo(0f));
        }

        [Test]
        public void Quat_MatchesUnityEulerConvention()
        {
            // Unity: Quaternion.Euler(0, 90, 0) * forward = right; Euler(90, 0, 0) * forward = down.
            var yaw = Quat.EulerDegrees(0f, 90f, 0f) * Vec3.Forward;
            Assert.That(yaw.x, Is.EqualTo(1f).Within(1e-5f));
            var pitch = Quat.EulerDegrees(90f, 0f, 0f) * Vec3.Forward;
            Assert.That(pitch.y, Is.EqualTo(-1f).Within(1e-5f));
        }
    }
}
