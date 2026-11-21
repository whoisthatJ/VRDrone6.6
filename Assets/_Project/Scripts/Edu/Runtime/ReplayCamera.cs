using UnityEngine;
using UnityEngine.InputSystem;

namespace Ushqyr.Edu
{
    public enum ReplayView { Chase, Onboard, Fixed, TopDown, Free }

    /// <summary>
    /// Where the replay viewer looks from (EDU_PLAN F2): behind the drone, from the drone, from a fixed place that
    /// follows it with its eyes, straight down from above, or a free camera (W A S D, Q/E down and up, hold the right
    /// mouse button to look, the wheel changes the height of the view from above).
    /// </summary>
    public sealed class ReplayCamera
    {
        private readonly Camera _camera;
        private readonly float _baseFov;
        private Vector3 _velocity, _fixedPlace;
        private float _yaw, _freeYaw, _freePitch, _topHeight = 9f;
        private bool _placed;

        public ReplayView View { get; private set; } = ReplayView.Chase;
        /// <summary>Upward tilt of the onboard view, degrees.</summary>
        public float OnboardTilt = 25f;
        /// <summary>How far behind and above the drone the chase view sits, in metres; set from the drone's size.</summary>
        public float ChaseDistance = 1.6f, ChaseHeight = 0.5f;

        public ReplayCamera(Camera camera)
        {
            _camera = camera;
            _baseFov = camera.fieldOfView;
        }

        public Vector3 Position => _camera.transform.position;

        public void Next() => Set((ReplayView)(((int)View + 1) % 5));

        public void Set(ReplayView view)
        {
            Transform t = _camera.transform;
            if (view == ReplayView.Free)
            {
                Vector3 e = t.eulerAngles;
                _freeYaw = e.y;
                _freePitch = e.x > 180f ? e.x - 360f : e.x;
            }
            // The fixed camera stands where the viewer was looking from when it was chosen.
            if (view == ReplayView.Fixed) _fixedPlace = View == ReplayView.Onboard ? t.position + Vector3.up * 1.5f - t.forward * 4f : t.position;
            View = view;
            _placed = false;
        }

        /// <summary>Once a frame, after the drone was put at the cursor. <paramref name="jumped"/>: the timeline was moved by hand.</summary>
        public void Update(Vector3 position, Quaternion rotation, float dt, bool jumped)
        {
            Transform t = _camera.transform;
            dt = Mathf.Min(dt, 0.05f);
            if (jumped) _placed = false;
            _camera.fieldOfView = View == ReplayView.Onboard ? 100f : _baseFov;
            _camera.orthographic = false;
            switch (View)
            {
                case ReplayView.Onboard:
                    t.SetPositionAndRotation(position + rotation * new Vector3(0f, 0.02f, 0.03f), rotation * Quaternion.Euler(-OnboardTilt, 0f, 0f));
                    break;
                case ReplayView.Fixed:
                {
                    t.position = _fixedPlace;
                    Vector3 to = position - _fixedPlace;
                    if (to.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(to, Vector3.up);
                    // Zoom in as the drone flies away, so it stays a readable size.
                    _camera.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(3f / Mathf.Max(1f, to.magnitude)) * Mathf.Rad2Deg, 8f, _baseFov);
                    break;
                }
                case ReplayView.TopDown:
                {
                    Mouse mouse = Mouse.current;
                    if (mouse != null) _topHeight = Mathf.Clamp(_topHeight - mouse.scroll.ReadValue().y * 0.01f * _topHeight, 4f, 150f);
                    // North (+Z) is up on the screen and stays there: the picture is a map.
                    t.SetPositionAndRotation(position + Vector3.up * _topHeight, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    break;
                }
                case ReplayView.Free:
                    FreeFly(t, dt);
                    break;
                default:
                {
                    // Follow the heading only (not the pitch and roll), so the picture stays calm.
                    float heading = rotation.eulerAngles.y;
                    _yaw = _placed ? Mathf.LerpAngle(_yaw, heading, 1f - Mathf.Exp(-4f * dt)) : heading;
                    Vector3 wanted = position + Quaternion.Euler(0f, _yaw, 0f) * Vector3.back * ChaseDistance + Vector3.up * ChaseHeight;
                    t.position = _placed ? Vector3.SmoothDamp(t.position, wanted, ref _velocity, 0.12f, Mathf.Infinity, dt) : wanted;
                    t.rotation = Quaternion.LookRotation(position + Vector3.up * 0.1f - t.position, Vector3.up);
                    _placed = true;
                    break;
                }
            }
        }

        private void FreeFly(Transform t, float dt)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _freeYaw += d.x * 0.15f;
                _freePitch = Mathf.Clamp(_freePitch - d.y * 0.15f, -85f, 85f);
            }
            t.rotation = Quaternion.Euler(_freePitch, _freeYaw, 0f);
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed) move += Vector3.back;
            if (kb.aKey.isPressed) move += Vector3.left;
            if (kb.dKey.isPressed) move += Vector3.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move += Vector3.down;
            t.position += t.rotation * move * ((kb.leftShiftKey.isPressed ? 14f : 5f) * dt);
        }
    }
}
