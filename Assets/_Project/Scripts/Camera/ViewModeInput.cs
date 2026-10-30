using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace GateRush.Cameras
{
    /// <summary>View-mode button: left Touch Y, gamepad left shoulder, or keyboard V. Long-press Y recentres the goggles screen.</summary>
    public sealed class ViewModeInput : MonoBehaviour
    {
        [SerializeField] private ViewModeManager views;
        [SerializeField] private GogglesScreen gogglesScreen;
        [SerializeField] private float longPressSeconds = 0.6f;

        private ButtonControl _touchY;
        private bool _dirty = true;
        private float _heldFor;
        private bool _longPressFired;

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is XRController) _dirty = true;
        }

        private void Update()
        {
            if (views == null) return;
            if (_dirty)
            {
                _dirty = false;
                _touchY = XRController.leftHand?.TryGetChildControl<ButtonControl>("secondaryButton");
            }

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && kb.vKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame))
                views.Cycle();

            if (_touchY == null) return;
            if (_touchY.isPressed)
            {
                _heldFor += Time.unscaledDeltaTime;
                if (!_longPressFired && _heldFor >= longPressSeconds)
                {
                    _longPressFired = true;
                    if (gogglesScreen != null) gogglesScreen.Recenter();
                }
            }
            else if (_touchY.wasReleasedThisFrame)
            {
                if (!_longPressFired) views.Cycle();
                _heldFor = 0f;
                _longPressFired = false;
            }
        }
    }
}
