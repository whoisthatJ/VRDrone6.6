using System;
using GateRush.Flight;

namespace GateRush.Input
{
    /// <summary>Calibrated range of one raw channel.</summary>
    [Serializable]
    public struct ChannelRange
    {
        public float Min;
        public float Center;
        public float Max;

        public ChannelRange(float min, float center, float max) { Min = min; Center = center; Max = max; }

        /// <summary>Raw HID/Android axes as the Input System reports them: -1..1, centred at 0.</summary>
        public static ChannelRange Default => new ChannelRange(-1f, 0f, 1f);

        public bool IsValid => Max - Min > 1e-4f && Center >= Min && Center <= Max;
    }

    /// <summary>A stick axis: which channel, which way round, and its calibrated range.</summary>
    [Serializable]
    public struct AxisAssignment
    {
        /// <summary>Channel index, or -1 if unassigned.</summary>
        public int Channel;
        public bool Invert;
        public ChannelRange Range;

        public AxisAssignment(int channel, bool invert = false) { Channel = channel; Invert = invert; Range = ChannelRange.Default; }

        public bool IsAssigned => Channel >= 0;
    }

    /// <summary>A switch: which channel, and the raw value for each position (2 or 3 positions).</summary>
    [Serializable]
    public struct SwitchAssignment
    {
        /// <summary>Channel index, or -1 if unassigned.</summary>
        public int Channel;
        /// <summary>Raw value in position 0 (e.g. disarmed / Acro).</summary>
        public float Position0;
        /// <summary>Raw value in position 1 (e.g. armed / Angle).</summary>
        public float Position1;
        /// <summary>Raw value in position 2 (Horizon), only used if <see cref="ThreePosition"/>.</summary>
        public float Position2;
        public bool ThreePosition;

        public SwitchAssignment(int channel, float pos0, float pos1)
        {
            Channel = channel; Position0 = pos0; Position1 = pos1; Position2 = pos1; ThreePosition = false;
        }

        public static SwitchAssignment None => new SwitchAssignment(-1, 0f, 1f);

        public bool IsAssigned => Channel >= 0;
    }

    /// <summary>
    /// How a radio's channels become sticks and switches. Used both by shipped presets (guesses)
    /// and by per-device calibrations (measured). Throttle is a non-centring axis: min = 0, max = 1.
    /// </summary>
    [Serializable]
    public sealed class RadioMapping
    {
        public AxisAssignment Throttle = new AxisAssignment(2);
        public AxisAssignment Yaw = new AxisAssignment(3);
        // Unity's HID joystick layout already flips stick Y so "up/forward" reads positive.
        public AxisAssignment Pitch = new AxisAssignment(1);
        public AxisAssignment Roll = new AxisAssignment(0);
        public SwitchAssignment Arm = SwitchAssignment.None;
        public SwitchAssignment Mode = SwitchAssignment.None;
        public SwitchAssignment Reset = SwitchAssignment.None;
        /// <summary>Camera-view switch/button: each flip to position 1 cycles FPV → chase → line of sight.</summary>
        public SwitchAssignment View = SwitchAssignment.None;

        public RadioMapping Clone() => (RadioMapping)MemberwiseClone();

        /// <summary>Centred axis → -1..1 with separate scaling for each side of centre (handles asymmetric gimbals).</summary>
        public static float Centered(float raw, in AxisAssignment a)
        {
            var r = a.Range.IsValid ? a.Range : ChannelRange.Default;
            float v = raw >= r.Center
                ? (raw - r.Center) / MathF.Max(r.Max - r.Center, 1e-4f)
                : (raw - r.Center) / MathF.Max(r.Center - r.Min, 1e-4f);
            v = Clamp(v, -1f, 1f);
            return a.Invert ? -v : v;
        }

        /// <summary>Non-centring axis (throttle) → 0..1 over the full min..max travel.</summary>
        public static float Full(float raw, in AxisAssignment a)
        {
            var r = a.Range.IsValid ? a.Range : ChannelRange.Default;
            float v = Clamp((raw - r.Min) / MathF.Max(r.Max - r.Min, 1e-4f), 0f, 1f);
            return a.Invert ? 1f - v : v;
        }

        /// <summary>Which position (0, 1 or 2) a switch is in: the nearest recorded position value.</summary>
        public static int SwitchPosition(float raw, in SwitchAssignment s)
        {
            float d0 = MathF.Abs(raw - s.Position0);
            float d1 = MathF.Abs(raw - s.Position1);
            if (!s.ThreePosition) return d1 < d0 ? 1 : 0;
            float d2 = MathF.Abs(raw - s.Position2);
            if (d0 <= d1 && d0 <= d2) return 0;
            return d1 <= d2 ? 1 : 2;
        }

        /// <summary>Mode switch position → flight mode: 0 Acro, 1 Angle, 2 Horizon.</summary>
        public static FlightMode ModeFor(int position) => position == 1 ? FlightMode.Angle : position == 2 ? FlightMode.Horizon : FlightMode.Acro;

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
