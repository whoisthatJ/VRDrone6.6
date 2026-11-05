using System;
using System.Collections.Generic;
using Ushqyr.Flight;
using Ushqyr.Garage;
using Ushqyr.Race;
using Ushqyr.Track;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.AI
{
    /// <summary>Race-mode options (P4), from the settings.</summary>
    [Serializable]
    public struct BotRaceOptions
    {
        public int Count;
        public BotLevel Level;
        /// <summary>Midair collisions with bots; off = ghost bots (fly through each other and the player).</summary>
        public bool Collisions;
        /// <summary>Adaptive bots: the player's recent lap time (s); 0 = unknown (they start as Pilots).</summary>
        public float PlayerPace;
        public int Seed;
    }

    /// <summary>
    /// Race mode with bots (P4): 1-7 bots on the start grid next to the player, launched by the same countdown, timed
    /// through the same gates. Live positions and gaps from the checkpoint pass times, simple avoidance (step aside /
    /// ease off behind a quad just ahead), optional midair collisions, names, colours and builds, and a recording of
    /// every racer for the winner replay.
    /// </summary>
    public sealed class BotRace : MonoBehaviour
    {
        public const float QuadRadius = 0.17f;
        public const float BumpCrashSpeed = 6f;
        public const float RecordInterval = 0.05f;

        public sealed class Racer
        {
            public string Name;
            public Color Tint;
            public bool IsPlayer;
            public BotRun Bot;
            public Transform Visual;
            public readonly List<double> Passes = new List<double>();
            public readonly List<Vector3> TrackPositions = new List<Vector3>();
            public readonly List<Quaternion> TrackRotations = new List<Quaternion>();
            public int Position;
            /// <summary>Seconds behind the leader at the last checkpoint both passed (0 for the leader).</summary>
            public double Gap;
            public bool Finished;
            public double TotalTime;
            public double BestLap;
            public float LineHint = -1f;
        }

        private readonly List<Racer> _racers = new List<Racer>();
        private RaceSession _session;
        private QuadVehicle _player;
        private BotLine _line;
        private LoadedTrack _track;
        private BotRaceOptions _options;
        private float _recordTimer;
        private bool _started;

        public IReadOnlyList<Racer> Racers => _racers;
        public BotLine Line => _line;
        public bool Active => _racers.Count > 1;
        /// <summary>Racers sorted by position.</summary>
        public readonly List<Racer> Standings = new List<Racer>();
        public event Action StandingsChanged;

        /// <summary>
        /// Sets up the bots for a loaded course. <paramref name="line"/> comes from <see cref="RacingLineAsset.Get"/>.
        /// </summary>
        public void Configure(RaceSession session, QuadVehicle player, LoadedTrack track, BotLine line, BotRaceOptions options,
            SizeClass size, QuadModelMaterials materials, string playerName, int quadLayer)
        {
            Clear();
            _session = session;
            _player = player;
            _track = track;
            _line = line;
            _options = options;
            if (options.Count <= 0 || line == null || !line.IsValid || track == null) return;

            _racers.Add(new Racer { Name = playerName, Tint = Color.white, IsPlayer = true });
            var collision = player != null ? player.BaseCollision : CollisionSettings.Default;
            var level = BotDifficultyProfile.For(options.Level);
            int laps = session != null && session.Timer != null ? session.Timer.TotalLaps : track.Data.Laps;
            for (int i = 0; i < Mathf.Clamp(options.Count, 0, 7); i++)
            {
                var build = BotRoster.BuildFor(size, i);
                var profile = BuildComposer.Compose(build);
                var bot = new BotRun(line, track.Gates, profile, level, laps, collision, options.Seed * 31 + i + 1);
                if (options.Level == BotLevel.Adaptive && options.PlayerPace > 0f)
                    bot.SetTargetLap(options.PlayerPace * (0.97f + 0.06f * i / Mathf.Max(1, options.Count - 1)));
                var root = new GameObject("Bot " + BotRoster.NameFor(i, options.Seed)).transform;
                root.SetParent(transform, false);
                var model = QuadModelBuilder.Build(build, materials, root, quadLayer);
                var racer = new Racer
                {
                    Name = BotRoster.NameFor(i, options.Seed), Tint = BotRoster.Tints[i % BotRoster.Tints.Length], Bot = bot, Visual = root,
                };
                int index = _racers.Count;
                bot.GatePassed += (g, r, t) => OnPass(_racers[index], r, t);
                _racers.Add(racer);
            }
            if (session != null)
            {
                session.PhaseChanged += OnPhase;
                session.GatePassed += OnPlayerGate;
            }
            PlaceOnGrid();
        }

        public void Clear()
        {
            if (_session != null)
            {
                _session.PhaseChanged -= OnPhase;
                _session.GatePassed -= OnPlayerGate;
            }
            foreach (var r in _racers)
                if (r.Visual != null) Destroy(r.Visual.gameObject);
            _racers.Clear();
            Standings.Clear();
            _started = false;
        }

        private void OnDestroy() => Clear();

        // ---------- grid and start ----------

        /// <summary>Grid slot <paramref name="slot"/> (1-based, the player is slot 0): beside and behind the pad, on the floor.</summary>
        public static Vector3 GridSlot(Transform spawn, int slot)
        {
            int row = (slot + 1) / 4, col = (slot + 1) % 4;
            float x = (col - 1) * 1.5f;
            var p = spawn.position + spawn.rotation * new Vector3(x, 0f, -2.5f * row);
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y + 0.12f;
            return p;
        }

        private void PlaceOnGrid()
        {
            var spawn = _track.Spawn;
            for (int i = 1; i < _racers.Count; i++)
            {
                var r = _racers[i];
                r.Bot.Stop();
                r.Bot.Place(GridSlot(spawn, i), spawn.rotation);
                r.Passes.Clear();
                r.TrackPositions.Clear();
                r.TrackRotations.Clear();
                r.Finished = false;
                r.LineHint = -1f;
                Pose(r);
            }
            var p = _racers.Count > 0 ? _racers[0] : null;
            if (p != null)
            {
                p.Passes.Clear();
                p.TrackPositions.Clear();
                p.TrackRotations.Clear();
                p.Finished = false;
            }
            _started = false;
            UpdateStandings();
        }

        private void OnPhase(RacePhase phase)
        {
            if (phase == RacePhase.Racing && !_started)
            {
                _started = true;
                double start = _session.Timer.StartTime;
                double lag = _session.Now - start;
                foreach (var r in _racers)
                    if (!r.IsPlayer)
                    {
                        r.Bot.Start(start);
                        // Catch up the time between the start signal and this frame.
                        if (lag > 0) r.Bot.Advance((float)lag);
                    }
            }
            else if (phase == RacePhase.Idle) PlaceOnGrid();
        }

        private void OnPlayerGate(int gate, PassResult result)
        {
            if (_racers.Count == 0) return;
            OnPass(_racers[0], result, _session.Now);
        }

        private void OnPass(Racer r, PassResult result, double time)
        {
            if (result == PassResult.Ignored || result == PassResult.WrongGate) return;
            r.Passes.Add(time);
            if (result == PassResult.RaceFinished)
            {
                r.Finished = true;
                var timer = r.IsPlayer ? _session.Timer : r.Bot.Timer;
                r.TotalTime = timer.TotalTime(time);
                r.BestLap = timer.HasBestLap ? timer.BestLap : 0;
            }
            UpdateStandings();
        }

        // ---------- per frame ----------

        private void Update()
        {
            if (_racers.Count < 2 || _session == null) return;
            float dt = Time.deltaTime;
            Avoidance();
            foreach (var r in _racers)
            {
                if (r.IsPlayer) continue;
                r.Bot.Advance(dt);
                Pose(r);
            }
            if (_options.Collisions) Collide();
            Record(dt);
        }

        private void Pose(Racer r)
        {
            if (r.Visual != null) r.Visual.SetPositionAndRotation(r.Bot.Position, r.Bot.Rotation);
        }

        private Vector3 PositionOf(Racer r) => r.IsPlayer ? (_player != null ? _player.transform.position : Vector3.zero) : r.Bot.Position;
        private Vector3 VelocityOf(Racer r) => r.IsPlayer ? (_player != null ? TrajectoryFollower.ToU(_player.State.Velocity) : Vector3.zero) : r.Bot.Velocity;

        // Step aside (within the room left) from a quad just ahead, and don't ram it.
        private void Avoidance()
        {
            foreach (var me in _racers)
            {
                if (me.IsPlayer) continue;
                var planner = me.Bot.Planner;
                Vector3 avoid = Vector3.zero;
                float cap = float.MaxValue;
                Vector3 p = me.Bot.Position, v = me.Bot.Velocity;
                float speed = v.magnitude;
                if (speed > 1f)
                {
                    Vector3 fwd = v / speed;
                    foreach (var other in _racers)
                    {
                        if (other == me) continue;
                        Vector3 d = PositionOf(other) - p;
                        float ahead = Vector3.Dot(d, fwd);
                        if (ahead < 0f || ahead > 5f) continue;
                        Vector3 side = d - fwd * ahead;
                        float lat = side.magnitude;
                        if (lat > 1.2f) continue;
                        Vector3 away = lat > 1e-3f ? -side / lat : Vector3.Cross(Vector3.up, fwd);
                        avoid += away * (1.2f - lat);
                        float otherSpeed = Vector3.Dot(VelocityOf(other), fwd);
                        // Bold bots go for the gap; careful ones sit behind.
                        if (ahead < 2.5f && me.Bot.Level.Aggression < 0.6f) cap = Mathf.Min(cap, Mathf.Max(2f, otherSpeed));
                    }
                }
                planner.Avoid = avoid;
                planner.SpeedCap = me.Bot.Timer.Finished ? planner.SpeedCap : cap;
            }
        }

        private void Collide()
        {
            for (int i = 0; i < _racers.Count; i++)
                for (int j = i + 1; j < _racers.Count; j++)
                {
                    var a = _racers[i];
                    var b = _racers[j];
                    if ((!a.IsPlayer && a.Bot.Crashed) || (!b.IsPlayer && b.Bot.Crashed)) continue;
                    if (a.IsPlayer && _player != null && (_player.IsCrashed || _player.HoldAtSpawn)) continue;
                    Vector3 d = PositionOf(b) - PositionOf(a);
                    float dist = d.magnitude;
                    if (dist > 2f * QuadRadius || dist < 1e-4f) continue;
                    Vector3 n = d / dist;
                    float closing = Vector3.Dot(VelocityOf(a) - VelocityOf(b), n);
                    if (closing <= 0f) continue;
                    bool hard = closing > BumpCrashSpeed;
                    Bump(a, -n * closing * 0.6f, hard);
                    Bump(b, n * closing * 0.6f, hard);
                }
        }

        private void Bump(Racer r, Vector3 dv, bool crash)
        {
            if (r.IsPlayer)
            {
                if (_player != null) _player.ApplyImpulse(dv, crash);
                return;
            }
            r.Bot.Bump(dv, crash);
        }

        private void Record(float dt)
        {
            if (!_started) return;
            _recordTimer += dt;
            if (_recordTimer < RecordInterval) return;
            _recordTimer = 0f;
            foreach (var r in _racers)
            {
                if (r.Finished && r.TrackPositions.Count > 0) continue;
                if (r.IsPlayer)
                {
                    if (_player == null) continue;
                    r.TrackPositions.Add(_player.transform.position);
                    r.TrackRotations.Add(_player.transform.rotation);
                }
                else
                {
                    r.TrackPositions.Add(r.Bot.Position);
                    r.TrackRotations.Add(r.Bot.Rotation);
                }
            }
        }

        // ---------- standings ----------

        private void UpdateStandings()
        {
            Standings.Clear();
            Standings.AddRange(_racers);
            // More checkpoints first; then whoever reached the last one sooner. Finished racers by total time.
            Standings.Sort((x, y) =>
            {
                if (x.Finished && y.Finished) return x.TotalTime.CompareTo(y.TotalTime);
                int c = y.Passes.Count.CompareTo(x.Passes.Count);
                if (c != 0) return c;
                if (x.Passes.Count == 0) return x.IsPlayer ? -1 : (y.IsPlayer ? 1 : 0);
                return x.Passes[x.Passes.Count - 1].CompareTo(y.Passes[y.Passes.Count - 1]);
            });
            var leader = Standings.Count > 0 ? Standings[0] : null;
            for (int i = 0; i < Standings.Count; i++)
            {
                var r = Standings[i];
                r.Position = i + 1;
                int k = r.Passes.Count - 1;
                r.Gap = leader != null && k >= 0 && k < leader.Passes.Count ? r.Passes[k] - leader.Passes[k] : 0;
            }
            StandingsChanged?.Invoke();
        }

        /// <summary>The winner (first finisher), or the leader if nobody finished.</summary>
        public Racer Winner => Standings.Count > 0 ? Standings[0] : null;
    }
}
