using System.Collections.Generic;
using System.Linq;
using Ushqyr.Strike;
using NUnit.Framework;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>
    /// P6 online Strike Range without a network: the message format, the host's hit validation, tolerance to latency
    /// and clock error, and a simulated round where four pilots on laggy links race for the same targets and every PC
    /// ends with the same targets and the same scoreboard.
    /// </summary>
    public sealed class StrikeNetTests
    {
        private static RangeLayout Layout()
        {
            var l = ScriptableObject.CreateInstance<RangeLayout>();
            l.Spots = Enumerable.Range(0, 24).Select(i => new StaticSpot { Position = new Vector3(i * 20f, 0f, 300f), Yaw = i * 15f }).ToArray();
            l.Pads = new[] { new PadSpot { Position = Vector3.zero } };
            l.Roads.Add(new RangeRoad { Points = Enumerable.Range(0, 60).Select(i => new Vector3(-300f + i * 10f, 0f, 200f + Mathf.Sin(i * 0.2f) * 30f)).ToArray() });
            l.Roads.Add(new RangeRoad { Points = Enumerable.Range(0, 50).Select(i => new Vector3(100f + Mathf.Cos(i * 0.15f) * 40f, 0f, -200f + i * 12f)).ToArray() });
            return l;
        }

        // A claim from a quad that really reached the target at time t.
        private static StrikeWire.HitClaim ClaimAt(RangeDirector d, int slot, float t, float speed = 15f)
        {
            d.Pose(slot, t, out var p, out _, out _);
            return new StrikeWire.HitClaim { Slot = slot, Generation = d.Targets[slot].Plan.Generation, Time = t, Speed = speed, Position = p + Vector3.up * 1.2f };
        }

        private static int MovingSlot(RangeDirector d) => d.Targets.FindIndex(t => t.Plan.Class == TargetClass.Moving);

        // ---------- wire format ----------

        [Test]
        public void Messages_RoundTrip()
        {
            var setup = new StrikeWire.RoundSetup { RaceId = 7, Seed = 123456789, Difficulty = RangeDifficulty.Hard, Bots = 3, BotLevel = 4 };
            Assert.AreEqual(setup, StrikeWire.DecodeSetup(StrikeWire.Encode(setup)));

            var claim = new StrikeWire.HitClaim { Slot = 12, Generation = 3, Time = 61.25f, Speed = 17.5f, Position = new Vector3(-120.5f, 4.25f, 333f) };
            Assert.AreEqual(claim, StrikeWire.DecodeClaim(StrikeWire.Encode(claim)));

            var confirm = new StrikeWire.HitConfirm { Slot = 12, Generation = 3, Pilot = StrikeWire.BotKeyBase + 2, Points = 413, Time = 61.2f };
            Assert.AreEqual(confirm, StrikeWire.DecodeConfirm(StrikeWire.Encode(confirm)));

            var reject = new StrikeWire.ClaimReject { Pilot = 5, Slot = 9, Verdict = RefereeVerdict.TooFar };
            Assert.AreEqual(reject, StrikeWire.DecodeReject(StrikeWire.Encode(reject)));

            var score = new StrikeWire.ScoreState { Pilot = 3, Score = 4321, Streak = 4, BestStreak = 6, Launches = 11, Crashes = 3, H0 = 2, H1 = 3, H2 = 1 };
            Assert.AreEqual(score, StrikeWire.DecodeScore(StrikeWire.Encode(score)));

            Assert.AreEqual(1.5f, StrikeWire.DecodeCrash(StrikeWire.EncodeCrash(1.5f)));

            var bots = new List<StrikeWire.BotPose>
            {
                new StrikeWire.BotPose { Index = 0, Down = false, Position = new Vector3(1, 2, 3), Rotation = Quaternion.Euler(10, 20, 30) },
                new StrikeWire.BotPose { Index = 2, Down = true, Position = new Vector3(-4, 5, -6), Rotation = Quaternion.identity },
            };
            var back = StrikeWire.DecodeBots(StrikeWire.Encode(bots));
            Assert.AreEqual(bots.Count, back.Count);
            for (int i = 0; i < bots.Count; i++)
            {
                Assert.AreEqual(bots[i].Index, back[i].Index);
                Assert.AreEqual(bots[i].Down, back[i].Down);
                Assert.AreEqual(bots[i].Position, back[i].Position);
                Assert.AreEqual(bots[i].Rotation, back[i].Rotation);
            }
        }

        [Test]
        public void Messages_AreSmall()
        {
            // Event messages stay tiny: the per-pilot cost is the 20 Hz snapshot stream, not the strike events.
            Assert.LessOrEqual(StrikeWire.Encode(new StrikeWire.HitClaim()).Length, 28);
            Assert.LessOrEqual(StrikeWire.Encode(new StrikeWire.HitConfirm()).Length, 12);
            Assert.LessOrEqual(StrikeWire.Encode(new StrikeWire.ScoreState()).Length, 20);
            var three = Enumerable.Range(0, 3).Select(i => new StrikeWire.BotPose { Index = (byte)i, Rotation = Quaternion.identity }).ToList();
            Assert.LessOrEqual(StrikeWire.Encode(three).Length, 100);
        }

        /// <summary>Round seeds go up to int.MaxValue (online setups, offline TickCount): bot names must not overflow.</summary>
        [Test]
        public void BotNames_WorkForAnySeed()
        {
            foreach (int seed in new[] { 0, 1, 1017909653, int.MaxValue, int.MaxValue - 3, -5, int.MinValue })
                for (int slot = 0; slot < 8; slot++)
                    Assert.IsFalse(string.IsNullOrEmpty(Ushqyr.AI.BotRoster.NameFor(slot, seed)), $"seed {seed} slot {slot}");
        }

        // ---------- the referee ----------

        [Test]
        public void Referee_AwardsAValidClaim_AndDisablesTheTarget()
        {
            var d = new RangeDirector(Layout(), 42, RangeDifficulty.Normal);
            var r = new StrikeReferee(d);
            var c = ClaimAt(d, 0, 10f);
            Assert.AreEqual(RefereeVerdict.Hit, r.Resolve(1, c, 10.1f, out var confirm));
            Assert.IsFalse(d.Targets[0].Active);
            Assert.AreEqual(1, d.Targets[0].HitBy);
            Assert.AreEqual(1, confirm.Pilot);
            Assert.AreEqual(StrikeRules.Points(d.Targets[0].Plan.Class, 0, 15f), confirm.Points);
            Assert.AreEqual(confirm.Points, r.Scores[1].Score);
            Assert.AreEqual(1, r.Scores[1].Streak);
        }

        [Test]
        public void Referee_FirstValidClaimWins()
        {
            var d = new RangeDirector(Layout(), 42, RangeDifficulty.Normal);
            var r = new StrikeReferee(d);
            Assert.AreEqual(RefereeVerdict.Hit, r.Resolve(1, ClaimAt(d, 3, 20f), 20.2f, out _));
            // The second pilot hit it a moment earlier, but their claim arrived later: it doesn't count.
            Assert.AreEqual(RefereeVerdict.AlreadyDown, r.Resolve(2, ClaimAt(d, 3, 19.95f), 20.3f, out _));
            Assert.AreEqual(1, d.Targets[3].HitBy);
            Assert.AreEqual(0, r.Scores[2].Score);
            Assert.AreEqual(1, r.Scores[2].Crashes, "a lost claim ends the launch like a crash");
        }

        [Test]
        public void Referee_RejectsBadClaims()
        {
            var d = new RangeDirector(Layout(), 42, RangeDifficulty.Normal);
            var r = new StrikeReferee(d);
            var good = ClaimAt(d, 1, 30f);

            var slow = good;
            slow.Speed = StrikeRules.HitSpeed - 0.5f;
            Assert.AreEqual(RefereeVerdict.TooSlow, r.Check(slow, 30.1f));

            var far = good;
            far.Position += new Vector3(StrikeReferee.PositionTolerance + 2f, 0f, 0f);
            Assert.AreEqual(RefereeVerdict.TooFar, r.Check(far, 30.1f));

            var high = good;
            high.Position += Vector3.up * (StrikeReferee.HeightTolerance + 3f);
            Assert.AreEqual(RefereeVerdict.TooFar, r.Check(high, 30.1f));

            Assert.AreEqual(RefereeVerdict.BadTime, r.Check(good, 30f + StrikeReferee.MaxClaimAge + 0.5f), "too old on arrival");
            Assert.AreEqual(RefereeVerdict.BadTime, r.Check(good, 30f - StrikeReferee.MaxClaimLead - 0.5f), "from the future");

            var stale = good;
            stale.Generation = good.Generation + 1;
            Assert.AreEqual(RefereeVerdict.Stale, r.Check(stale, 30.1f));

            var unknown = good;
            unknown.Slot = d.Targets.Count + 4;
            Assert.AreEqual(RefereeVerdict.UnknownSlot, r.Check(unknown, 30.1f));

            Assert.AreEqual(RefereeVerdict.Hit, r.Check(good, 30.1f), "the untouched claim is fine");
        }

        [Test]
        public void Referee_StaleAfterRespawn()
        {
            var d = new RangeDirector(Layout(), 42, RangeDifficulty.Normal);
            var r = new StrikeReferee(d);
            var old = ClaimAt(d, 2, 5f);
            Assert.AreEqual(RefereeVerdict.Hit, r.Resolve(0, old, 5.1f, out _));
            d.Update(5f + StrikeRules.RespawnMax + 0.1f);
            Assert.IsTrue(d.Targets[2].Active, "respawned");
            var replay = old;
            replay.Time = 5f + StrikeRules.RespawnMax + 0.2f;
            Assert.AreEqual(RefereeVerdict.Stale, r.Check(replay, replay.Time), "a claim for the old life is not a hit on the new one");
        }

        /// <summary>Latency up to the claim age and clock-sync error both ways still count for a moving target.</summary>
        [Test]
        public void Referee_ToleratesLatencyAndClockError([Values(0.03f, 0.15f, 0.35f, 0.8f)] float latency, [Values(-0.2f, 0f, 0.2f)] float clockError)
        {
            var d = new RangeDirector(Layout(), 77, RangeDifficulty.Hard);
            int slot = MovingSlot(d);
            Assert.GreaterOrEqual(slot, 0, "the Hard mix has moving targets");
            var r = new StrikeReferee(d);
            const float hostTime = 40f;
            // The pilot's clock reads hostTime + clockError when the quad touches the target (which is where it really is at hostTime).
            d.Pose(slot, hostTime, out var where, out _, out _);
            var claim = new StrikeWire.HitClaim { Slot = slot, Generation = d.Targets[slot].Plan.Generation, Time = hostTime + clockError, Speed = 14f, Position = where + Vector3.up };
            Assert.AreEqual(RefereeVerdict.Hit, r.Check(claim, hostTime + latency), $"latency {latency}, clock error {clockError}");
        }

        // ---------- a simulated online round ----------

        private struct Packet
        {
            public float Arrive;
            public int From, To; // -1 = host
            public byte[] Data;
            public string Kind;
        }

        /// <summary>
        /// Host + 3 clients, each with its own director (same seed), events delayed by random latency (20-250 ms) and
        /// frames at different rates. The pilots claim targets near each other; the host resolves; confirms and scores
        /// reach everyone late. At the end every PC has the same targets, and the host's scoreboard is everyone's.
        /// </summary>
        [Test]
        public void SimulatedRound_StaysInSyncOnEveryPc()
        {
            var layout = Layout();
            const int seed = 9001, pilots = 4;
            var host = new RangeDirector(layout, seed, RangeDifficulty.Hard);
            var referee = new StrikeReferee(host);
            var clients = Enumerable.Range(0, pilots).Select(_ => new RangeDirector(layout, seed, RangeDifficulty.Hard)).ToArray(); // pilot 0 is the host's own view
            var scores = Enumerable.Range(0, pilots).Select(_ => new Dictionary<int, StrikeWire.ScoreState>()).ToArray();
            var rng = new System.Random(5);
            var queue = new List<Packet>();
            float[] frame = { 1f / 90f, 1f / 72f, 1f / 60f, 1f / 30f };
            float[] next = new float[pilots];
            float[] nextClaim = { 1f, 1.3f, 1.6f, 1.9f };
            int disputes = 0;
            float Lag() => 0.02f + (float)rng.NextDouble() * 0.23f;

            for (float t = 0f; t <= StrikeRules.RoundSeconds + 2f; t += 0.002f)
            {
                // Deliveries due now.
                foreach (var p in queue.Where(q => q.Arrive <= t).OrderBy(q => q.Arrive).ToList())
                {
                    queue.Remove(p);
                    if (p.To < 0)
                    {
                        if (p.Kind == "claim")
                        {
                            var v = referee.Resolve(p.From, StrikeWire.DecodeClaim(p.Data), t, out var confirm);
                            if (v == RefereeVerdict.Hit)
                                for (int i = 0; i < pilots; i++) queue.Add(new Packet { Arrive = t + Lag(), To = i, Kind = "confirm", Data = StrikeWire.Encode(confirm) });
                            else if (v == RefereeVerdict.AlreadyDown || v == RefereeVerdict.Stale) disputes++;
                            var s = StrikeWire.Encode(StrikeWire.ScoreState.From(p.From, referee.Scores[p.From]));
                            for (int i = 0; i < pilots; i++) queue.Add(new Packet { Arrive = t + Lag(), To = i, Kind = "score", Data = s });
                        }
                        continue;
                    }
                    if (p.Kind == "confirm")
                    {
                        var c = StrikeWire.DecodeConfirm(p.Data);
                        var target = clients[p.To].Targets[c.Slot];
                        if (target.Plan.Generation == c.Generation && target.Active) clients[p.To].Hit(c.Slot, c.Time, c.Pilot);
                    }
                    else if (p.Kind == "score")
                    {
                        var s = StrikeWire.DecodeScore(p.Data);
                        scores[p.To][s.Pilot] = s;
                    }
                }

                host.Update(t);
                for (int i = 0; i < pilots; i++)
                {
                    if (t < next[i]) continue;
                    next[i] += frame[i];
                    clients[i].Update(t);
                    // Every pilot goes for the same nearest active target from time to time: contested hits.
                    if (t >= nextClaim[i] && t < StrikeRules.RoundSeconds)
                    {
                        nextClaim[i] = t + 2.5f + (float)rng.NextDouble() * 2f;
                        int slot = clients[i].Targets.FindIndex(x => x.Active);
                        if (slot < 0) continue;
                        var claim = ClaimAt(clients[i], slot, t);
                        queue.Add(new Packet { Arrive = t + Lag(), From = i, To = -1, Kind = "claim", Data = StrikeWire.Encode(claim) });
                    }
                }
            }
            Assert.IsEmpty(queue, "everything was delivered");

            // The same targets everywhere: same life, same state, same plan, same killer.
            for (int i = 0; i < pilots; i++)
                for (int s = 0; s < host.Targets.Count; s++)
                {
                    var a = host.Targets[s];
                    var b = clients[i].Targets[s];
                    Assert.AreEqual(a.Plan.Generation, b.Plan.Generation, $"pc {i} slot {s} generation");
                    Assert.AreEqual(a.Active, b.Active, $"pc {i} slot {s} active");
                    Assert.AreEqual(a.Plan.Place, b.Plan.Place, $"pc {i} slot {s} place");
                    Assert.AreEqual(a.Plan.Kind, b.Plan.Kind, $"pc {i} slot {s} kind");
                    Assert.AreEqual(a.HitBy, b.HitBy, $"pc {i} slot {s} hit by");
                }

            // The same scoreboard everywhere, matching the host's.
            int totalHits = 0;
            foreach (var kv in referee.Scores)
            {
                totalHits += kv.Value.Hits;
                var expected = StrikeWire.ScoreState.From(kv.Key, kv.Value);
                for (int i = 0; i < pilots; i++) Assert.AreEqual(expected, scores[i][kv.Key], $"pc {i} sees pilot {kv.Key}'s score");
            }
            Assert.Greater(totalHits, 20, "the pilots scored");
            Assert.Greater(disputes, 0, "some claims were contested (the test covers the race for a target)");
            Debug.Log($"[Ushqyr][Strike] Simulated online round: {totalHits} hits, {disputes} contested claims turned down");
        }
    }
}
