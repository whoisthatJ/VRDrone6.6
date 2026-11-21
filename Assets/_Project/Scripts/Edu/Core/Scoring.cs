using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>One line of a rubric: a measurement of the flight, and what counts as perfect and as worthless.</summary>
    public sealed class Criterion
    {
        public string Id, Metric, Label, Tip;
        /// <summary>The steps of a guided exercise, for the metric "steps_done"; null for every other line.</summary>
        public List<StepSpec> Steps;
        public Dictionary<string, object> Params;
        public double Weight = 1, Best, Worst;
        /// <summary>A required criterion at zero fails the attempt whatever the total is (for example "did not land").</summary>
        public bool Required;
    }

    /// <summary>
    /// Something that can be assigned and graded (EDU_PLAN D1, F1): an exercise, a race, a scenario, a programming task.
    /// It is data: a JSON file checked when it is read. Its hash goes into every attempt, so an old grade always says
    /// which version of the activity it was earned on.
    /// </summary>
    public sealed class ActivityDefinition
    {
        public const int Format = 1;
        public string Id, Type, Title, Briefing, Hash;
        /// <summary>
        /// Where the activity stands in a course: its block ("A" to "E" in training) and its number. Given as "block"
        /// and "number", or read from an id like "a02-hover-land". Empty and 0 for an activity outside any course.
        /// </summary>
        public string Block = "";
        public int Number;
        public double PassMark = 60, TimeLimit;
        public double[] Stars = { 60, 80, 95 };
        public readonly List<Criterion> Criteria = new List<Criterion>();
        public Dictionary<string, object> Raw;
        /// <summary>The steps the pilot is led through, when the activity has them (the first "steps_done" line of its rubric).</summary>
        public List<StepSpec> Steps
        {
            get
            {
                foreach (Criterion c in Criteria)
                    if (c.Steps != null) return c.Steps;
                return null;
            }
        }

        /// <summary>How the activity is flown: place, drone, objects, weather, what ends it. Never null.</summary>
        public ActivitySetup Setup = new ActivitySetup();

        private static readonly string[] Types = { "exercise", "race", "soccer", "scenario", "program", "free" };

        /// <summary>Reads and checks an activity; every fault is reported in words a content author can act on.</summary>
        public static ActivityDefinition Parse(string json)
        {
            Dictionary<string, object> o = Json.ParseObject(json);
            double format = Json.Num(o, "format", -1);
            if (format != Format) throw new FormatException("activity: \"format\" must be " + Format + " (found " + (format < 0 ? "none" : format.ToString(CultureInfo.InvariantCulture)) + ")");
            var a = new ActivityDefinition
            {
                Raw = o,
                Id = Json.Str(o, "id"),
                Type = Json.Str(o, "type"),
                Title = Json.Str(o, "title"),
                Briefing = Json.Str(o, "briefing", ""),
                PassMark = Json.Num(o, "pass", 60),
                TimeLimit = Json.Num(o, "time_limit", 0),
            };
            if (string.IsNullOrEmpty(a.Id)) throw new FormatException("activity: \"id\" is missing");
            a.Block = Json.Str(o, "block", "");
            a.Number = (int)Json.Num(o, "number", 0);
            if (a.Block.Length == 0 && a.Id.Length >= 3 && a.Id[0] >= 'a' && a.Id[0] <= 'z' && char.IsDigit(a.Id[1]))
            {
                int end = 1;
                while (end < a.Id.Length && char.IsDigit(a.Id[end])) end++;
                if (end < a.Id.Length && a.Id[end] == '-' && int.TryParse(a.Id.Substring(1, end - 1), out int n))
                {
                    a.Block = a.Id.Substring(0, 1).ToUpperInvariant();
                    if (a.Number == 0) a.Number = n;
                }
            }
            foreach (char c in a.Id)
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.'))
                    throw new FormatException("activity " + a.Id + ": \"id\" may hold only a-z, 0-9, '-', '_' and '.'");
            if (Array.IndexOf(Types, a.Type) < 0) throw new FormatException("activity " + a.Id + ": \"type\" must be one of " + string.Join(", ", Types));
            if (string.IsNullOrEmpty(a.Title)) throw new FormatException("activity " + a.Id + ": \"title\" is missing");
            if (a.PassMark < 0 || a.PassMark > 100) throw new FormatException("activity " + a.Id + ": \"pass\" must be between 0 and 100");
            if (a.TimeLimit < 0) throw new FormatException("activity " + a.Id + ": \"time_limit\" cannot be negative");
            List<object> stars = Json.Arr(o, "stars");
            if (stars != null)
            {
                if (stars.Count != 3 || !(stars[0] is double s1) || !(stars[1] is double s2) || !(stars[2] is double s3) || s1 > s2 || s2 > s3 || s1 < 0 || s3 > 100)
                    throw new FormatException("activity " + a.Id + ": \"stars\" must be three growing numbers between 0 and 100");
                a.Stars = new[] { s1, s2, s3 };
            }
            List<object> rubric = Json.Arr(o, "rubric");
            if (rubric == null || rubric.Count == 0)
            {
                if (a.Type != "free") throw new FormatException("activity " + a.Id + ": \"rubric\" is missing or empty");
                rubric = new List<object>();
            }
            var seen = new HashSet<string>();
            foreach (object item in rubric)
            {
                var c = item as Dictionary<string, object> ?? throw new FormatException("activity " + a.Id + ": every rubric line must be an object");
                var cr = new Criterion
                {
                    Id = Json.Str(c, "id"),
                    Metric = Json.Str(c, "metric"),
                    Label = Json.Str(c, "label"),
                    Tip = Json.Str(c, "tip"),
                    Params = Json.Obj(c, "params") ?? new Dictionary<string, object>(),
                    Weight = Json.Num(c, "weight", 1),
                    Best = Json.Num(c, "best", double.NaN),
                    Worst = Json.Num(c, "worst", double.NaN),
                    Required = Json.Flag(c, "required"),
                };
                if (string.IsNullOrEmpty(cr.Id) || !seen.Add(cr.Id)) throw new FormatException("activity " + a.Id + ": a rubric line has no \"id\" or repeats one (" + cr.Id + ")");
                if (!Metrics.Known(cr.Metric)) throw new FormatException("activity " + a.Id + ", line " + cr.Id + ": unknown metric \"" + cr.Metric + "\"");
                if (double.IsNaN(cr.Best) || double.IsNaN(cr.Worst) || cr.Best == cr.Worst)
                    throw new FormatException("activity " + a.Id + ", line " + cr.Id + ": \"best\" and \"worst\" must be two different numbers");
                if (cr.Weight <= 0) throw new FormatException("activity " + a.Id + ", line " + cr.Id + ": \"weight\" must be above 0");
                if (string.IsNullOrEmpty(cr.Label)) throw new FormatException("activity " + a.Id + ", line " + cr.Id + ": \"label\" is missing");
                if (cr.Metric == "steps_done")
                {
                    cr.Steps = StepTracker.Parse(Json.Arr(cr.Params, "steps"), "activity " + a.Id + ", line " + cr.Id);
                    if (cr.Steps.Count == 0) throw new FormatException("activity " + a.Id + ", line " + cr.Id + ": \"steps_done\" needs a list of \"steps\" in its params");
                }
                a.Criteria.Add(cr);
            }
            a.Setup = ActivitySetup.Parse(Json.Obj(o, "setup"), a.Id, name => Array.IndexOf(Ushqyr.Flight.FailureSet.Names, name) >= 0);
            using (SHA256 sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(o)));
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
                a.Hash = sb.ToString();
            }
            return a;
        }
    }

    public sealed class CriterionResult
    {
        public string Id, Label, Tip;
        /// <summary>The measured value in the metric's own unit (metres, seconds, a count).</summary>
        public double Value;
        /// <summary>0..1: how much of this line's weight was earned.</summary>
        public double Fraction;
        public double Weight;
        public bool Required;
    }

    public sealed class ScoreResult
    {
        public double Score;
        public bool Passed;
        public int Stars;
        public readonly List<CriterionResult> Lines = new List<CriterionResult>();
        /// <summary>Tips of the two weakest lines (those that lost the most points); fewer when little was lost.</summary>
        public readonly List<string> Tips = new List<string>();
    }

    /// <summary>Turns a recorded flight into a grade. The same recording and the same activity always give the same grade.</summary>
    public static class Rubric
    {
        public static ScoreResult Score(ActivityDefinition activity, FlightRecording flight)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (flight == null) throw new ArgumentNullException(nameof(flight));
            // An activity's numbers are in its own frame; a flight flown somewhere else in a scene is brought into it.
            flight = flight.InOwnFrame();
            var result = new ScoreResult();
            double total = 0, earned = 0;
            bool requiredFailed = false;
            foreach (Criterion c in activity.Criteria)
            {
                double value = Metrics.Measure(c.Metric, c.Params, flight);
                double fraction = double.IsNaN(value) ? 0 : (value - c.Worst) / (c.Best - c.Worst);
                fraction = fraction < 0 ? 0 : fraction > 1 ? 1 : fraction;
                result.Lines.Add(new CriterionResult { Id = c.Id, Label = c.Label, Tip = c.Tip, Value = value, Fraction = fraction, Weight = c.Weight, Required = c.Required });
                total += c.Weight;
                earned += c.Weight * fraction;
                if (c.Required && fraction <= 0) requiredFailed = true;
            }
            result.Score = total > 0 ? Math.Round(100.0 * earned / total, 1) : 0;
            result.Passed = !requiredFailed && result.Score >= activity.PassMark;
            if (result.Passed)
                for (int i = 0; i < 3; i++)
                    if (result.Score >= activity.Stars[i]) result.Stars = i + 1;
            var weakest = new List<CriterionResult>(result.Lines);
            weakest.Sort((x, y) =>
            {
                int byLoss = (y.Weight * (1 - y.Fraction)).CompareTo(x.Weight * (1 - x.Fraction));
                return byLoss != 0 ? byLoss : string.CompareOrdinal(x.Id, y.Id);
            });
            foreach (CriterionResult line in weakest)
            {
                if (result.Tips.Count == 2) break;
                if (line.Fraction < 0.9 && !string.IsNullOrEmpty(line.Tip)) result.Tips.Add(line.Tip);
            }
            return result;
        }
    }

    /// <summary>
    /// The measurements a rubric can ask for. Each one reads only the recording. Distances are in metres, times in
    /// seconds, angles in degrees. "In the air" means armed and more than 15 cm above the ground level given in the
    /// parameters ("ground", 0 by default).
    /// </summary>
    public static class Metrics
    {
        private delegate double Fn(Dictionary<string, object> p, FlightRecording f);

        private static readonly Dictionary<string, Fn> All = new Dictionary<string, Fn>
        {
            ["time"] = (p, f) => AirTime(p, f),
            // The whole attempt from start to end, on the ground or in the air.
            ["duration"] = (p, f) => f.Duration,
            ["zone_reached"] = ZoneReached,
            ["hold_time"] = HoldTime,
            ["position_error"] = PositionError,
            ["height_error"] = HeightError,
            ["heading_error"] = HeadingError,
            ["landing_distance"] = LandingDistance,
            ["touchdown_speed"] = TouchdownSpeed,
            ["smoothness"] = Smoothness,
            ["event_count"] = EventCount,
            ["steps_done"] = (p, f) => StepTracker.CountDone(StepTracker.Parse(Json.Arr(p, "steps"), "steps"), f),
            ["gates_in_order"] = GatesInOrder,
            ["path_error"] = PathError,
            ["speed_in_band"] = SpeedInBand,
            ["outside_fence"] = OutsideFence,
            ["battery_left"] = (p, f) => f.Samples.Count > 0 ? f.Samples[f.Samples.Count - 1].Battery * 100.0 : 0,
        };

        public static bool Known(string metric) => metric != null && All.ContainsKey(metric);

        /// <summary>How a metric's value is written: a format with its unit ("{0} m"), a key of the language table.</summary>
        public static string Unit(string metric)
        {
            switch (metric)
            {
                case "time":
                case "duration":
                case "hold_time":
                case "outside_fence":
                    return "{0} s";
                case "position_error":
                case "height_error":
                case "landing_distance":
                case "path_error":
                    return "{0} m";
                case "touchdown_speed":
                    return "{0} m/s";
                case "heading_error":
                    return "{0}°";
                case "battery_left":
                case "speed_in_band":
                    return "{0}%";
                default:
                    return "{0}";
            }
        }

        public static IEnumerable<string> Names => All.Keys;

        public static double Measure(string metric, Dictionary<string, object> p, FlightRecording f)
        {
            if (!All.TryGetValue(metric ?? "", out Fn fn)) throw new ArgumentException("unknown metric: " + metric);
            return fn(p ?? new Dictionary<string, object>(), f);
        }

        private static double Ground(Dictionary<string, object> p) => Json.Num(p, "ground", 0);

        private static bool InAir(FlightSample s, double ground) => s.Armed && s.Y - ground > 0.15;

        /// <summary>The part of the flight a measurement looks at: "from" and "to" in seconds, the whole flight by default.</summary>
        private static bool InWindow(FlightSample s, Dictionary<string, object> p, FlightRecording f)
        {
            double t = s.T - (f.Samples.Count > 0 ? f.Samples[0].T : 0);
            return t >= Json.Num(p, "from", double.NegativeInfinity) && t <= Json.Num(p, "to", double.PositiveInfinity);
        }

        private static double Dt(FlightRecording f) => f.Rate > 0 ? 1.0 / f.Rate : 0.01;

        private static double AirTime(Dictionary<string, object> p, FlightRecording f)
        {
            double ground = Ground(p), t = 0, dt = Dt(f);
            foreach (FlightSample s in f.Samples) if (InAir(s, ground)) t += dt;
            return t;
        }

        private static double Flat(FlightSample s, Dictionary<string, object> p) =>
            Math.Sqrt(Sq(s.X - Json.Num(p, "x", 0)) + Sq(s.Z - Json.Num(p, "z", 0)));

        private static double Sq(double v) => v * v;

        private static bool Inside(FlightSample s, Dictionary<string, object> p)
        {
            if (Flat(s, p) > Json.Num(p, "radius", 1)) return false;
            double y = Json.Num(p, "y", double.NaN);
            return double.IsNaN(y) || Math.Abs(s.Y - y) <= Json.Num(p, "height_tolerance", 0.5);
        }

        /// <summary>1 when the drone was inside the zone (x, z, radius, optional y ± height_tolerance) at any moment, else 0.</summary>
        private static double ZoneReached(Dictionary<string, object> p, FlightRecording f)
        {
            foreach (FlightSample s in f.Samples) if (InWindow(s, p, f) && Inside(s, p)) return 1;
            return 0;
        }

        /// <summary>The longest unbroken stay inside the zone while in the air, seconds.</summary>
        private static double HoldTime(Dictionary<string, object> p, FlightRecording f)
        {
            double ground = Ground(p), dt = Dt(f), run = 0, best = 0;
            foreach (FlightSample s in f.Samples)
            {
                run = InWindow(s, p, f) && InAir(s, ground) && Inside(s, p) ? run + dt : 0;
                if (run > best) best = run;
            }
            return best;
        }

        private static double MeanInAir(Dictionary<string, object> p, FlightRecording f, Func<FlightSample, double> value)
        {
            double ground = Ground(p), sum = 0;
            int n = 0;
            foreach (FlightSample s in f.Samples)
            {
                if (!InWindow(s, p, f) || !InAir(s, ground)) continue;
                sum += value(s);
                n++;
            }
            return n > 0 ? sum / n : double.NaN; // never in the air: no value, the line earns nothing
        }

        /// <summary>Mean horizontal distance from the point (x, z) while in the air.</summary>
        private static double PositionError(Dictionary<string, object> p, FlightRecording f) => MeanInAir(p, f, s => Flat(s, p));

        /// <summary>Mean distance from the height "y" while in the air.</summary>
        private static double HeightError(Dictionary<string, object> p, FlightRecording f)
        {
            double y = Json.Num(p, "y", 0);
            return MeanInAir(p, f, s => Math.Abs(s.Y - y));
        }

        /// <summary>Mean distance of the heading from "heading" (degrees) while in the air.</summary>
        private static double HeadingError(Dictionary<string, object> p, FlightRecording f)
        {
            double want = Json.Num(p, "heading", 0);
            return MeanInAir(p, f, s =>
            {
                double d = Math.Abs(s.Heading - want) % 360.0;
                return d > 180 ? 360 - d : d;
            });
        }

        private static int Touchdown(Dictionary<string, object> p, FlightRecording f)
        {
            // The last moment the drone came down from the air and stayed down to the end of the recording.
            double ground = Ground(p);
            int last = -1;
            for (int i = 1; i < f.Samples.Count; i++)
                if (f.Samples[i - 1].Y - ground > 0.15 && f.Samples[i].Y - ground <= 0.15) last = i;
            if (last < 0) return -1;
            for (int i = last; i < f.Samples.Count; i++)
                if (f.Samples[i].Y - ground > 0.15) return -1;
            return last;
        }

        /// <summary>Distance from the pad (x, z) where the drone came to rest; no value if it never landed.</summary>
        private static double LandingDistance(Dictionary<string, object> p, FlightRecording f)
        {
            if (Touchdown(p, f) < 0) return double.NaN;
            FlightSample end = f.Samples[f.Samples.Count - 1];
            return end.Speed > 0.3 ? double.NaN : Flat(end, p);
        }

        /// <summary>Downward speed at the moment of touching the ground; no value if it never landed.</summary>
        private static double TouchdownSpeed(Dictionary<string, object> p, FlightRecording f)
        {
            int i = Touchdown(p, f);
            return i < 1 ? double.NaN : Math.Max(0, -f.Samples[i - 1].Vy);
        }

        /// <summary>How much the sticks were stirred: the root mean square of their speed (full deflections per second) in the air.</summary>
        private static double Smoothness(Dictionary<string, object> p, FlightRecording f)
        {
            double ground = Ground(p), dt = Dt(f), sum = 0;
            int n = 0;
            for (int i = 1; i < f.Samples.Count; i++)
            {
                FlightSample a = f.Samples[i - 1], b = f.Samples[i];
                if (!InWindow(b, p, f) || !InAir(a, ground) || !InAir(b, ground)) continue;
                sum += (Sq(b.Roll - a.Roll) + Sq(b.Pitch - a.Pitch) + Sq(b.Yaw - a.Yaw)) / (3 * dt * dt);
                n++;
            }
            return n > 0 ? Math.Sqrt(sum / n) : double.NaN;
        }

        /// <summary>How many events of "type" the recording holds.</summary>
        private static double EventCount(Dictionary<string, object> p, FlightRecording f)
        {
            string type = Json.Str(p, "type", "crash");
            int n = 0;
            foreach (FlightEvent e in f.Events) if (e.Type == type) n++;
            return n;
        }

        /// <summary>How many gates were passed in order: "gate" events whose data is 0, 1, 2, … A gate out of turn does not count.</summary>
        private static double GatesInOrder(Dictionary<string, object> p, FlightRecording f)
        {
            int next = 0;
            foreach (FlightEvent e in f.Events)
                if (e.Type == "gate" && int.TryParse(e.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index == next) next++;
            return next;
        }

        /// <summary>Mean distance from the path "points" ([[x, y, z], …], straight lines between them) while in the air.</summary>
        private static double PathError(Dictionary<string, object> p, FlightRecording f)
        {
            List<object> raw = Json.Arr(p, "points");
            var pts = new List<double[]>();
            if (raw != null)
                foreach (object o in raw)
                    if (o is List<object> xyz && xyz.Count == 3 && xyz[0] is double x && xyz[1] is double y && xyz[2] is double z)
                        pts.Add(new[] { x, y, z });
            if (pts.Count < 2) return double.NaN;
            return MeanInAir(p, f, s =>
            {
                double best = double.MaxValue;
                for (int i = 1; i < pts.Count; i++)
                {
                    double[] a = pts[i - 1], b = pts[i];
                    double ax = b[0] - a[0], ay = b[1] - a[1], az = b[2] - a[2];
                    double len = ax * ax + ay * ay + az * az;
                    double t = len > 0 ? ((s.X - a[0]) * ax + (s.Y - a[1]) * ay + (s.Z - a[2]) * az) / len : 0;
                    t = t < 0 ? 0 : t > 1 ? 1 : t;
                    double d = Math.Sqrt(Sq(s.X - a[0] - ax * t) + Sq(s.Y - a[1] - ay * t) + Sq(s.Z - a[2] - az * t));
                    if (d < best) best = d;
                }
                return best;
            });
        }

        /// <summary>The share of the time in the air (0..1) with the speed between "min" and "max".</summary>
        private static double SpeedInBand(Dictionary<string, object> p, FlightRecording f)
        {
            double min = Json.Num(p, "min", 0), max = Json.Num(p, "max", double.PositiveInfinity);
            return MeanInAir(p, f, s => s.Speed >= min && s.Speed <= max ? 1 : 0);
        }

        /// <summary>Seconds spent outside the fence: above "max_height", or further than "radius" from (x, z).</summary>
        private static double OutsideFence(Dictionary<string, object> p, FlightRecording f)
        {
            double ground = Ground(p), dt = Dt(f), t = 0;
            double top = Json.Num(p, "max_height", double.PositiveInfinity), radius = Json.Num(p, "radius", double.PositiveInfinity);
            foreach (FlightSample s in f.Samples)
                if (InAir(s, ground) && (s.Y - ground > top || Flat(s, p) > radius)) t += dt;
            return t;
        }
    }
}
