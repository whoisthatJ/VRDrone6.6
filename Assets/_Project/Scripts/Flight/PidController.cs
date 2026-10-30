using System;

namespace GateRush.Flight
{
    /// <summary>
    /// Three-axis rate PID. P on error, I with a limit, D on measurement through a
    /// first-order low-pass, optional feedforward on setpoint change.
    /// </summary>
    public static class PidController
    {
        /// <summary>Returns the per-axis command (mixer units). Axis order X = pitch, Y = yaw, Z = roll.</summary>
        public static Vec3 Update(ref PidState s, in PidSettings p, Vec3 setpoint, Vec3 gyro, float dt, bool active)
        {
            if (!s.Initialized)
            {
                s.PrevGyro = gyro;
                s.PrevSetpoint = setpoint;
                s.DFiltered = Vec3.Zero;
                s.Initialized = true;
            }

            // D-term on measurement, low-passed. Runs even when inactive so it doesn't kick on activation.
            Vec3 dRaw = (gyro - s.PrevGyro) / dt;
            float rc = 1f / (2f * MathF.PI * MathF.Max(p.DTermLowpassHz, 1f));
            float alpha = dt / (rc + dt);
            s.DFiltered = s.DFiltered + (dRaw - s.DFiltered) * alpha;
            Vec3 setpointDelta = (setpoint - s.PrevSetpoint) / dt;
            s.PrevGyro = gyro;
            s.PrevSetpoint = setpoint;

            if (!active)
            {
                s.Integral = Vec3.Zero;
                return Vec3.Zero;
            }

            Vec3 error = setpoint - gyro;
            s.Integral = new Vec3(
                Rates.Clamp(s.Integral.x + error.x * p.Pitch.I * dt, -p.ITermLimit, p.ITermLimit),
                Rates.Clamp(s.Integral.y + error.y * p.Yaw.I * dt, -p.ITermLimit, p.ITermLimit),
                Rates.Clamp(s.Integral.z + error.z * p.Roll.I * dt, -p.ITermLimit, p.ITermLimit));

            return new Vec3(
                Axis(p.Pitch, error.x, s.Integral.x, s.DFiltered.x, setpointDelta.x, p.OutputLimit),
                Axis(p.Yaw, error.y, s.Integral.y, s.DFiltered.y, setpointDelta.y, p.OutputLimit),
                Axis(p.Roll, error.z, s.Integral.z, s.DFiltered.z, setpointDelta.z, p.OutputLimit));
        }

        private static float Axis(in PidGains g, float error, float integral, float dGyro, float dSetpoint, float limit)
        {
            float output = g.P * error + integral - g.D * dGyro + g.FF * dSetpoint;
            return Rates.Clamp(output, -limit, limit);
        }
    }
}
