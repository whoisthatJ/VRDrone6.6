using System;
using Ushqyr.Flight;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Turns the drone's state, seen once per rendered frame, into a recording with an even rate (EDU_PLAN F2: 100 Hz).
    /// A frame rate of 60 or 144 gives the same 100 samples a second: values between two frames are interpolated. The
    /// recording can be limited to the last N seconds ("last flight" in free flight).
    /// </summary>
    public sealed class FlightRecorderCore
    {
        private readonly float _keepSeconds;
        private readonly double _step;
        private QuadState _prev;
        private PilotInput _prevSticks;
        private double _prevTime, _next, _start;
        private bool _has;

        public FlightRecording Recording { get; private set; }

        /// <param name="rate">Samples per second in the recording.</param>
        /// <param name="keepSeconds">0 keeps everything; otherwise only the last so many seconds are kept.</param>
        public FlightRecorderCore(float rate = 100f, float keepSeconds = 0f)
        {
            if (rate <= 0f) throw new ArgumentOutOfRangeException(nameof(rate));
            _step = 1.0 / rate;
            _keepSeconds = keepSeconds;
            Recording = new FlightRecording { Rate = rate };
        }

        /// <summary>Starts a new recording; notes and settings of the recorder stay.</summary>
        public void Restart()
        {
            var fresh = new FlightRecording { Rate = Recording.Rate };
            foreach (var note in Recording.Notes) fresh.Notes[note.Key] = note.Value;
            Recording = fresh;
            _has = false;
        }

        /// <summary>Time since the start of the recording, for stamping events.</summary>
        public float Now => _has ? (float)(_prevTime - _start) : 0f;

        public void Event(string type, string data = "") => Recording.Event(Now, type, data);

        /// <summary>
        /// The state at <paramref name="time"/> (seconds, never going back), the sticks as the pilot holds them, and the
        /// battery left (0..1).
        /// </summary>
        public void Push(double time, in QuadState s, in PilotInput sticks, float battery)
        {
            if (!_has)
            {
                _has = true;
                _start = time;
                _next = time;
            }
            else if (time < _prevTime) return; // the clock never runs backwards; ignore a stray frame
            while (_next <= time + 1e-9)
            {
                double span = time - _prevTime;
                float k = span > 1e-9 ? (float)((_next - _prevTime) / span) : 1f;
                if (_next <= _start + 1e-9) k = 1f;
                Recording.Samples.Add(Sample((float)(_next - _start), _prev, s, _prevSticks, sticks, k, battery));
                _next += _step;
            }
            _prev = s;
            _prevSticks = sticks;
            _prevTime = time;
            if (_keepSeconds > 0f) Trim();
        }

        private void Trim()
        {
            int keep = (int)(_keepSeconds * Recording.Rate);
            int extra = Recording.Samples.Count - keep;
            // Cut in blocks, not one sample at a time: removing from the front of a long list is slow.
            if (extra < Recording.Rate * 5) return;
            float cut = Recording.Samples[extra].T;
            Recording.Samples.RemoveRange(0, extra);
            Recording.Events.RemoveAll(e => e.T < cut);
        }

        private static FlightSample Sample(float t, in QuadState a, in QuadState b, in PilotInput sa, in PilotInput sb, float k, float battery)
        {
            Vec3 p = Vec3.Lerp(a.Position, b.Position, k), v = Vec3.Lerp(a.Velocity, b.Velocity, k);
            Quat qa = a.Orientation, qb = b.Orientation;
            // The short way between the two orientations, then back to unit length.
            float dot = qa.x * qb.x + qa.y * qb.y + qa.z * qb.z + qa.w * qb.w, sign = dot < 0f ? -1f : 1f;
            float x = qa.x + (qb.x * sign - qa.x) * k, y = qa.y + (qb.y * sign - qa.y) * k, z = qa.z + (qb.z * sign - qa.z) * k, w = qa.w + (qb.w * sign - qa.w) * k;
            float len = MathF.Sqrt(x * x + y * y + z * z + w * w);
            if (len < 1e-6f) { x = qb.x; y = qb.y; z = qb.z; w = qb.w; len = 1f; }
            return new FlightSample
            {
                T = t,
                X = p.x, Y = p.y, Z = p.z,
                Vx = v.x, Vy = v.y, Vz = v.z,
                Qx = x / len, Qy = y / len, Qz = z / len, Qw = w / len,
                // Sticks are not interpolated: a sample holds what the pilot's hands were doing at the newer frame.
                Throttle = sb.Throttle, Yaw = sb.Yaw, Pitch = sb.Pitch, Roll = sb.Roll,
                Mode = (byte)sb.Mode,
                Armed = b.Armed,
                Battery = battery,
            };
        }
    }
}
