using System;
using Ushqyr.Flight;
using UnityEngine;

namespace Ushqyr.AI
{
    /// <summary>Where a planner wants the quad to be now: position, velocity, acceleration and which way to look.</summary>
    public struct BotReference
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Accel;
        /// <summary>Direction to point the nose (camera) along; projected onto the thrust plane.</summary>
        public Vector3 Heading;
    }

    /// <summary>Planner interface (P4): turns the bot's situation into a reference to follow.</summary>
    public interface IBotPlanner
    {
        BotReference Plan(Vector3 position, Vector3 velocity, float dt);
    }

    /// <summary>
    /// Trajectory follower and stick synthesiser (P4). A position / velocity PD with acceleration feed-forward gives the
    /// wanted acceleration; adding gravity and the drag the quad is feeling gives the thrust vector, whose direction is
    /// the wanted body up and whose length the wanted thrust. The attitude error becomes body rates (tilt first, yaw
    /// once the tilt is close), and <see cref="RateInverse"/> turns the rates into Acro sticks and the thrust into
    /// throttle. Everything then goes through the same rates, PID, mixer, motors and physics as the player's quad.
    /// </summary>
    public sealed class TrajectoryFollower
    {
        /// <summary>Global knobs for tuning experiments (harness only).</summary>
        public static class Tuning
        {
            public static float FeedForward = 0f;
            public static float TiltGain = 20f;
            public static float YawGain = 1.5f;
        }

        /// <summary>Natural frequency of the position loop (rad/s).</summary>
        public float Omega = 4.5f;
        public float Damping = 0.8f;
        /// <summary>Body rate per radian of tilt error (1/s).</summary>
        public float TiltGain = Tuning.TiltGain;
        public float YawGain = Tuning.YawGain;
        /// <summary>Highest body rate asked for (rad/s).</summary>
        public float MaxRate = 11f;
        /// <summary>Largest correction the PD adds on top of the feed-forward (m/s²).</summary>
        public float MaxCorrection = 30f;
        public float MaxPositionCorrection = 20f, MaxVelocityCorrection = 20f;

        private readonly QuadProfile _p;
        private readonly float _g, _maxThrustAccel;
        private Vector3 _prevUp, _ffRate;
        private bool _hasPrev;

        public TrajectoryFollower(in QuadProfile profile, float gravity = 9.81f)
        {
            _p = profile;
            _g = gravity;
            _maxThrustAccel = 4f * profile.Motors.MaxThrustPerMotor / Mathf.Max(0.01f, profile.Mass);
        }

        public QuadProfile Profile => _p;

        public void Reset() => _hasPrev = false;

        /// <summary>Sticks for the next control tick (Acro mode).</summary>
        public PilotInput Compute(in QuadState s, in BotReference r, float dt)
        {
            Vector3 p = ToU(s.Position), v = ToU(s.Velocity);
            Quaternion q = ToU(s.Orientation);
            Quaternion qi = Quaternion.Inverse(q);
            Vector3 up = q * Vector3.up;

            // Wanted acceleration.
            float kp = Omega * Omega, kd = 2f * Damping * Omega;
            // Position and velocity errors are limited apart, so a big speed error can't drown the height / line error.
            Vector3 cp = kp * (r.Position - p), cv = kd * (r.Velocity - v);
            if (cp.sqrMagnitude > MaxPositionCorrection * MaxPositionCorrection) cp = cp.normalized * MaxPositionCorrection;
            if (cv.sqrMagnitude > MaxVelocityCorrection * MaxVelocityCorrection) cv = cv.normalized * MaxVelocityCorrection;
            Vector3 corr = cp + cv;
            if (corr.sqrMagnitude > MaxCorrection * MaxCorrection) corr = corr.normalized * MaxCorrection;
            Vector3 a = r.Accel + corr;

            // Thrust vector: the acceleration plus gravity, minus the drag the frame feels now.
            Vec3 vBody = s.Orientation.Conjugate * s.Velocity;
            Vector3 drag = ToU(s.Orientation * AeroModel.DragForce(_p.Aero, vBody)) / _p.Mass;
            Vector3 g = new Vector3(0f, _g, 0f);
            Vector3 f = a + g - drag;
            float inflow = Mathf.Max(0f, vBody.y);
            float fall = _p.Motors.PropPitchSpeed > 0f ? Mathf.Clamp(1f - inflow / _p.Motors.PropPitchSpeed, 0.3f, 1f) : 1f;
            float fmax = _maxThrustAccel * fall * 0.92f;
            if (f.sqrMagnitude > fmax * fmax)
            {
                // Height first: keep the vertical part (weight + climb / sink), shorten the horizontal part to fit.
                float fy = Mathf.Clamp(f.y, -fmax * 0.9f, fmax * 0.9f);
                Vector3 hz = new Vector3(f.x, 0f, f.z);
                float room = Mathf.Sqrt(Mathf.Max(0f, fmax * fmax - fy * fy));
                if (hz.sqrMagnitude > room * room) hz = hz.normalized * room;
                f = new Vector3(hz.x, fy, hz.z);
            }

            // Wanted attitude: up along the thrust, nose along the heading.
            float fm = f.magnitude;
            Vector3 upD = fm > 1.5f ? f / fm : Vector3.Slerp(up, fm > 1e-3f ? f / fm : up, fm / 1.5f).normalized;
            Vector3 head = r.Heading.sqrMagnitude > 1e-4f ? r.Heading : q * Vector3.forward;
            Vector3 h = head - upD * Vector3.Dot(head, upD);
            if (h.sqrMagnitude < 1e-3f)
            {
                Vector3 fwd = q * Vector3.forward;
                h = fwd - upD * Vector3.Dot(fwd, upD);
            }
            h.Normalize();

            // Tilt: rotate the body up onto the wanted up.
            Vector3 ub = qi * upD;
            Vector3 axis = Vector3.Cross(Vector3.up, ub);
            float sinA = axis.magnitude;
            float angle = Mathf.Atan2(sinA, ub.y);
            Vector3 w = sinA > 1e-5f ? axis / sinA * (angle * TiltGain) : (ub.y < 0f ? Vector3.right * (Mathf.PI * TiltGain) : Vector3.zero);

            // Feed-forward: how fast the wanted up is turning (low-passed), in body axes, tilt only.
            if (_hasPrev && dt > 0f)
            {
                Vector3 rate = Vector3.Cross(_prevUp, upD) / dt;
                _ffRate = Vector3.Lerp(_ffRate, rate, 0.25f);
            }
            else _ffRate = Vector3.zero;
            _prevUp = upD;
            _hasPrev = true;
            Vector3 ff = qi * _ffRate;
            w += new Vector3(ff.x, 0f, ff.z) * Tuning.FeedForward;

            // Yaw: once the tilt is close, turn the nose onto the heading within the thrust plane.
            Vector3 hb = qi * h;
            float yawErr = Mathf.Atan2(hb.x, hb.z);
            w.y += yawErr * YawGain * Mathf.Clamp01(ub.y);

            if (w.sqrMagnitude > MaxRate * MaxRate) w = w.normalized * MaxRate;
            RateInverse.SticksFor(_p.Rates, new Vec3(w.x, w.y, w.z), out float pitch, out float yaw, out float roll);

            // Throttle: the thrust along the current body up.
            float thrust = _p.Mass * Mathf.Max(0f, Vector3.Dot(f, up));
            float throttle = RateInverse.ThrottleFor(_p.Motors, thrust, vBody.y);

            return new PilotInput
            {
                Throttle = throttle, Pitch = pitch, Yaw = yaw, Roll = roll, Arm = true, Mode = FlightMode.Acro,
            };
        }

        public static Vector3 ToU(Vec3 v) => new Vector3(v.x, v.y, v.z);
        public static Quaternion ToU(Quat q) => new Quaternion(q.x, q.y, q.z, q.w);
        public static Vec3 ToF(Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quat ToF(Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
    }
}
