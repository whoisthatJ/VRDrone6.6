using System;

namespace Ushqyr.Flight
{
    /// <summary>Where the drone gets its position from.</summary>
    public enum PositionSourceKind
    {
        /// <summary>The true state, with no error: free flight and tests of the controller itself.</summary>
        Perfect = 0,
        /// <summary>Outdoors: satellite positioning and a barometer.</summary>
        Gps = 1,
        /// <summary>Indoors: a downward camera measuring motion over the floor, and a range finder for the height.</summary>
        OpticalFlow = 2,
    }

    [Serializable]
    public struct PositionSourceSettings
    {
        public PositionSourceKind Kind;
        /// <summary>How far the GPS position wanders from the truth, typical (m), and how slowly (s).</summary>
        public float GpsWander, GpsWanderSeconds;
        public float GpsRate;
        public float GpsSpeedNoise;
        /// <summary>Barometer: slow drift (m) over its time (s), and fast noise (m).</summary>
        public float BaroDrift, BaroDriftSeconds, BaroNoise;
        /// <summary>Optical flow works between these heights above the floor (m).</summary>
        public float FlowMinHeight, FlowMaxHeight;
        /// <summary>Speed error of the flow sensor per metre of height (m/s per m), and its fixed bias (m/s), which becomes drift.</summary>
        public float FlowNoisePerMetre, FlowBias;
        public float FlowRate;

        public static PositionSourceSettings Perfect => new PositionSourceSettings { Kind = PositionSourceKind.Perfect };

        public static PositionSourceSettings Outdoor => new PositionSourceSettings
        {
            Kind = PositionSourceKind.Gps,
            GpsWander = 0.5f, GpsWanderSeconds = 12f, GpsRate = 10f, GpsSpeedNoise = 0.05f,
            BaroDrift = 0.15f, BaroDriftSeconds = 40f, BaroNoise = 0.03f,
        };

        public static PositionSourceSettings Indoor => new PositionSourceSettings
        {
            Kind = PositionSourceKind.OpticalFlow,
            FlowMinHeight = 0.08f, FlowMaxHeight = 4f, FlowNoisePerMetre = 0.02f, FlowBias = 0.01f, FlowRate = 50f,
        };
    }

    /// <summary>Faults the teacher or an activity can switch on (EDU_PLAN F4).</summary>
    public struct PositionFaults
    {
        /// <summary>No satellite fix (GPS) or no usable picture of the floor (flow).</summary>
        public bool Lost;
        /// <summary>Makes the GPS wander this many times larger (1 = normal).</summary>
        public float Degraded;
        /// <summary>Compass interference: the heading the drone believes is off by this many degrees.</summary>
        public float HeadingOffsetDeg;
    }

    /// <summary>
    /// What the drone believes about where it is (EDU_PLAN F3). The autopilot flies on this estimate, not on the truth,
    /// so a camera drone outdoors wanders a little as a real one does, and a lost fix has real consequences. The errors
    /// come from a seed: the same seed and the same flight give the same errors.
    /// </summary>
    public sealed class PositionEstimator
    {
        private readonly PositionSourceSettings _c;
        private uint _rng;
        private Vec3 _wander, _flowBias, _offset, _velocity;
        private float _baro, _sinceFix, _sinceUpdate, _lostFor;
        private bool _started;

        /// <summary>False when the source has given nothing usable for more than a second.</summary>
        public bool PositionOk { get; private set; } = true;

        public PositionEstimator(in PositionSourceSettings settings, int seed)
        {
            _c = settings;
            _rng = (uint)seed * 2654435761u + 0x9E3779B9u;
            if (_rng == 0) _rng = 1;
            // The flow sensor's bias is a property of this drone on this flight: fixed, in a random direction.
            float a = Uniform() * 2f * MathF.PI;
            _flowBias = new Vec3(MathF.Cos(a), 0f, MathF.Sin(a)) * settings.FlowBias;
        }

        /// <summary>The estimate for this step: the true state with the position, speed and heading the sensors would give.</summary>
        public QuadState Estimate(in QuadState truth, float groundY, in PositionFaults faults, float dt)
        {
            if (_c.Kind == PositionSourceKind.Perfect)
            {
                PositionOk = !faults.Lost;
                return Turned(truth, faults.HeadingOffsetDeg);
            }
            float height = truth.Position.y - groundY;
            bool usable = !faults.Lost && (_c.Kind != PositionSourceKind.OpticalFlow || height >= _c.FlowMinHeight && height <= _c.FlowMaxHeight);
            _lostFor = usable ? 0f : _lostFor + dt;
            PositionOk = _lostFor < 1f;

            if (_c.Kind == PositionSourceKind.Gps)
            {
                // The error wanders: it is pulled back to zero slowly and pushed around by noise (a first-order Gauss-Markov process).
                float scale = faults.Degraded > 1f ? faults.Degraded : 1f;
                float tau = MathF.Max(_c.GpsWanderSeconds, 0.1f), k = dt / tau, push = MathF.Sqrt(2f * k);
                _wander = _wander * (1f - k) + new Vec3(Gauss(), Gauss() * 1.5f, Gauss()) * (_c.GpsWander * scale * push);
                float tauB = MathF.Max(_c.BaroDriftSeconds, 0.1f), kB = dt / tauB;
                _baro = _baro * (1f - kB) + Gauss() * _c.BaroDrift * MathF.Sqrt(2f * kB);
                _sinceFix += dt;
                if (!_started || (usable && _sinceFix >= 1f / MathF.Max(_c.GpsRate, 1f)))
                {
                    _started = true;
                    _sinceFix = 0f;
                    _offset = new Vec3(_wander.x, _baro + Gauss() * _c.BaroNoise, _wander.z); // height comes from the barometer
                    _velocity = new Vec3(Gauss(), Gauss(), Gauss()) * _c.GpsSpeedNoise;
                }
            }
            else
            {
                _sinceUpdate += dt;
                float period = 1f / MathF.Max(_c.FlowRate, 1f);
                if (usable && _sinceUpdate >= period)
                {
                    // The flow sensor measures speed; position is that speed added up, so its bias becomes a slow drift.
                    float noise = _c.FlowNoisePerMetre * MathF.Max(height, 0.1f);
                    _velocity = _flowBias + new Vec3(Gauss(), 0f, Gauss()) * noise;
                    _offset += new Vec3(_velocity.x, 0f, _velocity.z) * _sinceUpdate;
                    _sinceUpdate = 0f;
                }
            }

            QuadState e = Turned(truth, faults.HeadingOffsetDeg);
            e.Position = truth.Position + _offset;
            e.Velocity = truth.Velocity + _velocity;
            return e;
        }

        /// <summary>The offset between the believed and the true position right now (m).</summary>
        public Vec3 Error => _offset;

        private static QuadState Turned(in QuadState s, float headingOffsetDeg)
        {
            if (headingOffsetDeg == 0f) return s;
            QuadState e = s;
            e.Orientation = Quat.AxisAngle(Vec3.Up, headingOffsetDeg * MathF.PI / 180f) * s.Orientation;
            return e;
        }

        private float Uniform()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng >> 8) * (1f / 16777216f);
        }

        private float Gauss()
        {
            float u = MathF.Max(Uniform(), 1e-7f), v = Uniform();
            return MathF.Sqrt(-2f * MathF.Log(u)) * MathF.Cos(2f * MathF.PI * v);
        }
    }
}
