using System;
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
