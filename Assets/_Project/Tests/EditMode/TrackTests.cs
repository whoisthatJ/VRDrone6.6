using System;
using GateRush.Race;
using GateRush.Track;
using NUnit.Framework;
using UnityEngine;

namespace GateRush.Tests
{
    public class TrackTests
    {
        private static TrackData Sample()
        {
            var t = new TrackData { Id = "test", Name = "Test", Author = "me", Environment = "hangar", Laps = 2 };
            t.Spawn.Position = new Vector3(1, 0.15f, 2);
            t.Spawn.Yaw = 90f;
            t.Gates.Add(new GateData { Type = "Square", Position = new Vector3(0, 0, 10), Yaw = 0 });
            t.Gates.Add(new GateData { Type = "Hurdle", Position = new Vector3(5, 0, 20), Yaw = 45, Pitch = 10, Scale = 1.5f });
            t.Props.Add(new PropData { Type = "Crate", Position = new Vector3(3, 0, 3), Euler = new Vector3(0, 30, 0), Scale = new Vector3(2, 1, 1) });
            return t;
        }

        [Test]
        public void Json_RoundTrip_PreservesEverything()
        {
            var t = Sample();
            var copy = TrackData.FromJson(t.ToJson());
            Assert.AreEqual("test", copy.Id);
            Assert.AreEqual("hangar", copy.Environment);
            Assert.AreEqual(2, copy.Laps);
            Assert.AreEqual(new Vector3(1, 0.15f, 2), copy.Spawn.Position);
            Assert.AreEqual(90f, copy.Spawn.Yaw);
            Assert.AreEqual(2, copy.Gates.Count);
            Assert.AreEqual("Hurdle", copy.Gates[1].Type);
            Assert.AreEqual(new Vector3(5, 0, 20), copy.Gates[1].Position);
            Assert.AreEqual(45f, copy.Gates[1].Yaw);
            Assert.AreEqual(10f, copy.Gates[1].Pitch);
            Assert.AreEqual(1.5f, copy.Gates[1].Scale);
            Assert.AreEqual(1, copy.Props.Count);
            Assert.AreEqual(new Vector3(2, 1, 1), copy.Props[0].Scale);
            // Serializing again gives identical JSON.
            Assert.AreEqual(t.ToJson(), copy.ToJson());
        }

        [Test]
        public void FromJson_MissingListsAreEmptyNotNull()
        {
            var t = TrackData.FromJson("{\"Id\":\"bare\"}");
            Assert.IsNotNull(t.Gates);
            Assert.IsNotNull(t.Props);
            Assert.IsNotNull(t.Spawn);
            Assert.AreEqual(3, t.Laps);
        }

        [Test]
        public void FromJson_EmptyThrows()
        {
            Assert.Throws<ArgumentException>(() => TrackData.FromJson(" "));
        }

        [Test]
        public void Validate_ReportsProblems()
        {
            var ok = Sample();
            Assert.IsEmpty(ok.Validate(new[] { "Square", "Hurdle" }, new[] { "Crate" }));

            var bad = Sample();
            bad.Id = "";
            bad.Laps = 0;
            bad.Gates[0].Type = "Mystery";
            bad.Gates[1].Scale = 0f;
            bad.Props[0].Type = "Rocket";
            var errors = bad.Validate(new[] { "Square", "Hurdle" }, new[] { "Crate" });
            Assert.AreEqual(5, errors.Count, string.Join("\n", errors));

            Assert.IsNotEmpty(new TrackData { Id = "x" }.Validate(), "no gates");
        }

        [Test]
        public void Loader_InstantiatesGatesInOrderWithPoses()
        {
            var gateTemplate = new GameObject("GateTemplate");
            gateTemplate.AddComponent<Gate>();
            var propTemplate = new GameObject("PropTemplate");
            var prefabs = ScriptableObject.CreateInstance<TrackPrefabs>();
            prefabs.Gates.Add(new TrackPrefabs.Entry { Type = "Square", Prefab = gateTemplate });
            prefabs.Gates.Add(new TrackPrefabs.Entry { Type = "Hurdle", Prefab = gateTemplate });
            prefabs.Props.Add(new TrackPrefabs.Entry { Type = "Crate", Prefab = propTemplate });
            LoadedTrack loaded = null;
            try
            {
                loaded = TrackLoader.Load(Sample(), prefabs);
                Assert.AreEqual(2, loaded.Gates.Length);
                Assert.AreEqual(0, loaded.Gates[0].Order);
                Assert.AreEqual(1, loaded.Gates[1].Order);
                Assert.That(Vector3.Distance(new Vector3(5, 0, 20), loaded.Gates[1].transform.position), Is.LessThan(1e-4f));
                Assert.AreEqual(45f, loaded.Gates[1].transform.eulerAngles.y, 0.01f);
                Assert.AreEqual(1.5f, loaded.Gates[1].transform.localScale.x, 1e-5f);
                Assert.AreEqual(90f, loaded.Spawn.eulerAngles.y, 0.01f);
                Assert.AreEqual(4, loaded.Root.childCount, "2 gates + 1 prop + spawn");
            }
            finally
            {
                if (loaded != null) UnityEngine.Object.DestroyImmediate(loaded.Root.gameObject);
                UnityEngine.Object.DestroyImmediate(gateTemplate);
                UnityEngine.Object.DestroyImmediate(propTemplate);
                UnityEngine.Object.DestroyImmediate(prefabs);
            }
        }
    }
}
