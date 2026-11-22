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
        /// <summary>"stick", "zone", "height", "turn", "heading", "circle", "gate", "photo" or "landed".</summary>
        public string Kind = "";
        public string Stick = "";
        /// <summary>Stick: the value to go above (or below, when <see cref="Below"/>).</summary>
        public double Level;
        public bool Below;
        /// <summary>Zone: a cylinder at (X, Z) of this radius, around height Y within the tolerance. Height: Y and the tolerance alone.</summary>
        public double X, Y, Z, Radius = 1, Tolerance = 0.5;
        /// <summary>Seconds the condition must hold without a break.</summary>
        public double For;
        /// <summary>
        /// Turn: degrees to turn on the spot, clockwise positive. Heading: the heading to face. Circle: degrees to
        /// fly round the point (X, Z), clockwise seen from above positive.
        /// </summary>
        public double Degrees;
        /// <summary>Circle: only flying at a distance between these from the point counts (m).</summary>
        public double Near, Far = 1000;
        /// <summary>
        /// Gate: an opening <see cref="Width"/> wide and <see cref="Height"/> tall whose bottom middle is at (X, Y, Z);
        /// it is flown through along <see cref="Degrees"/> (a heading). Flying through it backwards does not count.
        /// </summary>
        public double Width = 2, Height = 2;
        /// <summary>Landed: true when the landing must be inside the circle (X, Z, Radius).</summary>
        public bool OnPad;
        /// <summary>
        /// Photo: the target's mouth is at (X, Y, Z) and faces along <see cref="Degrees"/> (a heading), lifted by
        /// <see cref="TiltDeg"/> (90 = straight up). The photo counts when the camera is in front of the mouth within
        /// <see cref="ConeDeg"/> of its axis, between <see cref="Near"/> and <see cref="Far"/> away, and points at it
        /// within <see cref="AimDeg"/>.
        /// </summary>
        public double TiltDeg, ConeDeg = 15, AimDeg = 8;

        /// <summary>
        /// True when a photo (the data of a "photo" event: x;y;z;heading;tilt;zoom, in the activity's frame) looks into
        /// this step's target.
        /// </summary>
        public bool PhotoAligned(string photo)
        {
            string[] parts = (photo ?? "").Split(';');
            if (parts.Length < 5) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var n = new double[5];
            for (int i = 0; i < 5; i++)
                if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, inv, out n[i])) return false;
            const double rad = Math.PI / 180.0;
            // The way the mouth faces, and the way the camera looks.
            double ax = Math.Sin(Degrees * rad) * Math.Cos(TiltDeg * rad), ay = Math.Sin(TiltDeg * rad), az = Math.Cos(Degrees * rad) * Math.Cos(TiltDeg * rad);
            double fx = Math.Sin(n[3] * rad) * Math.Cos(n[4] * rad), fy = Math.Sin(n[4] * rad), fz = Math.Cos(n[3] * rad) * Math.Cos(n[4] * rad);
            double dx = n[0] - X, dy = n[1] - Y, dz = n[2] - Z, distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < Near || distance > Far || distance < 1e-6) return false;
            double inFront = Math.Acos(Math.Max(-1, Math.Min(1, (dx * ax + dy * ay + dz * az) / distance))) / rad;
            double aim = Math.Acos(Math.Max(-1, Math.Min(1, -(dx * fx + dy * fy + dz * fz) / distance))) / rad;
            return inFront <= ConeDeg && aim <= AimDeg;
        }
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
        private bool _hasHeading, _wasAway, _hasBefore;
        private FlightSample _before;

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
                    // Being there is what counts; with no holding time asked for, one moment inside is enough.
                    met = inside && _held >= step.For;
                    Progress = step.For > 0 ? (float)Math.Min(1.0, _held / step.For) : inside ? 1f : 0f;
                    break;
                }
                case "heading":
                {
                    double off = Math.Abs(((s.Heading - step.Degrees) % 360 + 540) % 360 - 180);
                    _held = off <= step.Tolerance ? _held + dt : 0;
                    met = off <= step.Tolerance && _held >= step.For;
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
                case "gate":
                {
                    // The plane of the gate, crossed from behind it to in front of it, inside the opening.
                    const double rad = Math.PI / 180.0;
                    double fx = Math.Sin(step.Degrees * rad), fz = Math.Cos(step.Degrees * rad);
                    double along = (s.X - step.X) * fx + (s.Z - step.Z) * fz;
                    met = false;
                    if (_hasBefore)
                    {
                        double alongBefore = (_before.X - step.X) * fx + (_before.Z - step.Z) * fz;
                        if (alongBefore < 0 && along >= 0)
                        {
                            double k = alongBefore / (alongBefore - along);
                            double x = _before.X + (s.X - _before.X) * k, y = _before.Y + (s.Y - _before.Y) * k, z = _before.Z + (s.Z - _before.Z) * k;
                            double across = (x - step.X) * fz - (z - step.Z) * fx;
                            met = Math.Abs(across) <= step.Width * 0.5 && y >= step.Y && y <= step.Y + step.Height;
                        }
                    }
                    Progress = 0f;
                    break;
                }
                case "circle":
                {
                    double dx = s.X - step.X, dz = s.Z - step.Z, distance = Math.Sqrt(dx * dx + dz * dz);
                    float bearing = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                    // Only the way flown at the right distance counts; cutting across the middle earns nothing.
                    if (_hasHeading && distance >= step.Near && distance <= step.Far)
                        _turned += ((bearing - _lastHeading) % 360f + 540f) % 360f - 180f;
                    _lastHeading = bearing;
                    _hasHeading = true;
                    double share = step.Degrees != 0 ? _turned / step.Degrees : 1;
                    met = share >= 1;
                    Progress = (float)Math.Max(0.0, Math.Min(1.0, share));
                    break;
                }
                case "landed":
                    met = !s.Armed && s.Y < 0.3f && s.Speed < 0.3f;
                    if (met && step.OnPad)
                    {
                        double dx = s.X - step.X, dz = s.Z - step.Z;
                        met = dx * dx + dz * dz <= step.Radius * step.Radius;
                    }
                    Progress = met ? 1f : 0f;
                    break;
                default:
                    met = false;
                    break;
            }
            _before = s;
            _hasBefore = true;
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

        /// <summary>
        /// Something that happened in the flight (an event of the recording, in the activity's frame). A photo step is
        /// done by a "photo" event that looks into its target; every other event is ignored.
        /// </summary>
        public void FeedEvent(in FlightEvent e)
        {
            if (Finished || e.Type != "photo") return;
            StepSpec step = _steps[Done];
            if (step.Kind != "photo" || !step.PhotoAligned(e.Data)) return;
            Done++;
            _held = _turned = 0;
            _hasHeading = _wasAway = false;
            Progress = 0f;
        }

        /// <summary>How many steps a recorded flight completed, in order.</summary>
        public static int CountDone(List<StepSpec> steps, FlightRecording flight)
        {
            var tracker = new StepTracker(steps);
            double dt = flight.Rate > 0 ? 1.0 / flight.Rate : 0.01;
            // Events are taken at their own moments, between the samples.
            var events = new List<FlightEvent>(flight.Events);
            events.Sort((a, b) => a.T.CompareTo(b.T));
            int next = 0;
            foreach (FlightSample s in flight.Samples)
            {
                while (next < events.Count && events[next].T <= s.T) tracker.FeedEvent(events[next++]);
                tracker.Feed(s, dt);
                if (tracker.Finished) break;
            }
            while (next < events.Count && !tracker.Finished) tracker.FeedEvent(events[next++]);
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
                else if (o.ContainsKey("gate"))
                {
                    Dictionary<string, object> g = Json.Obj(o, "gate") ?? throw new FormatException(where + ": \"gate\" must be an object {x, y, z, heading, width, height}");
                    step.Kind = "gate";
                    step.X = Json.Num(g, "x", 0); step.Y = Json.Num(g, "y", 0); step.Z = Json.Num(g, "z", 0);
                    step.Degrees = Json.Num(g, "heading", 0);
                    step.Width = Json.Num(g, "width", 2); step.Height = Json.Num(g, "height", 2);
                    if (step.Width <= 0 || step.Height <= 0) throw new FormatException(where + ": a gate's width and height must be above 0");
                }
                else if (o.ContainsKey("photo"))
                {
                    Dictionary<string, object> t = Json.Obj(o, "photo") ?? throw new FormatException(where + ": \"photo\" must be an object {x, y, z, heading, tilt, cone, aim, near, far}");
                    step.Kind = "photo";
                    step.X = Json.Num(t, "x", 0); step.Y = Json.Num(t, "y", 0); step.Z = Json.Num(t, "z", 0);
                    step.Degrees = Json.Num(t, "heading", 0); step.TiltDeg = Json.Num(t, "tilt", 0);
                    step.ConeDeg = Json.Num(t, "cone", 15); step.AimDeg = Json.Num(t, "aim", 8);
                    step.Near = Json.Num(t, "near", 1); step.Far = Json.Num(t, "far", 15);
                    if (step.ConeDeg <= 0 || step.AimDeg <= 0 || step.Far <= step.Near) throw new FormatException(where + ": a photo target needs a cone and an aim above 0, and \"far\" beyond \"near\"");
                }
                else if (o.ContainsKey("circle"))
                {
                    Dictionary<string, object> c = Json.Obj(o, "circle") ?? throw new FormatException(where + ": \"circle\" must be an object {x, z, degrees, near, far}");
                    step.Kind = "circle";
                    step.X = Json.Num(c, "x", 0); step.Z = Json.Num(c, "z", 0);
                    step.Degrees = Json.Num(c, "degrees", 360);
                    step.Near = Json.Num(c, "near", 0); step.Far = Json.Num(c, "far", 1000);
                    if (step.Degrees == 0 || step.Near < 0 || step.Far <= step.Near)
                        throw new FormatException(where + ": a circle needs degrees other than 0 and \"far\" beyond \"near\"");
                }
                else if (Json.Flag(o, "landed"))
                {
                    step.Kind = "landed";
                    Dictionary<string, object> pad = Json.Obj(o, "pad");
                    if (pad != null)
                    {
                        step.OnPad = true;
                        step.X = Json.Num(pad, "x", 0); step.Z = Json.Num(pad, "z", 0);
                        step.Radius = Json.Num(pad, "radius", 0.75);
                        if (step.Radius <= 0) throw new FormatException(where + ": a pad's radius must be above 0");
                    }
                }
                else throw new FormatException(where + ": a step needs one of \"stick\", \"zone\", \"height\", \"turn\", \"heading\", \"circle\", \"gate\", \"photo\" or \"landed\"");
                steps.Add(step);
            }
            return steps;
        }
    }
}
