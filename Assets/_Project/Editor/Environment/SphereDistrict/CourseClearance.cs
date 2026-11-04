using System.Collections.Generic;
using System.Text;
using GateRush.Track;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GateRush.EditorTools.SphereDistrict
{
    /// <summary>
    /// E6 course check: samples a route's flight line every metre in its environment scene and reports every stretch
    /// where a sphere of <c>radius</c> touches scenery colliders (buildings, canopy, lamps, cars, city cells) or the
    /// line drops near the ground. Courses through the Sphere District are tuned until this comes back clean.
    /// </summary>
    public static class CourseClearance
    {
        /// <param name="ignorePrefix">Collider names to skip (e.g. "SD_PropColliders" before a rebuild moves the props).</param>
        public static string Check(TrackData data, float radius = 2f, float minHeight = 1.2f, string ignorePrefix = null)
        {
            string scene = Builders.TrackSceneBuilder.ScenePath(data.Environment);
            if (EditorSceneManager.GetActiveScene().path != scene) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            Physics.SyncTransforms();
            int mask = LayerMask.GetMask("TrackGeometry");
            var route = new TrackRoute(data.Route);
            var sb = new StringBuilder($"{data.Id}: {route.Length:F0} m, {data.Route.Obstacles.Count} obstacles.");
            var hits = new Collider[16];
            string open = null;
            float openFrom = 0f;
            var names = new HashSet<string>();
            int problems = 0;
            float maxY = 0f;
            for (float d = 0f; d <= route.Length; d += 1f)
            {
                var p = route.Sample(d).Position;
                maxY = Mathf.Max(maxY, p.y);
                int n = Physics.OverlapSphereNonAlloc(p, radius, hits, mask, QueryTriggerInteraction.Ignore);
                string what = p.y < minHeight ? "too low" : null;
                for (int i = 0; i < n; i++)
                {
                    // The ground is checked by height (the start straight flies 1.8 m over it).
                    string name = hits[i].name;
                    if (name == "Ground" || (ignorePrefix != null && name.StartsWith(ignorePrefix))) continue;
                    what ??= name;
                    names.Add(name);
                }
                if (what != null)
                {
                    names.Add(what);
                    if (open == null)
                    {
                        open = what;
                        openFrom = d;
                    }
                }
                else if (open != null)
                {
                    sb.Append($"\n  {openFrom:F0}-{d:F0} m at {Where(route.Sample(openFrom).Position)}: {string.Join(", ", names)}");
                    open = null;
                    names.Clear();
                    problems++;
                }
            }
            if (open != null)
            {
                sb.Append($"\n  {openFrom:F0}-end: {string.Join(", ", names)}");
                problems++;
            }
            sb.Append(problems == 0 ? " Clear." : $"\n  {problems} blocked stretches.");
            sb.Append($" Top {maxY:F0} m.");
            return sb.ToString();
        }

        /// <summary>"bearing/distance/height" from the sphere, the units the course knots are written in.</summary>
        public static string Where(Vector3 p)
        {
            float bearing = Mathf.Repeat(Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg, 360f);
            return $"({bearing:F0}°, {new Vector2(p.x, p.z).magnitude:F0} m, y {p.y:F1})";
        }
    }
}
