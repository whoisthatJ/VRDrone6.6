using UnityEngine;

namespace GateRush.Race
{
    /// <summary>Segment-vs-gate-opening test: did the motion from a to b fly through the opening in its forward direction?</summary>
    public static class GateCrossing
    {
        /// <param name="center">World centre of the opening.</param>
        /// <param name="rotation">Gate rotation; local +Z is the flight direction, X/Y span the opening.</param>
        /// <param name="halfSize">Half width (x) and half height (y) of the opening.</param>
        /// <param name="fraction">Where along a→b the plane was crossed (0..1), for sub-frame timing.</param>
        public static bool TryCross(Vector3 a, Vector3 b, Vector3 center, Quaternion rotation, Vector2 halfSize, out float fraction)
        {
            fraction = 0f;
            Quaternion inv = Quaternion.Inverse(rotation);
            Vector3 la = inv * (a - center);
            Vector3 lb = inv * (b - center);

            // Must go from behind the plane to on/in front of it.
            if (!(la.z < 0f && lb.z >= 0f)) return false;

            fraction = -la.z / (lb.z - la.z);
            Vector3 p = Vector3.LerpUnclamped(la, lb, fraction);
            return Mathf.Abs(p.x) <= halfSize.x && Mathf.Abs(p.y) <= halfSize.y;
        }
    }
}
