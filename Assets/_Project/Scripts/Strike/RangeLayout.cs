using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ushqyr.Strike
{
    /// <summary>Fictional vehicle silhouettes (no real models, insignia or countries).</summary>
    public enum VehicleKind
    {
        Tracked = 0,
        Wheeled = 1,
        Truck = 2,
        Artillery = 3,
        RadarTruck = 4,
    }

    [Serializable]
    public struct PadSpot
    {
        public Vector3 Position;
        public float Yaw;
    }

    [Serializable]
    public struct StaticSpot
    {
        public Vector3 Position;
        public float Yaw;
        /// <summary>Inside an earth revetment (a U of berms).</summary>
        public bool Revetment;
    }

    /// <summary>A dirt road: an open polyline on the terrain (points every few metres).</summary>
    [Serializable]
    public sealed class RangeRoad
    {
        public Vector3[] Points = new Vector3[0];
        [NonSerialized] private float[] _cum;

        public float Length
        {
            get
            {
                Ensure();
                return _cum.Length > 0 ? _cum[_cum.Length - 1] : 0f;
            }
        }

        private void Ensure()
        {
            if (_cum != null && _cum.Length == Points.Length) return;
            _cum = new float[Points.Length];
            for (int i = 1; i < Points.Length; i++) _cum[i] = _cum[i - 1] + Vector3.Distance(Points[i - 1], Points[i]);
        }

        /// <summary>Point and flat direction at distance <paramref name="d"/> along the road (clamped to the ends).</summary>
        public void Sample(float d, out Vector3 position, out Vector3 forward)
        {
            Ensure();
            int n = Points.Length;
            if (n < 2)
            {
                position = n > 0 ? Points[0] : Vector3.zero;
                forward = Vector3.forward;
                return;
            }
            d = Mathf.Clamp(d, 0f, _cum[n - 1]);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_cum[mid] <= d) lo = mid;
                else hi = mid;
            }
            float seg = Mathf.Max(1e-4f, _cum[hi] - _cum[lo]);
            float u = (d - _cum[lo]) / seg;
            position = Vector3.Lerp(Points[lo], Points[hi], u);
            Vector3 f = Points[hi] - Points[lo];
            f.y = 0f;
            forward = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }
    }

    /// <summary>
    /// The range as data (P5): launch pads, static spots, dirt roads and a heightfield of the terrain. Written by the
    /// Range_Steppe generator and stored in the scene, so target motion, bots and tests all read the same numbers.
    /// </summary>
    [CreateAssetMenu(menuName = "Ushqyr/Range Layout", fileName = "RangeLayout")]
    public sealed class RangeLayout : ScriptableObject
    {
        public PadSpot[] Pads = new PadSpot[0];
        public StaticSpot[] Spots = new StaticSpot[0];
        public List<RangeRoad> Roads = new List<RangeRoad>();
        /// <summary>Heightfield: <see cref="HeightRes"/>² samples covering <see cref="Size"/> m centred on the origin.</summary>
        public float Size = 1200f;
        public int HeightRes;
        public float[] Heights = new float[0];

        /// <summary>Terrain height (m) at a world x / z, bilinear from the heightfield.</summary>
        public float Height(float x, float z)
        {
            if (HeightRes < 2 || Heights.Length < HeightRes * HeightRes) return 0f;
            float half = Size * 0.5f, cell = Size / (HeightRes - 1);
            float fx = Mathf.Clamp((x + half) / cell, 0f, HeightRes - 1.001f);
            float fz = Mathf.Clamp((z + half) / cell, 0f, HeightRes - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float u = fx - ix, w = fz - iz;
            float a = Heights[iz * HeightRes + ix], b = Heights[iz * HeightRes + ix + 1];
            float c = Heights[(iz + 1) * HeightRes + ix], d = Heights[(iz + 1) * HeightRes + ix + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), w);
        }

        public Vector3 OnGround(Vector3 p) => new Vector3(p.x, Height(p.x, p.z), p.z);
    }
}
