using System;
using System.Collections.Generic;
using Ushqyr.Garage;
using Ushqyr.Input;
using Ushqyr.Net;
using Ushqyr.Race;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Game
{
    /// <summary>
    /// Runs the local side of an online race in a track scene: puts the quad on its grid slot, reports "loaded" to
    /// the host, starts the countdown at the shared GO time, streams the quad's snapshots and shows the other pilots.
    /// Added by <see cref="TrackSceneController"/> when the scene was loaded for an online race.
    /// </summary>
    public sealed class OnlineRaceDriver : MonoBehaviour
    {
        /// <summary>Snapshots sent per second.</summary>
        public const float SendRate = 20f;

        public static OnlineRaceDriver Current { get; private set; }

        private TrackSceneController _scene;
        private QuadVehicle _vehicle;
        private RaceSession _race;
        private QuadModelMaterials _materials;
        private NetRace _net;
        private Transform _remoteRoot;
        private readonly Dictionary<ulong, RemotePilotView> _views = new Dictionary<ulong, RemotePilotView>();
        private float _sendTimer;
        private bool _sentFinish;
        private bool _director;

        /// <summary>This PC directs the race and does not fly in it.</summary>
        public bool Directing => _director;

        public IEnumerable<RemotePilotView> Views => _views.Values;
        public RaceSession Race => _race;

        public void Configure(TrackSceneController scene, QuadVehicle vehicle, RaceSession race, QuadModelMaterials materials)
        {
            _scene = scene;
            _vehicle = vehicle;
            _race = race;
            _materials = materials;
        }

        private void Start()
        {
            _net = NetRace.Instance;
            var track = _scene != null ? _scene.Track : null;
            if (_net == null || track == null || _race == null || _vehicle == null)
            {
                enabled = false;
                return;
            }
            Current = this;

            // Own grid slot on the start pad.
            var local = _net.Local;
            int slot = local != null ? local.Slot : 0;
            var slotPoint = new GameObject("Online Grid Slot").transform;
            slotPoint.SetParent(track.Root, false);
            slotPoint.SetPositionAndRotation(track.Spawn.position + track.Spawn.rotation * OnlineRules.GridOffset(slot), track.Spawn.rotation);
            _race.ExternalStart = true;
            int laps = _net.Laps > 0 ? _net.Laps : track.Data.Laps;
            _race.SetCourse(track.Gates, slotPoint, track.Data.Id, laps, track.Data.Name);
            _director = _net.Director && _net.IsHost;
            if (_director)
            {
                // The director's own quad stays on the ground, unseen and unheard; the director watches the others.
                _vehicle.HoldAtSpawn = true;
                foreach (Renderer r in _vehicle.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (AudioSource a in _vehicle.GetComponentsInChildren<AudioSource>(true)) a.mute = true;
                gameObject.AddComponent<DirectorCamera>().Configure(this, track.Spawn);
            }

            _remoteRoot = new GameObject("Online Pilots").transform;
            SyncViews();
            _net.RosterChanged += SyncViews;
            _net.GoReceived += OnGo;
            _race.PhaseChanged += OnPhase;

            if (HasArg("-online-autopilot")) StartAutopilot(track.Gates);

            // GO already given (we loaded after the host's timeout): join in straight away.
            if (_director) { /* not on the grid: the race starts when the pilots are */ }
            else if (_net.Phase == OnlinePhase.Racing) OnGo(_net.GoServerTime);
            else _net.SendReady();
            Debug.Log($"[Ushqyr] Online race on '{track.Data.Name}': grid slot {slot + 1}, {_net.Pilots.Count} pilots");
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_net != null)
            {
                _net.RosterChanged -= SyncViews;
                _net.GoReceived -= OnGo;
            }
            if (_race != null) _race.PhaseChanged -= OnPhase;
        }

        private void OnGo(double goServerTime)
        {
            if (_director) return;
            double goLocal = _race.Now + (goServerTime - _net.ServerTime);
            _race.ScheduleStart(goLocal);
        }

        private void OnPhase(RacePhase phase)
        {
            if (phase != RacePhase.Finished || _sentFinish) return;
            _sentFinish = true;
            var t = _race.Timer;
            _net.SendFinish((float)t.TotalTime(_race.Now), t.HasBestLap ? (float)t.BestLap : 0f);
        }

        private void Update()
        {
            if (!NetRace.Active) return;
            if (_director)
            {
                _vehicle.HoldAtSpawn = true;
                return;
            }
            var local = _net.Local;
            float progress = _race.Progress;
            int lap = _race.Timer != null ? _race.Timer.CurrentLap : 0;
            if (local != null)
            {
                local.Progress = progress;
                local.Lap = lap;
            }

            _sendTimer -= Time.deltaTime;
            if (_sendTimer > 0f) return;
            _sendTimer += 1f / SendRate;
            if (_sendTimer < 0f) _sendTimer = 0f;
            ref readonly var s = ref _vehicle.State;
            var v = s.Velocity;
            _net.SendState(new PilotSnapshot
            {
                Time = _net.ServerTime,
                Position = _vehicle.transform.position,
                Rotation = _vehicle.transform.rotation,
                Velocity = new Vector3(v.x, v.y, v.z),
                M0 = PilotSnapshot.PackMotor(s.MotorSpeed[0]),
                M1 = PilotSnapshot.PackMotor(s.MotorSpeed[1]),
                M2 = PilotSnapshot.PackMotor(s.MotorSpeed[2]),
                M3 = PilotSnapshot.PackMotor(s.MotorSpeed[3]),
                Progress = progress,
                Lap = (byte)Mathf.Clamp(lap, 0, 255),
                Armed = s.Armed,
            });
        }

        /// <summary>One remote quad per other pilot; follows the host's roster.</summary>
        private void SyncViews()
        {
            if (_net == null) return;
            ulong me = _net.LocalClientId;
            foreach (var p in _net.Pilots.Values)
            {
                if (p.ClientId == me || _views.ContainsKey(p.ClientId)) continue;
                _views[p.ClientId] = RemotePilotView.Create(p, _materials, _remoteRoot, 0);
            }
            var gone = new List<ulong>();
            foreach (var kv in _views)
                if (!_net.Pilots.ContainsKey(kv.Key)) gone.Add(kv.Key);
            foreach (ulong id in gone)
            {
                if (_views[id] != null) Destroy(_views[id].gameObject);
                _views.Remove(id);
            }
        }

        /// <summary>Test hook: the autopilot flies this pilot (two-instance smoke tests).</summary>
        private void StartAutopilot(Gate[] gates)
        {
            var source = _vehicle.gameObject.AddComponent<ScriptedInputSource>();
            _vehicle.InputSource = source;
            var autopilot = _vehicle.gameObject.AddComponent<RaceAutopilot>();
            autopilot.Configure(_race, _vehicle, source, gates);
            // -online-speed N: the autopilot's cruise speed in m/s (its own gentle 8 takes over two minutes a lap).
            string speed = BenchmarkRunner.Arg(Environment.GetCommandLineArgs(), "-online-speed", null);
            if (speed != null && float.TryParse(speed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mps)) autopilot.CruiseSpeed = mps;
        }

        private static bool HasArg(string name) =>
            Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }
}
