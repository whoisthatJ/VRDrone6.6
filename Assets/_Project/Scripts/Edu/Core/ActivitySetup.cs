using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Where an activity sits in the world: its origin (the ground under the start point) and the direction its +Z
    /// points. An activity is written in its own frame, so the same exercise can be laid out in any open place, and a
    /// flight is scored in that frame, whatever scene it was flown in. Headings are degrees, 0 = +Z, clockwise from above.
    /// </summary>
    public struct FlightFrame
    {
        public float X, Y, Z, HeadingDeg;

        public static FlightFrame Identity => default;
        public bool IsIdentity => X == 0f && Y == 0f && Z == 0f && HeadingDeg == 0f;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0:0.###};{1:0.###};{2:0.###};{3:0.###}", X, Y, Z, HeadingDeg);

        public static bool TryParse(string text, out FlightFrame frame)
        {
            frame = default;
            string[] parts = (text ?? "").Split(';');
            if (parts.Length != 4) return false;
            CultureInfo inv = CultureInfo.InvariantCulture;
            return float.TryParse(parts[0], NumberStyles.Float, inv, out frame.X) && float.TryParse(parts[1], NumberStyles.Float, inv, out frame.Y)
                   && float.TryParse(parts[2], NumberStyles.Float, inv, out frame.Z) && float.TryParse(parts[3], NumberStyles.Float, inv, out frame.HeadingDeg);
        }

        /// <summary>A world point in the activity's frame.</summary>
        public void ToLocal(float wx, float wy, float wz, out float x, out float y, out float z)
        {
            double h = HeadingDeg * Math.PI / 180.0, c = Math.Cos(h), s = Math.Sin(h);
            double dx = wx - X, dz = wz - Z;
            x = (float)(dx * c - dz * s);
            y = wy - Y;
            z = (float)(dx * s + dz * c);
        }

        /// <summary>A point of the activity's frame in the world.</summary>
        public void ToWorld(float lx, float ly, float lz, out float x, out float y, out float z)
        {
            double h = HeadingDeg * Math.PI / 180.0, c = Math.Cos(h), s = Math.Sin(h);
            x = (float)(X + lx * c + lz * s);
            y = Y + ly;
            z = (float)(Z - lx * s + lz * c);
        }

        /// <summary>One moment of a flight, recorded in the world, as the activity's frame sees it.</summary>
        public FlightSample Local(in FlightSample s)
        {
            if (IsIdentity) return s;
            FlightSample m = s;
            ToLocal(s.X, s.Y, s.Z, out m.X, out m.Y, out m.Z);
            Turn(s.Vx, s.Vz, false, out m.Vx, out m.Vz);
            double half = -HeadingDeg * Math.PI / 360.0;
            float ry = (float)Math.Sin(half), rw = (float)Math.Cos(half);
            m.Qx = rw * s.Qx + ry * s.Qz;
            m.Qy = rw * s.Qy + ry * s.Qw;
            m.Qz = rw * s.Qz - ry * s.Qx;
            m.Qw = rw * s.Qw - ry * s.Qy;
            return m;
        }

        /// <summary>A direction (a velocity) turned into the frame, or back with <paramref name="toWorld"/>.</summary>
        public void Turn(float vx, float vz, bool toWorld, out float x, out float z)
        {
            double h = (toWorld ? -HeadingDeg : HeadingDeg) * Math.PI / 180.0, c = Math.Cos(h), s = Math.Sin(h);
            x = (float)(vx * c - vz * s);
            z = (float)(vx * s + vz * c);
        }
    }

    /// <summary>Something placed for an activity: a box to hover in, a pad to land on, a pole, a flag, a gate, a target.</summary>
    public sealed class ActivityObject
    {
        public static readonly string[] Types = { "box", "pad", "pole", "flag", "gate", "target", "marker" };

        public string Type, Id;
        /// <summary>Centre in the activity's frame; Y is height above the ground of the start point.</summary>
        public double X, Y, Z;
        /// <summary>Width and depth (box), diameter (pad, pole), opening (gate).</summary>
        public double Size = 1;
        public double Height = 1;
        public double HeadingDeg;
        /// <summary>A target's mouth lifted above the horizon (90 = it lies on its back, mouth up).</summary>
        public double TiltDeg;
    }

    /// <summary>A failure or a change of weather at a moment of the attempt.</summary>
    public sealed class TimedChange
    {
        public double T;
        /// <summary>
        /// 0 or more: the change comes <see cref="T"/> seconds after this many steps of a guided exercise are done,
        /// not at a time of the clock (the pilot sets the pace, the failure follows). -1: by the clock.
        /// </summary>
        public int AfterStep = -1;
        /// <summary>A failure's name (<c>gps_lost</c>, <c>prop2</c> …) or a weather value (<c>wind</c>, <c>rain</c> …).</summary>
        public string Name;
        public double Value;
    }

    /// <summary>
    /// The part of an activity that says how it is flown (EDU_PLAN §4): where, with which drone, in which mode and
    /// view, what stands in the scene, what the weather is and what breaks when, and what ends the attempt. All of
    /// it is optional; an activity with no "setup" is flown with the drone and place the pilot has chosen.
    /// </summary>
    public sealed class ActivitySetup
    {
        public static readonly string[] Assists = { "off", "altitude", "position" };
        public static readonly string[] Views = { "fpv", "chase", "los" };
        public static readonly string[] Modes = { "acro", "angle", "horizon" };
        public static readonly string[] WeatherNames = { "wind", "wind_from", "gusts", "rain", "visibility", "temperature" };

        /// <summary>The place by the name of its environment in the track catalogue ("field", "hangar" …); empty = wherever the pilot is.</summary>
        public string Place = "";
        /// <summary>The drone by its catalogue id; empty = the pilot's own.</summary>
        public string Drone = "";
        public string Assist = "";
        public string View = "";
        /// <summary>
        /// The flight controller's mode the pilot must fly in ("acro", "angle", "horizon"); empty = whatever the
        /// pilot's own switch says. An exercise that teaches acro is flown in acro.
        /// </summary>
        public string Mode = "";
        /// <summary>The start point relative to the place's own start, and the way the drone faces.</summary>
        public double StartX, StartZ, StartHeadingDeg;
        public readonly List<ActivityObject> Objects = new List<ActivityObject>();
        public readonly Dictionary<string, double> Weather = new Dictionary<string, double>();
        public readonly List<TimedChange> Timeline = new List<TimedChange>();
        /// <summary>The attempt ends when the drone has flown and is on the ground again with its motors off.</summary>
        public bool EndOnLanding = true;
        /// <summary>The attempt ends when the drone crashes.</summary>
        public bool EndOnCrash = true;
        /// <summary>The attempt ends when the last step of a guided exercise is done.</summary>
        public bool EndOnSteps = true;
        /// <summary>The drone stays on the ground with its motors off for the whole attempt (a controls check).</summary>
        public bool OnTheGround;

        /// <summary>Reads the "setup" of an activity; every fault is reported in words a content author can act on.</summary>
        public static ActivitySetup Parse(Dictionary<string, object> setup, string activityId, Func<string, bool> knownFailure)
        {
            var s = new ActivitySetup();
            if (setup == null) return s;
            string where = "activity " + activityId + ", setup";
            s.Place = Json.Str(setup, "place", "");
            s.Drone = Json.Str(setup, "drone", "");
            s.Assist = Json.Str(setup, "assist", "");
            s.View = Json.Str(setup, "view", "");
            s.OnTheGround = Json.Flag(setup, "on_the_ground");
            s.Mode = Json.Str(setup, "mode", "");
            if (s.Mode.Length > 0 && Array.IndexOf(Modes, s.Mode) < 0) throw new FormatException(where + ": \"mode\" must be one of " + string.Join(", ", Modes));
            if (s.Assist.Length > 0 && Array.IndexOf(Assists, s.Assist) < 0) throw new FormatException(where + ": \"assist\" must be one of " + string.Join(", ", Assists));
            if (s.View.Length > 0 && Array.IndexOf(Views, s.View) < 0) throw new FormatException(where + ": \"view\" must be one of " + string.Join(", ", Views));
            Dictionary<string, object> start = Json.Obj(setup, "start");
            if (start != null)
            {
                s.StartX = Json.Num(start, "x", 0);
                s.StartZ = Json.Num(start, "z", 0);
                s.StartHeadingDeg = Json.Num(start, "heading", 0);
            }
            Dictionary<string, object> end = Json.Obj(setup, "end");
            if (end != null)
            {
                s.EndOnLanding = Json.Flag(end, "landing", true);
                s.EndOnCrash = Json.Flag(end, "crash", true);
                s.EndOnSteps = Json.Flag(end, "steps", true);
            }
            var ids = new HashSet<string>();
            foreach (object item in Json.Arr(setup, "objects") ?? new List<object>())
            {
                var o = item as Dictionary<string, object> ?? throw new FormatException(where + ": every object must be an object {…}");
                var obj = new ActivityObject
                {
                    Type = Json.Str(o, "type", ""),
                    Id = Json.Str(o, "id", ""),
                    X = Json.Num(o, "x", 0), Y = Json.Num(o, "y", 0), Z = Json.Num(o, "z", 0),
                    Size = Json.Num(o, "size", 1), Height = Json.Num(o, "height", 1), HeadingDeg = Json.Num(o, "heading", 0), TiltDeg = Json.Num(o, "tilt", 0),
                };
                if (Array.IndexOf(ActivityObject.Types, obj.Type) < 0) throw new FormatException(where + ": object type \"" + obj.Type + "\" is not one of " + string.Join(", ", ActivityObject.Types));
                if (obj.Size <= 0 || obj.Height <= 0 || obj.Size > 200 || obj.Height > 200) throw new FormatException(where + ": object " + obj.Type + " has a size or height outside 0 to 200 m");
                if (obj.Id.Length > 0 && !ids.Add(obj.Id)) throw new FormatException(where + ": two objects are called \"" + obj.Id + "\"");
                s.Objects.Add(obj);
            }
            Dictionary<string, object> weather = Json.Obj(setup, "weather");
            if (weather != null)
                foreach (KeyValuePair<string, object> kv in weather)
                {
                    if (Array.IndexOf(WeatherNames, kv.Key) < 0 || !(kv.Value is double value))
                        throw new FormatException(where + ": weather \"" + kv.Key + "\" is not a number of " + string.Join(", ", WeatherNames));
                    s.Weather[kv.Key] = value;
                }
            foreach (object item in Json.Arr(setup, "timeline") ?? new List<object>())
            {
                var o = item as Dictionary<string, object> ?? throw new FormatException(where + ": every timeline entry must be an object {…}");
                var change = new TimedChange { T = Json.Num(o, "t", -1), Name = Json.Str(o, "name", ""), Value = Json.Num(o, "value", 1), AfterStep = (int)Json.Num(o, "after_step", -1) };
                if (change.AfterStep >= 0 && change.T < 0) change.T = 0;
                if (change.T < 0) throw new FormatException(where + ": a timeline entry needs a time \"t\" (seconds from the start) or \"after_step\"");
                bool isWeather = Array.IndexOf(WeatherNames, change.Name) >= 0;
                if (!isWeather && (knownFailure == null || !knownFailure(change.Name)))
                    throw new FormatException(where + ": timeline name \"" + change.Name + "\" is neither a failure nor a weather value");
                s.Timeline.Add(change);
            }
            s.Timeline.Sort((a, b) => a.AfterStep != b.AfterStep ? a.AfterStep.CompareTo(b.AfterStep) : a.T.CompareTo(b.T));
            return s;
        }
    }
}
