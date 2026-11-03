using System.Collections.Generic;
using System.Linq;
using GateRush.Core;
using GateRush.Game;
using GateRush.Garage;
using GateRush.Net;
using GateRush.Race;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GateRush.Tests
{
    /// <summary>M9 online race: snapshot interpolation and wire format, grid, standings, progress, names, opt-in.</summary>
    public class OnlineTests
    {
        private static PilotSnapshot At(double t, float x, float progress = 0f) => new PilotSnapshot
        {
            Time = t,
            Position = new Vector3(x, 1f, 0f),
            Rotation = Quaternion.identity,
            Velocity = new Vector3(10f, 0f, 0f),
            M0 = 255,
            Progress = progress,
        };

        [Test]
        public void Buffer_InterpolatesBetweenSnapshots_AndExtrapolatesBriefly()
        {
            var b = new SnapshotBuffer();
            Assert.IsTrue(b.Add(At(1.0, 0f)));
            Assert.IsTrue(b.Add(At(1.05, 0.5f)));
            Assert.IsTrue(b.Add(At(1.10, 1.0f)));
            var motors = new float[4];

            Assert.IsTrue(b.Sample(1.075, out var p, out _, motors));
            Assert.AreEqual(0.75f, p.x, 1e-4f, "halfway between the 2nd and 3rd sample");
            Assert.AreEqual(1f, motors[0], 1e-4f);

            b.Sample(0.5, out p, out _, null);
            Assert.AreEqual(0f, p.x, 1e-4f, "before the first sample: hold it");

            b.Sample(1.15, out p, out _, null);
            Assert.AreEqual(1.5f, p.x, 1e-3f, "50 ms past the newest: extrapolate with the velocity");
            b.Sample(5.0, out p, out _, null);
            Assert.AreEqual(1f + 10f * (float)SnapshotBuffer.MaxExtrapolation, p.x, 1e-3f, "extrapolation is capped");
        }

        [Test]
        public void Buffer_DropsLateAndDuplicatePackets()
        {
            var b = new SnapshotBuffer();
            b.Add(At(2.0, 2f));
            Assert.IsFalse(b.Add(At(1.9, 9f)), "older than the newest");
            Assert.IsFalse(b.Add(At(2.0, 9f)), "duplicate");
            Assert.AreEqual(1, b.Count);
            for (int i = 1; i <= 100; i++) b.Add(At(2.0 + i * 0.05, i));
            Assert.LessOrEqual(b.Count, 32, "bounded memory");
        }

        [Test]
        public void Snapshot_RoundTripsThroughTheWireFormat()
        {
            var s = new PilotSnapshot
            {
                Time = 1234.5678,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Euler(10f, 20f, 30f),
                Velocity = new Vector3(-4f, 5f, 6f),
                M0 = 1, M1 = 2, M2 = 3, M3 = 250,
                Progress = 17.25f,
                Lap = 3,
                Armed = true,
            };
            using var w = new FastBufferWriter(PilotSnapshot.Size + 8, Allocator.Temp);
            s.Write(w);
            Assert.AreEqual(PilotSnapshot.Size, w.Length, "Size matches what is written");
            using var r = new FastBufferReader(w, Allocator.Temp);
            var back = PilotSnapshot.Read(r);
            Assert.AreEqual(s.Time, back.Time);
            Assert.AreEqual(s.Position, back.Position);
            Assert.AreEqual(s.Rotation, back.Rotation);
            Assert.AreEqual(s.Velocity, back.Velocity);
            Assert.AreEqual(250, back.M3);
            Assert.AreEqual(17.25f, back.Progress);
            Assert.AreEqual(3, back.Lap);
            Assert.IsTrue(back.Armed);
            Assert.AreEqual(128, PilotSnapshot.PackMotor(0.5f), 1);
        }

        [Test]
        public void Grid_GivesEightDistinctSlots_FirstOneOnTheSpawn()
        {
            Assert.AreEqual(Vector3.zero, OnlineRules.GridOffset(0));
            var offsets = Enumerable.Range(0, OnlineRules.MaxPilots).Select(OnlineRules.GridOffset).ToList();
            for (int i = 0; i < offsets.Count; i++)
                for (int j = i + 1; j < offsets.Count; j++)
                    Assert.Greater(Vector3.Distance(offsets[i], offsets[j]), 1.4f, $"slots {i} and {j} are apart");
            Assert.Less(OnlineRules.GridOffset(4).z, 0f, "pilots 5-8 start behind");

            var pilots = new List<NetPilot> { new NetPilot { Slot = 0 }, new NetPilot { Slot = 2 } };
            Assert.AreEqual(1, OnlineRules.FreeSlot(pilots), "a leaver's slot is reused");
        }

        [Test]
        public void Standings_FinishersByTime_ThenByDistance()
        {
            var a = new NetPilot { Name = "a", Progress = 30f };
            var b = new NetPilot { Name = "b", Finished = true, TotalTime = 95f };
            var c = new NetPilot { Name = "c", Progress = 31.5f };
            var d = new NetPilot { Name = "d", Finished = true, TotalTime = 90f };
            var order = OnlineRules.Standings(new[] { a, b, c, d }).Select(p => p.Name);
            CollectionAssert.AreEqual(new[] { "d", "b", "c", "a" }, order);
        }

        [Test]
        public void Progress_CountsGatesAcrossLaps()
        {
            const int gates = 5;
            Assert.AreEqual(0, RaceProgress.GatesPassed(false, 0, 0, gates), "before the holeshot");
            Assert.AreEqual(1, RaceProgress.GatesPassed(true, 0, 1, gates), "holeshot");
            Assert.AreEqual(5, RaceProgress.GatesPassed(true, 0, 0, gates), "last gate of lap 1");
            Assert.AreEqual(6, RaceProgress.GatesPassed(true, 1, 1, gates), "lap 1 done, gate 0 again");
            Assert.AreEqual(0.5f, RaceProgress.LegFraction(Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 0f)), 1e-4f);
            Assert.AreEqual(0f, RaceProgress.LegFraction(Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(-30f, 0f, 0f)), "flying away never goes negative");
            Assert.Less(RaceProgress.LegFraction(Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 0f)), 1f, "never reaches the next gate count");
        }

        [Test]
        public void Names_AreCleanedForTheServices()
        {
            Assert.AreEqual("Red_Fox", OnlineServices.PlayerName("Red Fox"));
            Assert.AreEqual("Pilot", OnlineServices.PlayerName("  "));
            Assert.AreEqual("Red Fox", OnlineServices.DisplayName("Red_Fox#1234"));
            Assert.AreEqual("Viper", OnlineServices.StripNumber("Viper#99"));
            Assert.AreEqual("p2", OnlineServices.Profile(new[] { "game.exe", "-online-profile", "p2" }));
            Assert.AreEqual("bad", OnlineServices.Profile(new[] { "-online-profile", "b a!d" }));
            Assert.AreEqual("", OnlineServices.Profile(new[] { "game.exe" }));
        }

        [Test]
        public void OnlineIsOptIn()
        {
            var s = new GameSettings();
            Assert.IsFalse(s.Online.PostLapTimes, "posting lap times is off until the player turns it on");
            s.Online = null;
            s.Clamp();
            Assert.IsNotNull(s.Online, "old saves get the online prefs");
            Assert.IsFalse(NetRace.Active, "nothing connects by itself");
        }

        [Test]
        public void Leaderboards_ExistForEveryBuiltInTrackOnly()
        {
            Assert.AreEqual("lap-field", OnlineLeaderboards.IdFor("field"));
            Assert.IsTrue(OnlineLeaderboards.Has("serpent-arena"));
            Assert.IsFalse(OnlineLeaderboards.Has("user:my-track"));
            foreach (string id in OnlineLeaderboards.Tracks)
                FileAssert.Exists($"Assets/_Project/Online/Leaderboards/lap-{id}.lb", $"deploy file for {id}");
        }

        [Test]
        public void StockQuads_MapToTheirPresetModel()
        {
            Assert.AreEqual(QuadPresets.Race5.Name, OnlineFlow.LocalBuild("race5").Name);
            Assert.AreEqual(QuadPresets.Whoop65.Name, OnlineFlow.LocalBuild("whoop65").Name);
            var json = QuadPresets.Freestyle5.ToJson();
            Assert.AreEqual(QuadPresets.Freestyle5.Name, RemotePilotView.Parse(json).Name, "a pilot's build arrives intact");
            Assert.AreEqual(QuadPresets.Race5.Name, RemotePilotView.Parse("{garbage").Name, "bad data falls back to the racer");
        }
    }
}
