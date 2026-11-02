using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GateRush.Core
{
    /// <summary>
    /// Back to the main menu from any scene: Esc, F1 or gamepad right-stick click.
    /// Lives on the persistent App object.
    /// </summary>
    public sealed class SceneSwitcher : MonoBehaviour
    {
        [SerializeField] private string menuScene = "Menu";

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

        private void Update()
        {
            var kb = Keyboard.current;
            bool pressed = (kb != null && (kb.f1Key.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                           || (Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame);
            if (!pressed || SceneManager.GetActiveScene().name == menuScene) return;
            if (MenuOverride != null && MenuOverride()) return;
            MenuOverride = null;
            SceneManager.LoadScene(menuScene);
        }
    }
}
