using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The drone's FPV camera as a sensor: renders only when the session asks for a frame (at simulator-time instants) and
    /// reads the picture back as bytes. It is a separate camera from the one that draws the window.
    ///
    /// The lens bends the picture (barrel distortion): the scene is rendered as a plain perspective view somewhat larger
    /// than the output, and <see cref="LensMap"/> says which rendered pixel each output pixel shows.
    /// </summary>
    public sealed class BenchCameraSensor : MonoBehaviour
    {
        private Camera _camera;
        private RenderTexture _target;
        private Texture2D _readback;
        private NativeArray<int> _map;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int SourceWidth { get; private set; }
        public int SourceHeight { get; private set; }
        public float HorizontalFov { get; private set; }
        public float TiltDegrees { get; private set; }
        /// <summary>Radial distortion coefficient of the lens model (0 = none).</summary>
        public float Distortion { get; private set; }
        /// <summary>Focal length of the output picture in pixels (square pixels, principal point in the middle).</summary>
        public float FocalPixels { get; private set; }
        public Vector3 MountPosition => transform.localPosition;
        /// <summary>Output pixel (top row first) to byte offset in the bottom-up source render.</summary>
        public NativeArray<int> LensMap => _map;

        public void Setup(int width, int height, float horizontalFov, float tiltDegrees, int hiddenLayers, float distortion)
        {
            Width = width;
            Height = height;
            HorizontalFov = horizontalFov;
            TiltDegrees = tiltDegrees;
            Distortion = Mathf.Clamp(distortion, 0f, 0.5f);
            transform.localRotation = Quaternion.Euler(-tiltDegrees, 0f, 0f);

            FocalPixels = AnalogVideo.LensFocal(width, horizontalFov, Distortion);
            AnalogVideo.LensSourceExtent(width, height, FocalPixels, Distortion, out float halfX, out float halfY);
            // Enough source pixels that the middle of the picture keeps the output's sharpness.
            SourceWidth = Mathf.Min(1600, Mathf.CeilToInt(halfX * FocalPixels) * 2);
            SourceHeight = Mathf.Min(1200, Mathf.CeilToInt(halfY * FocalPixels) * 2);

            _camera = gameObject.GetComponent<Camera>();
            if (_camera == null) _camera = gameObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 2f * Mathf.Atan(halfY) * Mathf.Rad2Deg;
            _camera.aspect = halfX / halfY;
            _camera.nearClipPlane = 0.02f;
            _camera.farClipPlane = 300f;
            _camera.cullingMask = ~hiddenLayers;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            // A sensor, not a view: the graphics preferences must not touch it. Post-processing would also bring URP's
            // dithering, whose pattern follows the frame counter: the same flight would not give the same pictures.
            Ushqyr.Core.GraphicsQuality.LeaveAlone(_camera);
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.dithering = false;
            data.stopNaN = false;
            data.renderShadows = true;

            Release();
            _target = new RenderTexture(SourceWidth, SourceHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Bench FPV" };
            _target.Create();
            _camera.targetTexture = _target;
            _readback = new Texture2D(SourceWidth, SourceHeight, TextureFormat.RGB24, false, false);
            _map = new NativeArray<int>(width * height, Allocator.Persistent);
            AnalogVideo.BuildLensMap(_map, width, height, SourceWidth, SourceHeight, FocalPixels, Distortion);
        }

        /// <summary>
        /// Renders the view from the drone's current pose and returns the source render as RGB24, rows bottom-up (the
        /// graphics card's order). The data belongs to the read-back texture and is overwritten by the next call.
        /// </summary>
        public NativeArray<byte> Capture()
        {
            var request = new RenderPipeline.StandardRequest { destination = _target };
            if (RenderPipeline.SupportsRenderRequest(_camera, request)) RenderPipeline.SubmitRenderRequest(_camera, request);
            else _camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _readback.ReadPixels(new Rect(0, 0, SourceWidth, SourceHeight), 0, 0, false);
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
            if (_map.IsCreated) _map.Dispose();
        }

        private void OnDestroy() => Release();
    }
}
