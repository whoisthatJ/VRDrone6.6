using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace GateRush.EditorTools
{
    /// <summary>
    /// M0 project configuration for Meta Quest. Idempotent: safe to run again.
    /// Menu: GateRush/Setup/Apply Quest Project Settings.
    /// </summary>
    public static class ProjectSetup
    {
        private const string QuestUrpPath = "Assets/_Project/Settings/URP_Quest.asset";
        private const string SourceUrpPath = "Assets/Settings/Project Configuration/Performance URP Config.asset";

        [MenuItem("GateRush/Setup/Apply Quest Project Settings")]
        public static string Apply()
        {
            var log = new StringBuilder();
            ApplyPlayerSettings(log);
            ApplyOpenXr(log);
            ApplyQuality(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[GateRush] Project setup:\n" + log);
            return log.ToString();
        }

        private static void ApplyPlayerSettings(StringBuilder log)
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "GateRush";
            PlayerSettings.productName = "GateRush VR";
            PlayerSettings.SetApplicationIdentifier(android, "com.gaterush.vr");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            // Meta Horizon Store requires minSdk 32 (Android 12L).
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            log.AppendLine("Player: com.gaterush.vr, IL2CPP, ARM64, Vulkan, minSdk 32, targetSdk 34, Linear");
        }

        private static void ApplyOpenXr(StringBuilder log)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;

            foreach (var feature in settings.GetFeatures())
            {
                string ns = feature.GetType().Namespace ?? "";
                // Quest-only build: the Android XR (non-Meta) feature group is turned off.
                if (ns == "UnityEngine.XR.OpenXR.Features.Android" && feature.enabled)
                {
                    feature.enabled = false;
                    log.AppendLine("OpenXR: disabled " + feature.GetType().Name + " (Android XR)");
                }
            }

            var touch = settings.GetFeature<OculusTouchControllerProfile>();
            if (touch != null) touch.enabled = true;
            EditorUtility.SetDirty(settings);
            log.AppendLine("OpenXR: Single Pass Instanced, Oculus Touch profile on");

            var perTarget = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (perTarget != null) perTarget.InitManagerOnStart = true;
        }

        private static void ApplyQuality(StringBuilder log)
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QuestUrpPath);
            if (urp == null)
            {
                AssetDatabase.CopyAsset(SourceUrpPath, QuestUrpPath);
                urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QuestUrpPath);
            }

            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.renderScale = 1f;
            urp.shadowDistance = 0f;
            var urpSo = new SerializedObject(urp);
            SetBool(urpSo, "m_MainLightShadowsSupported", false);
            SetBool(urpSo, "m_AdditionalLightShadowsSupported", false);
            SetBool(urpSo, "m_SoftShadowsSupported", false);
            SetBool(urpSo, "m_RequireDepthTexture", false);
            SetBool(urpSo, "m_RequireOpaqueTexture", false);
            urpSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            var qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(qualityAsset);
            var levels = so.FindProperty("m_QualitySettings");
            while (levels.arraySize > 1) levels.DeleteArrayElementAtIndex(levels.arraySize - 1);

            var q = levels.GetArrayElementAtIndex(0);
            q.FindPropertyRelative("name").stringValue = "Quest";
            q.FindPropertyRelative("antiAliasing").intValue = 4;
            q.FindPropertyRelative("vSyncCount").intValue = 0;
            q.FindPropertyRelative("shadows").intValue = 0;
            q.FindPropertyRelative("realtimeReflectionProbes").boolValue = false;
            q.FindPropertyRelative("customRenderPipeline").objectReferenceValue = urp;

            so.FindProperty("m_CurrentQuality").intValue = 0;
            var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < perPlatform.arraySize; i++)
                perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.SetQualityLevel(0, true);
            log.AppendLine("Quality: single 'Quest' level, MSAA 4x, HDR off, no realtime shadows, render scale 1.0");
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }
    }
}
