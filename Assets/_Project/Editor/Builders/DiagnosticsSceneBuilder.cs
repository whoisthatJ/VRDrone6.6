using GateRush.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GateRush.EditorTools.Builders
{
    /// <summary>Builds Assets/_Project/Scenes/InputDiagnostics.unity: a desktop camera facing a world-space diagnostics panel.</summary>
    public static class DiagnosticsSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/InputDiagnostics.unity";

        // 1 canvas unit = 1 mm.
        private const float CanvasScale = 0.001f;
        private static readonly Vector2 PanelSize = new Vector2(1500f, 1200f);

        [MenuItem("GateRush/Builders/Build Input Diagnostics Scene")]
        public static string Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

            var head = BootSceneBuilder.CreateCamera(new Vector3(0f, -0.1f, 0f), 56f, out _);
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = new Color(0.06f, 0.07f, 0.09f);

            var canvasGo = new GameObject("Diagnostics Panel", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = PanelSize;
            rt.localScale = Vector3.one * CanvasScale;
            // On a root world-space canvas the anchored position drives x/y; set all three through it.
            rt.anchoredPosition3D = new Vector3(0f, 1.5f, 1.3f);
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

            Background(rt);
            var list = Text(rt, "Device List", new Vector2(30f, -20f), new Vector2(1440f, 260f), 22f);
            var details = Text(rt, "Details", new Vector2(30f, -290f), new Vector2(1440f, 200f), 20f);
            var footer = Text(rt, "Footer", new Vector2(30f, -1120f), new Vector2(1440f, 70f), 20f);
            details.textWrappingMode = TextWrappingModes.Normal;

            var rows = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            rows.SetParent(rt, false);
            rows.anchorMin = rows.anchorMax = new Vector2(0f, 1f);
            rows.pivot = new Vector2(0f, 1f);
            rows.anchoredPosition = new Vector2(30f, -500f);
            rows.sizeDelta = new Vector2(1440f, 600f);

            var panel = canvasGo.AddComponent<InputDiagnosticsPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("deviceList").objectReferenceValue = list;
            so.FindProperty("details").objectReferenceValue = details;
            so.FindProperty("footer").objectReferenceValue = footer;
            so.FindProperty("rowsRoot").objectReferenceValue = rows;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return ScenePath;
        }

        private static void Background(RectTransform parent)
        {
            var go = new GameObject("Background", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.14f, 0.95f);
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, Vector2 pos, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.raycastTarget = false;
            t.text = name;
            return t;
        }
    }
}
