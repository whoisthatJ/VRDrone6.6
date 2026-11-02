using System.Linq;
using GateRush.Race;
using GateRush.Track;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GateRush.Tests
{
    /// <summary>The T2 obstacle kit: every prefab is registered, LOD'd, collides with primitives only and has its checkpoints.</summary>
    public class ObstacleKitTests
    {
        private static readonly string[] GateTypes = { "Single", "Double", "Slalom", "Cube", "Ladder", "Dive", "Ramp", "Arch" };
        private static readonly string[] PropTypes = { "ChevronBoard", "ChevronBoardLeft", "Flag", "LandingPad", "StartPad" };

        private static TrackPrefabs Prefabs => AssetDatabase.LoadAssetAtPath<TrackPrefabs>("Assets/_Project/Data/TrackPrefabs.asset");

        [Test]
        public void EveryObstacle_IsRegistered_LodGrouped_AndHasCheckpoints([ValueSource(nameof(GateTypes))] string type)
        {
            var prefab = Prefabs.Gate(type);
            Assert.IsNotNull(prefab, $"{type} not registered in TrackPrefabs (run Build All Scenes)");

            var lod = prefab.GetComponent<LODGroup>();
            Assert.IsNotNull(lod, "LODGroup");
            Assert.AreEqual(3, lod.lodCount, "LOD0/1/2");
            foreach (var l in lod.GetLODs()) Assert.IsNotEmpty(l.renderers);

            Assert.IsEmpty(prefab.GetComponentsInChildren<MeshCollider>(true), "no mesh colliders on obstacles");
            Assert.IsNotEmpty(prefab.GetComponentsInChildren<BoxCollider>(true), "box colliders");

            var gates = prefab.GetComponentsInChildren<Gate>(true);
            Assert.AreEqual(type == "Slalom" ? 3 : 1, gates.Length, "checkpoints");
            var highlight = prefab.GetComponent<NextGateHighlight>();
            Assert.IsNotNull(highlight, "NextGateHighlight");
            Assert.IsTrue(highlight.Leds.Length > 0 && highlight.Leds.All(r => r != null), "LED renderers");

            // Each opening sits inside the obstacle and above the ground (checked on an instance: assets have no bounds).
            var inst = Object.Instantiate(prefab);
            try
            {
                var bounds = new Bounds(inst.transform.position, Vector3.zero);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);
                foreach (var g in inst.GetComponentsInChildren<Gate>(true))
                {
                    Vector3 c = g.OpeningCenterWorld;
                    Assert.IsTrue(bounds.Contains(c), $"opening centre {c} outside {bounds}");
                    Assert.Greater(c.y - g.OpeningSize.y / 2f * Mathf.Abs(g.transform.up.y), -0.01f, "opening above ground");
                }
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        [Test]
        public void EveryProp_IsRegisteredWithLods([ValueSource(nameof(PropTypes))] string type)
        {
            var prefab = Prefabs.Prop(type);
            Assert.IsNotNull(prefab, $"{type} not registered");
            Assert.IsNotNull(prefab.GetComponent<LODGroup>());
            Assert.IsEmpty(prefab.GetComponentsInChildren<MeshCollider>(true));
            Assert.IsEmpty(prefab.GetComponentsInChildren<Gate>(true), "props are not checkpoints");
            if (type == "Flag") Assert.IsNotNull(prefab.GetComponent<FlagWind>(), "flags move in the wind");
        }

        [Test]
        public void Dive_FacesForwardAndDown()
        {
            var gate = Prefabs.Gate("Dive").GetComponentInChildren<Gate>(true);
            Vector3 f = gate.transform.forward;
            Assert.Greater(f.z, 0.5f, "towards the flying direction");
            Assert.Less(f.y, -0.5f, "downwards");
        }

        [Test]
        public void Highlight_NextPulsesBrightest_PassedDimmest()
        {
            var go = new GameObject("hl");
            try
            {
                var h = go.AddComponent<NextGateHighlight>();
                float lo = float.MaxValue, hi = 0f;
                for (float t = 0f; t < 2f; t += 0.01f)
                {
                    float i = h.IntensityFor(GateVisualState.Next, t);
                    lo = Mathf.Min(lo, i);
                    hi = Mathf.Max(hi, i);
                }
                Assert.Greater(hi - lo, 1f, "the next gate pulses");
                Assert.Greater(hi, h.IntensityFor(GateVisualState.Upcoming, 0f));
                Assert.Greater(h.IntensityFor(GateVisualState.Upcoming, 0f), h.IntensityFor(GateVisualState.Idle, 0f));
                Assert.Greater(h.IntensityFor(GateVisualState.Idle, 0f), h.IntensityFor(GateVisualState.Passed, 0f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SplineTrack_SlalomAddsOneCheckpointPerGate_InRouteOrder()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/Tracks/spline-test.json");
            var data = TrackData.FromJson(json.text);
            var track = TrackBuilder.Build(data, Prefabs, null, 0);
            try
            {
                int obstacles = data.Route.Obstacles.Count(o => o.IsCheckpoint);
                Assert.AreEqual(obstacles + 2, track.Gates.Length, "the slalom's three gates are three checkpoints");
                Assert.IsTrue(track.Gates[0].name.Contains("Arch"), "gate 0 is the finish arch");
                float finish = track.Route.ClosestDistance(track.Gates[0].OpeningCenterWorld);
                float last = -1f;
                foreach (var g in track.Gates.Skip(1))
                {
                    float ahead = TrackCheckpoints.Ahead(finish, track.Route.ClosestDistance(g.OpeningCenterWorld), track.Route.Length);
                    Assert.Greater(ahead, last, g.name);
                    last = ahead;
                }
            }
            finally
            {
                Object.DestroyImmediate(track.Root.gameObject);
            }
        }
    }
}
