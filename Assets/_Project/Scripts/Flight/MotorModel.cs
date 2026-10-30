using System;

namespace GateRush.Flight
{
    /// <summary>First-order motor spool plus thrust ∝ ω² and reaction torque ∝ thrust.</summary>
    public static class MotorModel
    {
        /// <summary>Target speed fraction for a mixer command (0..1) when armed.</summary>
        public static float TargetSpeed(in MotorSettings m, float command, bool armed)
        {
            if (!armed) return 0f;
            return m.IdleFraction + (1f - m.IdleFraction) * Rates.Clamp(command, 0f, 1f);
        }

        /// <summary>Advances one motor's speed fraction towards its target with a first-order lag.</summary>
        public static float Spool(in MotorSettings m, float speed, float target, float dt)
        {
            float tau = target > speed ? m.TimeConstantUp : m.TimeConstantDown;
            float k = 1f - MathF.Exp(-dt / MathF.Max(tau, 1e-4f));
            return speed + (target - speed) * k;
        }

        /// <summary>Thrust (N) of one motor at the given speed fraction and axial inflow speed (m/s, + = air coming in from above).</summary>
        public static float Thrust(in MotorSettings m, float speed, float axialInflow)
        {
            float staticThrust = m.MaxThrustPerMotor * speed * speed;
            if (m.PropPitchSpeed <= 0f) return staticThrust;
            // Inflow reduces the prop's effective angle of attack. Descending (negative inflow) gives no bonus;
            // prop-wash effects are modelled separately later.
            float falloff = Rates.Clamp(1f - axialInflow / (m.PropPitchSpeed * MathF.Max(speed, 0.05f)), 0f, 1f);
            return staticThrust * falloff;
        }

        /// <summary>Throttle curve applied to the pilot's throttle stick.</summary>
        public static float ThrottleCurve(in MotorSettings m, float throttle)
        {
            throttle = Rates.Clamp(throttle, 0f, 1f);
            return m.ThrottleCurveExpo == 1f ? throttle : MathF.Pow(throttle, m.ThrottleCurveExpo);
        }

        /// <summary>Speed fraction at which four motors exactly support the given weight (N), ignoring inflow.</summary>
        public static float HoverSpeed(in MotorSettings m, float weight) => MathF.Sqrt(weight / (4f * m.MaxThrustPerMotor));

        /// <summary>Mixer command that yields the hover speed in steady state.</summary>
        public static float HoverCommand(in MotorSettings m, float weight) =>
            (HoverSpeed(m, weight) - m.IdleFraction) / (1f - m.IdleFraction);
    }
}
