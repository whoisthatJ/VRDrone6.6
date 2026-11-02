using UnityEngine;

namespace GateRush.Track
{
    /// <summary>
    /// Feather-flag wind: the cloth swings around the pole with two layered sines and a small lean, phase-offset per
    /// flag from its position so a row of flags never moves in step. Transform only (no per-vertex CPU work).
    /// </summary>
    public sealed class FlagWind : MonoBehaviour
    {
        [Tooltip("The cloth (rotated around the pole's Y axis).")]
        [SerializeField] private Transform cloth;
        [SerializeField] private float swingDegrees = 14f;
        [SerializeField] private float leanDegrees = 3f;
        [SerializeField] private float speed = 1.1f;

        private float _phase;
        private Quaternion _rest;

        public Transform Cloth
        {
            get => cloth;
            set => cloth = value;
        }

        private void Start()
        {
            if (cloth == null) return;
            _rest = cloth.localRotation;
            Vector3 p = transform.position;
            _phase = Mathf.Repeat(p.x * 0.37f + p.z * 0.61f, Mathf.PI * 2f);
        }

        private void Update()
        {
            if (cloth == null) return;
            float t = Time.time * speed + _phase;
            float swing = Mathf.Sin(t) * swingDegrees + Mathf.Sin(t * 2.7f + 1.3f) * swingDegrees * 0.3f;
            float lean = Mathf.Sin(t * 1.6f + 0.5f) * leanDegrees;
            cloth.localRotation = _rest * Quaternion.Euler(lean, swing, 0f);
        }
    }
}
