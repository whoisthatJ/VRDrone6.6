using GateRush.Core;
using GateRush.Track;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GateRush.Game
{
    /// <summary>Scene navigation shared by the menu and gameplay scenes.</summary>
    public static class GameFlow
    {
        public const string MenuScene = "Menu";
        public const string DiagnosticsScene = "InputDiagnostics";
        public const string GarageScene = "Garage";

        /// <summary>A track handed to the next track scene to open in the editor (consumed on load).</summary>
        public static TrackData PendingEdit { get; private set; }

        public static TrackData TakePendingEdit()
        {
            var t = PendingEdit;
            PendingEdit = null;
            return t;
        }

        /// <summary>Built-in track from the catalog, or a user track from the library.</summary>
        public static TrackData Resolve(TrackCatalog catalog, string trackId)
        {
            if (TrackLibrary.IsUserId(trackId))
            {
                try
                {
                    return TrackLibrary.Default.Load(trackId);
                }
                catch (System.Exception e) when (e is System.IO.IOException || e is System.ArgumentException)
                {
                    Debug.LogWarning($"[GateRush] User track '{trackId}' unreadable: {e.Message}");
                    return null;
                }
            }
            return catalog.Load(catalog.Find(trackId));
        }

        /// <summary>The catalog of whichever scene is running (menu or track), for online race loads.</summary>
        public static TrackCatalog Catalog { get; set; }

        /// <summary>Track an online race loads instead of the saved choice (the player's own pick stays as it was).</summary>
        public static string OnlineTrackId { get; private set; }

        /// <summary>Loads an online race's track without changing the saved track choice.</summary>
        public static bool RaceOnline(TrackCatalog catalog, string trackId)
        {
            OnlineTrackId = trackId;
            return Race(catalog, trackId, false);
        }

        /// <summary>Track the next track scene should load: the online race's, else the saved choice.</summary>
        public static string TakeTrackToLoad()
        {
            string id = OnlineTrackId;
            OnlineTrackId = null;
            return string.IsNullOrEmpty(id) ? SaveSystem.Settings.TrackId : id;
        }

        /// <summary>Selects a track, saves the choice and loads its environment scene.</summary>
        public static bool Race(TrackCatalog catalog, string trackId) => Race(catalog, trackId, true);

        private static bool Race(TrackCatalog catalog, string trackId, bool remember)
        {
            var data = Resolve(catalog, trackId);
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

            if (remember)
            {
                SaveSystem.Settings.TrackId = trackId;
                SaveSystem.Commit();
            }
            PendingEdit = null;
            SceneManager.LoadScene(scene);
            return true;
        }

        /// <summary>Opens a track (new, user or a copy of a built-in) in the VR track editor.</summary>
        public static bool Edit(TrackCatalog catalog, TrackData track)
        {
            if (track.IsSplineTrack)
            {
                // The in-game editor places gates; spline tracks are edited in Unity (TrackDefinition + Tools > Track).
                Debug.LogWarning($"[GateRush] '{track.Name}' is a spline track: edit it in Unity (Tools > Track)");
                return false;
            }
            string scene = catalog.SceneFor(track.Environment);
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogWarning($"[GateRush] No scene for environment '{track.Environment}'");
                return false;
            }
            PendingEdit = track;
            SceneManager.LoadScene(scene);
            return true;
        }

        public static void Menu() => SceneManager.LoadScene(MenuScene);

        public static void Diagnostics() => SceneManager.LoadScene(DiagnosticsScene);
        /// <summary>The garage: choose and build your quad (T7).</summary>
        public static void Garage() => SceneManager.LoadScene(GarageScene);

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
