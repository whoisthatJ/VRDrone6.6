using System;

namespace GateRush.Flight
{
    public enum FlightMode
    {
        Acro = 0,
        Angle = 1,
        Horizon = 2,
    }

    /// <summary>
    /// Normalised pilot commands. Every input source (keyboard, gamepad, RC radio) produces this.
    /// Lives in Flight (not Input) so the simulator has no dependency on input code.
    /// </summary>
    [Serializable]
    public struct PilotInput
    {
        /// <summary>0..1</summary>
        public float Throttle;
        /// <summary>-1..1, positive = yaw right.</summary>
        public float Yaw;
        /// <summary>-1..1, positive = stick forward (nose down).</summary>
        public float Pitch;
        /// <summary>-1..1, positive = roll right.</summary>
        public float Roll;
        public bool Arm;
        public FlightMode Mode;
        public bool Reset;

        public static PilotInput Armed(float throttle, FlightMode mode = FlightMode.Acro) =>
            new PilotInput { Throttle = throttle, Arm = true, Mode = mode };

        /// <summary>
        /// Two controllers flying one quad (e.g. radio + keyboard): sticks add, throttle takes the higher one, either
        /// can arm or reset. The flight mode comes from <paramref name="primary"/>.
        /// </summary>
        public static PilotInput Mix(in PilotInput primary, in PilotInput other) => new PilotInput
        {
            Throttle = Math.Max(primary.Throttle, other.Throttle),
            Yaw = Math.Clamp(primary.Yaw + other.Yaw, -1f, 1f),
            Pitch = Math.Clamp(primary.Pitch + other.Pitch, -1f, 1f),
            Roll = Math.Clamp(primary.Roll + other.Roll, -1f, 1f),
            Arm = primary.Arm || other.Arm,
            Mode = primary.Mode,
            Reset = primary.Reset || other.Reset,
        };
    }
}
