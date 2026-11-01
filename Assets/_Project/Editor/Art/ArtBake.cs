using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Tools > Art > Bake Lighting: for each scene, bakes lightmaps, light probes and reflection probes
    /// (Progressive GPU, settings from the scene's LightingSettings), computes occlusion culling for the indoor/dense
    /// scenes (Hangar, Industrial) and saves. Runs one scene after another on editor ticks; progress in
    /// Builds/art-status.txt. Re-run after Build All Scenes and the art pass.
    /// </summary>
    public static class ArtBake
    {
        private static readonly Queue<string> Pending = new Queue<string>();
        private static readonly StringBuilder Log = new StringBuilder();
        private static string _current;
        private static double _started;

        [MenuItem("Tools/Art/Bake Lighting (all scenes)")]
        private static void Menu() => BakeDeferred("Field,Hangar,Industrial,Menu");

        public static string BakeDeferred(string envs)
        {
            ArtJobs.RequireCleanScenes();
            Pending.Clear();
            Log.Clear();
            foreach (var e in envs.Split(',')) Pending.Enqueue(e.Trim());
            ArtJobs.SetStatus("running bake " + envs);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return ArtJobs.StatusPath;
        }

        private static void Tick()
        {
            try
            {
                if (_current != null)
                {
                    if (Lightmapping.isRunning) return;
                    var scene = EditorSceneManager.GetActiveScene();
                    // No occlusion culling: baked Umbra data culled the hangar roof and walls from parts of the hall
                    // (huge thin occluders), and both scenes are open spaces far below the draw-call budget.
                    StaticOcclusionCulling.Clear();
                    EditorSceneManager.SaveScene(scene);
                    Log.AppendLine($"{_current}: baked in {EditorApplication.timeSinceStartup - _started:F0} s, {LightmapSettings.lightmaps.Length} lightmaps");
                    _current = null;
                }
                if (Pending.Count == 0)
                {
                    EditorApplication.update -= Tick;
                    ArtJobs.ReturnToBoot();
                    ArtJobs.SetStatus("done bake\n" + Log);
                    return;
                }
                _current = Pending.Dequeue();
                ArtJobs.SetStatus($"running bake {_current} (left: {Pending.Count})");
                var s = EditorSceneManager.OpenScene(ArtJobs.ScenePath(_current == "Menu" ? "Menu" : "Track_" + _current), OpenSceneMode.Single);
                EnsureLightingSettings(_current);
                _started = EditorApplication.timeSinceStartup;
                if (!Lightmapping.BakeAsync())
                {
                    Log.AppendLine($"{_current}: bake did not start");
                    _current = null;
                }
            }
            catch (System.Exception e)
            {
                EditorApplication.update -= Tick;
                _current = null;
                Debug.LogException(e);
                ArtJobs.SetStatus("error bake\n" + Log + e);
            }
        }

        /// <summary>The menu scene had no lighting asset; give it one (other scenes keep the builder's).</summary>
        private static void EnsureLightingSettings(string env)
        {
            if (Lightmapping.TryGetLightingSettings(out var existing) && existing != null) return;
            string path = $"Assets/_Project/Scenes/Lighting_{env}.lighting";
            var s = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (s == null)
            {
                s = new LightingSettings { name = "Lighting_" + env };
                s.bakedGI = true;
                s.realtimeGI = false;
                s.mixedBakeMode = MixedLightingMode.IndirectOnly;
                s.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
                s.lightmapResolution = 4f;
                s.lightmapMaxSize = 1024;
                s.directSampleCount = 16;
                s.indirectSampleCount = 128;
                s.environmentSampleCount = 64;
                s.maxBounces = 2;
                s.directionalityMode = LightmapsMode.NonDirectional;
                s.lightmapCompression = LightmapCompression.NormalQuality;
                AssetDatabase.CreateAsset(s, path);
            }
            Lightmapping.lightingSettings = s;
        }
    }
}
