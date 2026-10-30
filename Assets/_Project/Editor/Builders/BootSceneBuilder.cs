using GateRush.Core;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/Boot.unity: app-wide services (display config, perf log, scene switcher)
    /// kept alive across scenes, then loads the first gameplay scene. Also hosts the shared XR rig factory.
    /// </summary>
    public static class BootSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Boot.unity";

        /// <summary>Builds every scene in build order: Boot, Track_TestField, InputDiagnostics.</summary>
        [MenuItem("GateRush/Builders/Build All Scenes")]
        public static string BuildAll()
        {
            Build();
            TestFieldBuilder.Build();
            DiagnosticsSceneBuilder.Build();
            return string.Join(", ", System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path));
        }

        [MenuItem("GateRush/Builders/Build Boot Scene")]
        public static string Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var app = new GameObject("App");
            app.AddComponent<AppBootstrap>();
            app.AddComponent<XrDisplayConfig>();
            app.AddComponent<PerfLogger>();
            app.AddComponent<SceneSwitcher>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            return ScenePath;
        }

        public static XROrigin CreateXrRig()
        {
            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);

            var camGo = new GameObject("Head Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1500f;
            camGo.AddComponent<AudioListener>();

            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(
                new InputAction("Position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(
                new InputAction("Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            tpd.trackingStateInput = new InputActionProperty(
                new InputAction("Tracking State", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

            origin.CameraFloorOffsetObject = offset;
            origin.Camera = cam;
            return origin;
        }
    }
}
