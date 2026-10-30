using UnityEngine;

namespace GateRush.Cameras
{
    /// <summary>
    /// FPV camera mounted on the quad: uptilt and horizontal FOV, like a real FPV cam.
    /// Sits on a child of the quad so it inherits the interpolated pose.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FpvCamera : MonoBehaviour
    {
        [SerializeField, Range(0f, 60f)] private float tiltDegrees = 25f;
        [SerializeField, Range(90f, 150f)] private float horizontalFov = 120f;

        private Camera _camera;
        private float _appliedTilt = -1f, _appliedFov = -1f, _appliedAspect = -1f;

        public Camera Camera => _camera != null ? _camera : _camera = GetComponent<Camera>();

        public float TiltDegrees
        {
            get => tiltDegrees;
            set => tiltDegrees = Mathf.Clamp(value, 0f, 60f);
        }

        public float HorizontalFov
        {
            get => horizontalFov;
            set => horizontalFov = Mathf.Clamp(value, 90f, 150f);
        }

        private void LateUpdate() => Apply();

        private void OnValidate() => Apply();

        public void Apply()
        {
            var cam = Camera;
            float aspect = cam.aspect;
            if (tiltDegrees == _appliedTilt && horizontalFov == _appliedFov && aspect == _appliedAspect) return;

            // Uptilt = camera pitched up = negative rotation about the quad's X axis.
            transform.localRotation = Quaternion.Euler(-tiltDegrees, 0f, 0f);
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontalFov, aspect);
            _appliedTilt = tiltDegrees;
            _appliedFov = horizontalFov;
            _appliedAspect = aspect;
        }
    }
}
