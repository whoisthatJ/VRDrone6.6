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
        private bool _recording, _stopRecording;

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
                case "strike_chase": shot = StrikeChase(s); break;
                case "strike_targets": shot = StrikeTargets(s); break;
                case "strike_fpv": shot = StrikeFpv(s); break;
                case "menu_strike": shot = MenuStill("ShowVenue", "steppe"); break;
                case "strike_results": shot = StrikeResults(s); break;
                case "shot_race_chase": shot = ShotRaceChase(s); break;
                case "shot_expo": shot = ShotExpo(s); break;
                case "shot_fpv": shot = ShotFpv(s); break;
                case "shot_strike": shot = ShotStrike(s); break;
                case "shot_garage": shot = ShotGarage(); break;
                case "perf_expo": shot = PerfExpo(s, "expo-v2-sprint"); break;
                case "perf_stadium": shot = PerfExpo(s, "stadium-oval"); break;
                case "perf_steppe": shot = PerfExpo(s, "strike-steppe"); break;
                case "shot_hitfx": shot = ShotHitFx(s); break;
                case "menu_lan": shot = MenuStill("ShowOnline", null); break;
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
        private IEnumerator Follow(ViewModeManager views, Transform target, float seconds, float back, float up,
            System.Func<Vector3> probe = null, System.Func<Vector3> velocity = null, System.Func<bool> stop = null)
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
                if (stop != null && stop()) break;
                Vector3 at = probe != null ? probe() : target.position;
                Vector3 d = velocity != null ? velocity() : at - last;
                last = at;
                if (d.sqrMagnitude > 1e-4f) dir = Vector3.Slerp(dir, d.normalized, velocity != null ? 0.25f : 0.15f);
                Vector3 f = probe != null ? dir.normalized : Flat(dir);
                Vector3 want = at - f * back + Vector3.up * up;
                // Fast subjects (strike bots at 20+ m/s) are followed exactly; the smoothing would drop them behind.
                t.position = probe != null ? want : Vector3.SmoothDamp(t.position, want, ref vel, 0.03f);
                t.rotation = Quaternion.LookRotation(at + f * 3f - t.position, Vector3.up);
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

        // ---------- Strike Range (P5-P7) ----------

        private IEnumerator LoadStrike(GameSettings s, int bots)
        {
            s.Strike.Difficulty = 2; // Hard: convoys, evasive vehicles, smoke and wind
            s.Strike.Bots = bots;
            s.Strike.BotLevel = 4;
            s.Strike.Weather = 0;
            yield return Load("strike-steppe");
            Ushqyr.Strike.StrikeSession session = null;
            for (int i = 0; i < 900; i++)
            {
                session = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
                if (session != null && session.Phase == Ushqyr.Strike.StrikePhase.Round) break;
                yield return null;
            }
        }

        // Behind an Elite bot from transit into its dive onto a target.
        private IEnumerator StrikeChase(GameSettings s)
        {
            yield return LoadStrike(s, 3);
            var session = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
            if (session == null || session.Bots.Count == 0)
            {
                Finish("no strike bots");
                yield break;
            }
            yield return Wait(2f);
            Transform target = null;
            foreach (Transform c in session.transform)
                if (c.name.StartsWith("Strike Bot ")) { target = c; break; }
            if (target == null)
            {
                Finish("no bot visual");
                yield break;
            }
            var bot = session.Bots[0];
            float hitAt = -1f;
            System.Func<bool> stop = () =>
            {
                if (hitAt < 0f && bot.Score.Hits > 0) hitAt = Time.time;
                return hitAt >= 0f && Time.time - hitAt > 1.2f;
            };
            yield return Follow(FindAnyObjectByType<ViewModeManager>(), target, 30f, 2.4f, 0.8f, () => bot.Position, () => bot.Velocity, stop);
        }

        // A camera over a moving convoy target with the strike HUD (timer, scores, radar, markers), bots diving in.
        private IEnumerator StrikeTargets(GameSettings s)
        {
            yield return LoadStrike(s, 3);
            var session = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
            var views = FindAnyObjectByType<ViewModeManager>();
            if (session == null || session.Director == null || views == null)
            {
                Finish("no strike session");
                yield break;
            }
            int slot = session.Director.Targets.FindIndex(t => t.Plan.Class == Ushqyr.Strike.TargetClass.Moving);
            if (slot < 0) slot = 0;
            yield return Wait(4f);
            views.SetFreeCamera(true);
            var cam = views.OutputCamera.transform;
            float seconds = 10f;
            StartCoroutine(RecordRoutine(seconds));
            Vector3 smooth = Vector3.zero;
            for (float t = 0f; t < seconds; t += 1f / PitchClips.Fps)
            {
                session.Director.Pose(slot, session.RoundTime, out var p, out float yaw, out _);
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                float a = t / seconds;
                Vector3 want = p - fwd * Mathf.Lerp(38f, 20f, a) + side * Mathf.Lerp(18f, -10f, a) + Vector3.up * Mathf.Lerp(22f, 12f, a);
                smooth = t == 0f ? want : Vector3.Lerp(smooth, want, 0.12f);
                cam.SetPositionAndRotation(smooth, Quaternion.LookRotation(p + fwd * 6f + Vector3.up * 1.5f - smooth, Vector3.up));
                yield return null;
            }
            while (_recording) yield return null;
            views.SetFreeCamera(false);
        }

        // The pilot's own view with the HUD: the quad is flown in by an Elite strike bot's autopilot (camera only).
        private IEnumerator StrikeFpv(GameSettings s)
        {
            yield return LoadStrike(s, 2);
            var session = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
            var views = FindAnyObjectByType<ViewModeManager>();
            if (session == null || session.Bots.Count == 0 || views == null)
            {
                Finish("no strike bots");
                yield break;
            }
            Transform bot = null;
            foreach (Transform c in session.transform)
                if (c.name.StartsWith("Strike Bot ")) { bot = c; break; }
            yield return Wait(1.5f);
            views.SetFreeCamera(true);
            var cam = views.OutputCamera.transform;
            float seconds = 30f;
            var sim = session.Bots[0];
            float hitAt = -1f;
            StartCoroutine(RecordRoutine(seconds));
            Quaternion rot = bot.rotation;
            for (float t = 0f; t < seconds; t += 1f / PitchClips.Fps)
            {
                foreach (var r in bot.GetComponentsInChildren<Renderer>()) r.enabled = false; // the camera sits inside it
                if (hitAt < 0f && sim.Score.Hits > 0) hitAt = t;
                if (hitAt >= 0f && t - hitAt > 1.2f) { _stopRecording = true; break; }
                // A camera on the bot's nose, tilted up 25° like an FPV camera, smoothed a little.
                rot = Quaternion.Slerp(rot, bot.rotation * Quaternion.Euler(-25f, 0f, 0f), 0.35f);
                cam.SetPositionAndRotation(bot.position + bot.forward * 0.08f, rot);
                yield return null;
            }
            while (_recording) yield return null;
            views.SetFreeCamera(false);
        }

        // The results screen of a full round against 3 bots (time runs 6x to get there; one frame is kept).
        private IEnumerator StrikeResults(GameSettings s)
        {
            yield return LoadStrike(s, 3);
            var session = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
            if (session == null)
            {
                Finish("no strike session");
                yield break;
            }
            // 3x speed at 60 frames a second: 50 ms of simulation a frame, inside the bots' 64-step cap.
            Time.captureFramerate = 60;
            Time.timeScale = 3f;
            for (int i = 0; i < 20000 && session.Phase != Ushqyr.Strike.StrikePhase.Finished; i++) yield return null;
            Time.timeScale = 1f;
            yield return Wait(0.5f);
            yield return Record(1f / PitchClips.Fps);
        }

        // A still of a menu page (one frame).
        private IEnumerator MenuStill(string page, string venue)
        {
            if (SceneManager.GetActiveScene().name != GameFlow.MenuScene)
            {
                SceneManager.LoadScene(GameFlow.MenuScene);
                yield return null;
            }
            yield return Wait(1f);
            var menu = FindAnyObjectByType<Ushqyr.UI.MenuController>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            if (venue != null) typeof(Ushqyr.UI.MenuController).GetField("_venue", flags)?.SetValue(menu, venue);
            typeof(Ushqyr.UI.MenuController).GetMethod(page, flags)?.Invoke(menu, null);
            yield return Wait(0.5f);
            yield return Record(1f / PitchClips.Fps);
        }

        // ---------- showcase stills (4K) ----------

        public const int StillW = 3840, StillH = 2160;
        private Camera _stillCam;
        private RenderTexture _stillRt;
        private Texture2D _stillTex;
        private int _still;

        /// <summary>A camera of our own with the game camera's look (post, AA, volumes) and every layer but the VR room.</summary>
        private Camera StillCamera(Camera game)
        {
            if (_stillCam != null) return _stillCam;
            QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names, "Ultra"), true);
            var go = new GameObject("Still Camera");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(game);
            cam.cullingMask = ~0 & ~(1 << LayerMask.NameToLayer("VRRoom")) & ~(1 << LayerMask.NameToLayer("EditorUI"));
            cam.nearClipPlane = 0.03f;
            var src = game.GetComponent<UniversalAdditionalCameraData>();
            var dst = cam.GetUniversalAdditionalCameraData();
            if (src != null)
            {
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.volumeLayerMask = src.volumeLayerMask;
            }
            dst.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            dst.antialiasingQuality = AntialiasingQuality.High;
            cam.enabled = false;
            _stillRt = new RenderTexture(StillW, StillH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = _stillRt;
            cam.aspect = StillW / (float)StillH;
            _stillTex = new Texture2D(StillW, StillH, TextureFormat.RGB24, false);
            _stillCam = cam;
            return cam;
        }

        /// <summary>Renders one 4K still from <paramref name="pos"/> looking at <paramref name="look"/> (call after WaitForEndOfFrame).</summary>
        private void Still(Camera game, Vector3 pos, Vector3 look, float fov, string tag)
        {
            var cam = StillCamera(game);
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _stillRt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _stillRt;
            _stillTex.ReadPixels(new Rect(0, 0, StillW, StillH), 0, 0);
            _stillTex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_dir, $"s{_still++:00}_{tag}.png"), _stillTex.EncodeToPNG());
        }

        /// <summary>The Game view with its HUD at twice its size (3840x2160 from a 1080p view).</summary>
        private IEnumerator ScreenStill(string tag)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"s{_still++:00}_{tag}.png"), 2);
            yield return null;
            yield return null;
        }

        private void EndStills(string note)
        {
            if (_stillCam != null) Destroy(_stillCam.gameObject);
            if (_stillRt != null) Destroy(_stillRt);
            if (_stillTex != null) Destroy(_stillTex);
            Finish($"{_still} stills: {note}");
        }

        private static void ShowModel(Transform t)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            foreach (var tr in t.GetComponentsInChildren<Transform>(true))
                if (!tr.gameObject.activeSelf && tr.GetComponent<Renderer>() != null) tr.gameObject.SetActive(true);
        }

        // The player's drone in the pack: a low chase camera behind and to the side, the bots around and ahead.
        private IEnumerator ShotRaceChase(GameSettings s)
        {
            s.Feed.StadiumLighting = 2; // day: the drones read clearly against the track
            s.Race.BotCount = 5;
            s.Race.BotLevel = 2; // rookies keep pace with the autopilot, so the pack stays together
            s.Race.BotCollisions = true;
            yield return Load("stadium-oval");
            var views = FindAnyObjectByType<ViewModeManager>();
            HideHud();
            Ushqyr.EditorTools.SphereDistrict.CourseFlightTest.Attach(12f);
            var ctrl = FindAnyObjectByType<TrackSceneController>();
            var visual = typeof(TrackSceneController).GetField("quadVisual", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(ctrl) as Transform;
            var bots = FindAnyObjectByType<Ushqyr.AI.BotRace>();
            for (int i = 0; i < 300 && (bots == null || bots.Racers.Count < 2); i++)
            {
                yield return null;
                bots = FindAnyObjectByType<Ushqyr.AI.BotRace>();
            }
            if (visual == null || bots == null)
            {
                EndStills("no player or bots");
                yield break;
            }
            ShowModel(visual);
            var game = views.OutputCamera;
            var eof = new WaitForEndOfFrame();
            Time.captureFramerate = PitchClips.Fps;
            Vector3 last = visual.position, fwd = visual.forward;
            var race = FindAnyObjectByType<RaceSession>();
            // Wait for GO: then the pack leaves the grid together for a few seconds.
            for (int i = 0; i < 40 * PitchClips.Fps && race != null && race.Phase != RacePhase.Racing; i++) yield return null;
            int shots = 0;
            for (int frame = 0; frame < 5 * PitchClips.Fps; frame++)
            {
                yield return eof;
                Vector3 p = visual.position, d = p - last;
                last = p;
                if (d.sqrMagnitude > 1e-5f) fwd = Vector3.Slerp(fwd, d.normalized, 0.3f);
                if (frame % 4 != 0 || frame < 10) continue;
                Vector3 f = Flat(fwd), right = Vector3.Cross(Vector3.up, f);
                // The bots within 8 m: their centre sets the framing.
                Vector3 pack = Vector3.zero;
                int n = 0;
                foreach (var r in bots.Racers)
                    if (!r.IsPlayer && r.Visual != null && (r.Visual.position - p).magnitude < 8f) { pack += r.Visual.position; n++; }
                if (n == 0) continue;
                pack /= n;
                Vector3 mid = (p + pack) * 0.5f;
                // 1: low, just behind the player's drone, the pack ahead and beside it.
                Still(game, p - f * 0.7f + right * 0.18f + Vector3.up * 0.16f, mid + f * 1.5f, 58f, "chase");
                // 2: from the front-side, the player's drone leading the camera eye, bots behind it.
                Still(game, p + f * 1.6f + right * 1.1f + Vector3.up * 0.35f, Vector3.Lerp(p, pack, 0.35f), 50f, "front");
                // 3: a little higher behind the whole group.
                Still(game, mid - f * 3.2f + Vector3.up * 1.0f, mid + f * 2f, 45f, "pack");
                shots += 3;
            }
            Time.captureFramerate = 0;
            EndStills("race chase");
        }

        // The Expo district at golden hour from the air.
        private IEnumerator ShotExpo(GameSettings s)
        {
            s.Feed.ExpoLighting = 1;
            yield return Load("expo-v2-free");
            var views = FindAnyObjectByType<ViewModeManager>();
            var v = FindAnyObjectByType<QuadVehicle>();
            if (v != null) v.HoldAtSpawn = true;
            HideHud();
            yield return Wait(2f);
            var eof = new WaitForEndOfFrame();
            var sphere = new Vector3(0f, 45f, 0f);
            foreach (var (bearing, dist, height, fov) in new[] { (215f, 260f, 110f, 45f), (245f, 190f, 60f, 50f), (170f, 150f, 25f, 55f), (300f, 320f, 140f, 42f) })
            {
                yield return eof;
                Vector3 pos = Ushqyr.EditorTools.Builders.RouteLibrary.ExpoPoint(bearing, dist, height);
                Still(views.OutputCamera, pos, sphere, fov, "expo");
            }
            EndStills("expo");
        }

        // FPV with the OSD through the Expo Sprint gates.
        private IEnumerator ShotFpv(GameSettings s)
        {
            s.Feed.ExpoLighting = 2;
            s.Camera.ViewMode = (int)ViewMode.Fpv;
            QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names, "Ultra"), true);
            yield return Load("expo-v2-sprint");
            var views = FindAnyObjectByType<ViewModeManager>();
            if (views != null) views.Apply(ViewMode.Fpv);
            Ushqyr.EditorTools.SphereDistrict.CourseFlightTest.Attach(13f);
            yield return Wait(6f);
            for (int i = 0; i < 16; i++)
            {
                yield return ScreenStill("fpv");
                yield return Wait(1.2f);
            }
            EndStills("fpv");
        }

        // The Strike Range: an Elite bot's dive seen from behind, and its FPV view with the HUD, near the target.
        private IEnumerator ShotStrike(GameSettings s)
        {
            QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names, "Ultra"), true);
            yield return LoadStrike(s, 3);
            var session = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
            var views = FindAnyObjectByType<ViewModeManager>();
            if (session == null || session.Bots.Count == 0 || views == null)
            {
                EndStills("no strike bots");
                yield break;
            }
            var bot = session.Bots[0];
            Transform model = null;
            foreach (Transform c in session.transform)
                if (c.name.StartsWith("Strike Bot ")) { model = c; break; }
            var game = views.OutputCamera;
            var eof = new WaitForEndOfFrame();
            Time.captureFramerate = PitchClips.Fps;
            int shots = 0, hits = 0;
            for (int frame = 0; frame < 110 * PitchClips.Fps && shots < 30; frame++)
            {
                yield return eof;
                if (bot.Down || bot.Target < 0 || frame % 3 != 0) continue;
                session.Director.Pose(bot.Target, session.RoundTime, out var tp, out _, out _);
                Vector3 p = bot.Position, v = bot.Velocity;
                float dist = Vector3.Distance(p, tp);
                if (dist > 45f || dist < 8f || v.sqrMagnitude < 4f) continue;
                // Just behind the bot, looking past it at the target: the drone big in the lower third.
                Vector3 to = (tp + Vector3.up * 1.2f - p).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, Flat(to));
                Still(game, p - to * 0.75f + right * 0.22f + Vector3.up * 0.28f, tp + Vector3.up * 1f, 56f, "dive");
                shots++;
                if (bot.Score.Hits > hits) hits = bot.Score.Hits;
            }
            Time.captureFramerate = 0;
            // The HUD on the range: a camera over a convoy, scores and radar on screen.
            int slot = session.Director.Targets.FindIndex(t => t.Active && t.Plan.Class == Ushqyr.Strike.TargetClass.Moving);
            if (slot >= 0)
            {
                views.SetFreeCamera(true);
                var cam = views.OutputCamera.transform;
                for (int i = 0; i < 4; i++)
                {
                    session.Director.Pose(slot, session.RoundTime, out var tp, out float yaw, out _);
                    Vector3 fw = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, fw);
                    Vector3 at = tp - fw * 22f + side * (8f - i * 5f) + Vector3.up * 11f;
                    cam.SetPositionAndRotation(at, Quaternion.LookRotation(tp + fw * 6f - at, Vector3.up));
                    yield return null;
                    yield return ScreenStill("hud");
                    yield return Wait(0.8f);
                }
                views.SetFreeCamera(false);
            }
            EndStills("strike");
        }

        // The garage: a few classes of drone on the bench.
        private IEnumerator ShotGarage()
        {
            QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names, "Ultra"), true);
            SceneManager.LoadScene(GameFlow.GarageScene);
            yield return null;
            yield return Wait(4f);
            var gc = FindAnyObjectByType<Ushqyr.UI.GarageCamera>();
            var garage = FindAnyObjectByType<Ushqyr.UI.GarageController>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var yaw = typeof(Ushqyr.UI.GarageCamera).GetField("yaw", flags);
            var setBuild = typeof(Ushqyr.UI.GarageController).GetMethod("SetBuild", flags);
            float start = gc != null ? (float)yaw.GetValue(gc) : 0f;
            var builds = new[] { Ushqyr.Garage.QuadPresets.Race5, Ushqyr.Garage.QuadPresets.Freestyle5, Ushqyr.Garage.QuadPresets.Cinewhoop35 };
            foreach (var b in builds)
            {
                if (setBuild != null && garage != null) setBuild.Invoke(garage, new object[] { b, true });
                foreach (float a in new[] { 20f, 55f })
                {
                    if (gc != null) yaw.SetValue(gc, start + a);
                    yield return Wait(1.2f);
                    yield return ScreenStill("garage");
                }
            }
            EndStills("garage");
        }

        // A hit on a moving target, seen from 18 m: the flash, the sparks, then the burning wreck rolling off the road.
        private IEnumerator ShotHitFx(GameSettings s)
        {
            QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names, "Ultra"), true);
            yield return LoadStrike(s, 0);
            var session = FindAnyObjectByType<Ushqyr.Strike.StrikeSession>();
            var views = FindAnyObjectByType<ViewModeManager>();
            int slot = session.Director.Targets.FindIndex(t => t.Active && t.Plan.Class == Ushqyr.Strike.TargetClass.Moving);
            if (slot < 0) { EndStills("no moving target"); yield break; }
            yield return Wait(0.5f);
            session.Director.Pose(slot, session.RoundTime, out var tp, out float yaw, out _);
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, fwd);
            Vector3 cam = tp + side * 14f + fwd * 9f + Vector3.up * 5f;
            var game = views.OutputCamera;
            session.Director.Hit(slot, session.RoundTime, 0);
            var eof = new WaitForEndOfFrame();
            Time.captureFramerate = 60;
            foreach (int frames in new[] { 2, 6, 14, 30, 90, 200 })
            {
                for (int i = 0; i < frames; i++) yield return null;
                yield return eof;
                Still(game, cam, tp + Vector3.up * 1.5f + fwd * 2f, 50f, "hit");
            }
            Time.captureFramerate = 0;
            EndStills("hit fx");
        }

        // ---------- performance probe ----------

        /// <summary>FPV autopilot on a track at High, then 10 s of FrameStats (real time, no capture framerate).</summary>
        private IEnumerator PerfExpo(GameSettings s, string track)
        {
            s.Feed.ExpoLighting = 2;
            s.Camera.ViewMode = (int)ViewMode.Fpv;
            QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names, "High"), true);
            yield return Load(track);
            var views = FindAnyObjectByType<ViewModeManager>();
            if (views != null) views.Apply(ViewMode.Fpv);
            Ushqyr.EditorTools.SphereDistrict.CourseFlightTest.Attach(13f);
            Time.captureFramerate = 0;
            yield return new WaitForSecondsRealtime(4f);
            string file = Path.Combine(_dir, "stats.txt");
            yield return FrameStats.Sample(600, file);
            File.WriteAllText(Path.Combine(_dir, "f0000.jpg"), "");
            Finish(File.Exists(file) ? File.ReadAllText(file).Replace((char)10, (char)32) : "no stats");
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
            _stopRecording = false;
            for (int i = 0; i < frames && !_stopRecording; i++)
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
