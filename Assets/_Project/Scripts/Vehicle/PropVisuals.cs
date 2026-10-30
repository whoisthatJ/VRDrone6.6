using UnityEngine;

namespace GateRush.Vehicle
{
    /// <summary>Spins the four prop transforms from motor speed. Order matches the mixer (RR, FR, RL, FL).</summary>
    public sealed class PropVisuals : MonoBehaviour
    {
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private Transform[] props = new Transform[4];
        [Tooltip("Visual spin at full motor speed (deg/s). Kept low so the props read as blurred discs, not strobing.")]
        [SerializeField] private float maxVisualDegPerSec = 2200f;

        private static readonly float[] Direction = { -1f, 1f, 1f, -1f }; // CW, CCW, CCW, CW seen from above

        private void LateUpdate()
        {
            if (vehicle == null) return;
            ref readonly var s = ref vehicle.State;
            float dt = Time.deltaTime;
            for (int i = 0; i < props.Length && i < 4; i++)
            {
                if (props[i] == null) continue;
                float speed = s.MotorSpeed[i];
                props[i].Rotate(0f, Direction[i] * speed * maxVisualDegPerSec * dt, 0f, Space.Self);
            }
        }
    }
}
