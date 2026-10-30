using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

namespace GateRush.Core
{
    /// <summary>
    /// Temporary scene toggle until the main menu exists (M5): flight ↔ input diagnostics.
    /// Left Touch menu button, gamepad right-stick click, or F1.
    /// </summary>
    public sealed class SceneSwitcher : MonoBehaviour
    {
        [SerializeField] private string flightScene = "Track_TestField";
        [SerializeField] private string diagnosticsScene = "InputDiagnostics";

        private ButtonControl _menu;
        private bool _dirty = true;

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is XRController) _dirty = true;
        }

        private void Update()
        {
            if (_dirty)
            {
                _dirty = false;
                _menu = XRController.leftHand?.TryGetChildControl<ButtonControl>("menu");
            }

            bool pressed = (_menu != null && _menu.wasPressedThisFrame)
                           || (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                           || (Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame);
            if (!pressed) return;

            string current = SceneManager.GetActiveScene().name;
            SceneManager.LoadScene(current == diagnosticsScene ? flightScene : diagnosticsScene);
        }
    }
}
