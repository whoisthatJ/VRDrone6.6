using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GateRush.UI
{
    /// <summary>
    /// Garage orbit camera: drag with the mouse (or the right stick) to orbit the quad on the mat, scroll (or the
    /// triggers) to zoom. <see cref="Frame"/> fits the view to a model; the turntable spin lives on the quad anchor.
    /// </summary>
    public sealed class GarageCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float yaw = -35f;
        [SerializeField] private float pitch = 24f;
        [SerializeField] private float distance = 0.6f;
        [SerializeField] private float minPitch = 4f, maxPitch = 80f;
        [SerializeField] private float orbitSpeed = 0.25f;
        [SerializeField] private float stickSpeed = 120f;

        private float _minDistance = 0.15f, _maxDistance = 2f;
        private Vector3 _focus;
        private bool _dragging;

        public Vector3 Focus => _focus;
        public float Distance => distance;

        public void Configure(Transform orbitTarget) => target = orbitTarget;

        /// <summary>Frames a model: focus on its centre, distance from its size.</summary>
        public void Frame(Bounds b)
        {
            _focus = b.center;
            float size = Mathf.Max(b.size.x, b.size.z, 0.05f);
            distance = size * 2.2f + 0.05f;
            _minDistance = size * 0.9f;
            _maxDistance = size * 6f + 0.3f;
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame && !overUi) _dragging = true;
                if (!mouse.leftButton.isPressed) _dragging = false;
                if (_dragging)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * orbitSpeed;
                    pitch -= d.y * orbitSpeed;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (!overUi && Mathf.Abs(scroll) > 0.01f) distance *= scroll > 0f ? 0.9f : 1.1f;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 s = pad.rightStick.ReadValue();
                yaw += s.x * stickSpeed * Time.unscaledDeltaTime;
                pitch -= s.y * stickSpeed * Time.unscaledDeltaTime;
                distance *= 1f + (pad.leftTrigger.ReadValue() - pad.rightTrigger.ReadValue()) * Time.unscaledDeltaTime;
            }
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Clamp(distance, _minDistance, _maxDistance);

            Vector3 focus = _focus;
            if (target != null && _focus == Vector3.zero) focus = target.position;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
