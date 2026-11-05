using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ushqyr.AI;
using Ushqyr.Flight;
using Ushqyr.Game;
using Ushqyr.Garage;
using Ushqyr.Track;
using Ushqyr.Vehicle;
using UnityEditor;
using UnityEngine;

namespace Ushqyr.EditorTools.Bots
{
    /// <summary>
    /// Headless bot flights in the Editor (P4): builds a catalogue course (gates, lanes, props with their colliders) into
    /// the open scene, bakes its racing line and flies <see cref="BotRun"/>s through it as fast as the CPU allows.
    /// Used by the acceptance tests and for tuning. The venue scenery is not loaded: the lines keep their 2 m clearance
    /// from it by construction (CourseClearance), so only the course's own geometry can be hit.
    /// </summary>
    public static class BotHarness
    {
        public const string CatalogPath = "Assets/_Project/Data/TrackCatalog.asset";
        public const string PrefabsPath = "Assets/_Project/Data/TrackPrefabs.asset";

        public sealed class Course
        {
            public TrackData Data;
            public LoadedTrack Track;
            public BotLine Line;
            public QuadProfile Profile;
            public void Dispose()
            {
                if (Track != null && Track.Root != null) Object.DestroyImmediate(Track.Root.gameObject);
            }
        }

        public struct Result
        {
            public bool Finished;
            public float[] Laps;
            public int Missed, Crashes, Rescues, Mistakes;
            public float SimSeconds;
            public float MeanLap => Laps != null && Laps.Length > 0 ? Laps.Average() : 0f;
            /// <summary>Mean of the flying laps (lap 1 includes the launch).</summary>
            public float MeanFlyingLap => Laps != null && Laps.Length > 1 ? Laps.Skip(1).Average() : MeanLap;
            public override string ToString() =>
                $"{(Finished ? "finished" : "DNF")} laps [{string.Join(", ", (Laps ?? new float[0]).Select(l => l.ToString("F1")))}] missed {Missed} crashes {Crashes} rescues {Rescues} mistakes {Mistakes}";
        }

        /// <summary>The race courses in the catalogue (free-flight tracks have no race).</summary>
        public static List<string> RaceCourses()
        {
            var cat = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogPath);
            var ids = new List<string>();
            foreach (var e in cat.Tracks)
            {
                var d = cat.Load(e);
                if (d != null && !d.FreeFlight) ids.Add(e.Id);
            }
            return ids;
        }

        public static QuadProfile ProfileFor(SizeClass size) => BuildComposer.Compose(BotRoster.ReferenceBuild(size));

        /// <summary>Line style for <see cref="Load"/> when not given (tuning).</summary>
        public static bool Tight;

        public static Course Load(string id, SizeClass size = SizeClass.Five) => Load(id, size, Tight);

        public static Course Load(string id, SizeClass size, bool tight)
        {
            var cat = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogPath);
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PrefabsPath);
            var data = cat.Load(cat.Find(id));
            var track = TrackLoader.Load(data, prefabs);
            Physics.SyncTransforms();
            var profile = ProfileFor(size);
            var line = RacingLineBaker.Bake(track.Route, data.Route, track.Gates, track.Spawn.position, profile, Collision().TrackGeometry, tight);
            return new Course { Data = data, Track = track, Line = line, Profile = profile };
        }

        /// <summary>The course with its cached line from <c>Resources/RacingLines</c> (baked if missing or out of date).</summary>
        public static Course LoadCached(string id, SizeClass size = SizeClass.Five)
        {
            var cat = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogPath);
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PrefabsPath);
            var data = cat.Load(cat.Find(id));
            var track = TrackLoader.Load(data, prefabs);
            Physics.SyncTransforms();
            var profile = ProfileFor(size);
            var line = RacingLineAsset.Get(id, (int)size, track.Route, data.Route, track.Gates, track.Spawn.position, profile, Collision().TrackGeometry, out bool rebaked);
            if (rebaked) Debug.LogWarning($"[Ushqyr][Bots] cached line for {id} missing or out of date: baked now");
            return new Course { Data = data, Track = track, Line = line, Profile = profile };
        }

        public static CollisionSettings Collision()
        {
            var c = CollisionSettings.Default;
            c.TrackGeometry = 1 << LayerMask.NameToLayer("TrackGeometry");
            return c;
        }

        /// <summary>Flies one bot for <paramref name="laps"/> laps (or until the time limit).</summary>
        public static Result Fly(Course course, BotDifficultyProfile level, int seed, int laps, float targetLap = 0f, StringBuilder log = null)
        {
            var bot = new BotRun(course.Line, course.Track.Gates, course.Profile, level, laps, Collision(), seed);
            var spawn = course.Track.Spawn;
            bot.Place(spawn.position + Vector3.up * 0.05f, spawn.rotation);
            if (targetLap > 0f) bot.SetTargetLap(targetLap);
            int missedBefore = 0;
            bot.Start(0.0);
            const float frame = 0.02f;
            float limit = Mathf.Max(60f, course.Line.LapTime / Mathf.Max(0.3f, level.Pace) * laps * 3f + 30f);
            float t = 0f;
            int lastGate = -1;
            bot.GatePassed += (g, r, time) =>
            {
                lastGate = g;
                if (log != null && bot.MissedGates != missedBefore)
                {
                    log.AppendLine($"  missed before gate {g} at {time:F1}s");
                    missedBefore = bot.MissedGates;
                }
            };
            int crashes = 0;
            while (!bot.Timer.Finished && t < limit)
            {
                bot.Advance(frame);
                t += frame;
                if (log != null && bot.Crashes != crashes)
                {
                    crashes = bot.Crashes;
                    log.AppendLine($"  crash {crashes} at {t:F1}s, pos {bot.Position:F1}, next gate {bot.Timer.NextCheckpoint} ({course.Track.Gates[bot.Timer.NextCheckpoint].name}), speed {bot.Velocity.magnitude:F1}, off {bot.Planner.OffLine:F1}, touching {Touching(bot.Position)}");
                }
            }
            var laps2 = new float[bot.Timer.CompletedLaps];
            for (int i = 0; i < laps2.Length; i++) laps2[i] = (float)bot.Timer.LapTime(i);
            return new Result
            {
                Finished = bot.Timer.Finished, Laps = laps2, Missed = bot.MissedGates, Crashes = bot.Crashes, Rescues = bot.Rescues,
                Mistakes = bot.Planner.Mistakes, SimSeconds = t,
            };
        }

        /// <summary>Position / velocity / attitude / sticks every <paramref name="every"/> s for the first seconds of a flight.</summary>
        public static string Trace(string id, float seconds, float every = 0.1f, string levelName = "Elite", float from = 0f, int seed = 1)
        {
            var sb = new StringBuilder();
            var course = Load(id);
            try
            {
                var level = BotDifficultyProfile.Default((BotLevel)System.Enum.Parse(typeof(BotLevel), levelName));
                var bot = new BotRun(course.Line, course.Track.Gates, course.Profile, level, 3, Collision(), seed);
                var spawn = course.Track.Spawn;
                bot.Place(spawn.position + Vector3.up * 0.05f, spawn.rotation);
                bot.Start(0.0);
                sb.AppendLine($"spawn {spawn.position:F2} line0 {course.Line.Points[0]:F2} v0 {course.Line.Speed[0]:F1}");
                float t = 0f, next = from;
                while (t < seconds)
                {
                    bot.Advance(0.002f);
                    t += 0.002f;
                    if (t >= next)
                    {
                        next += every;
                        var up = bot.Rotation * Vector3.up;
                        var i = bot.Input;
                        course.Line.Sample(Mathf.Max(0f, bot.Planner.Progress), out var lp, out _, out _, out var lv, out _, out var slack);
                        sb.AppendLine($"t {t:F2} p {bot.Position:F2} v {bot.Velocity:F1} up {up:F2} thr {i.Throttle:F2} p/r/y {i.Pitch:F2}/{i.Roll:F2}/{i.Yaw:F2} s {bot.Planner.Progress:F1} off {bot.Planner.OffLine:F2} vref {lv * level.Pace:F1} slack {slack:F2} next {bot.Timer.NextCheckpoint} crash {bot.Crashes}");
                    }
                }
            }
            finally { course.Dispose(); }
            return sb.ToString();
        }

        public static string Touching(Vector3 p)
        {
            var names = Physics.OverlapSphere(p, 0.6f, ~0, QueryTriggerInteraction.Ignore).Select(c => c.transform.parent != null ? c.transform.parent.name + "/" + c.name : c.name);
            return string.Join(" ", names);
        }

        /// <summary>Each gate: its opening, the baker's safe radius and how far off centre the line passes.</summary>
        public static string Gates(string id)
        {
            var sb = new StringBuilder();
            var course = Load(id);
            try
            {
                var specs = RacingLineBaker.Specs(course.Track.Gates, Collision().TrackGeometry);
                for (int k = 0; k < specs.Length; k++)
                {
                    var g = course.Track.Gates[k];
                    int si = course.Line.GateSamples[k];
                    Vector3 lp = course.Line.Points[si];
                    Vector3 q = lp - specs[k].Centre;
                    float ax = Vector3.Dot(q, specs[k].Normal);
                    float rad = (q - specs[k].Normal * ax).magnitude;
                    float cross = Vector3.Angle(course.Line.Tangents[si], specs[k].Normal);
                    sb.AppendLine($"{k} {g.name} size {g.OpeningSize:F2} aim {g.AimPointWorld:F1} open {g.OpeningCenterWorld:F1} r {specs[k].Radius:F2} line@{si * course.Line.Step:F0}m axial {ax:F2} radial {rad:F2} angle {cross:F0} v {course.Line.Speed[si]:F1} slack {course.Line.Slack[si]:F2}");
                }
            }
            finally { course.Dispose(); }
            return sb.ToString();
        }

        /// <summary>Where the baked line runs closer than <paramref name="min"/> m to course geometry (and what it touches).</summary>
        public static string LineClearance(string id, float min = 0.35f, bool tight = false)
        {
            var sb = new StringBuilder();
            var course = Load(id, SizeClass.Five, tight);
            try
            {
                int mask = Collision().TrackGeometry;
                var l = course.Line;
                int bad = 0;
                for (int i = 0; i < l.Count; i += 2)
                {
                    var p = l.Points[i];
                    var hits = Physics.OverlapSphere(p, min, mask, QueryTriggerInteraction.Ignore);
                    if (hits.Length == 0) continue;
                    if (bad++ < 25) sb.AppendLine($"{i * l.Step:F0} m {p:F1}: {hits[0].transform.root.name}/{hits[0].name}");
                }
                sb.Insert(0, $"{id}: {bad} line samples within {min} m of geometry\n");
            }
            finally { course.Dispose(); }
            return sb.ToString();
        }

        /// <summary>Quick tuning report: one bot per level on a course.</summary>
        public static string Report(string id, int laps = 2, int seed = 1, string levels = "Elite")
        {
            var sb = new StringBuilder();
            var course = Load(id);
            try
            {
                var l = course.Line;
                sb.AppendLine($"{id}: line {l.Length:F0} m, profile lap {l.LapTime:F1} s, top {l.Speed.Max():F1} m/s, min {l.Speed.Min():F1}, gates {course.Track.Gates.Length}, laps {laps}");
                foreach (var name in levels.Split(','))
                {
                    var level = BotDifficultyProfile.Default((BotLevel)System.Enum.Parse(typeof(BotLevel), name.Trim()));
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = Fly(course, level, seed, laps, 0f, sb);
                    sb.AppendLine($"{name}: {r} ({sw.ElapsedMilliseconds} ms)");
                }
            }
            finally { course.Dispose(); }
            return sb.ToString();
        }
    }
}
