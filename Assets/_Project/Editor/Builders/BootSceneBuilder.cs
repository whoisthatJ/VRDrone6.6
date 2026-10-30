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

        /// <summary>
        /// Builds all content and every scene, then sets the build order:
        /// Boot, Menu, Track_Field, Track_Hangar, Track_Industrial, InputDiagnostics.
        /// </summary>
        [MenuItem("GateRush/Builders/Build All Scenes")]
        public static string BuildAll()
        {
            PropBuilder.BuildAll();      // gate + prop prefabs, TrackPrefabs
            CatalogBuilder.BuildAll();   // track JSON, quad profiles, catalogs
            // Persist and re-import the content first: scenes must reference the saved assets, not in-flight copies.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var order = new System.Collections.Generic.List<string> { Build(), MenuSceneBuilder.Build() };
            foreach (var env in TrackSceneBuilder.Environments) order.Add(TrackSceneBuilder.Build(env));
            order.Add(DiagnosticsSceneBuilder.Build());

            EditorBuildSettings.scenes = order.ConvertAll(p => new EditorBuildSettingsScene(p, true)).ToArray();
            AssetDatabase.SaveAssets();
            VerifySceneReferences(order);
            return string.Join(", ", order);
        }

        /// <summary>
        /// Opens each built scene and fails if any GateRush component has an unassigned object reference
        /// (e.g. an asset loaded before EditorSceneManager.NewScene, which unloads unused assets).
        /// </summary>
        /// <summary>References that are optional by design.</summary>
        private static readonly System.Collections.Generic.HashSet<string> OptionalReferences = new System.Collections.Generic.HashSet<string>
        {
            "QuadVehicle.rates", // pilot rates come from the settings (TrackSceneController)
        };

        public static void VerifySceneReferences(System.Collections.Generic.IEnumerable<string> scenePaths)
        {
            var problems = new System.Collections.Generic.List<string>();
            foreach (var path in scenePaths)
            {
                EditorSceneManager.OpenScene(path);
                foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                {
                    if (mb == null || mb.GetType().Namespace == null || !mb.GetType().Namespace.StartsWith("GateRush")) continue;
                    var so = new SerializedObject(mb);
                    var it = so.GetIterator();
                    bool enter = true;
                    while (it.NextVisible(enter))
                    {
                        enter = false;
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.name != "m_Script"
                            && !OptionalReferences.Contains(mb.GetType().Name + "." + it.name))
                            problems.Add($"{System.IO.Path.GetFileNameWithoutExtension(path)}: {mb.GetType().Name}.{it.name}");
                    }
                }
            }
            if (problems.Count > 0)
                throw new System.InvalidOperationException("Unassigned scene references (re-run Build All Scenes):\n" + string.Join("\n", problems));
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
