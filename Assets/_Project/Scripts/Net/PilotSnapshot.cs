using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GateRush.Net
{
    /// <summary>One sample of a pilot's quad, sent 20 times a second. <see cref="Time"/> is server time.</summary>
    public struct PilotSnapshot
    {
        public double Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        /// <summary>Motor speeds 0..255 (RR, FR, RL, FL), for the prop spin.</summary>
        public byte M0, M1, M2, M3;
        /// <summary><see cref="GateRush.Race.RaceSession.Progress"/>.</summary>
        public float Progress;
        public byte Lap;
        public bool Armed;

        public float Motor(int i) => (i == 0 ? M0 : i == 1 ? M1 : i == 2 ? M2 : M3) / 255f;

        public static byte PackMotor(float speed01) => (byte)Mathf.Clamp(Mathf.RoundToInt(speed01 * 255f), 0, 255);

        public void Write(FastBufferWriter w)
        {
            w.WriteValueSafe(Time);
            w.WriteValueSafe(Position);
            w.WriteValueSafe(Rotation);
            w.WriteValueSafe(Velocity);
            w.WriteValueSafe(M0);
            w.WriteValueSafe(M1);
            w.WriteValueSafe(M2);
            w.WriteValueSafe(M3);
            w.WriteValueSafe(Progress);
            w.WriteValueSafe(Lap);
            w.WriteValueSafe(Armed);
        }

        public static PilotSnapshot Read(FastBufferReader r)
        {
            var s = new PilotSnapshot();
            r.ReadValueSafe(out s.Time);
            r.ReadValueSafe(out s.Position);
            r.ReadValueSafe(out s.Rotation);
            r.ReadValueSafe(out s.Velocity);
            r.ReadValueSafe(out s.M0);
            r.ReadValueSafe(out s.M1);
            r.ReadValueSafe(out s.M2);
            r.ReadValueSafe(out s.M3);
            r.ReadValueSafe(out s.Progress);
            r.ReadValueSafe(out s.Lap);
            r.ReadValueSafe(out s.Armed);
            return s;
        }

        public const int Size = 8 + 12 + 16 + 12 + 4 + 4 + 1 + 1;
    }

    /// <summary>
    /// Recent snapshots of one remote pilot, played back a little in the past so there is always a sample on both
    /// sides (snapshot interpolation). Past the newest sample it extrapolates with the velocity for a short while.
    /// </summary>
    public sealed class SnapshotBuffer
    {
        /// <summary>How far behind server time remote quads are shown (2.4 send intervals).</summary>
        public const double Delay = 0.12;
        /// <summary>Longest extrapolation when packets stop.</summary>
        public const double MaxExtrapolation = 0.25;
        private const int Capacity = 32;

        private readonly List<PilotSnapshot> _samples = new List<PilotSnapshot>(Capacity);

        public int Count => _samples.Count;
        public bool HasData => _samples.Count > 0;
        public PilotSnapshot Latest => _samples[_samples.Count - 1];

        /// <summary>Adds a sample; out-of-order and duplicate packets (older than the newest) are dropped.</summary>
        public bool Add(in PilotSnapshot s)
        {
            if (_samples.Count > 0 && s.Time <= Latest.Time) return false;
            if (_samples.Count == Capacity) _samples.RemoveAt(0);
            _samples.Add(s);
            return true;
        }

        public void Clear() => _samples.Clear();

        /// <summary>Pose at <paramref name="time"/> (server time minus <see cref="Delay"/>).</summary>
        public bool Sample(double time, out Vector3 position, out Quaternion rotation, float[] motors)
        {
            position = default;
            rotation = Quaternion.identity;
            if (_samples.Count == 0) return false;

            var first = _samples[0];
            if (time <= first.Time)
            {
                Output(first, first, 0f, out position, out rotation, motors);
                return true;
            }
            var last = Latest;
            if (time >= last.Time)
            {
                // Newer than every sample: extrapolate a little, then hold.
                float ahead = (float)System.Math.Min(time - last.Time, MaxExtrapolation);
                Output(last, last, 0f, out position, out rotation, motors);
                position += last.Velocity * ahead;
                return true;
            }
            for (int i = _samples.Count - 1; i > 0; i--)
            {
                var a = _samples[i - 1];
                if (a.Time > time) continue;
                var b = _samples[i];
                float t = (float)((time - a.Time) / (b.Time - a.Time));
                Output(a, b, t, out position, out rotation, motors);
                return true;
            }
            Output(first, first, 0f, out position, out rotation, motors);
            return true;
        }

        private static void Output(in PilotSnapshot a, in PilotSnapshot b, float t, out Vector3 position, out Quaternion rotation, float[] motors)
        {
            position = Vector3.LerpUnclamped(a.Position, b.Position, t);
            rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            if (motors == null) return;
            for (int i = 0; i < 4 && i < motors.Length; i++) motors[i] = Mathf.Lerp(a.Motor(i), b.Motor(i), t);
        }
    }
}
