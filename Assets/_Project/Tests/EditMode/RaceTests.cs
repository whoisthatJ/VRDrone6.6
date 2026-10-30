using System.IO;
using GateRush.Race;
using NUnit.Framework;
using UnityEngine;

namespace GateRush.Tests
{
    public class LapTimerTests
    {
        /// <summary>Flies one full lap (gates 1..N-1 then 0) starting at <paramref name="t"/>, one gate per second.</summary>
        private static PassResult FlyLap(LapTimer timer, ref double t)
        {
            for (int g = 1; g < timer.CheckpointCount; g++) timer.Pass(g, t += 1);
            return timer.Pass(0, t += 1);
        }

        [Test]
        public void NotStarted_IgnoresGates()
        {
            var timer = new LapTimer(4, 3);
            Assert.AreEqual(PassResult.Ignored, timer.Pass(0, 1));
        }

        [Test]
        public void ThreeLapRace_LapTimesTotalAndBest()
        {
            var timer = new LapTimer(4, 3);
            timer.Start(10);
            Assert.AreEqual(PassResult.Checkpoint, timer.Pass(0, 12)); // holeshot
            double t = 12;
            Assert.AreEqual(PassResult.LapCompleted, FlyLap(timer, ref t)); // ends at 16
            Assert.AreEqual(6, timer.LapTime(0), 1e-9, "lap 1 includes the launch");

            t += 0.5; // a slower lap: 4.5 s
            for (int g = 1; g < 4; g++) timer.Pass(g, t += 1);
            Assert.AreEqual(PassResult.LapCompleted, timer.Pass(0, t += 1.5));
            Assert.AreEqual(5, timer.LapTime(1), 1e-9);

            Assert.AreEqual(PassResult.RaceFinished, FlyLap(timer, ref t));
            Assert.AreEqual(4, timer.LapTime(2), 1e-9);
            Assert.IsTrue(timer.Finished);
            Assert.AreEqual(4, timer.BestLap, 1e-9);
            Assert.AreEqual(2, timer.BestLapIndex);
            Assert.AreEqual(15, timer.TotalTime(999), 1e-9);
            Assert.AreEqual(PassResult.Ignored, timer.Pass(1, t + 1), "nothing counts after the finish");
        }

        [Test]
        public void CheckpointsMustBeFlownInOrder()
        {
            var timer = new LapTimer(4, 1);
            timer.Start(0);
            timer.Pass(0, 1);
            Assert.AreEqual(PassResult.WrongGate, timer.Pass(2, 2), "skipping gate 1");
            Assert.AreEqual(1, timer.NextCheckpoint);
            Assert.AreEqual(PassResult.WrongGate, timer.Pass(0, 3), "cutting back through start/finish");
            Assert.AreEqual(PassResult.Checkpoint, timer.Pass(1, 4));
            Assert.AreEqual(PassResult.Checkpoint, timer.Pass(2, 5));
            Assert.AreEqual(PassResult.Checkpoint, timer.Pass(3, 6));
            Assert.AreEqual(PassResult.RaceFinished, timer.Pass(0, 7));
            Assert.AreEqual(7, timer.TotalTime(100), 1e-9);
        }

        [Test]
        public void StartGateFirst_IsTheHoleshot_NotALap()
        {
            var timer = new LapTimer(3, 2);
            timer.Start(0);
            Assert.AreEqual(PassResult.WrongGate, timer.Pass(1, 1), "gate 1 before the holeshot");
            Assert.AreEqual(PassResult.Checkpoint, timer.Pass(0, 2));
            Assert.AreEqual(0, timer.CompletedLaps);
            Assert.AreEqual(0, timer.LastCheckpoint);
        }

        [Test]
        public void SingleGateTrack_EachPassAfterHoleshotIsALap()
        {
            var timer = new LapTimer(1, 2);
            timer.Start(0);
            Assert.AreEqual(PassResult.Checkpoint, timer.Pass(0, 1));
            Assert.AreEqual(PassResult.LapCompleted, timer.Pass(0, 4));
            Assert.AreEqual(PassResult.RaceFinished, timer.Pass(0, 6));
            Assert.AreEqual(2, timer.BestLap, 1e-9);
        }

        [Test]
        public void CurrentLapTime_RunsFromLapStart()
        {
            var timer = new LapTimer(2, 3);
            timer.Start(100);
            Assert.AreEqual(5, timer.CurrentLapTime(105), 1e-9);
            timer.Pass(0, 101);
            timer.Pass(1, 103);
            timer.Pass(0, 106);
            Assert.AreEqual(2, timer.CurrentLap);
            Assert.AreEqual(1.5, timer.CurrentLapTime(107.5), 1e-9);
        }
    }

    public class GateCrossingTests
    {
        private static readonly Vector2 Half = new Vector2(0.76f, 0.76f);

        [Test]
        public void ThroughTheMiddle_Forward_Counts()
        {
            Assert.IsTrue(GateCrossing.TryCross(new Vector3(0, 0, -1), new Vector3(0, 0, 3), Vector3.zero, Quaternion.identity, Half, out float f));
            Assert.AreEqual(0.25f, f, 1e-6f);
        }

        [Test]
        public void Backwards_DoesNotCount()
        {
            Assert.IsFalse(GateCrossing.TryCross(new Vector3(0, 0, 1), new Vector3(0, 0, -1), Vector3.zero, Quaternion.identity, Half, out _));
        }

        [Test]
        public void OutsideTheOpening_DoesNotCount()
        {
            Assert.IsFalse(GateCrossing.TryCross(new Vector3(1f, 0, -1), new Vector3(1f, 0, 1), Vector3.zero, Quaternion.identity, Half, out _));
            Assert.IsFalse(GateCrossing.TryCross(new Vector3(0, 0.9f, -1), new Vector3(0, 0.9f, 1), Vector3.zero, Quaternion.identity, Half, out _));
        }

        [Test]
        public void RotatedGate_UsesItsOwnForward()
        {
            var rot = Quaternion.Euler(0f, 90f, 0f); // flight direction +X
            var c = new Vector3(10, 2, 5);
            Assert.IsTrue(GateCrossing.TryCross(c + new Vector3(-0.5f, 0.3f, 0.2f), c + new Vector3(0.5f, 0.3f, 0.2f), c, rot, Half, out _));
            Assert.IsFalse(GateCrossing.TryCross(c + new Vector3(0.3f, 0, -0.5f), c + new Vector3(0.3f, 0, 0.5f), c, rot, Half, out _), "parallel to the plane, in front of it");
            Assert.IsFalse(GateCrossing.TryCross(c + new Vector3(-1f, 0, 0), c + new Vector3(1f, 0, 0), c, Quaternion.Euler(0f, -90f, 0f), Half, out _), "+X through a gate facing -X is backwards");
        }

        [Test]
        public void SegmentEndingBeforePlane_DoesNotCount()
        {
            Assert.IsFalse(GateCrossing.TryCross(new Vector3(0, 0, -2), new Vector3(0, 0, -0.1f), Vector3.zero, Quaternion.identity, Half, out _));
        }
    }

    public class LeaderboardTests
    {
        private static LeaderboardEntry E(string pilot, float total, float best) =>
            new LeaderboardEntry { Pilot = pilot, Quad = "5\" Race", TotalTime = total, BestLap = best, Date = "2026-10-30" };

        [Test]
        public void Submit_SortsByTotal_AndTracksBestLap()
        {
            var lb = new LocalLeaderboard();
            Assert.AreEqual(1, lb.Submit("t", E("a", 60, 19), out bool best1));
            Assert.IsTrue(best1);
            Assert.AreEqual(1, lb.Submit("t", E("b", 55, 19.5f), out bool best2));
            Assert.IsFalse(best2);
            Assert.AreEqual(3, lb.Submit("t", E("c", 70, 18), out bool best3));
            Assert.IsTrue(best3);

            var board = lb.Board("t");
            Assert.AreEqual(new[] { "b", "a", "c" }, board.Entries.ConvertAll(x => x.Pilot).ToArray());
            Assert.AreEqual(18f, board.BestLap);
            Assert.AreEqual("c", board.BestLapPilot);
        }

        [Test]
        public void Submit_KeepsTopTenOnly()
        {
            var lb = new LocalLeaderboard();
            for (int i = 0; i < 12; i++) lb.Submit("t", E("p" + i, 50 + i, 15), out _);
            Assert.AreEqual(LocalLeaderboard.MaxEntries, lb.Board("t").Entries.Count);
            Assert.AreEqual(-1, lb.Submit("t", E("slow", 99, 30), out _));
            Assert.AreEqual(1, lb.Submit("t", E("fast", 10, 3), out _));
            Assert.AreEqual(LocalLeaderboard.MaxEntries, lb.Board("t").Entries.Count);
        }

        [Test]
        public void Json_RoundTrip_KeepsTracksSeparate()
        {
            var lb = new LocalLeaderboard();
            lb.Submit("field", E("a", 60, 19), out _);
            lb.Submit("hangar", E("b", 40, 12), out _);
            var copy = new LocalLeaderboard();
            copy.FromJson(lb.ToJson());
            Assert.AreEqual(2, copy.Data.Tracks.Count);
            Assert.AreEqual("b", copy.Board("hangar").Entries[0].Pilot);
            Assert.AreEqual(19f, copy.Board("field").BestLap);
        }
    }

    public class GhostTrackTests
    {
        [Test]
        public void Sample_InterpolatesBetweenSamples()
        {
            var g = new GhostTrack(10, 0.1f);
            g.Add(Vector3.zero, Quaternion.identity);
            g.Add(new Vector3(1, 0, 0), Quaternion.Euler(0, 90, 0));
            Assert.IsTrue(g.Sample(0.05f, out var p, out var r));
            Assert.AreEqual(0.5f, p.x, 1e-5f);
            Assert.AreEqual(45f, r.eulerAngles.y, 0.01f);
            g.Sample(5f, out p, out _);
            Assert.AreEqual(1f, p.x, 1e-5f, "clamped to the end");
        }

        [Test]
        public void Add_StopsAtCapacityWithoutThrowing()
        {
            var g = new GhostTrack(2, 0.1f);
            for (int i = 0; i < 5; i++) g.Add(Vector3.one * i, Quaternion.identity);
            Assert.AreEqual(2, g.Count);
            Assert.IsTrue(g.IsFull);
        }

        [Test]
        public void Binary_RoundTrip()
        {
            var g = new GhostTrack(100, 1f / 30f) { LapTime = 17.25f };
            for (int i = 0; i < 50; i++) g.Add(new Vector3(i, i * 0.5f, -i), Quaternion.Euler(i, i * 2, 0));

            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) g.Write(w);
            ms.Position = 0;
            var copy = new GhostTrack(100, 0.5f);
            using (var r = new BinaryReader(ms)) Assert.IsTrue(copy.Read(r));

            Assert.AreEqual(50, copy.Count);
            Assert.AreEqual(17.25f, copy.LapTime);
            Assert.AreEqual(1f / 30f, copy.SampleInterval);
            copy.Sample(49f / 30f, out var p, out _);
            Assert.That(Vector3.Distance(new Vector3(49, 24.5f, -49), p), Is.LessThan(1e-3f));
        }

        [Test]
        public void Read_RejectsForeignData()
        {
            var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            using var r = new BinaryReader(ms);
            Assert.IsFalse(new GhostTrack(4, 0.1f).Read(r));
        }
    }
}
