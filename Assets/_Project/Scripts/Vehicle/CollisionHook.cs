using GateRush.Flight;
using UnityEngine;

namespace GateRush.Vehicle
{
    /// <summary>
    /// Sweeps the quad's collision sphere along each sub-step's motion against the track geometry layer
    /// and applies a bounce/friction response, or flags a crash on a hard impact.
    /// </summary>
    public struct CollisionHook : IStepHook
    {
        private const float Skin = 0.002f;

        public CollisionSettings Settings;

        /// <summary>Pose before the last sub-step, used for render interpolation.</summary>
        public Vec3 PrevPosition;
        public Quat PrevOrientation;

        public bool Crashed;
        public float LastImpactSpeed;
        /// <summary>Increments on every impact harder than <see cref="CollisionSettings.MinBounceSpeed"/> (effects/audio poll it).</summary>
        public int ImpactCount;

        public bool AfterStep(ref QuadState s, in QuadState previous, float dt)
        {
            PrevPosition = previous.Position;
            PrevOrientation = previous.Orientation;

            Vector3 from = ToUnity(previous.Position);
            Vector3 delta = ToUnity(s.Position) - from;
            float dist = delta.magnitude;
            if (dist < 1e-6f) return true;

            Vector3 dir = delta / dist;
            if (!Physics.SphereCast(from, Settings.Radius, dir, out RaycastHit hit, dist + Skin,
                    Settings.TrackGeometry, QueryTriggerInteraction.Ignore))
                return true;

            Vector3 n = hit.normal;
            Vector3 v = ToUnity(s.Velocity);
            float vn = Vector3.Dot(v, n);
            s.Position = FromUnity(from + dir * Mathf.Max(0f, hit.distance - Skin));
            if (vn >= 0f) return true;

            float impact = -vn;
            LastImpactSpeed = impact;
            if (impact > Settings.MinBounceSpeed) ImpactCount++;
            if (impact > Settings.CrashSpeed)
            {
                Crashed = true;
                s.Velocity = Vec3.Zero;
                s.AngularVelocity = Vec3.Zero;
                return false;
            }

            // Normal response with optional bounce, then Coulomb friction from the normal impulse.
            float e = impact > Settings.MinBounceSpeed ? Settings.Restitution : 0f;
            float dvn = (1f + e) * impact;
            Vector3 vt = v - n * vn;
            float vtMag = vt.magnitude;
            float frictionDv = Mathf.Min(vtMag, Settings.Friction * dvn);
            if (vtMag > 1e-6f) vt -= vt / vtMag * frictionDv;
            v = vt + n * (e * impact);

            s.Velocity = FromUnity(v);
            if (impact > Settings.MinBounceSpeed) s.AngularVelocity = s.AngularVelocity * Settings.AngularDamping;
            return true;
        }

        public static Vector3 ToUnity(Vec3 v) => new Vector3(v.x, v.y, v.z);
        public static Vec3 FromUnity(Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quaternion ToUnity(Quat q) => new Quaternion(q.x, q.y, q.z, q.w);
        public static Quat FromUnity(Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
    }
}
