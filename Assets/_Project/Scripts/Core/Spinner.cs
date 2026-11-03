using UnityEngine;

namespace GateRush.Core
{
    /// <summary>Turns its transform at a steady rate about a local axis (wind turbine rotors, fans).</summary>
    public sealed class Spinner : MonoBehaviour
    {
        [SerializeField] private Vector3 axis = Vector3.forward;
        [SerializeField] private float rpm = 18f;

        public void Configure(Vector3 localAxis, float revolutionsPerMinute)
        {
            axis = localAxis;
            rpm = revolutionsPerMinute;
        }

        private void Update() => transform.Rotate(axis, rpm * 6f * Time.deltaTime, Space.Self);
    }
}
