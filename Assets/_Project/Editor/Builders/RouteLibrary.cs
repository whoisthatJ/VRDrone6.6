using System.Collections.Generic;
using System.IO;
using GateRush.Track;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Built-in spline tracks, defined in code like <see cref="CourseLibrary"/>: each becomes a JSON file in Data/Tracks
    /// (what the game loads) and a <see cref="TrackDefinition"/> asset in Data/TrackDefinitions (for editing and
    /// Tools > Track > Rebuild).
    /// </summary>
    public static class RouteLibrary
    {
        public const string DefinitionDir = "Assets/_Project/Data/TrackDefinitions";

        public static IEnumerable<TrackData> All()
        {
            yield return SplineTest();
        }

        /// <summary>Writes every built-in spline track's JSON and definition asset.</summary>
        public static List<string> WriteAll()
        {
            Directory.CreateDirectory(CourseLibrary.TrackDir);
            Directory.CreateDirectory(DefinitionDir);
            var written = new List<string>();
            foreach (var t in All())
            {
                string json = $"{CourseLibrary.TrackDir}/{t.Id}.json";
                File.WriteAllText(json, t.ToJson());
                AssetDatabase.ImportAsset(json, ImportAssetOptions.ForceSynchronousImport);

                string path = $"{DefinitionDir}/{t.Id}.asset";
                var def = AssetDatabase.LoadAssetAtPath<TrackDefinition>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<TrackDefinition>();
                    AssetDatabase.CreateAsset(def, path);
                }
                def.Track = t;
                def.Json = AssetDatabase.LoadAssetAtPath<TextAsset>(json);
                EditorUtility.SetDirty(def);
                written.Add(json);
            }
            AssetDatabase.SaveAssets();
            return written;
        }

        /// <summary>
        /// T1 test track in the Field: a figure-eight (lemniscate, ~315 m) whose second crossing goes over the first
        /// on a 2.6 m bridge, with red single gates, green hurdles and blue large gates; the finish is the white large
        /// gate just after the start pad.
        /// </summary>
        public static TrackData SplineTest()
        {
            var t = new TrackData
            {
                Id = "spline-test", Name = "Spline Test Loop", Author = "GateRush", Environment = "field", Laps = 2,
            };
            var r = t.Route;
            r.CorridorWidth = 4f;
            r.FlightHeight = 1.5f;
            const int knots = 16;
            for (int i = 0; i < knots; i++) r.Knots.Add(new RouteKnot { Position = TestLoop(i * Mathf.PI * 2f / knots) });

            var route = new TrackRoute(r);
            float At(float turns) => route.ClosestDistance(TestLoop(turns * Mathf.PI));

            void Add(string type, float turns, string color) =>
                r.Obstacles.Add(new RouteObstacle { Type = type, Distance = At(turns), Color = color });

            Add("Large", 0.12f, "white"); // finish
            Add("Square", 0.25f, "red");
            Add("Hurdle", 0.42f, "green");
            Add("Square", 0.62f, "red");
            Add("Large", 0.80f, "blue");
            Add("Square", 1.00f, "red");
            Add("Hurdle", 1.18f, "green");
            Add("Square", 1.35f, "red");
            Add("Large", 1.50f, "blue"); // on the bridge
            Add("Square", 1.70f, "red");
            Add("Hurdle", 1.88f, "green");
            r.FinishObstacle = 0;

            // Start pad on the centre line a few metres before the finish gate.
            var pad = route.Sample(At(0.12f) - 9f);
            var flat = pad.FlatForward;
            t.Spawn.Position = new Vector3(pad.Position.x, r.GroundHeight + 0.15f, pad.Position.z);
            t.Spawn.Yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            return t;
        }

        /// <summary>The test loop's flight line at parameter <paramref name="t"/> (radians): lemniscate of Bernoulli, raised around t = 3π/2.</summary>
        public static Vector3 TestLoop(float t)
        {
            const float a = 60f;
            float s = Mathf.Sin(t), c = Mathf.Cos(t);
            float den = 1f + s * s;
            float dt = Mathf.DeltaAngle(t * Mathf.Rad2Deg, 270f) * Mathf.Deg2Rad;
            float y = 1.5f + 2.6f * Mathf.Exp(-dt * dt / (2f * 0.42f * 0.42f));
            return new Vector3(a * c / den, y, a * s * c / den);
        }

        /// <summary>True if <paramref name="p"/> is within <paramref name="clearance"/> metres (horizontally) of a built-in spline route.</summary>
        public static bool NearAnyRoute(string environment, Vector3 p, float clearance)
        {
            foreach (var t in All())
            {
                if (t.Environment != environment) continue;
                var route = new TrackRoute(t.Route);
                route.ClosestDistance(p, -1f, 0f, horizontal: true);
                if (route.LastClosestSqr < clearance * clearance) return true;
            }
            return false;
        }
    }
}
