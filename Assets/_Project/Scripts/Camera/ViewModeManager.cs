using System;
using GateRush.Vehicle;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.Cameras
{
    public enum ViewMode
    {
        /// <summary>The quad's FPV camera, full screen (default).</summary>
        Fpv = 0,
        /// <summary>Third-person camera behind and above the quad.</summary>
        Chase = 1,
        /// <summary>The pilot stands by the start pad and watches the quad.</summary>
        LineOfSight = 2,
    }

    /// <summary>
    /// Desktop camera set: one rendering camera with a <see cref="CinemachineBrain"/> and three Cinemachine cameras
    /// (FPV on the quad, chase, line of sight from the pilot stand). Only the active view's camera is enabled, so the
    /// brain cuts straight to it. The pilot's own quad is hidden in FPV; the FPV lens-distortion volume only applies
    /// in FPV. <see cref="SetFreeCamera"/> hands the camera to another controller (the track editor).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ViewModeManager : MonoBehaviour
    {
        [SerializeField] private ViewMode mode = ViewMode.Fpv;
        [SerializeField] private QuadVehicle vehicle;
        [Tooltip("The camera that renders (has the CinemachineBrain).")]
        [SerializeField] private Camera outputCamera;
        [SerializeField] private FpvCamera fpv;
        [SerializeField] private CinemachineCamera chase;
        [Tooltip("Chase offset for a 5\" quad (visual scale 1); smaller quads get a proportionally closer camera.")]
        [SerializeField] private Vector3 chaseOffset = new Vector3(0f, 0.6f, -1.6f);
        [SerializeField] private CinemachineCamera lineOfSight;
        [Tooltip("Where the line-of-sight pilot stands (moved next to the track's spawn).")]
        [SerializeField] private Transform losStand;
        [SerializeField] private LayerMask localQuadLayer;
        [Tooltip("Barrel distortion for the FPV lens; weight is driven by the view and the setting.")]
        [SerializeField] private Volume lensVolume;
        [Tooltip("Output camera pose on its rig when no view drives it (track editor): eye height, level.")]
        [SerializeField] private Vector3 cameraHome = new Vector3(0f, 1.6f, 0f);

        private bool _free;
        private bool _lensDistortion;
        private int _fullMask = ~0;

        public event Action<ViewMode> Changed;

        public ViewMode Mode => mode;
        public Camera OutputCamera => outputCamera;
        public Transform LosStand => losStand;

        /// <summary>FPV barrel distortion on/off (settings).</summary>
        public bool LensDistortion
        {
            get => _lensDistortion;
            set
            {
                _lensDistortion = value;
                UpdateLens();
            }
        }

        private void Awake()
        {
            if (outputCamera != null) _fullMask = outputCamera.cullingMask;
        }

        private void Start() => Apply(mode);

        public void Cycle() => Apply((ViewMode)(((int)mode + 1) % 3));

        public void Apply(ViewMode newMode)
        {
            mode = newMode;
            bool fpvOn = !_free && mode == ViewMode.Fpv;
            if (fpv != null && fpv.VirtualCamera != null) fpv.VirtualCamera.enabled = fpvOn;
            if (chase != null) chase.enabled = !_free && mode == ViewMode.Chase;
            if (lineOfSight != null) lineOfSight.enabled = !_free && mode == ViewMode.LineOfSight;
            if (outputCamera != null) outputCamera.cullingMask = fpvOn ? _fullMask & ~localQuadLayer : _fullMask;
            UpdateLens();
            if (!_free) Debug.Log($"[GateRush] View mode: {mode}");
            Changed?.Invoke(mode);
        }

        /// <summary>
        /// True: every Cinemachine camera is off, so another script (the track editor) moves the output camera.
        /// False: back to the current view.
        /// </summary>
        public void SetFreeCamera(bool free)
        {
            _free = free;
            if (outputCamera != null)
            {
                // The brain would keep placing the camera (it moves it in world space); switch it off and hand the
                // camera over at its home pose on the rig.
                var brain = outputCamera.GetComponent<CinemachineBrain>();
                if (brain != null) brain.enabled = !free;
                if (free) outputCamera.transform.SetLocalPositionAndRotation(cameraHome, Quaternion.identity);
            }
            Apply(mode);
        }

        /// <summary>Keeps the chase framing the same for every quad class (a whoop is ~0.3× a 5" quad).</summary>
        public void SetQuadScale(float visualScale)
        {
            var follow = chase != null ? chase.GetComponent<CinemachineFollow>() : null;
            if (follow != null) follow.FollowOffset = chaseOffset * Mathf.Clamp(visualScale, 0.3f, 2f);
        }

        private void UpdateLens()
        {
            if (lensVolume != null) lensVolume.weight = !_free && mode == ViewMode.Fpv && _lensDistortion ? 1f : 0f;
        }
    }
}
