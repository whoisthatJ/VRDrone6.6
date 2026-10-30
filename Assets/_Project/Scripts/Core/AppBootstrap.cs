using UnityEngine;
using UnityEngine.SceneManagement;

namespace GateRush.Core
{
    /// <summary>Lives in the Boot scene: keeps app-wide services alive and loads the first gameplay scene.</summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private string firstScene = "Track_TestField";

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(firstScene)) SceneManager.LoadScene(firstScene);
        }
    }
}
