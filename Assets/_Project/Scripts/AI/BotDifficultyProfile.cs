using UnityEngine;

namespace Ushqyr.AI
{
    public enum BotLevel
    {
        Rookie = 0,
        Amateur = 1,
        Pilot = 2,
        Pro = 3,
        Elite = 4,
        /// <summary>Follows the player's recent pace (±5 %).</summary>
        Adaptive = 5,
    }

    /// <summary>
    /// How well a bot flies (P4). Bots never get more grip or thrust than the player's quad: a level only changes how
    /// close to the baked racing line's speed they go, how cleanly they hold the line and how they react.
    /// Assets live in <c>Resources/Bots/</c>; <see cref="For"/> falls back to built-in values without them.
    /// </summary>
    [CreateAssetMenu(menuName = "Ushqyr/Bot Difficulty", fileName = "Bot_")]
    public sealed class BotDifficultyProfile : ScriptableObject
    {
        public BotLevel Level = BotLevel.Pilot;
        [Tooltip("Fraction of the racing line's speed profile (Elite 0.95).")]
        [Range(0.3f, 1f)] public float Pace = 0.8f;
        [Tooltip("Wander off the line, metres (kept inside the gate openings and the corridor).")]
        [Range(0f, 2f)] public float LineNoise = 0.3f;
        [Tooltip("Delay between seeing and moving the sticks (s).")]
        [Range(0f, 0.15f)] public float ReactionDelay = 0.03f;
        [Tooltip("Random stick wobble (fraction of full deflection).")]
        [Range(0f, 0.1f)] public float StickNoise = 0.01f;
        [Tooltip("Chance per gate of a mistake: a wide, slow exit.")]
        [Range(0f, 0.5f)] public float MistakeChance = 0.03f;
        [Tooltip("Seconds on the ground after a crash before re-launching from the last gate.")]
        [Range(0.5f, 6f)] public float RecoverySeconds = 2f;
        [Tooltip("0 = yields and brakes early, 1 = brakes late and overtakes boldly.")]
        [Range(0f, 1f)] public float Aggression = 0.5f;

        private static readonly BotDifficultyProfile[] Cache = new BotDifficultyProfile[6];

        /// <summary>The profile for a level: <c>Resources/Bots/Bot_{Level}</c>, or the built-in values.</summary>
        public static BotDifficultyProfile For(BotLevel level)
        {
            int i = (int)level;
            if (Cache[i] != null) return Cache[i];
            var asset = Resources.Load<BotDifficultyProfile>("Bots/Bot_" + level);
            return Cache[i] = asset != null ? asset : Default(level);
        }

        /// <summary>Built-in values per level (also what the editor writes into the assets).</summary>
        public static BotDifficultyProfile Default(BotLevel level)
        {
            var p = CreateInstance<BotDifficultyProfile>();
            p.name = "Bot_" + level;
            p.Level = level;
            switch (level)
            {
                case BotLevel.Rookie: p.Set(0.50f, 0.7f, 0.10f, 0.015f, 0.15f, 4f, 0.1f); break;
                case BotLevel.Amateur: p.Set(0.64f, 0.5f, 0.07f, 0.012f, 0.10f, 3f, 0.3f); break;
                case BotLevel.Pilot: p.Set(0.76f, 0.4f, 0.05f, 0.009f, 0.06f, 2.5f, 0.5f); break;
                case BotLevel.Pro: p.Set(0.86f, 0.25f, 0.03f, 0.006f, 0.03f, 2f, 0.7f); break;
                case BotLevel.Elite: p.Set(0.95f, 0.10f, 0.015f, 0.005f, 0f, 1.5f, 0.9f); break;
                // Adaptive starts as a Pilot; its pace is then steered towards the player's.
                default: p.Set(0.76f, 0.3f, 0.04f, 0.008f, 0.03f, 2f, 0.5f); break;
            }
            return p;
        }

        private void Set(float pace, float noise, float delay, float stick, float mistakes, float recovery, float aggression)
        {
            Pace = pace;
            LineNoise = noise;
            ReactionDelay = delay;
            StickNoise = stick;
            MistakeChance = mistakes;
            RecoverySeconds = recovery;
            Aggression = aggression;
        }
    }
}
