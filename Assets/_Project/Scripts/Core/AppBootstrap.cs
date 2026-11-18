using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ushqyr.Core
{
    /// <summary>Lives in the Boot scene: keeps app-wide services alive and loads the first gameplay scene.</summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private string firstScene = "Menu";

        /// <summary>Set before the Boot scene starts (e.g. from the command line) to open another scene instead of the menu.</summary>
        public static string FirstSceneOverride;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            string scene = string.IsNullOrEmpty(FirstSceneOverride) ? firstScene : FirstSceneOverride;
            if (!string.IsNullOrEmpty(scene)) SceneManager.LoadScene(scene);
        }
    }
}
