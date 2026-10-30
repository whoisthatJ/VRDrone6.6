using GateRush.Cameras;
using GateRush.Input;
using GateRush.Race;
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
        public const string VrRoomLayerName = "VRRoom";
        private const string MatDir = "Assets/_Project/Art/Materials";
        private const string DataDir = "Assets/_Project/Data";

        [MenuItem("GateRush/Builders/Build Test Field Scene")]
        public static string Build()
        {
            int trackLayer = EnsureLayer(TrackLayerName, 8);
            int quadLayer = EnsureLayer(LocalQuadLayerName, 9);
            int roomLayer = EnsureLayer(VrRoomLayerName, 10);
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

            var pilotGo = new GameObject("Pilot");
            var keyboardPad = pilotGo.AddComponent<KeyboardGamepadSource>();
            var touch = pilotGo.AddComponent<TouchControllerSource>();
            var quad = BuildQuad(mats, assets, keyboardPad, spawn, quadLayer);
            var router = pilotGo.AddComponent<VehicleInputRouter>();
            var rso = new SerializedObject(router);
            rso.FindProperty("vehicle").objectReferenceValue = quad;
            var srcs = rso.FindProperty("sources");
            srcs.arraySize = 2;
            srcs.GetArrayElementAtIndex(0).objectReferenceValue = keyboardPad;
            srcs.GetArrayElementAtIndex(1).objectReferenceValue = touch;
            rso.ApplyModifiedPropertiesWithoutUndo();

            GateBuilder.BuildAll();
            var race = BuildRace(quad, spawn, track.transform, mats);

            var fpv = quad.GetComponentInChildren<FpvCamera>();
            BuildOverlay(quad, fpv.GetComponent<Camera>(), race);
            BuildViews(quad, fpv, mats, roomLayer, quadLayer);

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
                float h = 3f + (float)rng.NextDouble() * 9f;
                var pos = new Vector3(x * 25f + (float)rng.NextDouble() * 8f - 4f, h * 0.5f, z * 25f + (float)rng.NextDouble() * 8f - 4f);
                // Keep the race line clear.
                if (NearCourse(pos, 9f)) continue;
                Box(parent, $"Pillar {x},{z}", pos, new Vector3(1.2f, h, 1.2f), mats.Pillar, layer);
            }

        }

        // ---------- race course ----------

        private struct CourseGate
        {
            public Vector3 Position;
            public GateKind Kind;
            public CourseGate(float x, float y, float z, GateKind kind) { Position = new Vector3(x, y, z); Kind = kind; }
        }

        // ~285 m loop. Gate 0 is start/finish, straight ahead of the pad. Gate 2 floats 3 m up.
        private static readonly CourseGate[] Course =
        {
            new CourseGate(0f, 0f, 14f, GateKind.Square),
            new CourseGate(22f, 0f, 45f, GateKind.Square),
            new CourseGate(55f, 3f, 62f, GateKind.Square),
            new CourseGate(88f, 0f, 42f, GateKind.Hurdle),
            new CourseGate(82f, 0f, 2f, GateKind.Large),
            new CourseGate(52f, 0f, -26f, GateKind.Square),
            new CourseGate(16f, 0f, -32f, GateKind.Large),
            new CourseGate(-18f, 0f, -12f, GateKind.Square),
        };

        /// <summary>True if p (XZ) is within <paramref name="clearance"/> of the course line or the pad.</summary>
        private static bool NearCourse(Vector3 p, float clearance)
        {
            var q = new Vector2(p.x, p.z);
            if (q.magnitude < clearance) return true;
            for (int i = 0; i < Course.Length; i++)
            {
                var a = new Vector2(Course[i].Position.x, Course[i].Position.z);
                var b = new Vector2(Course[(i + 1) % Course.Length].Position.x, Course[(i + 1) % Course.Length].Position.z);
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                if (Vector2.Distance(q, a + ab * t) < clearance) return true;
            }
            return false;
        }

        private static Gate[] BuildCourse(Transform parent)
        {
            var gates = new Gate[Course.Length];
            for (int i = 0; i < Course.Length; i++)
            {
                var c = Course[i];
                Vector3 heading = i == 0
                    ? Vector3.forward
                    : Course[(i + 1) % Course.Length].Position - Course[i - 1].Position;
                heading.y = 0f;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(GateBuilder.Load(c.Kind), parent);
                go.name = $"Gate {i} ({c.Kind})";
                go.transform.SetPositionAndRotation(c.Position, Quaternion.LookRotation(heading.normalized, Vector3.up));
                gates[i] = go.GetComponent<Gate>();
            }
            return gates;
        }

        private static RaceSession BuildRace(QuadVehicle quad, Transform startPad, Transform track, Materials mats)
        {
            var gates = BuildCourse(track);

            var raceGo = new GameObject("Race");
            var ghosts = raceGo.AddComponent<GhostSystem>();
            var ghostVisual = BuildGhostVisual(mats);
            var gso = new SerializedObject(ghosts);
            gso.FindProperty("ghostVisual").objectReferenceValue = ghostVisual;
            gso.ApplyModifiedPropertiesWithoutUndo();

            var session = raceGo.AddComponent<RaceSession>();
            var so = new SerializedObject(session);
            so.FindProperty("vehicle").objectReferenceValue = quad;
            so.FindProperty("ghosts").objectReferenceValue = ghosts;
            so.FindProperty("startPad").objectReferenceValue = startPad;
            so.FindProperty("trackId").stringValue = "Test Field";
            var arr = so.FindProperty("gates");
            arr.arraySize = gates.Length;
            for (int i = 0; i < gates.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = gates[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return session;
        }

        /// <summary>A see-through copy of the quad's silhouette for ghost replays (visible in FPV, no colliders).</summary>
        private static Transform BuildGhostVisual(Materials mats)
        {
            var root = new GameObject("Ghost Quad").transform;
            Part(root, PrimitiveType.Cube, "Body", Vector3.zero, new Vector3(0.06f, 0.04f, 0.16f), Quaternion.identity, mats.Ghost);
            Part(root, PrimitiveType.Cube, "Arm A", Vector3.zero, new Vector3(0.03f, 0.01f, 0.26f), Quaternion.Euler(0f, 45f, 0f), mats.Ghost);
            Part(root, PrimitiveType.Cube, "Arm B", Vector3.zero, new Vector3(0.03f, 0.01f, 0.26f), Quaternion.Euler(0f, -45f, 0f), mats.Ghost);
            for (int i = 0; i < 4; i++)
            {
                var p = GateRush.Flight.Mixer.MotorPosition(i, 0.078f, 0.078f);
                Part(root, PrimitiveType.Cylinder, $"Prop {i}", new Vector3(p.x, 0.02f, p.z), new Vector3(0.13f, 0.002f, 0.13f), Quaternion.identity, mats.Ghost);
            }
            return root;
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

            // The XR rig's head camera is MainCamera and owns the AudioListener; this one only feeds the goggles/display.
            var camGo = new GameObject("FPV Camera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.02f, 0.095f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            // The pilot's own quad is hidden from its FPV view (it is still visible to LOS/spectator cameras).
            cam.cullingMask = ~(1 << quadLayer);
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = quadLayer;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
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

        /// <summary>XR rig, goggles room + curved screen, comfort vignette, LOS stand and the view-mode manager.</summary>
        private static void BuildViews(QuadVehicle quad, FpvCamera fpv, Materials mats, int roomLayer, int quadLayer)
        {
            var origin = BootSceneBuilder.CreateXrRig();
            origin.transform.position = new Vector3(0f, 0f, -2f);
            var head = origin.Camera;

            var room = new GameObject("Goggles Room") { layer = roomLayer };
            var screenGo = new GameObject("Goggles Screen", typeof(MeshFilter), typeof(MeshRenderer)) { layer = roomLayer };
            screenGo.transform.SetParent(room.transform, false);
            var screenRenderer = screenGo.GetComponent<MeshRenderer>();
            screenRenderer.sharedMaterial = mats.Screen;
            screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var screen = screenGo.AddComponent<GogglesScreen>();
            var sso = new SerializedObject(screen);
            sso.FindProperty("head").objectReferenceValue = head.transform;
            sso.ApplyModifiedPropertiesWithoutUndo();

            var vignette = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vignette.name = "Comfort Vignette";
            Object.DestroyImmediate(vignette.GetComponent<Collider>());
            vignette.transform.SetParent(head.transform, false);
            vignette.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            vignette.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            var vr = vignette.GetComponent<MeshRenderer>();
            vr.sharedMaterial = mats.Vignette;
            vr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var comfort = vignette.AddComponent<ComfortVignette>();
            var cso = new SerializedObject(comfort);
            cso.FindProperty("vehicle").objectReferenceValue = quad;
            cso.ApplyModifiedPropertiesWithoutUndo();
            vignette.SetActive(false);

            var stand = new GameObject("LOS Stand").transform;
            stand.position = new Vector3(-4f, 0f, -6f);
            stand.rotation = Quaternion.LookRotation(new Vector3(4f, 0f, 14f));

            var viewsGo = new GameObject("Views");
            var views = viewsGo.AddComponent<ViewModeManager>();
            var vso = new SerializedObject(views);
            vso.FindProperty("vehicle").objectReferenceValue = quad;
            vso.FindProperty("xrOrigin").objectReferenceValue = origin.transform;
            vso.FindProperty("headCamera").objectReferenceValue = head;
            vso.FindProperty("droneCamera").objectReferenceValue = fpv;
            vso.FindProperty("gogglesScreen").objectReferenceValue = screen;
            vso.FindProperty("gogglesRoom").objectReferenceValue = room;
            vso.FindProperty("vignette").objectReferenceValue = comfort;
            vso.FindProperty("losStand").objectReferenceValue = stand;
            vso.FindProperty("roomLayer").intValue = 1 << roomLayer;
            vso.FindProperty("localQuadLayer").intValue = 1 << quadLayer;
            vso.ApplyModifiedPropertiesWithoutUndo();

            var input = viewsGo.AddComponent<ViewModeInput>();
            var iso = new SerializedObject(input);
            iso.FindProperty("views").objectReferenceValue = views;
            iso.FindProperty("gogglesScreen").objectReferenceValue = screen;
            iso.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildOverlay(QuadVehicle quad, Camera fpvCamera, RaceSession race)
        {
            var canvasGo = new GameObject("Debug Overlay", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            // Drawn by the FPV camera, so it shows inside the goggles feed like an OSD.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = fpvCamera;
            canvas.planeDistance = 0.05f;
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

            BuildRaceHud(canvasGo.transform, race);
        }

        private static void BuildRaceHud(Transform canvas, RaceSession race)
        {
            var lap = Label(canvas, "Lap Info", 0, 28);
            var lrt = (RectTransform)lap.transform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 1f);
            lrt.anchoredPosition = new Vector2(-24f, -20f);
            lrt.sizeDelta = new Vector2(700f, 80f);
            lap.alignment = TextAlignmentOptions.TopRight;

            var centre = Label(canvas, "Centre", 0, 72);
            var crt = (RectTransform)centre.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(0f, 160f);
            crt.sizeDelta = new Vector2(1400f, 120f);
            centre.alignment = TextAlignmentOptions.Center;
            centre.fontStyle = FontStyles.Bold;
            centre.text = "";

            var panel = new GameObject("Results", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvas, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, -110f);
            prt.sizeDelta = new Vector2(1200f, 470f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var results = Label(panel.transform, "Results Text", 0, 26);
            var rrt = (RectTransform)results.transform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = new Vector2(30f, 20f);
            rrt.offsetMax = new Vector2(-30f, -20f);
            results.alignment = TextAlignmentOptions.TopLeft;
            panel.SetActive(false);

            var hud = canvas.gameObject.AddComponent<RaceHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("session").objectReferenceValue = race;
            so.FindProperty("lapInfo").objectReferenceValue = lap;
            so.FindProperty("centre").objectReferenceValue = centre;
            so.FindProperty("resultsPanel").objectReferenceValue = panel;
            so.FindProperty("resultsText").objectReferenceValue = results;
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
            public Material Ground, Pillar, Accent, Frame, PropFront, PropRear, Screen, Vignette, Ghost, GateFrame;
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
                Screen = UnlitMat("GogglesScreen", Color.white, false),
                Vignette = UnlitMat("Vignette", Color.black, true),
                Ghost = UnlitMat("Ghost", new Color(0.3f, 0.85f, 1f, 0.35f), true),
                // Lit, colour driven per gate state by Gate.SetVisualState.
                GateFrame = Mat("GateFrame", new Color(0.55f, 0.55f, 0.6f)),
            };
        }

        private static Material UnlitMat(string name, Color color, bool transparent)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            }
            EditorUtility.SetDirty(m);
            return m;
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
