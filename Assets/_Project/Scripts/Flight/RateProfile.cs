using System;

namespace GateRush.Flight
{
    public enum RatesType
    {
        Betaflight = 0,
        Actual = 1,
    }

    /// <summary>Per-axis rate curve parameters, using the same units as Betaflight's UI.</summary>
    [Serializable]
    public struct AxisRates
    {
        /// <summary>Betaflight: RC Rate (e.g. 1.0). Actual: Center Sensitivity in deg/s (e.g. 70).</summary>
        public float RcRate;
        /// <summary>Betaflight: Super Rate (e.g. 0.7). Actual: Max Rate in deg/s (e.g. 670).</summary>
        public float Rate;
        /// <summary>Expo, 0..1.</summary>
        public float Expo;

        public AxisRates(float rcRate, float rate, float expo) { RcRate = rcRate; Rate = rate; Expo = expo; }
    }

    [Serializable]
    public struct RateProfile
    {
        public RatesType Type;
        public AxisRates Roll;
        public AxisRates Pitch;
        public AxisRates Yaw;

        public static RateProfile BetaflightDefault => new RateProfile
        {
            Type = RatesType.Betaflight,
            Roll = new AxisRates(1.0f, 0.7f, 0f),
            Pitch = new AxisRates(1.0f, 0.7f, 0f),
            Yaw = new AxisRates(1.0f, 0.7f, 0f),
        };

        public static RateProfile ActualDefault => new RateProfile
        {
            Type = RatesType.Actual,
            Roll = new AxisRates(70f, 670f, 0.54f),
            Pitch = new AxisRates(70f, 670f, 0.54f),
            Yaw = new AxisRates(70f, 670f, 0.54f),
        };
    }

    /// <summary>Stick-to-rate curves. Formulas follow Betaflight 4.x (rc.c).</summary>
    public static class Rates
    {
        /// <summary>Betaflight's hard limit on setpoint rate.</summary>
        public const float MaxRateDegPerSec = 1998f;

        public static float DegPerSec(RatesType type, in AxisRates r, float stick)
        {
            stick = Clamp(stick, -1f, 1f);
            float rate = type == RatesType.Actual ? Actual(r, stick) : Betaflight(r, stick);
            return Clamp(rate, -MaxRateDegPerSec, MaxRateDegPerSec);
        }

        /// <summary>Betaflight rates: RC Rate, Super Rate, Expo.</summary>
        public static float Betaflight(in AxisRates r, float stick)
        {
            float abs = MathF.Abs(stick);
            float cmd = stick;
            if (r.Expo > 0f)
                cmd = stick * abs * abs * abs * r.Expo + stick * (1f - r.Expo);

            float rcRate = r.RcRate;
            if (rcRate > 2f) rcRate += 14.54f * (rcRate - 2f);

            float angleRate = 200f * rcRate * cmd;
            if (r.Rate != 0f)
            {
                float superFactor = 1f / Clamp(1f - abs * r.Rate, 0.01f, 1f);
                angleRate *= superFactor;
            }
            return angleRate;
        }

        /// <summary>Actual rates: Center Sensitivity, Max Rate, Expo.</summary>
        public static float Actual(in AxisRates r, float stick)
        {
            float abs = MathF.Abs(stick);
            float s5 = stick * stick * stick * stick * stick;
            float expoCurve = abs * (s5 * r.Expo + stick * (1f - r.Expo));
            float center = r.RcRate;
            float stickMovement = MathF.Max(0f, r.Rate - center);
            return stick * center + stickMovement * expoCurve;
        }

        internal static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
