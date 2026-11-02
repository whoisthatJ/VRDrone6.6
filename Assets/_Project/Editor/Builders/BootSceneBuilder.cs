using GateRush.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/Boot.unity: app-wide services (display config, perf log, scene switcher)
    /// kept alive across scenes, then loads the first gameplay scene. Also hosts the shared desktop camera factory.
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
            PresetBuilder.BuildAll();    // BetaFPV radio presets
            // Persist and re-import the content first: scenes must reference the saved assets, not in-flight copies.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var order = new System.Collections.Generic.List<string> { Build(), MenuSceneBuilder.Build(), GarageSceneBuilder.Build() };
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
            "VtxSignal.screen", // desktop: the link model only feeds the OSD link quality
            "TrackEditorController.leftHand", // VR hands: unused on the desktop
            "TrackEditorController.rightHand",
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
            // Leave an empty, clean scene open so the test runner never stops on a "save modified scenes?" dialog.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (problems.Count > 0)
                throw new System.InvalidOperationException("Unassigned scene references (re-run Build All Scenes):\n" + string.Join("\n", problems));
        }

        [MenuItem("GateRush/Builders/Build Boot Scene")]
        public static string Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var app = new GameObject("App");
            app.AddComponent<AppBootstrap>();
            app.AddComponent<PerfLogger>();
            app.AddComponent<SceneSwitcher>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            return ScenePath;
        }

        /// <summary>
        /// Desktop camera: a "Camera Rig" root (moved by the track editor) with the tagged main camera at eye height,
        /// an AudioListener and URP camera data.
        /// </summary>
        public static Camera CreateCamera(Vector3 rigPosition, float verticalFov, out Transform rig)
        {
            rig = new GameObject("Camera Rig").transform;
            rig.position = rigPosition;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 1500f;
            cam.fieldOfView = verticalFov;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            return cam;
        }
    }
}
