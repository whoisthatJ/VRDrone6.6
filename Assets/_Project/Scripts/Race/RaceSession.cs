using System;
using Ushqyr.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Ushqyr.Race
{
    public enum RacePhase
    {
        /// <summary>On the pad. Arming starts the countdown.</summary>
        Idle = 0,
        /// <summary>3-2-1: armed and held on the pad. Disarming cancels.</summary>
        Countdown = 1,
        Racing = 2,
        /// <summary>Results shown; free flight until restart.</summary>
        Finished = 3,
    }

    /// <summary>
    /// Runs a lap race over an ordered list of gates: countdown, sub-frame gate timing, checkpoint respawns,
    /// ghosts and the local leaderboard. Restart: T / gamepad left-stick click.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class RaceSession : MonoBehaviour
    {
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private Gate[] gates = new Gate[0];
        [SerializeField] private GhostSystem ghosts;
        [SerializeField] private Transform startPad;
        [SerializeField, Range(1, 10)] private int laps = 3;
        [SerializeField] private float countdownSeconds = 3f;
        [Tooltip("After a checkpoint respawn the quad is held until armed and throttle is above this.")]
        [SerializeField] private float releaseThrottle = 0.12f;
        [Tooltip("Defaults to the scene name.")]
        [SerializeField] private string trackId;
        [SerializeField] private string pilotName = "Pilot";
        [Tooltip("How long the 'missed gate' hint stays up (s).")]
        [SerializeField] private float wrongGateHintSeconds = 1.5f;

        private LapTimer _timer;
        private string _trackName;
        private readonly LocalLeaderboard _board = new LocalLeaderboard();
        private Transform _checkpointSpawn;
        private RacePhase _phase;
        private double _countdownEnd;
        private Vector3 _prevPos;
        private double _prevTime;
        private bool _hasPrev, _holdForRelease;

        public event Action<RacePhase> PhaseChanged;
        /// <summary>Gate index and result, for HUD/audio.</summary>
        public event Action<int, PassResult> GatePassed;

        public RacePhase Phase => _phase;
        public LapTimer Timer => _timer;
        public string TrackId => trackId;
        /// <summary>Display name (falls back to the id).</summary>
        public string TrackName => string.IsNullOrEmpty(_trackName) ? trackId : _trackName;
        public int GateCount => gates.Length;
        /// <summary>A copy of the course in race order (gate 0 = start/finish).</summary>
        public Gate[] CourseGates => (Gate[])gates.Clone();
        /// <summary>The gate to fly through next, or null without a course or after the finish.</summary>
        public Gate NextGate => _timer != null && !_timer.Finished && gates.Length > 0
            ? gates[Mathf.Clamp(_timer.NextCheckpoint, 0, gates.Length - 1)]
            : null;
        public double Now => Time.timeAsDouble;
        public float CountdownRemaining => _phase == RacePhase.Countdown ? (float)Math.Max(0, _countdownEnd - Now) : 0f;
        public bool WaitingForRelease => _holdForRelease;
        public double WrongGateUntil { get; private set; }
        /// <summary>Flying past (not through) the next gate within this distance counts as missing it.</summary>
        public const float MissRadius = 5f;

        private Ushqyr.Core.RouteGuidanceLevel _guidance = Ushqyr.Core.RouteGuidanceLevel.Standard;

        /// <summary>Route guidance (T5): Off shows every gate plain; Standard/Trainer highlight next, upcoming and passed.</summary>
        public Ushqyr.Core.RouteGuidanceLevel Guidance
        {
            get => _guidance;
            set
            {
                if (_guidance == value) return;
                _guidance = value;
                if (gates != null && _timer != null) UpdateGateVisuals();
            }
        }
        public float LastLapTime { get; private set; }
        public bool LastLapWasPersonalBest { get; private set; }
        /// <summary>1-based leaderboard rank of the last finished race, or -1.</summary>
        public int LastRank { get; private set; } = -1;
        public bool LastRaceSetTrackBestLap { get; private set; }
        public TrackBoard Board => _board.Board(trackId);
        /// <summary>Share (0–100) of your earlier races on this track that the last one beat; -1 for the first race.</summary>
        public float LastPercentile { get; private set; } = -1f;
        /// <summary>The start/finish obstacle (race gate 0), e.g. the finish cube.</summary>
        public Gate FinishGate => gates.Length > 0 ? gates[0] : null;

        // Race statistics for the results screen (reset at GO).
        public int Crashes { get; private set; }
        /// <summary>Respawns that weren't crashes (the pilot's reset button).</summary>
        public int Resets { get; private set; }
        /// <summary>Metres per second.</summary>
        public float TopSpeed { get; private set; }
        /// <summary>Metres flown during the race (respawn jumps excluded).</summary>
        public float Distance { get; private set; }
        private bool _crashPending;

        /// <summary>P3: seconds added for touching a HoloRing rim.</summary>
        public const float RimPenalty = 0.5f;
        /// <summary>Raised when a HoloRing rim is touched (the HUD shows the penalty).</summary>
        public event Action<HoloRing> RimTouched;
        /// <summary>Until when the HUD shows the last rim penalty.</summary>
        public double RimTouchedUntil { get; private set; }
        private HoloRing[] _rings = new HoloRing[0];
        private float[] _ringCooldown = new float[0];

        private void FindRings()
        {
            var list = new System.Collections.Generic.List<HoloRing>();
            foreach (var g in gates)
            {
                var r = g != null ? g.GetComponentInParent<HoloRing>() : null;
                if (r != null && !list.Contains(r)) list.Add(r);
            }
            _rings = list.ToArray();
            _ringCooldown = new float[_rings.Length];
        }

        /// <summary>Touching a ring's rim (tube + the quad's ~0.2 m) glitches it and costs <see cref="RimPenalty"/>.</summary>
        private void CheckRims(Vector3 from, Vector3 to, double now)
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                var r = _rings[i];
                if (r == null || now < _ringCooldown[i]) continue;
                // Only near the ring: cheap reject by distance to the centre.
                float reach = r.WorldRadius + r.WorldTube + 2f;
                if ((to - r.CentreWorld).sqrMagnitude > reach * reach) continue;
                float touch = r.WorldTube + 0.2f;
                bool hit = false;
                for (int k = 0; k <= 4 && !hit; k++) hit = r.RimDistance(Vector3.Lerp(from, to, k / 4f)) < touch;
                if (!hit) continue;
                _ringCooldown[i] = (float)now + 1f;
                _timer.AddPenalty(RimPenalty);
                r.Glitch();
                RimTouchedUntil = now + 1.2;
                RimTouched?.Invoke(r);
            }
        }
        public GhostSystem Ghosts => ghosts;
        public QuadVehicle Vehicle => vehicle;

        /// <summary>
        /// Online races: the start comes from <see cref="ScheduleStart"/> (same moment for every pilot) instead of
        /// arming, the countdown is not cancelled by disarming, and Restart only respawns at the last checkpoint.
        /// </summary>
        public bool ExternalStart { get; set; }

        /// <summary>
        /// The start of FAI drone racing (F9U.6.2) instead of 3-2-1: after the "arm" call the tone comes at a moment
        /// nobody can foresee, 2 to 4 seconds later, and the model is free on its start position meanwhile.
        /// </summary>
        public bool FaiStart { get; set; }
        /// <summary>The model was not on its start position when the tone sounded (the first half of a false start).</summary>
        public bool LeftStartEarly { get; private set; }
        /// <summary>Left the start before the tone and went through the first gate: disqualified from the race (F9U.6.2).</summary>
        public bool FalseStart { get; private set; }
        /// <summary>Until when the HUD says "false start".</summary>
        public double FalseStartUntil { get; private set; }
        /// <summary>How far from its start position a model may be and still count as standing on it, metres.</summary>
        public const float StartPositionRadius = 0.35f;
        /// <summary>The tone: the time of the start signal on the <see cref="Now"/> clock.</summary>
        public event Action<double> StartSignal;
        /// <summary>Any gate flown through the right way, in turn or not, with its sub-frame time: for a timing that keeps its own rules.</summary>
        public event Action<int, double> GateCrossed;
        /// <summary>A false start happened (online, the race goes on for the others; alone, the race is set up again).</summary>
        public event Action FalseStarted;

        /// <summary>Starts the countdown so that GO is at <paramref name="goTime"/> (<see cref="Now"/> clock).</summary>
        public void ScheduleStart(double goTime)
        {
            Restart();
            _countdownEnd = goTime;
            vehicle.HoldAtSpawn = !FaiStart;
            SetPhase(RacePhase.Countdown);
        }

        /// <summary>
        /// Distance along the race in gates: gates passed (holeshot included) plus the fraction of the current leg
        /// flown. Comparable between pilots on the same course; used for online standings.
        /// </summary>
        public float Progress
        {
            get
            {
                if (_timer == null || !_timer.Started || gates.Length == 0) return 0f;
                if (_timer.Finished) return _timer.TotalLaps * gates.Length + 1;
                int passed = RaceProgress.GatesPassed(_timer.LastCheckpoint >= 0, _timer.CompletedLaps, _timer.NextCheckpoint, gates.Length);
                Vector3 to = gates[_timer.NextCheckpoint].OpeningCenterWorld;
                Vector3 from = passed == 0 ? (startPad != null ? startPad.position : to) : gates[(_timer.NextCheckpoint - 1 + gates.Length) % gates.Length].OpeningCenterWorld;
                return passed + RaceProgress.LegFraction(from, to, vehicle.transform.position);
            }
        }

        public string PilotName
        {
            get => pilotName;
            set => pilotName = string.IsNullOrWhiteSpace(value) ? "Pilot" : value;
        }

        private void Awake()
        {
            if (string.IsNullOrEmpty(trackId)) trackId = gameObject.scene.name;
            _timer = new LapTimer(Mathf.Max(1, gates.Length), laps);
            for (int i = 0; i < gates.Length; i++) gates[i].Order = i;
            _checkpointSpawn = new GameObject("Checkpoint Respawn").transform;
            _checkpointSpawn.SetParent(transform, false);
            _board.Load(LocalLeaderboard.DefaultPath);
        }

        private void Start()
        {
            vehicle.Respawned += OnRespawned;
            vehicle.Crashed += OnCrashed;
            ReloadGhost();
            UpdateGateVisuals();
        }

        /// <summary>Replaces the course (e.g. loaded from track JSON) and resets the race.</summary>
        public void SetCourse(Gate[] course, Transform pad, string id, int lapCount, string displayName = null)
        {
            gates = course ?? new Gate[0];
            startPad = pad;
            trackId = string.IsNullOrEmpty(id) ? gameObject.scene.name : id;
            _trackName = displayName;
            laps = Mathf.Clamp(lapCount, 1, 10);
            _timer = new LapTimer(Mathf.Max(1, gates.Length), laps);
            for (int i = 0; i < gates.Length; i++) gates[i].Order = i;
            FindRings();
            ReloadGhost();
            Restart();
        }

        /// <summary>Loads the saved best ghost for the current track and quad (call after the quad changes).</summary>
        public void ReloadGhost()
        {
            if (ghosts != null) ghosts.Load(trackId, vehicle.Profile.Name);
        }

        private void OnDestroy()
        {
            if (vehicle == null) return;
            vehicle.Respawned -= OnRespawned;
            vehicle.Crashed -= OnCrashed;
        }

        private void OnCrashed()
        {
            if (_phase != RacePhase.Racing) return;
            Crashes++;
            _crashPending = true;
        }

        public void Restart()
        {
            _timer.Reset();
            if (ghosts != null) ghosts.Stop();
            _holdForRelease = false;
            vehicle.HoldAtSpawn = false;
            vehicle.SpawnPoint = startPad;
            SetPhase(RacePhase.Idle);
            vehicle.Respawn();
            UpdateGateVisuals();
        }

        private void Update()
        {
            if (RestartPressed())
            {
                if (!ExternalStart)
                {
                    Restart();
                    return;
                }
                if (_phase == RacePhase.Racing) vehicle.Respawn();
            }

            double now = Now;
            bool armed = vehicle.State.Armed || vehicle.LastInput.Arm;

            switch (_phase)
            {
                case RacePhase.Idle:
                    if (!ExternalStart && armed && !vehicle.IsCrashed)
                    {
                        // FAI: the tone comes 2 to 4 seconds after the arm call, and the model is not held.
                        _countdownEnd = now + (FaiStart ? UnityEngine.Random.Range(2f, 4f) : countdownSeconds);
                        vehicle.HoldAtSpawn = !FaiStart;
                        SetPhase(RacePhase.Countdown);
                    }
                    break;

                case RacePhase.Countdown:
                    if (!ExternalStart && !vehicle.LastInput.Arm)
                    {
                        vehicle.HoldAtSpawn = false;
                        SetPhase(RacePhase.Idle);
                    }
                    else if (now >= _countdownEnd)
                    {
                        vehicle.HoldAtSpawn = false;
                        var at = vehicle.State.Position;
                        LeftStartEarly = FaiStart && startPad != null && Vector3.Distance(new Vector3(at.x, at.y, at.z), startPad.position) > StartPositionRadius;
                        FalseStart = false;
                        StartSignal?.Invoke(_countdownEnd);
                        _timer.Start(_countdownEnd);
                        if (ghosts != null) ghosts.BeginLap(_countdownEnd);
                        _hasPrev = false;
                        LastRank = -1;
                        Crashes = Resets = 0;
                        TopSpeed = Distance = 0f;
                        _crashPending = false;
                        SetPhase(RacePhase.Racing);
                        UpdateGateVisuals();
                    }
                    break;

                case RacePhase.Racing:
                    if (_holdForRelease)
                    {
                        vehicle.HoldAtSpawn = true;
                        if (vehicle.LastInput.Arm && vehicle.LastInput.Throttle > releaseThrottle)
                        {
                            _holdForRelease = false;
                            vehicle.HoldAtSpawn = false;
                        }
                    }
                    if (_hasPrev && !vehicle.HoldAtSpawn)
                    {
                        var q = vehicle.State.Position;
                        float step = Vector3.Distance(_prevPos, new Vector3(q.x, q.y, q.z));
                        Distance += step;
                        if (now > _prevTime) TopSpeed = Mathf.Max(TopSpeed, step / (float)(now - _prevTime));
                    }
                    if (_hasPrev && _rings.Length > 0)
                    {
                        var q2 = vehicle.State.Position;
                        CheckRims(_prevPos, new Vector3(q2.x, q2.y, q2.z), now);
                    }
                    CheckGates(now);
                    if (ghosts != null && _phase == RacePhase.Racing)
                        ghosts.Record(Time.deltaTime, vehicle.transform.position, vehicle.transform.rotation);
                    break;
            }

            var p = vehicle.State.Position;
            _prevPos = new Vector3(p.x, p.y, p.z);
            _prevTime = now;
            _hasPrev = !vehicle.IsCrashed && !vehicle.HoldAtSpawn;
        }

        private void CheckGates(double now)
        {
            if (!_hasPrev || gates.Length == 0) return;
            var p = vehicle.State.Position;
            var cur = new Vector3(p.x, p.y, p.z);

            int next = _timer.NextCheckpoint;
            if (gates[next].TryCross(_prevPos, cur, out float f))
            {
                double at = _prevTime + f * (now - _prevTime);
                GateCrossed?.Invoke(next, at);
                // A false start: not on the start position at the tone, and now through the first gate.
                if (FaiStart && LeftStartEarly && !FalseStart && _timer.LastCheckpoint < 0 && next == 0)
                {
                    FalseStart = true;
                    FalseStartUntil = now + 4.0;
                    FalseStarted?.Invoke();
                    if (!ExternalStart)
                    {
                        Restart();
                        return;
                    }
                }
                OnPass(next, at);
                return;
            }

            for (int i = 0; i < gates.Length; i++)
            {
                if (i == next || !gates[i].TryCross(_prevPos, cur, out float fw)) continue;
                GateCrossed?.Invoke(i, _prevTime + fw * (now - _prevTime));
                WrongGateUntil = now + wrongGateHintSeconds;
                GatePassed?.Invoke(i, PassResult.WrongGate);
                return;
            }

            // Flew past the next gate: warn and point back to it (the lap only counts through every gate in order).
            if (_phase == RacePhase.Racing && gates[next].PassedBeside(_prevPos, cur, MissRadius))
            {
                WrongGateUntil = now + wrongGateHintSeconds;
                GatePassed?.Invoke(next, PassResult.WrongGate);
            }
        }

        private void OnPass(int gate, double time)
        {
            var result = _timer.Pass(gate, time);
            if (result == PassResult.Ignored || result == PassResult.WrongGate) return;

            gates[gate].GetRespawnPose(out Vector3 rp, out Quaternion rr);
            _checkpointSpawn.SetPositionAndRotation(rp, rr);
            vehicle.SpawnPoint = _checkpointSpawn;

            if (result == PassResult.LapCompleted || result == PassResult.RaceFinished)
            {
                int lapIndex = _timer.CompletedLaps - 1;
                LastLapTime = (float)_timer.LapTime(lapIndex);
                LastLapWasPersonalBest = ghosts != null ? ghosts.EndLap(LastLapTime) : _timer.BestLapIndex == lapIndex;
                if (result == PassResult.LapCompleted && ghosts != null) ghosts.BeginLap(time);
            }

            if (result == PassResult.RaceFinished) Finish();
            GatePassed?.Invoke(gate, result);
            UpdateGateVisuals();
        }

        private void Finish()
        {
            if (ghosts != null) ghosts.Stop();
            var entry = new LeaderboardEntry
            {
                Pilot = pilotName,
                Quad = vehicle.Profile.Name,
                TotalTime = (float)_timer.TotalTime(Now),
                BestLap = (float)_timer.BestLap,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };
            LastRank = _board.Submit(trackId, entry, out bool trackBest, out float percentile);
            LastPercentile = percentile;
            LastRaceSetTrackBestLap = trackBest;
            _board.Save(LocalLeaderboard.DefaultPath);
            SetPhase(RacePhase.Finished);
            Debug.Log($"[Ushqyr] Race finished on {trackId}: total {entry.TotalTime:F2}s, best lap {entry.BestLap:F2}s, rank {LastRank}");
        }

        private void OnRespawned()
        {
            _hasPrev = false;
            if (_phase == RacePhase.Racing)
            {
                if (_crashPending) _crashPending = false;
                else Resets++;
            }
            // Mid-race respawns at a checkpoint are in the air: hold there until the pilot re-arms and adds throttle.
            if (_phase == RacePhase.Racing && _timer.LastCheckpoint >= 0) _holdForRelease = true;
        }

        private void SetPhase(RacePhase phase)
        {
            if (_phase == phase) return;
            _phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        private void UpdateGateVisuals()
        {
            bool racing = _phase == RacePhase.Racing;
            bool showStart = _phase == RacePhase.Idle || _phase == RacePhase.Countdown;
            int next = racing ? _timer.NextCheckpoint : 0;
            int upcoming = gates.Length > 1 ? (next + 1) % gates.Length : -1;
            bool guided = _guidance != Ushqyr.Core.RouteGuidanceLevel.Off;
            for (int i = 0; i < gates.Length; i++)
            {
                GateVisualState s = GateVisualState.Idle;
                if (guided && (racing || showStart))
                {
                    if (i == next) s = GateVisualState.Next;
                    else if (i == upcoming) s = GateVisualState.Upcoming;
                    // Gates already flown this lap (gate 0 counts once the lap is under way).
                    else if (racing && next > 0 && i < next) s = GateVisualState.Passed;
                }
                gates[i].SetVisualState(s);
            }
        }

        private bool RestartPressed()
        {
            return (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
                   || (Gamepad.current != null && Gamepad.current.leftStickButton.wasPressedThisFrame);
        }
    }
}
