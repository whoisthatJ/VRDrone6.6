using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// V7 gates, props and the quad. Gates: padded fabric covers in race colours (per type) over the unchanged
    /// colliders, brushed-aluminium bases with sandbags, and LED strips on both faces that become the gate's
    /// frameRenderers (so next/upcoming/idle states light the LEDs). Props: CC0 crate and barrel models, painted
    /// containers, concrete pillar and wall. Quad (per track scene): carbon frame, anodized motor bells, tinted
    /// polycarbonate props, LiPo pack, camera, antenna and a rear LED. Art children are named "Art …" so re-runs
    /// replace them.
    /// </summary>
    public static partial class PrefabArt
    {
        private const string GateDir = "Assets/_Project/Prefabs/Gates";
        private const string PropDir = "Assets/_Project/Prefabs/Props";

        static partial void ApplyImpl(ref string log)
        {
            var sb = new System.Text.StringBuilder("prefabs:");
            sb.Append(Gate("Gate_Square", "M_Fabric_GateOrange"));
            sb.Append(Gate("Gate_Large", "M_Fabric_GateBlue"));
            sb.Append(Gate("Gate_Hurdle", "M_Fabric_GateRed"));
            sb.Append(Prop("Prop_Crate", body => Model(body, "wooden_crate_02", true)));
            sb.Append(Prop("Prop_Barrel", body => Model(body, "Barrel_01", false)));
            sb.Append(Prop("Prop_Pillar", body => Box(body, "M_Concrete_Pillar")));
            sb.Append(Prop("Prop_Wall", body => Box(body, "M_Concrete_Blocks")));
            sb.Append(Prop("Prop_Container", body => Box(body, "M_Container_Blue")));
            sb.Append(Prop("Prop_ContainerRed", body => Box(body, "M_Container_Red")));
            sb.Append(Prop("Prop_ContainerGreen", body => Box(body, "M_Container_Green")));
            AssetDatabase.SaveAssets();
            log = sb.ToString();
        }

        private static string Key(Vector3 s) => $"{s.x:0.###}x{s.y:0.###}x{s.z:0.###}".Replace(',', '.');

        private static Mesh BoxAsset(Vector3 size) => ArtPass.MeshAsset("Box_" + Key(size), () => ProcMesh.Box(size, Vector3.zero, false));
        private static Mesh PaddedAsset(Vector3 size, float corner = 0.42f) => ArtPass.MeshAsset("Padded_" + Key(size) + "_" + corner.ToString("0.##").Replace(',', '.'), () => ProcMesh.PaddedBar(size, corner));

        private static void ClearArt(Transform t)
        {
            foreach (var child in t.Cast<Transform>().ToList())
                if (child.name.StartsWith("Art ")) Object.DestroyImmediate(child.gameObject);
        }

        // ---------- gates ----------

        private static string Gate(string name, string fabric)
        {
            string path = $"{GateDir}/{name}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ClearArt(root.transform);
                var all = new List<Renderer>();
                var leds = new List<Renderer>();
                var ledMat = MaterialLibrary.Get("M_LED_Gate");
                foreach (string part in new[] { "Post L", "Post R", "Top", "Bottom" })
                {
                    var t = root.transform.Find(part);
                    if (t == null) continue;
                    var size = t.localScale;
                    t.GetComponent<MeshFilter>().sharedMesh = PaddedAsset(size);
                    var r = t.GetComponent<MeshRenderer>();
                    r.sharedMaterial = MaterialLibrary.Get(fabric);
                    r.shadowCastingMode = ShadowCastingMode.On;
                    all.Add(r);
                    bool vertical = size.y >= size.x;
                    foreach (float face in new[] { -1f, 1f })
                    {
                        var ledSize = vertical ? new Vector3(0.03f, size.y * 0.9f, 0.006f) : new Vector3(size.x * 0.9f, 0.03f, 0.006f);
                        var led = new GameObject($"Art LED {part} {(face > 0 ? "front" : "back")}") { layer = 0 };
                        led.transform.SetParent(root.transform, false);
                        led.transform.localPosition = t.localPosition + new Vector3(0f, 0f, face * (size.z / 2f + 0.002f));
                        led.transform.localScale = ledSize;
                        led.AddComponent<MeshFilter>().sharedMesh = BoxAsset(ledSize);
                        var lr = led.AddComponent<MeshRenderer>();
                        lr.sharedMaterial = ledMat;
                        lr.shadowCastingMode = ShadowCastingMode.Off;
                        lr.receiveShadows = false;
                        leds.Add(lr);
                        all.Add(lr);
                    }
                }
                var bag = PaddedAsset(new Vector3(0.42f, 0.13f, 0.28f), 0.5f);
                foreach (string part in new[] { "Foot L", "Foot R" })
                {
                    var t = root.transform.Find(part);
                    if (t == null) continue;
                    var size = t.localScale;
                    t.GetComponent<MeshFilter>().sharedMesh = BoxAsset(size);
                    var r = t.GetComponent<MeshRenderer>();
                    r.sharedMaterial = MaterialLibrary.Get("M_Aluminium_Brushed");
                    all.Add(r);
                    for (int k = 0; k < 2; k++)
                    {
                        var sb = new GameObject($"Art Sandbag {part} {k}");
                        sb.transform.SetParent(root.transform, false);
                        float along = (k == 0 ? -1f : 1f) * Mathf.Max(size.x, size.z) * 0.28f;
                        bool alongX = size.x >= size.z;
                        sb.transform.localPosition = t.localPosition + new Vector3(alongX ? along : 0f, size.y / 2f + 0.065f, alongX ? 0f : along);
                        sb.transform.localRotation = Quaternion.Euler(0f, alongX ? 90f : 0f, 0f);
                        sb.transform.localScale = new Vector3(0.42f, 0.13f, 0.28f);
                        sb.AddComponent<MeshFilter>().sharedMesh = bag;
                        var br = sb.AddComponent<MeshRenderer>();
                        br.sharedMaterial = MaterialLibrary.Get("M_Fabric_Sandbag");
                        all.Add(br);
                    }
                }

                var gate = root.GetComponent<GateRush.Race.Gate>();
                var so = new SerializedObject(gate);
                var fr = so.FindProperty("frameRenderers");
                fr.arraySize = leds.Count;
                for (int i = 0; i < leds.Count; i++) fr.GetArrayElementAtIndex(i).objectReferenceValue = leds[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                SetLod(root, all);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $" {name} ({leds.Count} LEDs)";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetLod(GameObject root, List<Renderer> renderers)
        {
            var lod = root.GetComponent<LODGroup>();
            if (lod == null) return;
            var lods = lod.GetLODs();
            if (lods.Length == 0) return;
            lods[0].renderers = renderers.ToArray();
            lod.SetLODs(lods);
            lod.RecalculateBounds();
        }

        // ---------- props ----------

        private static string Prop(string name, System.Func<Transform, List<Renderer>> art)
        {
            string path = $"{PropDir}/{name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return $" {name} (missing)";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ClearArt(root.transform);
                var body = root.transform.Find("Body");
                var renderers = art(body);
                SetLod(root, renderers);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $" {name}";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static List<Renderer> Box(Transform body, string material)
        {
            body.GetComponent<MeshFilter>().sharedMesh = BoxAsset(body.localScale);
            var r = body.GetComponent<MeshRenderer>();
            r.enabled = true;
            r.sharedMaterial = MaterialLibrary.Get(material);
            r.shadowCastingMode = ShadowCastingMode.On;
            return new List<Renderer> { r };
        }

        /// <summary>Hides the primitive (its collider stays) and fits a CC0 model into the body's bounds.</summary>
        private static List<Renderer> Model(Transform body, string modelId, bool uniform)
        {
            var bodyRenderer = body.GetComponent<MeshRenderer>();
            bodyRenderer.enabled = true;
            var target = bodyRenderer.bounds; // prefab contents: the body's world bounds (read while enabled)
            bodyRenderer.enabled = false;
            var src = ArtCatalog.Find(modelId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{src.Folder}/{src.Id}_{src.Resolution}.fbx");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, body.parent);
            model.name = "Art Model";
            var renderers = model.GetComponentsInChildren<MeshRenderer>(true).Cast<Renderer>().ToList();
            var b = ArtPass.Bounds(renderers);
            Vector3 k = new Vector3(target.size.x / b.size.x, target.size.y / b.size.y, target.size.z / b.size.z);
            if (uniform) k = Vector3.one * Mathf.Min(k.x, Mathf.Min(k.y, k.z));
            model.transform.localScale = Vector3.Scale(model.transform.localScale, k);
            b = ArtPass.Bounds(renderers);
            model.transform.position += new Vector3(target.center.x - b.center.x, target.min.y - b.min.y, target.center.z - b.center.z);
            foreach (var r in renderers)
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => ArtPass.ModelMaterial(src, m)).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On;
                r.gameObject.layer = body.gameObject.layer;
            }
            return renderers;
        }

        // ---------- quad (per track scene) ----------

        public static void ApplyQuad(ArtContext ctx)
        {
            var quad = ctx.Scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Quad");
            var visual = quad != null ? quad.transform.Find("Visual") : null;
            if (visual == null) return;
            ClearArt(visual);
            int layer = visual.gameObject.layer;
            foreach (Transform part in visual)
            {
                var r = part.GetComponent<MeshRenderer>();
                if (r == null) continue;
                string n = part.name;
                if (n == "Body" || n.StartsWith("Arm"))
                {
                    part.GetComponent<MeshFilter>().sharedMesh = BoxAsset(part.localScale);
                    r.sharedMaterial = MaterialLibrary.Get("M_CarbonFiber");
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                else if (n.StartsWith("Motor"))
                {
                    r.sharedMaterial = MaterialLibrary.Get("M_Metal_Anodized");
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                else if (n.StartsWith("Prop"))
                {
                    string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "";
                    bool front = mat == "PropFront" || mat == "M_Prop_Front";
                    r.sharedMaterial = MaterialLibrary.Get(front ? "M_Prop_Front" : "M_Prop_Rear");
                }
            }

            void Add(string name, Vector3 pos, Vector3 size, string material, Quaternion rot = default)
            {
                var go = new GameObject("Art " + name) { layer = layer };
                go.transform.SetParent(visual, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f ? Quaternion.identity : rot;
                go.transform.localScale = size;
                go.AddComponent<MeshFilter>().sharedMesh = BoxAsset(size);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialLibrary.Get(material);
                mr.shadowCastingMode = ShadowCastingMode.On;
            }

            Add("LiPo", new Vector3(0f, 0.036f, -0.012f), new Vector3(0.036f, 0.032f, 0.075f), "M_Lipo");
            Add("Strap", new Vector3(0f, 0.053f, -0.012f), new Vector3(0.044f, 0.004f, 0.012f), "M_Plastic_Black");
            Add("Top plate", new Vector3(0f, 0.019f, 0f), new Vector3(0.05f, 0.003f, 0.12f), "M_CarbonFiber");
            Add("Camera", new Vector3(0f, 0.004f, 0.072f), new Vector3(0.02f, 0.02f, 0.018f), "M_Plastic_Black");
            Add("Lens", new Vector3(0f, 0.006f, 0.082f), new Vector3(0.012f, 0.012f, 0.006f), "M_Glass_Night");
            Add("Antenna", new Vector3(0f, 0.03f, -0.075f), new Vector3(0.004f, 0.004f, 0.06f), "M_Plastic_Black", Quaternion.Euler(-35f, 0f, 0f));
            Add("LED", new Vector3(0f, 0.004f, -0.0705f), new Vector3(0.03f, 0.006f, 0.003f), "M_LED_Red");
            ctx.Log.Append(" quad;");
        }
    }
}
