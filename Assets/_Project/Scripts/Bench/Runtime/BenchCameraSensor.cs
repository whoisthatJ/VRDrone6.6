using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The drone's FPV camera as a sensor: renders only when the session asks for a frame (at simulator-time instants) and
    /// reads the picture back as bytes. It is a separate camera from the one that draws the window.
    /// </summary>
    public sealed class BenchCameraSensor : MonoBehaviour
    {
        private Camera _camera;
        private RenderTexture _target;
        private Texture2D _readback;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public float HorizontalFov { get; private set; }
        public float TiltDegrees { get; private set; }
        /// <summary>Focal length in pixels (square pixels, principal point in the middle).</summary>
        public float FocalPixels => Width * 0.5f / Mathf.Tan(HorizontalFov * 0.5f * Mathf.Deg2Rad);
        public Vector3 MountPosition => transform.localPosition;

        public void Setup(int width, int height, float horizontalFov, float tiltDegrees, int hiddenLayers)
        {
            Width = width;
            Height = height;
            HorizontalFov = horizontalFov;
            TiltDegrees = tiltDegrees;
            transform.localRotation = Quaternion.Euler(-tiltDegrees, 0f, 0f);

            _camera = gameObject.GetComponent<Camera>();
            if (_camera == null) _camera = gameObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontalFov, (float)width / height);
            _camera.nearClipPlane = 0.02f;
            _camera.farClipPlane = 300f;
            _camera.cullingMask = ~hiddenLayers;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;

            Release();
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Bench FPV" };
            _target.Create();
            _camera.targetTexture = _target;
            _readback = new Texture2D(width, height, TextureFormat.RGB24, false, false);
        }

        /// <summary>
        /// Renders the view from the drone's current pose and returns it as RGB24, rows bottom-up (the graphics card's
        /// order). The data belongs to the read-back texture and is overwritten by the next call.
        /// </summary>
        public Unity.Collections.NativeArray<byte> Capture()
        {
            var request = new RenderPipeline.StandardRequest { destination = _target };
            if (RenderPipeline.SupportsRenderRequest(_camera, request)) RenderPipeline.SubmitRenderRequest(_camera, request);
            else _camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            RenderTexture.active = previous;
            return _readback.GetRawTextureData<byte>();
        }

        private void Release()
        {
            if (_target != null)
            {
                if (_camera != null) _camera.targetTexture = null;
                _target.Release();
                Destroy(_target);
            }
            if (_readback != null) Destroy(_readback);
        }

        private void OnDestroy() => Release();
    }
}
