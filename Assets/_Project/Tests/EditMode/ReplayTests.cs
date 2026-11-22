using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ushqyr.Edu;

namespace Ushqyr.Tests
{
    /// <summary>The replay viewer's clock, graphs and comparison (EDU_PLAN F2), on small hand-made recordings.</summary>
    public sealed class ReplayTests
    {
        /// <summary>A flight along +Z at <paramref name="speed"/> m/s, 2 m up, starting at time stamp <paramref name="t0"/>.</summary>
        private static FlightRecording Line(float seconds, float speed, float t0 = 0f, float x = 0f)
        {
            var rec = new FlightRecording { Rate = 100f };
            int n = (int)(seconds * 100f) + 1;
            for (int i = 0; i < n; i++)
            {
                float t = i / 100f;
                rec.Samples.Add(new FlightSample
                {
                    T = t0 + t, X = x, Y = 2f, Z = speed * t, Vz = speed, Qw = 1f,
                    Throttle = 0.5f, Battery = 1f - t / 100f, Armed = true,
                });
            }
            return rec;
        }

        /// <summary>
        /// The quality gate of EDU_PLAN F1 on what is shipped: every activity has a reference flight, flown in the
        /// simulator, and that flight scores 90 or more under the activity's own rubric.
        /// </summary>
        [Test]
        public void EveryShippedActivity_HasAReferenceFlightThatScores90()
        {
            EduSession.ReloadActivities(); // the files on disk, not what an earlier test run read
            Assert.IsNotEmpty(EduSession.Activities);
            foreach (ActivityDefinition a in EduSession.Activities.Values)
            {
                FlightRecording reference = EduSession.Reference(a);
                Assert.IsNotNull(reference, a.Id + " has no reference flight");
                ScoreResult score = Rubric.Score(a, reference);
                Assert.GreaterOrEqual(score.Score, 90.0, a.Id + ": the reference flight must score 90 or more");
                Assert.IsTrue(score.Passed, a.Id);
            }
        }

        // ---------- an activity's own frame ----------

        [Test]
        public void Frame_TurnsAndShiftsPoints_ThereAndBack()
        {
            var frame = new FlightFrame { X = 10f, Y = 0.5f, Z = -4f, HeadingDeg = 90f };
            // The frame's +Z points east (+X of the world): one metre ahead of its origin is one metre east.
            frame.ToWorld(0f, 2f, 1f, out float wx, out float wy, out float wz);
            Assert.AreEqual(11f, wx, 1e-4f);
            Assert.AreEqual(2.5f, wy, 1e-4f);
            Assert.AreEqual(-4f, wz, 1e-4f);
            frame.ToLocal(wx, wy, wz, out float lx, out float ly, out float lz);
            Assert.AreEqual(0f, lx, 1e-4f);
            Assert.AreEqual(2f, ly, 1e-4f);
            Assert.AreEqual(1f, lz, 1e-4f);
            Assert.IsTrue(FlightFrame.TryParse(frame.ToString(), out FlightFrame read));
            Assert.AreEqual(90f, read.HeadingDeg);
            Assert.IsFalse(FlightFrame.TryParse("1;2;3", out _));
            Assert.IsTrue(default(FlightFrame).IsIdentity);
        }

        [Test]
        public void AFlightFlownElsewhere_ScoresTheSameInItsOwnFrame()
        {
            // The same straight flight, once at the world's origin and once somewhere else, facing another way.
            FlightRecording home = Line(10f, 2f);
            home.Event(3f, "photo", "0.00;2.00;6.00;0.0;-30.0;1.0");
            var frame = new FlightFrame { X = 40f, Y = 3f, Z = 25f, HeadingDeg = 135f };
            FlightRecording away = home.PlacedAt(frame);
            Assert.AreEqual(frame.ToString(), away.Notes["frame"]);
            Assert.AreNotEqual(home.Samples[500].X, away.Samples[500].X);
            Assert.AreEqual(135f, away.Samples[500].Heading, 0.01f, "the drone faces the way the frame does");
            Assert.AreEqual(home.Samples[500].Speed, away.Samples[500].Speed, 1e-4f);

            FlightRecording back = away.InOwnFrame();
            Assert.IsFalse(back.Notes.ContainsKey("frame"));
            for (int i = 0; i < home.Samples.Count; i += 97)
            {
                Assert.AreEqual(home.Samples[i].X, back.Samples[i].X, 1e-3f);
                Assert.AreEqual(home.Samples[i].Y, back.Samples[i].Y, 1e-3f);
                Assert.AreEqual(home.Samples[i].Z, back.Samples[i].Z, 1e-3f);
                Assert.AreEqual(home.Samples[i].Vz, back.Samples[i].Vz, 1e-3f);
                Assert.AreEqual(0f, back.Samples[i].Heading, 0.01f);
            }
            StringAssert.StartsWith("0.00;2.00;6.00;0.0", back.Events[0].Data, "the place and heading of a photo come back too");
            StringAssert.Contains(";135.0;", away.Events[0].Data);
            Assert.AreSame(home, home.InOwnFrame(), "a flight with no frame is left alone");
        }

        // ---------- the steps of a guided exercise ----------

        private static List<StepSpec> Steps(string json) => StepTracker.Parse((List<object>)Json.Parse(json), "test");

        [Test]
        public void Steps_AreFollowedInOrder_AndHoldingMustNotBreak()
        {
            List<StepSpec> steps = Steps("[{\"say\":\"up\",\"height\":3,\"tolerance\":0.3,\"for\":2},"
                + "{\"say\":\"box\",\"zone\":{\"x\":0,\"y\":3,\"z\":10,\"radius\":1,\"tolerance\":0.5},\"for\":1},"
                + "{\"say\":\"turn\",\"turn\":-360},{\"say\":\"land\",\"landed\":true}]");
            var t = new StepTracker(steps);
            var s = new FlightSample { Y = 3f, Qw = 1f, Armed = true };

            for (int i = 0; i < 150; i++) t.Feed(s, 0.01);          // 1.5 s at the height
            Assert.AreEqual(0, t.Done);
            Assert.AreEqual(0.75f, t.Progress, 0.02f);
            s.Y = 4f; t.Feed(s, 0.01);                              // left it: the count starts again
            Assert.AreEqual(0f, t.Progress);
            s.Y = 3.1f;
            for (int i = 0; i < 205; i++) t.Feed(s, 0.01);
            Assert.AreEqual(1, t.Done);
            Assert.AreEqual("box", t.Current.Say);

            s.Z = 10.5f;
            for (int i = 0; i < 105; i++) t.Feed(s, 0.01);
            Assert.AreEqual(2, t.Done);

            // A full turn to the left, 2 degrees a step; a turn to the right first does not count.
            for (int deg = 0; deg <= 90; deg += 2) { Face(ref s, deg); t.Feed(s, 0.01); }
            Assert.AreEqual(2, t.Done);
            for (int deg = 90; deg >= -350; deg -= 2) { Face(ref s, deg); t.Feed(s, 0.01); }
            Assert.AreEqual(2, t.Done, "350 degrees to the left of where the step began is not yet a full turn");
            for (int deg = -350; deg >= -362; deg -= 2) { Face(ref s, deg); t.Feed(s, 0.01); }
            Assert.AreEqual(3, t.Done, "the turn is counted from where the step began: the wrong-way 90 had to be made up for");

            s.Y = 0.05f; s.Armed = false;
            t.Feed(s, 0.01);
            Assert.IsTrue(t.Finished);
            Assert.IsNull(t.Current);
        }

        private static void Face(ref FlightSample s, float headingDeg)
        {
            double half = headingDeg * Math.PI / 360.0;
            s.Qx = s.Qz = 0f;
            s.Qy = (float)Math.Sin(half);
            s.Qw = (float)Math.Cos(half);
        }

        [Test]
        public void AZoneWithNoHoldingTime_StillHasToBeEntered()
        {
            // Found by a reference flight that hovered on the spot and was credited with a whole lap of waypoints.
            List<StepSpec> lap = Steps("[{\"say\":\"a\",\"zone\":{\"x\":5,\"y\":2,\"z\":10,\"radius\":2}},{\"say\":\"b\",\"zone\":{\"x\":0,\"y\":2,\"z\":20,\"radius\":2}},"
                + "{\"say\":\"c\",\"heading\":90,\"tolerance\":10},{\"say\":\"d\",\"height\":8}]");
            var t = new StepTracker(lap);
            var s = new FlightSample { Y = 2f, Qw = 1f, Armed = true };
            for (int i = 0; i < 500; i++) t.Feed(s, 0.01);
            Assert.AreEqual(0, t.Done, "hovering at the start is none of the waypoints");
            s.X = 5f; s.Z = 10f; t.Feed(s, 0.01);
            Assert.AreEqual(1, t.Done, "one moment inside is enough when no time is asked for");
            s.X = 0f; s.Z = 20f; t.Feed(s, 0.01);
            Assert.AreEqual(2, t.Done);
            t.Feed(s, 0.01);
            Assert.AreEqual(2, t.Done, "facing north is not facing east");
            Face(ref s, 92f); t.Feed(s, 0.01);
            Assert.AreEqual(3, t.Done);
            t.Feed(s, 0.01);
            Assert.AreEqual(3, t.Done, "2 m is not 8 m");
        }

        [Test]
        public void StickSteps_NeedTheStickToBeMovedThere()
        {
            List<StepSpec> steps = Steps("[{\"say\":\"a\",\"stick\":\"throttle\",\"above\":0.8},{\"say\":\"b\",\"stick\":\"throttle\",\"below\":0.2},"
                + "{\"say\":\"c\",\"stick\":\"roll\",\"above\":0.7},{\"say\":\"d\",\"stick\":\"pitch\",\"below\":-0.7}]");
            var t = new StepTracker(steps);
            var s = new FlightSample { Qw = 1f, Throttle = 1f };
            t.Feed(s, 0.01);
            Assert.AreEqual(0, t.Done, "a stick that already was there has not been moved");
            s.Throttle = 0.5f; t.Feed(s, 0.01);
            s.Throttle = 0.9f; t.Feed(s, 0.01);
            Assert.AreEqual(1, t.Done);
            s.Throttle = 0.1f; t.Feed(s, 0.01);
            Assert.AreEqual(2, t.Done);
            s.Pitch = -1f; t.Feed(s, 0.01);
            Assert.AreEqual(2, t.Done, "the wrong stick does nothing");
            s.Pitch = 0f; s.Roll = 0.8f; t.Feed(s, 0.01);
            Assert.AreEqual(3, t.Done);
            s.Pitch = -0.9f; t.Feed(s, 0.01);
            Assert.IsTrue(t.Finished);
        }

        /// <summary>A flight round (cx, cz) at radius r, 2 m up, nose on the centre, turning by degPerSec (clockwise positive).</summary>
        private static FlightRecording Circle(float cx, float cz, float r, float degPerSec, float seconds, float noseOffDeg = 0f)
        {
            var rec = new FlightRecording { Rate = 100f };
            for (int i = 0; i <= (int)(seconds * 100f); i++)
            {
                float t = i / 100f;
                double a = degPerSec * t * Math.PI / 180.0;
                var s = new FlightSample { T = t, X = cx + r * (float)Math.Sin(a), Y = 2f, Z = cz + r * (float)Math.Cos(a), Armed = true };
                Face(ref s, degPerSec * t + 180f + noseOffDeg); // from a bearing of a, the centre lies at a + 180
                rec.Samples.Add(s);
            }
            return rec;
        }

        [Test]
        public void CircleStep_CountsOnlyTheWayFlownAtTheRightDistance()
        {
            List<StepSpec> once = Steps("[{\"say\":\"c\",\"circle\":{\"x\":0,\"z\":10,\"degrees\":360,\"near\":3,\"far\":7}}]");
            Assert.AreEqual(1, StepTracker.CountDone(once, Circle(0f, 10f, 5f, 30f, 13f)), "a full circle clockwise");
            Assert.AreEqual(0, StepTracker.CountDone(once, Circle(0f, 10f, 5f, 30f, 11f)), "330 degrees is not a circle");
            Assert.AreEqual(0, StepTracker.CountDone(once, Circle(0f, 10f, 5f, -30f, 13f)), "the wrong way round");
            Assert.AreEqual(0, StepTracker.CountDone(once, Circle(0f, 10f, 1f, 30f, 13f)), "a small turn close to the pole is not the circle");
            List<StepSpec> back = Steps("[{\"say\":\"c\",\"circle\":{\"x\":0,\"z\":10,\"degrees\":-360,\"near\":3,\"far\":7}}]");
            Assert.AreEqual(1, StepTracker.CountDone(back, Circle(0f, 10f, 5f, -30f, 13f)));
            Assert.Throws<FormatException>(() => Steps("[{\"say\":\"c\",\"circle\":{\"degrees\":0}}]"));
        }

        [Test]
        public void PhotoStep_CountsOnlyAPhotoThatLooksIntoTheTarget()
        {
            // A bucket 2 m up at z = 8, its mouth facing the start (heading 180).
            List<StepSpec> steps = Steps("[{\"say\":\"p\",\"photo\":{\"x\":0,\"y\":2,\"z\":8,\"heading\":180}}]");
            StepSpec target = steps[0];
            Assert.IsTrue(target.PhotoAligned("0.00;2.00;4.00;0.0;0.0;1.0"), "straight in front, looking at it");
            Assert.IsFalse(target.PhotoAligned("0.00;2.00;4.00;90.0;0.0;1.0"), "in the right place, looking the wrong way");
            Assert.IsFalse(target.PhotoAligned("4.00;2.00;6.00;296.6;0.0;1.0"), "looking at it, but from the side: the mouth cannot be seen into");
            Assert.IsFalse(target.PhotoAligned("0.00;2.00;12.00;180.0;0.0;1.0"), "from behind");
            Assert.IsFalse(target.PhotoAligned("0.00;2.00;-20.00;0.0;0.0;1.0"), "too far away");
            Assert.IsTrue(target.PhotoAligned("0.50;2.30;4.00;352.9;-4.3;1.0"), "a little off the axis and aimed back at it");
            Assert.IsFalse(target.PhotoAligned("nonsense"));

            // A bucket on the ground with its mouth up: the camera must look straight down from above it.
            StepSpec up = Steps("[{\"say\":\"p\",\"photo\":{\"x\":3,\"y\":0.3,\"z\":5,\"tilt\":90}}]")[0];
            Assert.IsTrue(up.PhotoAligned("3.00;4.00;5.00;0.0;-90.0;2.0"));
            Assert.IsFalse(up.PhotoAligned("3.00;4.00;5.00;0.0;0.0;2.0"), "above it, but looking at the horizon");

            // In a flight: the photo taken at the right place counts, at its own moment.
            FlightRecording flight = Line(10f, 1f);
            flight.Event(2f, "photo", "0.00;2.00;2.00;90.0;0.0;1.0");
            flight.Event(4f, "photo", "0.00;2.00;4.00;0.0;0.0;1.0");
            Assert.AreEqual(1, StepTracker.CountDone(steps, flight));
            Assert.AreEqual(0, StepTracker.CountDone(steps, Line(10f, 1f)), "no photo, no step");
        }

        [Test]
        public void AChange_CanFollowAStep()
        {
            const string head = "{\"format\":1,\"id\":\"x\",\"type\":\"exercise\",\"title\":\"t\",\"rubric\":[{\"id\":\"a\",\"metric\":\"time\",\"label\":\"l\",\"best\":1,\"worst\":9}],\"setup\":";
            ActivitySetup setup = ActivityDefinition.Parse(head + "{\"timeline\":[{\"after_step\":2,\"t\":3,\"name\":\"motor1\",\"value\":0.3},{\"after_step\":1,\"name\":\"battery_drop\",\"value\":0.7},{\"t\":5,\"name\":\"wind\",\"value\":4}]}}").Setup;
            Assert.AreEqual(-1, setup.Timeline[0].AfterStep, "changes by the clock come first");
            Assert.AreEqual(1, setup.Timeline[1].AfterStep);
            Assert.AreEqual(0, setup.Timeline[1].T, "no delay given: at once");
            Assert.AreEqual(3, setup.Timeline[2].T);
        }

        [Test]
        public void GateStep_IsFlownThroughTheOpening_TheRightWay()
        {
            // An opening 2 m wide and 2 m tall, its bottom 1 m up, at z = 10, flown northwards (+Z).
            List<StepSpec> gate = Steps("[{\"say\":\"g\",\"gate\":{\"x\":0,\"y\":1,\"z\":10,\"heading\":0,\"width\":2,\"height\":2}}]");
            Assert.AreEqual(1, StepTracker.CountDone(gate, Line(10f, 2f)), "straight through the middle at 2 m");
            Assert.AreEqual(0, StepTracker.CountDone(gate, Line(10f, 2f, x: 1.5f)), "past the post");
            Assert.AreEqual(0, StepTracker.CountDone(gate, Line(4f, 2f)), "stopped short of it");

            FlightRecording back = Line(10f, 2f);
            for (int i = 0; i < back.Samples.Count; i++)
            {
                FlightSample s = back.Samples[i];
                s.Z = 20f - s.Z; // the same line flown southwards
                back.Samples[i] = s;
            }
            Assert.AreEqual(0, StepTracker.CountDone(gate, back), "through the opening, but backwards");

            FlightRecording over = Line(10f, 2f);
            for (int i = 0; i < over.Samples.Count; i++)
            {
                FlightSample s = over.Samples[i];
                s.Y = 3.5f;
                over.Samples[i] = s;
            }
            Assert.AreEqual(0, StepTracker.CountDone(gate, over), "over the top bar");

            // Height band: 2 m is inside 1.5 to 2.5 all the way; only the stretch between the two lines is looked at.
            var p = new Dictionary<string, object> { ["low"] = 1.5, ["high"] = 2.5, ["zmin"] = 5.0, ["zmax"] = 15.0 };
            Assert.AreEqual(0.0, Metrics.Measure("height_in_band", p, over), 1e-6);
            for (int i = 0; i < over.Samples.Count; i++)
            {
                FlightSample s = over.Samples[i];
                if (s.Z >= 5f && s.Z < 10f) s.Y = 2f; // half of the stretch at the right height
                s.Armed = true;
                over.Samples[i] = s;
            }
            Assert.AreEqual(50.0, Metrics.Measure("height_in_band", p, over), 1.0);
        }

        [Test]
        public void LandingStep_CanAskForAPad()
        {
            List<StepSpec> steps = Steps("[{\"say\":\"l\",\"landed\":true,\"pad\":{\"x\":5,\"z\":5,\"radius\":0.5}}]");
            var t = new StepTracker(steps);
            var s = new FlightSample { Qw = 1f, X = 5f, Z = 6f, Y = 0.05f };
            t.Feed(s, 0.01);
            Assert.AreEqual(0, t.Done, "landed, but a metre from the pad");
            s.Z = 5.3f;
            t.Feed(s, 0.01);
            Assert.AreEqual(1, t.Done);
        }

        [Test]
        public void CirclingMeasures_LookOnlyAtTheCircle()
        {
            var p = new Dictionary<string, object> { ["x"] = 0.0, ["z"] = 10.0, ["radius"] = 5.0, ["near"] = 3.0, ["far"] = 7.0 };
            FlightRecording good = Circle(0f, 10f, 5.5f, 30f, 13f, noseOffDeg: 10f);
            Assert.AreEqual(0.5, Metrics.Measure("radius_error", p, good), 0.01);
            Assert.AreEqual(10.0, Metrics.Measure("aim_error", p, good), 0.1);
            // A long way to the circle and back does not spoil the measure: it is outside "near" to "far".
            FlightRecording far = Circle(0f, 10f, 20f, 30f, 13f);
            Assert.IsTrue(double.IsNaN(Metrics.Measure("radius_error", p, far)), "never on the circle: no value");
        }

        [Test]
        public void StepsDone_IsMeasuredFromTheRecording_InItsOwnFrame()
        {
            const string json = "{\"format\":1,\"id\":\"x\",\"type\":\"exercise\",\"title\":\"t\",\"rubric\":[{\"id\":\"s\",\"metric\":\"steps_done\",\"label\":\"l\",\"best\":2,\"worst\":0,"
                + "\"params\":{\"steps\":[{\"say\":\"go\",\"zone\":{\"x\":0,\"y\":2,\"z\":12,\"radius\":1},\"for\":0.5},{\"say\":\"on\",\"zone\":{\"x\":0,\"y\":2,\"z\":18,\"radius\":1},\"for\":0.5}]}}]}";
            ActivityDefinition a = ActivityDefinition.Parse(json);
            Assert.AreEqual(2, a.Steps.Count);
            FlightRecording flight = Line(10f, 2f); // reaches z = 12 at 6 s and z = 18 at 9 s
            Assert.AreEqual(100, Rubric.Score(a, flight).Score);
            Assert.AreEqual(50, Rubric.Score(a, Line(7f, 2f)).Score, "half the steps, half the points");
            // Flown somewhere else, facing another way: the same steps are found.
            Assert.AreEqual(100, Rubric.Score(a, flight.PlacedAt(new FlightFrame { X = 80f, Y = 1f, Z = -30f, HeadingDeg = 200f })).Score);

            Assert.Throws<FormatException>(() => ActivityDefinition.Parse(json.Replace("\"zone\"", "\"cloud\"")));
            Assert.Throws<FormatException>(() => ActivityDefinition.Parse(json.Replace("\"say\":\"go\",", "")));
            Assert.Throws<FormatException>(() => ActivityDefinition.Parse(json.Replace("\"steps\"", "\"stepz\"")));
        }

        [Test]
        public void Setup_IsReadAndChecked()
        {
            const string head = "{\"format\":1,\"id\":\"x\",\"type\":\"exercise\",\"title\":\"t\",\"rubric\":[{\"id\":\"a\",\"metric\":\"time\",\"label\":\"l\",\"best\":1,\"worst\":9}],\"setup\":";
            ActivityDefinition a = ActivityDefinition.Parse(head + "{\"place\":\"field\",\"drone\":\"edu-camera900\",\"assist\":\"position\",\"view\":\"chase\","
                + "\"start\":{\"x\":2,\"z\":-3,\"heading\":90},\"weather\":{\"wind\":4,\"rain\":0.5},"
                + "\"objects\":[{\"type\":\"box\",\"id\":\"b\",\"y\":2,\"size\":2,\"height\":1},{\"type\":\"pad\",\"size\":1.5}],"
                + "\"timeline\":[{\"t\":20,\"name\":\"gps_lost\"},{\"t\":5,\"name\":\"wind\",\"value\":8}],\"end\":{\"crash\":false}}}");
            Assert.AreEqual("edu-camera900", a.Setup.Drone);
            Assert.AreEqual(90, a.Setup.StartHeadingDeg);
            Assert.AreEqual(2, a.Setup.Objects.Count);
            Assert.AreEqual(4, a.Setup.Weather["wind"]);
            Assert.AreEqual("wind", a.Setup.Timeline[0].Name, "the timeline is in order of time");
            Assert.AreEqual(1, a.Setup.Timeline[1].Value, "a failure with no value is switched on");
            Assert.IsTrue(a.Setup.EndOnLanding);
            Assert.IsFalse(a.Setup.EndOnCrash);

            Assert.IsNotNull(ActivityDefinition.Parse(head + "{}}").Setup, "an empty setup is fine");
            foreach (string bad in new[]
                     {
                         "{\"assist\":\"magic\"}", "{\"view\":\"top\"}", "{\"objects\":[{\"type\":\"tree\"}]}", "{\"objects\":[{\"type\":\"box\",\"size\":0}]}",
                         "{\"objects\":[{\"type\":\"box\",\"id\":\"a\"},{\"type\":\"pad\",\"id\":\"a\"}]}", "{\"weather\":{\"snow\":1}}",
                         "{\"timeline\":[{\"t\":3,\"name\":\"wings_fall_off\"}]}", "{\"timeline\":[{\"name\":\"gps_lost\"}]}",
                     })
                Assert.Throws<FormatException>(() => ActivityDefinition.Parse(head + bad + "}"), bad);
        }

        /// <summary>
        /// The other half of the quality gate: flights that did not do the exercise do not pass it. Three of them for
        /// every shipped activity: the drone never leaves the ground; it takes off, hovers where it started and comes
        /// down again; and it flies off in a straight line and never comes back.
        /// </summary>
        [Test]
        public void EveryShippedActivity_FailsFlightsThatDidNotDoIt()
        {
            FlightRecording Flight(Func<float, FlightSample> at, float seconds)
            {
                var rec = new FlightRecording { Rate = 100f };
                for (int i = 0; i <= (int)(seconds * 100f); i++)
                {
                    FlightSample s = at(i / 100f);
                    s.T = i / 100f;
                    s.Qw = 1f;
                    s.Battery = 1f;
                    rec.Samples.Add(s);
                }
                return rec;
            }
            EduSession.ReloadActivities(); // the files on disk, not what an earlier test run read
            FlightRecording idle = Flight(t => new FlightSample { Y = 0.02f }, 30f);
            // Up to 2 m in 2 s, 20 s there, down in 2 s, motors off: right for "take off, hover, land" only.
            FlightRecording hop = Flight(t => new FlightSample
            {
                Y = t < 2f ? t : t < 22f ? 2f : t < 24f ? 24f - t : 0.02f, Vy = t < 2f ? 1f : t >= 22f && t < 24f ? -1f : 0f,
                Armed = t < 24f, Throttle = 0.5f,
            }, 26f);
            FlightRecording away = Flight(t => new FlightSample { X = 3f * t, Y = 2f, Vx = 3f, Armed = true, Throttle = 0.5f }, 40f);

            foreach (ActivityDefinition a in EduSession.Activities.Values)
            {
                Assert.IsFalse(Rubric.Score(a, idle).Passed, a.Id + ": a drone that never took off must not pass");
                Assert.IsFalse(Rubric.Score(a, away).Passed, a.Id + ": flying away in a straight line must not pass");
                if (a.Id != "a02-hover-land") Assert.IsFalse(Rubric.Score(a, hop).Passed, a.Id + ": a plain take-off and landing must not pass");
            }
        }

        [Test]
        public void Playback_RunsAtTheChosenSpeed_AndStopsAtTheEnd()
        {
            var p = new ReplayPlayback(Line(10f, 1f));
            Assert.AreEqual(10f, p.End, 1e-3f);
            p.Advance(1f);
            Assert.AreEqual(0f, p.Time, "paused until asked to play");
            p.Toggle();
            p.Advance(1f);
            Assert.AreEqual(1f, p.Time, 1e-5f);
            p.Faster(); p.Faster(); p.Faster();
            Assert.AreEqual(4f, p.Speed, "never faster than 4x");
            p.Advance(1f);
            Assert.AreEqual(5f, p.Time, 1e-5f);
            for (int i = 0; i < 10; i++) p.Slower();
            Assert.AreEqual(0.1f, p.Speed, "never slower than 0.1x");
            p.Advance(1f);
            Assert.AreEqual(5.1f, p.Time, 1e-5f);
            p.SetSpeed(1.9f);
            Assert.AreEqual(2f, p.Speed);
            p.Advance(60f);
            Assert.AreEqual(10f, p.Time);
            Assert.IsFalse(p.Playing, "stops at the end");
            p.Toggle();
            Assert.AreEqual(0f, p.Time, "play at the end starts again");
            Assert.IsTrue(p.Playing);
        }

        [Test]
        public void Playback_SeekAndStepStayInsideTheFlight()
        {
            var p = new ReplayPlayback(Line(10f, 1f));
            p.Playing = true;
            p.Step(0.05f);
            Assert.IsFalse(p.Playing, "a step pauses");
            Assert.AreEqual(0.05f, p.Time, 1e-6f);
            p.Seek(-3f);
            Assert.AreEqual(0f, p.Time);
            p.Seek(99f);
            Assert.AreEqual(10f, p.Time);
            p.Seek(float.NaN);
            Assert.AreEqual(0f, p.Time);
        }

        [Test]
        public void SampleAt_BlendsBetweenSamples_AndCountsFromTheFirstKeptSample()
        {
            FlightRecording rec = Line(10f, 2f, t0: 300f); // a rolling recording: its first sample is not at 0
            FlightSample s = ReplayPlayback.SampleAt(rec, 1.005f);
            Assert.AreEqual(2.01f, s.Z, 1e-3f);
            Assert.AreEqual(2f, s.Speed, 1e-5f);
            Assert.AreEqual(1f, s.Qw, 1e-5f);
            Assert.AreEqual(0f, ReplayPlayback.SampleAt(rec, -5f).Z, 1e-5f);
            Assert.AreEqual(20f, ReplayPlayback.SampleAt(rec, 50f).Z, 1e-3f);

            var p = new ReplayPlayback(rec);
            p.Seek(4f);
            Assert.AreEqual(304f, p.RecordingTime, 1e-3f);
            Assert.AreEqual(8f, p.Now.Z, 1e-3f);
        }

        [Test]
        public void SampleAt_TurnsTheShortWay()
        {
            // The same heading written with both signs of the quaternion: the blend must not swing through zero.
            var rec = new FlightRecording { Rate = 100f };
            float h = (float)Math.Sin(Math.PI / 4), c = (float)Math.Cos(Math.PI / 4);
            rec.Samples.Add(new FlightSample { T = 0f, Qy = h, Qw = c });
            rec.Samples.Add(new FlightSample { T = 0.01f, Qy = -h, Qw = -c });
            FlightSample s = ReplayPlayback.SampleAt(rec, 0.005f);
            Assert.AreEqual(1f, s.Qx * s.Qx + s.Qy * s.Qy + s.Qz * s.Qz + s.Qw * s.Qw, 1e-5f);
            Assert.AreEqual(90f, s.Heading, 0.01f);
        }

        [Test]
        public void Events_AreGivenOnTheViewersClock()
        {
            FlightRecording rec = Line(10f, 1f, t0: 300f);
            rec.Event(306f, "crash", "4.2");
            rec.Event(302f, "gate", "1");
            rec.Event(120f, "gate", "0"); // from the part that was not kept
            var events = new ReplayPlayback(rec).Events();
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(2f, events[0].Key, 1e-3f);
            Assert.AreEqual("gate", events[0].Value.Type);
            Assert.AreEqual(6f, events[1].Key, 1e-3f);
        }

        [Test]
        public void Graph_KeepsAShortSpike_AndAHoverStaysFlat()
        {
            FlightRecording rec = Line(60f, 0f);
            FlightSample jab = rec.Samples[3210];
            jab.Throttle = 1f;
            rec.Samples[3210] = jab; // one sample, 10 ms long
            ReplayGraph throttle = ReplayGraph.Build(rec, ReplaySeries.Throttle, 200);
            Assert.AreEqual(200, throttle.High.Length);
            int column = Array.FindIndex(throttle.High, v => v >= 1f);
            Assert.That(column, Is.InRange(106, 107));
            Assert.AreEqual(1f, throttle.High[column], "the jab is in its column");
            Assert.AreEqual(0.5f, throttle.Low[column]);
            Assert.AreEqual(0f, throttle.Min);
            Assert.AreEqual(1f, throttle.Max);

            ReplayGraph height = ReplayGraph.Build(rec, ReplaySeries.Height, 200, groundY: 0.5f);
            Assert.AreEqual(1.5f, height.High[0], 1e-5f, "height above the ground");
            Assert.AreEqual(0f, height.Min);
            Assert.GreaterOrEqual(height.Max - height.Min, 1f);

            ReplayGraph more = ReplayGraph.Build(Line(0.05f, 0f), ReplaySeries.Speed, 50);
            Assert.AreEqual(0f, more.High[49], "more columns than samples still draws a line");
        }

        [Test]
        public void Comparison_GivesTheTimeBehindOnTheSamePath()
        {
            FlightRecording student = Line(10f, 2f), reference = Line(10f, 2.5f);
            var c = new ReplayComparison(student, reference);
            // At 5 s the student is at 10 m; the reference was there at 4 s.
            Assert.AreEqual(1f, c.GapAt(5f), 0.02f);
            Assert.AreEqual(0f, c.DistanceAt(5f), 0.02f);
            Assert.AreEqual(0f, c.GapAt(0f), 0.02f);

            var ahead = new ReplayComparison(reference, student);
            Assert.AreEqual(-1f, ahead.GapAt(4f), 0.02f, "negative when ahead");

            var elsewhere = new ReplayComparison(student, Line(10f, 2f, x: 30f));
            Assert.IsTrue(float.IsNaN(elsewhere.GapAt(5f)), "no gap between flights on different paths");
            Assert.AreEqual(30f, elsewhere.DistanceAt(5f), 0.1f);
        }

        [Test]
        public void Comparison_FollowsALap_NotTheNearestPointOfAnyLap()
        {
            // Two laps of a 10 m circle. On the second lap the nearest point of the other flight is also on lap one.
            FlightRecording Circle(float lapSeconds)
            {
                var rec = new FlightRecording { Rate = 100f };
                int n = (int)(lapSeconds * 2f * 100f) + 1;
                for (int i = 0; i < n; i++)
                {
                    float t = i / 100f;
                    double a = 2.0 * Math.PI * t / lapSeconds;
                    rec.Samples.Add(new FlightSample { T = t, X = (float)(10.0 * Math.Sin(a)), Y = 2f, Z = (float)(10.0 * Math.Cos(a)), Qw = 1f });
                }
                return rec;
            }
            var c = new ReplayComparison(Circle(10f), Circle(8f));
            // At 15 s the slower flight is halfway round lap two; the faster one was there at 12 s.
            Assert.AreEqual(3f, c.GapAt(15f), 0.05f);
        }
    }
}
