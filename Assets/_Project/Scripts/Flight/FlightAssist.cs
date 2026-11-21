using System;

namespace Ushqyr.Flight
{
    /// <summary>How much the drone helps the pilot. Above these come the flight controller's own Angle, Horizon and Acro.</summary>
    public enum AssistMode
    {
        /// <summary>No help: the sticks go to the flight controller as they are.</summary>
        Off = 0,
        /// <summary>The throttle stick sets climb and descent; centred, the drone keeps its height. Tilt is the pilot's.</summary>
        AltitudeHold = 1,
        /// <summary>The sticks set speed over the ground; hands off, the drone stops and stays where it is.</summary>
        Position = 2,
    }

    /// <summary>Limits and gains of the assisted modes. Defaults suit a camera drone; a profile may carry its own.</summary>
    [Serializable]
    public struct AssistSettings
    {
        /// <summary>Top speed over the ground with full stick (m/s).</summary>
        public float MaxSpeed;
        public float MaxClimb;
        public float MaxDescent;
        /// <summary>Largest tilt the assist will ask for (degrees).</summary>
        public float MaxTiltDeg;
        /// <summary>Yaw rate with full stick (deg/s).</summary>
        public float MaxYawRate;
        /// <summary>Position error to speed (1/s).</summary>
        public float PositionGain;
        /// <summary>Speed error to acceleration (1/s), and how fast a steady error (wind) is leaned into (1/s²).</summary>
        public float VelocityGain;
        public float VelocityIntegral;
        public float ClimbGain;
        public float ClimbIntegral;
        /// <summary>Heading error to yaw rate (1/s).</summary>
        public float HeadingGain;
        /// <summary>Stick travel around the centre that counts as "hands off".</summary>
        public float Deadband;

        public static AssistSettings Default => new AssistSettings
        {
            MaxSpeed = 8f,
            MaxClimb = 3f,
            MaxDescent = 2f,
            MaxTiltDeg = 30f,
            MaxYawRate = 120f,
            PositionGain = 1.2f,
            VelocityGain = 3f,
            VelocityIntegral = 1.5f,
            ClimbGain = 5f,
            ClimbIntegral = 3f,
            HeadingGain = 3f,
            Deadband = 0.08f,
        };
    }

    /// <summary>What the assist remembers between steps.</summary>
    public struct AssistState
    {
        public Vec3 HoldPosition;
        public bool Holding;
        public float HoldHeight;
        public bool HoldingHeight;
        /// <summary>Acceleration the drone has learnt it needs to stand still (it leans into the wind by this much).</summary>
        public Vec3 Lean;
        public float Lift;
    }

    /// <summary>
    /// Assisted flight and the base of the autopilot (EDU_PLAN F3, decision D3). It is an outer loop: it reads the
    /// drone's state and produces ordinary stick commands in Angle mode for the existing flight controller. The physics
    /// and the inner loops are untouched, so a person, a bot and a student's program all fly the same drone.
    /// </summary>
    public static class FlightAssist
    {
        private const float Rad2Deg = 180f / MathF.PI;

        /// <summary>The pilot's sticks in an assisted mode, turned into sticks for the flight controller.</summary>
        public static PilotInput Pilot(ref AssistState a, in QuadState s, in PilotInput sticks, AssistMode mode,
            in QuadProfile p, in AssistSettings c, in PhysicsSettings env, float dt)
        {
            if (mode == AssistMode.Off || !sticks.Arm)
            {
                a = default;
                return sticks;
            }
            float climbStick = Dead((sticks.Throttle - 0.5f) * 2f, c.Deadband);
            float climb = climbStick > 0f ? climbStick * c.MaxClimb : climbStick * c.MaxDescent;
            float yawRate = Dead(sticks.Yaw, c.Deadband) * c.MaxYawRate;

            if (mode == AssistMode.AltitudeHold)
            {
                a.Holding = false;
                float throttle = Height(ref a, s, climb, climbStick != 0f, p, c, env, dt);
                PilotInput o = sticks;
                o.Mode = FlightMode.Angle;
                o.Throttle = throttle;
                o.Yaw = YawStick(p, yawRate);
                return o;
            }

            // Position: the sticks ask for a speed in the direction the drone faces.
            float fwd = Dead(sticks.Pitch, c.Deadband), right = Dead(sticks.Roll, c.Deadband);
            Flat(s, out Vec3 f, out Vec3 r);
            Vec3 want = (f * fwd + r * right) * c.MaxSpeed;
            if (fwd != 0f || right != 0f) a.Holding = false;
            else
            {
                Vec3 flatVel = new Vec3(s.Velocity.x, 0f, s.Velocity.z);
                if (!a.Holding && flatVel.Magnitude < 0.7f)
                {
                    a.Holding = true; // it has stopped: this is the spot to keep
                    a.HoldPosition = s.Position;
                }
                if (a.Holding)
                {
                    Vec3 e = a.HoldPosition - s.Position;
                    want = Limit(new Vec3(e.x, 0f, e.z) * c.PositionGain, c.MaxSpeed);
                }
            }
            return Fly(ref a, s, want, climb, climbStick != 0f, yawRate, sticks, p, c, env, dt);
        }

        /// <summary>Guided flight by speed: fly this velocity (world axes, Y up) and turn at this rate.</summary>
        public static PilotInput Velocity(ref AssistState a, in QuadState s, Vec3 velocity, float yawRateDeg,
            in QuadProfile p, in AssistSettings c, in PhysicsSettings env, float dt)
        {
            a.Holding = false;
            Vec3 flat = Limit(new Vec3(velocity.x, 0f, velocity.z), c.MaxSpeed);
            float climb = Clamp(velocity.y, -c.MaxDescent, c.MaxClimb);
            return Fly(ref a, s, flat, climb, true, Clamp(yawRateDeg, -c.MaxYawRate, c.MaxYawRate), PilotInput.Armed(0f, FlightMode.Angle), p, c, env, dt);
        }

        /// <summary>
        /// Guided flight to a point: go there at up to <paramref name="speed"/> m/s and stay. With a heading (degrees,
        /// 0 = +Z, clockwise from above) the drone turns to it; with NaN it keeps the heading it has.
        /// </summary>
        public static PilotInput GoTo(ref AssistState a, in QuadState s, Vec3 target, float headingDeg, float speed,
            in QuadProfile p, in AssistSettings c, in PhysicsSettings env, float dt)
        {
            Vec3 e = target - s.Position;
            Vec3 flat = Limit(new Vec3(e.x, 0f, e.z) * c.PositionGain, MathF.Min(MathF.Max(speed, 0.1f), c.MaxSpeed));
            float climb = Clamp(e.y * c.PositionGain, -c.MaxDescent, c.MaxClimb);
            float yawRate = 0f;
            if (!float.IsNaN(headingDeg))
            {
                float err = (headingDeg - HeadingDeg(s)) % 360f;
                if (err > 180f) err -= 360f;
                if (err < -180f) err += 360f;
                yawRate = Clamp(err * c.HeadingGain, -c.MaxYawRate, c.MaxYawRate);
            }
            a.Holding = true;
            a.HoldPosition = target;
            a.HoldingHeight = false;
            return Fly(ref a, s, flat, climb, true, yawRate, PilotInput.Armed(0f, FlightMode.Angle), p, c, env, dt);
        }

        /// <summary>Heading of the drone in degrees: 0 = +Z, growing clockwise seen from above.</summary>
        public static float HeadingDeg(in QuadState s)
        {
            Vec3 f = s.Forward;
            float deg = MathF.Atan2(f.x, f.z) * Rad2Deg;
            return deg < 0f ? deg + 360f : deg;
        }

        private static PilotInput Fly(ref AssistState a, in QuadState s, Vec3 flatVelocity, float climb, bool climbing, float yawRateDeg,
            in PilotInput like, in QuadProfile p, in AssistSettings c, in PhysicsSettings env, float dt)
        {
            float g = env.Gravity;
            float maxAccel = g * MathF.Tan(c.MaxTiltDeg / Rad2Deg);
            Vec3 err = flatVelocity - new Vec3(s.Velocity.x, 0f, s.Velocity.z);
            Vec3 accel = err * c.VelocityGain + a.Lean;
            // The lean is only learnt while the request is within reach, so a long push against the limit does not wind it up.
            if (accel.Magnitude < maxAccel) a.Lean = Limit(a.Lean + err * (c.VelocityIntegral * dt), maxAccel);
            accel = Limit(accel, maxAccel);

            Flat(s, out Vec3 f, out Vec3 r);
            float pitchDeg = MathF.Atan(Vec3.Dot(accel, f) / g) * Rad2Deg; // nose down to go forward
            float rollDeg = MathF.Atan(Vec3.Dot(accel, r) / g) * Rad2Deg;
            float full = MathF.Max(p.AngleMode.MaxAngleDeg, 1f);

            PilotInput o = like;
            o.Arm = true;
            o.Mode = FlightMode.Angle;
            o.Pitch = Clamp(pitchDeg / full, -1f, 1f);
            o.Roll = Clamp(rollDeg / full, -1f, 1f);
            o.Yaw = YawStick(p, yawRateDeg);
            o.Throttle = Height(ref a, s, climb, climbing, p, c, env, dt);
            return o;
        }

        /// <summary>The throttle stick that gives the wanted climb rate, or keeps the height when no climb is asked for.</summary>
        private static float Height(ref AssistState a, in QuadState s, float climb, bool climbing,
            in QuadProfile p, in AssistSettings c, in PhysicsSettings env, float dt)
        {
            if (climbing) a.HoldingHeight = false;
            else
            {
                if (!a.HoldingHeight && MathF.Abs(s.Velocity.y) < 0.5f)
                {
                    a.HoldingHeight = true;
                    a.HoldHeight = s.Position.y;
                }
                climb = a.HoldingHeight ? Clamp((a.HoldHeight - s.Position.y) * c.PositionGain * 1.5f, -c.MaxDescent, c.MaxClimb) : 0f;
            }
            float err = climb - s.Velocity.y;
            float accel = err * c.ClimbGain + a.Lift;
            float thrust = p.Mass * (env.Gravity + accel) / MathF.Max(s.Up.y, 0.5f); // more thrust when tilted
            float max = 4f * p.Motors.MaxThrustPerMotor;
            if (thrust > 0f && thrust < max) a.Lift = Clamp(a.Lift + err * c.ClimbIntegral * dt, -env.Gravity, env.Gravity);
            return ThrottleFor(p, Clamp(thrust, 0f, max));
        }

        /// <summary>The throttle stick position at which the four motors give this much thrust in still air.</summary>
        public static float ThrottleFor(in QuadProfile p, float thrust)
        {
            MotorSettings m = p.Motors;
            float speed = MathF.Sqrt(MathF.Max(thrust, 0f) / (4f * m.MaxThrustPerMotor));
            float command = Clamp((speed - m.IdleFraction) / MathF.Max(1f - m.IdleFraction, 1e-3f), 0f, 1f);
            return m.ThrottleCurveExpo == 1f || m.ThrottleCurveExpo <= 0f ? command : MathF.Pow(command, 1f / m.ThrottleCurveExpo);
        }

        /// <summary>The yaw stick position that asks the flight controller for this yaw rate (found by halving; the rate curve only grows).</summary>
        public static float YawStick(in QuadProfile p, float degPerSec)
        {
            if (degPerSec == 0f) return 0f;
            float sign = degPerSec < 0f ? -1f : 1f, want = MathF.Abs(degPerSec);
            if (Rates.DegPerSec(p.Rates.Type, p.Rates.Yaw, 1f) <= want) return sign;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Rates.DegPerSec(p.Rates.Type, p.Rates.Yaw, mid) < want) lo = mid;
                else hi = mid;
            }
            return sign * (lo + hi) * 0.5f;
        }

        /// <summary>The drone's forward and right directions flattened to the ground.</summary>
        private static void Flat(in QuadState s, out Vec3 forward, out Vec3 right)
        {
            Vec3 f = s.Forward;
            f = new Vec3(f.x, 0f, f.z);
            if (f.SqrMagnitude < 1e-6f)
            {
                // Pointing straight up or down: take the direction from the drone's top instead.
                Vec3 u = s.Up;
                f = new Vec3(-u.x, 0f, -u.z);
                if (f.SqrMagnitude < 1e-6f) f = Vec3.Forward;
            }
            forward = f.Normalized;
            right = new Vec3(forward.z, 0f, -forward.x);
        }

        private static float Dead(float v, float band)
        {
            float m = MathF.Abs(v);
            if (m <= band) return 0f;
            return MathF.Sign(v) * MathF.Min((m - band) / MathF.Max(1f - band, 1e-3f), 1f);
        }

        private static Vec3 Limit(Vec3 v, float max)
        {
            float m = v.Magnitude;
            return m > max && m > 0f ? v * (max / m) : v;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
