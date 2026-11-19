using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The trackside video: a few fixed camera positions around the venue. For every recorded frame the position with
    /// the best view of the drone is used and the camera turns to follow it, like an operator on a tripod. It renders
    /// only when the recorder asks, at simulator-time instants.
    /// </summary>
    public sealed class BenchTracksideCamera : MonoBehaviour
    {
        [SerializeField] private Vector3[] positions = { new Vector3(0f, 6f, -9f) };

        private Camera _camera;
        private RenderTexture _target;
        private Texture2D _readback;
        private byte[] _raw;
        private int _current = -1;
        private double _switchedAt = double.NegativeInfinity;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public void SetPositions(Vector3[] p) => positions = p;

        public void Setup(int width, int height)
        {
            Width = width;
            Height = height;
            _camera = gameObject.GetComponent<Camera>();
            if (_camera == null) _camera = gameObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 300f;
            _camera.allowHDR = true;
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Bench Trackside" };
            _target.Create();
            _camera.targetTexture = _target;
            _readback = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            _raw = new byte[width * height * 3];
            _current = -1;
            _switchedAt = double.NegativeInfinity;
        }

        /// <summary>One frame looking at <paramref name="drone"/>; RGB24, rows bottom-up. The array is reused.</summary>
        public byte[] Capture(Vector3 drone, double simTime)
        {
            // Nearest position wins, but a cut needs a clear advantage and a moment since the last cut.
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < positions.Length; i++)
            {
                float d = (positions[i] - drone).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            if (_current < 0 || (best != _current && simTime - _switchedAt > 1.5 &&
                                 bestDistance < (positions[_current] - drone).sqrMagnitude * 0.6f))
            {
                _current = best;
                _switchedAt = simTime;
            }

            Vector3 from = positions[_current];
            float distance = Mathf.Max(1f, Vector3.Distance(from, drone));
            transform.SetPositionAndRotation(from, Quaternion.LookRotation(drone - from, Vector3.up));
            // Keep about four metres of the scene across the picture: the drone is small, a gate gives the scale.
            _camera.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(2f / distance) * Mathf.Rad2Deg * Height / Width, 8f, 60f);

            var request = new RenderPipeline.StandardRequest { destination = _target };
            if (RenderPipeline.SupportsRenderRequest(_camera, request)) RenderPipeline.SubmitRenderRequest(_camera, request);
            else _camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            RenderTexture.active = previous;
            _readback.GetRawTextureData<byte>().CopyTo(_raw);
            return _raw;
        }

        private void OnDestroy()
        {
            if (_target != null)
            {
                if (_camera != null) _camera.targetTexture = null;
                _target.Release();
                Destroy(_target);
            }
            if (_readback != null) Destroy(_readback);
        }
    }
}
