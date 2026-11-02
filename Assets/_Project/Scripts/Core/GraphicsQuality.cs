using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GateRush.Core
{
    /// <summary>Graphics options (Windows monitor build). Saved with the rest of <see cref="GameSettings"/>.</summary>
    [Serializable]
    public sealed class GraphicsPrefs
    {
        /// <summary>Quality level: "Low", "Medium", "High" or "Ultra". Empty = pick from the GPU on first launch.</summary>
        public string Preset = "";
        [Range(0.5f, 1f)] public float RenderScale = 1f;
        /// <summary>0 off, 1 SMAA, 2 MSAA 2×, 3 MSAA 4×.</summary>
        public int AntiAliasing = 3;
        /// <summary>0 off, 1 low, 2 medium, 3 high.</summary>
        public int Shadows = 2;
        public bool Ssao = true;
        /// <summary>Terrain grass density, 0..1.</summary>
        [Range(0f, 1f)] public float Vegetation = 0.85f;
        public bool PostProcessing = true;
        public bool VSync = true;
        /// <summary>Frame-rate cap when VSync is off; 0 = uncapped.</summary>
        public int FrameCap;

        /// <summary>Choices offered for <see cref="FrameCap"/>.</summary>
        public static readonly int[] FrameCaps = { 0, 60, 120, 144, 165, 240 };

        public static GraphicsPrefs ForPreset(string preset) => preset switch
        {
            "Low" => new GraphicsPrefs { Preset = "Low", RenderScale = 0.85f, AntiAliasing = 1, Shadows = 0, Ssao = false, Vegetation = 0.25f },
            "Medium" => new GraphicsPrefs { Preset = "Medium", RenderScale = 1f, AntiAliasing = 1, Shadows = 1, Ssao = false, Vegetation = 0.5f },
            "Ultra" => new GraphicsPrefs { Preset = "Ultra", RenderScale = 1f, AntiAliasing = 3, Shadows = 3, Ssao = true, Vegetation = 1f },
            _ => new GraphicsPrefs { Preset = "High", RenderScale = 1f, AntiAliasing = 3, Shadows = 2, Ssao = true, Vegetation = 0.85f },
        };

        public void Clamp()
        {
            if (!string.IsNullOrEmpty(Preset) && Array.IndexOf(GraphicsQuality.Presets, Preset) < 0) Preset = "High";
            RenderScale = Mathf.Clamp(RenderScale, 0.5f, 1f);
            AntiAliasing = Mathf.Clamp(AntiAliasing, 0, 3);
            Shadows = Mathf.Clamp(Shadows, 0, 3);
            Vegetation = Mathf.Clamp01(Vegetation);
            if (Array.IndexOf(FrameCaps, FrameCap) < 0) FrameCap = 0;
        }
    }

    /// <summary>
    /// Applies <see cref="GraphicsPrefs"/>: the quality level (one URP asset per preset, see Settings/PC), then the
    /// player's overrides on the active URP asset (render scale, MSAA, shadows, SSAO) and per scene (cameras: AA and
    /// post-processing; terrains: grass density and distance). Runs before the first scene and after every scene load.
    /// In the editor the URP assets are restored when Play mode ends so playing never dirties them.
    /// </summary>
    public static class GraphicsQuality
    {
        public static readonly string[] Presets = { "Low", "Medium", "High", "Ultra" };

        private struct ShadowLevel
        {
            public float Distance;
            public int Resolution;
            public int Cascades;
        }

        private static readonly ShadowLevel[] ShadowLevels =
        {
            new ShadowLevel { Distance = 0f, Resolution = 1024, Cascades = 1 },
            new ShadowLevel { Distance = 70f, Resolution = 1024, Cascades = 2 },
            new ShadowLevel { Distance = 150f, Resolution = 2048, Cascades = 4 },
            new ShadowLevel { Distance = 220f, Resolution = 4096, Cascades = 4 },
        };

        public static GraphicsPrefs Current { get; private set; }

        /// <summary>Preset for a GPU with <paramref name="vramMb"/> of video memory (first launch only).</summary>
        public static string DetectPreset(int vramMb) => vramMb >= 10000 ? "Ultra" : vramMb >= 5500 ? "High" : vramMb >= 3000 ? "Medium" : "Low";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            var settings = SaveSystem.Settings;
            settings.Graphics ??= new GraphicsPrefs();
            if (string.IsNullOrEmpty(settings.Graphics.Preset))
            {
                string preset = DetectPreset(SystemInfo.graphicsMemorySize);
                settings.Graphics = GraphicsPrefs.ForPreset(preset);
                Debug.Log($"[GateRush] Graphics preset {preset} chosen for {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)");
            }
            Apply(settings.Graphics);
            SceneManager.sceneLoaded += (_, _) => ApplyScene();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) RestoreAssets();
            };
#endif
        }

        public static void Apply(GraphicsPrefs prefs)
        {
            prefs.Clamp();
            Current = prefs;
            int level = Array.IndexOf(QualitySettings.names, string.IsNullOrEmpty(prefs.Preset) ? "High" : prefs.Preset);
            if (level >= 0 && level != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(level, true);
            QualitySettings.vSyncCount = prefs.VSync ? 1 : 0;
            Application.targetFrameRate = !prefs.VSync && prefs.FrameCap > 0 ? prefs.FrameCap : -1;

            var urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                Remember(urp);
                urp.renderScale = prefs.RenderScale;
                urp.msaaSampleCount = prefs.AntiAliasing == 3 ? 4 : prefs.AntiAliasing == 2 ? 2 : 1;
                var shadows = ShadowLevels[prefs.Shadows];
                urp.shadowDistance = shadows.Distance;
                urp.mainLightShadowmapResolution = shadows.Resolution;
                urp.shadowCascadeCount = shadows.Cascades;
                foreach (var data in urp.rendererDataList)
                {
                    if (data == null) continue;
                    foreach (var feature in data.rendererFeatures)
                        if (feature is ScreenSpaceAmbientOcclusion) feature.SetActive(prefs.Ssao);
                }
            }
            ApplyScene();
        }

        /// <summary>Per-scene part: camera AA / post-processing and terrain grass.</summary>
        public static void ApplyScene()
        {
            var prefs = Current;
            if (prefs == null) return;
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (cam.targetTexture != null && cam.cameraType != CameraType.Game) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data == null || data.renderType == CameraRenderType.Overlay) continue;
                data.renderPostProcessing = prefs.PostProcessing;
                data.antialiasing = prefs.AntiAliasing == 1 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.stopNaN = prefs.PostProcessing;
                data.dithering = prefs.PostProcessing;
                cam.allowMSAA = prefs.AntiAliasing >= 2;
            }
            foreach (var terrain in Terrain.activeTerrains)
            {
                terrain.detailObjectDensity = prefs.Vegetation;
                terrain.detailObjectDistance = Mathf.Lerp(50f, 150f, prefs.Vegetation);
            }
        }

        // ---------- editor: keep the URP assets clean ----------

        private struct Snapshot
        {
            public float RenderScale, ShadowDistance;
            public int Msaa, ShadowResolution, Cascades;
            public Dictionary<ScriptableRendererFeature, bool> Features;
        }

        private static readonly Dictionary<UniversalRenderPipelineAsset, Snapshot> Snapshots = new Dictionary<UniversalRenderPipelineAsset, Snapshot>();

        private static void Remember(UniversalRenderPipelineAsset urp)
        {
            if (!Application.isEditor || Snapshots.ContainsKey(urp)) return;
            var features = new Dictionary<ScriptableRendererFeature, bool>();
            foreach (var data in urp.rendererDataList)
                if (data != null)
                    foreach (var f in data.rendererFeatures)
                        if (f != null) features[f] = f.isActive;
            Snapshots[urp] = new Snapshot
            {
                RenderScale = urp.renderScale,
                Msaa = urp.msaaSampleCount,
                ShadowDistance = urp.shadowDistance,
                ShadowResolution = urp.mainLightShadowmapResolution,
                Cascades = urp.shadowCascadeCount,
                Features = features,
            };
        }

        private static void RestoreAssets()
        {
            foreach (var kv in Snapshots)
            {
                var urp = kv.Key;
                if (urp == null) continue;
                var s = kv.Value;
                urp.renderScale = s.RenderScale;
                urp.msaaSampleCount = s.Msaa;
                urp.shadowDistance = s.ShadowDistance;
                urp.mainLightShadowmapResolution = s.ShadowResolution;
                urp.shadowCascadeCount = s.Cascades;
                foreach (var f in s.Features)
                    if (f.Key != null) f.Key.SetActive(f.Value);
            }
            Snapshots.Clear();
        }
    }
}
