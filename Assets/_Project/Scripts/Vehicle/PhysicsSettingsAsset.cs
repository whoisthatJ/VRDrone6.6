using GateRush.Flight;
using UnityEngine;

namespace GateRush.Vehicle
{
    [System.Serializable]
    public struct CollisionSettings
    {
        /// <summary>Collision sphere radius around the quad's centre (m).</summary>
        public float Radius;
        /// <summary>Bounce factor for the normal velocity (0..1).</summary>
        public float Restitution;
        /// <summary>Coulomb friction coefficient against the surface.</summary>
        public float Friction;
        /// <summary>Normal impact speed above which the quad crashes (m/s).</summary>
        public float CrashSpeed;
        /// <summary>Impacts slower than this don't bounce (stops jitter when resting).</summary>
        public float MinBounceSpeed;
        /// <summary>Fraction of angular velocity kept after a non-crash impact (0..1).</summary>
        public float AngularDamping;
        /// <summary>Seconds after a crash before respawning.</summary>
        public float RespawnDelay;
        /// <summary>Falling below this world height counts as a crash.</summary>
        public float OutOfBoundsY;
        public LayerMask TrackGeometry;

        public static CollisionSettings Default => new CollisionSettings
        {
            Radius = 0.12f,
            Restitution = 0.3f,
            Friction = 0.6f,
            CrashSpeed = 7f,
            MinBounceSpeed = 1f,
            AngularDamping = 0.5f,
            RespawnDelay = 1.5f,
            OutOfBoundsY = -20f,
            TrackGeometry = ~0,
        };
    }

    [CreateAssetMenu(menuName = "GateRush/Physics Settings", fileName = "PhysicsSettings")]
    public sealed class PhysicsSettingsAsset : ScriptableObject
    {
        public PhysicsSettings Simulation = PhysicsSettings.Default;
        public CollisionSettings Collision = CollisionSettings.Default;
    }
}
