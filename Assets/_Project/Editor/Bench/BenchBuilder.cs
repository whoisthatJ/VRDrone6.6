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
        public const string ParkingScenePath = "Assets/_Project/Scenes/Bench_Parking.unity";
        public static string ScenePath(string venue) => venue == "parking" ? ParkingScenePath : HallScenePath;
        public const string TracksDir = "Assets/StreamingAssets/Bench/Tracks";
        public const string RulesPath = "Assets/StreamingAssets/Bench/rules.json";

        /// <summary>Hall v1: inner size in metres.</summary>
        public const float HallL = 40f, HallW = 20f, HallH = 8f;

        [MenuItem("Ushqyr/Bench/Build Everything")]
        public static string BuildAll()
        {
            string gate = BuildGatePrefab();
            string tracks = WriteTracks();
            string scene = BuildHallScene() + ", " + BuildParkingScene();
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
            foreach (BenchTrack t in PublicTracks())
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

        public static BenchTrack[] PublicTracks() => new[] { H1Oval(), H2Eight(), H3Slalom(), X1Mixed(), P1Columns(), P2Maze() };

        private static TrackGate G(int n, float x, float z, float yaw, string colour = "#FF00C8") => new TrackGate
        {
            id = "G" + n, position = new[] { Round(x), 0f, Round(z) }, yaw = Round(yaw),
            led = new GateLed { color = colour, brightness = 1f, mode = "solid" }, direction = "any",
        };

        private static BenchTrack Track(string id, string name, string difficulty, string venue, float startX, float startZ, float startYaw) => new BenchTrack
        {
            id = id, name = name, author = "Ushqyr", difficulty = difficulty, venue = venue, lighting = "default",
            rules = new TrackRules { repeat_window = 4, time_limit = 120f },
            start = new TrackStart { position = new[] { startX, 0f, startZ }, yaw = startYaw },
        };

        /// <summary>Figure-eight (a lemniscate, 15 m half-length) with the crossing between gates.</summary>
        public static BenchTrack H2Eight()
        {
            const float a = 15f;
            Vector3 P(float deg)
            {
                float t = deg * Mathf.Deg2Rad, d = 1f + Mathf.Sin(t) * Mathf.Sin(t);
                return new Vector3(a * Mathf.Cos(t) / d, 0f, a * Mathf.Sin(t) * Mathf.Cos(t) / d);
            }
            var t8 = Track("H2_Eight", "Hall Eight", "medium", "hall", 0f, 0f, 0f);
            for (int i = 0; i < 8; i++)
            {
                float deg = 22.5f + 45f * i;
                Vector3 p = P(deg), tangent = P(deg + 1f) - P(deg - 1f);
                t8.gates.Add(G(i + 1, p.x, p.z, Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg));
            }
            // The pad: on the curve a little before the first gate, looking along it.
            Vector3 s = P(-12f), look = P(-10f) - P(-14f);
            t8.start = new TrackStart { position = new[] { Round(s.x), 0f, Round(s.z) }, yaw = Round(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg) };
            return t8;
        }

        /// <summary>Offset slalom eastwards, a 180° hairpin at the end, and two gates back.</summary>
        public static BenchTrack H3Slalom()
        {
            var t = Track("H3_Slalom", "Hall Slalom", "medium", "hall", -17f, -5f, 90f);
            t.gates.Add(G(1, -12f, -5f, 90f));
            t.gates.Add(G(2, -6f, -2f, 90f));
            t.gates.Add(G(3, 0f, -6f, 90f));
            t.gates.Add(G(4, 6f, -2.5f, 90f));
            t.gates.Add(G(5, 13f, 1.5f, 0f));
            t.gates.Add(G(6, 6f, 6f, -90f));
            t.gates.Add(G(7, -8f, 5.5f, -90f));
            return t;
        }

        /// <summary>Ten gates in five LED colours, with painted and glowing panels in the same colours around the hall.</summary>
        public static BenchTrack X1Mixed()
        {
            const float a = 14f, b = 6.6f;
            var t = Track("X1_Mixed", "Hall Mixed", "hard", "hall", 0f, -6.6f, 90f);
            for (int i = 0; i < 10; i++)
            {
                float th = (-72f + 36f * i) * Mathf.Deg2Rad, r = i % 2 == 0 ? 1f : 0.78f;
                var pos = new Vector3(a * r * Mathf.Cos(th), 0f, b * r * Mathf.Sin(th));
                var tangent = new Vector3(-a * Mathf.Sin(th), 0f, b * Mathf.Cos(th));
                t.gates.Add(G(i + 1, pos.x, pos.z, Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg, TrackGenerator.Colours[i % TrackGenerator.Colours.Length]));
            }
            Vector3 first = BenchTrack.V(t.gates[0].position), start = BenchTrack.V(t.start.position);
            t.start.yaw = Round(Mathf.Atan2(first.x - start.x, first.z - start.z) * Mathf.Rad2Deg);
            // Distractors: on the walls (thin panels) and two boxes on the floor in the middle.
            float wz = HallW * 0.5f - 0.06f, wx = HallL * 0.5f - 0.06f;
            TrackProp Panel(string id, float x, float z, float yaw, float w, float h, string colour, bool glow, float lift = 2.2f) => new TrackProp
            {
                id = id, position = new[] { x, lift, z }, yaw = yaw, size = new[] { w, h, 0.05f }, color = colour, glow = glow,
            };
            t.props.Add(Panel("W1", -12f, wz, 0f, 2.4f, 1.6f, "#FF00C8", false));
            t.props.Add(Panel("W2", 3f, wz, 0f, 1.8f, 1.8f, "#00E5FF", true));
            t.props.Add(Panel("W3", 14f, -wz, 0f, 2.6f, 1.2f, "#FFD400", false));
            t.props.Add(Panel("W4", -5f, -wz, 0f, 2f, 2f, "#3CFF4E", true));
            t.props.Add(Panel("W5", wx, 3f, 90f, 2.2f, 1.5f, "#FF5A1F", false));
            t.props.Add(Panel("W6", -wx, -3f, 90f, 2.2f, 1.5f, "#FF00C8", true));
            t.props.Add(new TrackProp { id = "B1", position = new[] { -2.5f, 0f, 0f }, yaw = 20f, size = new[] { 0.8f, 0.8f, 0.8f }, color = "#FFD400", glow = false });
            t.props.Add(new TrackProp { id = "B2", position = new[] { 3f, 0f, 0.8f }, yaw = -35f, size = new[] { 0.7f, 1.1f, 0.7f }, color = "#00E5FF", glow = false });
            return t;
        }

        /// <summary>A loop round the middle column rows of the parking, in its low light.</summary>
        public static BenchTrack P1Columns()
        {
            var t = Track("P1_Columns", "Parking Columns", "easy", "parking", -19f, -8f, 90f);
            t.gates.Add(G(1, -12f, -8f, 90f));
            t.gates.Add(G(2, 4f, -8f, 90f));
            t.gates.Add(G(3, 20f, 0f, 0f));
            t.gates.Add(G(4, 4f, 8f, -90f));
            t.gates.Add(G(5, -12f, 8f, -90f));
            t.gates.Add(G(6, -20f, 0f, 180f));
            return t;
        }

        /// <summary>Nine gates weaving between the column rows: after most gates the next one is round a corner.</summary>
        public static BenchTrack P2Maze()
        {
            var t = Track("P2_Maze", "Parking Maze", "hard", "parking", -26f, -8f, 90f);
            t.gates.Add(G(1, -20f, -8f, 90f));
            t.gates.Add(G(2, -12f, 0f, 0f));
            t.gates.Add(G(3, -4f, 8f, 90f));
            t.gates.Add(G(4, 4f, 0f, 180f));
            t.gates.Add(G(5, 12f, -8f, 90f));
            t.gates.Add(G(6, 20f, 0f, 0f));
            t.gates.Add(G(7, 12f, 8f, -90f));
            t.gates.Add(G(8, -12f, 8f, -90f));
            t.gates.Add(G(9, -20f, 0f, 180f));
            return t;
        }

        /// <summary>
        /// Flies every public track with the oracle pilot in its own venue scene and writes the outcome to
        /// Docs/bench/track_check.md. A track passes when it validates and the oracle completes a lap inside the time limit.
        /// </summary>
        [MenuItem("Ushqyr/Bench/Check Public Tracks")]
        public static string CheckTracks()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Public tracks: validation and oracle-pilot check\n");
            sb.AppendLine("Generated by *Ushqyr > Bench > Check Public Tracks*. A track passes when it has no validation errors and the oracle");
            sb.AppendLine("pilot (true position, authoring only) scores every gate once inside the time limit.\n");
            sb.AppendLine("| Track | Venue | Gates | Validation | Oracle lap, s | Collisions | Result |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            bool all = true;
            foreach (BenchTrack t in PublicTracks())
            {
                List<TrackIssue> issues = TrackValidator.Validate(t);
                OracleResult r = FlyInScene(t, t.rules.time_limit);
                int errors = issues.FindAll(i => i.Error).Count, warnings = issues.Count - errors;
                bool ok = errors == 0 && r.Lap && r.LapTime <= t.rules.time_limit;
                all &= ok;
                sb.AppendLine($"| `{t.id}` | {t.venue} | {t.gates.Count} | {errors} errors, {warnings} warnings | " +
                              (r.Lap ? r.LapTime.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "no lap") +
                              $" | {r.Collisions} | {(ok ? "pass" : "**FAIL**")} |");
                foreach (TrackIssue i in issues) sb.AppendLine($"|  |  |  | {i} |  |  |  |");
            }
            Directory.CreateDirectory("Docs/bench");
            File.WriteAllText("Docs/bench/track_check.md", sb.ToString().Replace("\r\n", "\n"));
            return (all ? "ALL PASS\n" : "FAILURES\n") + sb;
        }

        /// <summary>Opens the track's venue scene, stands its gates and props up, and lets the oracle fly it.</summary>
        public static OracleResult FlyInScene(BenchTrack t, float seconds)
        {
            EditorSceneManager.OpenScene(ScenePath(t.venue), OpenSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<BenchGate>(GatePrefabPath);
            var root = new GameObject("Oracle Check").transform;
            foreach (TrackGate g in t.gates)
                Object.Instantiate(prefab, BenchTrack.V(g.position), Quaternion.Euler(0f, g.yaw, 0f), root);
            BenchSession.BuildProps(t, root, prefab);
            Physics.SyncTransforms();
            int layer = LayerMask.NameToLayer(Builders.TrackSceneBuilder.TrackLayerName);
            OracleResult r = BenchOracle.Fly(t, 1 << layer, seconds);
            Object.DestroyImmediate(root.gameObject);
            return r;
        }

        // ---------- hall ----------

        [MenuItem("Ushqyr/Bench/Build Hall Scene")]
        public static string BuildHallScene() => BuildScene("hall");

        [MenuItem("Ushqyr/Bench/Build Parking Scene")]
        public static string BuildParkingScene() => BuildScene("parking");

        public static string BuildScene(string venueId)
        {
            Venue venue = Venue.Get(venueId);
            float HallLength = venue.Length, HallWidth = venue.Width, HallHeight = venue.Height;
            string HallScenePath = ScenePath(venueId);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int trackLayer = LayerMask.NameToLayer(Builders.TrackSceneBuilder.TrackLayerName);
            int quadLayer = LayerMask.NameToLayer(Builders.TrackSceneBuilder.LocalQuadLayerName);
            var gatePrefab = AssetDatabase.LoadAssetAtPath<BenchGate>(GatePrefabPath);

            var hall = new GameObject(venueId == "parking" ? "Parking" : "Hall").transform;
            if (venueId == "parking") BuildParking(hall, trackLayer, venue);
            else BuildHall(hall, trackLayer);
            BuildLighting(venueId);

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
            sso.FindProperty("defaultTrack").stringValue = venueId == "parking" ? "P1_Columns" : "H1_Oval";
            Builders.GarageSceneBuilder.AssignModelMaterials(sso.FindProperty("modelMaterials"));
            sso.ApplyModifiedPropertiesWithoutUndo();
            // Trackside video: four positions along the long walls and one high overview.
            var filmGo = new GameObject("Trackside Camera");
            var film = filmGo.AddComponent<BenchTracksideCamera>();
            float fx = HallLength * 0.5f - 1.2f, fz = HallWidth * 0.5f - 0.8f;
            var filmPositions = new[]
            {
                new Vector3(-fx * 0.55f, Mathf.Min(2.6f, HallHeight - 0.9f), -fz + 2.2f * (venueId == "parking" ? 1f : 0f)),
                new Vector3(fx * 0.55f, Mathf.Min(2.6f, HallHeight - 0.9f), -fz + 2.2f * (venueId == "parking" ? 1f : 0f)),
                new Vector3(-fx * 0.55f, Mathf.Min(2.6f, HallHeight - 0.9f), fz - 2.2f * (venueId == "parking" ? 1f : 0f)),
                new Vector3(fx * 0.55f, Mathf.Min(2.6f, HallHeight - 0.9f), fz - 2.2f * (venueId == "parking" ? 1f : 0f)),
                new Vector3(fx, Mathf.Min(3.2f, HallHeight - 0.7f), 0f), new Vector3(-fx, Mathf.Min(3.2f, HallHeight - 0.7f), 0f),
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
            const float L = HallL, W = HallW, H = HallH, t = 0.3f;
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

        private static void BuildParking(Transform root, int layer, Venue venue)
        {
            float L = venue.Length, W = venue.Width, H = venue.Height;
            const float t = 0.3f;
            GameObject Solid(string name, Vector3 centre, Vector3 size, string mat, bool shadows)
            {
                var go = Box(root, name, centre, size, mat, shadows);
                go.layer = layer;
                go.AddComponent<BoxCollider>();
                go.isStatic = true;
                return go;
            }

            Solid("Floor", new Vector3(0f, -t * 0.5f, 0f), new Vector3(L, t, W), "M_Asphalt_PitLane", false);
            Solid("Ceiling", new Vector3(0f, H + t * 0.5f, 0f), new Vector3(L, t, W), "M_Concrete_Wall", false);
            Solid("Wall North", new Vector3(0f, H * 0.5f, W * 0.5f + t * 0.5f), new Vector3(L + 2f * t, H, t), "M_Concrete_Blocks", false);
            Solid("Wall South", new Vector3(0f, H * 0.5f, -W * 0.5f - t * 0.5f), new Vector3(L + 2f * t, H, t), "M_Concrete_Blocks", false);
            Solid("Wall East", new Vector3(L * 0.5f + t * 0.5f, H * 0.5f, 0f), new Vector3(t, H, W), "M_Concrete_Blocks", false);
            Solid("Wall West", new Vector3(-L * 0.5f - t * 0.5f, H * 0.5f, 0f), new Vector3(t, H, W), "M_Concrete_Blocks", false);

            string[] paints = { "M_Paint_White", "M_Plastic_Grey", "M_Container_Blue", "M_Plastic_Black", "M_Container_Red", "M_Steel_Plate" };
            int column = 0, car = 0;
            foreach (VenueBox b in venue.Boxes)
            {
                if (b.Kind == "column")
                {
                    Solid("Column " + column, new Vector3(b.X, b.Height * 0.5f, b.Z), new Vector3(b.SizeX, b.Height, b.SizeZ), "M_Concrete_Pillar", true);
                    Box(root, "Column Stripe " + column++, new Vector3(b.X, 0.45f, b.Z), new Vector3(b.SizeX + 0.02f, 0.9f, b.SizeZ + 0.02f), "M_SafetyStripes", false);
                    continue;
                }
                // A parked car: one solid box for flying into, simple shapes for the eye.
                var holder = new GameObject("Car " + car) { layer = layer };
                holder.transform.SetParent(root, false);
                holder.transform.position = new Vector3(b.X, 0f, b.Z);
                var solid = holder.AddComponent<BoxCollider>();
                solid.center = new Vector3(0f, b.Height * 0.5f, 0f);
                solid.size = new Vector3(b.SizeX, b.Height, b.SizeZ);
                string paint = paints[b.Variant % paints.Length];
                float nose = b.Z < 0f ? 1f : -1f; // noses point into the lane
                Box(holder.transform, "Body", new Vector3(b.X, 0.62f, b.Z), new Vector3(b.SizeX, 0.62f, b.SizeZ), paint, true);
                Box(holder.transform, "Cabin", new Vector3(b.X, 1.2f, b.Z - nose * 0.35f), new Vector3(b.SizeX - 0.16f, 0.56f, b.SizeZ * 0.52f), "M_Glass_Night", true);
                Box(holder.transform, "Roof", new Vector3(b.X, 1.49f, b.Z - nose * 0.35f), new Vector3(b.SizeX - 0.2f, 0.04f, b.SizeZ * 0.48f), paint, false);
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                        Box(holder.transform, "Wheel", new Vector3(b.X + sx * (b.SizeX * 0.5f - 0.1f), 0.32f, b.Z + sz * b.SizeZ * 0.3f),
                            new Vector3(0.24f, 0.64f, 0.64f), "M_Rubber_Black", false);
                car++;
            }

            // Bay lines along both walls, lane lines, and strip lamps under the ceiling.
            int line = 0;
            foreach (float z in new[] { -14.6f, 14.6f })
                for (float x = -28.35f; x <= 28.4f; x += 2.7f)
                    Box(root, "Bay Line " + line++, new Vector3(x, 0.003f, z), new Vector3(0.1f, 0.006f, 4.6f), "M_Stadium_LinePaint", false);
            foreach (float z in new[] { -12f, 12f })
                Box(root, "Lane Line " + line++, new Vector3(0f, 0.003f, z * 0.985f), new Vector3(L - 3f, 0.006f, 0.1f), "M_Paint_Yellow", false);
            int lamp = 0;
            for (int ix = -3; ix <= 3; ix++)
                foreach (float z in new[] { -8f, 0f, 8f })
                    Box(root, "Lamp " + lamp++, new Vector3(ix * 8f + 4f * (ix < 3 ? 1f : -1f) * 0f, H - 0.06f, z), new Vector3(1.5f, 0.06f, 0.18f), "M_Lamp_Emitter", false);
        }

        private static void BuildLighting(string venueId)
        {
            bool parking = venueId == "parking";
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            float amb = parking ? 0.55f : 1f;
            RenderSettings.ambientSkyColor = new Color(0.8f, 0.8f, 0.78f) * amb;
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.72f, 0.72f) * amb;
            RenderSettings.ambientGroundColor = new Color(0.5f, 0.49f, 0.47f) * amb;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0.4f;

            // The hall's lamps as one soft light from above; the walls and the roof don't cast shadows, the gates do.
            var go = new GameObject("Hall Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = parking ? 0.6f : 1.1f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.55f;
            go.transform.rotation = Quaternion.Euler(72f, 25f, 0f);

            // Light bounced off the floor: it is what makes the roof and the undersides visible at all.
            var bounce = new GameObject("Floor Bounce").AddComponent<Light>();
            bounce.type = LightType.Directional;
            bounce.color = new Color(0.9f, 0.88f, 0.84f);
            bounce.intensity = parking ? 0.3f : 0.45f;
            bounce.shadows = LightShadows.None;
            bounce.transform.rotation = Quaternion.Euler(-80f, -40f, 0f);
            new GameObject("Venue Lights").AddComponent<BenchVenueLights>().Configure(light, bounce);
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
