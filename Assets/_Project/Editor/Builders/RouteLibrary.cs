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
            yield return StadiumOval();
            yield return SerpentArena();
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

            Add("Arch", 0.12f, "white"); // finish
            Add("Single", 0.25f, "red");
            Add("Ladder", 0.42f, "green");
            Add("Cube", 0.62f, "green");
            Add("Double", 0.80f, "red");
            Add("Single", 1.00f, "red");
            Add("Slalom", 1.20f, "white");
            Add("Single", 1.36f, "red");
            Add("Single", 1.50f, "blue"); // on the bridge
            Add("Dive", 1.64f, "blue"); // dive off the bridge
            Add("Ladder", 1.88f, "green");
            r.FinishObstacle = 0;

            // Chevron boards on the outside of the two tight ends, arrows pointing into the turn.
            foreach (float turns in new[] { 0f, 1f })
            {
                float d = At(turns) - 10f;
                var p = route.Sample(d);
                var ahead = route.Sample(d + 8f);
                bool turnsLeft = Vector3.Cross(p.FlatForward, ahead.FlatForward).y < 0f;
                r.Obstacles.Add(new RouteObstacle
                {
                    Type = turnsLeft ? "ChevronBoardLeft" : "ChevronBoard", Distance = d + 12f, Lateral = turnsLeft ? 4.5f : -4.5f, IsCheckpoint = false,
                });
            }

            // Start pad beside the route, 6 m to the left, a dozen metres before the finish arch.
            var pad = route.Sample(At(0.12f) - 12f);
            var flat = pad.FlatForward;
            Vector3 padPos = pad.Position - pad.Right * 6f;
            t.Spawn.Position = new Vector3(padPos.x, r.GroundHeight + 0.15f, padPos.z);
            t.Spawn.Yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            foreach (float side in new[] { -4.2f, 4.2f })
                t.Props.Add(new PropData { Type = "Flag", Position = new Vector3(padPos.x, 0f, padPos.z) - pad.Right * 1.5f + flat * side, Euler = new Vector3(0f, t.Spawn.Yaw, 0f) });
            // Landing pads in the middle of both loops.
            foreach (float x in new[] { -36f, 36f })
                t.Props.Add(new PropData { Type = "LandingPad", Position = new Vector3(x, 0f, 0f) });
            return t;
        }

        /// <summary>
        /// Stadium beginner track (T3): an oval along the middle of the 400 m running track (straights 84.4 m, radius
        /// 41 m, ~430 m), flown anticlockwise, with kit gates on the straights and bends and the start pad on the outer lanes.
        /// </summary>
        public static TrackData StadiumOval()
        {
            var t = new TrackData { Id = "stadium-oval", Name = "Stadium Oval", Author = "GateRush", Environment = "stadium", Laps = 3 };
            var r = t.Route;
            r.CorridorWidth = 4f;
            r.FlightHeight = 1.5f;
            const float half = 42.195f, radius = 41.38f;
            // Knots around the oval, anticlockwise seen from above (start on the south straight heading +X).
            foreach (var p in OvalPoints(half, radius, 20)) r.Knots.Add(new RouteKnot { Position = new Vector3(p.x, 1.5f, p.y) });
            var route = new TrackRoute(r);
            float D(float x, float z) => route.ClosestDistance(new Vector3(x, 1.5f, z));

            void Add(string type, float d, string color, bool checkpoint = true, float lateral = 0f) =>
                r.Obstacles.Add(new RouteObstacle { Type = type, Distance = d, Color = color, IsCheckpoint = checkpoint, Lateral = lateral });

            // Narrow gates (Single, Cube, Double) sit where the line is straight; the bends get ladders and wide arches.
            Add("Arch", D(0f, -radius), "white"); // finish on the south straight
            Add("Double", D(30f, -radius), "red");
            Add("Ladder", D(half + radius * 0.7f, -radius * 0.7f), "green");
            Add("Arch", D(half + radius, 0f), "green");
            Add("Single", D(half - 2f, radius), "red"); // bend exit
            Add("Slalom", D(10f, radius), "white");
            Add("Cube", D(-30f, radius), "green");
            Add("Single", D(-half + 2f, radius), "blue"); // bend entry
            Add("Arch", D(-half - radius, 0f), "blue");
            Add("Ladder", D(-half - radius * 0.7f, -radius * 0.7f), "green");
            Add("Single", D(-30f, -radius), "red");
            r.FinishObstacle = 0;

            // Start pad on the outer lanes of the south straight, 12 m before the finish arch, facing along it.
            float dPad = r.Obstacles[0].Distance - 12f;
            var pad = route.Sample(dPad);
            Vector3 padPos = pad.Position + pad.Right * 4.6f;
            t.Spawn.Position = new Vector3(padPos.x, r.GroundHeight + 0.15f, padPos.z);
            t.Spawn.Yaw = Mathf.Atan2(pad.FlatForward.x, pad.FlatForward.z) * Mathf.Rad2Deg;
            foreach (float side in new[] { -4.2f, 4.2f })
                t.Props.Add(new PropData { Type = "Flag", Position = new Vector3(padPos.x, 0f, padPos.z) + pad.Right * 1.2f + pad.FlatForward * side, Euler = new Vector3(0f, t.Spawn.Yaw, 0f) });
            return t;
        }

        /// <summary>
        /// Serpent Arena (T4) centre line, traced from Docs/reference/ref_track_stadium.png: the chevron line was picked in
        /// picture pixels and mapped to pitch metres with a homography fitted on the two goal areas
        /// (Docs/reference/trace_serpent.py), then z × 0.85 to keep the top bends inside the LED boards. Flying order:
        /// start straight east, white-pad loop, back west through the centre, left S, the south bridge over it, the
        /// bottom sweep to the cube, the climb over the centre crossing, the pad-2 loop, the top loop, west along the top,
        /// down the north bridge over the centre straight, the pad-1 loop and under the climb back to the start.
        /// y = 4 m on bridges, 1.5 m elsewhere.
        /// </summary>
        public static readonly Vector3[] SerpentKnots =
        {
            new Vector3(-0.7f, 1.5f, -17.0f), new Vector3(3.2f, 1.5f, -21.6f), new Vector3(9.0f, 1.5f, -25.7f), new Vector3(17.4f, 1.5f, -26.1f), new Vector3(25.9f, 1.5f, -22.9f), new Vector3(32.4f, 1.5f, -17.4f),
            new Vector3(34.6f, 1.5f, -11.9f), new Vector3(33.1f, 1.5f, -9.2f), new Vector3(25.9f, 1.5f, -9.9f), new Vector3(19.0f, 1.5f, -13.1f), new Vector3(14.8f, 1.5f, -16.6f), new Vector3(8.8f, 1.5f, -16.8f),
            new Vector3(5.0f, 1.5f, -13.1f), new Vector3(2.8f, 1.5f, -7.6f), new Vector3(1.5f, 1.5f, -3.2f), new Vector3(0.1f, 1.5f, 0.2f), new Vector3(-3.5f, 1.5f, 4.2f), new Vector3(-7.5f, 1.5f, 9.4f),
            new Vector3(-12.1f, 1.5f, 12.7f), new Vector3(-17.1f, 1.5f, 14.1f), new Vector3(-21.6f, 1.5f, 14.2f), new Vector3(-26.0f, 1.5f, 13.6f), new Vector3(-29.9f, 1.5f, 12.7f), new Vector3(-33.0f, 1.5f, 12.2f),
            new Vector3(-35.7f, 1.5f, 13.2f), new Vector3(-38.0f, 1.5f, 15.4f), new Vector3(-39.0f, 1.5f, 19.2f), new Vector3(-38.0f, 1.5f, 24.1f), new Vector3(-34.6f, 1.5f, 29.1f), new Vector3(-29.4f, 1.5f, 31.9f),
            new Vector3(-24.0f, 1.5f, 32.1f), new Vector3(-19.7f, 1.5f, 30.3f), new Vector3(-20.3f, 4.0f, 25.2f), new Vector3(-24.7f, 4.0f, 20.4f), new Vector3(-29.2f, 4.0f, 16.0f), new Vector3(-33.1f, 4.0f, 11.9f),
            new Vector3(-36.2f, 4.0f, 6.5f), new Vector3(-38.2f, 1.5f, 0.8f), new Vector3(-38.4f, 1.5f, -4.3f), new Vector3(-37.1f, 1.5f, -9.1f), new Vector3(-33.6f, 1.5f, -13.5f), new Vector3(-28.1f, 1.5f, -17.2f),
            new Vector3(-22.1f, 1.5f, -19.4f), new Vector3(-16.5f, 1.5f, -19.7f), new Vector3(-11.4f, 1.5f, -17.5f), new Vector3(-6.3f, 4.0f, -13.4f), new Vector3(-1.6f, 4.0f, -8.1f), new Vector3(0.8f, 4.0f, -2.2f),
            new Vector3(2.8f, 4.0f, 2.7f), new Vector3(5.7f, 1.5f, 6.6f), new Vector3(10.3f, 1.5f, 8.4f), new Vector3(15.0f, 1.5f, 5.2f), new Vector3(16.3f, 1.5f, -0.9f), new Vector3(19.5f, 1.5f, -5.4f),
            new Vector3(23.5f, 1.5f, -4.4f), new Vector3(28.5f, 1.5f, -0.8f), new Vector3(32.0f, 1.5f, 2.9f), new Vector3(30.9f, 1.5f, 7.8f), new Vector3(31.8f, 1.5f, 13.0f), new Vector3(38.3f, 1.5f, 17.8f),
            new Vector3(40.7f, 1.5f, 24.4f), new Vector3(37.8f, 1.5f, 30.3f), new Vector3(31.0f, 1.5f, 30.9f), new Vector3(24.6f, 1.5f, 25.9f), new Vector3(22.1f, 1.5f, 20.7f), new Vector3(15.4f, 1.5f, 23.6f),
            new Vector3(10.9f, 1.5f, 28.6f), new Vector3(4.4f, 1.5f, 29.9f), new Vector3(-1.7f, 4.0f, 28.6f), new Vector3(-9.3f, 4.0f, 24.0f), new Vector3(-15.9f, 4.0f, 19.4f), new Vector3(-21.3f, 4.0f, 15.2f),
            new Vector3(-25.9f, 4.0f, 11.5f), new Vector3(-27.3f, 1.5f, 6.4f), new Vector3(-27.6f, 1.5f, 1.1f), new Vector3(-27.3f, 1.5f, -2.9f), new Vector3(-24.5f, 1.5f, -6.2f), new Vector3(-19.8f, 1.5f, -8.0f),
            new Vector3(-14.8f, 1.5f, -7.7f), new Vector3(-10.9f, 1.5f, -7.7f), new Vector3(-7.6f, 1.5f, -10.6f), new Vector3(-4.1f, 1.5f, -13.8f),
        };

        /// <summary>
        /// The stadium race track (T4): one closed ~470 m route over the whole pitch with four overpasses, two landing-pad
        /// loops, a figure-eight crossing at the centre spot, a tight S at the west end and a loop at the north-east end.
        /// 22 obstacles; the finish is an arch until the finish cube (T6).
        /// </summary>
        public static TrackData SerpentArena()
        {
            var t = new TrackData { Id = "serpent-arena", Name = "Serpent Arena", Author = "GateRush", Environment = "stadium", Laps = 3 };
            var r = t.Route;
            r.CorridorWidth = 4f;
            r.FlightHeight = 1.5f;
            foreach (var k in SerpentKnots) r.Knots.Add(new RouteKnot { Position = k });
            var route = new TrackRoute(r);
            // Obstacles sit on knots (never on a crossing, where the closest point would be ambiguous).
            float At(int knot) => route.ClosestDistance(SerpentKnots[knot]);
            void Add(string type, int knot, string color) =>
                r.Obstacles.Add(new RouteObstacle { Type = type, Distance = At(knot), Color = color, IsCheckpoint = true });

            Add("Arch", 3, "white");      // finish, beside the start pad
            Add("Ladder", 5, "green");    // white-pad loop
            Add("Single", 8, "red");
            Add("Single", 12, "red");
            Add("Double", 16, "red");     // centre straight, heading west
            Add("Single", 28, "red");     // left S, after the hairpin
            Add("Single", 30, "red");
            Add("Single", 34, "blue");    // on the south bridge
            Add("Dive", 36, "blue");      // dive off it
            Add("Single", 38, "red");
            Add("Ladder", 41, "green");   // bottom sweep
            Add("Single", 43, "red");
            Add("Cube", 44, "green");     // the cube before the climb
            Add("Ladder", 50, "green");   // pad-2 loop
            Add("Single", 56, "red");
            Add("Ladder", 58, "green");   // north-east loop
            Add("Single", 60, "red");
            Add("Ladder", 64, "green");
            Add("Slalom", 66, "white");   // top straight
            Add("Single", 69, "blue");    // on the north bridge
            Add("Dive", 72, "blue");      // dive off it into the pad-1 loop
            Add("Ladder", 75, "green");
            r.Obstacles.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            r.FinishObstacle = r.Obstacles.FindIndex(o => o.Type == "Arch");

            // Start pad south of the start straight, 12 m before the finish arch (TrackBuilder lays the start grid at the spawn).
            var pad = route.Sample(At(3) - 12f);
            Vector3 padPos = pad.Position + pad.Right * 4.5f;
            t.Spawn.Position = new Vector3(padPos.x, r.GroundHeight + 0.15f, padPos.z);
            t.Spawn.Yaw = Mathf.Atan2(pad.FlatForward.x, pad.FlatForward.z) * Mathf.Rad2Deg;
            foreach (float side in new[] { -4.2f, 4.2f })
                t.Props.Add(new PropData { Type = "Flag", Position = new Vector3(padPos.x, 0f, padPos.z) + pad.Right * 1.4f + pad.FlatForward * side, Euler = new Vector3(0f, t.Spawn.Yaw, 0f) });
            // Landing pads inside the two big loops and the white-pad loop (where the reference has them). 1 mm up: the
            // pad's print disc is 0.03 m high, exactly the top of the stadium pitch, and would z-fight with it.
            foreach (var p in new[] { new Vector3(-16f, 0.001f, 3f), new Vector3(18.4f, 0.001f, 3f), new Vector3(25.9f, 0.001f, -12.7f) })
                t.Props.Add(new PropData { Type = "LandingPad", Position = p });
            return t;
        }

        /// <summary>Points on a stadium-shaped oval (two straights of ±<paramref name="half"/> and semicircles of <paramref name="radius"/>), anticlockwise from (−half, −radius).</summary>
        public static List<Vector2> OvalPoints(float half, float radius, int perArc)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < 4; i++) pts.Add(new Vector2(-half + i * half * 2f / 4f, -radius));
            for (int i = 0; i < perArc; i++)
            {
                float a = -Mathf.PI / 2 + i * Mathf.PI / perArc;
                pts.Add(new Vector2(half + Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
            }
            for (int i = 0; i < 4; i++) pts.Add(new Vector2(half - i * half * 2f / 4f, radius));
            for (int i = 0; i < perArc; i++)
            {
                float a = Mathf.PI / 2 + i * Mathf.PI / perArc;
                pts.Add(new Vector2(-half + Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
            }
            return pts;
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
