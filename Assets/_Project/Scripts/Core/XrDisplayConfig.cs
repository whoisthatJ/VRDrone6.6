using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace GateRush.Core
{
    /// <summary>
    /// Applies Quest display settings once the XR display subsystem is running:
    /// refresh rate (72 Hz default, 90 Hz option) and fixed foveated rendering.
    /// </summary>
    public sealed class XrDisplayConfig : MonoBehaviour
    {
        [SerializeField] private float targetRefreshRate = 72f;
        [SerializeField, Range(0f, 1f)] private float foveationLevel = 1f;

        private readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();
        private bool _applied;

        public float TargetRefreshRate => targetRefreshRate;

        public void SetRefreshRate(float hz)
        {
            targetRefreshRate = hz;
            _applied = false;
        }

        private void Update()
        {
            if (_applied) return;

            SubsystemManager.GetSubsystems(_displays);
            for (int i = 0; i < _displays.Count; i++)
            {
                var display = _displays[i];
                if (!display.running) continue;

                display.foveatedRenderingLevel = foveationLevel;
                display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                bool ok = display.TryRequestDisplayRefreshRate(targetRefreshRate);
                Debug.Log($"[GateRush] Display: refresh {targetRefreshRate} Hz requested={ok}, foveation={foveationLevel}");
                _applied = true;
            }
        }
    }
}
