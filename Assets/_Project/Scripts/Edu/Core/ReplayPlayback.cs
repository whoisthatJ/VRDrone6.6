using System;
using System.Collections.Generic;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The clock of the replay viewer (EDU_PLAN F2): a position on a recording's timeline that plays at 0.1 to 4 times
    /// the flown speed, pauses, steps and jumps. Time 0 is the first sample kept in the recording.
    /// </summary>
    public sealed class ReplayPlayback
    {
        public static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };

        private readonly FlightRecording _rec;
        private readonly float _t0;
        private int _speed = 3;

        public ReplayPlayback(FlightRecording recording)
        {
            _rec = recording ?? throw new ArgumentNullException(nameof(recording));
            if (_rec.Samples.Count == 0) throw new ArgumentException("the recording is empty");
            _t0 = _rec.Samples[0].T;
            End = _rec.Duration;
        }

        public FlightRecording Recording => _rec;
        /// <summary>Length of the flight in seconds.</summary>
        public float End { get; }
        /// <summary>Seconds from the start of the recording, 0..End.</summary>
        public float Time { get; private set; }
        public bool Playing { get; set; }
        public float Speed => Speeds[_speed];

        public void Faster() => _speed = Math.Min(_speed + 1, Speeds.Length - 1);
        public void Slower() => _speed = Math.Max(_speed - 1, 0);

        public void SetSpeed(float speed)
        {
            int best = 0;
            for (int i = 1; i < Speeds.Length; i++)
                if (Math.Abs(Speeds[i] - speed) < Math.Abs(Speeds[best] - speed)) best = i;
            _speed = best;
        }

        public void Seek(float t) => Time = t < 0f || float.IsNaN(t) ? 0f : t > End ? End : t;

        /// <summary>Moves by a small amount and pauses (frame-by-frame review).</summary>
        public void Step(float seconds)
        {
            Playing = false;
            Seek(Time + seconds);
        }

        /// <summary>Play from the current place; from the start when the end was reached.</summary>
        public void Toggle()
        {
            if (!Playing && Time >= End) Time = 0f;
            Playing = !Playing;
        }

        /// <summary>Lets <paramref name="realSeconds"/> of the viewer's time pass. Stops at the end.</summary>
        public void Advance(float realSeconds)
        {
            if (!Playing || realSeconds <= 0f) return;
            Time += realSeconds * Speed;
            if (Time >= End)
            {
                Time = End;
                Playing = false;
            }
        }

        /// <summary>The drone and the sticks at the cursor.</summary>
        public FlightSample Now => SampleAt(_rec, Time);

        /// <summary>The recording's own time stamp (as events carry it) at the cursor.</summary>
        public float RecordingTime => _t0 + Time;

        /// <summary>Events as (seconds from the start, event), in order; events before the kept part are left out.</summary>
        public List<KeyValuePair<float, FlightEvent>> Events()
        {
            var list = new List<KeyValuePair<float, FlightEvent>>();
            foreach (FlightEvent e in _rec.Events)
            {
                float t = e.T - _t0;
                if (t >= 0f && t <= End + 1e-3f) list.Add(new KeyValuePair<float, FlightEvent>(t, e));
            }
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            return list;
        }

        /// <summary>
        /// The recording at <paramref name="t"/> seconds from its start: the two samples around it, blended. Outside
        /// the flight it gives the first or the last sample.
        /// </summary>
        public static FlightSample SampleAt(FlightRecording rec, float t)
        {
            List<FlightSample> s = rec.Samples;
            if (s.Count == 0) return default;
            float at = s[0].T + t;
            if (at <= s[0].T) return s[0];
            if (at >= s[s.Count - 1].T) return s[s.Count - 1];
            int lo = 0, hi = s.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (s[mid].T <= at) lo = mid; else hi = mid;
            }
            FlightSample a = s[lo], b = s[hi];
            float span = b.T - a.T;
            float k = span > 1e-9f ? (at - a.T) / span : 0f;
            var r = a;
            r.T = at;
            r.X = a.X + (b.X - a.X) * k; r.Y = a.Y + (b.Y - a.Y) * k; r.Z = a.Z + (b.Z - a.Z) * k;
            r.Vx = a.Vx + (b.Vx - a.Vx) * k; r.Vy = a.Vy + (b.Vy - a.Vy) * k; r.Vz = a.Vz + (b.Vz - a.Vz) * k;
            r.Throttle = a.Throttle + (b.Throttle - a.Throttle) * k;
            r.Yaw = a.Yaw + (b.Yaw - a.Yaw) * k;
            r.Pitch = a.Pitch + (b.Pitch - a.Pitch) * k;
            r.Roll = a.Roll + (b.Roll - a.Roll) * k;
            r.Battery = a.Battery + (b.Battery - a.Battery) * k;
            // Orientation: the shorter way round, then normalised (the samples are 10 ms apart, so this is exact enough).
            float dot = a.Qx * b.Qx + a.Qy * b.Qy + a.Qz * b.Qz + a.Qw * b.Qw;
            float sign = dot < 0f ? -1f : 1f;
            float qx = a.Qx + (sign * b.Qx - a.Qx) * k, qy = a.Qy + (sign * b.Qy - a.Qy) * k;
            float qz = a.Qz + (sign * b.Qz - a.Qz) * k, qw = a.Qw + (sign * b.Qw - a.Qw) * k;
            float n = (float)Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (n < 1e-6f) { qx = qy = qz = 0f; qw = 1f; n = 1f; }
            r.Qx = qx / n; r.Qy = qy / n; r.Qz = qz / n; r.Qw = qw / n;
            return r;
        }
    }

    /// <summary>What a graph of the viewer shows.</summary>
    public enum ReplaySeries { Height, Speed, Throttle }

    /// <summary>A recording reduced to the columns of a graph: the lowest and highest value inside each column.</summary>
    public sealed class ReplayGraph
    {
        public readonly float[] Low, High;
        /// <summary>The range of the vertical axis.</summary>
        public readonly float Min, Max;

        private ReplayGraph(float[] low, float[] high, float min, float max)
        {
            Low = low; High = high; Min = min; Max = max;
        }

        public static float Value(in FlightSample s, ReplaySeries series) =>
            series == ReplaySeries.Height ? s.Y : series == ReplaySeries.Speed ? s.Speed : s.Throttle;

        /// <summary>
        /// One column per <paramref name="columns"/> of the flight's length, so a short spike (a hit, a stick jab) is
        /// still seen however long the flight. Height is drawn from <paramref name="groundY"/>; throttle from 0 to 1.
        /// </summary>
        public static ReplayGraph Build(FlightRecording rec, ReplaySeries series, int columns, float groundY = 0f)
        {
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            var low = new float[columns];
            var high = new float[columns];
            int n = rec.Samples.Count;
            float min = float.MaxValue, max = float.MinValue;
            float last = n > 0 ? Shown(rec.Samples[0], series, groundY) : 0f;
            for (int c = 0; c < columns; c++)
            {
                int from = (int)((long)c * n / columns), to = (int)((long)(c + 1) * n / columns);
                float lo = last, hi = last; // a column with no sample of its own continues the line
                for (int i = from; i < to; i++)
                {
                    float v = Shown(rec.Samples[i], series, groundY);
                    if (i == from) lo = hi = v;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                    last = v;
                }
                low[c] = lo; high[c] = hi;
                if (lo < min) min = lo;
                if (hi > max) max = hi;
            }
            if (n == 0) { min = 0f; max = 1f; }
            if (series == ReplaySeries.Throttle) { min = 0f; max = 1f; }
            else
            {
                if (min > 0f) min = 0f;
                if (max - min < 1f) max = min + 1f; // a hover is a flat line, not noise blown up to full height
            }
            return new ReplayGraph(low, high, min, max);
        }

        private static float Shown(in FlightSample s, ReplaySeries series, float groundY) =>
            series == ReplaySeries.Height ? s.Y - groundY : Value(s, series);
    }

    /// <summary>
    /// Two flights side by side (EDU_PLAN F2: "compare with the reference"): how far in time one is behind the other
    /// along the path they share. For every moment of the flight it finds when the other flight was at the nearest
    /// place, moving forward only, so a path that crosses itself (laps, a figure of eight) is not mistaken.
    /// </summary>
    public sealed class ReplayComparison
    {
        /// <summary>Matches are computed this many times a second.</summary>
        public const float Rate = 10f;
        /// <summary>Further apart than this (m), the two flights are not on the same path and no gap is given.</summary>
        public const float MaxDistance = 5f;
        /// <summary>The other flight is searched this far ahead (s) of the last match.</summary>
        public const float Window = 3f;

        private readonly float[] _otherTime, _distance;

        public ReplayComparison(FlightRecording flight, FlightRecording other)
        {
            int count = (int)(flight.Duration * Rate) + 1;
            _otherTime = new float[count];
            _distance = new float[count];
            float match = 0f, otherEnd = other.Duration;
            float step = 1f / other.Rate;
            for (int i = 0; i < count; i++)
            {
                FlightSample p = ReplayPlayback.SampleAt(flight, i / Rate);
                float best = float.MaxValue, bestT = match;
                for (float t = match; t <= Math.Min(match + Window, otherEnd) + 1e-4f; t += step)
                {
                    FlightSample q = ReplayPlayback.SampleAt(other, t);
                    float dx = p.X - q.X, dy = p.Y - q.Y, dz = p.Z - q.Z;
                    float d = dx * dx + dy * dy + dz * dz;
                    if (d < best - 1e-9f) { best = d; bestT = t; }
                }
                match = bestT;
                _otherTime[i] = bestT;
                _distance[i] = (float)Math.Sqrt(best);
            }
        }

        /// <summary>Metres between this flight at <paramref name="t"/> and the nearest matching place of the other.</summary>
        public float DistanceAt(float t) => _distance[Index(t)];

        /// <summary>
        /// Seconds this flight is behind the other at <paramref name="t"/> (negative: ahead). NaN when the two are not
        /// on the same path there.
        /// </summary>
        public float GapAt(float t)
        {
            int i = Index(t);
            return _distance[i] > MaxDistance ? float.NaN : i / Rate - _otherTime[i];
        }

        private int Index(float t)
        {
            int i = (int)Math.Round(t * Rate);
            return i < 0 ? 0 : i >= _otherTime.Length ? _otherTime.Length - 1 : i;
        }
    }
}
