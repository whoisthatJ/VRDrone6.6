using System;
using GateRush.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace GateRush.Race
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
    /// ghosts and the local leaderboard. Restart: T / gamepad left-stick click / left Touch stick click.
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

        private ButtonControl _touchRestart;
        private bool _touchDirty = true;

        public event Action<RacePhase> PhaseChanged;
        /// <summary>Gate index and result, for HUD/audio.</summary>
        public event Action<int, PassResult> GatePassed;

        public RacePhase Phase => _phase;
        public LapTimer Timer => _timer;
        public string TrackId => trackId;
        /// <summary>Display name (falls back to the id).</summary>
        public string TrackName => string.IsNullOrEmpty(_trackName) ? trackId : _trackName;
        public int GateCount => gates.Length;
        public double Now => Time.timeAsDouble;
        public float CountdownRemaining => _phase == RacePhase.Countdown ? (float)Math.Max(0, _countdownEnd - Now) : 0f;
        public bool WaitingForRelease => _holdForRelease;
        public double WrongGateUntil { get; private set; }
        public float LastLapTime { get; private set; }
        public bool LastLapWasPersonalBest { get; private set; }
        /// <summary>1-based leaderboard rank of the last finished race, or -1.</summary>
        public int LastRank { get; private set; } = -1;
        public bool LastRaceSetTrackBestLap { get; private set; }
        public TrackBoard Board => _board.Board(trackId);
        public GhostSystem Ghosts => ghosts;

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
            if (vehicle != null) vehicle.Respawned -= OnRespawned;
        }

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is XRController) _touchDirty = true;
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
                Restart();
                return;
            }

            double now = Now;
            bool armed = vehicle.State.Armed || vehicle.LastInput.Arm;

            switch (_phase)
            {
                case RacePhase.Idle:
                    if (armed && !vehicle.IsCrashed)
                    {
                        _countdownEnd = now + countdownSeconds;
                        vehicle.HoldAtSpawn = true;
                        SetPhase(RacePhase.Countdown);
                    }
                    break;

                case RacePhase.Countdown:
                    if (!vehicle.LastInput.Arm)
                    {
                        vehicle.HoldAtSpawn = false;
                        SetPhase(RacePhase.Idle);
                    }
                    else if (now >= _countdownEnd)
                    {
                        vehicle.HoldAtSpawn = false;
                        _timer.Start(_countdownEnd);
                        if (ghosts != null) ghosts.BeginLap(_countdownEnd);
                        _hasPrev = false;
                        LastRank = -1;
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
                OnPass(next, _prevTime + f * (now - _prevTime));
                return;
            }

            for (int i = 0; i < gates.Length; i++)
            {
                if (i == next || !gates[i].TryCross(_prevPos, cur, out _)) continue;
                WrongGateUntil = now + wrongGateHintSeconds;
                GatePassed?.Invoke(i, PassResult.WrongGate);
                return;
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
            LastRank = _board.Submit(trackId, entry, out bool trackBest);
            LastRaceSetTrackBestLap = trackBest;
            _board.Save(LocalLeaderboard.DefaultPath);
            SetPhase(RacePhase.Finished);
            Debug.Log($"[GateRush] Race finished on {trackId}: total {entry.TotalTime:F2}s, best lap {entry.BestLap:F2}s, rank {LastRank}");
        }

        private void OnRespawned()
        {
            _hasPrev = false;
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
            for (int i = 0; i < gates.Length; i++)
            {
                GateVisualState s = GateVisualState.Idle;
                if (racing || showStart)
                {
                    if (i == next) s = GateVisualState.Next;
                    else if (i == upcoming) s = GateVisualState.Upcoming;
                }
                gates[i].SetVisualState(s);
            }
        }

        private bool RestartPressed()
        {
            if (_touchDirty)
            {
                _touchDirty = false;
                _touchRestart = XRController.leftHand?.TryGetChildControl<ButtonControl>("thumbstickClicked");
            }
            return (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
                   || (Gamepad.current != null && Gamepad.current.leftStickButton.wasPressedThisFrame)
                   || (_touchRestart != null && _touchRestart.device.added && _touchRestart.wasPressedThisFrame);
        }
    }
}
