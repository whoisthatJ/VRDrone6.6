using System.Collections;
using System.IO;
using Ushqyr.Cameras;
using Ushqyr.Core;
using Ushqyr.Game;
using Ushqyr.Race;
using Ushqyr.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace Ushqyr.EditorTools.Capture
{
    /// <summary>
    /// Scripted gameplay clips for the pitch deck. In Play mode, <c>PitchClips.Run("name")</c> sets the settings in memory
    /// (nothing is saved), loads the track, drives the shot (autopilot, camera view, free camera paths) and records the
    /// Game view - HUD included - frame by frame at a fixed 30 fps into <c>Recordings/clips/&lt;name&gt;/</c>.
    /// <c>done.txt</c> appears there when the clip is finished.
    /// </summary>
    public static class PitchClips
    {
        public const int Fps = 30;
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "clips"));

        public static string Run(string clip)
        {
            if (!Application.isPlaying) return "enter Play mode first";
            var go = new GameObject("Clip Director " + clip);
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ClipDirector>().Clip = clip;
            return "recording " + clip;
        }

        public static string Status(string clip) =>
            File.Exists(Path.Combine(Root, clip, "done.txt")) ? File.ReadAllText(Path.Combine(Root, clip, "done.txt")) : "running";
    }

    public sealed class ClipDirector : MonoBehaviour
    {
        public string Clip;

        private string _dir;
        private int _frame;
        private bool _recording;

        private IEnumerator Start()
        {
            _dir = Path.Combine(PitchClips.Root, Clip);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            var s = SaveSystem.Settings;
            s.Race.BotCount = 0;
            s.Race.TrackIntro = false;
            s.Guidance.RouteGuidance = (int)RouteGuidanceLevel.Standard;
            s.Feed.ShowDebugOverlay = false;
            s.Camera.LensDistortion = false;
            IEnumerator shot;
            switch (Clip)
            {
                case "intro_expo": shot = IntroExpo(s); break;
                case "fpv_expo": shot = FlyTrack(s, "expo-v2-sprint", ViewMode.Fpv, 13f, 6f, 10f); break;
                case "chase_stadium": shot = FlyTrack(s, "stadium-oval", ViewMode.Fpv, 11f, 3f, 9f, night: true, chase: true); break;
                case "bots_chase": shot = BotsChase(s); break;
                case "bots_race": shot = BotsRace(s); break;
                case "aerial_expo": shot = AerialExpo(s); break;
                case "garage": shot = Garage(); break;
                default: Finish("unknown clip"); yield break;
            }
            yield return shot;
        }

        // ---------- shots ----------

        private IEnumerator IntroExpo(GameSettings s)
        {
            s.Feed.ExpoLighting = 2;
            s.Race.TrackIntro = true;
            yield return Load("expo-v2-sprint");
            yield return Record(TrackIntro.ShotSeconds * TrackIntro.Shots);
        }

        private IEnumerator FlyTrack(GameSettings s, string track, ViewMode view, float speed, float warmup, float seconds, bool night = false, bool chase = false)
        {
            s.Feed.ExpoLighting = 2;
            s.Feed.StadiumLighting = night ? 0 : 2;
            s.Camera.ViewMode = (int)view;
            yield return Load(track);
            var views = FindAnyObjectByType<ViewModeManager>();
            if (views != null) views.Apply(view);
            Ushqyr.EditorTools.SphereDistrict.CourseFlightTest.Attach(speed);
            yield return Wait(warmup);
            if (!chase)
            {
                yield return Record(seconds);
                yield break;
            }
            // The drawn model (quadVisual) rather than the physics object.
            var ctrl = FindAnyObjectByType<TrackSceneController>();
            var visual = typeof(TrackSceneController).GetField("quadVisual", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(ctrl) as Transform;
            var v = FindAnyObjectByType<QuadVehicle>();
            yield return Follow(views, visual != null ? visual : v.transform, seconds, 0.9f, 0.3f);
        }

        // Third person behind one bot in the pack: the other drones fly round it.
        private IEnumerator BotsChase(GameSettings s)
        {
            s.Feed.StadiumLighting = 2;
            s.Race.BotCount = 6;
            s.Race.BotLevel = 2;
            s.Race.BotCollisions = true;
            yield return Load("stadium-oval");
            var views = FindAnyObjectByType<ViewModeManager>();
            Ushqyr.EditorTools.SphereDistrict.CourseFlightTest.Attach(9f);
            var bots = FindAnyObjectByType<Ushqyr.AI.BotRace>();
            for (int i = 0; i < 300 && (bots == null || bots.Racers.Count < 2); i++)
            {
                yield return null;
                bots = FindAnyObjectByType<Ushqyr.AI.BotRace>();
            }
            if (bots == null || bots.Racers.Count < 2)
            {
                Finish("no bots");
                yield break;
            }
            // Follow the last bot on the grid so the rest of the pack is ahead of the camera.
            var target = bots.Racers[bots.Racers.Count - 1].Visual;
            yield return Follow(views, target, 11f, 1.0f, 0.32f);
        }

        /// <summary>Records a smoothed chase camera behind <paramref name="target"/> (HUD hidden).</summary>
        private IEnumerator Follow(ViewModeManager views, Transform target, float seconds, float back, float up)
        {
            HideHud();
            // An own camera, not the game's: that one sits on the quad's rig and hides the quad's own layer (FPV).
            var game = views.OutputCamera;
            var go = new GameObject("Clip Chase Camera");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(game);
            cam.cullingMask = ~0 & ~(1 << LayerMask.NameToLayer("VRRoom")) & ~(1 << LayerMask.NameToLayer("EditorUI"));
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.03f;
            var src = game.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var dst = cam.GetUniversalAdditionalCameraData();
            if (src != null)
            {
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.volumeLayerMask = src.volumeLayerMask;
            }
            // The player's own model is hidden in FPV: show it for the chase shot.
            var hidden = new System.Collections.Generic.List<Renderer>();
            foreach (var r in target.GetComponentsInChildren<Renderer>(true))
                if (!r.enabled) { r.enabled = true; hidden.Add(r); }
            var inactive = new System.Collections.Generic.List<GameObject>();
            foreach (var tr in target.GetComponentsInChildren<Transform>(true))
                if (!tr.gameObject.activeSelf && tr.GetComponent<Renderer>() != null) { tr.gameObject.SetActive(true); inactive.Add(tr.gameObject); }
            // Rendered by hand into an own texture each frame (not the screen, which shows the game camera).
            cam.enabled = false;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.aspect = 16f / 9f;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var t = cam.transform;
            Vector3 last = target.position, dir = target.forward, vel = Vector3.zero;
            t.position = target.position - Flat(dir) * back + Vector3.up * up;
            Time.captureFramerate = PitchClips.Fps;
            int frames = Mathf.RoundToInt(seconds * PitchClips.Fps);
            var eof = new WaitForEndOfFrame();
            for (int n = 0; n < frames; n++)
            {
                Vector3 d = target.position - last;
                last = target.position;
                if (d.sqrMagnitude > 1e-4f) dir = Vector3.Slerp(dir, d.normalized, 0.15f);
                Vector3 f = Flat(dir);
                Vector3 want = target.position - f * back + Vector3.up * up;
                t.position = Vector3.SmoothDamp(t.position, want, ref vel, 0.03f);
                t.rotation = Quaternion.LookRotation(target.position + f * 1.2f + Vector3.up * 0.1f - t.position, Vector3.up);
                yield return eof;
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(_dir, $"f{_frame++:0000}.jpg"), tex.EncodeToJPG(92));
            }
            foreach (var r in hidden) if (r != null) r.enabled = false;
            foreach (var g in inactive) if (g != null) g.SetActive(false);
            Destroy(go);
            Destroy(rt);
            Destroy(tex);
            Finish($"{_frame} frames 1920x1080 (chase camera)");
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }

        private static void HideHud()
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas) c.enabled = false;
        }

        private IEnumerator BotsRace(GameSettings s)
        {
            s.Feed.StadiumLighting = 0;
            s.Camera.ViewMode = (int)ViewMode.Chase;
            s.Race.BotCount = 5;
            s.Race.BotLevel = 2;
            s.Race.BotCollisions = true;
            yield return Load("stadium-oval");
            var views = FindAnyObjectByType<ViewModeManager>();
            if (views != null) views.Apply(ViewMode.Chase);
            Ushqyr.EditorTools.SphereDistrict.CourseFlightTest.Attach(9f);
            yield return Wait(0.5f);
            yield return Record(11f);
        }

        private IEnumerator AerialExpo(GameSettings s)
        {
            s.Feed.ExpoLighting = 1; // golden hour
            yield return Load("expo-v2-free");
            var views = FindAnyObjectByType<ViewModeManager>();
            var v = FindAnyObjectByType<QuadVehicle>();
            if (v != null) v.HoldAtSpawn = true;
            HideHud();
            views.SetFreeCamera(true);
            var cam = views.OutputCamera.transform;
            float seconds = 10f;
            StartCoroutine(RecordRoutine(seconds));
            for (float t = 0f; t < seconds; t += 1f / PitchClips.Fps)
            {
                float u = t / seconds, e = u * u * (3f - 2f * u);
                float bearing = 200f + 70f * e;
                float dist = Mathf.Lerp(300f, 190f, e), height = Mathf.Lerp(130f, 70f, e);
                Vector3 pos = Ushqyr.EditorTools.Builders.RouteLibrary.ExpoPoint(bearing, dist, height);
                cam.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(0f, 45f, 0f) - pos, Vector3.up));
                yield return null;
            }
            while (_recording) yield return null;
            views.SetFreeCamera(false);
        }

        private IEnumerator Garage()
        {
            SceneManager.LoadScene(GameFlow.GarageScene);
            yield return null;
            yield return Wait(4f);
            var gc = FindAnyObjectByType<Ushqyr.UI.GarageCamera>();
            var garage = FindAnyObjectByType<Ushqyr.UI.GarageController>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var yaw = typeof(Ushqyr.UI.GarageCamera).GetField("yaw", flags);
            var setBuild = typeof(Ushqyr.UI.GarageController).GetMethod("SetBuild", flags);
            // One of each class: 5" race, 5" freestyle, 3" toothpick, 3.5" cinewhoop, 65 mm whoop (the 7" pushes the
            // garage camera back through the wall).
            var builds = new[]
            {
                Ushqyr.Garage.QuadPresets.Race5, Ushqyr.Garage.QuadPresets.Freestyle5, Ushqyr.Garage.QuadPresets.Toothpick3,
                Ushqyr.Garage.QuadPresets.Cinewhoop35, Ushqyr.Garage.QuadPresets.Whoop65,
            };
            const float each = 1.8f;
            float seconds = each * builds.Length;
            if (setBuild == null) Debug.LogWarning("[Ushqyr][Clips] GarageController.SetBuild not found: drones won't switch");
            int shown = -1;
            StartCoroutine(RecordRoutine(seconds));
            float start = gc != null ? (float)yaw.GetValue(gc) : 0f;
            for (float t = 0f; t < seconds; t += 1f / PitchClips.Fps)
            {
                int k = Mathf.Min(builds.Length - 1, (int)(t / each));
                if (k != shown && setBuild != null && garage != null)
                {
                    shown = k;
                    setBuild.Invoke(garage, new object[] { builds[k], true });
                }
                if (gc != null) yaw.SetValue(gc, start + 25f * t);
                yield return null;
            }
            while (_recording) yield return null;
        }

        // ---------- helpers ----------

        private IEnumerator Load(string trackId)
        {
            SaveSystem.Settings.TrackId = trackId;
            var cat = UnityEditor.AssetDatabase.LoadAssetAtPath<TrackCatalog>("Assets/_Project/Data/TrackCatalog.asset");
            GameFlow.Catalog = cat;
            // The private overload that does not save the choice (the user's own track selection stays).
            typeof(GameFlow).GetMethod("Race", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { cat, trackId, false });
            // Wait for the track scene to finish loading its course.
            for (int i = 0; i < 600; i++)
            {
                var c = FindAnyObjectByType<TrackSceneController>();
                if (c != null && c.Track != null) break;
                yield return null;
            }
            yield return null;
        }

        private IEnumerator Wait(float seconds)
        {
            Time.captureFramerate = PitchClips.Fps;
            int frames = Mathf.RoundToInt(seconds * PitchClips.Fps);
            for (int i = 0; i < frames; i++) yield return null;
        }

        private IEnumerator Record(float seconds)
        {
            yield return RecordRoutine(seconds);
        }

        private IEnumerator RecordRoutine(float seconds)
        {
            _recording = true;
            Time.captureFramerate = PitchClips.Fps;
            int frames = Mathf.RoundToInt(seconds * PitchClips.Fps);
            var eof = new WaitForEndOfFrame();
            for (int i = 0; i < frames; i++)
            {
                yield return eof;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(_dir, $"f{_frame++:0000}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
            }
            _recording = false;
            Finish($"{_frame} frames {Screen.width}x{Screen.height}");
        }

        private void Finish(string note)
        {
            Time.captureFramerate = 0;
            File.WriteAllText(Path.Combine(_dir, "done.txt"), note);
            Destroy(gameObject, 0.1f);
        }
    }
}
