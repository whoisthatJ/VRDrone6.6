using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ushqyr.Strike
{
    public enum TargetClass
    {
        /// <summary>Parked in a revetment or in the open (100 pts).</summary>
        Static = 0,
        /// <summary>Driving a dirt road at 10-40 km/h, alone or in a convoy (200 pts).</summary>
        Moving = 1,
        /// <summary>Stop-go, reversing, zig-zag and smoke screens (300 pts).</summary>
        Evasive = 2,
    }

    public enum RangeDifficulty
    {
        /// <summary>Static targets only, no wind.</summary>
        Easy = 0,
        /// <summary>Static and moving targets, a few evasive ones, light wind.</summary>
        Normal = 1,
        /// <summary>More evasive targets, gusty wind, smoke.</summary>
        Hard = 2,
    }

    /// <summary>Strike Range rules (P5). Plain numbers: tests, bots and the online host all read them from here.</summary>
    public static class StrikeRules
    {
        public const float RoundSeconds = 120f;
        public const float CountdownSeconds = 3f;
        /// <summary>A contact at this speed or faster is a hit; slower is a bump (a crash, no score).</summary>
        public const float HitSpeed = 8f;
        /// <summary>Each hit without a crash raises the multiplier by this much, up to <see cref="MaxMultiplier"/>.</summary>
        public const float StreakStep = 0.1f;
        public const float MaxMultiplier = 2f;
        /// <summary>Speed bonus: +1 % per m/s above <see cref="HitSpeed"/>, at most +25 %.</summary>
        public const float SpeedBonusPerMs = 0.01f, MaxSpeedBonus = 0.25f;
        /// <summary>Relaunch from a pad this long after a hit or a crash.</summary>
        public const float RelaunchSeconds = 1.5f;
        /// <summary>A disabled target comes back elsewhere after 10-15 s.</summary>
        public const float RespawnMin = 10f, RespawnMax = 15f;
        public const int Pads = 3;

        public static int BasePoints(TargetClass c) => c switch
        {
            TargetClass.Moving => 200,
            TargetClass.Evasive => 300,
            _ => 100,
        };

        /// <summary>The multiplier for the next hit after <paramref name="streak"/> hits in a row.</summary>
        public static float Multiplier(int streak) => Math.Min(MaxMultiplier, 1f + StreakStep * Math.Max(0, streak));

        public static float SpeedBonus(float impactSpeed) =>
            Math.Clamp((impactSpeed - HitSpeed) * SpeedBonusPerMs, 0f, MaxSpeedBonus);

        /// <summary>Points for a hit: base × streak multiplier × (1 + speed bonus), rounded.</summary>
        public static int Points(TargetClass c, int streak, float impactSpeed) =>
            (int)MathF.Round(BasePoints(c) * Multiplier(streak) * (1f + SpeedBonus(impactSpeed)));

        public static bool IsHit(float impactSpeed) => impactSpeed >= HitSpeed;

        /// <summary>Wind for the range: steady (m/s) and gust amplitude.</summary>
        public static (float steady, float gust) Wind(RangeDifficulty d) => d switch
        {
            RangeDifficulty.Easy => (0f, 0f),
            RangeDifficulty.Normal => (2f, 1f),
            _ => (4f, 4f),
        };

        /// <summary>
        /// The range's wind at <paramref name="time"/>: a steady wind from a direction picked by the seed, with gusts
        /// (two slow sines, so every PC computes the same wind from the seed and the shared clock).
        /// </summary>
        public static Vector3 WindAt(RangeDifficulty d, int seed, float time)
        {
            var (steady, gust) = Wind(d);
            if (steady <= 0f && gust <= 0f) return Vector3.zero;
            float heading = StrikeRandom.Value((uint)seed, 41u, 0u) * Mathf.PI * 2f;
            float g = gust * (0.6f * Mathf.Sin(time * 0.7f + seed % 7) + 0.4f * Mathf.Sin(time * 1.9f + seed % 3));
            float swing = 0.25f * Mathf.Sin(time * 0.23f);
            float speed = Mathf.Max(0f, steady + g);
            return new Vector3(Mathf.Sin(heading + swing), 0f, Mathf.Cos(heading + swing)) * speed;
        }

        /// <summary>How many targets of each class are out at once.</summary>
        public static (int statics, int moving, int evasive) Mix(RangeDifficulty d) => d switch
        {
            RangeDifficulty.Easy => (10, 0, 0),
            RangeDifficulty.Normal => (6, 5, 2),
            _ => (4, 5, 5),
        };
    }

    /// <summary>One pilot's round (player or bot): score, streak, hits per class, launches, crashes.</summary>
    [Serializable]
    public sealed class StrikeScore
    {
        public string Pilot = "";
        public int Score;
        /// <summary>Hits in a row since the last crash.</summary>
        public int Streak;
        public int BestStreak;
        public int Launches;
        public int Crashes;
        public int[] HitsByClass = new int[3];
        public int Hits => HitsByClass[0] + HitsByClass[1] + HitsByClass[2];
        /// <summary>Hits per launch (each launch ends in a hit, a crash or the end of the round).</summary>
        public float Accuracy => Launches > 0 ? (float)Hits / Launches : 0f;
        public float Multiplier => StrikeRules.Multiplier(Streak);

        public void Launch() => Launches++;

        /// <summary>Registers a hit and returns the points scored.</summary>
        public int Hit(TargetClass c, float impactSpeed)
        {
            int pts = StrikeRules.Points(c, Streak, impactSpeed);
            Score += pts;
            HitsByClass[(int)c]++;
            Streak++;
            if (Streak > BestStreak) BestStreak = Streak;
            return pts;
        }

        /// <summary>A crash (terrain, obstacle, or a bump below the hit speed): the streak resets.</summary>
        public void Crash()
        {
            Crashes++;
            Streak = 0;
        }

        public static int Compare(StrikeScore a, StrikeScore b)
        {
            int c = b.Score.CompareTo(a.Score);
            return c != 0 ? c : b.Hits.CompareTo(a.Hits);
        }
    }

    /// <summary>Small deterministic hash-based random numbers (identical on every PC and in every build).</summary>
    public static class StrikeRandom
    {
        public static uint Hash(uint a, uint b = 0, uint c = 0, uint d = 0)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = Mix(h, a);
                h = Mix(h, b);
                h = Mix(h, c);
                h = Mix(h, d);
                return h;
            }
        }

        private static uint Mix(uint h, uint v)
        {
            unchecked
            {
                h ^= v;
                h *= 16777619u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return h;
            }
        }

        /// <summary>A value in [0, 1) from the hash inputs.</summary>
        public static float Value(uint a, uint b = 0, uint c = 0, uint d = 0) => (Hash(a, b, c, d) & 0xFFFFFF) / 16777216f;

        public static float Range(float min, float max, uint a, uint b = 0, uint c = 0, uint d = 0) => min + (max - min) * Value(a, b, c, d);

        public static int Index(int count, uint a, uint b = 0, uint c = 0, uint d = 0) => count <= 0 ? 0 : (int)(Hash(a, b, c, d) % (uint)count);
    }
}
