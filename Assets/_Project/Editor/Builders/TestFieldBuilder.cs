using GateRush.Cameras;
using GateRush.Input;
using GateRush.UI;
using GateRush.Vehicle;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GateRush.EditorTools.Builders
{
    /// <summary>Builds Assets/_Project/Scenes/Track_TestField.unity: flat field, obstacles, a flyable quad with FPV camera and debug overlay.</summary>
    public static class TestFieldBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Track_TestField.unity";
        public const string TrackLayerName = "TrackGeometry";
        public const string LocalQuadLayerName = "LocalQuad";
        private const string MatDir = "Assets/_Project/Art/Materials";
        private const string DataDir = "Assets/_Project/Data";

        [MenuItem("GateRush/Builders/Build Test Field Scene")]
        public static string Build()
        {
            int trackLayer = EnsureLayer(TrackLayerName, 8);
            int quadLayer = EnsureLayer(LocalQuadLayerName, 9);
            var assets = EnsureDataAssets(1 << trackLayer);
            var mats = EnsureMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.5f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.22f);

            var track = new GameObject("Track");
            BuildField(track.transform, trackLayer, mats);

            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(track.transform, false);
            spawn.position = new Vector3(0f, 0.15f, 0f);

            var pilot = new GameObject("Pilot").AddComponent<KeyboardGamepadSource>();
            var quad = BuildQuad(mats, assets, pilot, spawn, quadLayer);
            BuildOverlay(quad);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        private static void BuildField(Transform parent, int layer, Materials mats)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(200f, 1f, 200f); // 2 km
            ground.GetComponent<Renderer>().sharedMaterial = mats.Ground;
            ground.layer = layer;

            var pad = Box(parent, "Spawn Pad", new Vector3(0f, 0.01f, 0f), new Vector3(1.5f, 0.02f, 1.5f), mats.Accent, layer);
            pad.transform.SetParent(parent, false);

            // Pillars on a loose grid for depth and speed cues.
            var rng = new System.Random(1234);
            for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
            {
                if (x == 0 && z <= 1 && z >= -1) continue;
                float h = 3f + (float)rng.NextDouble() * 9f;
                var pos = new Vector3(x * 25f + (float)rng.NextDouble() * 8f - 4f, h * 0.5f, z * 25f + (float)rng.NextDouble() * 8f - 4f);
                Box(parent, $"Pillar {x},{z}", pos, new Vector3(1.2f, h, 1.2f), mats.Pillar, layer);
            }

            // Simple 1.5 m square arches as fly-through references (real race gates come in M4).
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                var centre = new Vector3(Mathf.Sin(angle) * 40f, 0f, Mathf.Cos(angle) * 40f + 10f);
                var rot = Quaternion.Euler(0f, i * 60f + 90f, 0f);
                Arch(parent, $"Arch {i}", centre, rot, 1.5f, mats.Accent, layer);
            }
        }

        private static void Arch(Transform parent, string name, Vector3 basePos, Quaternion rot, float opening, Material mat, int layer)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(basePos, rot);
            const float t = 0.1f;
            float half = opening * 0.5f + t * 0.5f;
            Box(root, "Left", new Vector3(-half, opening * 0.5f + 0.5f, 0f), new Vector3(t, opening + 1f, t), mat, layer, true);
            Box(root, "Right", new Vector3(half, opening * 0.5f + 0.5f, 0f), new Vector3(t, opening + 1f, t), mat, layer, true);
            Box(root, "Top", new Vector3(0f, opening + 1f + t * 0.5f, 0f), new Vector3(opening + 2f * t, t, t), mat, layer, true);
            Box(root, "Bottom", new Vector3(0f, 1f - t * 0.5f, 0f), new Vector3(opening + 2f * t, t, t), mat, layer, true);
        }

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, int layer, bool local = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            if (local) go.transform.localPosition = pos;
            else go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.layer = layer;
            go.isStatic = true;
            return go;
        }

        private static QuadVehicle BuildQuad(Materials mats, DataAssets assets, PilotInputSource pilot, Transform spawn, int quadLayer)
        {
            var root = new GameObject("Quad");
            root.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Part(visual, PrimitiveType.Cube, "Body", new Vector3(0f, 0f, 0f), new Vector3(0.045f, 0.035f, 0.14f), Quaternion.identity, mats.Frame);
            Part(visual, PrimitiveType.Cube, "Arm A", Vector3.zero, new Vector3(0.022f, 0.006f, 0.24f), Quaternion.Euler(0f, 45f, 0f), mats.Frame);
            Part(visual, PrimitiveType.Cube, "Arm B", Vector3.zero, new Vector3(0.022f, 0.006f, 0.24f), Quaternion.Euler(0f, -45f, 0f), mats.Frame);

            var quad = root.AddComponent<QuadVehicle>();
            var props = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var p = GateRush.Flight.Mixer.MotorPosition(i, 0.078f, 0.078f);
                var pos = new Vector3(p.x, 0.012f, p.z);
                Part(visual, PrimitiveType.Cylinder, $"Motor {i}", pos, new Vector3(0.026f, 0.01f, 0.026f), Quaternion.identity, mats.Frame);
                var prop = Part(visual, PrimitiveType.Cylinder, $"Prop {i}", pos + new Vector3(0f, 0.012f, 0f),
                    new Vector3(0.127f, 0.0015f, 0.127f), Quaternion.identity, i == 1 || i == 3 ? mats.PropFront : mats.PropRear);
                props[i] = prop.transform;
            }

            var camGo = new GameObject("FPV Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.02f, 0.095f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            // The pilot's own quad is hidden from its FPV view (it is still visible to LOS/spectator cameras).
            cam.cullingMask = ~(1 << quadLayer);
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = quadLayer;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<FpvCamera>();

            var so = new SerializedObject(quad);
            so.FindProperty("profile").objectReferenceValue = assets.Quad;
            so.FindProperty("rates").objectReferenceValue = assets.Rates;
            so.FindProperty("physics").objectReferenceValue = assets.Physics;
            so.FindProperty("input").objectReferenceValue = pilot;
            so.FindProperty("spawnPoint").objectReferenceValue = spawn;
            so.ApplyModifiedPropertiesWithoutUndo();

            var propVisuals = root.AddComponent<PropVisuals>();
            var pso = new SerializedObject(propVisuals);
            pso.FindProperty("vehicle").objectReferenceValue = quad;
            var arr = pso.FindProperty("props");
            arr.arraySize = 4;
            for (int i = 0; i < 4; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = props[i];
            pso.ApplyModifiedPropertiesWithoutUndo();
            return quad;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private static void BuildOverlay(QuadVehicle quad)
        {
            var canvasGo = new GameObject("Debug Overlay", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var overlay = canvasGo.AddComponent<FlightDebugOverlay>();
            var status = Label(canvasGo.transform, "Status", 0, 30);
            var sticks = Label(canvasGo.transform, "Sticks", 1, 22);
            var rates = Label(canvasGo.transform, "Rates", 2, 22);
            var motion = Label(canvasGo.transform, "Motion", 3, 22);

            var so = new SerializedObject(overlay);
            so.FindProperty("vehicle").objectReferenceValue = quad;
            so.FindProperty("status").objectReferenceValue = status;
            so.FindProperty("sticks").objectReferenceValue = sticks;
            so.FindProperty("rates").objectReferenceValue = rates;
            so.FindProperty("motion").objectReferenceValue = motion;
            var bars = so.FindProperty("motorBars");
            bars.arraySize = 4;
            string[] names = { "RR", "FR", "RL", "FL" };
            for (int i = 0; i < 4; i++) bars.GetArrayElementAtIndex(i).objectReferenceValue = MotorBar(canvasGo.transform, i, names[i]);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TextMeshProUGUI Label(Transform parent, string name, int row, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -20f - row * 36f);
            rt.sizeDelta = new Vector2(1200f, 36f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.outlineWidth = 0.2f;
            t.text = name;
            return t;
        }

        private static RectTransform MotorBar(Transform parent, int i, string label)
        {
            var frame = new GameObject("Motor " + label, typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(parent, false);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0f);
            frt.pivot = new Vector2(0f, 0f);
            frt.anchoredPosition = new Vector2(24f + i * 34f, 40f);
            frt.sizeDelta = new Vector2(26f, 160f);
            frame.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

            var bar = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(frame.transform, false);
            var brt = (RectTransform)bar.transform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            bar.GetComponent<Image>().color = new Color(0.3f, 0.9f, 0.4f, 0.9f);

            var text = Label(frame.transform, "Label", 0, 16);
            text.text = label;
            var trt = (RectTransform)text.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 0f);
            trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(0f, -2f);
            trt.sizeDelta = new Vector2(30f, 20f);
            return brt;
        }

        // ---------- assets ----------

        private struct DataAssets
        {
            public QuadProfileAsset Quad;
            public RateProfileAsset Rates;
            public PhysicsSettingsAsset Physics;
        }

        private struct Materials
        {
            public Material Ground, Pillar, Accent, Frame, PropFront, PropRear;
        }

        private static DataAssets EnsureDataAssets(int trackMask)
        {
            var quad = LoadOrCreate<QuadProfileAsset>($"{DataDir}/QuadProfiles/Race5.asset");
            var rates = LoadOrCreate<RateProfileAsset>($"{DataDir}/Rates/Default_Betaflight.asset");
            var physics = LoadOrCreate<PhysicsSettingsAsset>($"{DataDir}/PhysicsSettings.asset");
            physics.Collision.TrackGeometry = trackMask;
            if (physics.Collision.OutOfBoundsY == 0f) physics.Collision.OutOfBoundsY = CollisionSettings.Default.OutOfBoundsY;
            EditorUtility.SetDirty(physics);
            return new DataAssets { Quad = quad, Rates = rates, Physics = physics };
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Materials EnsureMaterials()
        {
            System.IO.Directory.CreateDirectory(MatDir);
            // Regenerate in place so the texture keeps its GUID across rebuilds.
            var grid = AssetDatabase.LoadAssetAtPath<Texture2D>($"{MatDir}/Grid.asset");
            var fresh = MakeGridTexture();
            if (grid == null)
            {
                grid = fresh;
                AssetDatabase.CreateAsset(grid, $"{MatDir}/Grid.asset");
            }
            else
            {
                EditorUtility.CopySerialized(fresh, grid);
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(grid);
            }

            var ground = Mat("Ground", new Color(0.35f, 0.45f, 0.3f));
            ground.SetTexture("_BaseMap", grid);
            ground.SetTextureScale("_BaseMap", new Vector2(400f, 400f)); // 5 m cells on the 2 km plane
            return new Materials
            {
                Ground = ground,
                Pillar = Mat("Pillar", new Color(0.75f, 0.72f, 0.68f)),
                Accent = Mat("Accent", new Color(1f, 0.45f, 0.1f)),
                Frame = Mat("Frame", new Color(0.12f, 0.12f, 0.13f)),
                PropFront = Mat("PropFront", new Color(0.9f, 0.15f, 0.15f)),
                PropRear = Mat("PropRear", new Color(0.15f, 0.15f, 0.15f)),
            };
        }

        private static Material Mat(string name, Color color)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Texture2D MakeGridTexture()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Grid", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool line = x < 4 || y < 4;
                byte v = line ? (byte)255 : (byte)150;
                px[y * size + x] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private static int EnsureLayer(string name, int preferred)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            int index = preferred;
            for (int i = preferred; i < 32 && !string.IsNullOrEmpty(layers.GetArrayElementAtIndex(index).stringValue); i++) index = i + 1;
            layers.GetArrayElementAtIndex(index).stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return index;
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
