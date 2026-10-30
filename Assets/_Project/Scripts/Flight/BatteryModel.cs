using System;

namespace GateRush.Flight
{
    /// <summary>LiPo pack parameters.</summary>
    [Serializable]
    public struct BatterySettings
    {
        /// <summary>Cells in series (4 = 4S).</summary>
        public int Cells;
        public float CapacityMah;
        /// <summary>Pack internal resistance (ohm); sag = current × resistance.</summary>
        public float InternalResistance;
        /// <summary>Current at full throttle on all four motors (A), sets the load curve.</summary>
        public float MaxCurrent;
        /// <summary>Electronics draw while armed (A).</summary>
        public float IdleCurrent;

        public static BatterySettings Race4S => new BatterySettings { Cells = 4, CapacityMah = 1300f, InternalResistance = 0.018f, MaxCurrent = 120f, IdleCurrent = 0.6f };
        public static BatterySettings Freestyle6S => new BatterySettings { Cells = 6, CapacityMah = 1100f, InternalResistance = 0.025f, MaxCurrent = 90f, IdleCurrent = 0.8f };
        public static BatterySettings Whoop1S => new BatterySettings { Cells = 1, CapacityMah = 300f, InternalResistance = 0.06f, MaxCurrent = 12f, IdleCurrent = 0.15f };
    }

    /// <summary>
    /// Simple LiPo model: open-circuit voltage from state of charge, minus I·R sag. Current follows motor speed
    /// (power ∝ ω³ for a prop, so I ∝ speed³). Motor thrust scales with (V / V_full)² when sag is enabled.
    /// </summary>
    public static class BatteryModel
    {
        public const float CellFull = 4.2f;
        public const float CellEmpty = 3.3f;

        /// <summary>Resting cell voltage for a state of charge (0..1): flat middle, steep at both ends.</summary>
        public static float CellOpenCircuit(float soc)
        {
            soc = soc < 0f ? 0f : (soc > 1f ? 1f : soc);
            // Piecewise-linear LiPo curve (typical discharge shape).
            if (soc > 0.9f) return 4.05f + (soc - 0.9f) / 0.1f * 0.15f;
            if (soc > 0.2f) return 3.75f + (soc - 0.2f) / 0.7f * 0.30f;
            if (soc > 0.05f) return 3.55f + (soc - 0.05f) / 0.15f * 0.20f;
            return CellEmpty + soc / 0.05f * 0.25f;
        }

        /// <summary>Pack current (A) for motor speed fractions.</summary>
        public static float Current(in BatterySettings b, in Motor4 speed, bool armed)
        {
            if (!armed) return 0f;
            float load = 0f;
            for (int i = 0; i < 4; i++)
            {
                float s = speed[i];
                load += s * s * s;
            }
            return b.IdleCurrent + b.MaxCurrent * 0.25f * load;
        }

        /// <summary>Advances the pack: drains charge and returns the loaded voltage.</summary>
        public static float Step(in BatterySettings b, ref float usedMah, float current, float dt)
        {
            usedMah += current * dt * (1000f / 3600f);
            float soc = 1f - usedMah / Math.Max(b.CapacityMah, 1f);
            float ocv = CellOpenCircuit(soc) * b.Cells;
            return Math.Max(0f, ocv - current * b.InternalResistance);
        }

        /// <summary>Thrust multiplier from the loaded voltage relative to a full pack.</summary>
        public static float ThrustScale(in BatterySettings b, float voltage)
        {
            float full = CellFull * Math.Max(1, b.Cells);
            float r = voltage / full;
            return r * r;
        }
    }
}
