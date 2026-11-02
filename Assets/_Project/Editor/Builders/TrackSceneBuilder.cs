using GateRush.Cameras;
using GateRush.Game;
using GateRush.Input;
using GateRush.Race;
using GateRush.Track;
using GateRush.UI;
using GateRush.Vehicle;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Builds the track scenes (Track_Field, Track_Hangar, Track_Industrial): environment geometry, the pilot's quad
    /// with its FPV camera mount, the desktop camera set (Cinemachine FPV / chase / line of sight), the screen-space
    /// OSD and race HUD, race session and the <see cref="TrackSceneController"/> that loads the selected track JSON at
    /// runtime.
    /// </summary>
    public static class TrackSceneBuilder
    {
        public const string TrackLayerName = "TrackGeometry";
        public const string LocalQuadLayerName = "LocalQuad";
        public static readonly string[] Environments = { "field", "hangar", "industrial", "stadium" };
        private const string MatDir = "Assets/_Project/Art/Materials";
        private const string DataDir = "Assets/_Project/Data";

        public static string ScenePath(string environment) =>
            $"Assets/_Project/Scenes/Track_{char.ToUpperInvariant(environment[0])}{environment.Substring(1)}.unity";

        [MenuItem("GateRush/Builders/Build Track Scenes")]
        public static string BuildAllEnvironments()
        {
            var paths = new System.Collections.Generic.List<string>();
            foreach (var env in Environments) paths.Add(Build(env));
            return string.Join(", ", paths);
        }

        public static string Build(string environment)
        {
            // NewScene unloads unused assets, so create the scene before loading anything it will reference.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            int trackLayer = EnsureLayer(TrackLayerName, 8);
            int quadLayer = EnsureLayer(LocalQuadLayerName, 9);
            var assets = EnsureDataAssets(1 << trackLayer);
            var mats = EnsureMaterials();
            var trackPrefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PropBuilder.TrackPrefabsPath);
            var quadCatalog = AssetDatabase.LoadAssetAtPath<QuadCatalog>(CatalogBuilder.QuadCatalogPath);
            var trackCatalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogBuilder.TrackCatalogPath);

            var world = new GameObject("Environment");
            EnvironmentBuilder.Build(environment, world.transform, trackLayer, mats.Ground);

            // Placeholder spawn; the track JSON's spawn replaces it at runtime.
            var spawn = new GameObject("Default Spawn").transform;
            spawn.position = new Vector3(0f, 0.15f, 0f);

            var pilotGo = new GameObject("Pilot");
            var keyboardPad = pilotGo.AddComponent<KeyboardGamepadSource>();
            var provider = pilotGo.AddComponent<RadioDeviceProvider>();
            var pso = new SerializedObject(provider);
            pso.FindProperty("presets").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ControllerPresetLibrary>(PresetBuilder.LibraryPath);
            pso.ApplyModifiedPropertiesWithoutUndo();
            var radio = pilotGo.AddComponent<RcRadioSource>();
            radio.Provider = provider;
            var quad = BuildQuad(mats, assets, keyboardPad, spawn, quadLayer);
            var router = pilotGo.AddComponent<VehicleInputRouter>();
            var rso = new SerializedObject(router);
            rso.FindProperty("vehicle").objectReferenceValue = quad;
            var srcs = rso.FindProperty("sources");
            srcs.arraySize = 2;
            srcs.GetArrayElementAtIndex(0).objectReferenceValue = keyboardPad;
            srcs.GetArrayElementAtIndex(1).objectReferenceValue = radio;
            rso.ApplyModifiedPropertiesWithoutUndo();

            var ghostVisual = BuildGhostVisual(mats);
            var race = BuildRace(quad, spawn, ghostVisual);

            var guard = pilotGo.AddComponent<ControllerHotplugGuard>();
            var gso = new SerializedObject(guard);
            gso.FindProperty("router").objectReferenceValue = router;
            gso.FindProperty("race").objectReferenceValue = race;
            gso.ApplyModifiedPropertiesWithoutUndo();

            var fpv = quad.GetComponentInChildren<FpvCamera>();
            var views = BuildViews(quad, fpv, quadLayer, trackLayer, radio);
            var debugOverlay = BuildOverlay(quad, race, guard, views.Vtx, views.Manager);

            var controllerGo = new GameObject("Track Scene");
            var controller = controllerGo.AddComponent<TrackSceneController>();
            var cso = new SerializedObject(controller);
            cso.FindProperty("environment").stringValue = environment;
            cso.FindProperty("quads").objectReferenceValue = quadCatalog;
            cso.FindProperty("tracks").objectReferenceValue = trackCatalog;
            cso.FindProperty("trackPrefabs").objectReferenceValue = trackPrefabs;
            cso.FindProperty("vehicle").objectReferenceValue = quad;
            cso.FindProperty("race").objectReferenceValue = race;
            cso.FindProperty("quadVisual").objectReferenceValue = quad.transform.Find("Visual");
            cso.FindProperty("ghostVisual").objectReferenceValue = ghostVisual;
            cso.FindProperty("fpvCamera").objectReferenceValue = fpv;
            cso.FindProperty("views").objectReferenceValue = views.Manager;
            cso.FindProperty("editor").objectReferenceValue = BuildEditor(views.Rig, views.Camera, trackPrefabs, trackLayer);
            cso.FindProperty("keyboardGamepad").objectReferenceValue = keyboardPad;
            cso.FindProperty("radio").objectReferenceValue = radio;
            cso.FindProperty("radioProvider").objectReferenceValue = provider;
            cso.FindProperty("quadAudio").objectReferenceValue = quad.GetComponent<QuadAudio>();
            cso.FindProperty("vtx").objectReferenceValue = views.Vtx;
            cso.FindProperty("debugOverlay").objectReferenceValue = debugOverlay;
            cso.FindProperty("hud").objectReferenceValue = debugOverlay.transform.parent.gameObject;
            cso.ApplyModifiedPropertiesWithoutUndo();

            string path = ScenePath(environment);
            EditorSceneManager.SaveScene(scene, path);
            AddToBuildSettings(path);
            AssetDatabase.SaveAssets();
            return path;
        }

        public const string EditorUiLayerName = "EditorUI";

        /// <summary>
        /// Inactive track editor (enabled by TrackSceneController when a track is opened for editing). It moves the
        /// camera rig and turns the output camera itself (mouse + keyboard).
        /// </summary>
        private static GateRush.TrackEditor.TrackEditorController BuildEditor(Transform rig, Camera head, TrackPrefabs prefabs, int trackLayer)
        {
            int uiLayer = EnsureLayer(EditorUiLayerName, 11);

            var go = new GameObject("Track Editor");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = GateRush.EditorTools.Builders.Materials.Unlit("EditorPointer", new Color(0.4f, 0.9f, 1f), false);
            line.widthMultiplier = 0.006f;
            line.enabled = false;

            var editor = go.AddComponent<GateRush.TrackEditor.TrackEditorController>();
            var so = new SerializedObject(editor);
            so.FindProperty("prefabs").objectReferenceValue = prefabs;
            so.FindProperty("markerMaterial").objectReferenceValue = GateRush.EditorTools.Builders.Materials.Unlit("EditorMarker", Color.white, false);
            so.FindProperty("panelMaterial").objectReferenceValue = GateRush.EditorTools.Builders.Materials.Unlit("EditorPanel", Color.white, false);
            so.FindProperty("xrOrigin").objectReferenceValue = rig;
            so.FindProperty("head").objectReferenceValue = head;
            so.FindProperty("pointerLine").objectReferenceValue = line;
            so.FindProperty("pointerMask").intValue = 1 << trackLayer;
            so.FindProperty("editorUiLayer").intValue = uiLayer;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);
            return editor;
        }

        private static RaceSession BuildRace(QuadVehicle quad, Transform startPad, Transform ghostVisual)
        {
            var raceGo = new GameObject("Race");
            var ghosts = raceGo.AddComponent<GhostSystem>();
            var gso = new SerializedObject(ghosts);
            gso.FindProperty("ghostVisual").objectReferenceValue = ghostVisual;
            gso.ApplyModifiedPropertiesWithoutUndo();

            var session = raceGo.AddComponent<RaceSession>();
            var so = new SerializedObject(session);
            so.FindProperty("vehicle").objectReferenceValue = quad;
            so.FindProperty("ghosts").objectReferenceValue = ghosts;
            so.FindProperty("startPad").objectReferenceValue = startPad;
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

            // FPV camera mount: a Cinemachine camera the main camera follows in the FPV view. The pilot's own quad
            // (LocalQuad layer) is hidden in FPV by the view manager and visible in the chase/LOS views.
            var camGo = new GameObject("FPV Camera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.02f, 0.095f);
            var vcam = camGo.AddComponent<CinemachineCamera>();
            vcam.Lens = new LensSettings { FieldOfView = 80f, NearClipPlane = 0.02f, FarClipPlane = 1500f, Dutch = 0f };
            camGo.AddComponent<FpvCamera>();
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = quadLayer;

            var so = new SerializedObject(quad);
            so.FindProperty("profile").objectReferenceValue = assets.Quad;
            so.FindProperty("physics").objectReferenceValue = assets.Physics;
            so.FindProperty("input").objectReferenceValue = pilot;
            so.FindProperty("spawnPoint").objectReferenceValue = spawn;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Prop blur discs (fade in with RPM) on the same layer as the visuals.
            var discs = new Renderer[4];
            for (int i = 0; i < 4; i++)
            {
                var disc = Part(visual, PrimitiveType.Cylinder, $"Blur {i}", props[i].localPosition + new Vector3(0f, 0.002f, 0f),
                    new Vector3(0.13f, 0.0008f, 0.13f), Quaternion.identity, mats.PropBlur);
                disc.layer = quadLayer;
                discs[i] = disc.GetComponent<Renderer>();
            }

            var propVisuals = root.AddComponent<PropVisuals>();
            var pso = new SerializedObject(propVisuals);
            pso.FindProperty("vehicle").objectReferenceValue = quad;
            var arr = pso.FindProperty("props");
            arr.arraySize = 4;
            for (int i = 0; i < 4; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = props[i];
            var blur = pso.FindProperty("blurDiscs");
            blur.arraySize = 4;
            for (int i = 0; i < 4; i++) blur.GetArrayElementAtIndex(i).objectReferenceValue = discs[i];
            pso.ApplyModifiedPropertiesWithoutUndo();

            var audio = root.AddComponent<AudioSource>();
            audio.spatialBlend = 0f;
            var quadAudio = root.AddComponent<QuadAudio>();
            var aso = new SerializedObject(quadAudio);
            aso.FindProperty("vehicle").objectReferenceValue = quad;
            aso.ApplyModifiedPropertiesWithoutUndo();

            var fx = root.AddComponent<CrashEffects>();
            var fso = new SerializedObject(fx);
            fso.FindProperty("vehicle").objectReferenceValue = quad;
            fso.FindProperty("particleMaterial").objectReferenceValue = mats.Sparks;
            fso.ApplyModifiedPropertiesWithoutUndo();
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

        private struct ViewRefs
        {
            public ViewModeManager Manager;
            public Transform Rig;
            public Camera Camera;
            public VtxSignal Vtx;
        }

        public const string FpvLensProfilePath = DataDir + "/FpvLens.asset";

        /// <summary>
        /// Output camera with a Cinemachine brain, chase and line-of-sight Cinemachine cameras (the FPV one lives on the
        /// quad), the FPV lens-distortion volume, the LOS pilot stand, the VTX link model and the view manager + input.
        /// </summary>
        private static ViewRefs BuildViews(QuadVehicle quad, FpvCamera fpv, int quadLayer, int trackLayer, RcRadioSource radio)
        {
            var cam = BootSceneBuilder.CreateCamera(new Vector3(0f, 0f, -2f), 70f, out var rig);
            var brain = cam.gameObject.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;

            var viewsGo = new GameObject("Views");

            var chaseGo = new GameObject("Chase Camera");
            chaseGo.transform.SetParent(viewsGo.transform, false);
            var chase = chaseGo.AddComponent<CinemachineCamera>();
            chase.Lens = new LensSettings { FieldOfView = 70f, NearClipPlane = 0.05f, FarClipPlane = 1500f };
            chase.Follow = quad.transform;
            chase.LookAt = quad.transform;
            var follow = chaseGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 0.6f, -1.6f);
            var tracker = follow.TrackerSettings;
            tracker.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LockToTargetWithWorldUp;
            tracker.PositionDamping = new Vector3(0.3f, 0.3f, 0.3f);
            tracker.RotationDamping = new Vector3(0f, 0.4f, 0f);
            follow.TrackerSettings = tracker;
            var chaseAim = chaseGo.AddComponent<CinemachineRotationComposer>();
            chaseAim.Damping = new Vector2(0.15f, 0.15f);
            chase.enabled = false;

            var stand = new GameObject("LOS Stand").transform;
            stand.position = new Vector3(-4f, 0f, -6f);
            stand.rotation = Quaternion.LookRotation(new Vector3(4f, 0f, 14f));
            var losGo = new GameObject("LOS Camera");
            losGo.transform.SetParent(stand, false);
            losGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var los = losGo.AddComponent<CinemachineCamera>();
            los.Lens = new LensSettings { FieldOfView = 50f, NearClipPlane = 0.05f, FarClipPlane = 1500f };
            los.LookAt = quad.transform;
            var losAim = losGo.AddComponent<CinemachineRotationComposer>();
            losAim.Damping = new Vector2(0.1f, 0.1f);
            los.enabled = false;

            var lensGo = new GameObject("FPV Lens");
            lensGo.transform.SetParent(viewsGo.transform, false);
            var lens = lensGo.AddComponent<UnityEngine.Rendering.Volume>();
            lens.isGlobal = true;
            lens.priority = 100f;
            lens.weight = 0f;
            lens.sharedProfile = EnsureLensProfile();

            var views = viewsGo.AddComponent<ViewModeManager>();
            var vso = new SerializedObject(views);
            vso.FindProperty("vehicle").objectReferenceValue = quad;
            vso.FindProperty("outputCamera").objectReferenceValue = cam;
            vso.FindProperty("fpv").objectReferenceValue = fpv;
            vso.FindProperty("chase").objectReferenceValue = chase;
            vso.FindProperty("lineOfSight").objectReferenceValue = los;
            vso.FindProperty("losStand").objectReferenceValue = stand;
            vso.FindProperty("localQuadLayer").intValue = 1 << quadLayer;
            vso.FindProperty("lensVolume").objectReferenceValue = lens;
            vso.ApplyModifiedPropertiesWithoutUndo();

            var input = viewsGo.AddComponent<ViewModeInput>();
            var iso = new SerializedObject(input);
            iso.FindProperty("views").objectReferenceValue = views;
            iso.FindProperty("radio").objectReferenceValue = radio;
            iso.ApplyModifiedPropertiesWithoutUndo();

            // Analog video link: pilot (LOS stand) → quad; drives the OSD link quality.
            var vtx = viewsGo.AddComponent<VtxSignal>();
            var xso = new SerializedObject(vtx);
            xso.FindProperty("vehicle").objectReferenceValue = quad;
            xso.FindProperty("receiver").objectReferenceValue = stand;
            xso.FindProperty("obstacles").intValue = 1 << trackLayer;
            xso.ApplyModifiedPropertiesWithoutUndo();

            return new ViewRefs { Manager = views, Rig = rig, Camera = cam, Vtx = vtx };
        }

        /// <summary>Volume profile with the FPV wide-angle (barrel) lens distortion.</summary>
        private static UnityEngine.Rendering.VolumeProfile EnsureLensProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(FpvLensProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(profile, FpvLensProfilePath);
            }
            if (!profile.TryGet(out UnityEngine.Rendering.Universal.LensDistortion distortion))
            {
                distortion = profile.Add<UnityEngine.Rendering.Universal.LensDistortion>(true);
                distortion.name = "LensDistortion";
                AssetDatabase.AddObjectToAsset(distortion, profile);
            }
            distortion.intensity.Override(-0.35f);
            distortion.scale.Override(1.06f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Screen-space HUD (every view): Betaflight-style OSD, next-gate indicator, race HUD, hot-plug overlay and the
        /// optional debug readout. Returns the debug container.
        /// </summary>
        private static GameObject BuildOverlay(QuadVehicle quad, RaceSession race, ControllerHotplugGuard guard, VtxSignal vtx, ViewModeManager views)
        {
            var canvasGo = new GameObject("OSD", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Developer readout lives in its own full-screen container so settings can toggle it.
            var debugGo = new GameObject("Debug Readout", typeof(RectTransform));
            debugGo.transform.SetParent(canvasGo.transform, false);
            var drt = (RectTransform)debugGo.transform;
            drt.anchorMin = Vector2.zero;
            drt.anchorMax = Vector2.one;
            drt.offsetMin = drt.offsetMax = Vector2.zero;
            drt.anchoredPosition = new Vector2(0f, -150f);

            var overlay = debugGo.AddComponent<FlightDebugOverlay>();
            var status = Label(debugGo.transform, "Status", 0, 22);
            var sticks = Label(debugGo.transform, "Sticks", 1, 20);
            var rates = Label(debugGo.transform, "Rates", 2, 20);
            var motion = Label(debugGo.transform, "Motion", 3, 20);

            var so = new SerializedObject(overlay);
            so.FindProperty("vehicle").objectReferenceValue = quad;
            so.FindProperty("status").objectReferenceValue = status;
            so.FindProperty("sticks").objectReferenceValue = sticks;
            so.FindProperty("rates").objectReferenceValue = rates;
            so.FindProperty("motion").objectReferenceValue = motion;
            var bars = so.FindProperty("motorBars");
            bars.arraySize = 4;
            string[] names = { "RR", "FR", "RL", "FL" };
            for (int i = 0; i < 4; i++) bars.GetArrayElementAtIndex(i).objectReferenceValue = MotorBar(debugGo.transform, i, names[i]);
            so.ApplyModifiedPropertiesWithoutUndo();
            debugGo.SetActive(false);

            BuildOsd(canvasGo.transform, quad, vtx, views);
            BuildNextGate(canvasGo.transform, race, quad);
            BuildRaceHud(canvasGo.transform, race);
            BuildHotplugOverlay(canvasGo.transform, guard);
            return debugGo;
        }

        /// <summary>Betaflight-style OSD elements around the edges of the feed, crosshair and artificial horizon in the middle.</summary>
        private static void BuildOsd(Transform canvas, QuadVehicle quad, VtxSignal vtx, ViewModeManager views)
        {
            TextMeshProUGUI Osd(string name, Vector2 anchor, Vector2 pos, float size, TextAlignmentOptions align)
            {
                var t = Label(canvas, name, 0, size);
                var rt = (RectTransform)t.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
                rt.anchoredPosition = pos;
                rt.sizeDelta = new Vector2(600f, 90f);
                t.alignment = align;
                t.fontStyle = FontStyles.Bold;
                t.text = "";
                return t;
            }

            var battery = Osd("OSD Battery", new Vector2(0f, 1f), new Vector2(40f, -30f), 30, TextAlignmentOptions.TopLeft);
            var link = Osd("OSD Link", new Vector2(0f, 1f), new Vector2(40f, -110f), 26, TextAlignmentOptions.TopLeft);
            var mode = Osd("OSD Mode", new Vector2(0.5f, 1f), new Vector2(0f, -30f), 32, TextAlignmentOptions.Top);
            var timer = Osd("OSD Timer", new Vector2(0.5f, 0f), new Vector2(0f, 40f), 34, TextAlignmentOptions.Bottom);
            var throttle = Osd("OSD Throttle", new Vector2(0f, 0f), new Vector2(40f, 40f), 28, TextAlignmentOptions.BottomLeft);
            var motion = Osd("OSD Motion", new Vector2(1f, 0f), new Vector2(-40f, 40f), 28, TextAlignmentOptions.BottomRight);
            var warning = Osd("OSD Warning", new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), 40, TextAlignmentOptions.Center);
            warning.color = new Color(1f, 0.35f, 0.25f);

            var cross = Osd("OSD Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, 44, TextAlignmentOptions.Center);
            cross.text = "-+-";

            var horizonGo = new GameObject("OSD Horizon", typeof(RectTransform), typeof(Image));
            horizonGo.transform.SetParent(canvas, false);
            var hrt = (RectTransform)horizonGo.transform;
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(360f, 3f);
            var img = horizonGo.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.8f);
            img.raycastTarget = false;

            var osd = canvas.gameObject.AddComponent<OsdHud>();
            var so = new SerializedObject(osd);
            so.FindProperty("vehicle").objectReferenceValue = quad;
            so.FindProperty("vtx").objectReferenceValue = vtx;
            so.FindProperty("battery").objectReferenceValue = battery;
            so.FindProperty("mode").objectReferenceValue = mode;
            so.FindProperty("timer").objectReferenceValue = timer;
            so.FindProperty("throttle").objectReferenceValue = throttle;
            so.FindProperty("motion").objectReferenceValue = motion;
            so.FindProperty("link").objectReferenceValue = link;
            so.FindProperty("warning").objectReferenceValue = warning;
            so.FindProperty("horizon").objectReferenceValue = hrt;
            so.FindProperty("crosshair").objectReferenceValue = cross.gameObject;
            so.FindProperty("views").objectReferenceValue = views;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Next-gate bracket (on screen) / edge arrow (off screen) with the distance.</summary>
        private static void BuildNextGate(Transform canvas, RaceSession race, QuadVehicle quad)
        {
            var root = new GameObject("Next Gate", typeof(RectTransform));
            root.transform.SetParent(canvas, false);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;

            RectTransform Glyph(string name, string text, float size)
            {
                var t = Label(root.transform, name, 0, size);
                var rt = (RectTransform)t.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(160f, 90f);
                t.alignment = TextAlignmentOptions.Center;
                t.fontStyle = FontStyles.Bold;
                t.color = new Color(0.35f, 1f, 0.45f);
                t.text = text;
                return rt;
            }

            var marker = Glyph("Gate Marker", "[  ]", 54);
            var arrow = Glyph("Gate Arrow", "^", 64);
            var distance = Glyph("Gate Distance", "", 26).GetComponent<TextMeshProUGUI>();
            distance.color = Color.white;

            var indicator = root.AddComponent<NextGateIndicator>();
            var so = new SerializedObject(indicator);
            so.FindProperty("race").objectReferenceValue = race;
            so.FindProperty("vehicle").objectReferenceValue = quad;
            so.FindProperty("marker").objectReferenceValue = marker;
            so.FindProperty("arrow").objectReferenceValue = arrow;
            so.FindProperty("distance").objectReferenceValue = distance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildHotplugOverlay(Transform canvas, ControllerHotplugGuard guard)
        {
            var panel = new GameObject("Hotplug Pause", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvas, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1100f, 300f);
            panel.GetComponent<Image>().color = new Color(0.3f, 0.05f, 0.05f, 0.75f);
            var text = Label(panel.transform, "Hotplug Text", 0, 40);
            var trt = (RectTransform)text.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(30f, 20f);
            trt.offsetMax = new Vector2(-30f, -20f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            panel.SetActive(false);

            var overlay = canvas.gameObject.AddComponent<HotplugOverlay>();
            var so = new SerializedObject(overlay);
            so.FindProperty("guard").objectReferenceValue = guard;
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("text").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
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
            public Material Ground, Pillar, Accent, Frame, PropFront, PropRear, Ghost, GateFrame, PropBlur, Sparks;
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
                PropBlur = UnlitMat("PropBlur", new Color(0.15f, 0.15f, 0.15f, 0.45f), true),
                Sparks = SparksMaterial(),
                Ghost = UnlitMat("Ghost", new Color(0.3f, 0.85f, 1f, 0.35f), true),
                // Lit, colour driven per gate state by Gate.SetVisualState.
                GateFrame = Mat("GateFrame", new Color(0.55f, 0.55f, 0.6f)),
            };
        }

        /// <summary>Additive URP particle material (vertex colours drive the spark colour/fade).</summary>
        private static Material SparksMaterial()
        {
            string path = $"{MatDir}/Sparks.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f); // additive
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(m);
            return m;
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
