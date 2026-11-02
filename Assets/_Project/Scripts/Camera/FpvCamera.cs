using Unity.Cinemachine;
using UnityEngine;

namespace GateRush.Cameras
{
    /// <summary>
    /// FPV camera mount on the quad: uptilt and horizontal FOV, like a real FPV cam. Drives a
    /// <see cref="CinemachineCamera"/> on the same object (no body/aim, so it sits exactly on the mount); the rendering
    /// camera follows it while the FPV view is active. A child of the quad, so it inherits the interpolated pose.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class FpvCamera : MonoBehaviour
    {
        [SerializeField, Range(0f, 60f)] private float tiltDegrees = 25f;
        [SerializeField, Range(90f, 150f)] private float horizontalFov = 120f;

        private CinemachineCamera _vcam;
        private float _appliedTilt = -1f, _appliedFov = -1f, _appliedAspect = -1f;

        public CinemachineCamera VirtualCamera => _vcam != null ? _vcam : _vcam = GetComponent<CinemachineCamera>();

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

        /// <summary>Vertical field of view for the current output aspect.</summary>
        public float VerticalFov => Camera.HorizontalToVerticalFieldOfView(horizontalFov, OutputAspect);

        private static float OutputAspect
        {
            get
            {
                var main = Camera.main;
                if (main != null) return main.aspect;
                return Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            }
        }

        private void LateUpdate() => Apply();

        private void OnValidate() => Apply();

        public void Apply()
        {
            var vcam = VirtualCamera;
            if (vcam == null) return;
            float aspect = OutputAspect;
            if (tiltDegrees == _appliedTilt && horizontalFov == _appliedFov && aspect == _appliedAspect) return;

            // Uptilt = camera pitched up = negative rotation about the quad's X axis.
            transform.localRotation = Quaternion.Euler(-tiltDegrees, 0f, 0f);
            var lens = vcam.Lens;
            lens.FieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontalFov, aspect);
            vcam.Lens = lens;
            _appliedTilt = tiltDegrees;
            _appliedFov = horizontalFov;
            _appliedAspect = aspect;
        }
    }
}
