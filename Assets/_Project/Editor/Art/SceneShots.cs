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
                cam.farClipPlane = Mathf.Max(source != null ? source.farClipPlane : 0f, 1500f);
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
