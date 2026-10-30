using GateRush.Flight;
using UnityEngine;

namespace GateRush.Input
{
    /// <summary>
    /// Base for anything that produces <see cref="PilotInput"/>. Owns arming and flight-mode state so
    /// every source behaves the same: arm only with throttle low, mode cycles Acro → Angle → Horizon.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public abstract class PilotInputSource : MonoBehaviour
    {
        [SerializeField] protected StickShaping shaping = StickShaping.Default;
        [SerializeField, Range(0f, 0.2f)] protected float armThrottleLimit = 0.05f;
        [SerializeField] protected FlightMode mode = FlightMode.Acro;

        private bool _armed;

        /// <summary>Hover throttle of the current quad, used by spring-centred throttle mappings. Set by the vehicle.</summary>
        public float HoverThrottle { get; set; } = 0.3f;

        public PilotInput Current { get; private set; }

        /// <summary>True if this source has a usable device right now.</summary>
        public abstract bool IsConnected { get; }

        /// <summary>
        /// When several sources are connected the router uses the highest priority.
        /// Guide: RC radio 30, gamepad 20, Touch controllers 10, keyboard 1.
        /// </summary>
        public abstract int Priority { get; }

        public bool IsArmed => _armed;

        /// <summary>Reads devices into raw sticks (-1..1 each, throttle already 0..1) and button edges.</summary>
        protected abstract void ReadRaw(out float throttle, out float yaw, out float pitch, out float roll,
            out bool armToggle, out bool modeCycle, out bool reset);

        public void Disarm() => _armed = false;

        protected virtual void Update()
        {
            if (!IsConnected)
            {
                _armed = false;
                Current = default;
                return;
            }

            ReadRaw(out float throttle, out float yaw, out float pitch, out float roll,
                out bool armToggle, out bool modeCycle, out bool reset);

            if (armToggle)
            {
                if (_armed) _armed = false;
                else if (throttle <= armThrottleLimit) _armed = true;
            }
            if (modeCycle) mode = (FlightMode)(((int)mode + 1) % 3);

            Current = new PilotInput
            {
                Throttle = throttle,
                Yaw = shaping.Apply(yaw),
                Pitch = shaping.Apply(pitch),
                Roll = shaping.Apply(roll),
                Arm = _armed,
                Mode = mode,
                Reset = reset,
            };
        }
    }
}
