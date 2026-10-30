using System;
using System.IO;
using UnityEngine;

namespace GateRush.Race
{
    /// <summary>
    /// One lap of recorded poses at a fixed sample interval. Preallocated: recording never allocates.
    /// Binary format: "GRG1", version, interval, lap time, count, then count × (pos xyz, rot xyzw).
    /// </summary>
    public sealed class GhostTrack
    {
        private const uint Magic = 0x31475247; // "GRG1"
        private const int Version = 1;

        private readonly Vector3[] _positions;
        private readonly Quaternion[] _rotations;

        public GhostTrack(int capacity, float sampleInterval)
        {
            _positions = new Vector3[capacity];
            _rotations = new Quaternion[capacity];
            SampleInterval = sampleInterval;
        }

        public float SampleInterval { get; private set; }
        public int Count { get; private set; }
        public int Capacity => _positions.Length;
        public float LapTime { get; set; }
        public float Duration => Count > 1 ? (Count - 1) * SampleInterval : 0f;
        public bool IsFull => Count >= _positions.Length;

        public void Clear()
        {
            Count = 0;
            LapTime = 0f;
        }

        public void Add(Vector3 position, Quaternion rotation)
        {
            if (IsFull) return;
            _positions[Count] = position;
            _rotations[Count] = rotation;
            Count++;
        }

        /// <summary>Interpolated pose at time t (seconds from lap start). Clamped to the recorded range.</summary>
        public bool Sample(float t, out Vector3 position, out Quaternion rotation)
        {
            if (Count == 0)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }

            float f = Mathf.Max(0f, t) / SampleInterval;
            int i = Mathf.Min((int)f, Count - 1);
            int j = Mathf.Min(i + 1, Count - 1);
            float k = Mathf.Clamp01(f - i);
            position = Vector3.LerpUnclamped(_positions[i], _positions[j], k);
            rotation = Quaternion.SlerpUnclamped(_rotations[i], _rotations[j], k);
            return true;
        }

        public void CopyFrom(GhostTrack other)
        {
            int n = Mathf.Min(other.Count, Capacity);
            Array.Copy(other._positions, _positions, n);
            Array.Copy(other._rotations, _rotations, n);
            Count = n;
            SampleInterval = other.SampleInterval;
            LapTime = other.LapTime;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(SampleInterval);
            w.Write(LapTime);
            w.Write(Count);
            for (int i = 0; i < Count; i++)
            {
                var p = _positions[i];
                var r = _rotations[i];
                w.Write(p.x); w.Write(p.y); w.Write(p.z);
                w.Write(r.x); w.Write(r.y); w.Write(r.z); w.Write(r.w);
            }
        }

        /// <summary>Reads into this track (up to its capacity). Returns false for a foreign or newer file.</summary>
        public bool Read(BinaryReader r)
        {
            if (r.ReadUInt32() != Magic || r.ReadInt32() != Version) return false;
            SampleInterval = r.ReadSingle();
            LapTime = r.ReadSingle();
            int count = r.ReadInt32();
            Count = 0;
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var q = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                Add(p, q);
            }
            return true;
        }
    }
}
