using System.Globalization;
using System.IO;
using Unity.Cinemachine;
using Ushqyr.Cameras;
using Ushqyr.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The stabilised camera of a camera drone (EDU_PLAN F3): the picture stays level while the drone leans, the
    /// camera tilts from straight down to a little above the horizon, zooms, takes photos and carries a light.
    /// <para>Page Up / Page Down tilt, Home looks ahead again, Z and X zoom, P takes a photo, O switches the light.</para>
    /// A photo is an event of the flight (where from, which way, how far zoomed), so an exercise can be scored on
    /// what was photographed; it is also saved as a picture in the user's data folder.
    /// </summary>
    [DefaultExecutionOrder(200)] // after the on-board camera has set its own tilt
    [DisallowMultipleComponent]
    public sealed class EduGimbal : MonoBehaviour
    {
        public const float LowestTilt = -90f, HighestTilt = 30f, WidestFov = 84f, MaxZoom = 4f;

        private QuadVehicle _vehicle;
        private FpvCamera _camera;
        private Transform _model;
        private Light _light;
        private Quaternion _stuckAt = Quaternion.identity;
        private bool _wasStuck;

        /// <summary>Degrees above the horizon (negative looks down).</summary>
        public float TiltDeg { get; set; }
        /// <summary>1 is the whole picture; 4 is four times closer.</summary>
        public float Zoom { get; set; } = 1f;
        /// <summary>A failure: the gimbal no longer moves or stabilises; the camera is fixed to the drone as it was.</summary>
        public bool Stuck { get; set; }
        public bool LightOn => _light != null && _light.enabled;
        public int Photos { get; private set; }

        /// <summary>Puts the gimbal on the drone and takes over the on-board camera; <paramref name="model"/> is the part of the model that turns.</summary>
        public static EduGimbal Attach(QuadVehicle vehicle, FpvCamera camera, Transform model)
        {
            if (vehicle == null) return null;
            EduGimbal gimbal = vehicle.GetComponent<EduGimbal>();
            if (gimbal == null) gimbal = vehicle.gameObject.AddComponent<EduGimbal>();
            gimbal._vehicle = vehicle;
            gimbal._camera = camera;
            gimbal._model = model;
            gimbal.TiltDeg = 0f;
            gimbal.Zoom = 1f;
            gimbal.Stuck = false;
            gimbal.enabled = true;
            return gimbal;
        }

        /// <summary>Takes the gimbal off (another drone was chosen): the on-board camera is fixed to the drone again.</summary>
        public static void Detach(QuadVehicle vehicle)
        {
            EduGimbal gimbal = vehicle != null ? vehicle.GetComponent<EduGimbal>() : null;
            if (gimbal == null || !gimbal.enabled) return;
            gimbal.enabled = false;
            if (gimbal._light != null) gimbal._light.enabled = false;
            if (gimbal._camera != null) gimbal._camera.Invalidate();
        }

        public void SetLight(bool on)
        {
            if (_light == null)
            {
                if (!on || _camera == null) return;
                var go = new GameObject("Gimbal Light");
                go.transform.SetParent(_camera.transform, false);
                _light = go.AddComponent<Light>();
                _light.type = LightType.Spot;
                _light.range = 45f;
                _light.spotAngle = 38f;
                _light.innerSpotAngle = 24f;
                _light.intensity = 600f;
                _light.shadows = LightShadows.None;
            }
            _light.enabled = on;
        }

        /// <summary>Takes a photo: an event of the flight, and a picture file. Returns the file's path.</summary>
        public string TakePhoto()
        {
            Photos++;
            CultureInfo inv = CultureInfo.InvariantCulture;
            Vector3 p = _camera != null ? _camera.transform.position : transform.position;
            float heading = transform.eulerAngles.y;
            EduRecorder recorder = GetComponent<EduRecorder>();
            if (recorder != null)
                recorder.Event("photo", string.Format(inv, "{0:0.00};{1:0.00};{2:0.00};{3:0.0};{4:0.0};{5:0.0}", p.x, p.y, p.z, heading, TiltDeg, Zoom));
            string file = Path.Combine(EduSession.Folder, "photos", "photo-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss", inv) + "-" + Photos + ".png");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                ScreenCapture.CaptureScreenshot(file);
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Edu] photo not saved: " + e.Message);
            }
            return file;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || EduReplayViewer.Current != null) return;
            if (!Stuck)
            {
                float turn = (kb.pageUpKey.isPressed ? 1f : 0f) - (kb.pageDownKey.isPressed ? 1f : 0f);
                TiltDeg = Mathf.Clamp(TiltDeg + turn * 45f * Time.unscaledDeltaTime, LowestTilt, HighestTilt);
                if (kb.homeKey.wasPressedThisFrame) TiltDeg = 0f;
            }
            float zoom = (kb.zKey.isPressed ? 1f : 0f) - (kb.xKey.isPressed ? 1f : 0f);
            Zoom = Mathf.Clamp(Zoom * Mathf.Exp(zoom * 1.2f * Time.unscaledDeltaTime), 1f, MaxZoom);
            if (kb.pKey.wasPressedThisFrame) TakePhoto();
            if (kb.oKey.wasPressedThisFrame) SetLight(!LightOn);
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            Transform cam = _camera.transform;
            if (Stuck)
            {
                // It stays as it was relative to the drone and shakes with it.
                if (!_wasStuck) _stuckAt = Quaternion.Inverse(transform.rotation) * cam.rotation;
                cam.rotation = transform.rotation * _stuckAt;
            }
            else
            {
                // Level with the world: only the drone's heading turns the picture, never its lean.
                cam.rotation = Quaternion.Euler(-TiltDeg, transform.eulerAngles.y, 0f);
            }
            _wasStuck = Stuck;
            if (_model != null) _model.rotation = cam.rotation;

            CinemachineCamera lens = _camera.VirtualCamera;
            if (lens == null) return;
            Camera main = Camera.main;
            float aspect = main != null ? main.aspect : 16f / 9f;
            float horizontal = 2f * Mathf.Atan(Mathf.Tan(WidestFov * 0.5f * Mathf.Deg2Rad) / Zoom) * Mathf.Rad2Deg;
            LensSettings settings = lens.Lens;
            settings.FieldOfView = Camera.HorizontalToVerticalFieldOfView(horizontal, aspect);
            lens.Lens = settings;
        }
    }
}
