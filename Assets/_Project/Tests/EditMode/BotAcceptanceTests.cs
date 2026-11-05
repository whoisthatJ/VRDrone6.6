using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ushqyr.AI;
using Ushqyr.EditorTools.Bots;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>
    /// P4 acceptance, headless: bots fly the real courses (gates, lanes, props with colliders) through the same flight
    /// model as the player, from the cached racing lines.
    /// </summary>
    public sealed class BotAcceptanceTests
    {
        [SetUp]
        public void EmptyScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Tearing a built course down logs Unity's "cannot destroy while activating" for some kit parts; these
            // tests judge the bots, not the teardown.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        }

        private static IEnumerable<string> Courses() => BotHarness.RaceCourses();

        /// <summary>Elite completes every lap of every course on 10 seeds without missing a gate.</summary>
        [Test, TestCaseSource(nameof(Courses))]
        public void Elite_CompletesEveryLap_On10Seeds(string course)
        {
            var c = BotHarness.LoadCached(course);
            try
            {
                var elite = BotDifficultyProfile.Default(BotLevel.Elite);
                int laps = Mathf.Max(1, c.Data.Laps);
                var failures = new List<string>();
                for (int seed = 1; seed <= 10; seed++)
                {
                    var r = BotHarness.Fly(c, elite, seed, laps);
                    if (!r.Finished || r.Missed > 0 || r.Laps.Length != laps) failures.Add($"seed {seed}: {r}");
                }
                Assert.IsEmpty(failures, string.Join("\n", failures));
            }
            finally { c.Dispose(); }
        }

        /// <summary>Mean lap times are strictly ordered Rookie > Amateur > Pilot > Pro > Elite.</summary>
        [TestCase("stadium-oval")]
        [TestCase("field")]
        [TestCase("hangar")]
        public void LapTimes_AreOrderedByLevel(string course)
        {
            var c = BotHarness.LoadCached(course);
            try
            {
                var levels = new[] { BotLevel.Rookie, BotLevel.Amateur, BotLevel.Pilot, BotLevel.Pro, BotLevel.Elite };
                var means = new float[levels.Length];
                for (int i = 0; i < levels.Length; i++)
                {
                    var p = BotDifficultyProfile.Default(levels[i]);
                    var laps = new List<float>();
                    for (int seed = 1; seed <= 3; seed++) laps.AddRange(BotHarness.Fly(c, p, seed, 2).Laps);
                    Assert.IsNotEmpty(laps, levels[i] + " flew no lap");
                    means[i] = laps.Average();
                }
                string report = string.Join(", ", levels.Select((l, i) => $"{l} {means[i]:F1}"));
                for (int i = 1; i < means.Length; i++) Assert.Greater(means[i - 1], means[i], report);
            }
            finally { c.Dispose(); }
        }

        /// <summary>An Adaptive bot settles within 5 % of a recorded pace.</summary>
        [TestCase("field", 1.3f)]
        [TestCase("stadium-oval", 1.6f)]
        public void Adaptive_StaysWithinFivePercentOfThePace(string course, float slower)
        {
            var c = BotHarness.LoadCached(course);
            try
            {
                var elite = BotHarness.Fly(c, BotDifficultyProfile.Default(BotLevel.Elite), 1, 2);
                float target = elite.MeanFlyingLap * slower;
                var r = BotHarness.Fly(c, BotDifficultyProfile.Default(BotLevel.Adaptive), 3, 6, target);
                Assert.IsTrue(r.Finished, r.ToString());
                // After two laps of settling.
                float settled = r.Laps.Skip(2).Average();
                Assert.That(settled, Is.InRange(target * 0.95f, target * 1.05f), $"target {target:F1}: {r}");
            }
            finally { c.Dispose(); }
        }

        /// <summary>Seven bots cost little enough per frame to hold the frame rate (simulation + collisions, 90 fps frames).</summary>
        [Test]
        public void SevenBots_FitTheFrameBudget()
        {
            var c = BotHarness.LoadCached("expo-v2-sprint");
            try
            {
                var bots = new List<BotRun>();
                for (int i = 0; i < 7; i++)
                {
                    var b = new BotRun(c.Line, c.Track.Gates, c.Profile, BotDifficultyProfile.Default(BotLevel.Pilot), 3, BotHarness.Collision(), i + 1);
                    b.Place(c.Track.Spawn.position + Vector3.right * (i * 1.5f), c.Track.Spawn.rotation);
                    b.Start(0);
                    bots.Add(b);
                }
                const float frame = 1f / 90f;
                for (int f = 0; f < 90; f++) foreach (var b in bots) b.Advance(frame); // warm up
                var sw = Stopwatch.StartNew();
                const int frames = 900;
                for (int f = 0; f < frames; f++) foreach (var b in bots) b.Advance(frame);
                double ms = sw.Elapsed.TotalMilliseconds / frames;
                UnityEngine.Debug.Log($"[Ushqyr][Bots] 7 bots: {ms:F3} ms per 90 fps frame");
                Assert.Less(ms, 2.0, "7 bots must cost under 2 ms per frame (of the 11.1 ms budget at 90 fps)");
            }
            finally { c.Dispose(); }
        }
    }
}
