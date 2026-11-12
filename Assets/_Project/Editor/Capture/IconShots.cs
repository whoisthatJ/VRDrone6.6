using System.IO;
using System.Reflection;
using Ushqyr.Garage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ushqyr.EditorTools.Capture
{
    /// <summary>
    /// Renders the garage's shipped drones on a transparent background, in the garage's own light, as source art
    /// for the game icon (Recordings/icon: one PNG per build and view).
    /// </summary>
    public static class IconShots
    {
        private const int Layer = 31, Size = 1024;

        [MenuItem("Ushqyr/Capture/Render Icon Drones")]
        public static string Render()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "icon"));
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Garage.unity", OpenSceneMode.Single);
            var garage = Object.FindAnyObjectByType<Ushqyr.UI.GarageController>();
            var mats = (QuadModelMaterials)typeof(Ushqyr.UI.GarageController)
                .GetField("materials", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(garage);

            var turntable = (Transform)typeof(Ushqyr.UI.GarageController)
                .GetField("turntable", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(garage);
            var camGo = new GameObject("Icon Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 24f;
            cam.cullingMask = 1 << Layer;
            cam.allowHDR = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            // No volumes: screen-space reflections would mirror the flat backdrop onto the drone and wash it out.
            data.volumeLayerMask = 0;

            // A key light of our own as well, so the top of the drone is never left dark by the room's lamps.
            var keyGo = new GameObject("Icon Key") { hideFlags = HideFlags.HideAndDontSave };
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.color = new Color(1f, 0.97f, 0.92f);
            key.cullingMask = 1 << Layer;
            key.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            var views = new (string name, Vector3 dir)[]
            {
                ("hero", new Vector3(-0.55f, -0.62f, 0.56f)),   // three-quarter from the front, above
                ("top", new Vector3(0f, -1f, 0.02f)),
                ("front", new Vector3(0f, -0.28f, -0.96f)),     // nose on, a little above
            };
            int n = 0;
            var rt = new RenderTexture(Size * 2, Size * 2, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 }; // supersampled instead: 2x size, scaled down later
            try
            {
                foreach (var build in QuadPresets.All)
                {
                    var stage = new GameObject("Icon Stage").transform;
                    // On the garage turntable: inside the room's reflection probe and under its lamps (only the drone's
                    // layer is drawn, so the room itself stays out of the picture).
                    stage.position = turntable != null ? turntable.position + Vector3.up * 0.25f : new Vector3(0f, 1.2f, 0f);
                    var model = QuadModelBuilder.Build(build, mats, stage, Layer);
                    foreach (var t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                    var bounds = QuadModelBuilder.Bounds(model.Root);
                    float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    foreach (var (name, d) in views)
                    {
                        Vector3 dirN = d.normalized;
                        float dist = size * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
                        cam.transform.SetPositionAndRotation(bounds.center - dirN * dist, Quaternion.LookRotation(dirN, name == "top" ? Vector3.forward : Vector3.up));
                        cam.nearClipPlane = dist * 0.05f;
                        cam.farClipPlane = dist * 4f;
                        cam.targetTexture = rt;
                        // The alpha a camera leaves behind is not coverage (materials write their own), so render on
                        // black and on white: the cut-out is worked out from the difference (see Tools/Icon/make_icon.py).
                        foreach (var (suffix, back) in new[] { ("k", Color.black), ("w", Color.white) })
                        {
                            cam.backgroundColor = back;
                            cam.Render();
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                            tex.Apply();
                            RenderTexture.active = prev;
                            File.WriteAllBytes(Path.Combine(dir, $"{build.Id.Replace("preset-", "")}_{name}_{suffix}.png"), tex.EncodeToPNG());
                            Object.DestroyImmediate(tex);
                        }
                        n++;
                    }
                    Object.DestroyImmediate(stage.gameObject);
                }
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(keyGo);
            }
            return $"{n} renders in {dir}";
        }

        /// <summary>
        /// The 5" Race drone flying in the stadium, for the icon: per time of day and camera, the stadium itself
        /// (Recordings/icon/stadium_*_bg.png, through the game's own renderer) and the drone alone from the same camera
        /// on black and on white (stadium_*_k/w.png), lit by the stadium's lights.
        /// </summary>
        [MenuItem("Ushqyr/Capture/Render Icon Stadium")]
        public static string RenderStadium()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "icon"));
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Track_Stadium.unity", OpenSceneMode.Single);
            var scene = Object.FindAnyObjectByType<Ushqyr.Game.TrackSceneController>();
            var mats = (QuadModelMaterials)typeof(Ushqyr.Game.TrackSceneController)
                .GetField("buildMaterials", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(scene);
            var lighting = Object.FindAnyObjectByType<Ushqyr.Core.StadiumLighting>();
            var source = GameObject.Find("Main Camera")?.GetComponent<Camera>();

            var camGo = new GameObject("Icon Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 1 << Layer;
            cam.allowHDR = false;
            cam.aspect = 16f / 9f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.volumeLayerMask = 0;

            // Where it flies (over the pitch, banking into a turn) and the cameras: three-quarter from the front and
            // a little above, low enough that the stands and floodlights are behind it; it banks towards the camera.
            var spots = new (string name, Vector3 at, float yaw, Vector3 view)[]
            {
                ("a", new Vector3(0f, 5f, -14f), 25f, new Vector3(0.42f, -0.13f, 0.9f)),
                ("b", new Vector3(-20f, 4.5f, -6f), 60f, new Vector3(0.74f, -0.1f, 0.66f)),
                ("c", new Vector3(-40f, 5f, 4f), 95f, new Vector3(0.97f, -0.12f, 0.2f)),
            };
            int n = 0;
            var rt = new RenderTexture(3840, 2160, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                foreach (var time in new[] { Ushqyr.Core.TimeOfDay.Night, Ushqyr.Core.TimeOfDay.Dusk, Ushqyr.Core.TimeOfDay.Day })
                {
                    if (lighting != null) lighting.Apply(time);
                    foreach (var (name, at, yaw, view) in spots)
                    {
                        var stage = new GameObject("Icon Stage").transform;
                        stage.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(24f, 0f, -24f));
                        var model = QuadModelBuilder.Build(QuadPresets.Race5, mats, stage, Layer);
                        foreach (var t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                        var bounds = QuadModelBuilder.Bounds(model.Root);
                        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                        const float fov = 34f;
                        Vector3 d = view.normalized;
                        float dist = size * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 1.2f;
                        Vector3 pos = bounds.center - d * dist;
                        string tag = $"stadium_{time.ToString().ToLowerInvariant()}_{name}";

                        cam.fieldOfView = fov;
                        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(d, Vector3.up));
                        cam.nearClipPlane = dist * 0.05f;
                        cam.farClipPlane = dist * 6f;
                        cam.targetTexture = rt;
                        foreach (var (suffix, back) in new[] { ("k", Color.black), ("w", Color.white) })
                        {
                            cam.backgroundColor = back;
                            cam.Render();
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                            tex.Apply();
                            RenderTexture.active = prev;
                            File.WriteAllBytes(Path.Combine(dir, $"{tag}_{suffix}.png"), tex.EncodeToPNG());
                            Object.DestroyImmediate(tex);
                        }
                        cam.targetTexture = null;
                        Object.DestroyImmediate(stage.gameObject);

                        // The stadium behind it, as the game draws it (post-processing and all), from the same camera.
                        var shot = Ushqyr.EditorTools.Art.SceneShots.Render(source, new Ushqyr.EditorTools.Art.SceneShots.Bookmark(tag, pos, pos + d * 10f, fov));
                        File.WriteAllBytes(Path.Combine(dir, $"{tag}_bg.png"), shot.EncodeToPNG());
                        Object.DestroyImmediate(shot);
                        n++;
                    }
                }
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
            }
            return $"{n} stadium sets in {dir}";
        }
    }
}
