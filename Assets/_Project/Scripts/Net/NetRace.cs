using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace GateRush.Net
{
    public enum OnlinePhase { Lobby, Loading, Racing }

    /// <summary>
    /// The online race between pilots in one session, over named messages (no networked prefabs or scene sync). The
    /// host's PC runs the race: it hands out grid slots, tells everyone which track to load, waits until all have
    /// loaded and then sends one GO time on the shared server clock. Every pilot flies their own quad locally and sends
    /// 20 snapshots a second; the host forwards them to the others, who show them interpolated. Everyone (the host
    /// too) talks to the host the same way: the host's messages to itself are delivered locally by Netcode.
    /// </summary>
    public sealed class NetRace : MonoBehaviour
    {
        // Pilot → host.
        private const string MsgHello = "gr.s.hello", MsgReady = "gr.s.ready", MsgStateUp = "gr.s.state", MsgFinishUp = "gr.s.finish";
        // Host → pilots.
        private const string MsgRoster = "gr.c.roster", MsgLoad = "gr.c.load", MsgGo = "gr.c.go", MsgStateDown = "gr.c.state",
            MsgFinishDown = "gr.c.finish", MsgLobby = "gr.c.lobby";

        public static NetRace Instance { get; private set; }

        /// <summary>Connected to a session's race network.</summary>
        public static bool Active => Instance != null && Instance._net != null && Instance._net.IsListening;
        /// <summary>An online race is loading or under way (track scenes switch to the online start).</summary>
        public static bool InRace => Active && Instance.Phase != OnlinePhase.Lobby;

        private NetworkManager _net;
        private bool _registered;
        private float _loadStarted;

        public readonly Dictionary<ulong, NetPilot> Pilots = new Dictionary<ulong, NetPilot>();
        public OnlinePhase Phase { get; private set; } = OnlinePhase.Lobby;
        public string TrackId { get; private set; } = "";
        public int RaceId { get; private set; }
        /// <summary>Laps chosen by the host; 0 = the track's own lap count.</summary>
        public int Laps { get; private set; }
        /// <summary>GO on the server clock (valid once <see cref="GoReceived"/> fired).</summary>
        public double GoServerTime { get; private set; }

        /// <summary>Callsign and quad this PC races with (sent to the host on connect).</summary>
        public string LocalName = "Pilot";
        public string LocalBuildJson = "";

        /// <summary>A race network was created (game code hooks scene loading to it).</summary>
        public static event Action<NetRace> Created;

        public event Action RosterChanged;
        public event Action<string> LoadRequested;
        public event Action<double> GoReceived;
        public event Action<NetPilot> PilotFinished;
        public event Action ReturnedToLobby;
        public event Action<string> Disconnected;

        public bool IsHost => _net != null && _net.IsServer;
        public ulong LocalClientId => _net != null ? _net.LocalClientId : 0;
        public double ServerTime => _net != null ? _net.ServerTime.Time : 0;
        public NetPilot Local => Pilots.TryGetValue(LocalClientId, out var p) ? p : null;

        /// <summary>
        /// Creates the persistent NetworkManager (with the Unity transport) the session starts as host or client.
        /// </summary>
        public static NetRace Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Online");
            DontDestroyOnLoad(go);
            // A race doesn't pause when the window loses focus (the others keep flying).
            Application.runInBackground = true;
            var transport = go.AddComponent<UnityTransport>();
            var net = go.AddComponent<NetworkManager>();
            net.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = false,
                TickRate = 30,
            };
            var race = go.AddComponent<NetRace>();
            race._net = net;
            Created?.Invoke(race);
            return race;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (_net == null) _net = GetComponent<NetworkManager>();
        }

        private void Start()
        {
            _net.OnClientStarted += OnStarted;
            _net.OnClientStopped += OnStopped;
            _net.OnClientConnectedCallback += OnClientConnected;
            _net.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_net == null) return;
            _net.OnClientStarted -= OnStarted;
            _net.OnClientStopped -= OnStopped;
            _net.OnClientConnectedCallback -= OnClientConnected;
            _net.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        /// <summary>Back to a clean lobby state (after leaving a session).</summary>
        public void ResetState()
        {
            Pilots.Clear();
            Phase = OnlinePhase.Lobby;
            TrackId = "";
            _registered = false;
        }

        // ---------- connection ----------

        private void OnStarted()
        {
            var m = _net.CustomMessagingManager;
            m.RegisterNamedMessageHandler(MsgHello, OnHello);
            m.RegisterNamedMessageHandler(MsgReady, OnReady);
            m.RegisterNamedMessageHandler(MsgStateUp, OnStateUp);
            m.RegisterNamedMessageHandler(MsgFinishUp, OnFinishUp);
            m.RegisterNamedMessageHandler(MsgRoster, OnRoster);
            m.RegisterNamedMessageHandler(MsgLoad, OnLoad);
            m.RegisterNamedMessageHandler(MsgGo, OnGo);
            m.RegisterNamedMessageHandler(MsgStateDown, OnStateDown);
            m.RegisterNamedMessageHandler(MsgFinishDown, OnFinishDown);
            m.RegisterNamedMessageHandler(MsgLobby, OnLobby);
            _registered = true;
            Phase = OnlinePhase.Lobby;
        }

        private void OnStopped(bool wasHost)
        {
            bool had = _registered;
            ResetState();
            if (had) Disconnected?.Invoke(wasHost ? "The race was closed." : "Lost the connection to the host.");
        }

        private void OnClientConnected(ulong clientId)
        {
            if (clientId == _net.LocalClientId) SendHello();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            // During shutdown Netcode reports every pilot leaving; there is nobody left to tell.
            if (!IsHost || clientId == _net.LocalClientId || _net.ShutdownInProgress) return;
            if (Pilots.Remove(clientId)) BroadcastRoster();
            TryStartRace();
        }

        // ---------- pilot → host ----------

        public void SendHello()
        {
            using var w = Writer();
            w.WriteValueSafe(LocalName ?? "Pilot");
            w.WriteValueSafe(LocalBuildJson ?? "");
            ToHost(MsgHello, w);
        }

        /// <summary>Call when the track has loaded and the quad sits on its grid slot.</summary>
        public void SendReady()
        {
            using var w = Writer();
            w.WriteValueSafe(RaceId);
            ToHost(MsgReady, w);
        }

        public void SendState(in PilotSnapshot s)
        {
            if (!CanSend) return;
            using var w = new FastBufferWriter(PilotSnapshot.Size + 16, Allocator.Temp);
            s.Write(w);
            _net.CustomMessagingManager.SendNamedMessage(MsgStateUp, NetworkManager.ServerClientId, w, NetworkDelivery.UnreliableSequenced);
        }

        public void SendFinish(float totalTime, float bestLap)
        {
            using var w = Writer();
            w.WriteValueSafe(RaceId);
            w.WriteValueSafe(totalTime);
            w.WriteValueSafe(bestLap);
            ToHost(MsgFinishUp, w);
        }

        // ---------- host actions ----------

        /// <summary>Host: everyone loads <paramref name="trackId"/> (<paramref name="laps"/> 0 = the track's); GO follows once all are on the grid.</summary>
        public void StartRace(string trackId, int laps = 0)
        {
            if (!IsHost) return;
            using var w = Writer();
            w.WriteValueSafe(RaceId + 1);
            w.WriteValueSafe(trackId ?? "");
            w.WriteValueSafe(Mathf.Clamp(laps, 0, 10));
            ToAll(MsgLoad, w);
        }

        /// <summary>Host: everyone returns to the lobby page.</summary>
        public void BackToLobby()
        {
            if (!IsHost) return;
            using var w = Writer();
            w.WriteValueSafe((byte)0);
            ToAll(MsgLobby, w);
        }

        private void Update()
        {
            if (IsHost && Phase == OnlinePhase.Loading && Time.unscaledTime - _loadStarted > OnlineRules.LoadTimeout) SendGo();
        }

        // ---------- host handlers ----------

        private void OnHello(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            r.ReadValueSafe(out string name);
            r.ReadValueSafe(out string build);
            if (!Pilots.TryGetValue(sender, out var p))
            {
                p = new NetPilot { ClientId = sender, Slot = OnlineRules.FreeSlot(Pilots.Values) };
                Pilots[sender] = p;
            }
            p.Name = string.IsNullOrWhiteSpace(name) ? "Pilot" : name;
            p.BuildJson = build;
            BroadcastRoster();
        }

        private void OnReady(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            r.ReadValueSafe(out int raceId);
            if (raceId != RaceId || !Pilots.TryGetValue(sender, out var p)) return;
            p.Ready = true;
            Debug.Log($"[GateRush] Online: {p.Name} is on the grid");
            TryStartRace();
        }

        private void TryStartRace()
        {
            if (!IsHost || Phase != OnlinePhase.Loading) return;
            foreach (var p in Pilots.Values)
                if (!p.Ready) return;
            SendGo();
        }

        private void SendGo()
        {
            Debug.Log("[GateRush] Online: everyone is on the grid, GO in " + OnlineRules.StartLead + " s");
            using var w = Writer();
            w.WriteValueSafe(RaceId);
            w.WriteValueSafe(ServerTime + OnlineRules.StartLead);
            ToAll(MsgGo, w);
        }

        private void OnStateUp(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            var s = PilotSnapshot.Read(r);
            using var w = new FastBufferWriter(PilotSnapshot.Size + 24, Allocator.Temp);
            w.WriteValueSafe(sender);
            s.Write(w);
            var targets = new List<ulong>(_net.ConnectedClientsIds.Count);
            foreach (ulong id in _net.ConnectedClientsIds)
                if (id != sender) targets.Add(id);
            if (targets.Count > 0) _net.CustomMessagingManager.SendNamedMessage(MsgStateDown, targets, w, NetworkDelivery.UnreliableSequenced);
        }

        private void OnFinishUp(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            r.ReadValueSafe(out int raceId);
            r.ReadValueSafe(out float total);
            r.ReadValueSafe(out float best);
            if (raceId != RaceId) return;
            using var w = Writer();
            w.WriteValueSafe(sender);
            w.WriteValueSafe(total);
            w.WriteValueSafe(best);
            ToAll(MsgFinishDown, w);
        }

        private void BroadcastRoster()
        {
            using var w = Writer();
            w.WriteValueSafe(Pilots.Count);
            foreach (var p in Pilots.Values)
            {
                w.WriteValueSafe(p.ClientId);
                w.WriteValueSafe(p.Name);
                w.WriteValueSafe(p.BuildJson);
                w.WriteValueSafe((byte)p.Slot);
            }
            ToAll(MsgRoster, w);
        }

        // ---------- pilot handlers (from the host) ----------

        private void OnRoster(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int count);
            var seen = new HashSet<ulong>();
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out ulong id);
                r.ReadValueSafe(out string name);
                r.ReadValueSafe(out string build);
                r.ReadValueSafe(out byte slot);
                if (!Pilots.TryGetValue(id, out var p)) Pilots[id] = p = new NetPilot { ClientId = id };
                p.Name = name;
                p.BuildJson = build;
                p.Slot = slot;
                seen.Add(id);
            }
            var gone = new List<ulong>();
            foreach (ulong id in Pilots.Keys)
                if (!seen.Contains(id)) gone.Add(id);
            foreach (ulong id in gone) Pilots.Remove(id);
            RosterChanged?.Invoke();
        }

        private void OnLoad(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int raceId);
            r.ReadValueSafe(out string trackId);
            r.ReadValueSafe(out int laps);
            RaceId = raceId;
            TrackId = trackId;
            Laps = laps;
            Phase = OnlinePhase.Loading;
            _loadStarted = Time.unscaledTime;
            foreach (var p in Pilots.Values) p.ResetRace();
            Debug.Log($"[GateRush] Online: loading '{trackId}' for race {raceId}");
            LoadRequested?.Invoke(trackId);
        }

        private void OnGo(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int raceId);
            r.ReadValueSafe(out double go);
            if (raceId != RaceId || Phase != OnlinePhase.Loading)
            {
                Debug.LogWarning($"[GateRush] Online: GO for race {raceId} ignored (race {RaceId}, {Phase})");
                return;
            }
            Debug.Log($"[GateRush] Online: GO at server time {go:F2} (now {ServerTime:F2})");
            GoServerTime = go;
            Phase = OnlinePhase.Racing;
            GoReceived?.Invoke(go);
        }

        private void OnStateDown(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong id);
            var s = PilotSnapshot.Read(r);
            if (!Pilots.TryGetValue(id, out var p)) return;
            if (p.Buffer.Add(s))
            {
                p.Progress = s.Progress;
                p.Lap = s.Lap;
            }
        }

        private void OnFinishDown(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out ulong id);
            r.ReadValueSafe(out float total);
            r.ReadValueSafe(out float best);
            if (!Pilots.TryGetValue(id, out var p)) return;
            p.Finished = true;
            p.TotalTime = total;
            p.BestLap = best;
            PilotFinished?.Invoke(p);
        }

        private void OnLobby(ulong sender, FastBufferReader r)
        {
            Phase = OnlinePhase.Lobby;
            foreach (var p in Pilots.Values) p.ResetRace();
            ReturnedToLobby?.Invoke();
        }

        // ---------- sending ----------

        private static FastBufferWriter Writer() => new FastBufferWriter(256, Allocator.Temp, 64 * 1024);

        private bool CanSend => Active && !_net.ShutdownInProgress && _net.CustomMessagingManager != null;

        private void ToHost(string message, FastBufferWriter w)
        {
            if (!CanSend) return;
            _net.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        private void ToAll(string message, FastBufferWriter w)
        {
            if (!CanSend) return;
            _net.CustomMessagingManager.SendNamedMessageToAll(message, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }
}
