using GateRush.Cameras;
using UnityEngine;
using UnityEngine.XR;

namespace GateRush.UI
{
    /// <summary>Shows its target only in the views where the FPV OSD can't be seen: Immersive and Line of sight in XR.</summary>
    public sealed class ViewVisibility : MonoBehaviour
    {
        [SerializeField] private ViewModeManager views;
        [SerializeField] private GameObject target;

        private void LateUpdate()
        {
            if (views == null || target == null) return;
            bool show = XRSettings.isDeviceActive && views.isActiveAndEnabled && views.Mode != ViewMode.Goggles;
            if (target.activeSelf != show) target.SetActive(show);
        }
    }
}
