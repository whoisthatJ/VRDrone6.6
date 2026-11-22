using System.Collections.Generic;
using NUnit.Framework;
using Ushqyr.Edu;

namespace Ushqyr.Tests
{
    /// <summary>Race timing by FAI F9U from gate passes: laps, splits, the missed gate, the false start, positions and gaps.</summary>
    public class RaceTimingTests
    {
        private static readonly string[] Two = { "A", "B" };

        /// <summary>Flies whole laps of a four-gate course: gates 1, 2, 3 and the line, a quarter of the lap time apart.</summary>
        private static double Lap(RaceTiming r, string pilot, double from, double lapTime)
        {
            for (int g = 1; g <= 4; g++) r.Gate(pilot, g % 4, from + lapTime * g / 4.0);
            return from + lapTime;
        }

        [Test]
        public void AnEliminationRace_IsTimedFromTheSignal_WithSplitsPositionsAndGaps()
        {
            var r = new RaceTiming(Two, 4, 3, true);
            Assert.IsFalse(r.Started);
            r.Start(100);
            r.LeftStart("A", 100.2);
            r.LeftStart("B", 100.3);
            r.Gate("A", 0, 102.0);   // holeshot 2.0
            Assert.AreEqual(1, r.Of("A").Position);
            Assert.IsTrue(double.IsNaN(r.Gap("B")), "B has passed nothing yet");
            r.Gate("B", 0, 102.5);   // holeshot 2.5
            Assert.AreEqual(0.5, r.Gap("B"), 1e-9);
            Assert.AreEqual(0, r.Gap("A"), 1e-9);

            double a = 102.0, b = 102.5;
            a = Lap(r, "A", a, 20);     // A: 20, 21, 22
            b = Lap(r, "B", b, 19);     // B: 19, 20, 21: B takes the lead on lap one
            Assert.AreEqual(1, r.Of("B").Position, "B crossed the line at 121.5, A at 122.0");
            Assert.AreEqual(0.5, r.Gap("A"), 1e-9);
            a = Lap(r, "A", a, 21);
            b = Lap(r, "B", b, 20);
            Assert.IsFalse(r.Over);
            a = Lap(r, "A", a, 22);
            b = Lap(r, "B", b, 21);
            Assert.IsTrue(r.Over);

            PilotTiming pa = r.Of("A"), pb = r.Of("B");
            Assert.AreEqual(new[] { 20.0, 21.0, 22.0 }, pa.Laps.ToArray());
            Assert.AreEqual(2.0, pa.Holeshot, 1e-9);
            Assert.AreEqual(65.0, pa.RaceTime, 1e-9, "from the signal: 2 + 20 + 21 + 22");
            Assert.AreEqual(62.5, pb.RaceTime, 1e-9);
            Assert.AreEqual(20.0, pa.BestLap, 1e-9);
            Assert.AreEqual(63.0, pa.BestThree, 1e-9);
            Assert.AreEqual(new[] { 5.0, 10.0, 15.0, 20.0 }, pa.Splits[0], "the time at each gate of the first lap");
            Assert.IsTrue(pa.Finished && pb.Finished);
            Assert.AreEqual(1, pb.Position);

            List<RaceEntry> results = r.Results();
            Tournament.Place(results, 3, Two);
            Assert.AreEqual("B", results.Find(e => e.Place == 1).Pilot);
            Assert.AreEqual(65.0, results.Find(e => e.Pilot == "A").Time, 1e-9);
        }

        [Test]
        public void AQualificationRace_IsTimedFromTheTimingGate()
        {
            var r = new RaceTiming(Two, 4, 3, false);
            r.Start(0);
            r.Gate("A", 0, 7.5); // a slow take-off costs nothing in qualification
            double a = 7.5;
            for (int i = 0; i < 3; i++) a = Lap(r, "A", a, 20);
            Assert.AreEqual(60.0, r.Of("A").RaceTime, 1e-9);
            Assert.AreEqual(7.5, r.Of("A").Holeshot, 1e-9);
        }

        [Test]
        public void AMissedGate_VoidsTheLap_UnlessThePilotGoesBackThroughIt()
        {
            var r = new RaceTiming(Two, 4, 3, true);
            r.Start(0);
            r.Gate("A", 0, 2);
            // Lap one: gate 2 is skipped, then corrected at once.
            r.Gate("A", 1, 7);
            r.Gate("A", 3, 12);
            Assert.AreEqual(2, r.Of("A").Missed, "gate 3 does not count: gate 2 was skipped");
            Assert.AreEqual(2, r.Of("A").NextGate);
            r.Gate("A", 2, 16);
            Assert.AreEqual(-1, r.Of("A").Missed);
            r.Gate("A", 3, 20);
            r.Gate("A", 0, 25);
            Assert.AreEqual(1, r.Of("A").Laps.Count, "corrected: the lap counts, with the time the detour took");
            Assert.AreEqual(23.0, r.Of("A").Laps[0], 1e-9);
            Assert.AreEqual(0, r.Of("A").VoidLaps);

            // Lap two: gate 2 is skipped and the pilot flies on over the line: the lap is void.
            r.Gate("A", 1, 30);
            r.Gate("A", 3, 40);
            r.Gate("A", 0, 45);
            Assert.AreEqual(1, r.Of("A").VoidLaps);
            Assert.AreEqual(1, r.Of("A").Laps.Count);
            Assert.AreEqual(1 + 4, r.Of("A").Progress, "the holeshot and one lap, no more");

            // Two more good laps: three valid laps, but not three in a row.
            double t = 45;
            t = Lap(r, "A", t, 20);
            t = Lap(r, "A", t, 20);
            PilotTiming a = r.Of("A");
            Assert.IsTrue(a.Finished);
            Assert.AreEqual(85.0, a.RaceTime, 1e-9, "the clock ran through the void lap");
            Assert.IsTrue(double.IsNaN(a.BestThree), "the void lap came between them");
            Assert.AreEqual(20.0, a.BestLap, 1e-9);
        }

        [Test]
        public void AFalseStart_ThroughTheFirstGate_DisqualifiesFromTheRace()
        {
            var r = new RaceTiming(new[] { "A", "B", "C" }, 4, 3, true);
            r.LeftStart("A", 9.7);          // before the signal
            r.LeftStart("C", 9.8);
            r.BackOnStart("C");             // dropped to the ground at once
            r.Start(10);
            r.LeftStart("B", 10.1);
            r.LeftStart("C", 10.6);
            r.Gate("A", 0, 11.5);
            r.Gate("B", 0, 12);
            r.Gate("C", 0, 12.5);
            Assert.IsTrue(r.Of("A").FalseStart);
            Assert.IsTrue(r.Of("A").Disqualified);
            Assert.IsFalse(r.Of("B").Disqualified);
            Assert.IsFalse(r.Of("C").Disqualified, "back on the ground before the signal: a start from the back, not a false start");
            Assert.AreEqual(3, r.Of("A").Position);
            Lap(r, "A", 11.5, 10);
            Assert.AreEqual(0, r.Of("A").Laps.Count, "nothing is timed for a disqualified pilot");

            List<RaceEntry> results = r.Results();
            Tournament.Place(results, 3, new[] { "A", "B", "C" });
            Assert.AreEqual(3, results.Find(e => e.Pilot == "A").Place);
        }

        [Test]
        public void AfterTheFlightTime_NothingCounts_AndTheUnfinishedArePlacedByTheirLaps()
        {
            var r = new RaceTiming(new[] { "A", "B", "C" }, 4, 3, true) { FlightTime = 60 };
            r.Start(0);
            r.Gate("A", 0, 2);
            r.Gate("B", 0, 3);
            double a = 2, b = 3;
            a = Lap(r, "A", a, 15);
            a = Lap(r, "A", a, 15);
            b = Lap(r, "B", b, 25);
            r.Tick(59.9);
            Assert.IsFalse(r.TimeIsUp);
            b = Lap(r, "B", b, 25);   // B's second lap ends at 53
            r.Tick(60);
            Assert.IsTrue(r.TimeIsUp);
            Assert.IsTrue(r.Over);
            Lap(r, "A", a, 15);       // too late
            Assert.AreEqual(2, r.Of("A").Laps.Count);
            Assert.IsFalse(r.Of("A").Finished);

            List<RaceEntry> results = r.Results();
            Assert.IsTrue(results.Find(e => e.Pilot == "C").DidNotFly, "C never moved");
            Tournament.Place(results, 3, new[] { "A", "B", "C" });
            Assert.AreEqual("A", results.Find(e => e.Place == 1).Pilot, "two laps each: A's took 32 s, B's 53 s");
            Assert.AreEqual(3, results.Find(e => e.Pilot == "C").Place);
        }

        [Test]
        public void AnExtraLap_IsNoted_AndDisqualifiesUnderStrictRules()
        {
            foreach (bool strict in new[] { false, true })
            {
                var r = new RaceTiming(new[] { "A" }, 4, 3, true) { DisqualifyExtraLap = strict };
                r.Start(0);
                r.Gate("A", 0, 2);
                double t = 2;
                for (int i = 0; i < 4; i++) t = Lap(r, "A", t, 20);
                Assert.IsTrue(r.Of("A").ExtraLap);
                Assert.AreEqual(strict, r.Of("A").Disqualified);
                Assert.AreEqual(3, r.Of("A").Laps.Count, "the fourth lap is not a result");
                Assert.AreEqual(62.0, r.Of("A").RaceTime, 1e-9);
            }
        }

        [Test]
        public void TimesAreKeptToAThousandth_AndARetiredPilotStopsThere()
        {
            var r = new RaceTiming(Two, 1, 1, true);
            r.Start(0);
            r.Gate("A", 0, 1.0004);
            r.Gate("B", 0, 1.0014);
            Assert.AreEqual(1.0, r.Of("A").Holeshot, 1e-12);
            Assert.AreEqual(1.001, r.Of("B").Holeshot, 1e-12);
            r.Retire("B");
            r.Gate("B", 0, 5);
            Assert.AreEqual(0, r.Of("B").Laps.Count);
            Assert.IsFalse(r.Over);
            r.Gate("A", 0, 9.9996);
            Assert.AreEqual(9.0, r.Of("A").Laps[0], 1e-12);
            Assert.IsTrue(r.Over);
            Assert.IsFalse(r.Results().Find(e => e.Pilot == "B").DidNotFly, "B flew, and stopped");
        }
    }
}
