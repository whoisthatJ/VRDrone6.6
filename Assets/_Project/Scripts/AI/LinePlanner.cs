using System;
using UnityEngine;

namespace Ushqyr.AI
{
    /// <summary>
    /// Follows a baked <see cref="BotLine"/> (P4) the way a pilot of a given level would: at a fraction of the line's speed
    /// (<see cref="Pace"/>), wandering off it a little (smooth noise kept inside the corridor and the openings), now and
    /// then a mistake (a wide, slow exit after a gate), stepping aside or easing off for a quad just ahead, and flying
    /// back to the line before a gate it missed.
    /// </summary>
    public sealed class LinePlanner : IBotPlanner
    {
        public const float NoiseTime = 1.6f;
        public const float MistakeSeconds = 1.8f;
        /// <summary>Feed-forward is read this far ahead (s) to cover the attitude lag.</summary>
        public const float Lead = 0.1f;
        /// <summary>The reference speed is at most this much above the speed flown now (m/s).</summary>
        public const float Ramp = 6f;

        private readonly BotLine _line;
        private readonly BotDifficultyProfile _level;
        private readonly System.Random _rng;
        private float _s = -1f;
        private Vector2 _noise;
        private float _mistakeLeft;
        private bool _rejoin;
        private float _rejoinS;

        public LinePlanner(BotLine line, BotDifficultyProfile level, int seed)
        {
            _line = line;
            _level = level;
            _rng = new System.Random(seed);
            Pace = level.Pace;
        }

        public BotLine Line => _line;
        public BotDifficultyProfile Level => _level;
        /// <summary>Fraction of the line's speed profile flown now (Adaptive bots change it).</summary>
        public float Pace { get; set; }
        /// <summary>Where the bot is along the line (m), or -1 before the first plan.</summary>
        public float Progress => _s;
        /// <summary>How far the bot is from the line (m).</summary>
        public float OffLine { get; private set; }
        /// <summary>Flying back to the line before a missed gate.</summary>
        public bool Rejoining => _rejoin;
        /// <summary>Sideways nudge from the avoidance (world, m); limited to the room left.</summary>
        public Vector3 Avoid { get; set; }
        /// <summary>Speed cap from the avoidance (a slower quad just ahead), m/s.</summary>
        public float SpeedCap { get; set; } = float.MaxValue;
        public int Mistakes { get; private set; }
        /// <summary>Reaction time (s): turns and braking ahead are noticed this late.</summary>
        public float Reaction { get; set; }

        /// <summary>Forget where the bot was (after a respawn): the next plan searches the whole line.</summary>
        public void Relocate()
        {
            _s = -1f;
            _rejoin = false;
            _noise = Vector2.zero;
        }

        /// <summary>Called as a gate is passed: maybe a mistake on the way out.</summary>
        public void OnGatePassed()
        {
            if (_level.MistakeChance > 0f && _rng.NextDouble() < _level.MistakeChance)
            {
                _mistakeLeft = MistakeSeconds;
                Mistakes++;
            }
        }

        /// <summary>Fly back to the line at <paramref name="distance"/> (before a missed gate) and go on from there.</summary>
        public void Rejoin(float distance)
        {
            _rejoin = true;
            _rejoinS = _line.Wrap(distance);
        }

        public BotReference Plan(Vector3 position, Vector3 velocity, float dt)
        {
            if (_rejoin)
            {
                // Back along the line itself (it is clear of everything), slowly, to the rejoin point; then forward again.
                float cur = _line.Closest(position, _s, out float offBack, 12f, 12f);
                _s = cur;
                OffLine = offBack;
                float behind = _line.Ahead(_rejoinS, cur);
                if (behind < 1.5f || behind > _line.Length - 5f)
                {
                    if (velocity.sqrMagnitude < 3f * 3f) _rejoin = false;
                    _line.Sample(_rejoinS, out Vector3 rp, out Vector3 rt, out _, out _, out _, out _);
                    return new BotReference { Position = rp, Velocity = Vector3.zero, Accel = Vector3.zero, Heading = rt };
                }
                float target = _line.Wrap(cur - Mathf.Min(2f, behind));
                _line.Sample(target, out Vector3 bp, out Vector3 bt, out _, out _, out _, out _);
                float back = Mathf.Min(4f, 1f + behind * 0.5f);
                return new BotReference { Position = bp, Velocity = -bt * back, Accel = Vector3.zero, Heading = bt };
            }

            float s = _line.Closest(position, _s, out float off);
            if (_s >= 0f && off > 10f) s = _line.Closest(position, -1f, out off);
            _s = s;
            OffLine = off;

            _line.Sample(s, out Vector3 p, out Vector3 t, out _, out float speed, out _, out float slack);
            float vNow = Mathf.Max(2f, Vector3.Dot(velocity, t));
            // Turn-ins and braking are noticed late by the reaction time.
            _line.Sample(s + vNow * (Lead - Reaction), out _, out Vector3 tLead, out Vector3 kLead, out float speedLead, out float accelLead, out _);

            // Smooth wander (Ornstein-Uhlenbeck), wider during a mistake, inside the room left.
            float mistake = _mistakeLeft > 0f ? 1f : 0f;
            _mistakeLeft = Mathf.Max(0f, _mistakeLeft - dt);
            float sigma = _level.LineNoise * (1f + mistake);
            if (sigma > 0f)
            {
                float k = Mathf.Sqrt(2f * dt / NoiseTime) * sigma;
                _noise += -_noise * (dt / NoiseTime) + new Vector2(Gauss(), Gauss()) * k;
            }
            else _noise = Vector2.zero;
            Vector3 side = Vector3.Cross(Mathf.Abs(t.y) < 0.95f ? Vector3.up : Vector3.forward, t).normalized;
            Vector3 upish = Vector3.Cross(t, side);
            // Pilots wander sideways more than up and down.
            Vector3 offset = side * _noise.x + upish * (_noise.y * 0.4f) + Avoid;
            float room = Mathf.Max(0f, slack * 0.8f);
            if (offset.sqrMagnitude > room * room) offset = offset.normalized * room;

            float pace = Pace * (1f - 0.25f * mistake);
            // Build speed up gradually (launch, after a respawn), and ease off while far from the line.
            float along = Mathf.Max(0f, Vector3.Dot(velocity, t));
            float cap = Mathf.Min(SpeedCap, along + Ramp);
            cap *= Mathf.Clamp(1f - (off - 1f) / 6f, 0.35f, 1f);
            float vs = Mathf.Min(pace * speed, cap);
            float vsLead = Mathf.Min(pace * speedLead, cap);
            Vector3 accel = kLead * (vsLead * vsLead) + tLead * (pace * pace * accelLead);
            _line.Sample(s + 3f + vNow * 0.15f, out _, out Vector3 tHead, out _, out _, out _, out _);

            return new BotReference { Position = p + offset, Velocity = t * vs, Accel = accel, Heading = tHead };
        }

        private float Gauss()
        {
            // Box-Muller.
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        internal float NextNoise() => (float)(_rng.NextDouble() * 2.0 - 1.0);
    }
}
