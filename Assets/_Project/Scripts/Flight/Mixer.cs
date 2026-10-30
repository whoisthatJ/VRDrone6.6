using System;

namespace GateRush.Flight
{
    /// <summary>
    /// Quad-X mixer with Betaflight-style desaturation.
    /// Motor order (Betaflight): 0 = rear-right (CW), 1 = front-right (CCW), 2 = rear-left (CCW), 3 = front-left (CW), props-in.
    /// </summary>
    public static class Mixer
    {
        // Factors for body-axis commands (X = pitch nose-down, Y = yaw right, Z = roll left).
        // Pitch nose-down: rear motors up. Roll left: right motors up. Yaw right: CCW props up.
        private static readonly Vec3[] Factors =
        {
            new Vec3(+1f, -1f, +1f), // rear-right, CW
            new Vec3(-1f, +1f, +1f), // front-right, CCW
            new Vec3(+1f, +1f, -1f), // rear-left, CCW
            new Vec3(-1f, -1f, -1f), // front-left, CW
        };

        /// <summary>Body-frame motor position for motor i.</summary>
        public static Vec3 MotorPosition(int i, float armX, float armZ)
        {
            var f = Factors[i];
            // Roll factor sign = side (+ right), pitch factor sign = -front.
            return new Vec3(f.z * armX, 0f, -f.x * armZ);
        }

        /// <summary>Yaw direction of motor i's reaction torque: +1 for CCW props (they push the frame clockwise = yaw right).</summary>
        public static float YawSign(int i) => Factors[i].y;

        /// <summary>Mixes throttle (0..1) and per-axis commands into motor outputs in 0..1.</summary>
        public static Motor4 Mix(float throttle, Vec3 axisCommand, bool airMode)
        {
            var mix = new Motor4();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float m = Vec3.Dot(Factors[i], axisCommand);
                mix[i] = m;
                if (m < min) min = m;
                if (m > max) max = m;
            }

            // Desaturate: if the PID spread exceeds the full range, scale it down.
            float range = max - min;
            if (range > 1f)
            {
                float scale = 1f / range;
                for (int i = 0; i < 4; i++) mix[i] *= scale;
                min *= scale;
                max *= scale;
            }

            if (airMode)
            {
                // Shift throttle so the whole mix fits in 0..1, keeping full PID authority.
                throttle = Rates.Clamp(throttle, -min, 1f - max);
            }

            var outM = new Motor4();
            for (int i = 0; i < 4; i++) outM[i] = Rates.Clamp(throttle + mix[i], 0f, 1f);
            return outM;
        }
    }
}
