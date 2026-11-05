using System.Collections.Generic;
using System.Linq;
using Ushqyr.AI;
using Ushqyr.Garage;
using Ushqyr.Strike;
using Ushqyr.Vehicle;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>
    /// P5 acceptance, headless: Strike Range bots fly full 120 s rounds on the real Range_Steppe (terrain collider,
    /// target hitboxes) through the player's flight model, and the levels are clearly separated by score.
    /// </summary>
    public sealed class StrikeBotTests
    {
        private const string Scene = "Assets/_Project/Scenes/Range_Steppe.unity";
        private const float Frame = 1f / 60f;

        private RangeLayout _layout;
        private CollisionSettings _collision;

        [OneTimeSetUp]
        public void OpenRange()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var session = Object.FindAnyObjectByType<StrikeSession>();
            Assert.NotNull(session, "Range_Steppe has a StrikeSession");
            _layout = session.Layout;
            Assert.NotNull(_layout, "the session has its RangeLayout");
            var quad = Object.FindAnyObjectByType<QuadVehicle>();
            _collision = quad != null ? quad.BaseCollision : CollisionSettings.Default;
        }

        [OneTimeTearDown]
        public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        public struct RoundResult
        {
            public int Score, Hits, Crashes, Launches;
            public override string ToString() => $"{Score} pts, {Hits} hits, {Crashes} crashes, {Launches} launches";
        }

        /// <summary>One solo bot round (no player), 60 fps frames, the same director logic as the game.</summary>
        private RoundResult Round(BotLevel level, int seed, RangeDifficulty difficulty)
        {
            var root = new GameObject("Strike Test Round").transform;
            try
            {
                int layer = LayerMask.NameToLayer("TrackGeometry");
                var director = new RangeDirector(_layout, seed, difficulty);
                var views = new List<RangeTargetView>();
                for (int i = 0; i < director.Targets.Count; i++)
                {
                    var v = RangeTargetView.Create(root, director, i, layer);
                    v.Sync(0f);
                    views.Add(v);
                }
                var build = BotRoster.BuildFor(SizeClass.Five, 0);
                var bot = new StrikeBot(0, "Test", director, BuildComposer.Compose(build), BotDifficultyProfile.For(level), _collision, seed * 13);
                bot.Launch(0, 0f);
                for (float t = 0f; t < StrikeRules.RoundSeconds; t += Frame)
                {
                    director.Update(t);
                    foreach (var v in views) v.Sync(t);
                    Physics.SyncTransforms();
                    bot.Advance(Frame, t, _ => seed % _layout.Pads.Length);
                }
                return new RoundResult { Score = bot.Score.Score, Hits = bot.Score.Hits, Crashes = bot.Score.Crashes, Launches = bot.Score.Launches };
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        [Test]
        public void Layout_HasPadsSpotsRoadsAndHeights()
        {
            Assert.AreEqual(StrikeRules.Pads, _layout.Pads.Length);
            Assert.GreaterOrEqual(_layout.Spots.Length, 20);
            Assert.GreaterOrEqual(_layout.Roads.Count, 3);
            Assert.AreEqual(_layout.HeightRes * _layout.HeightRes, _layout.Heights.Length);
            foreach (var p in _layout.Pads)
                Assert.AreEqual(_layout.Height(p.Position.x, p.Position.z), p.Position.y - 0.2f, 0.3f, "pads sit on the ground");
        }

        /// <summary>Rookie, Pilot and Elite over 10 rounds (Normal mix): mean scores strictly ordered and well apart.</summary>
        [Test]
        public void Levels_AreSeparatedByScore_Over10Rounds()
        {
            var levels = new[] { BotLevel.Rookie, BotLevel.Pilot, BotLevel.Elite };
            var means = new float[levels.Length];
            var log = new List<string>();
            for (int l = 0; l < levels.Length; l++)
            {
                var rounds = new List<RoundResult>();
                for (int seed = 1; seed <= 10; seed++) rounds.Add(Round(levels[l], seed, RangeDifficulty.Normal));
                means[l] = (float)rounds.Average(r => r.Score);
                log.Add($"{levels[l]}: mean {means[l]:F0} | " + string.Join("; ", rounds));
            }
            string report = string.Join("\n", log);
            Debug.Log("[Ushqyr][Strike] Bot rounds\n" + report);
            Assert.Greater(means[0], 0f, "even a rookie scores\n" + report);
            Assert.Greater(means[1], means[0] * 1.25f, "Pilot clearly above Rookie\n" + report);
            Assert.Greater(means[2], means[1] * 1.25f, "Elite clearly above Pilot\n" + report);
        }
    }
}
