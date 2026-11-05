using System;
using Ushqyr.Flight;

namespace Ushqyr.AI
{
    /// <summary>
    /// What a quad can do (P4), from its <see cref="QuadProfile"/>: the most acceleration along the flight path for a
    /// speed and climb angle (thrust with the prop-inflow falloff, gravity, drag), the most braking, the most sideways
    /// acceleration in a turn, and the top speed. The racing-line baker's speed profile stays inside these numbers, so a
    /// bot never asks for more than the player's quad could give.
    /// </summary>
    public sealed class FlightEnvelope
    {
        public const float SpeedStep = 0.5f;
        public const float SpeedCap = 60f;
        private const int Slopes = 13; // -90..90 in 15 degree steps

        private readonly float[,] _accel, _brake;
        private readonly float[] _lateral;
        private readonly int _speeds;

        public float Mass { get; }
        public float Gravity { get; }
        /// <summary>Static thrust of all four motors at full speed (N).</summary>
        public float StaticThrust { get; }
        /// <summary>Thrust to weight.</summary>
        public float ThrustToWeight => StaticThrust / (Mass * Gravity);
        /// <summary>Top speed in level flight (m/s).</summary>
        public float TopSpeed { get; }

        public FlightEnvelope(in QuadProfile p, float gravity = 9.81f)
        {
            Mass = MathF.Max(0.01f, p.Mass);
            Gravity = gravity;
            StaticThrust = 4f * p.Motors.MaxThrustPerMotor;
            _speeds = (int)(SpeedCap / SpeedStep) + 1;
            _accel = new float[_speeds, Slopes];
            _brake = new float[_speeds, Slopes];
            _lateral = new float[_speeds];
            float top = SpeedCap;
            var m = p.Motors;
            var aero = p.Aero;
            for (int i = 0; i < _speeds; i++)
            {
                float v = i * SpeedStep;
                for (int k = 0; k < Slopes; k++)
                {
                    float phi = (-90f + 15f * k) * MathF.PI / 180f;
                    float gAlong = Gravity * MathF.Sin(phi), gNormal = Gravity * MathF.Cos(phi);
                    float bestA = -1e9f, bestB = -1e9f;
                    // beta = angle between the thrust and the velocity (0 = pushing straight along the path).
                    for (int b = 0; b <= 90; b += 2)
                    {
                        float beta = b * MathF.PI / 180f;
                        float cb = MathF.Cos(beta), sb = MathF.Sin(beta);
                        // Pushing forward: air comes in through the props at v·cos(beta).
                        float fall = m.PropPitchSpeed > 0f ? Math.Clamp(1f - v * cb / m.PropPitchSpeed, 0f, 1f) : 1f;
                        float thrust = StaticThrust * fall / Mass;
                        float drag = Drag(aero, v, cb, sb) / Mass;
                        if (thrust * sb >= gNormal - 1e-3f) bestA = MathF.Max(bestA, thrust * cb - gAlong - drag);
                        // Braking: the thrust points back along the path; the props see no inflow loss.
                        float thrustB = StaticThrust / Mass;
                        if (thrustB * sb >= gNormal - 1e-3f) bestB = MathF.Max(bestB, thrustB * cb + gAlong + drag);
                    }
                    _accel[i, k] = bestA;
                    _brake[i, k] = MathF.Max(0f, bestB);
                }
                // Level turn: thrust holds the weight and the drag, the rest pulls sideways.
                float fallL = m.PropPitchSpeed > 0f ? Math.Clamp(1f - v * 0.15f / m.PropPitchSpeed, 0f, 1f) : 1f;
                float t = StaticThrust * fallL / Mass, d = Drag(aero, v, 0f, 1f) / Mass;
                _lateral[i] = MathF.Sqrt(MathF.Max(0f, t * t - Gravity * Gravity - d * d));
                if (top >= SpeedCap && _accel[i, Slopes / 2] <= 0f) top = v;
            }
            TopSpeed = top;
        }

        // Drag (N) at speed v, with the frame tilted so its thrust axis makes angle beta with the velocity: the airflow
        // hits the frame's top (body Y) by cos(beta) and its front (body Z) by sin(beta).
        private static float Drag(in AeroSettings a, float v, float cb, float sb)
        {
            float vy = v * cb, vz = v * sb;
            float dy = a.LinearDrag.y * vy + a.QuadraticDrag.y * vy * vy;
            float dz = a.LinearDrag.z * vz + a.QuadraticDrag.z * vz * vz;
            return dy * cb + dz * sb;
        }

        /// <summary>Most acceleration along the path (m/s²) at speed <paramref name="v"/>, climbing at <paramref name="slope"/> (rad, + up).</summary>
        public float Accel(float v, float slope) => Lookup(_accel, v, slope);

        /// <summary>Most braking (m/s², positive) at speed <paramref name="v"/> on a path at <paramref name="slope"/>.</summary>
        public float Brake(float v, float slope) => Lookup(_brake, v, slope);

        /// <summary>Most sideways (centripetal) acceleration in a level turn at speed <paramref name="v"/> (m/s²).</summary>
        public float Lateral(float v)
        {
            float f = Math.Clamp(v / SpeedStep, 0f, _speeds - 1.001f);
            int i = (int)f;
            return _lateral[i] + (_lateral[i + 1] - _lateral[i]) * (f - i);
        }

        private float Lookup(float[,] table, float v, float slope)
        {
            float f = Math.Clamp(v / SpeedStep, 0f, _speeds - 1.001f);
            float g = Math.Clamp((slope * 180f / MathF.PI + 90f) / 15f, 0f, Slopes - 1.001f);
            int i = (int)f, k = (int)g;
            float u = f - i, w = g - k;
            float a = table[i, k] + (table[i, k + 1] - table[i, k]) * w;
            float b = table[i + 1, k] + (table[i + 1, k + 1] - table[i + 1, k]) * w;
            return a + (b - a) * u;
        }
    }
}
