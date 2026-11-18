using System;
using System.Collections.Generic;
using System.IO;
using Ushqyr.Flight;
using Ushqyr.Garage;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Bench
{
    public enum AttemptState { Waiting, Running, Ended }

    /// <summary>
    /// One AI Bench run: builds the track from its file, owns the simulator clock, and serves the program's
    /// messages. In sync mode every STEP message advances the simulation by exactly one control period, so the result
    /// doesn't depend on how fast the computer or the program is.
    ///
    /// Pipeline per step: RC packet -> link (delay, failsafe) -> the same flight controller and flight model as the rest
    /// of the simulator -> sensors (camera, IMU, ToF, battery) -> observation. The referee watches the true position but
    /// is not reachable from the API.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class BenchSession : MonoBehaviour
    {
        [SerializeField] private QuadVehicle quad;
        [SerializeField] private Transform spawn;
        [SerializeField] private Transform gatesRoot;
        [SerializeField] private BenchGate gatePrefab;
        [SerializeField] private QuadModelMaterials modelMaterials = new QuadModelMaterials();
        [SerializeField] private LayerMask solidLayers = ~0;
        [SerializeField] private int quadLayer = 9;
        [Tooltip("Track loaded when nothing was chosen on the command line (file name in StreamingAssets/Bench/Tracks).")]
        [SerializeField] private string defaultTrack = "H1_Oval";

        private BenchRules _rules;
        private BenchTrack _track;
        private readonly List<BenchGate> _gates = new List<BenchGate>();
        private Referee _referee;
        private RcLink _link;
        private BenchApiServer _server;
        private BenchCameraSensor _camera;
        private ImuModel _imu;
        private TofModel _tof;
        private AnalogVideo _video;
        private BenchRandom _batteryNoise;

        // Simulator clock: whole physics steps since the start signal.
        private long _tick;
        private float _dt;
        private int _stepsPerControl, _imuEvery, _tofEvery, _batteryEvery;
        private long _cameraFrames;
        private float _timeLimit;

        private int _connection;
        private bool _helloDone, _wantTruth, _startedOnce, _paced = true;
        private double _paceCredit;
        private BenchApiServer.Inbound _held;
        private bool _hasHeld;
        private Vector3 _stuckAnchor;
        private long _stuckSince;

        private readonly List<int> _channels = new List<int>(RcLink.Channels);
        private readonly List<ImuSample> _imuBatch = new List<ImuSample>(16);
        private byte[] _imuBytes = new byte[16 * 28];
        private readonly MsgPackWriter _header = new MsgPackWriter();

        private struct Frame { public double Time; public long Seq; public byte[] Pixels; }
        private readonly Queue<Frame> _framesInFlight = new Queue<Frame>();
        private readonly Stack<byte[]> _framePool = new Stack<byte[]>();
        private byte[] _delivered;
        private float _tofRange; private bool _tofValid, _tofNew; private double _tofTime;
        private float _batteryVoltage; private bool _batteryNew; private double _batteryTime;

        public AttemptState State { get; private set; } = AttemptState.Waiting;
        public string Status { get; private set; } = "waiting";
        public double SimTime => _tick * (double)_dt;
        public int Points => _referee != null ? _referee.Points : 0;
        public double LastScoreTime => _referee != null ? _referee.LastScoreTime : 0;
        public int GateCount => _gates.Count;
        public BenchRules Rules => _rules;
        public BenchTrack Track => _track;
        public bool ProgramConnected => _server != null && _server.Connected && _helloDone;
        public int Port => _server != null ? _server.Port : 0;
        public QuadVehicle Quad => quad;
        public Referee Referee => _referee;
        public string LoadError { get; private set; }
        /// <summary>The last frame handed to the program (RGB24, rows top-down), for the on-screen preview.</summary>
        public byte[] LastFrame { get; private set; }
        public long LastFrameSeq { get; private set; }
        public int CameraWidth => _rules.camera_width;
        public int CameraHeight => _rules.camera_height;
        /// <summary>True when the drone flies a measured model of the organisers' whoop; false for the placeholder.</summary>
        public bool DroneCalibrated => false;
        public string DroneName => "Whoop v0";

        /// <summary>False until the track is built and the API port is open.</summary>
        public bool Ready { get; private set; }

        // Start, not Awake: the quad's own Awake applies its default profile first, then the bench configures it.
        private void Start()
        {
            try
            {
                _rules = BenchLauncher.LoadRules();
                _track = BenchLauncher.LoadTrack(defaultTrack);
            }
            catch (Exception e)
            {
                LoadError = e.Message;
                Debug.LogError("[Bench] " + e.Message);
                enabled = false;
                return;
            }

            BuildTrack();
            SetupDrone();
            SetupClock();
            ResetWorld(_rules.seed);

            try
            {
                _server = new BenchApiServer(_rules.port, BenchLauncher.LoopbackOnly) { PingAllowed = !_rules.Competition };
                Debug.Log($"[Bench] track {_track.id} ({_gates.Count} gates), mode {_rules.mode}, API on port {_server.Port}");
                Ready = true;
            }
            catch (Exception e)
            {
                LoadError = $"port {_rules.port}: {e.Message}";
                Debug.LogError("[Bench] can't open the API port: " + e.Message);
                enabled = false;
            }
        }

        private Unity.Collections.NativeArray<byte> _processed;

        private void OnDestroy()
        {
            _server?.Dispose();
            if (_processed.IsCreated) _processed.Dispose();
        }

        // ---------- set-up ----------

        private void BuildTrack()
        {
            var refGates = new List<RefereeGate>();
            foreach (var g in _track.gates)
            {
                var gate = Instantiate(gatePrefab, BenchTrack.V(g.position), Quaternion.Euler(0f, g.yaw, 0f), gatesRoot);
                gate.name = "Gate " + g.id;
                gate.Id = g.id;
                gate.SetLed(BenchTrack.LedColor(g.led), g.led != null ? g.led.brightness : 1f, g.led != null && g.led.mode == "blink");
                _gates.Add(gate);
                refGates.Add(RefereeGate.At(g.id, CollisionHook.FromUnity(gate.OpeningCentre), CollisionHook.FromUnity(gate.transform.rotation),
                    g.direction == "front"));
            }
            int window = _track.rules != null && _track.rules.repeat_window >= 0 ? _track.rules.repeat_window : _rules.repeat_window;
            _referee = new Referee(refGates, window);
            _timeLimit = _track.rules != null && _track.rules.time_limit > 0f ? _track.rules.time_limit : _rules.time_limit;
            // Instantiated colliders must be known to the physics queries before the first step.
            Physics.SyncTransforms();
        }

        private void SetupDrone()
        {
            QuadProfile profile = QuadProfile.Whoop65;
            var collision = CollisionSettings.Default;
            collision.Radius = 0.045f;
            collision.TrackGeometry = solidLayers;
            // A ducted whoop bounces off things; it is never "destroyed and respawned" in an attempt.
            collision.CrashSpeed = 1e6f;
            collision.OutOfBoundsY = -1e6f;
            quad.InputSource = null;
            quad.ExternalStepping = true;
            quad.Configure(profile, collision);
            quad.SetBatterySag(true);

            // The pad pose: the drone's centre rests one collision radius above the floor.
            spawn.SetPositionAndRotation(BenchTrack.V(_track.start.position) + Vector3.up * (collision.Radius + 0.002f),
                Quaternion.Euler(0f, _track.start.yaw, 0f));

            var model = QuadModelBuilder.Build(QuadPresets.Whoop65, modelMaterials, quad.transform, quadLayer);
            foreach (var t in model.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = quadLayer;
            var props = quad.GetComponent<PropVisuals>();
            if (props != null) props.Configure(model.Props, model.BlurDiscs);

            var camGo = new GameObject("FPV Sensor");
            camGo.transform.SetParent(quad.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.012f, 0.028f);
            _camera = camGo.AddComponent<BenchCameraSensor>();
            _camera.Setup(_rules.camera_width, _rules.camera_height, _rules.camera_hfov, _rules.camera_tilt, 1 << quadLayer);
        }

        private void SetupClock()
        {
            _dt = quad.FixedStep;
            float physicsHz = 1f / _dt;
            _stepsPerControl = Mathf.Max(1, Mathf.RoundToInt(physicsHz / _rules.control_hz));
            _imuEvery = Mathf.Max(1, Mathf.RoundToInt(physicsHz / Mathf.Max(1f, _rules.imu_hz)));
            _tofEvery = Mathf.Max(1, Mathf.RoundToInt(physicsHz / Mathf.Max(1f, _rules.tof_hz)));
            _batteryEvery = Mathf.Max(1, Mathf.RoundToInt(physicsHz / Mathf.Max(1f, _rules.battery_hz)));
        }

        /// <summary>Everything back to the start pad, time zero, noise streams restarted from <paramref name="seed"/>.</summary>
        private void ResetWorld(long seed)
        {
            _tick = 0;
            _cameraFrames = 0;
            quad.Respawn();
            quad.SyncPose();
            _referee.Reset(quad.State.Position);
            _link = new RcLink(new RcLinkSettings
            {
                RateHz = _rules.control_hz, Latency = _rules.rc_latency_ms * 0.001f, FailsafeTime = _rules.rc_failsafe_ms * 0.001f,
            });
            var imu = ImuSettings.Default;
            imu.RateHz = 1f / (_imuEvery * _dt);
            _imu = new ImuModel(imu, seed);
            _tof = new TofModel(TofSettings.Default, seed);
            _video = new AnalogVideo(_rules.camera_analog ? AnalogVideoSettings.Default : AnalogVideoSettings.Clean, seed);
            _batteryNoise = BenchRandom.For(seed, "battery");
            while (_framesInFlight.Count > 0) _framePool.Push(_framesInFlight.Dequeue().Pixels);
            _imuBatch.Clear();
            _tofNew = _batteryNew = false;
            _stuckAnchor = quad.transform.position;
            _stuckSince = 0;
            foreach (var g in _gates) g.Tick(0);
        }

        // ---------- the loop ----------

        private void Update()
        {
            if (!Ready) return;
            if (_server.Connection != _connection || !_server.Connected) ConnectionChanged();

            // Sync mode paced to real time: simulator time may not run ahead of the wall clock.
            // While frames are being recorded at a fixed rate, one window frame is exactly one frame of simulator time.
            float frameTime = Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Time.unscaledDeltaTime;
            _paceCredit = Math.Min(_paceCredit + frameTime, 0.1);
            double controlPeriod = _stepsPerControl * (double)_dt;
            float deadline = Time.realtimeSinceStartup + 0.012f;

            while (true)
            {
                if (!_hasHeld)
                {
                    bool waitForStep = State == AttemptState.Running && (!_paced || _paceCredit >= controlPeriod);
                    int waitMs = waitForStep ? Mathf.Clamp(Mathf.CeilToInt((deadline - Time.realtimeSinceStartup) * 1000f), 0, 4) : 0;
                    if (!(waitMs > 0 ? _server.WaitDequeue(out _held, waitMs) : _server.TryDequeue(out _held))) break;
                    if (_held.Connection != _server.Connection) continue;
                    // A new program connected in the middle of this frame: forget the old one before its first message.
                    if (_held.Connection != _connection) ConnectionChanged();
                    _hasHeld = true;
                }

                if (_held.Type == BenchProtocol.Step && State == AttemptState.Running && _paced)
                {
                    if (_paceCredit < controlPeriod) break; // keep it for the next frame
                    _paceCredit -= controlPeriod;
                }
                _hasHeld = false;
                Handle(_held);
                if (Time.realtimeSinceStartup > deadline) break;
            }
        }

        private void ConnectionChanged()
        {
            bool had = _helloDone;
            _connection = _server.Connection;
            _helloDone = false;
            _hasHeld = false;
            _startedOnce = false;
            if (had && State == AttemptState.Running) End("disconnected");
            if (had && _server.LastCloseReason != null) Debug.Log("[Bench] connection closed: " + _server.LastCloseReason);
        }

        private void Handle(in BenchApiServer.Inbound m)
        {
            if (!_helloDone)
            {
                if (m.Type != BenchProtocol.Hello) { Refuse("hello_first", "send HELLO first"); return; }
                Hello(m.Header);
                return;
            }
            if (_rules.Competition && !BenchProtocol.AllowedInCompetition(m.Type)) { Refuse("not_allowed", "message not allowed in competition mode"); return; }

            switch (m.Type)
            {
                case BenchProtocol.Start:
                    if (_rules.Competition && _startedOnce) { Refuse("already_started", "the attempt has already started"); return; }
                    Begin(_rules.seed);
                    break;
                case BenchProtocol.Reset:
                    Begin(BenchMessages.Long(m.Header, "seed", _rules.seed));
                    break;
                case BenchProtocol.Step:
                    Step(m.Header);
                    break;
                case BenchProtocol.Done:
                    if (State == AttemptState.Running) End("finished");
                    SendObservation();
                    break;
                default:
                    Refuse("unknown_message", "unknown message type");
                    break;
            }
        }

        private void Refuse(string code, string text)
        {
            _header.Reset();
            _header.Map(2).Key("code").Str(code).Key("message").Str(text);
            _server.Send(_connection, BenchProtocol.Error, _header);
            _server.Close("refused: " + code);
        }

        private void Hello(Dictionary<string, object> h)
        {
            if (BenchMessages.Long(h, "proto", -1) != BenchProtocol.Version) { Refuse("protocol", $"this simulator speaks protocol {BenchProtocol.Version}"); return; }
            if (!string.IsNullOrEmpty(_rules.token) && BenchMessages.Str(h, "token", "") != _rules.token) { Refuse("token", "wrong token"); return; }
            _wantTruth = !_rules.Competition && BenchMessages.Bool(h, "truth", false);
            // Training: a program may ask to run as fast as the computer allows (e.g. for learning).
            _paced = _rules.pace_realtime && (_rules.Competition || BenchMessages.Bool(h, "pace", true));
            _helloDone = true;
            if (State == AttemptState.Running) End("disconnected");
            State = AttemptState.Waiting;
            Status = "waiting";

            float f = _camera.FocalPixels;
            _header.Reset();
            _header.Map(9)
                .Key("proto").Int(BenchProtocol.Version)
                .Key("mode").Str(_rules.Competition ? "competition" : "training")
                .Key("sim").Str(Application.version)
                .Key("time_mode").Str("sync")
                .Key("control_hz").Double(1.0 / (_stepsPerControl * (double)_dt))
                .Key("time_limit").Double(_timeLimit)
                .Key("drone").Map(3)
                    .Key("class").Str("ai_mission")
                    .Key("name").Str(DroneName)
                    .Key("calibrated").Bool(DroneCalibrated)
                .Key("rc").Map(4)
                    .Key("channels").Int(RcLink.Channels)
                    .Key("rate_hz").Double(_rules.control_hz)
                    .Key("latency_ms").Double(_rules.rc_latency_ms)
                    .Key("failsafe_ms").Double(_rules.rc_failsafe_ms)
                .Key("sensors").Map(4)
                    .Key("camera").Map(12)
                        .Key("width").Int(_rules.camera_width)
                        .Key("height").Int(_rules.camera_height)
                        .Key("fps").Double(_rules.camera_fps)
                        .Key("format").Str("rgb8")
                        .Key("hfov_deg").Double(_rules.camera_hfov)
                        .Key("tilt_deg").Double(_rules.camera_tilt)
                        .Key("latency_ms").Double(_rules.camera_latency_ms)
                        .Key("fx").Double(f).Key("fy").Double(f)
                        .Key("cx").Double(_rules.camera_width * 0.5).Key("cy").Double(_rules.camera_height * 0.5)
                        // Mount in the body frame (x forward, y left, z up), metres.
                        .Key("mount").Array(3).Double(_camera.MountPosition.z).Double(-_camera.MountPosition.x).Double(_camera.MountPosition.y)
                    .Key("imu").Map(2)
                        .Key("rate_hz").Double(1.0 / (_imuEvery * (double)_dt))
                        .Key("frame").Str("x forward, y left, z up")
                    .Key("tof").Map(4)
                        .Key("rate_hz").Double(1.0 / (_tofEvery * (double)_dt))
                        .Key("min_m").Double(TofSettings.Default.MinRange)
                        .Key("max_m").Double(TofSettings.Default.MaxRange)
                        .Key("direction").Str("down")
                    .Key("battery").Map(2)
                        .Key("rate_hz").Double(1.0 / (_batteryEvery * (double)_dt))
                        .Key("cells").Int(quad.Profile.Battery.Cells);
            _server.Send(_connection, BenchProtocol.Welcome, _header);
            Debug.Log("[Bench] program connected: " + BenchMessages.Str(h, "client", "unknown client"));
        }

        private void Begin(long seed)
        {
            ResetWorld(seed);
            State = AttemptState.Running;
            Status = "running";
            _startedOnce = true;
            _paceCredit = 0;
            // The first observation carries a picture taken at the start signal, with no delay.
            CaptureFrame();
            DeliverFrames(force: true);
            ReadTof();
            ReadBattery();
            SendObservation();
        }

        private void End(string status)
        {
            if (State != AttemptState.Running) return;
            State = AttemptState.Ended;
            Status = status;
            Debug.Log($"[Bench] attempt ended: {status}, points {Points}, time {LastScoreTime:F3} s (drone {DroneName}, calibrated={DroneCalibrated})");
        }

        private void Step(Dictionary<string, object> h)
        {
            if (State != AttemptState.Running)
            {
                SendObservation();
                return;
            }
            // A packet with anything but finite numbers is dropped whole; the link then sees a missing packet.
            if (BenchMessages.Channels(h, "rc", _channels)) _link.Send(SimTime, _channels);

            for (int i = 0; i < _stepsPerControl; i++)
            {
                PilotInput input = _link.Sample(SimTime);
                QuadState before = quad.State;
                quad.StepExternal(input);
                _tick++;
                ref readonly QuadState after = ref quad.State;
                _referee.Step(before.Position, after.Position, (_tick - 1) * (double)_dt, SimTime);
                if (_tick % _imuEvery == 0) _imuBatch.Add(_imu.Read(before, after, _dt, quad.Gravity));
            }
            quad.SyncPose();

            if (_tick % _tofEvery == 0) ReadTof();
            if (_tick % _batteryEvery == 0) ReadBattery();
            if (SimTime + 1e-9 >= _cameraFrames / (double)_rules.camera_fps)
            {
                foreach (var g in _gates) g.Tick(SimTime);
                CaptureFrame();
            }
            DeliverFrames(force: false);

            if (SimTime >= _timeLimit) End("time_limit");
            else if (_rules.stuck_time > 0f) CheckStuck();
            SendObservation();
        }

        private void CheckStuck()
        {
            if ((quad.transform.position - _stuckAnchor).sqrMagnitude > 0.05f * 0.05f || Points == 0 && SimTime < 15.0)
            {
                _stuckAnchor = quad.transform.position;
                _stuckSince = _tick;
            }
            else if ((_tick - _stuckSince) * (double)_dt >= _rules.stuck_time) End("stuck");
        }

        // ---------- sensors ----------

        private void ReadTof()
        {
            Transform t = quad.transform;
            float truth = Physics.Raycast(t.position, -t.up, out RaycastHit hit, TofSettings.Default.MaxRange + 1f, solidLayers, QueryTriggerInteraction.Ignore)
                ? hit.distance : -1f;
            _tofRange = _tof.Read(truth, out _tofValid);
            _tofTime = SimTime;
            _tofNew = true;
        }

        private void ReadBattery()
        {
            float v = quad.State.BatteryVoltage;
            if (v <= 0f) v = BatteryModel.CellOpenCircuit(1f) * quad.Profile.Battery.Cells;
            _batteryVoltage = v + _batteryNoise.NextGaussian() * 0.01f;
            _batteryTime = SimTime;
            _batteryNew = true;
        }

        /// <summary>Average cost of one camera frame on this computer (ms): rendering + read-back, and the analog pass.</summary>
        public float CaptureMs { get; private set; }
        public float VideoMs { get; private set; }
        private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();

        private void CaptureFrame()
        {
            _watch.Restart();
            Unity.Collections.NativeArray<byte> raw = _camera.Capture();
            float captured = (float)_watch.Elapsed.TotalMilliseconds;
            if (!_processed.IsCreated || _processed.Length != raw.Length)
            {
                if (_processed.IsCreated) _processed.Dispose();
                _processed = new Unity.Collections.NativeArray<byte>(raw.Length, Unity.Collections.Allocator.Persistent);
            }
            _video.Process(raw, _processed, _rules.camera_width, _rules.camera_height, _cameraFrames);
            byte[] pixels = _framePool.Count > 0 ? _framePool.Pop() : new byte[raw.Length];
            _processed.CopyTo(pixels);
            CaptureMs += (captured - CaptureMs) * 0.05f;
            VideoMs += ((float)_watch.Elapsed.TotalMilliseconds - captured - VideoMs) * 0.05f;
            _framesInFlight.Enqueue(new Frame { Time = SimTime, Seq = _cameraFrames, Pixels = pixels });
            _cameraFrames++;
        }

        private double _deliveredTime;
        private long _deliveredSeq;

        /// <summary>Hands over the newest frame whose delay has passed; older waiting frames are skipped.</summary>
        private void DeliverFrames(bool force)
        {
            double latency = _rules.camera_latency_ms * 0.001;
            while (_framesInFlight.Count > 0 && (force || _framesInFlight.Peek().Time + latency <= SimTime + 1e-9))
            {
                Frame f = _framesInFlight.Dequeue();
                if (_delivered != null && _delivered != LastFrame) _framePool.Push(_delivered);
                _delivered = f.Pixels;
                _deliveredTime = f.Time;
                _deliveredSeq = f.Seq;
            }
        }

        // ---------- observation ----------

        private void SendObservation()
        {
            int imuCount = _imuBatch.Count;
            int imuLength = imuCount * 28;
            if (_imuBytes.Length < imuLength) _imuBytes = new byte[imuLength * 2];
            for (int i = 0; i < imuCount; i++)
            {
                ImuSample s = _imuBatch[i];
                int o = i * 28;
                Put(_imuBytes, o, s.Time); Put(_imuBytes, o + 4, s.Gx); Put(_imuBytes, o + 8, s.Gy); Put(_imuBytes, o + 12, s.Gz);
                Put(_imuBytes, o + 16, s.Ax); Put(_imuBytes, o + 20, s.Ay); Put(_imuBytes, o + 24, s.Az);
            }
            _imuBatch.Clear();

            bool camera = _delivered != null;
            bool truth = _wantTruth && !_rules.Competition;
            int fields = 5 + (camera ? 1 : 0) + (_tofNew ? 1 : 0) + (_batteryNew ? 1 : 0) + (truth ? 1 : 0);
            _header.Reset();
            _header.Map(fields)
                .Key("t").Double(SimTime)
                .Key("tick").Int(_tick)
                .Key("running").Bool(State == AttemptState.Running)
                .Key("status").Str(Status)
                .Key("imu_n").Int(imuCount);
            if (_tofNew) _header.Key("tof").Map(3).Key("t").Double(_tofTime).Key("range").Double(_tofRange).Key("valid").Bool(_tofValid);
            if (_batteryNew) _header.Key("battery").Map(2).Key("t").Double(_batteryTime).Key("voltage").Double(_batteryVoltage);
            if (camera) _header.Key("camera").Map(3).Key("t").Double(_deliveredTime).Key("seq").Int(_deliveredSeq).Key("size").Int(_delivered.Length);
#if !USHQYR_BENCH_SERVER
            if (truth) GroundTruth.Write(_header, this);
#endif
            _tofNew = _batteryNew = false;

            _server.Send(_connection, BenchProtocol.Observation, _header, _imuBytes, imuLength, camera ? _delivered : null, camera ? _delivered.Length : 0);
            if (camera)
            {
                // The preview keeps showing this frame; its buffer returns to the pool when the next one replaces it.
                if (LastFrame != null && LastFrame != _delivered) _framePool.Push(LastFrame);
                LastFrame = _delivered;
                LastFrameSeq = _deliveredSeq;
                _delivered = null;
            }
        }

        private static void Put(byte[] b, int at, float v)
        {
            int i = BitConverter.SingleToInt32Bits(v);
            b[at] = (byte)i; b[at + 1] = (byte)(i >> 8); b[at + 2] = (byte)(i >> 16); b[at + 3] = (byte)(i >> 24);
        }
    }

#if !USHQYR_BENCH_SERVER
    /// <summary>
    /// Ground truth for training mode (the drone's true pose and the score). Compiled out of the Bench Server build with
    /// USHQYR_BENCH_SERVER, and never called in competition mode.
    /// </summary>
    internal static class GroundTruth
    {
        public static void Write(MsgPackWriter w, BenchSession s)
        {
            ref readonly QuadState q = ref s.Quad.State;
            // Position and velocity in a right-handed frame: x east (Unity x), y north (Unity z), z up.
            w.Key("truth").Map(4)
                .Key("position").Array(3).Double(q.Position.x).Double(q.Position.z).Double(q.Position.y)
                .Key("velocity").Array(3).Double(q.Velocity.x).Double(q.Velocity.z).Double(q.Velocity.y)
                .Key("points").Int(s.Points)
                .Key("score_time").Double(s.LastScoreTime);
        }
    }
#endif
}
