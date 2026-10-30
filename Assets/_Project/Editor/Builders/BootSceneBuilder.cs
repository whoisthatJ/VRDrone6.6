using GateRush.Core;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace GateRush.EditorTools.Builders
{
    /// <summary>Builds Assets/_Project/Scenes/Boot.unity from code (minimal XR rig + ground).</summary>
    public static class BootSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Boot.unity";

        [MenuItem("GateRush/Builders/Build Boot Scene")]
        public static string Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);

            CreateXrRig();

            var boot = new GameObject("Boot");
            boot.AddComponent<XrDisplayConfig>();
            boot.AddComponent<PerfLogger>();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.55f);

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

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
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
