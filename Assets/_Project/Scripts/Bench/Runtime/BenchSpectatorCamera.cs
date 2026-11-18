using UnityEngine;
using UnityEngine.InputSystem;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The window's view of a bench run: a smooth chase behind the drone, or a fixed overview of the whole track
    /// (key C switches). Spectator only: it never feeds the program.
    /// </summary>
    public sealed class BenchSpectatorCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 overviewPosition = new Vector3(0f, 14f, -16f);
        [SerializeField] private Vector3 overviewLookAt = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private float distance = 0.9f;
        [SerializeField] private float height = 0.32f;

        private Vector3 _velocity;
        private float _yaw;
        private bool _placed;

        public bool Overview { get; set; }

        public void Configure(Transform quad, Vector3 overviewFrom, Vector3 overviewAt)
        {
            target = quad;
            overviewPosition = overviewFrom;
            overviewLookAt = overviewAt;
        }

        private void LateUpdate()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) Overview = !Overview;
            if (target == null) return;

            if (Overview)
            {
                transform.position = overviewPosition;
                // Mostly the hall, nudged towards the drone so it stays findable.
                Vector3 look = Vector3.Lerp(overviewLookAt, target.position, 0.35f);
                transform.rotation = Quaternion.LookRotation(look - overviewPosition, Vector3.up);
                _placed = false;
                return;
            }

            // Follow the heading only (not the pitch and roll), so the picture stays calm.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _yaw = _placed ? Mathf.LerpAngle(_yaw, target.eulerAngles.y, 1f - Mathf.Exp(-4f * dt)) : target.eulerAngles.y;
            Vector3 back = Quaternion.Euler(0f, _yaw, 0f) * Vector3.back;
            Vector3 wanted = target.position + back * distance + Vector3.up * height;
            transform.position = _placed ? Vector3.SmoothDamp(transform.position, wanted, ref _velocity, 0.12f, Mathf.Infinity, dt) : wanted;
            transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 0.1f - transform.position, Vector3.up);
            _placed = true;
        }
    }
}
