using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

namespace GateRush.Core
{
    /// <summary>
    /// Back to the main menu from any scene: left Touch menu button, gamepad right-stick click, Esc or F1.
    /// Lives on the persistent App object.
    /// </summary>
    public sealed class SceneSwitcher : MonoBehaviour
    {
        [SerializeField] private string menuScene = "Menu";

        private ButtonControl _menu;
        private bool _dirty = true;

        public string MenuScene => menuScene;

        /// <summary>
        /// Optional handler for the menu button; return true to consume the press (e.g. "back to the editor"
        /// while test-flying a track). Cleared automatically when its owner scene unloads.
        /// </summary>
        public static System.Func<bool> MenuOverride;

        // The Boot scene normally provides one; this covers starting Play mode directly in a track scene.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (FindAnyObjectByType<SceneSwitcher>() != null) return;
            var go = new GameObject(nameof(SceneSwitcher));
            DontDestroyOnLoad(go);
            go.AddComponent<SceneSwitcher>();
        }

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

            var kb = Keyboard.current;
            if (_menu != null && !_menu.device.added) _dirty = true;
            bool pressed = (_menu != null && _menu.device.added && _menu.wasPressedThisFrame)
                           || (kb != null && (kb.f1Key.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                           || (Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame);
            if (!pressed || SceneManager.GetActiveScene().name == menuScene) return;
            if (MenuOverride != null && MenuOverride()) return;
            MenuOverride = null;
            SceneManager.LoadScene(menuScene);
        }
    }
}
