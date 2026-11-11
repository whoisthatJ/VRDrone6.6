using System.Collections.Generic;
using Ushqyr.Strike;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>
    /// Range traffic never overlaps: over full rounds on the real Range_Steppe layout, with hits, wrecks veering off
    /// and respawns, no two vehicles' footprints (oriented rectangles) ever intersect, and every road vehicle stays in
    /// its own section of road.
    /// </summary>
    public sealed class StrikeTrafficTests
    {
        private const string LayoutPath = "Assets/_Project/Data/Strike/RangeLayout_Steppe.asset";
        private const float Step = 0.1f;

        private static RangeLayout Layout()
        {
            var l = AssetDatabase.LoadAssetAtPath<RangeLayout>(LayoutPath);
            Assert.NotNull(l, "Range_Steppe layout (build the steppe scene first)");
            return l;
        }

        private static bool Overlap(Vector3 pa, float ya, Vector2 ea, Vector3 pb, float yb, Vector2 eb, float margin) =>
            Footprints.Overlap(pa, ya, ea, pb, yb, eb, margin);

        private static bool Sunk(RangeTarget x, float t) =>
            !x.Active && x.WreckSinks && t - x.DisabledAt >= TargetMotion.WreckSeconds * 0.5f;

        [Test]
        public void Vehicles_NeverOverlap([Values(RangeDifficulty.Normal, RangeDifficulty.Hard)] RangeDifficulty difficulty)
        {
            var layout = Layout();
            var problems = new List<string>();
            for (int seed = 1; seed <= 8 && problems.Count < 10; seed++)
            {
                var d = new RangeDirector(layout, seed * 7919, difficulty);
                var rng = new System.Random(seed);
                int n = d.Targets.Count;
                var pos = new Vector3[n];
                var yaw = new float[n];
                float nextHit = 3f;
                for (float t = 0f; t <= StrikeRules.RoundSeconds && problems.Count < 10; t += Step)
                {
                    d.Update(t);
                    // Hits now and then, mostly on road vehicles (wrecks next to live traffic are the hard case).
                    if (t >= nextHit)
                    {
                        nextHit = t + 2f + (float)rng.NextDouble() * 3f;
                        int slot = rng.Next(n);
                        if (rng.NextDouble() < 0.8)
                            for (int k = 0; k < n; k++)
                            {
                                int c = (slot + k) % n;
                                if (d.Targets[c].Active && d.Targets[c].Plan.Class != TargetClass.Static) { slot = c; break; }
                            }
                        d.Hit(slot, t, 0);
                    }
                    for (int i = 0; i < n; i++) d.ViewPose(i, t, out pos[i], out yaw[i]);
                    for (int i = 0; i < n; i++)
                        for (int j = i + 1; j < n; j++)
                        {
                            var a = d.Targets[i];
                            var b = d.Targets[j];
                            // A wreck that found no free spot sinks out of sight within half the roll time.
                            if (Sunk(a, t) || Sunk(b, t)) continue;
                            if (Overlap(pos[i], yaw[i], RangeVehicles.Footprint(a.Plan.Kind), pos[j], yaw[j], RangeVehicles.Footprint(b.Plan.Kind), 0.2f))
                                problems.Add($"seed {seed} t={t:F1}: slot {i} ({a.Plan.Class} {a.Plan.Kind}{(a.Active ? "" : ", wreck")}) overlaps slot {j} ({b.Plan.Class} {b.Plan.Kind}{(b.Active ? "" : ", wreck")}) at {pos[i]} / {pos[j]}");
                        }
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void Sections_AreDisjoint_AndOnTheRoads([Values(RangeDifficulty.Normal, RangeDifficulty.Hard)] RangeDifficulty difficulty)
        {
            var layout = Layout();
            var d = new RangeDirector(layout, 4242, difficulty);
            var (_, moving, evasive) = StrikeRules.Mix(difficulty);
            Assert.AreEqual((moving + RangeDirector.ConvoySize - 1) / RangeDirector.ConvoySize + evasive, d.Sections.Count, "one section per group");
            for (int i = 0; i < d.Sections.Count; i++)
            {
                var a = d.Sections[i];
                float roadLength = layout.Roads[a.road].Length;
                Assert.GreaterOrEqual(a.start - TargetMotion.LaneOffset, 0f, $"section {i} starts on its road");
                Assert.LessOrEqual(a.start + a.length + TargetMotion.LaneOffset, roadLength, $"section {i} ends on its road");
                Assert.Greater(a.length, RangeDirector.ConvoyGap * RangeDirector.ConvoySize * 2f, $"section {i} fits a convoy");
                for (int j = i + 1; j < d.Sections.Count; j++)
                {
                    var b = d.Sections[j];
                    if (a.road != b.road) continue;
                    bool apart = a.start + a.length + 2f * TargetMotion.LaneOffset <= b.start || b.start + b.length + 2f * TargetMotion.LaneOffset <= a.start;
                    Assert.IsTrue(apart, $"sections {i} and {j} on road {a.road} overlap");
                }
            }
        }

        [Test]
        public void Convoys_KeepTheirSpacing_AndRespawnInLine()
        {
            var layout = Layout();
            var d = new RangeDirector(layout, 99, RangeDifficulty.Hard);
            var convoy = new List<int>();
            for (int i = 0; i < d.Targets.Count && convoy.Count < RangeDirector.ConvoySize; i++)
                if (d.Targets[i].Plan.Class == TargetClass.Moving) convoy.Add(i);
            Assert.AreEqual(RangeDirector.ConvoySize, convoy.Count);
            // Knock out the middle one; it comes back in its place in the line.
            d.Hit(convoy[1], 10f, 0);
            for (float t = 0f; t < 60f; t += 0.5f)
            {
                d.Update(t);
                if (!d.Targets[convoy[0]].Active || !d.Targets[convoy[1]].Active || !d.Targets[convoy[2]].Active) continue;
                d.Pose(convoy[0], t, out var p0, out float y0, out _);
                d.Pose(convoy[1], t, out var p1, out float y1, out _);
                d.Pose(convoy[2], t, out var p2, out float y2, out _);
                // 18 m apart along the loop: never touching (near a U-turn two can pass on opposite lanes).
                var k0 = RangeVehicles.Footprint(d.Targets[convoy[0]].Plan.Kind);
                var k1 = RangeVehicles.Footprint(d.Targets[convoy[1]].Plan.Kind);
                var k2 = RangeVehicles.Footprint(d.Targets[convoy[2]].Plan.Kind);
                Assert.IsFalse(Overlap(p0, y0, k0, p1, y1, k1, 0.2f), $"t={t}: members 0 and 1 touch");
                Assert.IsFalse(Overlap(p1, y1, k1, p2, y2, k2, 0.2f), $"t={t}: members 1 and 2 touch");
                Assert.Less(Vector3.Distance(p0, p1), RangeDirector.ConvoyGap + 1f, $"t={t}: member 1 fell out of line");
            }
            Assert.Greater(d.Targets[convoy[1]].Plan.Generation, 0, "the hit member respawned");
        }
    }
}
