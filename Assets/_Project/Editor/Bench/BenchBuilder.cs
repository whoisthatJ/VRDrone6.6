using System.Collections.Generic;
using System.IO;
using Ushqyr.Bench;
using Ushqyr.EditorTools.Art;
using Ushqyr.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ushqyr.EditorTools.Bench
{
    /// <summary>
    /// Generates everything AI Bench needs that isn't code: the standard gate prefab, the hall scene and the public track
    /// files. Repeatable: run <i>Ushqyr > Bench > Build Everything</i> again after changing any number here.
    /// </summary>
    public static class BenchBuilder
    {
        public const string Dir = "Assets/_Project/Bench";
        public const string GatePrefabPath = Dir + "/Prefabs/Gate_Standard_250.prefab";
        public const string LedMaterialPath = Dir + "/Materials/M_Bench_LED.mat";
        public const string FrameMaterialPath = Dir + "/Materials/M_Bench_GateFrame.mat";
        public const string HallScenePath = "Assets/_Project/Scenes/Bench_Hall.unity";
        public const string TracksDir = "Assets/StreamingAssets/Bench/Tracks";
        public const string RulesPath = "Assets/StreamingAssets/Bench/rules.json";

        /// <summary>Hall v1: inner size in metres.</summary>
        public const float HallLength = 40f, HallWidth = 20f, HallHeight = 8f;

        [MenuItem("Ushqyr/Bench/Build Everything")]
        public static string BuildAll()
        {
            string gate = BuildGatePrefab();
            string tracks = WriteTracks();
            string scene = BuildHallScene();
            return $"{gate}\n{tracks}\n{scene}";
        }

        // ---------- gate ----------

        [MenuItem("Ushqyr/Bench/Build Gate Prefab")]
        public static string BuildGatePrefab()
        {
            Directory.CreateDirectory(Dir + "/Prefabs");
            Directory.CreateDirectory(Dir + "/Materials");
            int trackLayer = LayerMask.NameToLayer(Builders.TrackSceneBuilder.TrackLayerName);

            Material frameMat = LitMaterial(FrameMaterialPath, new Color(0.035f, 0.036f, 0.04f), 0.25f, Color.black);
            Material ledMat = LitMaterial(LedMaterialPath, Color.white, 0.1f, Color.white);

            const float outer = GateGeometry.Outer, open = GateGeometry.Opening, bar = GateGeometry.Bar, depth = GateGeometry.Depth;
            var root = new GameObject("Gate_Standard_250");
            var frames = new List<Renderer>();
            var leds = new List<Renderer>();

            GameObject Bar(string name, Vector3 centre, Vector3 size)
            {
                var go = Part(root.transform, name, centre, size, frameMat, "Bench_Gate_" + name.Replace(" ", ""));
                go.layer = trackLayer;
                go.AddComponent<BoxCollider>();
                frames.Add(go.GetComponent<Renderer>());
                return go;
            }

            Bar("Bottom", new Vector3(0f, bar * 0.5f, 0f), new Vector3(outer, bar, depth));
            Bar("Top", new Vector3(0f, outer - bar * 0.5f, 0f), new Vector3(outer, bar, depth));
            Bar("Left", new Vector3(-(open + bar) * 0.5f, outer * 0.5f, 0f), new Vector3(bar, open, depth));
            Bar("Right", new Vector3((open + bar) * 0.5f, outer * 0.5f, 0f), new Vector3(bar, open, depth));

            // LED strip: a band around the opening on both faces, and the inner faces of the opening.
            const float band = 0.10f, proud = 0.004f;
            float c = GateGeometry.CentreHeight, half = open * 0.5f;
            int n = 0;
            void Led(Vector3 centre, Vector3 size)
            {
                var go = Part(root.transform, "LED " + n, centre, size, ledMat, "Bench_GateLed_" + n);
                n++;
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                leds.Add(r);
            }
            foreach (float face in new[] { -1f, 1f })
            {
                float z = face * (depth * 0.5f + proud * 0.5f);
                Led(new Vector3(0f, c + half + band * 0.5f, z), new Vector3(open + 2f * band, band, proud));
                Led(new Vector3(0f, c - half - band * 0.5f, z), new Vector3(open + 2f * band, band, proud));
                Led(new Vector3(-half - band * 0.5f, c, z), new Vector3(band, open, proud));
                Led(new Vector3(half + band * 0.5f, c, z), new Vector3(band, open, proud));
            }
            Led(new Vector3(0f, c + half - proud * 0.5f, 0f), new Vector3(open, proud, depth));
            Led(new Vector3(0f, c - half + proud * 0.5f, 0f), new Vector3(open, proud, depth));
            Led(new Vector3(-half + proud * 0.5f, c, 0f), new Vector3(proud, open - 2f * proud, depth));
            Led(new Vector3(half - proud * 0.5f, c, 0f), new Vector3(proud, open - 2f * proud, depth));

            root.AddComponent<BenchGate>().Configure(leds.ToArray(), frames.ToArray());
            PrefabUtility.SaveAsPrefabAsset(root, GatePrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return GatePrefabPath;
        }

        private static GameObject Part(Transform parent, string name, Vector3 centre, Vector3 size, Material mat, string meshName)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = ArtPass.MeshAsset(meshName, () => ProcMesh.Box(size, default, false, meshName));
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static Material LitMaterial(string path, Color baseColor, float smoothness, Color emission)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            if (emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", emission);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- tracks ----------

        [MenuItem("Ushqyr/Bench/Write Public Tracks")]
        public static string WriteTracks()
        {
            Directory.CreateDirectory(TracksDir);
            var written = new List<string>();
            foreach (BenchTrack t in new[] { H1Oval() })
            {
                string path = $"{TracksDir}/{t.id}{BenchLauncher.TrackExtension}";
                File.WriteAllText(path, t.ToJson() + "\n");
                written.Add(path);
            }
            if (!File.Exists(RulesPath)) File.WriteAllText(RulesPath, JsonUtility.ToJson(new BenchRules(), true) + "\n");
            AssetDatabase.Refresh();
            return string.Join(", ", written);
        }

        /// <summary>
        /// The demo track: six gates on a flat oval (9 x 7 m semi-axes), flown anticlockwise. Every gate faces along the
        /// oval, so from each gate the next one is ahead and to the left, well inside the camera's view.
        /// </summary>
        public static BenchTrack H1Oval()
        {
            const float a = 9f, b = 7f;
            const string colour = "#FF00C8";
            var t = new BenchTrack
            {
                id = "H1_Oval", name = "Hall Oval", author = "Ushqyr", difficulty = "easy", venue = "hall", lighting = "default",
                rules = new TrackRules { repeat_window = 4, time_limit = 120f },
            };
            for (int i = 0; i < 6; i++)
            {
                float th = (-60f + 60f * i) * Mathf.Deg2Rad;
                var pos = new Vector3(a * Mathf.Cos(th), 0f, b * Mathf.Sin(th));
                var tangent = new Vector3(-a * Mathf.Sin(th), 0f, b * Mathf.Cos(th));
                t.gates.Add(new TrackGate
                {
                    id = "G" + (i + 1),
                    position = new[] { Round(pos.x), 0f, Round(pos.z) },
                    yaw = Round(Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg),
                    led = new GateLed { color = colour, brightness = 1f, mode = "solid" },
                    direction = "any",
                });
            }
            // The pad: on the oval a quarter of the way before the first gate, looking at it.
            var start = new Vector3(0f, 0f, -b);
            Vector3 toFirst = BenchTrack.V(t.gates[0].position) - start;
            t.start = new TrackStart { position = new[] { start.x, 0f, start.z }, yaw = Round(Mathf.Atan2(toFirst.x, toFirst.z) * Mathf.Rad2Deg) };
            return t;
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;

        // ---------- hall ----------

        [MenuItem("Ushqyr/Bench/Build Hall Scene")]
        public static string BuildHallScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int trackLayer = LayerMask.NameToLayer(Builders.TrackSceneBuilder.TrackLayerName);
            int quadLayer = LayerMask.NameToLayer(Builders.TrackSceneBuilder.LocalQuadLayerName);
            var gatePrefab = AssetDatabase.LoadAssetAtPath<BenchGate>(GatePrefabPath);

            var hall = new GameObject("Hall").transform;
            BuildHall(hall, trackLayer);
            BuildLighting();

            // Camera for the window (spectators); the program's camera is created on the drone at runtime.
            var camGo = new GameObject("Spectator Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 74f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.025f);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 12f, -9.4f), Quaternion.Euler(48f, 0f, 0f));
            var spectator = camGo.AddComponent<BenchSpectatorCamera>();

            var volumeGo = new GameObject("Post Volume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Project/Art/Volumes/VP_Hangar.asset");

            var spawn = new GameObject("Start Pad").transform;
            spawn.position = new Vector3(0f, 0.05f, -7f);
            var pad = Box(spawn, "Pad", new Vector3(0f, -0.042f, 0f), new Vector3(0.6f, 0.01f, 0.6f), "M_Kit_LandingPad", false);
            pad.transform.localPosition = new Vector3(0f, -0.042f, 0f);

            var gates = new GameObject("Gates").transform;

            var drone = new GameObject("Drone");
            drone.transform.position = spawn.position;
            var quad = drone.AddComponent<QuadVehicle>();
            var qso = new SerializedObject(quad);
            qso.FindProperty("physics").objectReferenceValue = AssetDatabase.LoadAssetAtPath<PhysicsSettingsAsset>("Assets/_Project/Data/PhysicsSettings.asset");
            qso.FindProperty("spawnPoint").objectReferenceValue = spawn;
            qso.ApplyModifiedPropertiesWithoutUndo();
            var props = drone.AddComponent<PropVisuals>();
            var pso = new SerializedObject(props);
            pso.FindProperty("vehicle").objectReferenceValue = quad;
            pso.ApplyModifiedPropertiesWithoutUndo();
            var audio = drone.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            audio.minDistance = 1.5f;
            audio.maxDistance = 40f;
            var quadAudio = drone.AddComponent<QuadAudio>();
            var aso = new SerializedObject(quadAudio);
            aso.FindProperty("vehicle").objectReferenceValue = quad;
            aso.FindProperty("maxRpm").floatValue = 48000f;
            aso.FindProperty("blades").intValue = 4;
            aso.FindProperty("volume").floatValue = 0.35f;
            aso.ApplyModifiedPropertiesWithoutUndo();

            var benchGo = new GameObject("Bench");
            var session = benchGo.AddComponent<BenchSession>();
            var sso = new SerializedObject(session);
            sso.FindProperty("quad").objectReferenceValue = quad;
            sso.FindProperty("spawn").objectReferenceValue = spawn;
            sso.FindProperty("gatesRoot").objectReferenceValue = gates;
            sso.FindProperty("gatePrefab").objectReferenceValue = gatePrefab;
            sso.FindProperty("solidLayers").intValue = 1 << trackLayer;
            sso.FindProperty("quadLayer").intValue = quadLayer;
            sso.FindProperty("defaultTrack").stringValue = "H1_Oval";
            Builders.GarageSceneBuilder.AssignModelMaterials(sso.FindProperty("modelMaterials"));
            sso.ApplyModifiedPropertiesWithoutUndo();
            // Trackside video: four positions along the long walls and one high overview.
            var filmGo = new GameObject("Trackside Camera");
            var film = filmGo.AddComponent<BenchTracksideCamera>();
            float fx = HallLength * 0.5f - 1.2f, fz = HallWidth * 0.5f - 0.8f;
            var filmPositions = new[]
            {
                new Vector3(-fx * 0.55f, 2.6f, -fz), new Vector3(fx * 0.55f, 2.6f, -fz),
                new Vector3(-fx * 0.55f, 2.6f, fz), new Vector3(fx * 0.55f, 2.6f, fz),
                new Vector3(fx, 3.2f, 0f), new Vector3(-fx, 3.2f, 0f),
            };
            film.SetPositions(filmPositions);
            EditorUtility.SetDirty(film);
            var fso = new SerializedObject(session);
            fso.FindProperty("trackside").objectReferenceValue = film;
            fso.ApplyModifiedPropertiesWithoutUndo();

            benchGo.AddComponent<BenchHud>().Configure(session);
            EditorUtility.SetDirty(benchGo.GetComponent<BenchHud>());
            var hso = new SerializedObject(benchGo.GetComponent<BenchHud>());
            hso.FindProperty("session").objectReferenceValue = session;
            hso.ApplyModifiedPropertiesWithoutUndo();

            var cso = new SerializedObject(spectator);
            cso.FindProperty("target").objectReferenceValue = drone.transform;
            cso.FindProperty("overviewPosition").vector3Value = new Vector3(0f, HallHeight - 0.9f, -HallWidth * 0.5f + 0.4f);
            cso.FindProperty("overviewLookAt").vector3Value = new Vector3(0f, 0f, -1.5f);
            var spots = cso.FindProperty("tracksidePositions");
            spots.arraySize = filmPositions.Length;
            for (int i = 0; i < filmPositions.Length; i++) spots.GetArrayElementAtIndex(i).vector3Value = filmPositions[i];
            cso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, HallScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == HallScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(HallScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            return HallScenePath;
        }

        private static void BuildHall(Transform hall, int layer)
        {
            const float L = HallLength, W = HallWidth, H = HallHeight, t = 0.3f;
            GameObject Solid(string name, Vector3 centre, Vector3 size, string mat, bool shadows)
            {
                var go = Box(hall, name, centre, size, mat, shadows);
                go.layer = layer;
                go.AddComponent<BoxCollider>();
                go.isStatic = true;
                return go;
            }

            Solid("Floor", new Vector3(0f, -t * 0.5f, 0f), new Vector3(L, t, W), "M_Concrete_PaintedFloor", false);
            Solid("Ceiling", new Vector3(0f, H + t * 0.5f, 0f), new Vector3(L, t, W), "M_Steel_CorrugatedDark", false);
            Solid("Wall North", new Vector3(0f, H * 0.5f, W * 0.5f + t * 0.5f), new Vector3(L + 2f * t, H, t), "M_Concrete_Wall", false);
            Solid("Wall South", new Vector3(0f, H * 0.5f, -W * 0.5f - t * 0.5f), new Vector3(L + 2f * t, H, t), "M_Concrete_Wall", false);
            Solid("Wall East", new Vector3(L * 0.5f + t * 0.5f, H * 0.5f, 0f), new Vector3(t, H, W), "M_Concrete_Wall", false);
            Solid("Wall West", new Vector3(-L * 0.5f - t * 0.5f, H * 0.5f, 0f), new Vector3(t, H, W), "M_Concrete_Wall", false);

            // Painted wainscot and floor markings: something for the eye (and for optical flow) on plain surfaces.
            const float e = 0.012f;
            Box(hall, "Wainscot North", new Vector3(0f, 0.6f, W * 0.5f - e), new Vector3(L, 1.2f, e), "M_Wood_Plywood", false);
            Box(hall, "Wainscot South", new Vector3(0f, 0.6f, -W * 0.5f + e), new Vector3(L, 1.2f, e), "M_Wood_Plywood", false);
            Box(hall, "Wainscot East", new Vector3(L * 0.5f - e, 0.6f, 0f), new Vector3(e, 1.2f, W), "M_Wood_Plywood", false);
            Box(hall, "Wainscot West", new Vector3(-L * 0.5f + e, 0.6f, 0f), new Vector3(e, 1.2f, W), "M_Wood_Plywood", false);
            float inset = 1.5f, line = 0.08f;
            Box(hall, "Line North", new Vector3(0f, 0.003f, W * 0.5f - inset), new Vector3(L - 2f * inset, 0.006f, line), "M_Stadium_LinePaint", false);
            Box(hall, "Line South", new Vector3(0f, 0.003f, -W * 0.5f + inset), new Vector3(L - 2f * inset, 0.006f, line), "M_Stadium_LinePaint", false);
            Box(hall, "Line East", new Vector3(L * 0.5f - inset, 0.003f, 0f), new Vector3(line, 0.006f, W - 2f * inset), "M_Stadium_LinePaint", false);
            Box(hall, "Line West", new Vector3(-L * 0.5f + inset, 0.003f, 0f), new Vector3(line, 0.006f, W - 2f * inset), "M_Stadium_LinePaint", false);
            Box(hall, "Line Centre", new Vector3(0f, 0.003f, 0f), new Vector3(line, 0.006f, W - 2f * inset), "M_Stadium_LinePaint", false);

            // Roof beams and rows of lamps.
            for (int i = -3; i <= 3; i++)
            {
                float x = i * (L / 8f);
                Box(hall, "Beam " + (i + 3), new Vector3(x, H - 0.35f, 0f), new Vector3(0.3f, 0.7f, W), "M_Steel_Truss", false);
            }
            int lamp = 0;
            for (int ix = -3; ix <= 4; ix++)
                for (int iz = -1; iz <= 1; iz++)
                {
                    var p = new Vector3((ix - 0.5f) * (L / 8f), H - 0.75f, iz * (W / 3f));
                    Box(hall, "Lamp " + lamp++, p, new Vector3(1.6f, 0.08f, 0.35f), "M_Lamp_Emitter", false);
                }
        }

        private static void BuildLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.8f, 0.8f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.72f, 0.72f);
            RenderSettings.ambientGroundColor = new Color(0.5f, 0.49f, 0.47f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0.4f;

            // The hall's lamps as one soft light from above; the walls and the roof don't cast shadows, the gates do.
            var go = new GameObject("Hall Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.55f;
            go.transform.rotation = Quaternion.Euler(72f, 25f, 0f);

            // Light bounced off the floor: it is what makes the roof and the undersides visible at all.
            var bounce = new GameObject("Floor Bounce").AddComponent<Light>();
            bounce.type = LightType.Directional;
            bounce.color = new Color(0.9f, 0.88f, 0.84f);
            bounce.intensity = 0.45f;
            bounce.shadows = LightShadows.None;
            bounce.transform.rotation = Quaternion.Euler(-80f, -40f, 0f);
        }

        private static GameObject Box(Transform parent, string name, Vector3 centre, Vector3 size, string material, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = ProcMesh.Box(size, centre, false, "Bench " + name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.Get(material);
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }
}
