using System;
using System.Collections.Generic;

namespace Ushqyr.Edu
{
    /// <summary>One thing to do in a guided exercise, and how the flight shows that it was done.</summary>
    public sealed class StepSpec
    {
        public static readonly string[] Sticks = { "throttle", "yaw", "pitch", "roll" };

        /// <summary>What the pilot is told (a key of the language table).</summary>
        public string Say = "";
        /// <summary>"stick", "zone", "height", "turn", "heading" or "landed".</summary>
        public string Kind = "";
        public string Stick = "";
        /// <summary>Stick: the value to go above (or below, when <see cref="Below"/>).</summary>
        public double Level;
        public bool Below;
        /// <summary>Zone: a cylinder at (X, Z) of this radius, around height Y within the tolerance. Height: Y and the tolerance alone.</summary>
        public double X, Y, Z, Radius = 1, Tolerance = 0.5;
        /// <summary>Seconds the condition must hold without a break.</summary>
        public double For;
        /// <summary>Turn: degrees to turn on the spot, clockwise positive. Heading: the heading to face.</summary>
        public double Degrees;
    }

    /// <summary>
    /// Follows a flight through the steps of a guided exercise (EDU_PLAN §4: "on-screen guidance"): what to do now,
    /// how far the current step is, how many are done. The same tracker tells the pilot what to do while flying and
    /// counts the steps when the recording is graded, so the two can never disagree. Samples are in the activity's
    /// own frame.
    /// </summary>
    public sealed class StepTracker
    {
        private readonly List<StepSpec> _steps;
        private double _held, _turned;
        private float _lastHeading;
        private bool _hasHeading, _wasAway;

        public StepTracker(List<StepSpec> steps)
        {
            _steps = steps ?? new List<StepSpec>();
        }

        public int Count => _steps.Count;
        public int Done { get; private set; }
        public bool Finished => Done >= _steps.Count;
        public StepSpec Current => Finished ? null : _steps[Done];
        /// <summary>0..1: how much of the current step's holding time or turn is done.</summary>
        public float Progress { get; private set; }

        /// <summary>Takes the next moment of the flight; <paramref name="dt"/> is the time since the one before.</summary>
        public void Feed(in FlightSample s, double dt)
        {
            if (Finished) return;
            StepSpec step = _steps[Done];
            bool met;
            switch (step.Kind)
            {
                case "stick":
                {
                    bool there = StickThere(step, s);
                    // The stick has to be moved there, not found there: it must have been away from the mark first.
                    if (!there) _wasAway = true;
                    met = there && _wasAway;
                    Progress = met ? 1f : 0f;
                    break;
                }
                case "zone":
                case "height":
                {
                    bool inside = Math.Abs(s.Y - step.Y) <= step.Tolerance;
                    if (inside && step.Kind == "zone")
                    {
                        double dx = s.X - step.X, dz = s.Z - step.Z;
                        inside = dx * dx + dz * dz <= step.Radius * step.Radius;
                    }
                    _held = inside ? _held + dt : 0;
                    met = _held >= step.For;
                    Progress = step.For > 0 ? (float)Math.Min(1.0, _held / step.For) : inside ? 1f : 0f;
                    break;
                }
                case "heading":
                {
                    double off = Math.Abs(((s.Heading - step.Degrees) % 360 + 540) % 360 - 180);
                    _held = off <= step.Tolerance ? _held + dt : 0;
                    met = _held >= step.For;
                    Progress = step.For > 0 ? (float)Math.Min(1.0, _held / step.For) : off <= step.Tolerance ? 1f : 0f;
                    break;
                }
                case "turn":
                {
                    if (_hasHeading)
                    {
                        float d = ((s.Heading - _lastHeading) % 360f + 540f) % 360f - 180f;
                        _turned += d;
                    }
                    _lastHeading = s.Heading;
                    _hasHeading = true;
                    double share = step.Degrees != 0 ? _turned / step.Degrees : 1;
                    met = share >= 1;
                    Progress = (float)Math.Max(0.0, Math.Min(1.0, share));
                    break;
                }
                case "landed":
                    met = !s.Armed && s.Y < 0.3f && s.Speed < 0.3f;
                    Progress = met ? 1f : 0f;
                    break;
                default:
                    met = false;
                    break;
            }
            if (!met) return;
            Done++;
            _held = _turned = 0;
            _hasHeading = false;
            Progress = 0f;
            // The next step starts from this very moment: a stick that is not at its mark now counts as "away" already.
            _wasAway = !Finished && _steps[Done].Kind == "stick" && !StickThere(_steps[Done], s);
        }

        private static bool StickThere(StepSpec step, in FlightSample s)
        {
            double v = step.Stick == "throttle" ? s.Throttle : step.Stick == "yaw" ? s.Yaw : step.Stick == "pitch" ? s.Pitch : s.Roll;
            return step.Below ? v <= step.Level : v >= step.Level;
        }

        /// <summary>How many steps a recorded flight completed, in order.</summary>
        public static int CountDone(List<StepSpec> steps, FlightRecording flight)
        {
            var tracker = new StepTracker(steps);
            double dt = flight.Rate > 0 ? 1.0 / flight.Rate : 0.01;
            foreach (FlightSample s in flight.Samples)
            {
                tracker.Feed(s, dt);
                if (tracker.Finished) break;
            }
            return tracker.Done;
        }

        /// <summary>Reads a list of steps; every fault is reported in words a content author can act on.</summary>
        public static List<StepSpec> Parse(List<object> list, string where)
        {
            var steps = new List<StepSpec>();
            if (list == null) return steps;
            foreach (object item in list)
            {
                var o = item as Dictionary<string, object> ?? throw new FormatException(where + ": every step must be an object {…}");
                var step = new StepSpec { Say = Json.Str(o, "say", ""), For = Json.Num(o, "for", 0) };
                if (step.Say.Length == 0) throw new FormatException(where + ": a step has no \"say\" (what the pilot is told)");
                if (step.For < 0 || step.For > 600) throw new FormatException(where + ": \"for\" must be between 0 and 600 seconds");
                if (o.ContainsKey("stick"))
                {
                    step.Kind = "stick";
                    step.Stick = Json.Str(o, "stick", "");
                    if (Array.IndexOf(StepSpec.Sticks, step.Stick) < 0) throw new FormatException(where + ": \"stick\" must be one of " + string.Join(", ", StepSpec.Sticks));
                    if (o.ContainsKey("below")) { step.Below = true; step.Level = Json.Num(o, "below", 0); }
                    else if (o.ContainsKey("above")) step.Level = Json.Num(o, "above", 0);
                    else throw new FormatException(where + ": a stick step needs \"above\" or \"below\"");
                }
                else if (o.ContainsKey("zone"))
                {
                    Dictionary<string, object> z = Json.Obj(o, "zone") ?? throw new FormatException(where + ": \"zone\" must be an object {x, y, z, radius, tolerance}");
                    step.Kind = "zone";
                    step.X = Json.Num(z, "x", 0); step.Y = Json.Num(z, "y", 0); step.Z = Json.Num(z, "z", 0);
                    step.Radius = Json.Num(z, "radius", 1); step.Tolerance = Json.Num(z, "tolerance", 0.5);
                    if (step.Radius <= 0 || step.Tolerance <= 0) throw new FormatException(where + ": a zone's radius and tolerance must be above 0");
                }
                else if (o.ContainsKey("height"))
                {
                    step.Kind = "height";
                    step.Y = Json.Num(o, "height", 0);
                    step.Tolerance = Json.Num(o, "tolerance", 0.3);
                    if (step.Tolerance <= 0) throw new FormatException(where + ": a height's tolerance must be above 0");
                }
                else if (o.ContainsKey("turn"))
                {
                    step.Kind = "turn";
                    step.Degrees = Json.Num(o, "turn", 0);
                    if (step.Degrees == 0) throw new FormatException(where + ": \"turn\" needs a number of degrees (negative turns left)");
                }
                else if (o.ContainsKey("heading"))
                {
                    step.Kind = "heading";
                    step.Degrees = Json.Num(o, "heading", 0);
                    step.Tolerance = Json.Num(o, "tolerance", 15);
                }
                else if (Json.Flag(o, "landed")) step.Kind = "landed";
                else throw new FormatException(where + ": a step needs one of \"stick\", \"zone\", \"height\", \"turn\", \"heading\" or \"landed\"");
                steps.Add(step);
            }
            return steps;
        }
    }
}
