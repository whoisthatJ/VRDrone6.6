using System.Collections.Generic;
using System.IO;
using GateRush.EditorTools.Builders;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GateRush.EditorTools
{
    /// <summary>
    /// Bakes the track scenes' lighting one after another without blocking the Editor
    /// (Lightmapping.BakeAsync + bakeCompleted). Progress goes to Builds/bake-status.txt so a remote caller can poll.
    /// Re-run after "Build All Scenes", which regenerates the scenes and drops their bakes.
    /// </summary>
    public static class LightingBaker
    {
        public const string StatusPath = "Builds/bake-status.txt";

        private static readonly Queue<string> Pending = new Queue<string>();
        private static string _current;
        private static System.Diagnostics.Stopwatch _clock;

        [MenuItem("GateRush/Lighting/Bake All Track Lighting")]
        public static string BakeAll()
        {
            Pending.Clear();
            foreach (var env in TrackSceneBuilder.Environments) Pending.Enqueue(TrackSceneBuilder.ScenePath(env));
            _clock = System.Diagnostics.Stopwatch.StartNew();
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            Write("baking: starting " + Pending.Count + " scenes");
            Next();
            return StatusPath;
        }

        private static void Next()
        {
            if (Pending.Count == 0)
            {
                Lightmapping.bakeCompleted -= OnBakeCompleted;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Write($"done in {_clock.Elapsed:mm\\:ss}");
                return;
            }
            _current = Pending.Dequeue();
            EditorSceneManager.OpenScene(_current);
            Write($"baking: {Path.GetFileNameWithoutExtension(_current)} ({Pending.Count} left)");
            if (!Lightmapping.BakeAsync())
            {
                Write("failed to start bake for " + _current);
                Lightmapping.bakeCompleted -= OnBakeCompleted;
            }
        }

        private static void OnBakeCompleted()
        {
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[GateRush] Baked lighting for {scene.name} ({LightmapSettings.lightmaps.Length} lightmaps)");
            Next();
        }

        private static void Write(string status)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
            File.WriteAllText(StatusPath, status);
        }
    }
}
