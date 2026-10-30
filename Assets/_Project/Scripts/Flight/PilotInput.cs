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
    /// Normalised pilot commands. Every input source (Touch, gamepad, RC radio) produces this.
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
    }
}
