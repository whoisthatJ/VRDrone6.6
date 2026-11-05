using System.Linq;
using NUnit.Framework;
using Ushqyr.Strike;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>P5 Strike Range: scoring and hit rules, deterministic targets.</summary>
    public sealed class StrikeRulesTests
    {
        [Test]
        public void HitRule_EightMetresPerSecondOrMore()
        {
            Assert.IsFalse(StrikeRules.IsHit(7.99f));
            Assert.IsTrue(StrikeRules.IsHit(8f));
            Assert.IsTrue(StrikeRules.IsHit(30f));
        }

        [Test]
        public void Points_BaseTimesStreakTimesSpeedBonus()
        {
            Assert.AreEqual(100, StrikeRules.Points(TargetClass.Static, 0, 8f));
            Assert.AreEqual(200, StrikeRules.Points(TargetClass.Moving, 0, 8f));
            Assert.AreEqual(300, StrikeRules.Points(TargetClass.Evasive, 0, 8f));
            // Third hit in a row: x1.2; 18 m/s: +10 %.
            Assert.AreEqual(Mathf.RoundToInt(100 * 1.2f * 1.1f), StrikeRules.Points(TargetClass.Static, 2, 18f));
            // The speed bonus is capped at +25 %.
            Assert.AreEqual(125, StrikeRules.Points(TargetClass.Static, 0, 80f));
        }

        [Test]
        public void Streak_GrowsByTenPercentUpToDouble()
        {
            Assert.AreEqual(1f, StrikeRules.Multiplier(0), 1e-5f);
            Assert.AreEqual(1.5f, StrikeRules.Multiplier(5), 1e-5f);
            Assert.AreEqual(2f, StrikeRules.Multiplier(10), 1e-5f);
            Assert.AreEqual(2f, StrikeRules.Multiplier(25), 1e-5f);
        }

        [Test]
        public void Score_CrashResetsTheStreakAndCountsAccuracy()
        {
            var s = new StrikeScore();
            for (int i = 0; i < 3; i++)
            {
                s.Launch();
                s.Hit(TargetClass.Static, 8f);
            }
            Assert.AreEqual(100 + 110 + 120, s.Score);
            Assert.AreEqual(3, s.Streak);
            s.Launch();
            s.Crash();
            Assert.AreEqual(0, s.Streak);
            Assert.AreEqual(3, s.BestStreak);
            s.Launch();
            s.Hit(TargetClass.Moving, 8f);
            Assert.AreEqual(330 + 200, s.Score, "after a crash the multiplier starts again at x1");
            Assert.AreEqual(4, s.Hits);
            Assert.AreEqual(1, s.HitsByClass[1]);
            Assert.AreEqual(0.8f, s.Accuracy, 1e-5f);
        }

        [Test]
        public void Difficulty_EasyIsStaticOnlyWithoutWind()
        {
            var (st, mv, ev) = StrikeRules.Mix(RangeDifficulty.Easy);
            Assert.Greater(st, 0);
            Assert.AreEqual(0, mv + ev);
            Assert.AreEqual((0f, 0f), StrikeRules.Wind(RangeDifficulty.Easy));
            var hard = StrikeRules.Mix(RangeDifficulty.Hard);
            Assert.Greater(hard.evasive, StrikeRules.Mix(RangeDifficulty.Normal).evasive);
        }

        // ---------- targets ----------

        private static RangeLayout Layout()
        {
            var l = ScriptableObject.CreateInstance<RangeLayout>();
            l.Spots = Enumerable.Range(0, 24).Select(i => new StaticSpot { Position = new Vector3(i * 20f, 0f, 300f), Yaw = i * 15f }).ToArray();
            l.Pads = new[] { new PadSpot { Position = Vector3.zero } };
            l.Roads.Add(new RangeRoad { Points = Enumerable.Range(0, 60).Select(i => new Vector3(-300f + i * 10f, 0f, 200f + Mathf.Sin(i * 0.2f) * 30f)).ToArray() });
            l.Roads.Add(new RangeRoad { Points = Enumerable.Range(0, 50).Select(i => new Vector3(100f + Mathf.Cos(i * 0.15f) * 40f, 0f, -200f + i * 12f)).ToArray() });
            return l;
        }

        [Test]
        public void Targets_SameSeedSamePosesEverywhere([Values(RangeDifficulty.Normal, RangeDifficulty.Hard)] RangeDifficulty d)
        {
            var layout = Layout();
            var a = new RangeDirector(layout, 1234, d);
            var b = new RangeDirector(layout, 1234, d);
            var c = new RangeDirector(layout, 999, d);
            Assert.AreEqual(a.Targets.Count, b.Targets.Count);
            bool differs = false;
            foreach (float t in new[] { 0f, 7.3f, 31f, 64.2f, 119.9f })
                for (int i = 0; i < a.Targets.Count; i++)
                {
                    a.Pose(i, t, out var pa, out float ya, out _);
                    b.Pose(i, t, out var pb, out float yb, out _);
                    Assert.AreEqual(pa, pb, $"slot {i} at {t}");
                    Assert.AreEqual(ya, yb);
                    c.Pose(i, t, out var pc, out _, out _);
                    if ((pa - pc).sqrMagnitude > 1f) differs = true;
                }
            Assert.IsTrue(differs, "another seed gives another round");
        }

        [Test]
        public void Targets_MovingDriveAtTenToFortyKmh()
        {
            var layout = Layout();
            var dir = new RangeDirector(layout, 42, RangeDifficulty.Normal);
            var moving = dir.Targets.Where(t => t.Plan.Class == TargetClass.Moving).ToList();
            Assert.IsNotEmpty(moving);
            foreach (var t in moving)
            {
                float kmh = t.Plan.Speed * 3.6f;
                Assert.That(kmh, Is.InRange(10f, 40f));
                // Measured over one second (ping-pong turns aside), it moves at the planned speed.
                TargetMotion.Pose(layout, t.Plan, 20f, out var p0, out _, out _);
                TargetMotion.Pose(layout, t.Plan, 21f, out var p1, out _, out _);
                Assert.LessOrEqual(Vector3.Distance(p0, p1), t.Plan.Speed * 1.05f + 0.01f);
            }
        }

        [Test]
        public void Targets_StaticStartOnDistinctSpots()
        {
            var dir = new RangeDirector(Layout(), 7, RangeDifficulty.Easy);
            var spots = dir.Targets.Where(t => t.Plan.Class == TargetClass.Static).Select(t => t.Plan.Place).ToList();
            Assert.AreEqual(spots.Count, spots.Distinct().Count());
        }

        [Test]
        public void Targets_HitDisablesThenRespawnsInTenToFifteenSeconds()
        {
            var dir = new RangeDirector(Layout(), 5, RangeDifficulty.Normal);
            Assert.IsTrue(dir.Hit(0, 30f, 1));
            Assert.IsFalse(dir.Hit(0, 30.1f, 2), "the first valid hit wins");
            var t = dir.Targets[0];
            Assert.IsFalse(t.Active);
            Assert.AreEqual(1, t.HitBy);
            Assert.That(t.RespawnAt - 30f, Is.InRange(StrikeRules.RespawnMin, StrikeRules.RespawnMax));
            dir.Update(t.RespawnAt - 0.01f);
            Assert.IsFalse(t.Active);
            dir.Update(t.RespawnAt);
            Assert.IsTrue(t.Active);
            Assert.AreEqual(1, t.Plan.Generation);
            Assert.AreEqual(t.DisabledAt + (t.Plan.SpawnTime - t.DisabledAt), t.Plan.SpawnTime);
        }

        [Test]
        public void Evasive_StopsReversesAndPopsSmoke()
        {
            var plan = new TargetPlan { Class = TargetClass.Evasive, Speed = 8f, Seed = 77u, Direction = 1 };
            var speeds = Enumerable.Range(0, 200).Select(k => TargetMotion.SegmentSpeed(plan, k)).ToList();
            Assert.IsTrue(speeds.Any(v => v > 0f));
            Assert.IsTrue(speeds.Any(v => v == 0f));
            Assert.IsTrue(speeds.Any(v => v < 0f));
            Assert.IsTrue(Enumerable.Range(0, 200).Any(k => TargetMotion.SmokeStartsIn(plan, k)));
        }

        [Test]
        public void PingPong_StaysOnTheRoad()
        {
            for (float d = -500f; d < 500f; d += 13.7f)
            {
                float u = TargetMotion.PingPong(d, 120f, out int h);
                Assert.That(u, Is.InRange(0f, 120f));
                Assert.That(h == 1 || h == -1);
            }
        }
    }
}
