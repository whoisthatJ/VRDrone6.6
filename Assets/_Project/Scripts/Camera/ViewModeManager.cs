using System;
using GateRush.Vehicle;
using UnityEngine;
using UnityEngine.XR;

namespace GateRush.Cameras
{
    public enum ViewMode
    {
        /// <summary>Drone camera on a curved screen in a dark room (default, most comfortable).</summary>
        Goggles = 0,
        /// <summary>The XR rig rides on the drone with camera tilt; head rotation adds on top. Intense.</summary>
        Immersive = 1,
        /// <summary>The pilot stands on the field and watches the quad.</summary>
        LineOfSight = 2,
    }

    /// <summary>
    /// Switches between the three VR views. Without an active XR device (Editor, no headset) Goggles and
    /// Immersive fall back to the drone camera on the main display, and LOS uses the rig camera looking at the quad.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ViewModeManager : MonoBehaviour
    {
        [SerializeField] private ViewMode mode = ViewMode.Goggles;
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private Transform xrOrigin;
        [SerializeField] private Camera headCamera;
        [SerializeField] private FpvCamera droneCamera;
        [SerializeField] private GogglesScreen gogglesScreen;
        [SerializeField] private GameObject gogglesRoom;
        [SerializeField] private ComfortVignette vignette;
        [SerializeField] private Transform losStand;
        [SerializeField] private LayerMask roomLayer;
        [SerializeField] private LayerMask localQuadLayer;
        [SerializeField] private Vector2Int gogglesResolution = new Vector2Int(1280, 720);
        [SerializeField] private Color roomColor = new Color(0.02f, 0.02f, 0.025f);

        private RenderTexture _rt;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private bool _xrActive;

        public event Action<ViewMode> Changed;

        public ViewMode Mode => mode;

        private void Start()
        {
            if (xrOrigin != null)
            {
                _homePosition = xrOrigin.position;
                _homeRotation = xrOrigin.rotation;
            }
            Apply(mode);
        }

        public void Cycle() => Apply((ViewMode)(((int)mode + 1) % 3));

        public void Apply(ViewMode newMode)
        {
            mode = newMode;
            _xrActive = XRSettings.isDeviceActive;

            var droneCam = droneCamera != null ? droneCamera.Camera : null;
            bool goggles = mode == ViewMode.Goggles;
            bool immersive = mode == ViewMode.Immersive;
            bool los = mode == ViewMode.LineOfSight;

            // Rig placement.
            if (xrOrigin != null)
            {
                if (los && losStand != null) xrOrigin.SetPositionAndRotation(losStand.position, losStand.rotation);
                else xrOrigin.SetPositionAndRotation(_homePosition, _homeRotation);
            }

            // Drone camera: renders to the goggles texture in XR, straight to the display without XR.
            if (droneCam != null)
            {
                bool showDrone = _xrActive ? goggles : !los;
                droneCam.enabled = showDrone;
                droneCam.stereoTargetEye = StereoTargetEyeMask.None;
                droneCam.targetTexture = _xrActive && goggles ? EnsureTexture() : null;
                droneCam.cullingMask = ~(roomLayer | localQuadLayer);
            }

            if (headCamera != null)
            {
                headCamera.enabled = _xrActive || los;
                if (goggles)
                {
                    headCamera.cullingMask = roomLayer;
                    headCamera.clearFlags = CameraClearFlags.SolidColor;
                    headCamera.backgroundColor = roomColor;
                }
                else
                {
                    headCamera.cullingMask = immersive ? ~(roomLayer | localQuadLayer) : ~roomLayer;
                    headCamera.clearFlags = CameraClearFlags.Skybox;
                }
            }

            if (gogglesRoom != null) gogglesRoom.SetActive(goggles && _xrActive);
            if (gogglesScreen != null && goggles && _xrActive)
            {
                gogglesScreen.SetTexture(_rt);
                gogglesScreen.Recenter();
            }
            if (vignette != null) vignette.gameObject.SetActive(immersive && _xrActive);

            Debug.Log($"[GateRush] View mode: {mode} (XR {(_xrActive ? "on" : "off")})");
            Changed?.Invoke(mode);
        }

        private void LateUpdate()
        {
            // XR can come up after Start (e.g. headset put on later); re-apply once it does.
            if (XRSettings.isDeviceActive != _xrActive) Apply(mode);
            if (vehicle == null) return;

            if (mode == ViewMode.Immersive && _xrActive && xrOrigin != null && headCamera != null)
            {
                // Rig rotation = drone attitude + camera uptilt; then shift so the eye sits on the drone.
                float tilt = droneCamera != null ? droneCamera.TiltDegrees : 0f;
                xrOrigin.rotation = vehicle.transform.rotation * Quaternion.Euler(-tilt, 0f, 0f);
                xrOrigin.position += vehicle.transform.position - headCamera.transform.position;
            }
            else if (mode == ViewMode.LineOfSight && !_xrActive && headCamera != null)
            {
                headCamera.transform.LookAt(vehicle.transform.position);
            }
        }

        private RenderTexture EnsureTexture()
        {
            if (_rt != null) return _rt;
            _rt = new RenderTexture(gogglesResolution.x, gogglesResolution.y, 24, RenderTextureFormat.ARGB32)
            {
                name = "GogglesFeed",
                antiAliasing = 2,
                useMipMap = false,
            };
            _rt.Create();
            return _rt;
        }

        private void OnDestroy()
        {
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }
    }
}
