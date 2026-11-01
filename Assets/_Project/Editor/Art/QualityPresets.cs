using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Tools > Art > Build Quality Presets: the Windows (monitor) presets. One Forward+ renderer and one URP asset
    /// per preset in Assets/_Project/Settings/PC (decals everywhere, SSAO on High/Ultra), and the quality levels
    /// Medium / High / Ultra replacing the old single "Quest" level. Runtime overrides (render scale, AA, shadows,
    /// SSAO, vegetation, post-processing, VSync) are applied by GateRush.Core.GraphicsQuality.
    /// </summary>
    public static class QualityPresets
    {
        public const string Dir = "Assets/_Project/Settings/PC";
        public const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        private sealed class Preset
        {
            public string Name;
            public int Msaa;
            public int ShadowResolution;
            public float ShadowDistance;
            public int Cascades;
            public int SoftShadowQuality; // 0 low, 1 medium, 2 high
            public bool AdditionalShadows;
            public int AdditionalShadowResolution;
            public bool Ssao;
            public int SsaoSamples;       // 0 low, 1 medium, 2 high
            public float LodBias;
            public int TextureBudgetMb;
            public float DetailDistance;
            public float DetailDensity;
            public bool LodCrossFade;
        }

        private static readonly Preset[] Presets =
        {
            new Preset { Name = "Medium", Msaa = 1, ShadowResolution = 1024, ShadowDistance = 80f, Cascades = 2, SoftShadowQuality = 0, AdditionalShadows = false, AdditionalShadowResolution = 1024, Ssao = false, SsaoSamples = 0, LodBias = 1f, TextureBudgetMb = 1024, DetailDistance = 70f, DetailDensity = 0.5f, LodCrossFade = false },
            new Preset { Name = "High", Msaa = 4, ShadowResolution = 2048, ShadowDistance = 150f, Cascades = 4, SoftShadowQuality = 1, AdditionalShadows = true, AdditionalShadowResolution = 2048, Ssao = true, SsaoSamples = 1, LodBias = 1.5f, TextureBudgetMb = 2048, DetailDistance = 110f, DetailDensity = 0.85f, LodCrossFade = true },
            new Preset { Name = "Ultra", Msaa = 4, ShadowResolution = 4096, ShadowDistance = 220f, Cascades = 4, SoftShadowQuality = 2, AdditionalShadows = true, AdditionalShadowResolution = 4096, Ssao = true, SsaoSamples = 2, LodBias = 2f, TextureBudgetMb = 3072, DetailDistance = 150f, DetailDensity = 1f, LodCrossFade = true },
        };

        public static string AssetPath(string preset) => $"{Dir}/URP_PC_{preset}.asset";
        public static string RendererPath(string preset) => $"{Dir}/Renderer_PC_{preset}.asset";

        [MenuItem("Tools/Art/Build Quality Presets")]
        private static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            Directory.CreateDirectory(Dir);
            var log = new StringBuilder();
            var assets = new UniversalRenderPipelineAsset[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                var renderer = BuildRenderer(Presets[i], log);
                assets[i] = BuildAsset(Presets[i], renderer, log);
            }
            AssetDatabase.SaveAssets();
            BuildQualityLevels(assets, log);
            GraphicsSettings.defaultRenderPipeline = assets[1];
            AssetDatabase.SaveAssets();
            return "quality presets: " + string.Join(", ", QualitySettings.names) + log;
        }

        private static UniversalRendererData BuildRenderer(Preset p, StringBuilder log)
        {
            string path = RendererPath(p.Name);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, path);
            }
            data.renderingMode = RenderingMode.ForwardPlus;
            data.depthPrimingMode = DepthPrimingMode.Auto;
            data.copyDepthMode = CopyDepthMode.AfterOpaques;
            data.intermediateTextureMode = IntermediateTextureMode.Auto;
            data.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
            if (data.postProcessData == null) log.Append("; WARNING no PostProcessData at " + PostProcessDataPath);

            var ssao = Feature<ScreenSpaceAmbientOcclusion>(data, "SSAO");
            var so = new SerializedObject(ssao);
            Set(so, "m_Settings.Intensity", 1.6f, log);
            Set(so, "m_Settings.Radius", 0.35f, log);
            Set(so, "m_Settings.DirectLightingStrength", 0.15f, log);
            Set(so, "m_Settings.Falloff", 60f, log);
            SetEnum(so, "m_Settings.Samples", 2 - p.SsaoSamples, log); // URP order: High, Medium, Low
            SetBool(so, "m_Settings.Downsample", p.Name != "Ultra", log);
            SetBool(so, "m_Settings.AfterOpaque", false, log);
            so.ApplyModifiedPropertiesWithoutUndo();
            ssao.SetActive(p.Ssao);

            var decals = Feature<DecalRendererFeature>(data, "Decals");
            var dso = new SerializedObject(decals);
            SetEnum(dso, "m_Settings.technique", 1, log); // DBuffer: normals + MAOS on lit surfaces
            Set(dso, "m_Settings.maxDrawDistance", p.Name == "Medium" ? 60f : 120f, log);
            dso.ApplyModifiedPropertiesWithoutUndo();
            decals.SetActive(true);

            // Re-link sub-assets into the feature map (what URP's renderer inspector does).
            typeof(ScriptableRendererData).GetMethod("ValidateRendererFeatures", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(data, null);
            data.SetDirty();
            EditorUtility.SetDirty(data);
            return data;
        }

        private static T Feature<T>(UniversalRendererData data, string name) where T : ScriptableRendererFeature
        {
            var existing = data.rendererFeatures.OfType<T>().FirstOrDefault();
            if (existing != null) return existing;
            var f = ScriptableObject.CreateInstance<T>();
            f.name = name;
            AssetDatabase.AddObjectToAsset(f, data);
            data.rendererFeatures.Add(f);
            return f;
        }

        private static UniversalRenderPipelineAsset BuildAsset(Preset p, UniversalRendererData renderer, StringBuilder log)
        {
            string path = AssetPath(p.Name);
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, path);
            }
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            Set(so, "m_DefaultRendererIndex", 0, log);
            SetBool(so, "m_SupportsHDR", true, log);
            Set(so, "m_MSAA", p.Msaa, log);
            Set(so, "m_RenderScale", 1f, log);
            Set(so, "m_MainLightRenderingMode", 1, log);
            SetBool(so, "m_MainLightShadowsSupported", true, log);
            Set(so, "m_MainLightShadowmapResolution", p.ShadowResolution, log);
            Set(so, "m_AdditionalLightsRenderingMode", 1, log);
            Set(so, "m_AdditionalLightsPerObjectLimit", 8, log);
            SetBool(so, "m_AdditionalLightShadowsSupported", p.AdditionalShadows, log);
            Set(so, "m_AdditionalLightsShadowmapResolution", p.AdditionalShadowResolution, log);
            Set(so, "m_ShadowDistance", p.ShadowDistance, log);
            Set(so, "m_ShadowCascadeCount", p.Cascades, log);
            Set(so, "m_Cascade2Split", 0.2f, log);
            SetVector3(so, "m_Cascade4Split", new Vector3(0.06f, 0.18f, 0.42f), log);
            Set(so, "m_CascadeBorder", 0.15f, log);
            Set(so, "m_ShadowDepthBias", 1f, log);
            Set(so, "m_ShadowNormalBias", 0.6f, log);
            SetBool(so, "m_SoftShadowsSupported", true, log);
            Set(so, "m_SoftShadowQuality", p.SoftShadowQuality + 1, log);
            SetBool(so, "m_RequireDepthTexture", true, log);
            SetBool(so, "m_RequireOpaqueTexture", false, log);
            SetBool(so, "m_SupportsTerrainHoles", true, log);
            SetBool(so, "m_ReflectionProbeBlending", true, log);
            SetBool(so, "m_ReflectionProbeBoxProjection", true, log);
            Set(so, "m_ColorGradingMode", 1, log);   // HDR grading
            Set(so, "m_ColorGradingLutSize", 32, log);
            SetBool(so, "m_UseSRPBatcher", true, log);
            SetBool(so, "m_SupportsDynamicBatching", false, log);
            SetBool(so, "m_EnableLODCrossFade", p.LodCrossFade, log);
            Set(so, "m_GPUResidentDrawerMode", 0, log); // off: see VISUALS_PLAN decisions
            SetBool(so, "m_SupportsLightCookies", true, log);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void BuildQualityLevels(UniversalRenderPipelineAsset[] assets, StringBuilder log)
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(qs);
            var levels = so.FindProperty("m_QualitySettings");
            if (levels.arraySize == 0) levels.arraySize = 1;
            while (levels.arraySize < Presets.Length) levels.InsertArrayElementAtIndex(levels.arraySize - 1);
            levels.arraySize = Presets.Length;
            for (int i = 0; i < Presets.Length; i++)
            {
                var p = Presets[i];
                var e = levels.GetArrayElementAtIndex(i);
                Rel(e, "name").stringValue = p.Name;
                Rel(e, "customRenderPipeline").objectReferenceValue = assets[i];
                SetRel(e, "globalTextureMipmapLimit", 0, log);
                SetRel(e, "anisotropicTextures", 1, log);       // per texture (the art imports at aniso 8)
                SetRel(e, "antiAliasing", 0, log);              // URP owns MSAA
                SetRel(e, "lodBias", p.LodBias, log);
                SetRel(e, "maximumLODLevel", 0, log);
                SetRel(e, "vSyncCount", 1, log);
                SetRelBool(e, "softParticles", true, log);
                SetRelBool(e, "realtimeReflectionProbes", true, log);
                SetRelBool(e, "billboardsFaceCameraPosition", true, log);
                SetRelBool(e, "streamingMipmapsActive", true, log);
                SetRelBool(e, "streamingMipmapsAddAllCameras", true, log);
                SetRel(e, "streamingMipmapsMemoryBudget", (float)p.TextureBudgetMb, log);
                SetRel(e, "terrainDetailDistance", p.DetailDistance, log);
                SetRel(e, "terrainDetailDensityScale", p.DetailDensity, log);
                SetRel(e, "terrainPixelError", p.Name == "Medium" ? 8f : 4f, log);
                SetRel(e, "terrainBasemapDistance", p.Name == "Medium" ? 400f : 800f, log);
                SetRel(e, "particleRaycastBudget", 256, log);
                var excluded = e.FindPropertyRelative("excludedTargetPlatforms");
                if (excluded != null) excluded.arraySize = 0;
            }
            var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
            if (perPlatform != null)
                for (int i = 0; i < perPlatform.arraySize; i++)
                {
                    var entry = perPlatform.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("second").intValue = 1; // High until GraphicsQuality picks from the GPU
                }
            var current = so.FindProperty("m_CurrentQuality");
            if (current != null) current.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.SetQualityLevel(1, true);
        }

        // ---------- SerializedObject helpers (log fields this URP version doesn't have) ----------

        private static SerializedProperty Rel(SerializedProperty e, string name) => e.FindPropertyRelative(name);

        private static void Set(SerializedObject so, string path, float v, StringBuilder log)
        {
            var p = so.FindProperty(path);
            if (p == null) { log.Append($"; missing {path}"); return; }
            if (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum) p.intValue = (int)v;
            else p.floatValue = v;
        }

        private static void SetBool(SerializedObject so, string path, bool v, StringBuilder log)
        {
            var p = so.FindProperty(path);
            if (p == null) { log.Append($"; missing {path}"); return; }
            p.boolValue = v;
        }

        private static void SetEnum(SerializedObject so, string path, int v, StringBuilder log)
        {
            var p = so.FindProperty(path);
            if (p == null) { log.Append($"; missing {path}"); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = v;
            else p.intValue = v;
        }

        private static void SetVector3(SerializedObject so, string path, Vector3 v, StringBuilder log)
        {
            var p = so.FindProperty(path);
            if (p == null) { log.Append($"; missing {path}"); return; }
            p.vector3Value = v;
        }

        private static void SetRel(SerializedProperty e, string name, float v, StringBuilder log)
        {
            var p = e.FindPropertyRelative(name);
            if (p == null) { log.Append($"; missing quality.{name}"); return; }
            if (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum) p.intValue = (int)v;
            else p.floatValue = v;
        }

        private static void SetRelBool(SerializedProperty e, string name, bool v, StringBuilder log)
        {
            var p = e.FindPropertyRelative(name);
            if (p == null) { log.Append($"; missing quality.{name}"); return; }
            p.boolValue = v;
        }
    }
}
