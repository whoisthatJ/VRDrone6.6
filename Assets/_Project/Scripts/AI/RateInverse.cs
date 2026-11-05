using System;
using Ushqyr.Flight;

namespace Ushqyr.AI
{
    /// <summary>
    /// Inverse stick curves (P4): the stick deflection that makes the flight controller ask for a given rate. Bots fly
    /// through the same rates, PID and physics as the player, so they turn their wanted body rates into sticks here.
    /// Both curve types are odd and rise monotonically with the stick, so a bisection on [0, 1] finds the deflection.
    /// </summary>
    public static class RateInverse
    {
        private const int Iterations = 30;

        /// <summary>Stick (-1..1) for <paramref name="degPerSec"/> on one axis; rates beyond full stick give ±1.</summary>
        public static float StickFor(RatesType type, in AxisRates r, float degPerSec)
        {
            float target = MathF.Abs(degPerSec);
            if (target < 1e-6f) return 0f;
            float full = Rates.DegPerSec(type, r, 1f);
            if (target >= full) return MathF.Sign(degPerSec);
            float lo = 0f, hi = 1f;
            for (int i = 0; i < Iterations; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Rates.DegPerSec(type, r, mid) < target) lo = mid;
                else hi = mid;
            }
            return MathF.Sign(degPerSec) * 0.5f * (lo + hi);
        }

        /// <summary>
        /// Acro sticks for a body-rate setpoint (rad/s, body axes: X pitch nose-down, Y yaw right, Z roll left), the
        /// inverse of <see cref="QuadSimulator.ComputeSetpoint"/> in Acro mode.
        /// </summary>
        public static void SticksFor(in RateProfile rp, Vec3 bodyRate, out float pitch, out float yaw, out float roll)
        {
            const float RadToDeg = 180f / MathF.PI;
            pitch = StickFor(rp.Type, rp.Pitch, bodyRate.x * RadToDeg);
            yaw = StickFor(rp.Type, rp.Yaw, bodyRate.y * RadToDeg);
            roll = StickFor(rp.Type, rp.Roll, -bodyRate.z * RadToDeg);
        }

        /// <summary>
        /// Throttle stick (0..1) for a total thrust (N) at the current axial inflow (m/s, + = air from above): the inverse
        /// of thrust ∝ speed² with the inflow falloff, the idle floor and the throttle curve. Ignores motor lag and the
        /// mixer's air-mode shift (the bot's feedback absorbs both).
        /// </summary>
        public static float ThrottleFor(in MotorSettings m, float thrust, float axialInflow, float voltageScale = 1f)
        {
            float perMotor = MathF.Max(0f, thrust) / (4f * MathF.Max(voltageScale, 0.05f));
            float speed = MathF.Sqrt(perMotor / m.MaxThrustPerMotor);
            if (m.PropPitchSpeed > 0f)
                for (int i = 0; i < 4; i++)
                {
                    float falloff = Math.Clamp(1f - axialInflow / (m.PropPitchSpeed * MathF.Max(speed, 0.05f)), 0.05f, 1f);
                    speed = MathF.Sqrt(perMotor / (m.MaxThrustPerMotor * falloff));
                }
            float command = Math.Clamp((speed - m.IdleFraction) / (1f - m.IdleFraction), 0f, 1f);
            return m.ThrottleCurveExpo == 1f || command <= 0f ? command : MathF.Pow(command, 1f / m.ThrottleCurveExpo);
        }
    }
}
