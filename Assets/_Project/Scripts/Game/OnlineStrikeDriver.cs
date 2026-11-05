using System.Collections.Generic;
using Ushqyr.AI;
using Ushqyr.Garage;
using Ushqyr.Net;
using Ushqyr.Strike;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Game
{
    /// <summary>
    /// The local side of an online Strike Range round (P6). Every PC builds the same target director from the host's
    /// seed and runs it on the shared clock (GO = round start), flies its own quad and streams snapshots as in online
    /// races. Hits are claimed to the host, whose <see cref="StrikeReferee"/> validates them (first valid claim wins)
    /// and confirms them to everyone with the new scores. The host also runs the bots and sends their poses.
    /// </summary>
    public sealed class OnlineStrikeDriver : MonoBehaviour, IStrikeLink
    {
        public const float SendRate = 20f, BotSendRate = 10f;

        /// <summary>The host's round setup, as received (it may arrive before or after the scene loads).</summary>
        public static StrikeWire.RoundSetup? PendingSetup;

        public static OnlineStrikeDriver Current { get; private set; }

        private TrackSceneController _scene;
        private QuadVehicle _vehicle;
        private StrikeSession _session;
        private QuadModelMaterials _materials;
        private SizeClass _size;
        private NetRace _net;
        private StrikeReferee _referee;
        private Transform _remoteRoot;
        private readonly Dictionary<ulong, RemotePilotView> _views = new Dictionary<ulong, RemotePilotView>();
        private readonly Dictionary<int, Transform> _botViews = new Dictionary<int, Transform>();
        private readonly Dictionary<int, StrikeWire.BotPose> _botPoses = new Dictionary<int, StrikeWire.BotPose>();
        private StrikeWire.RoundSetup _setup;
        private bool _begun, _launchedAll;
        private float _sendTimer, _botTimer;

        public float Clock => _net != null && _net.Phase == OnlinePhase.Racing ? (float)(_net.ServerTime - _net.GoServerTime) : -(StrikeRules.CountdownSeconds + 1f);
        public bool IsHost => _net != null && _net.IsHost;
        public int LocalPilot => _net != null && _net.Local != null ? _net.Local.Slot : 0;
        public StrikeReferee Referee => _referee;

        /// <summary>Host: chooses the round (seed, difficulty, bots) and tells everyone, before the load message.</summary>
        public static void HostAnnounce(NetRace net, int raceId, Ushqyr.Core.StrikePrefs prefs)
        {
            var setup = new StrikeWire.RoundSetup
            {
                RaceId = raceId,
                Seed = Random.Range(1, int.MaxValue),
                Difficulty = (RangeDifficulty)prefs.Difficulty,
                Bots = Mathf.Clamp(prefs.Bots, 0, 3),
                BotLevel = Mathf.Clamp(prefs.BotLevel, 0, 4),
            };
            PendingSetup = setup;
            net.SendExtraToAll(StrikeWire.Setup, StrikeWire.Encode(setup));
        }

        public void Configure(TrackSceneController scene, QuadVehicle vehicle, StrikeSession session, QuadModelMaterials materials, SizeClass size)
        {
            _scene = scene;
            _vehicle = vehicle;
            _session = session;
            _materials = materials;
            _size = size;
        }

        private void Start()
        {
            _net = NetRace.Instance;
            if (_net == null || _session == null || _vehicle == null)
            {
                enabled = false;
                return;
            }
            Current = this;
            _remoteRoot = new GameObject("Online Pilots").transform;
            _net.RegisterExtra(StrikeWire.Confirm, OnConfirm);
            _net.RegisterExtra(StrikeWire.Reject, OnReject);
            _net.RegisterExtra(StrikeWire.Score, OnScore);
            _net.RegisterExtra(StrikeWire.Bots, OnBots);
            if (IsHost)
            {
                _net.RegisterExtra(StrikeWire.Claim, OnClaim);
                _net.RegisterExtra(StrikeWire.Crash, OnCrash);
            }
            _net.RosterChanged += SyncViews;
            SyncViews();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_net == null) return;
            foreach (string m in new[] { StrikeWire.Confirm, StrikeWire.Reject, StrikeWire.Score, StrikeWire.Bots, StrikeWire.Claim, StrikeWire.Crash })
                _net.UnregisterExtra(m);
            _net.RosterChanged -= SyncViews;
            if (_session != null)
            {
                _session.BotHit -= OnBotHit;
                _session.BotCrashed -= OnBotCrashed;
                _session.RestartRequested -= OnRestartRequested;
            }
        }

        // Waits for the host's setup, then starts the round and reports "on the pad".
        private void TryBegin()
        {
            if (_begun || PendingSetup == null || PendingSetup.Value.RaceId != _net.RaceId) return;
            _setup = PendingSetup.Value;
            _begun = true;
            _session.Link = this;
            _session.StartPad = LocalPilot % StrikeRules.Pads;
            var options = new StrikeOptions
            {
                Difficulty = _setup.Difficulty,
                Bots = _setup.Bots,
                BotLevel = (BotLevel)_setup.BotLevel,
                Seed = _setup.Seed,
                PilotName = _net.LocalName,
            };
            _session.BotHit += OnBotHit;
            _session.BotCrashed += OnBotCrashed;
            _session.RestartRequested += OnRestartRequested;
            _session.Begin(_vehicle, options, _materials, _size);
            if (IsHost) _referee = new StrikeReferee(_session.Director);
            if (_net.Phase != OnlinePhase.Racing) _net.SendReady();
            Debug.Log($"[Ushqyr][Strike] Online round: seed {_setup.Seed}, {_setup.Difficulty}, {_setup.Bots} bots, pad {_session.StartPad + 1}, {(IsHost ? "host" : "client")}");
        }

        private void Update()
        {
            if (!NetRace.Active) return;
            TryBegin();
            if (!_begun) return;

            // Host: everyone's first launch at the start of the round.
            if (IsHost && !_launchedAll && Clock >= 0f)
            {
                _launchedAll = true;
                foreach (var p in _net.Pilots.Values)
                {
                    _referee.ScoreFor(p.Slot, p.Name).Launch();
                    SendScore(p.Slot);
                }
            }

            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = Mathf.Max(0f, _sendTimer + 1f / SendRate);
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
                    Progress = _session.PlayerScore.Score,
                    Lap = 0,
                    Armed = s.Armed,
                });
            }

            if (IsHost && _session.Bots.Count > 0)
            {
                _botTimer -= Time.deltaTime;
                if (_botTimer <= 0f)
                {
                    _botTimer = Mathf.Max(0f, _botTimer + 1f / BotSendRate);
                    var poses = new List<StrikeWire.BotPose>(_session.Bots.Count);
                    for (int i = 0; i < _session.Bots.Count; i++)
                    {
                        var b = _session.Bots[i];
                        poses.Add(new StrikeWire.BotPose { Index = (byte)i, Down = b.Down, Position = b.Position, Rotation = b.Rotation });
                    }
                    _net.SendExtraToAll(StrikeWire.Bots, StrikeWire.Encode(poses), false);
                }
            }
            else UpdateBotViews();
        }

        // ---------- IStrikeLink: the local pilot ----------

        public void ClaimHit(in StrikeWire.HitClaim claim)
        {
            if (IsHost) Judge(_net.LocalClientId, claim);
            else _net.SendExtraToHost(StrikeWire.Claim, StrikeWire.Encode(claim));
        }

        public void ReportCrash(float time)
        {
            if (IsHost) CrashOf(_net.LocalClientId);
            else _net.SendExtraToHost(StrikeWire.Crash, StrikeWire.EncodeCrash(time));
        }

        private void OnRestartRequested()
        {
            // After the round the host takes everyone back to the lobby (the next round gets a new seed there).
            if (IsHost && _session.Phase == StrikePhase.Finished) OnlineFlow.HostBackToLobby();
        }

        // ---------- host ----------

        private void OnClaim(ulong sender, byte[] data)
        {
            if (!IsHost || _referee == null) return;
            Judge(sender, StrikeWire.DecodeClaim(data));
        }

        private void OnCrash(ulong sender, byte[] data)
        {
            if (!IsHost || _referee == null) return;
            CrashOf(sender);
        }

        private void Judge(ulong sender, in StrikeWire.HitClaim claim)
        {
            if (_referee == null || !_net.Pilots.TryGetValue(sender, out var pilot)) return;
            _referee.ScoreFor(pilot.Slot, pilot.Name);
            var verdict = _referee.Resolve(pilot.Slot, claim, Clock, out var confirm);
            if (verdict == RefereeVerdict.Hit) _net.SendExtraToAll(StrikeWire.Confirm, StrikeWire.Encode(confirm));
            else
            {
                Debug.Log($"[Ushqyr][Strike] Claim by {pilot.Name} on target {claim.Slot} not counted: {verdict}");
                _net.SendExtraTo(sender, StrikeWire.Reject, StrikeWire.Encode(new StrikeWire.ClaimReject { Pilot = pilot.Slot, Slot = claim.Slot, Verdict = verdict }));
            }
            SendScore(pilot.Slot);
        }

        private void CrashOf(ulong sender)
        {
            if (_referee == null || !_net.Pilots.TryGetValue(sender, out var pilot)) return;
            _referee.ScoreFor(pilot.Slot, pilot.Name);
            _referee.Crash(pilot.Slot);
            SendScore(pilot.Slot);
        }

        private void OnBotHit(int bot, StrikeWire.HitConfirm confirm)
        {
            if (!IsHost) return;
            _net.SendExtraToAll(StrikeWire.Confirm, StrikeWire.Encode(confirm));
            SendBotScore(bot);
        }

        private void OnBotCrashed(int bot)
        {
            if (IsHost) SendBotScore(bot);
        }

        private void SendScore(int pilot)
        {
            if (_referee == null || !_referee.Scores.TryGetValue(pilot, out var s)) return;
            _net.SendExtraToAll(StrikeWire.Score, StrikeWire.Encode(StrikeWire.ScoreState.From(pilot, s)));
        }

        private void SendBotScore(int bot)
        {
            if (bot < 0 || bot >= _session.Bots.Count) return;
            _net.SendExtraToAll(StrikeWire.Score, StrikeWire.Encode(StrikeWire.ScoreState.From(StrikeWire.BotKeyBase + bot, _session.Bots[bot].Score)));
        }

        // ---------- everyone ----------

        private void OnConfirm(ulong sender, byte[] data)
        {
            if (!_begun) return;
            var c = StrikeWire.DecodeConfirm(data);
            _session.ApplyConfirm(c, NameOf(c.Pilot));
        }

        private void OnReject(ulong sender, byte[] data)
        {
            if (!_begun) return;
            var r = StrikeWire.DecodeReject(data);
            if (r.Pilot == LocalPilot) _session.ApplyReject(r.Verdict);
        }

        private void OnScore(ulong sender, byte[] data)
        {
            if (!_begun) return;
            var s = StrikeWire.DecodeScore(data);
            _session.ApplyScore(s, NameOf(s.Pilot));
        }

        private void OnBots(ulong sender, byte[] data)
        {
            if (!_begun || IsHost) return;
            foreach (var p in StrikeWire.DecodeBots(data)) _botPoses[p.Index] = p;
        }

        private string NameOf(int pilot)
        {
            if (pilot >= StrikeWire.BotKeyBase) return BotRoster.NameFor(pilot - StrikeWire.BotKeyBase, _setup.Seed);
            foreach (var p in _net.Pilots.Values)
                if (p.Slot == pilot) return p.Name;
            return $"Pilot {pilot + 1}";
        }

        // The host's bots on the clients: their own model, eased towards the latest pose (10 Hz).
        private void UpdateBotViews()
        {
            foreach (var kv in _botPoses)
            {
                if (!_botViews.TryGetValue(kv.Key, out var view) || view == null)
                {
                    view = new GameObject("Online Bot " + NameOf(StrikeWire.BotKeyBase + kv.Key)).transform;
                    view.SetParent(_remoteRoot, false);
                    if (_materials != null) QuadModelBuilder.Build(BotRoster.BuildFor(_size, kv.Key), _materials, view, 0);
                    view.SetPositionAndRotation(kv.Value.Position, kv.Value.Rotation);
                    _botViews[kv.Key] = view;
                }
                var pose = kv.Value;
                view.gameObject.SetActive(!pose.Down);
                float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
                view.SetPositionAndRotation(Vector3.Lerp(view.position, pose.Position, k), Quaternion.Slerp(view.rotation, pose.Rotation, k));
            }
        }

        /// <summary>One remote quad per other pilot; pilots who left drop out of the standings.</summary>
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
                if (_views[id] != null)
                {
                    var pilot = _views[id].Pilot;
                    if (pilot != null && _session != null) _session.RemovePilot(pilot.Slot);
                    Destroy(_views[id].gameObject);
                }
                _views.Remove(id);
            }
        }
    }
}
