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
            yield return ExpoSprint();
            yield return ExpoPro();
            yield return ExpoFreeFlight();
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
        /// 22 obstacles; the finish is the finish cube (T6).
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

            Add("Finish", 3, "");         // the finish cube (T6), beside the start pad
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
            r.FinishObstacle = r.Obstacles.FindIndex(o => o.Type == "Finish");

            // Start pad south of the start straight, 12 m before the finish cube (TrackBuilder lays the start grid at the spawn).
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

        // ===================== Sphere District courses (E6) =====================
        // Knots are written as (bearing clockwise from north, distance from the sphere, height). Air courses: no lanes,
        // gates on the ground where the line is low, floating "Air" gates where it climbs, a beacon on the next gate.
        // Clearances (CourseClearance, 2 m sphere): the south walkway gap is 190-198 (between the 160 and 226 pavilions),
        // pod gaps 118-138 and 184-203 (r 70-118), the canopy covers 71-249 at r 52-93 and 6.5 m (fly at 3 m under it or
        // 12+ m over it), pavilions are 24 m high (hops cross the end wings at 31 m), courtyards 165-211 m out.

        /// <summary>Lanes on the south walkway: northbound into the finish cube, southbound on the way back.</summary>
        public const float WalkNorth = 196.4f, WalkSouth = 192.6f;

        private static Vector3 B(float bearing, float distance, float y) => ExpoPoint(bearing, distance, y);

        private static void Arc(List<Vector3> k, float from, float to, float step, float r0, float r1, float y0, float y1)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(to - from) / step));
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                k.Add(B(Mathf.Lerp(from, to, t), Mathf.Lerp(r0, r1, t), Mathf.Lerp(y0, y1, t)));
            }
        }

        /// <summary>Start: north up the walkway through the finish cube, hop over the west wing of the 160 pavilion,
        /// fly the length of its courtyard, climb out over the inner wing and drop under the canopy through the
        /// 118-138 pod gap onto the orbit (61 m, 3 m high).</summary>
        private static List<Vector3> ExpoOpening()
        {
            return new List<Vector3>
            {
                B(WalkNorth, 300f, 1.8f),   // 0
                B(WalkNorth, 262f, 1.8f),   // 1 finish cube
                B(WalkNorth, 232f, 2.2f),   // 2
                B(195.5f, 212f, 6f),        // 3
                B(193f, 199f, 18f),         // 4 climbing at the courtyard's west end
                B(188f, 192f, 31f),         // 5 over the west wing
                B(181f, 188f, 22f),         // 6 dropping in
                B(172f, 187f, 10f),         // 7 courtyard
                B(160f, 187f, 7f),          // 8
                B(149f, 186f, 9f),          // 9
                B(141f, 182f, 21f),         // 10 climbing out
                B(135f, 158f, 31f),         // 11 over the inner wing
                B(129f, 125f, 15f),         // 12 dropping between the pods
                B(127f, 100f, 4f),          // 13 under the canopy edge
                B(122f, 76f, 3f),           // 14
                B(110f, OrbitRadius, 3f),   // 15 orbit, under the canopy
            };
        }

        /// <summary>
        /// Expo Sprint, version 2 (P3, beginner, ~1.5 km, top ~62 m): up the walkway, the courtyard roof hop, under the
        /// canopy and round the sphere at 3 m, then a gentle climb straight **up through the Holo Halo** (50 m) over the
        /// north plaza, a descending turn and back down the axis through the slalom, round the west side, down the
        /// walkway and the plaza U-turn into the finish cube. The first version is frozen as expo-astana in Track_Expo_v1.
        /// </summary>
        public static TrackData ExpoSprint()
        {
            var k = ExpoOpening();
            Arc(k, 110f, 10f, 20f, OrbitRadius, OrbitRadius, 3f, 3f);       // 16-20 orbit to the north
            k.Add(B(7f, 95f, 4f));                                           // 21
            k.Add(B(10f, 140f, 8f));                                         // 22
            k.Add(B(12f, 178f, 20f));                                        // 23 lining up under the Halo
            k.Add(B(12f, 200f, 35f));                                        // 24
            int halo = k.Count;
            k.Add(B(12f, 200f, 50f));                                        // 25 up through the Halo
            k.Add(B(12f, 200f, 62f));                                        // 26
            k.Add(B(15f, 224f, 58f));                                        // 27
            k.Add(B(19f, 252f, 42f));                                        // 28 descending turn
            k.Add(B(10f, 270f, 26f));                                        // 29
            k.Add(B(0f, 255f, 14f));                                         // 30
            k.Add(B(355f, 222f, 8f));                                        // 31
            int slalom = k.Count;
            k.Add(B(357f, 175f, 5f));                                        // 32 slalom down the axis
            k.Add(B(356f, 120f, 4f));                                        // 33
            k.Add(B(352f, 80f, 3f));                                         // 34
            Arc(k, 340f, 220f, 20f, OrbitRadius, OrbitRadius, 3f, 3f);       // west side of the orbit
            int west = k.Count - 4;
            k.Add(B(205f, 70f, 3f));
            k.Add(B(WalkSouth + 1f, 100f, 3f));                              // south pod gap
            k.Add(B(WalkSouth, 150f, 3f));
            int walk = k.Count;
            k.Add(B(WalkSouth, 210f, 3f));
            k.Add(B(WalkSouth, 265f, 2.5f));
            k.Add(B(WalkSouth, 305f, 2.5f));
            k.Add(B(193.5f, 322f, 3f));                                      // plaza U-turn
            k.Add(B(196.5f, 318f, 2.5f));

            var t = ExpoTrack("expo-v2-sprint", "Expo Sprint", 2, k);
            var r = t.Route;
            var route = new TrackRoute(r);
            float At(int knot) => route.ClosestDistance(k[knot]);
            void Add(string type, int knot, string color) => r.Obstacles.Add(new RouteObstacle { Type = type, Distance = At(knot), Color = color, IsCheckpoint = true });
            void Add3(string type, int knot, string color, float scale = 1f) => r.Obstacles.Add(new RouteObstacle { Type = type, Distance = At(knot), Color = color, IsCheckpoint = true, Align3D = true, Scale = scale });
            Add("Finish", 1, "");
            Add("Single", 2, "red");
            Add("Air", 8, "blue");        // courtyard
            Add("Air", 9, "blue");
            Add3("HoloRing", 11, "");     // over the inner wing
            Add("Single", 16, "red");     // under the canopy
            Add("Single", 19, "red");
            Add3("HoloRing", 23, "");     // lining up
            Add3("HoloHalo", halo, "");   // straight up through the Halo
            Add3("HoloRing", 28, "");     // descending turn
            Add("Slalom", slalom, "white");
            Add("Single", west, "red");   // west orbit
            Add("Double", walk, "red");   // walkway, southbound
            Add("Single", 0, "white");    // the straight into the finish
            return Finish(t, route, k);
        }

        /// <summary>
        /// Expo Pro, version 2 (P3, advanced, ~2.9 km, top ~113 m): the courtyard roof hop, then the **Sphere Crest**
        /// (climb the south-east flank, a crest ring over the top above the turbines, a dive series down the north side,
        /// a pull-out ring at 6 m), the **Holo Halo** loop (under it, up and back over it, then a vertical drop through
        /// its centre), the **monument-top dive**, the **ladder** (three stacked rings, straight up), a **split-S** from a
        /// high ring, the ring-road sweeper and the 226° pavilion's courtyard home. The first version is frozen as
        /// expo-pro in Track_Expo_v1.
        /// </summary>
        public static TrackData ExpoPro()
        {
            var k = ExpoOpening();
            k.RemoveRange(12, k.Count - 12);                                   // keep 0-11: walkway, cube, courtyard, inner wing
            k[10] = B(141f, 182f, 27f);                                        // climb out higher: the crest follows
            k[11] = B(136f, 158f, 36f);
            int crestStart = k.Count;
            k.Add(B(141f, 122f, 40f));                                         // climbing the south-east flank
            k.Add(B(150f, 86f, 62f));
            k.Add(B(162f, 60f, 86f));
            k.Add(B(178f, 36f, 104f));
            int crest = k.Count;
            k.Add(new Vector3(0f, 113f, 0f));                                  // crest, over the turbines
            k.Add(B(12f, 36f, 104f));                                          // dive series down the north side
            int dive1 = k.Count;
            k.Add(B(12f, 56f, 82f));
            int dive2 = k.Count;
            k.Add(B(12f, 67f, 55f));
            int dive3 = k.Count;
            k.Add(B(12f, 80f, 28f));
            int pullOut = k.Count;
            k.Add(B(12f, 102f, 6f));                                           // pull-out at 6 m
            k.Add(B(12f, 150f, 12f));
            k.Add(B(12f, 212f, 30f));                                          // under the Halo
            k.Add(B(12f, 250f, 55f));                                          // up ...
            k.Add(B(12f, 246f, 86f));                                          // ... and back over the top
            k.Add(B(12f, 218f, 93f));
            k.Add(B(12f, 202f, 82f));
            int halo = k.Count;
            k.Add(B(12f, 200f, 60f));                                          // straight down through the centre
            k.Add(B(12f, 200f, 40f));
            k.Add(B(12f, 213f, 21f));
            k.Add(B(12f, 250f, 13f));
            k.Add(B(12f, 330f, 32f));                                          // climbing to the needle
            k.Add(B(12f, 420f, 52f));
            int needle = k.Count;
            k.Add(B(12f, 471f, 55f));                                          // the ring just over the needle's top
            k.Add(B(12f, 500f, 36f));                                          // dive
            k.Add(B(10f, 528f, 10f));
            k.Add(B(5f, 545f, 8f));                                            // turning west, inside the park
            k.Add(B(0f, 500f, 8f));
            k.Add(B(352f, 440f, 8f));
            k.Add(B(340f, 385f, 7f));
            k.Add(B(318f, 330f, 7f));
            k.Add(B(308f, 300f, 7f));
            int ladder = k.Count;
            k.Add(B(305f, 287f, 15f));                                         // the ladder: three rings, straight up
            k.Add(B(305f, 287f, 28f));
            k.Add(B(305f, 287f, 41f));
            k.Add(B(305f, 287f, 52f));
            k.Add(B(302f, 315f, 62f));
            int high = k.Count;
            k.Add(B(300f, 365f, 64f));                                         // the high ring, then the split-S
            k.Add(B(300f, 405f, 55f));
            k.Add(B(301f, 418f, 38f));
            k.Add(B(299f, 398f, 24f));
            k.Add(B(295f, 360f, 14f));
            k.Add(B(289f, 335f, 5f));
            int sweepStart = k.Count;
            Arc(k, 284f, 260f, 8f, 330f, 330f, 4f, 4f);                       // ring-road sweeper
            int sweepEnd = k.Count - 1;
            k.Add(B(259f, 300f, 4f));                                          // into the 226 pavilion's courtyard
            k.Add(B(259f, 245f, 12f));
            k.Add(B(256f, 205f, 26f));
            k.Add(B(252f, 193f, 31f));
            k.Add(B(246f, 191f, 25f));
            k.Add(B(240f, 191f, 13f));
            int court2 = k.Count;
            k.Add(B(232f, 191f, 7f));
            k.Add(B(221f, 191f, 7f));
            k.Add(B(210f, 192f, 13f));
            k.Add(B(202f, 196f, 31f));
            k.Add(B(WalkSouth, 222f, 16f));
            k.Add(B(WalkSouth, 255f, 4f));
            k.Add(B(WalkSouth, 295f, 2.5f));
            k.Add(B(193.5f, 322f, 3f));                                        // plaza U-turn
            k.Add(B(196.5f, 318f, 2.5f));

            var t = ExpoTrack("expo-v2-pro", "Expo Pro", 2, k);
            var r = t.Route;
            var route = new TrackRoute(r);
            float At(int knot) => route.ClosestDistance(k[knot]);
            void Add(string type, int knot, string color) => r.Obstacles.Add(new RouteObstacle { Type = type, Distance = At(knot), Color = color, IsCheckpoint = true });
            void Add3(string type, int knot, string color, float scale = 1f) => r.Obstacles.Add(new RouteObstacle { Type = type, Distance = At(knot), Color = color, IsCheckpoint = true, Align3D = true, Scale = scale });
            Add("Finish", 1, "");
            Add("Air", 8, "blue");                      // first courtyard
            Add("Air", 9, "blue");
            Add3("HoloRing", 11, "");                   // over the inner wing
            Add3("HoloRing", crestStart + 1, "");       // up the flank
            Add3("HoloRing", crestStart + 3, "");
            Add3("HoloRing", crest, "", 1.2f);          // the crest
            Add3("HoloRing", dive1, "");                // dive series
            Add3("HoloRing", dive2, "");
            Add3("HoloRing", dive3, "");
            Add3("HoloRing", pullOut, "", 1.2f);        // pull-out at 6 m
            Add3("HoloHalo", halo, "");                 // down through the Halo
            Add3("HoloRing", needle, "");               // over the needle
            Add3("HoloRing", ladder, "");               // the ladder
            Add3("HoloRing", ladder + 1, "");
            Add3("HoloRing", ladder + 2, "");
            Add3("HoloRing", high, "", 1.2f);           // the high ring before the split-S
            Add3("HoloRing", high + 2, "");             // the bottom of the split-S
            for (int s = sweepStart + 1; s < sweepEnd; s += 2) Add("Single", s, "red");
            Add("Air", court2, "blue");                 // second courtyard
            Add("Air", court2 + 1, "blue");
            Add("Double", court2 + 5, "red");
            Add("Single", 0, "white");                  // the straight into the finish
            return Finish(t, route, k);
        }

        /// <summary>
        /// Expo free flight (E7): no gates, the whole 1.2 km area. Spawns: the south plaza (default), hovering above the
        /// sphere, the park and the east ring road; F / D-pad up switches, T resets.
        /// </summary>
        public static TrackData ExpoFreeFlight()
        {
            var t = new TrackData { Id = "expo-v2-free", Name = "Expo Free Flight", Author = "GateRush", Environment = "expo", Laps = 1, FreeFlight = true };
            t.Spawn = Spawn("South plaza", B(WalkNorth, 285f, 0.15f), 16f);
            t.ExtraSpawns.Add(Spawn("Above the sphere", B(194f, 30f, 112f), 14f));
            t.ExtraSpawns.Add(Spawn("Park", B(12f, 330f, 0.15f), 192f));
            t.ExtraSpawns.Add(Spawn("Ring road", B(90f, 330f, 0.15f), 0f));
            return t;
        }

        private static SpawnData Spawn(string name, Vector3 p, float yaw) => new SpawnData { Name = name, Position = p, Yaw = yaw };

        private static TrackData ExpoTrack(string id, string name, int laps, List<Vector3> knots)
        {
            var t = new TrackData { Id = id, Name = name, Author = "GateRush", Environment = "expo", Laps = laps };
            var r = t.Route;
            r.AirCourse = true;
            r.CorridorWidth = 5f;
            r.FlightHeight = 1.8f;
            foreach (var p in knots) r.Knots.Add(new RouteKnot { Position = p });
            return t;
        }

        /// <summary>Sorts the obstacles, makes the cube the finish and puts the start pad 26 m before it on the walkway.</summary>
        private static TrackData Finish(TrackData t, TrackRoute route, List<Vector3> knots)
        {
            var r = t.Route;
            r.Obstacles.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            r.FinishObstacle = r.Obstacles.FindIndex(o => o.Type == "Finish");
            var pad = route.Sample(route.ClosestDistance(knots[1]) - 26f);
            t.Spawn.Position = new Vector3(pad.Position.x, r.GroundHeight + 0.15f, pad.Position.z);
            t.Spawn.Yaw = Mathf.Atan2(pad.FlatForward.x, pad.FlatForward.z) * Mathf.Rad2Deg;
            foreach (float side in new[] { -4.2f, 4.2f })
                t.Props.Add(new PropData { Type = "Flag", Position = new Vector3(pad.Position.x, 0f, pad.Position.z) + pad.Right * 3f + pad.FlatForward * side, Euler = new Vector3(0f, t.Spawn.Yaw, 0f) });
            return t;
        }

        /// <summary>The flight lines of both Sphere District courses, sampled every 2 m (props keep clear of them).</summary>
        public static List<List<Vector3>> ExpoCorridors()
        {
            var all = new List<List<Vector3>>();
            foreach (var t in new[] { ExpoSprint(), ExpoPro() })
            {
                var route = new TrackRoute(t.Route);
                var pts = new List<Vector3>();
                for (float d = 0f; d < route.Length; d += 2f) pts.Add(route.Sample(d).Position);
                all.Add(pts);
            }
            return all;
        }

        /// <summary>Radius of the Expo course's orbit round the sphere (generators keep it clear).</summary>
        public const float OrbitRadius = 61f;

        /// <summary>A point at <paramref name="bearing"/> (degrees clockwise from north) and <paramref name="distance"/> from the sphere.</summary>
        public static Vector3 ExpoPoint(float bearing, float distance, float y = 0f)
        {
            float a = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * distance, y, Mathf.Cos(a) * distance);
        }

        /// <summary>The Expo route's knots (x east, y deck height, z north), in flying order.</summary>
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
