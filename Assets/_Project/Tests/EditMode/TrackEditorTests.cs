using System.IO;
using GateRush.Track;
using NUnit.Framework;
using UnityEngine;

namespace GateRush.Tests
{
    public class TrackEditModelTests
    {
        private static TrackEditModel NewModel() => new TrackEditModel(TrackEditModel.NewTrack("field", "Test"));

        [Test]
        public void Snapping_PositionGridAndAngleStep()
        {
            Assert.AreEqual(new Vector3(1.5f, 0f, -2f), TrackEditModel.SnapPosition(new Vector3(1.4f, -0.3f, -2.2f), 0.5f));
            Assert.AreEqual(30f, TrackEditModel.SnapAngle(37f, 15f));
            Assert.AreEqual(0f, TrackEditModel.SnapAngle(359f, 15f));
            Assert.AreEqual(345f, TrackEditModel.SnapAngle(-14f, 15f));
        }

        [Test]
        public void AddGate_SnapsAndAppends_OrInsertsAtIndex()
        {
            var m = NewModel();
            int a = m.AddGate("Large", new Vector3(3.2f, 0f, 20.1f), 47f);
            Assert.AreEqual(1, a);
            Assert.AreEqual(new Vector3(3f, 0f, 20f), m.Data.Gates[1].Position);
            Assert.AreEqual(45f, m.Data.Gates[1].Yaw);
            int b = m.AddGate("Hurdle", Vector3.zero, 0f, 1);
            Assert.AreEqual(1, b);
            Assert.AreEqual("Hurdle", m.Data.Gates[1].Type);
            Assert.AreEqual("Large", m.Data.Gates[2].Type);
        }

        [Test]
        public void SnapOff_KeepsExactValues_ButNeverBelowGround()
        {
            var m = NewModel();
            m.Snap = false;
            m.SetGate(0, new Vector3(1.23f, -1f, 4.56f), 12.3f, 1.7f);
            var g = m.Data.Gates[0];
            Assert.AreEqual(new Vector3(1.23f, 0f, 4.56f), g.Position);
            Assert.AreEqual(12.3f, g.Yaw, 1e-4f);
            Assert.AreEqual(1.7f, g.Scale, 1e-4f);
        }

        [Test]
        public void Scale_IsClamped()
        {
            var m = NewModel();
            m.SetGate(0, Vector3.zero, 0f, 99f);
            Assert.AreEqual(TrackEditModel.MaxScale, m.Data.Gates[0].Scale);
            m.SetGate(0, Vector3.zero, 0f, 0.01f);
            Assert.AreEqual(TrackEditModel.MinScale, m.Data.Gates[0].Scale);
        }

        [Test]
        public void MoveOrder_ReordersCheckpoints()
        {
            var m = NewModel();
            m.AddGate("Large", new Vector3(0, 0, 20), 0);
            m.AddGate("Hurdle", new Vector3(0, 0, 30), 0);
            Assert.AreEqual(0, m.MoveOrder(2, 0));
            Assert.AreEqual(new[] { "Hurdle", "Square", "Large" }, m.Data.Gates.ConvertAll(g => g.Type).ToArray());
            Assert.AreEqual(2, m.MoveOrder(0, 99), "clamped to the end");
            Assert.AreEqual("Hurdle", m.Data.Gates[2].Type);
        }

        [Test]
        public void ReverseDirection_KeepsStartGateAndTurnsGatesAround()
        {
            var m = NewModel();
            m.AddGate("Large", new Vector3(0, 0, 20), 0);
            m.AddGate("Hurdle", new Vector3(0, 0, 30), 90);
            m.ReverseDirection();
            Assert.AreEqual(new[] { "Square", "Hurdle", "Large" }, m.Data.Gates.ConvertAll(g => g.Type).ToArray());
            Assert.AreEqual(180f, m.Data.Gates[0].Yaw);
            Assert.AreEqual(270f, m.Data.Gates[1].Yaw);
        }

        [Test]
        public void UndoRedo_RestoresStructureAndDragAsOneStep()
        {
            var m = NewModel();
            m.AddGate("Large", new Vector3(0, 0, 20), 0);
            m.BeginEdit();
            for (int i = 1; i <= 10; i++) m.SetGate(1, new Vector3(i, 0, 20), 0, 1); // one drag
            Assert.AreEqual(10f, m.Data.Gates[1].Position.x);
            Assert.IsTrue(m.Undo());
            Assert.AreEqual(0f, m.Data.Gates[1].Position.x, "whole drag undone");
            Assert.IsTrue(m.Undo());
            Assert.AreEqual(1, m.Data.Gates.Count, "add undone");
            Assert.IsTrue(m.Redo());
            Assert.AreEqual(2, m.Data.Gates.Count);
            m.RemoveGate(0);
            Assert.IsFalse(m.CanRedo, "a new edit clears redo");
        }

        [Test]
        public void Undo_IsCapped()
        {
            var m = NewModel();
            for (int i = 0; i < TrackEditModel.MaxUndo + 10; i++) m.AddGate("Square", new Vector3(i, 0, 0), 0);
            int undos = 0;
            while (m.Undo()) undos++;
            Assert.AreEqual(TrackEditModel.MaxUndo, undos);
        }

        [Test]
        public void ChangedEvent_AndDirtyFlag()
        {
            var m = NewModel();
            int changes = 0;
            m.Changed += () => changes++;
            Assert.IsFalse(m.Dirty);
            m.AddProp("Crate", new Vector3(1, 0, 1), 10);
            m.SetProp(0, new Vector3(2, 0, 2), 90, 2f);
            m.SetSpawn(new Vector3(1, 0, 1), 95);
            Assert.AreEqual(3, changes);
            Assert.IsTrue(m.Dirty);
            Assert.AreEqual(Vector3.one * 2f, m.Data.Props[0].Scale);
            Assert.AreEqual(0.15f, m.Data.Spawn.Position.y, 1e-5f, "spawn kept above ground");
            Assert.AreEqual(90f, m.Data.Spawn.Yaw);
            m.MarkSaved();
            Assert.IsFalse(m.Dirty);
        }

        [Test]
        public void NewTrack_IsRaceable()
        {
            var t = TrackEditModel.NewTrack("hangar", "Mine");
            t.Id = "user-mine";
            Assert.IsEmpty(t.Validate(new[] { "Square" }, new string[0]));
        }
    }

    public class TrackLibraryTests
    {
        private string _dir;
        private TrackLibrary _lib;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gaterush-tracks-" + System.Guid.NewGuid().ToString("N"));
            _lib = new TrackLibrary(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void Save_AssignsUserId_LoadRoundTrips()
        {
            var t = TrackEditModel.NewTrack("industrial", "My Dock Run!");
            string path = _lib.Save(t);
            StringAssert.StartsWith(TrackLibrary.UserPrefix, t.Id);
            Assert.AreEqual("user-my-dock-run", t.Id);
            Assert.IsTrue(File.Exists(path));
            var back = _lib.Load(t.Id);
            Assert.AreEqual("My Dock Run!", back.Name);
            Assert.AreEqual("industrial", back.Environment);
            Assert.AreEqual(1, back.Gates.Count);
        }

        [Test]
        public void NewId_IsUniqueAgainstExistingFiles()
        {
            var a = TrackEditModel.NewTrack("field", "Loop");
            _lib.Save(a);
            var b = TrackEditModel.NewTrack("field", "Loop");
            _lib.Save(b);
            Assert.AreEqual("user-loop", a.Id);
            Assert.AreEqual("user-loop-2", b.Id);
            Assert.AreEqual(2, _lib.LoadAll().Count);
        }

        [Test]
        public void SharedFile_WithBuiltInIdOrBadJson_IsHandled()
        {
            Directory.CreateDirectory(_dir);
            var shared = TrackEditModel.NewTrack("field", "From A Friend");
            shared.Id = "field"; // a built-in id in someone else's file
            File.WriteAllText(Path.Combine(_dir, "friend track.json"), shared.ToJson());
            File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
            var all = _lib.LoadAll();
            var friend = all.Find(t => t.Name == "From A Friend");
            Assert.IsNotNull(friend);
            Assert.IsTrue(TrackLibrary.IsUserId(friend.Id), "re-keyed as a user track");
        }

        [Test]
        public void Duplicate_MakesAnUnsavedUserCopy()
        {
            var builtIn = TrackEditModel.NewTrack("hangar", "Hangar 7");
            builtIn.Id = "hangar";
            var copy = _lib.Duplicate(builtIn, "Hangar 7 (my version)");
            Assert.AreNotSame(builtIn, copy);
            Assert.IsTrue(TrackLibrary.IsUserId(copy.Id));
            Assert.AreEqual("hangar", builtIn.Id, "source untouched");
            Assert.IsFalse(File.Exists(_lib.PathFor(copy.Id)));
        }

        [Test]
        public void Delete_RemovesTheFile()
        {
            var t = TrackEditModel.NewTrack("field", "Gone");
            _lib.Save(t);
            Assert.IsTrue(_lib.Delete(t.Id));
            Assert.IsNull(_lib.Load(t.Id));
            Assert.IsFalse(_lib.Delete(t.Id));
        }

        [Test]
        public void Sanitize_KeepsIdsFileSafe()
        {
            Assert.AreEqual("My-Track_2", TrackLibrary.Sanitize("My Track_2"));
            Assert.AreEqual("track", TrackLibrary.Sanitize("///"));
            Assert.AreEqual(40, TrackLibrary.Sanitize(new string('a', 100)).Length);
        }
    }
}
