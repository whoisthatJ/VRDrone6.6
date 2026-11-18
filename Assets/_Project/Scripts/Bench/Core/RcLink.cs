using System;
using System.Collections.Generic;
using Ushqyr.Flight;

namespace Ushqyr.Bench
{
    [Serializable]
    public struct RcLinkSettings
    {
        /// <summary>Packets per second the link carries; packets sent faster are dropped (the newest one wins).</summary>
        public float RateHz;
        /// <summary>Constant delay from sending a packet to the flight controller acting on it (s).</summary>
        public float Latency;
        /// <summary>No packet for this long: the flight controller cuts the motors (s).</summary>
        public float FailsafeTime;

        public static RcLinkSettings Default => new RcLinkSettings { RateHz = 100f, Latency = 0.010f, FailsafeTime = 0.5f };
    }

    /// <summary>
    /// The radio link between the team's program and the flight controller, in simulator time.
    /// Channels are what a transmitter sends: 16 values of 1000..2000 microseconds, order roll, pitch, throttle, yaw,
    /// AUX1 (arm), AUX2 (flight mode), then spare. Anything outside the range is clamped; arming needs AUX1 high with the
    /// throttle low, as on a real flight controller.
    /// </summary>
    public sealed class RcLink
    {
        public const int Channels = 16;
        public const int MinUs = 1000, MidUs = 1500, MaxUs = 2000;
        /// <summary>Throttle must be at or below this to arm.</summary>
        public const int ArmThrottleUs = 1050;

        private struct Packet
        {
            public double Arrival;
            public int Roll, Pitch, Throttle, Yaw, Aux1, Aux2;
        }

        private readonly RcLinkSettings _settings;
        private readonly Queue<Packet> _inFlight = new Queue<Packet>();
        private Packet _current;
        private bool _hasCurrent;
        private double _lastAccepted = double.NegativeInfinity;
        private bool _armed;
        private bool _aux1WasHigh;

        public RcLink(RcLinkSettings settings) => _settings = settings;

        public bool Armed => _armed;
        /// <summary>True while no packet has reached the flight controller for longer than the failsafe time.</summary>
        public bool Failsafe { get; private set; }
        public int PacketsAccepted { get; private set; }
        public int PacketsDropped { get; private set; }

        public void Reset()
        {
            _inFlight.Clear();
            _hasCurrent = false;
            _lastAccepted = double.NegativeInfinity;
            _armed = false;
            _aux1WasHigh = false;
            Failsafe = false;
            PacketsAccepted = PacketsDropped = 0;
        }

        public static int Clamp(long us) => us < MinUs ? MinUs : us > MaxUs ? MaxUs : (int)us;

        /// <summary>
        /// The program sends a packet at simulator time <paramref name="now"/>. Missing channels are centred (throttle and
        /// AUX low). Returns false when the packet was dropped by the rate limit.
        /// </summary>
        public bool Send(double now, IReadOnlyList<int> channelsUs)
        {
            float period = _settings.RateHz > 0f ? 1f / _settings.RateHz : 0f;
            // A small tolerance so a sender exactly at the link rate is never dropped by rounding.
            if (now - _lastAccepted < period - 1e-6)
            {
                PacketsDropped++;
                return false;
            }
            _lastAccepted = now;
            PacketsAccepted++;
            int Ch(int i, int fallback) => channelsUs != null && i < channelsUs.Count ? Clamp(channelsUs[i]) : fallback;
            _inFlight.Enqueue(new Packet
            {
                Arrival = now + Math.Max(0f, _settings.Latency),
                Roll = Ch(0, MidUs), Pitch = Ch(1, MidUs), Throttle = Ch(2, MinUs), Yaw = Ch(3, MidUs),
                Aux1 = Ch(4, MinUs), Aux2 = Ch(5, MinUs),
            });
            return true;
        }

        /// <summary>What the flight controller acts on at simulator time <paramref name="now"/>.</summary>
        public PilotInput Sample(double now)
        {
            while (_inFlight.Count > 0 && _inFlight.Peek().Arrival <= now + 1e-9)
            {
                _current = _inFlight.Dequeue();
                _hasCurrent = true;
                bool aux1 = _current.Aux1 > MidUs;
                // Arm on the switch going high with the throttle down; a high switch at power-up doesn't arm.
                if (aux1 && !_aux1WasHigh && _current.Throttle <= ArmThrottleUs) _armed = true;
                if (!aux1) _armed = false;
                _aux1WasHigh = aux1;
            }

            Failsafe = _hasCurrent && now - _current.Arrival > _settings.FailsafeTime;
            if (!_hasCurrent) return default;
            if (Failsafe)
            {
                // Signal lost: motors off until the switch is cycled again.
                _armed = false;
                _aux1WasHigh = true;
                return new PilotInput { Mode = ModeOf(_current.Aux2) };
            }
            return new PilotInput
            {
                Roll = (_current.Roll - MidUs) / 500f,
                Pitch = (_current.Pitch - MidUs) / 500f,
                Yaw = (_current.Yaw - MidUs) / 500f,
                Throttle = (_current.Throttle - MinUs) / 1000f,
                Arm = _armed,
                Mode = ModeOf(_current.Aux2),
            };
        }

        /// <summary>AUX2: low = Angle (self-levelling), middle = Horizon, high = Acro.</summary>
        public static FlightMode ModeOf(int aux2Us) =>
            aux2Us < 1300 ? FlightMode.Angle : aux2Us <= 1700 ? FlightMode.Horizon : FlightMode.Acro;
    }
}
