using System.Collections.Generic;
using System.IO;
using System.Text;
using GateRush.EditorTools.Builders;
using GateRush.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Camera bookmarks per scene and before/after screenshots at 1920×1080:
    /// <c>Docs/screenshots/&lt;scene&gt;/&lt;bookmark&gt;_&lt;tag&gt;.jpg</c> plus a 2×2 contact sheet <c>_sheet_&lt;tag&gt;.jpg</c>
    /// (order: top-left, top-right, bottom-left, bottom-right). Track scenes get their built-in course spawned
    /// temporarily so gates show; the scene is never saved.
    /// </summary>
    public static class SceneShots
    {
        public const int Width = 1920, Height = 1080;
        public const string Root = "Docs/screenshots";
        public const string PrefabsPath = "Assets/_Project/Data/TrackPrefabs.asset";
        public static readonly string[] Environments = { "Field", "Hangar", "Industrial", "Stadium" };

        private const int LocalQuadLayer = 9, VRRoomLayer = 10, EditorUILayer = 11;

        public struct Bookmark
        {
            public string Name;
            public Vector3 Position;
            public Quaternion Rotation;
            public float VerticalFov;
            public bool HideLocalQuad;
            /// <summary>Vertical lens shift in frame heights (off-axis projection, like a cropped photo).</summary>
            public float LensShiftY;

            public Bookmark(string name, Vector3 position, Vector3 lookAt, float verticalFov, bool hideLocalQuad = false)
            {
                Name = name;
                Position = position;
                Rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
                VerticalFov = verticalFov;
                HideLocalQuad = hideLocalQuad;
                LensShiftY = 0f;
            }
        }

        [MenuItem("Tools/Art/Screenshots/Capture Before")]
        private static void MenuBefore() => Debug.Log(CaptureAll("before"));

        [MenuItem("Tools/Art/Screenshots/Capture After")]
        private static void MenuAfter() => Debug.Log(CaptureAll("after"));

        public static string CaptureAllDeferred(string tag) => ArtJobs.RunDeferred("screenshots " + tag, () => CaptureAll(tag));

        public static string CaptureAll(string tag)
        {
            ArtJobs.RequireCleanScenes();
            var report = new StringBuilder();
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // no cyan placeholder shaders in the captures
            try
            {
                foreach (var env in Environments) report.AppendLine(CaptureTrack(env, tag));
                report.AppendLine(CaptureMenu(tag));
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                ArtJobs.ReturnToBoot();
            }
            return report.ToString();
        }

        public static TrackData CourseFor(string environment) => environment switch
        {
            "Field" => CourseLibrary.Field(),
            "Hangar" => CourseLibrary.Hangar(),
            "Stadium" => RouteLibrary.StadiumOval(),
            _ => CourseLibrary.Industrial(),
        };

        public static string CaptureTrack(string environment, string tag)
        {
            string sceneName = "Track_" + environment;
            EditorSceneManager.OpenScene(ArtJobs.ScenePath(sceneName), OpenSceneMode.Single);
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PrefabsPath);
            var track = TrackLoader.Load(CourseFor(environment), prefabs);
            try
            {
                var source = GameObject.Find("Main Camera")?.GetComponent<Camera>();
                return CaptureSet(sceneName, TrackBookmarks(track, indoor: environment == "Hangar"), source, tag);
            }
            finally
            {
                Object.DestroyImmediate(track.Root.gameObject);
            }
        }

        public static string CaptureStadiumDeferred(string tag) => ArtJobs.RunDeferred("stadium shots " + tag, () => CaptureStadium(tag));

        /// <summary>Track_Stadium under each time-of-day preset: overview, pitch-side and FPV at the start (T3).</summary>
        public static string CaptureStadium(string tag)
        {
            ArtJobs.RequireCleanScenes();
            const string sceneName = "Track_Stadium";
            EditorSceneManager.OpenScene(ArtJobs.ScenePath(sceneName), OpenSceneMode.Single);
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PrefabsPath);
            var track = TrackLoader.Load(RouteLibrary.StadiumOval(), prefabs);
            var lighting = Object.FindAnyObjectByType<GateRush.Core.StadiumLighting>();
            var sb = new StringBuilder(sceneName + ":");
            try
            {
                var source = GameObject.Find("Main Camera")?.GetComponent<Camera>();
                var generic = TrackBookmarks(track);
                foreach (var time in new[] { GateRush.Core.TimeOfDay.Night, GateRush.Core.TimeOfDay.Dusk, GateRush.Core.TimeOfDay.Day })
                {
                    lighting.Apply(time);
                    var shots = new List<Bookmark>
                    {
                        new Bookmark("overview", new Vector3(-25f, 80f, -128f), new Vector3(0f, 0f, 8f), 52f),
                        new Bookmark("touchline", new Vector3(-20f, 3.5f, -33f), new Vector3(15f, 6f, 60f), 60f),
                        new Bookmark("bend", new Vector3(60f, 2.2f, -40f), new Vector3(80f, 1.5f, 10f), 60f),
                    };
                    shots.AddRange(generic.FindAll(b => b.Name == "spawn" || b.Name == "fpv"));
                    for (int i = 0; i < shots.Count; i++)
                    {
                        var b = shots[i];
                        b.Name = $"{time.ToString().ToLowerInvariant()}_{b.Name}";
                        shots[i] = b;
                    }
                    sb.Append(' ').Append(CaptureSet(sceneName, shots, source, $"{tag}"));
                }
                lighting.Apply(GateRush.Core.TimeOfDay.Night);
            }
            finally
            {
                Object.DestroyImmediate(track.Root.gameObject);
                ArtJobs.ReturnToBoot();
            }
            return sb.ToString();
        }

        public static string CaptureSerpentDeferred(string tag) => ArtJobs.RunDeferred("serpent shots " + tag, () => CaptureSerpent(tag));

        /// <summary>
        /// Serpent Arena (T4) at night and dusk: the reference camera (pose recovered from the homography in
        /// Docs/reference/trace_serpent.py with the principal point solved too: the reference is the top of a 68° frame), an overview and FPV.
        /// </summary>
        public static string CaptureSerpent(string tag)
        {
            ArtJobs.RequireCleanScenes();
            const string sceneName = "Track_Stadium";
            EditorSceneManager.OpenScene(ArtJobs.ScenePath(sceneName), OpenSceneMode.Single);
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PrefabsPath);
            var track = TrackLoader.Load(RouteLibrary.SerpentArena(), prefabs);
            var lighting = Object.FindAnyObjectByType<GateRush.Core.StadiumLighting>();
            var sb = new StringBuilder(sceneName + ":");
            try
            {
                var source = GameObject.Find("Main Camera")?.GetComponent<Camera>();
                var generic = TrackBookmarks(track);
                foreach (var time in new[] { GateRush.Core.TimeOfDay.Night, GateRush.Core.TimeOfDay.Dusk })
                {
                    lighting.Apply(time);
                    var shots = new List<Bookmark>
                    {
                        // Optical axis + lens shift: the reference is the top part of a 68° frame (principal point below it).
                        new Bookmark("serpent_reference", new Vector3(-52.35f, 43.04f, -51.9f), new Vector3(-30.31f, -5.53f, -24.41f), 41.6f) { LensShiftY = 0.552f },
                        new Bookmark("serpent_overview", new Vector3(0f, 95f, -70f), new Vector3(0f, 0f, 2f), 50f),
                    };
                    foreach (var g in generic.FindAll(b => b.Name == "spawn" || b.Name == "fpv"))
                    {
                        var b = g;
                        b.Name = "serpent_" + b.Name;
                        shots.Add(b);
                    }
                    for (int i = 0; i < shots.Count; i++)
                    {
                        var b = shots[i];
                        b.Name = $"{time.ToString().ToLowerInvariant()}_{b.Name}";
                        shots[i] = b;
                    }
                    sb.Append(' ').Append(CaptureSet(sceneName, shots, source, tag));
                }
                lighting.Apply(GateRush.Core.TimeOfDay.Night);
            }
            finally
            {
                Object.DestroyImmediate(track.Root.gameObject);
                ArtJobs.ReturnToBoot();
            }
            return sb.ToString();
        }

        /// <summary>P2: each shipped build on the garage turntable (detailed parts), three-quarter view and top view.</summary>
        public static string CaptureQuads(string tag)
        {
            ArtJobs.RequireCleanScenes();
            const string sceneName = "Garage";
            EditorSceneManager.OpenScene(ArtJobs.ScenePath(sceneName), OpenSceneMode.Single);
            var garage = Object.FindAnyObjectByType<GateRush.UI.GarageController>(FindObjectsInactive.Include);
            var so = new SerializedObject(garage);
            var turntable = (Transform)so.FindProperty("turntable").objectReferenceValue;
            var field = typeof(GateRush.UI.GarageController).GetField("materials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var mats = (GateRush.Garage.QuadModelMaterials)field.GetValue(garage);
            var source = Object.FindAnyObjectByType<Camera>();
            // No depth of field: it is focused for the garage's own framing and blurs these close-ups.
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = false;
            var shots = new List<Bookmark>();
            var models = new List<GameObject>();
            var sb = new StringBuilder(sceneName + ":");
            try
            {
                foreach (var b in GateRush.Garage.QuadPresets.All)
                {
                    var model = GateRush.Garage.QuadModelBuilder.Build(b, mats, turntable, turntable.gameObject.layer);
                    var bounds = GateRush.Garage.QuadModelBuilder.Bounds(model.Root);
                    float dist = bounds.extents.magnitude / Mathf.Tan(15f * Mathf.Deg2Rad) * 0.95f;
                    string id = b.Id.Replace("preset-", "");
                    // From the garage camera's side (the bench backs onto the wall), a little higher.
                    Vector3 dir = source != null ? (source.transform.position - bounds.center).normalized : new Vector3(0.75f, 0.55f, 1f).normalized;
                    dir = (new Vector3(dir.x, 0f, dir.z).normalized + Vector3.up * 0.55f).normalized;
                    var three = new Bookmark($"quad_{id}_34", bounds.center + dir * dist, bounds.center, 30f);
                    var top = new Bookmark($"quad_{id}_top", bounds.center + new Vector3(0f, 1f, -0.02f) * dist, bounds.center, 30f);
                    sb.Append(' ').Append(CaptureSet(sceneName, new List<Bookmark> { three, top }, source, tag));
                    Object.DestroyImmediate(model.Root);
                }
            }
            finally
            {
                ArtJobs.ReturnToBoot();
            }
            return sb.ToString();
        }

        /// <summary>Sphere District lighting presets (E5): the reference view, the walkway and a sphere close-up per preset.</summary>
        public static string CaptureExpoLighting(string tag)
        {
            ArtJobs.RequireCleanScenes();
            const string sceneName = "Track_Expo";
            EditorSceneManager.OpenScene(ArtJobs.ScenePath(sceneName), OpenSceneMode.Single);
            var lighting = Object.FindAnyObjectByType<GateRush.Core.StadiumLighting>();
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var sb = new StringBuilder(sceneName + ":");
            try
            {
                var source = GameObject.Find("Main Camera")?.GetComponent<Camera>();
                foreach (var time in new[] { GateRush.Core.TimeOfDay.Day, GateRush.Core.TimeOfDay.Dusk, GateRush.Core.TimeOfDay.Night })
                {
                    lighting.Apply(time);
                    string name = time == GateRush.Core.TimeOfDay.Dusk ? "golden" : time.ToString().ToLowerInvariant();
                    var shots = new List<Bookmark>
                    {
                        new Bookmark($"{name}_reference", RouteLibrary.ExpoPoint(194f, 470f, 235f), RouteLibrary.ExpoPoint(14f, 60f), 66f),
                        new Bookmark($"{name}_walkway", RouteLibrary.ExpoPoint(194f, 236f, 4f), RouteLibrary.ExpoPoint(14f, 30f, 30f), 62f),
                        new Bookmark($"{name}_sphere", RouteLibrary.ExpoPoint(160f, 150f, 20f), new Vector3(0f, 55f, 0f), 55f),
                    };
                    sb.Append(' ').Append(CaptureSet(sceneName, shots, source, tag + "_" + name));
                }
                lighting.Apply(GateRush.Core.TimeOfDay.Day);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                ArtJobs.ReturnToBoot();
            }
            return sb.ToString();
        }

        /// <summary>
        /// The finish cube (T6) on the Serpent Arena at night: from the approach, from the side, and the finish-moment
        /// cinematic view (inside the cube through the hexagon, the quad moved to the hover spot).
        /// </summary>
        public static string CaptureFinish(string tag)
        {
            ArtJobs.RequireCleanScenes();
            const string sceneName = "Track_Stadium";
            EditorSceneManager.OpenScene(ArtJobs.ScenePath(sceneName), OpenSceneMode.Single);
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PrefabsPath);
            var track = TrackLoader.Load(RouteLibrary.SerpentArena(), prefabs);
            try
            {
                var source = GameObject.Find("Main Camera")?.GetComponent<Camera>();
                var cube = track.Root.GetComponentInChildren<GateRush.Race.FinishCube>();
                if (cube == null) return "no finish cube";
                var t = cube.transform;
                var quad = Object.FindAnyObjectByType<GateRush.Vehicle.QuadVehicle>();
                if (quad != null)
                {
                    quad.enabled = false;
                    quad.transform.SetPositionAndRotation(cube.HoverAnchor.position, cube.HoverAnchor.rotation);
                    Physics.SyncTransforms();
                }
                Vector3 centre = t.position + Vector3.up * 2f;
                var shots = new List<Bookmark>
                {
                    new Bookmark("finish_approach", t.TransformPoint(new Vector3(-1.2f, 1.6f, -9f)), centre, 60f),
                    new Bookmark("finish_side", t.TransformPoint(new Vector3(-7f, 3f, -5f)), centre, 55f),
                    new Bookmark("finish_cinematic", cube.CameraAnchor.position, cube.CameraAnchor.position + cube.CameraAnchor.forward, 40f) { Rotation = cube.CameraAnchor.rotation },
                };
                return sceneName + ": " + CaptureSet(sceneName, shots, source, tag);
            }
            finally
            {
                Object.DestroyImmediate(track.Root.gameObject);
                ArtJobs.ReturnToBoot();
            }
        }

        /// <summary>
        /// Expo Astana: the reference view (aerial from the south like Docs/reference/ref_expo_astana.webp), an overview,
        /// the walkway, the sphere orbit and the spawn / FPV.
        /// </summary>
        public static string CaptureExpo(string tag) => CaptureExpo(tag, "Track_Expo", RouteLibrary.ExpoSprint());

        /// <summary>The same bookmarks in another Expo scene (Track_Expo_v1 with its frozen Classic Sprint).</summary>
        public static string CaptureExpoV1(string tag) => CaptureExpo(tag, "Track_Expo_v1",
            TrackData.FromJson(File.ReadAllText(GateRush.EditorTools.SphereDistrict.TrackFreezer.TrackJson("expo-astana"))));

        public static string CaptureExpo(string tag, string sceneName, TrackData course)
        {
            ArtJobs.RequireCleanScenes();
            EditorSceneManager.OpenScene(ArtJobs.ScenePath(sceneName), OpenSceneMode.Single);
            // The scene's own day preset (sky, ambient, probes): in the Editor the ambient otherwise carries over from
            // whichever scene was open before, and two captures of the same scene differ.
            var venue = Object.FindAnyObjectByType<GateRush.Core.StadiumLighting>();
            if (venue != null) venue.Apply(GateRush.Core.TimeOfDay.Day);
            else DynamicGI.UpdateEnvironment();
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PrefabsPath);
            var track = TrackLoader.Load(course, prefabs);
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var source = GameObject.Find("Main Camera")?.GetComponent<Camera>();
                var shots = new List<Bookmark>
                {
                    // The reference photo: high over the south-south-west edge, looking up the axis (bearing 14).
                    new Bookmark("expo_reference", RouteLibrary.ExpoPoint(194f, 470f, 235f), RouteLibrary.ExpoPoint(14f, 60f), 66f),
                    // P1: framed like the photo (solved from where the sphere, the petal edge and the horizon sit in it; compare on a 4:3 centre crop).
                    new Bookmark("expo_photo", RouteLibrary.ExpoPoint(194f, 450f, 262f), RouteLibrary.ExpoPoint(194f, 52f), 66f),
                    // Straight down, north up, like the satellite view (Docs/reference/ref_expo_astana_top.png).
                    new Bookmark("expo_top", new Vector3(0f, 900f, -0.5f), Vector3.zero, 50f),
                    new Bookmark("expo_overview", RouteLibrary.ExpoPoint(245f, 420f, 170f), new Vector3(0f, 20f, 0f), 50f),
                    new Bookmark("expo_walkway", RouteLibrary.ExpoPoint(194f, 236f, 4f), RouteLibrary.ExpoPoint(14f, 30f, 30f), 62f),
                    new Bookmark("expo_orbit", RouteLibrary.ExpoPoint(170f, 112f, 2.5f), RouteLibrary.ExpoPoint(120f, 112f, 8f), 66f),
                    new Bookmark("expo_trees", RouteLibrary.ExpoPoint(300f, 165f, 5f), RouteLibrary.ExpoPoint(290f, 260f, 3f), 60f),
                    new Bookmark("expo_lawn", RouteLibrary.ExpoPoint(250f, 305f, 3f), RouteLibrary.ExpoPoint(215f, 305f, 6f), 60f),
                };
                foreach (var g in TrackBookmarks(track).FindAll(b => b.Name == "spawn" || b.Name == "fpv"))
                {
                    var b = g;
                    b.Name = "expo_" + b.Name;
                    shots.Add(b);
                }
                return sceneName + ": " + CaptureSet(sceneName, shots, source, tag);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                Object.DestroyImmediate(track.Root.gameObject);
                ArtJobs.ReturnToBoot();
            }
        }

        public static string CaptureMenu(string tag)
        {
            EditorSceneManager.OpenScene(ArtJobs.ScenePath("Menu"), OpenSceneMode.Single);
            var head = Camera.main;
            // The XR rig lifts the head camera to standing height only in Play mode; the panel UI is also built at
            // runtime, so CapturePlayModeMenu adds the real in-game view.
            var shots = new List<Bookmark>
            {
                new Bookmark("menu", new Vector3(0f, 1.6f, 0f), new Vector3(0f, 1.45f, 1.6f), 60f),
                new Bookmark("room", new Vector3(-2.4f, 2.3f, -2.6f), new Vector3(0f, 1.1f, 1.2f), 65f),
            };
            shots.Add(new Bookmark("panel", new Vector3(0.9f, 1.5f, 0.2f), new Vector3(0f, 1.45f, 1.6f), 55f));
            shots.Add(new Bookmark("floor", new Vector3(0f, 0.6f, -1.5f), new Vector3(0f, 0.2f, 3f), 70f));
            return CaptureSet("Menu", shots, head, tag);
        }

        /// <summary>Spawn, first gate, overview and an in-flight FPV view (120° horizontal FOV, like the default drone camera).</summary>
        public static List<Bookmark> TrackBookmarks(LoadedTrack track, bool indoor = false)
        {
            var gates = track.Gates;
            Vector3 spawn = track.Spawn.position;
            Vector3 g0 = gates[0].OpeningCenterWorld;
            Vector3 toGate = g0 - spawn;
            toGate.y = 0f;
            Vector3 fwd = toGate.sqrMagnitude > 0.01f ? toGate.normalized : track.Spawn.forward;

            var list = new List<Bookmark>
            {
                new Bookmark("spawn", spawn - fwd * 5f + Vector3.up * 1.8f, spawn + fwd * 12f + Vector3.up * 0.8f, 55f),
            };

            Vector3 n = gates[0].transform.forward;
            if (Vector3.Dot(spawn - g0, n) < 0f) n = -n;
            list.Add(new Bookmark("gate1", g0 + n * 7f + Vector3.up * 0.4f, g0, 60f));

            var bounds = new Bounds(spawn, Vector3.zero);
            foreach (var g in gates) bounds.Encapsulate(g.OpeningCenterWorld);
            float r = Mathf.Max(bounds.extents.x, bounds.extents.z, 15f);
            Vector3 eye = bounds.center + new Vector3(-0.9f * r, 0.75f * r + 10f, -0.9f * r);
            if (indoor)
            {
                // Stay under the hangar roof (16 m) and inside its walls (x ±48, z ±24).
                eye = new Vector3(Mathf.Clamp(eye.x, -45f, 45f), 12.5f, Mathf.Clamp(eye.z, -22f, 22f));
            }
            list.Add(new Bookmark("overview", eye, bounds.center, indoor ? 70f : 50f));

            Vector3 p = Vector3.Lerp(spawn, g0, 0.45f);
            p.y = Mathf.Max(g0.y, 1.2f);
            float vfov = Camera.HorizontalToVerticalFieldOfView(120f, Width / (float)Height);
            list.Add(new Bookmark("fpv", p, g0 + (g0 - p).normalized * 20f, vfov, hideLocalQuad: true));
            return list;
        }

        private static string CaptureSet(string sceneName, List<Bookmark> bookmarks, Camera source, string tag)
        {
            string dir = Path.Combine(Root, sceneName);
            Directory.CreateDirectory(dir);
            var shots = new List<Texture2D>();
            var sb = new StringBuilder(sceneName + ":");
            foreach (var bm in bookmarks)
            {
                var tex = Render(source, bm);
                string file = Path.Combine(dir, $"{bm.Name}_{tag}.jpg");
                File.WriteAllBytes(file, tex.EncodeToJPG(90));
                shots.Add(tex);
                sb.Append(' ').Append(bm.Name);
            }
            SaveSheet(shots, Path.Combine(dir, $"_sheet_{tag}.jpg"));
            foreach (var t in shots) Object.DestroyImmediate(t);
            return sb.ToString();
        }

        public static Texture2D Render(Camera source, Bookmark bm)
        {
            var go = new GameObject("__ShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // variants still compiling would render as placeholders
            try
            {
                // Copy only what the shot needs: Camera.CopyFrom also copies stereo settings, which URP warns about.
                if (source != null)
                {
                    cam.clearFlags = source.clearFlags;
                    cam.backgroundColor = source.backgroundColor;
                    cam.allowHDR = source.allowHDR;
                    cam.allowMSAA = source.allowMSAA;
                    cam.useOcclusionCulling = source.useOcclusionCulling;
                }
                cam.transform.SetPositionAndRotation(bm.Position, bm.Rotation);
                cam.usePhysicalProperties = false;
                cam.fieldOfView = bm.VerticalFov;
                cam.aspect = Width / (float)Height;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = Mathf.Max(source != null ? source.farClipPlane : 0f, 6000f);
                if (bm.LensShiftY != 0f)
                {
                    var proj = cam.projectionMatrix;
                    proj.m12 += 2f * bm.LensShiftY;
                    cam.projectionMatrix = proj;
                }
                int mask = ~((1 << VRRoomLayer) | (1 << EditorUILayer));
                if (bm.HideLocalQuad) mask &= ~(1 << LocalQuadLayer);
                cam.cullingMask = mask;
                // GPU-instanced crowds of seats are queued per camera.
                GateRush.Core.InstancedSeats.DrawAll(cam);
                GateRush.Core.InstancedProps.DrawAll(cam);

                // Camera.CopyFrom does not copy URP's per-camera data: mirror post-processing and AA from the game camera.
                var data = cam.GetUniversalAdditionalCameraData();
                var src = source != null ? source.GetComponent<UniversalAdditionalCameraData>() : null;
                data.renderPostProcessing = src != null && src.renderPostProcessing;
                data.antialiasing = src != null ? src.antialiasing : AntialiasingMode.None;
                data.antialiasingQuality = src != null ? src.antialiasingQuality : AntialiasingQuality.High;

                var desc = new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 1 };
                var rt = RenderTexture.GetTemporary(desc);
                cam.targetTexture = rt;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                return tex;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Stores a Play-mode Game view capture (PNG from <c>unity command screenshot</c>) as Docs/screenshots/&lt;scene&gt;/&lt;name&gt;_&lt;tag&gt;.jpg.</summary>
        public static string StoreCapture(string pngPath, string sceneName, string name, string tag)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(File.ReadAllBytes(pngPath));
            string dir = Path.Combine(Root, sceneName);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"{name}_{tag}.jpg");
            File.WriteAllBytes(file, tex.EncodeToJPG(90));
            Object.DestroyImmediate(tex);
            return file;
        }

        private static void SaveSheet(List<Texture2D> shots, string path)
        {
            int w = Width / 2, h = Height / 2;
            var sheet = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            var fill = new Color32[Width * Height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(24, 24, 24, 255);
            sheet.SetPixels32(fill);
            var small = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            for (int i = 0; i < shots.Count && i < 4; i++)
            {
                Graphics.Blit(shots[i], small);
                RenderTexture.active = small;
                sheet.ReadPixels(new Rect(0, 0, w, h), (i % 2) * w, i < 2 ? h : 0);
            }
            sheet.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(small);
            File.WriteAllBytes(path, sheet.EncodeToJPG(88));
            Object.DestroyImmediate(sheet);
        }
    }
}
