using System.IO;
using Ushqyr.EditorTools.Art;
using Ushqyr.EditorTools.Range;
using Ushqyr.Strike;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ushqyr.EditorTools.Capture
{
    /// <summary>
    /// Review shots of the range vehicles: opens Range_Steppe, parks one of each kind near the pads in the day light and
    /// renders them from a few angles into Recordings/lineup (the scene is not saved).
    /// </summary>
    public static class VehicleLineup
    {
        [MenuItem("Ushqyr/Strike/Render Vehicle Line-up")]
        public static string Render()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "lineup"));
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Range_Steppe.unity", OpenSceneMode.Single);
            var lighting = Object.FindAnyObjectByType<RangeLighting>();
            if (lighting != null) lighting.Apply(RangeWeather.Day);
            RangeVehicles.MaterialsOverride = RangeSteppeBuilder.VehicleMaterials();
            var root = new GameObject("Lineup").transform;
            var paints = new[] { RangeVehicles.Olive, RangeVehicles.Tan, new Color(0.5f, 0.45f, 0.34f), RangeVehicles.Olive, RangeVehicles.Tan };
            float z = RangeSteppeBuilder.PadZ + 70f;
            var kinds = (VehicleKind[])System.Enum.GetValues(typeof(VehicleKind));
            var cam = GameObject.Find("Main Camera")?.GetComponent<Camera>();
            int n = 0;
            try
            {
                for (int i = 0; i < kinds.Length; i++)
                {
                    float x = -40f + i * 20f;
                    var go = RangeVehicles.Build(kinds[i], root, 0, paints[i % paints.Length]);
                    float y = RangeSteppeBuilder.Height(x, z);
                    go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, 35f, 0f));
                    Vector3 c = new Vector3(x, y + 1.4f, z);
                    foreach (var (dir3, dist, fov) in new[] { (new Vector3(1.2f, 0.55f, 1.4f), 10f, 40f), (new Vector3(-1.4f, 0.35f, -0.9f), 9f, 40f) })
                    {
                        Vector3 pos = c + dir3.normalized * dist;
                        var shot = SceneShots.Render(cam, new SceneShots.Bookmark(kinds[i].ToString(), pos, c, fov));
                        File.WriteAllBytes(Path.Combine(dir, $"{n++:00}_{kinds[i]}.png"), shot.EncodeToPNG());
                        Object.DestroyImmediate(shot);
                    }
                }
                // All five together, from above the pads.
                Vector3 mid = new Vector3(0f, RangeSteppeBuilder.Height(0f, z) + 1f, z);
                var all = SceneShots.Render(cam, new SceneShots.Bookmark("all", mid + new Vector3(8f, 14f, -34f), mid, 38f));
                File.WriteAllBytes(Path.Combine(dir, $"{n++:00}_all.png"), all.EncodeToPNG());
                Object.DestroyImmediate(all);
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
            return $"{n} shots in {dir}";
        }

        /// <summary>Range views from drone height, with a convoy and a parked vehicle on the ground (Recordings/range).</summary>
        [MenuItem("Ushqyr/Strike/Render Range Views")]
        public static string RenderRange()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "range"));
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Range_Steppe.unity", OpenSceneMode.Single);
            var lighting = Object.FindAnyObjectByType<RangeLighting>();
            if (lighting != null) lighting.Apply(RangeWeather.Day);
            RangeVehicles.MaterialsOverride = RangeSteppeBuilder.VehicleMaterials();
            var session = Object.FindAnyObjectByType<StrikeSession>();
            var layout = session.Layout;
            var director = new RangeDirector(layout, 4242, RangeDifficulty.Hard);
            var root = new GameObject("Targets").transform;
            for (int i = 0; i < director.Targets.Count; i++)
            {
                var v = RangeTargetView.Create(root, director, i, 0);
                v.Sync(30f);
            }
            var cam = GameObject.Find("Main Camera")?.GetComponent<Camera>();
            int n = 0;
            try
            {
                // A moving target to frame the low shots on.
                int slot = director.Targets.FindIndex(t => t.Plan.Class == TargetClass.Moving);
                director.Pose(slot, 30f, out var tp, out float yaw, out _);
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, fwd);
                var shots = new[]
                {
                    ("convoy_low", tp - fwd * 30f + side * 12f + Vector3.up * 9f, tp + Vector3.up * 1.5f, 45f),
                    ("convoy_dive", tp - fwd * 45f + Vector3.up * 30f, tp + fwd * 5f, 55f),
                    ("range_wide", new Vector3(0f, 45f, RangeSteppeBuilder.PadZ - 10f), new Vector3(0f, 0f, 0f), 60f),
                    ("ground_level", tp + side * 25f + Vector3.up * 2.2f, tp + Vector3.up * 1.2f, 50f),
                };
                var list = new System.Collections.Generic.List<(string, Vector3, Vector3, float)>(shots);
                list.Add(("pads", new Vector3(22f, 7f, RangeSteppeBuilder.PadZ + 34f), new Vector3(0f, 2f, RangeSteppeBuilder.PadZ - 15f), 60f));
                list.Add(("trees", new Vector3(-130f, 14f, 90f), new Vector3(-60f, 4f, 200f), 55f));
                // Earthworks (known only right after a rebuild): a free-standing bank and a revetment.
                foreach (var m in RangeSteppeBuilder.Mounds)
                {
                    bool bank = m.Seed >= 1000;
                    if (list.Exists(s => s.Item1 == (bank ? "berm" : "revetment"))) continue;
                    var c = new Vector3(m.Centre.x, RangeSteppeBuilder.Height(m.Centre.x, m.Centre.y), m.Centre.y);
                    float yr = (m.Yaw + 60f) * Mathf.Deg2Rad;
                    var off = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
                    list.Add((bank ? "berm" : "revetment", c + off * 26f + Vector3.up * 7f, c + Vector3.up * 0.8f, 50f));
                }
                foreach (var (name, pos, look, fov) in list)
                {
                    var p = pos;
                    p.y = Mathf.Max(p.y, RangeSteppeBuilder.Height(p.x, p.z) + 1.5f);
                    var shot = SceneShots.Render(cam, new SceneShots.Bookmark(name, p, look, fov));
                    File.WriteAllBytes(Path.Combine(dir, $"{n++:00}_{name}.png"), shot.EncodeToPNG());
                    Object.DestroyImmediate(shot);
                }
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
            return $"{n} shots in {dir}";
        }
    }
}
