using Ushqyr.AI;
using Ushqyr.Flight;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Strike
{
    /// <summary>
    /// A bot on the Strike Range (P5). Same flight model as the player (QuadSimulator, CollisionHook, the P4 trajectory
    /// follower and inverse rates); an <b>intercept planner</b> picks a target by value ÷ estimated time to reach it,
    /// climbs to a safe height over the terrain, predicts where a moving target will be and dives onto it. Levels change
    /// the cruise speed, the aim, whether it leads moving targets, and how well it chooses.
    /// </summary>
    public sealed class StrikeBot
    {
        public const int ControlDivider = 4;
        /// <summary>Height above the terrain while transiting (m).</summary>
        public const float CruiseHeight = 22f;
        /// <summary>Start the dive this far (horizontally) from the target (m).</summary>
        public const float DiveRange = 55f;

        private readonly RangeDirector _director;
        private readonly TrajectoryFollower _follower;
        private readonly PhysicsSettings _env;
        private readonly System.Random _rng;
        private CollisionHook _hook;
        private PilotInput _input;
        private float _accumulator;
        private int _tick;
        private float _relaunchAt = -1f;
        private int _target = -1;
        private Vector3 _aimNoise;
        private float _replanAt;
        private enum Phase { OnPad, Transit, Dive, Down }
        private Phase _phase = Phase.OnPad;

        public QuadState State;
        public readonly StrikeScore Score;
        public BotDifficultyProfile Level { get; }
        public QuadProfile Profile { get; }
        public int Index { get; }
        public int Pad { get; private set; }
        public int Target => _target;
        public bool Down => _phase == Phase.Down;

        /// <summary>Raised on a hit (slot, points) and on a crash.</summary>
        public System.Action<int, int> Hit;
        public System.Action Crashed;

        public StrikeBot(int index, string name, RangeDirector director, in QuadProfile profile, BotDifficultyProfile level, in CollisionSettings collision, int seed)
        {
            Index = index;
            _director = director;
            Profile = profile;
            Level = level;
            _env = PhysicsSettings.Default;
            _hook = new CollisionHook { Settings = collision };
            _follower = new TrajectoryFollower(profile, _env.Gravity);
            _rng = new System.Random(seed);
            Score = new StrikeScore { Pilot = name };
        }

        public Vector3 Position => TrajectoryFollower.ToU(State.Position);
        public Vector3 Velocity => TrajectoryFollower.ToU(State.Velocity);
        public Quaternion Rotation => TrajectoryFollower.ToU(State.Orientation);

        /// <summary>Cruise speed for the level (m/s).</summary>
        public float Cruise => Mathf.Lerp(9f, 24f, (Level.Pace - 0.5f) / 0.45f);

        /// <summary>Puts the bot on a pad, at rest, motors off, fresh battery.</summary>
        public void Launch(int pad, float time)
        {
            var p = _director.Layout.Pads[pad];
            Pad = pad;
            State = QuadState.AtRest(TrajectoryFollower.ToF(p.Position + Vector3.up * 0.15f), TrajectoryFollower.ToF(Quaternion.Euler(0f, p.Yaw, 0f)));
            _hook.Crashed = false;
            _hook.PrevPosition = State.Position;
            _hook.PrevOrientation = State.Orientation;
            _follower.Reset();
            _phase = Phase.Transit;
            _target = -1;
            _replanAt = time;
            Score.Launch();
        }

        /// <summary>Advances one rendered frame. <paramref name="nextPad"/> picks a free pad for the relaunch.</summary>
        public void Advance(float frameDt, float time, System.Func<int, int> nextPad)
        {
            if (_phase == Phase.Down)
            {
                if (time >= _relaunchAt) Launch(nextPad(Index), time);
                return;
            }
            float h = _env.FixedStep;
            _accumulator += frameDt;
            int steps = 0;
            while (_accumulator >= h && steps < 64)
            {
                _accumulator -= h;
                steps++;
                float t = time - _accumulator;
                if (_tick++ % ControlDivider == 0) Control(h * ControlDivider, t);
                QuadState prev = State;
                QuadSimulator.Step(ref State, _input, Profile, _env, h);
                if (!_hook.AfterStep(ref State, prev, h) || _hook.Crashed)
                {
                    Contact(TrajectoryFollower.ToU(prev.Position), TrajectoryFollower.ToU(prev.Velocity).magnitude, t);
                    return;
                }
                if (_tick % ControlDivider == 0 && TouchingTarget(Position, out int slot))
                {
                    Contact(Position, Velocity.magnitude, t, slot);
                    return;
                }
            }
        }

        private bool TouchingTarget(Vector3 p, out int slot)
        {
            slot = -1;
            var hits = Physics.OverlapSphere(p, _hook.Settings.Radius + 0.15f, _hook.Settings.TrackGeometry, QueryTriggerInteraction.Ignore);
            foreach (var c in hits)
            {
                var hb = c.GetComponent<StrikeHitbox>();
                if (hb != null && _director.Targets[hb.Slot].Active)
                {
                    slot = hb.Slot;
                    return true;
                }
            }
            return false;
        }

        // A contact: a hit when it is a target and fast enough; anything else is a crash.
        private void Contact(Vector3 at, float speed, float time, int slot = -1)
        {
            if (slot < 0) TouchingTarget(at, out slot);
            if (slot >= 0 && StrikeRules.IsHit(speed) && _director.Hit(slot, time, Index))
            {
                int pts = Score.Hit(_director.Targets[slot].Plan.Class, speed);
                Hit?.Invoke(slot, pts);
            }
            else
            {
                Score.Crash();
                Crashed?.Invoke();
            }
            _phase = Phase.Down;
            _relaunchAt = time + StrikeRules.RelaunchSeconds;
            State.Velocity = Vec3.Zero;
        }

        private void Control(float dt, float time)
        {
            Vector3 p = Position, v = Velocity;
            if (time >= _replanAt || _target < 0 || !_director.Targets[_target].Active)
            {
                Choose(p, time);
                // Slower levels re-plan less often.
                _replanAt = time + Mathf.Lerp(0.5f, 2.5f, Level.ReactionDelay / 0.1f);
            }
            if (_target < 0)
            {
                _input = _follower.Compute(State, Hover(p), dt);
                return;
            }
            Vector3 aim = Predict(p, time) + _aimNoise;
            var layout = _director.Layout;
            Vector3 flat = aim - p;
            flat.y = 0f;
            float dist = flat.magnitude;
            BotReference r;
            if (_phase != Phase.Dive && dist > DiveRange)
            {
                // Transit at a safe height over the terrain (and over what is ahead), towards the target.
                Vector3 dir = flat / Mathf.Max(dist, 1e-3f);
                Vector3 ahead = p + dir * 30f;
                float ground = Mathf.Max(layout.Height(p.x, p.z), layout.Height(ahead.x, ahead.z));
                float alt = ground + CruiseHeight;
                var carrot = new Vector3(p.x, alt, p.z) + dir * 6f;
                r = new BotReference { Position = carrot, Velocity = dir * Cruise, Accel = Vector3.zero, Heading = dir };
            }
            else
            {
                _phase = Phase.Dive;
                // The dive: straight at the (predicted) target, fast enough to count.
                Vector3 to = aim - p;
                Vector3 dir = to.normalized;
                float speed = Mathf.Max(StrikeRules.HitSpeed + 5f, Cruise);
                r = new BotReference { Position = p + dir * 4f, Velocity = dir * speed, Accel = Vector3.zero, Heading = new Vector3(dir.x, 0f, dir.z) };
            }
            _input = _follower.Compute(State, r, dt);
        }

        private BotReference Hover(Vector3 p)
        {
            float alt = _director.Layout.Height(p.x, p.z) + CruiseHeight;
            return new BotReference { Position = new Vector3(p.x, alt, p.z), Velocity = Vector3.zero, Accel = Vector3.zero, Heading = Rotation * Vector3.forward };
        }

        /// <summary>
        /// Target choice: value ÷ estimated time to reach (good pilots), or simply the nearest (weak ones), with a level
        /// dependent chance of a poor pick. Targets another pilot is already diving on are avoided by the better levels.
        /// </summary>
        private void Choose(Vector3 p, float time)
        {
            if (_phase == Phase.Dive && _target >= 0 && _director.Targets[_target].Active) return;
            float smart = Mathf.Clamp01((Level.Pace - 0.5f) / 0.45f);
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < _director.Targets.Count; i++)
            {
                var t = _director.Targets[i];
                if (!t.Active) continue;
                _director.Pose(i, time, out var tp, out _, out _);
                float eta = Vector3.Distance(p, tp) / Cruise + 2f;
                float value = StrikeRules.BasePoints(t.Plan.Class);
                // Weak pilots don't like moving targets (they miss them) and weigh distance only.
                float difficulty = t.Plan.Class == TargetClass.Static ? 1f : Mathf.Lerp(0.3f, 1f, smart);
                float score = Mathf.Lerp(-eta, value * difficulty / eta, smart);
                score += (float)(_rng.NextDouble() - 0.5) * (1f - smart) * 20f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best != _target)
            {
                _target = best;
                float noise = Level.LineNoise * 1.6f;
                _aimNoise = new Vector3((float)(_rng.NextDouble() * 2 - 1), (float)(_rng.NextDouble() * 0.5), (float)(_rng.NextDouble() * 2 - 1)) * noise;
            }
        }

        /// <summary>Where to aim: the target's centre, led by the time to get there (good pilots lead; weak ones don't).</summary>
        private Vector3 Predict(Vector3 p, float time)
        {
            float smart = Mathf.Clamp01((Level.Pace - 0.5f) / 0.45f);
            _director.Pose(_target, time, out var tp, out _, out _);
            Vector3 centre = tp + Vector3.up * 1.3f;
            float speed = Mathf.Max(StrikeRules.HitSpeed + 5f, Cruise);
            float lead = smart > 0.3f ? Vector3.Distance(p, centre) / speed : 0f;
            if (lead <= 0f) return centre;
            _director.Pose(_target, time + lead, out var future, out _, out _);
            return future + Vector3.up * 1.3f;
        }
    }
}
