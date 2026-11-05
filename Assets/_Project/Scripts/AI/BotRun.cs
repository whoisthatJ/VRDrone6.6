using System;
using System.Collections.Generic;
using Ushqyr.Flight;
using Ushqyr.Race;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.AI
{
    /// <summary>
    /// One bot in a race (P4), engine-independent enough to run headless: its own <see cref="QuadState"/> stepped by the
    /// same <see cref="QuadSimulator"/> as the player at the same fixed step, the same <see cref="CollisionHook"/>
    /// against the track geometry, its own <see cref="LapTimer"/> over the course gates, crash recovery from the last
    /// gate, missed-gate rejoin, and the pilot model: planner → follower → reaction delay → stick noise, at 250 Hz.
    /// </summary>
    public sealed class BotRun
    {
        public const int ControlDivider = 4;

        private readonly IReadOnlyList<Gate> _gates;
        private readonly TrajectoryFollower _follower;
        private readonly PhysicsSettings _env;
        private CollisionHook _hook;
        private readonly System.Random _rng;
        private readonly PilotInput[] _delay;
        private int _delayHead;
        private float _accumulator;
        private int _tick;
        private PilotInput _input;
        private Vector3 _tickFrom;
        private bool _hasTickFrom;
        private double _recoverAt;
        private Vector3 _stickNoise;
        private Vector3 _padPosition;
        private Quaternion _padRotation;
        private float _targetLap;
        private double _lastLapCheck;
        private float _stuckTimer;
        private Vector3 _stuckAnchor;

        public QuadState State;

        public BotRun(BotLine line, IReadOnlyList<Gate> gates, in QuadProfile profile, BotDifficultyProfile level, int laps,
            in CollisionSettings collision, int seed, PhysicsSettings? env = null)
        {
            _gates = gates;
            Line = line;
            Profile = profile;
            Level = level;
            _env = env ?? PhysicsSettings.Default;
            _hook = new CollisionHook { Settings = collision };
            _rng = new System.Random(seed * 7919 + 13);
            Planner = new LinePlanner(line, level, seed);
            _follower = new TrajectoryFollower(profile, _env.Gravity);
            // Slower reactions: a softer position loop and turns / braking noticed late (LinePlanner.Reaction). The
            // sticks themselves are not delayed: the attitude loop runs through them, and even a 16 ms lag there
            // makes the quad wobble and flip, which is not how a slow pilot flies.
            float soft = 1f / (1f + 4f * level.ReactionDelay);
            _follower.Omega *= soft;
            Planner.Reaction = level.ReactionDelay;
            _delay = new PilotInput[1];
            Timer = new LapTimer(Mathf.Max(1, gates.Count), Mathf.Max(1, laps));
        }

        public BotLine Line { get; }
        public QuadProfile Profile { get; }
        public BotDifficultyProfile Level { get; }
        public LinePlanner Planner { get; }
        public LapTimer Timer { get; }
        /// <summary>Race clock (same base as the session's clock).</summary>
        public double Clock { get; private set; }
        public bool Racing { get; private set; }
        public bool Crashed { get; private set; }
        public int Crashes { get; private set; }
        /// <summary>Gates flown past instead of through (the bot then flew back).</summary>
        public int MissedGates { get; private set; }
        public int Rescues { get; private set; }
        public float TopSpeed { get; private set; }
        public float Distance { get; private set; }
        public PilotInput Input => _input;
        public int ImpactCount => _hook.ImpactCount;
        /// <summary>Gate pass events: gate index, result, time.</summary>
        public event Action<int, PassResult, double> GatePassed;

        public Vector3 Position => TrajectoryFollower.ToU(State.Position);
        public Vector3 Velocity => TrajectoryFollower.ToU(State.Velocity);
        public Quaternion Rotation => TrajectoryFollower.ToU(State.Orientation);

        /// <summary>Adaptive bots: steer the pace so laps take about this long (s). 0 = off.</summary>
        public void SetTargetLap(float seconds)
        {
            _targetLap = seconds;
            if (seconds > 0f && Line.LapTime > 0f) Planner.Pace = Mathf.Clamp(Line.LapTime * PaceToTime / seconds, 0.3f, 1f);
        }

        /// <summary>How much longer than the line's profile time a flown lap takes at pace 1 (follower lag, launches); learnt per lap.</summary>
        public float PaceToTime { get; private set; } = 1.08f;

        public void Place(Vector3 position, Quaternion rotation)
        {
            _padPosition = position;
            _padRotation = rotation;
            State = QuadState.AtRest(TrajectoryFollower.ToF(position), TrajectoryFollower.ToF(rotation));
            _hook.PrevPosition = State.Position;
            _hook.PrevOrientation = State.Orientation;
            _hasTickFrom = false;
            Planner.Relocate();
            _follower.Reset();
        }

        /// <summary>The start signal: the timer runs from <paramref name="time"/> and the bot launches.</summary>
        public void Start(double time)
        {
            Clock = time;
            _lastLapCheck = time;
            Timer.Start(time);
            Racing = true;
            Crashed = false;
            _stuckAnchor = Position;
        }

        public void Stop() => Racing = false;

        /// <summary>A midair hit: a velocity change, or a crash when it was hard.</summary>
        public void Bump(Vector3 dv, bool crash)
        {
            if (!Racing || Crashed) return;
            if (crash)
            {
                Crash();
                return;
            }
            State.Velocity += TrajectoryFollower.ToF(dv);
        }

        /// <summary>Advances the bot by one rendered frame (fixed sub-steps; leftover time carries over).</summary>
        public void Advance(float frameDt)
        {
            if (!Racing) return;
            float h = _env.FixedStep;
            _accumulator += frameDt;
            int steps = 0;
            int maxSteps = Mathf.Max(_env.MaxSubSteps, Mathf.CeilToInt(0.05f / h));
            while (_accumulator >= h && steps < maxSteps)
            {
                _accumulator -= h;
                steps++;
                Clock += h;
                if (Crashed)
                {
                    if (Clock >= _recoverAt) Recover();
                    continue;
                }
                if (_tick++ % ControlDivider == 0) ControlTick(h * ControlDivider);

                QuadState prev = State;
                QuadSimulator.Step(ref State, _input, Profile, _env, h);
                if (!_hook.AfterStep(ref State, prev, h) || _hook.Crashed)
                {
                    Crash();
                    continue;
                }
                float sp = State.Velocity.Magnitude;
                if (sp > TopSpeed) TopSpeed = sp;
                Distance += sp * h;
                // A stray fall far below the course: treat as a crash.
                if (State.Position.y < -50f) Crash();
            }
            if (steps == maxSteps && _accumulator > h) _accumulator = 0f;
        }

        private bool _launched;

        private void ControlTick(float dt)
        {
            _launched = true;
            Vector3 pos = Position, vel = Velocity;
            if (_hasTickFrom && !Timer.Finished) CheckGates(_tickFrom, pos);
            _tickFrom = pos;
            _hasTickFrom = true;

            if (Timer.Finished)
            {
                // Cool-down after the finish: ease off and hover on the line's direction.
                Planner.SpeedCap = Mathf.Max(0f, Planner.SpeedCap - 8f * dt);
            }
            Watchdog(pos, dt);

            var r = Planner.Plan(pos, vel, dt);
            PilotInput cmd = _follower.Compute(State, r, dt);

            // Stick noise: smooth wobble.
            float sn = Level.StickNoise;
            if (sn > 0f)
            {
                float a = Mathf.Exp(-dt / 0.12f);
                _stickNoise = _stickNoise * a + new Vector3(Rand(), Rand(), Rand()) * (sn * Mathf.Sqrt(1f - a * a) * 1.7f);
                cmd.Pitch = Mathf.Clamp(cmd.Pitch + _stickNoise.x, -1f, 1f);
                cmd.Roll = Mathf.Clamp(cmd.Roll + _stickNoise.y, -1f, 1f);
                cmd.Yaw = Mathf.Clamp(cmd.Yaw + _stickNoise.z * 0.5f, -1f, 1f);
            }

            // Reaction delay: the sticks move a few ticks after the decision.
            if (_delay.Length > 1)
            {
                _delay[_delayHead] = cmd;
                _delayHead = (_delayHead + 1) % _delay.Length;
                PilotInput delayed = _delay[_delayHead];
                if (!delayed.Arm) delayed = cmd; // buffer not filled yet
                _input = delayed;
            }
            else _input = cmd;
        }

        private void CheckGates(Vector3 from, Vector3 to)
        {
            int next = Timer.NextCheckpoint;
            var gate = _gates[next];
            if (gate == null) return;
            if (gate.TryCross(from, to, out float frac))
            {
                double t = Clock - (1.0 - frac) * ControlDivider * _env.FixedStep;
                var result = Timer.Pass(next, t);
                if (result == PassResult.LapCompleted || result == PassResult.RaceFinished) OnLap(result);
                Planner.OnGatePassed();
                _stuckTimer = 0f;
                GatePassed?.Invoke(next, result, t);
                return;
            }
            // Flown past the gate (outside the opening, or the line's gate point left behind): go back for it.
            if (Planner.Rejoining || Line.GateSamples.Length <= next) return;
            float gateD = Line.DistanceOf(Line.GateSamples[next]);
            float ahead = Line.Ahead(gateD, Planner.Progress);
            bool beside = gate.PassedBeside(from, to, RaceSession.MissRadius);
            if (beside || (Planner.Progress >= 0f && ahead > 4f && ahead < 40f && Planner.OffLine < 10f))
            {
                MissedGates++;
                Planner.Rejoin(gateD - 12f);
            }
        }

        private void OnLap(PassResult result)
        {
            int done = Timer.CompletedLaps;
            if (done <= 0) return;
            float lap = (float)Timer.LapTime(done - 1);
            // Learn how flown laps compare with the profile (lap 1 includes the launch: skip it).
            if (done >= 2 && Planner.Pace > 0f)
            {
                float ratio = lap * Planner.Pace / Mathf.Max(1f, Line.LapTime);
                PaceToTime = Mathf.Lerp(PaceToTime, Mathf.Clamp(ratio, 0.9f, 1.6f), 0.6f);
            }
            if (_targetLap > 0f && result == PassResult.LapCompleted)
            {
                // Adaptive: correct the pace towards the target lap time.
                float want = Line.LapTime * PaceToTime / _targetLap;
                float err = lap / _targetLap;
                Planner.Pace = Mathf.Clamp(Mathf.Lerp(want, Planner.Pace * Mathf.Pow(err, 0.9f), 0.5f), 0.3f, 1f);
            }
        }

        private void Watchdog(Vector3 pos, float dt)
        {
            // Not getting anywhere for 6 s (wedged against something): respawn at the last gate.
            if ((pos - _stuckAnchor).sqrMagnitude > 1.5f * 1.5f)
            {
                _stuckAnchor = pos;
                _stuckTimer = 0f;
                return;
            }
            _stuckTimer += dt;
            if (_stuckTimer < 6f || Timer.Finished) return;
            _stuckTimer = 0f;
            Rescues++;
            Respawn();
        }

        private void Crash()
        {
            Crashed = true;
            Crashes++;
            _recoverAt = Clock + Level.RecoverySeconds;
            State.Velocity = Vec3.Zero;
            State.AngularVelocity = Vec3.Zero;
            State.Armed = false;
        }

        private void Recover()
        {
            Crashed = false;
            Respawn();
        }

        private void Respawn()
        {
            Vector3 p;
            Quaternion r;
            bool onPad = Timer.CompletedLaps == 0 && Timer.NextCheckpoint == 0 && Timer.LastCheckpoint == 0 && !_launched;
            if (onPad)
            {
                p = _padPosition;
                r = _padRotation;
            }
            else
            {
                // On the line, hovering: a run-up before the next gate, behind the last gate passed when the two are
                // close (slaloms), so the bot never restarts right in front of a frame.
                int next = Timer.NextCheckpoint;
                float gateD = next < Line.GateSamples.Length ? Line.DistanceOf(Line.GateSamples[next]) : 0f;
                int last = Timer.LastCheckpoint;
                float lastD = last < Line.GateSamples.Length ? Line.DistanceOf(Line.GateSamples[last]) : gateD;
                float gap = Line.Ahead(lastD, gateD);
                float d = gateD - 10f;
                if (gap < 20f)
                {
                    // Close gates (a slalom): restart before the whole cluster, so the run-up is clear of frames.
                    int k = last;
                    float kd = lastD;
                    for (int guard = 0; guard < _gates.Count; guard++)
                    {
                        int prev = (k - 1 + _gates.Count) % _gates.Count;
                        if (prev >= Line.GateSamples.Length) break;
                        float pd = Line.DistanceOf(Line.GateSamples[prev]);
                        if (Line.Ahead(pd, kd) >= 20f) break;
                        k = prev;
                        kd = pd;
                    }
                    d = kd - 10f;
                }
                Line.Sample(d, out p, out Vector3 t, out _, out _, out _, out _);
                Vector3 flat = new Vector3(t.x, 0f, t.z);
                r = Quaternion.LookRotation(flat.sqrMagnitude > 1e-3f ? flat : Vector3.forward, Vector3.up);
            }
            State = QuadState.AtRest(TrajectoryFollower.ToF(p), TrajectoryFollower.ToF(r));
            if (!onPad)
            {
                float hover = MotorModel.HoverSpeed(Profile.Motors, Profile.Mass * _env.Gravity);
                for (int i = 0; i < 4; i++) State.MotorSpeed[i] = hover;
                State.Armed = true;
            }
            _hook.Crashed = false;
            _hook.PrevPosition = State.Position;
            _hook.PrevOrientation = State.Orientation;
            _hasTickFrom = false;
            _stuckAnchor = p;
            Planner.Relocate();
            _follower.Reset();
            for (int i = 0; i < _delay.Length; i++) _delay[i] = default;
        }

        private float Rand() => (float)(_rng.NextDouble() * 2.0 - 1.0);
    }
}
