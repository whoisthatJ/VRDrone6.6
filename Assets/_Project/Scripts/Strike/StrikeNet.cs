using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ushqyr.Strike
{
    /// <summary>
    /// The online Strike Range protocol (P6), transport-free. Targets move on deterministic paths from the round seed
    /// and the shared clock, so only events travel: the round setup, hit claims and crashes (pilot → host), confirmed
    /// hits, rejections and scores (host → everyone), and the host's bots. Pilots keep sending their own quad's
    /// snapshots through the race network as in online races.
    /// </summary>
    public static class StrikeWire
    {
        public const string Setup = "us.c.strike.setup", Confirm = "us.c.strike.hit", Reject = "us.c.strike.reject",
            Score = "us.c.strike.score", Bots = "us.c.strike.bots";
        public const string Claim = "us.s.strike.claim", Crash = "us.s.strike.crash";

        /// <summary>Pilot keys: humans use their grid slot (0-7), the host's bots 100 + index.</summary>
        public const int BotKeyBase = 100;

        public struct RoundSetup
        {
            public int RaceId, Seed;
            public RangeDifficulty Difficulty;
            public int Bots;
            public int BotLevel;
        }

        public struct HitClaim
        {
            public int Slot, Generation;
            public float Time, Speed;
            public Vector3 Position;
        }

        public struct HitConfirm
        {
            public int Slot, Generation, Pilot, Points;
            public float Time;
        }

        public struct ClaimReject
        {
            public int Pilot, Slot;
            public RefereeVerdict Verdict;
        }

        public struct ScoreState
        {
            public int Pilot, Score, Streak, BestStreak, Launches, Crashes, H0, H1, H2;

            public static ScoreState From(int pilot, StrikeScore s) => new ScoreState
            {
                Pilot = pilot, Score = s.Score, Streak = s.Streak, BestStreak = s.BestStreak, Launches = s.Launches,
                Crashes = s.Crashes, H0 = s.HitsByClass[0], H1 = s.HitsByClass[1], H2 = s.HitsByClass[2],
            };

            public void ApplyTo(StrikeScore s)
            {
                s.Score = Score;
                s.Streak = Streak;
                s.BestStreak = BestStreak;
                s.Launches = Launches;
                s.Crashes = Crashes;
                s.HitsByClass[0] = H0;
                s.HitsByClass[1] = H1;
                s.HitsByClass[2] = H2;
            }
        }

        public struct BotPose
        {
            public byte Index;
            public bool Down;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        // ---------- encoding ----------

        public static byte[] Encode(RoundSetup m) => Write(w =>
        {
            w.Write(m.RaceId);
            w.Write(m.Seed);
            w.Write((byte)m.Difficulty);
            w.Write((byte)m.Bots);
            w.Write((byte)m.BotLevel);
        });

        public static RoundSetup DecodeSetup(byte[] b)
        {
            using var r = Reader(b);
            return new RoundSetup { RaceId = r.ReadInt32(), Seed = r.ReadInt32(), Difficulty = (RangeDifficulty)r.ReadByte(), Bots = r.ReadByte(), BotLevel = r.ReadByte() };
        }

        public static byte[] Encode(HitClaim m) => Write(w =>
        {
            w.Write((byte)m.Slot);
            w.Write(m.Generation);
            w.Write(m.Time);
            w.Write(m.Speed);
            V(w, m.Position);
        });

        public static HitClaim DecodeClaim(byte[] b)
        {
            using var r = Reader(b);
            return new HitClaim { Slot = r.ReadByte(), Generation = r.ReadInt32(), Time = r.ReadSingle(), Speed = r.ReadSingle(), Position = V(r) };
        }

        public static byte[] EncodeCrash(float time) => Write(w => w.Write(time));
        public static float DecodeCrash(byte[] b)
        {
            using var r = Reader(b);
            return r.ReadSingle();
        }

        public static byte[] Encode(HitConfirm m) => Write(w =>
        {
            w.Write((byte)m.Slot);
            w.Write(m.Generation);
            w.Write((byte)m.Pilot);
            w.Write((short)m.Points);
            w.Write(m.Time);
        });

        public static HitConfirm DecodeConfirm(byte[] b)
        {
            using var r = Reader(b);
            return new HitConfirm { Slot = r.ReadByte(), Generation = r.ReadInt32(), Pilot = r.ReadByte(), Points = r.ReadInt16(), Time = r.ReadSingle() };
        }

        public static byte[] Encode(ClaimReject m) => Write(w =>
        {
            w.Write((byte)m.Pilot);
            w.Write((byte)m.Slot);
            w.Write((byte)m.Verdict);
        });

        public static ClaimReject DecodeReject(byte[] b)
        {
            using var r = Reader(b);
            return new ClaimReject { Pilot = r.ReadByte(), Slot = r.ReadByte(), Verdict = (RefereeVerdict)r.ReadByte() };
        }

        public static byte[] Encode(ScoreState m) => Write(w =>
        {
            w.Write((byte)m.Pilot);
            w.Write(m.Score);
            w.Write((short)m.Streak);
            w.Write((short)m.BestStreak);
            w.Write((short)m.Launches);
            w.Write((short)m.Crashes);
            w.Write((short)m.H0);
            w.Write((short)m.H1);
            w.Write((short)m.H2);
        });

        public static ScoreState DecodeScore(byte[] b)
        {
            using var r = Reader(b);
            return new ScoreState
            {
                Pilot = r.ReadByte(), Score = r.ReadInt32(), Streak = r.ReadInt16(), BestStreak = r.ReadInt16(), Launches = r.ReadInt16(),
                Crashes = r.ReadInt16(), H0 = r.ReadInt16(), H1 = r.ReadInt16(), H2 = r.ReadInt16(),
            };
        }

        public static byte[] Encode(IReadOnlyList<BotPose> bots) => Write(w =>
        {
            w.Write((byte)bots.Count);
            foreach (var b in bots)
            {
                w.Write(b.Index);
                w.Write(b.Down);
                V(w, b.Position);
                // Smallest-three would save 4 bytes a bot; 3 bots at 10 Hz are not worth it.
                w.Write(b.Rotation.x);
                w.Write(b.Rotation.y);
                w.Write(b.Rotation.z);
                w.Write(b.Rotation.w);
            }
        });

        public static List<BotPose> DecodeBots(byte[] b)
        {
            using var r = Reader(b);
            int n = r.ReadByte();
            var list = new List<BotPose>(n);
            for (int i = 0; i < n; i++)
                list.Add(new BotPose { Index = r.ReadByte(), Down = r.ReadBoolean(), Position = V(r), Rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) });
            return list;
        }

        private static byte[] Write(Action<BinaryWriter> body)
        {
            using var ms = new MemoryStream(64);
            using (var w = new BinaryWriter(ms)) body(w);
            return ms.ToArray();
        }

        private static BinaryReader Reader(byte[] b) => new BinaryReader(new MemoryStream(b ?? Array.Empty<byte>()));

        private static void V(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        private static Vector3 V(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    public enum RefereeVerdict : byte
    {
        Hit = 0,
        /// <summary>The target was already disabled (someone else's hit arrived first).</summary>
        AlreadyDown = 1,
        /// <summary>The claim is for an earlier life of the slot (the target has respawned since).</summary>
        Stale = 2,
        TooSlow = 3,
        /// <summary>The claimed position is not near the target at the claimed time.</summary>
        TooFar = 4,
        /// <summary>The claim's time is outside the round or too far behind / ahead of the host clock.</summary>
        BadTime = 5,
        UnknownSlot = 6,
    }

    /// <summary>
    /// The host's hit validation (P6): a claim is a hit when the slot exists, it is still the same life of the target
    /// and the target is up, the impact speed counts, the claim time is within the round and not too old, and the quad
    /// was near the target at that time. The first valid claim to reach the host wins. Also keeps the authoritative
    /// score of every pilot.
    /// </summary>
    public sealed class StrikeReferee
    {
        /// <summary>Horizontal distance between the claimed impact and the target's centre that still counts (m):
        /// half a vehicle length plus the quad, plus room for clock and pose error.</summary>
        public const float PositionTolerance = 7f;
        /// <summary>Vertical tolerance above the target's ground point (m).</summary>
        public const float HeightTolerance = 6f;
        /// <summary>How old a claim may be on arrival (s): a round-trip at a bad connection.</summary>
        public const float MaxClaimAge = 1.0f;
        /// <summary>How far a claim may be ahead of the host clock (s): clock-sync error.</summary>
        public const float MaxClaimLead = 0.25f;

        private readonly RangeDirector _director;
        public readonly Dictionary<int, StrikeScore> Scores = new Dictionary<int, StrikeScore>();

        public StrikeReferee(RangeDirector director) => _director = director;

        public StrikeScore ScoreFor(int pilot, string name = null)
        {
            if (!Scores.TryGetValue(pilot, out var s)) Scores[pilot] = s = new StrikeScore { Pilot = name ?? $"Pilot {pilot + 1}" };
            else if (name != null) s.Pilot = name;
            return s;
        }

        public RefereeVerdict Check(in StrikeWire.HitClaim c, float hostNow)
        {
            if (c.Slot < 0 || c.Slot >= _director.Targets.Count) return RefereeVerdict.UnknownSlot;
            if (c.Time < 0f || c.Time > StrikeRules.RoundSeconds + MaxClaimLead || c.Time < hostNow - MaxClaimAge || c.Time > hostNow + MaxClaimLead)
                return RefereeVerdict.BadTime;
            var t = _director.Targets[c.Slot];
            if (t.Plan.Generation != c.Generation) return RefereeVerdict.Stale;
            if (!t.Active) return RefereeVerdict.AlreadyDown;
            if (!StrikeRules.IsHit(c.Speed)) return RefereeVerdict.TooSlow;
            _director.Pose(c.Slot, c.Time, out var pos, out _, out _);
            Vector2 d = new Vector2(c.Position.x - pos.x, c.Position.z - pos.z);
            if (d.magnitude > PositionTolerance || c.Position.y - pos.y > HeightTolerance || c.Position.y < pos.y - 2f) return RefereeVerdict.TooFar;
            return RefereeVerdict.Hit;
        }

        /// <summary>Validates and, when it is a hit, disables the target and scores it for <paramref name="pilot"/>.</summary>
        public RefereeVerdict Resolve(int pilot, in StrikeWire.HitClaim c, float hostNow, out StrikeWire.HitConfirm confirm)
        {
            confirm = default;
            var s = ScoreFor(pilot);
            var v = Check(c, hostNow);
            s.Launch();
            if (v != RefereeVerdict.Hit)
            {
                s.Crash();
                return v;
            }
            float time = Mathf.Min(c.Time, hostNow);
            _director.Hit(c.Slot, time, pilot);
            int pts = s.Hit(_director.Targets[c.Slot].Plan.Class, c.Speed);
            confirm = new StrikeWire.HitConfirm { Slot = c.Slot, Generation = c.Generation, Pilot = pilot, Points = pts, Time = time };
            return v;
        }

        /// <summary>A crash away from any target: the launch ends, the streak resets.</summary>
        public void Crash(int pilot)
        {
            var s = ScoreFor(pilot);
            s.Launch();
            s.Crash();
        }
    }
}
