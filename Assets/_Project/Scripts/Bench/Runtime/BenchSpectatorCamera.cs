using UnityEngine;
using UnityEngine.InputSystem;

namespace Ushqyr.Bench
{
    public enum SpectatorView { Chase, Overview, Onboard, Trackside, Free }

    /// <summary>
    /// The window's view of a bench run or replay: a smooth chase behind the drone, a fixed overview, the onboard view,
    /// the nearest trackside position, or a free camera (W A S D, Q/E down/up, hold the right mouse button to look).
    /// Key C switches. Spectator only: it never feeds the program.
    /// </summary>
    public sealed class BenchSpectatorCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 overviewPosition = new Vector3(0f, 14f, -16f);
        [SerializeField] private Vector3 overviewLookAt = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private float distance = 0.9f;
        [SerializeField] private float height = 0.32f;
        [SerializeField] private Vector3[] tracksidePositions = new Vector3[0];

        private Vector3 _velocity;
        private float _yaw, _freeYaw, _freePitch;
        private bool _placed;
        private Camera _camera;
        private float _baseFov;

        public SpectatorView View { get; set; } = SpectatorView.Chase;
        /// <summary>Views offered by key C; a live run has no use for the free camera's keys, a replay offers all.</summary>
        public bool AllViews { get; set; }
        /// <summary>Upward tilt of the onboard view (the FPV camera's).</summary>
        public float OnboardTilt { get; set; } = 20f;

        public bool Overview
        {
            get => View == SpectatorView.Overview;
            set => View = value ? SpectatorView.Overview : SpectatorView.Chase;
        }

        public void Next()
        {
            int count = AllViews ? 5 : 2;
            View = (SpectatorView)(((int)View + 1) % count);
            _placed = false;
            if (View == SpectatorView.Free)
            {
                Vector3 e = transform.eulerAngles;
                _freeYaw = e.y;
                _freePitch = e.x > 180f ? e.x - 360f : e.x;
            }
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _baseFov = _camera != null ? _camera.fieldOfView : 60f;
        }

        private void LateUpdate()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) Next();
            if (target == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (_camera != null) _camera.fieldOfView = View == SpectatorView.Onboard ? 95f : _baseFov;

            switch (View)
            {
                case SpectatorView.Overview:
                {
                    transform.position = overviewPosition;
                    // Mostly the hall, nudged towards the drone so it stays findable.
                    Vector3 look = Vector3.Lerp(overviewLookAt, target.position, 0.35f);
                    transform.rotation = Quaternion.LookRotation(look - overviewPosition, Vector3.up);
                    break;
                }
                case SpectatorView.Onboard:
                    transform.SetPositionAndRotation(target.TransformPoint(new Vector3(0f, 0.012f, 0.028f)),
                        target.rotation * Quaternion.Euler(-OnboardTilt, 0f, 0f));
                    break;
                case SpectatorView.Trackside when tracksidePositions.Length > 0:
                {
                    Vector3 best = tracksidePositions[0];
                    foreach (Vector3 p in tracksidePositions)
                        if ((p - target.position).sqrMagnitude < (best - target.position).sqrMagnitude) best = p;
                    transform.position = best;
                    transform.rotation = Quaternion.LookRotation(target.position - best, Vector3.up);
                    if (_camera != null)
                        _camera.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(2f / Mathf.Max(1f, Vector3.Distance(best, target.position))) * Mathf.Rad2Deg, 8f, 60f);
                    break;
                }
                case SpectatorView.Free:
                    FreeFly(kb, dt);
                    break;
                default:
                {
                    // Follow the heading only (not the pitch and roll), so the picture stays calm.
                    _yaw = _placed ? Mathf.LerpAngle(_yaw, target.eulerAngles.y, 1f - Mathf.Exp(-4f * dt)) : target.eulerAngles.y;
                    Vector3 back = Quaternion.Euler(0f, _yaw, 0f) * Vector3.back;
                    Vector3 wanted = target.position + back * distance + Vector3.up * height;
                    transform.position = _placed ? Vector3.SmoothDamp(transform.position, wanted, ref _velocity, 0.12f, Mathf.Infinity, dt) : wanted;
                    transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 0.1f - transform.position, Vector3.up);
                    _placed = true;
                    break;
                }
            }
        }

        private void FreeFly(Keyboard kb, float dt)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _freeYaw += d.x * 0.15f;
                _freePitch = Mathf.Clamp(_freePitch - d.y * 0.15f, -85f, 85f);
            }
            transform.rotation = Quaternion.Euler(_freePitch, _freeYaw, 0f);
            if (kb == null) return;
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move += Vector3.down;
            float speed = kb.leftShiftKey.isPressed ? 12f : 4f;
            transform.position += transform.rotation * move * (speed * dt);
        }
    }
}
