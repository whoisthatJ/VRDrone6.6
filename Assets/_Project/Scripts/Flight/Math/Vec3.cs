using System;

namespace GateRush.Flight
{
    /// <summary>
    /// Engine-free 3D vector. Uses Unity's axis convention (X right, Y up, Z forward,
    /// left-handed) so the bridge can copy values straight into a Transform.
    /// </summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public float x, y, z;

        public Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 One = new Vec3(1f, 1f, 1f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);

        public float SqrMagnitude => x * x + y * y + z * z;
        public float Magnitude => MathF.Sqrt(SqrMagnitude);

        public Vec3 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-9f ? this / m : Zero;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.x, -a.y, -a.z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.x * s, a.y * s, a.z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.x * s, a.y * s, a.z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.x / s, a.y / s, a.z / s);

        /// <summary>Component-wise product.</summary>
        public static Vec3 Scale(Vec3 a, Vec3 b) => new Vec3(a.x * b.x, a.y * b.y, a.z * b.z);

        /// <summary>Component-wise division.</summary>
        public static Vec3 Divide(Vec3 a, Vec3 b) => new Vec3(a.x / b.x, a.y / b.y, a.z / b.z);

        public static float Dot(Vec3 a, Vec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        public bool Equals(Vec3 o) => x.Equals(o.x) && y.Equals(o.y) && z.Equals(o.z);
        public override bool Equals(object obj) => obj is Vec3 o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x:F4}, {y:F4}, {z:F4})";
    }
}
