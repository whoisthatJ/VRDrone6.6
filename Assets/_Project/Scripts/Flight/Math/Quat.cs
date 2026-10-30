using System;

namespace GateRush.Flight
{
    /// <summary>
    /// Engine-free unit quaternion with the same component layout and rotation sense as
    /// UnityEngine.Quaternion, so values can be copied across directly.
    /// </summary>
    [Serializable]
    public struct Quat : IEquatable<Quat>
    {
        public float x, y, z, w;

        public Quat(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static readonly Quat Identity = new Quat(0f, 0f, 0f, 1f);

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        /// <summary>Rotates a vector (same result as Unity's <c>q * v</c>).</summary>
        public static Vec3 operator *(Quat q, Vec3 v)
        {
            // v' = v + 2w(u x v) + 2(u x (u x v)), u = (x, y, z)
            var u = new Vec3(q.x, q.y, q.z);
            var t = Vec3.Cross(u, v) * 2f;
            return v + t * q.w + Vec3.Cross(u, t);
        }

        public Quat Conjugate => new Quat(-x, -y, -z, w);

        public Quat Normalized
        {
            get
            {
                float m = MathF.Sqrt(x * x + y * y + z * z + w * w);
                return m > 1e-9f ? new Quat(x / m, y / m, z / m, w / m) : Identity;
            }
        }

        public static Quat AxisAngle(Vec3 axis, float radians)
        {
            var n = axis.Normalized;
            float h = radians * 0.5f;
            float s = MathF.Sin(h);
            return new Quat(n.x * s, n.y * s, n.z * s, MathF.Cos(h));
        }

        /// <summary>Same convention as Unity's Quaternion.Euler (Z, then X, then Y), in degrees.</summary>
        public static Quat EulerDegrees(float xDeg, float yDeg, float zDeg)
        {
            const float d2r = MathF.PI / 180f;
            return AxisAngle(Vec3.Up, yDeg * d2r) * AxisAngle(Vec3.Right, xDeg * d2r) * AxisAngle(Vec3.Forward, zDeg * d2r);
        }

        /// <summary>
        /// Integrates a body-frame angular velocity (rad/s) over dt with the exact
        /// rotation for that step, then renormalises.
        /// </summary>
        public Quat IntegrateBodyRate(Vec3 omegaBody, float dt)
        {
            float angle = omegaBody.Magnitude * dt;
            if (angle < 1e-12f) return this;
            return (this * AxisAngle(omegaBody, angle)).Normalized;
        }

        public bool Equals(Quat o) => x.Equals(o.x) && y.Equals(o.y) && z.Equals(o.z) && w.Equals(o.w);
        public override bool Equals(object obj) => obj is Quat o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(x, y, z, w);
        public override string ToString() => $"({x:F4}, {y:F4}, {z:F4}, {w:F4})";
    }
}
