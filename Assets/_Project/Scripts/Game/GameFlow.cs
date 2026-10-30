using GateRush.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GateRush.Game
{
    /// <summary>Scene navigation shared by the menu and gameplay scenes.</summary>
    public static class GameFlow
    {
        public const string MenuScene = "Menu";
        public const string DiagnosticsScene = "InputDiagnostics";

        /// <summary>Selects a track, saves the choice and loads its environment scene.</summary>
        public static bool Race(TrackCatalog catalog, string trackId)
        {
            var entry = catalog.Find(trackId);
            var data = catalog.Load(entry);
            if (data == null)
            {
                Debug.LogWarning($"[GateRush] Unknown track '{trackId}'");
                return false;
            }
            string scene = catalog.SceneFor(data.Environment);
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogWarning($"[GateRush] No scene for environment '{data.Environment}'");
                return false;
            }

            SaveSystem.Settings.TrackId = trackId;
            SaveSystem.Commit();
            SceneManager.LoadScene(scene);
            return true;
        }

        public static void Menu() => SceneManager.LoadScene(MenuScene);

        public static void Diagnostics() => SceneManager.LoadScene(DiagnosticsScene);

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
