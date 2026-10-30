using GateRush.Game;
using GateRush.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/Menu.unity: XR rig with laser pointers on both Touch controllers (XRI ray +
    /// XR UI input module, trigger = click), a world-space menu panel, and mouse support on the desktop.
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Menu.unity";

        [MenuItem("GateRush/Builders/Build Menu Scene")]
        public static string Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.intensity = 0.8f;
            light.transform.rotation = Quaternion.Euler(50f, -20f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f);

            var origin = BootSceneBuilder.CreateXrRig();
            var head = origin.Camera;
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = new Color(0.03f, 0.035f, 0.05f);

            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var lineMat = Materials.Unlit("PointerLine", Color.white, false);
            Controller(origin.CameraFloorOffsetObject.transform, "Left", lineMat);
            Controller(origin.CameraFloorOffsetObject.transform, "Right", lineMat);

            var es = new GameObject("EventSystem", typeof(EventSystem));
            var module = es.AddComponent<XRUIInputModule>();
            module.enableXRInput = true;
            module.enableMouseInput = true;
            module.enableGamepadInput = true;
            module.enableBuiltinActionsAsFallback = true;

            // Floor: a dark disc with an accent ring so the room has a sense of ground.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.localScale = new Vector3(8f, 0.01f, 8f);
            floor.GetComponent<Renderer>().sharedMaterial = Materials.Lit("MenuFloor", new Color(0.08f, 0.09f, 0.11f));
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Floor Ring";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.localScale = new Vector3(8.3f, 0.005f, 8.3f);
            ring.GetComponent<Renderer>().sharedMaterial = Materials.Emissive("MenuRing", new Color(1f, 0.55f, 0.15f), 1.5f);

            var canvasGo = new GameObject("Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(1600f, 1000f);
            rt.localScale = Vector3.one * 0.001f;
            // On a root world-space canvas the anchored position drives x/y; set all three through it.
            rt.anchoredPosition3D = new Vector3(0f, 1.45f, 1.6f);

            var menu = canvasGo.AddComponent<MenuController>();
            var so = new SerializedObject(menu);
            so.FindProperty("root").objectReferenceValue = rt;
            so.FindProperty("quads").objectReferenceValue = AssetDatabase.LoadAssetAtPath<QuadCatalog>(CatalogBuilder.QuadCatalogPath);
            so.FindProperty("tracks").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogBuilder.TrackCatalogPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            return ScenePath;
        }

        /// <summary>Aim-pose tracked controller with a UI laser: trigger = click.</summary>
        private static void Controller(Transform parent, string hand, Material lineMat)
        {
            string device = $"<XRController>{{{hand}Hand}}";
            var go = new GameObject($"{hand} Controller");
            go.transform.SetParent(parent, false);

            var tpd = go.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction($"{hand} Aim Position", InputActionType.Value, device + "/pointerPosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction($"{hand} Aim Rotation", InputActionType.Value, device + "/pointerRotation", expectedControlType: "Quaternion"));
            tpd.trackingStateInput = new InputActionProperty(new InputAction($"{hand} Tracking State", InputActionType.Value, device + "/trackingState", expectedControlType: "Integer"));

            var ray = go.AddComponent<XRRayInteractor>();
            ray.enableUIInteraction = true;
            ray.maxRaycastDistance = 10f;
            var press = new XRInputButtonReader($"{hand} UI Press", $"{hand} UI Press Value", false, XRInputButtonReader.InputSourceMode.InputAction);
            press.inputActionPerformed = new InputAction($"{hand} UI Press", InputActionType.Button, device + "/triggerPressed");
            press.inputActionValue = new InputAction($"{hand} UI Press Value", InputActionType.Value, device + "/trigger", expectedControlType: "Axis");
            ray.uiPressInput = press;

            // XRInteractorLineVisual requires (and adds) the LineRenderer.
            var visual = go.AddComponent<XRInteractorLineVisual>();
            visual.lineWidth = 0.005f;
            var line = go.GetComponent<LineRenderer>();
            line.sharedMaterial = lineMat;
            line.widthMultiplier = 0.005f;
        }
    }
}
