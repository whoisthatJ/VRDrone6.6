using GateRush.Race;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    public enum GateKind
    {
        /// <summary>1.52 m (5 ft) square opening, the common race-gate size; bottom bar 1 m up.</summary>
        Square = 0,
        /// <summary>2 m square opening for beginners/fast sections; bottom bar 1 m up.</summary>
        Large = 1,
        /// <summary>3 m wide, 1.5 m tall, open at the bottom: fly under the bar.</summary>
        Hurdle = 2,
    }

    /// <summary>Builds gate prefabs (frame boxes on TrackGeometry + <see cref="Gate"/>) into Assets/_Project/Prefabs/Gates.</summary>
    public static class GateBuilder
    {
        public const string PrefabDir = "Assets/_Project/Prefabs/Gates";
        private const float Bar = 0.12f;

        public static string PrefabPath(GateKind kind) => $"{PrefabDir}/Gate_{kind}.prefab";

        [MenuItem("GateRush/Builders/Build Gate Prefabs")]
        public static void BuildAll()
        {
            int layer = LayerMask.NameToLayer(TestFieldBuilder.TrackLayerName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/GateFrame.mat");
            System.IO.Directory.CreateDirectory(PrefabDir);
            Build(GateKind.Square, new Vector2(1.52f, 1.52f), 1f, true, layer, mat);
            Build(GateKind.Large, new Vector2(2f, 2f), 1f, true, layer, mat);
            Build(GateKind.Hurdle, new Vector2(3f, 1.5f), 0f, false, layer, mat);
        }

        public static GameObject Load(GateKind kind) => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind));

        private static void Build(GateKind kind, Vector2 opening, float bottom, bool bottomBar, int layer, Material mat)
        {
            var root = new GameObject("Gate_" + kind);
            float halfW = opening.x * 0.5f + Bar * 0.5f;
            float top = bottom + opening.y + Bar * 0.5f;
            float postHeight = top + Bar * 0.5f;

            var renderers = new System.Collections.Generic.List<Renderer>
            {
                Box(root.transform, "Post L", new Vector3(-halfW, postHeight * 0.5f, 0f), new Vector3(Bar, postHeight, Bar), layer, mat),
                Box(root.transform, "Post R", new Vector3(halfW, postHeight * 0.5f, 0f), new Vector3(Bar, postHeight, Bar), layer, mat),
                Box(root.transform, "Top", new Vector3(0f, top, 0f), new Vector3(opening.x + 2f * Bar, Bar, Bar), layer, mat),
            };
            if (bottomBar)
                renderers.Add(Box(root.transform, "Bottom", new Vector3(0f, bottom - Bar * 0.5f, 0f), new Vector3(opening.x + 2f * Bar, Bar, Bar), layer, mat));
            // Feet so floating-looking gates read as standing on the ground.
            renderers.Add(Box(root.transform, "Foot L", new Vector3(-halfW, 0.02f, 0f), new Vector3(Bar * 2f, 0.04f, 0.6f), layer, mat));
            renderers.Add(Box(root.transform, "Foot R", new Vector3(halfW, 0.02f, 0f), new Vector3(Bar * 2f, 0.04f, 0.6f), layer, mat));

            var gate = root.AddComponent<Gate>();
            var so = new SerializedObject(gate);
            so.FindProperty("openingSize").vector2Value = opening;
            so.FindProperty("openingCenter").vector3Value = new Vector3(0f, bottom + opening.y * 0.5f, 0f);
            var arr = so.FindProperty("frameRenderers");
            arr.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(kind));
            Object.DestroyImmediate(root);
        }

        private static Renderer Box(Transform parent, string name, Vector3 pos, Vector3 size, int layer, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.layer = layer;
            go.isStatic = true;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }
    }
}
