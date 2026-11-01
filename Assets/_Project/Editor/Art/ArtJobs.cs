using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Runs long editor jobs on the next editor tick so a CLI/MCP caller returns at once. The result lands in
    /// <see cref="StatusPath"/>: "running …", then "done …" or "error …" followed by the job's report.
    /// </summary>
    public static class ArtJobs
    {
        public const string StatusPath = "Builds/art-status.txt";
        public const string BootScene = "Assets/_Project/Scenes/Boot.unity";

        public static string RunDeferred(string name, Func<string> job)
        {
            SetStatus($"running {name} {DateTime.Now:HH:mm:ss}");
            EditorApplication.delayCall += () =>
            {
                try { SetStatus($"done {name}\n{job()}"); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    SetStatus($"error {name}\n{e}");
                }
            };
            return StatusPath;
        }

        public static void SetStatus(string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
            File.WriteAllText(StatusPath, text);
        }

        /// <summary>Refuses to run a scene-hopping job over unsaved user edits (scripts open scenes without prompting).</summary>
        public static void RequireCleanScenes()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException($"Scene '{EditorSceneManager.GetSceneAt(i).name}' has unsaved changes: save or discard them first.");
        }

        /// <summary>Leaves the editor on the Boot scene, clean, after a job opened and modified other scenes.</summary>
        public static void ReturnToBoot()
        {
            if (File.Exists(BootScene)) EditorSceneManager.OpenScene(BootScene, OpenSceneMode.Single);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static string ScenePath(string sceneName)
        {
            foreach (var s in EditorBuildSettings.scenes)
                if (Path.GetFileNameWithoutExtension(s.path) == sceneName) return s.path;
            return null;
        }
    }
}
