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
        [Tooltip("Optional: the camera that films the trackside video.")]
        [SerializeField] private BenchTracksideCamera trackside;

        // Recording and the attempt's life cycle. Wall-clock time is used only for the timeouts on the program,
        // never inside the simulation.
        private BenchRecorder _recorder;
        private int _eventsLogged, _impactsSeen;
        private bool _failsafeWas, _rcOk, _attemptOver;
        private ImuSample _lastImu;
        private float _wallStart, _lastStepWall;
        private byte[] _signingKey;
        private string _ffmpeg;

        /// <summary>The finished attempt's result, once its files are written.</summary>
        public AttemptResult LastResult { get; private set; }
        /// <summary>The attempt folder being written or last written; null when not recording.</summary>
        public string AttemptFolder => _recorder != null ? _recorder.Folder : null;
        public bool WritingFiles => _recorder != null && _recorder.Finishing;
        public IReadOnlyList<BenchGate> Gates => _gates;
        /// <summary>True when the scene shows a recorded attempt instead of running one (judge replay).</summary>
        public bool ReplayMode { get; private set; }
        /// <summary>True when the scene is the track editor's: no attempt, no API; the editor owns the gates.</summary>
        public bool EditMode { get; private set; }
        public BenchGate GatePrefab => gatePrefab;
        public Transform Spawn => spawn;
        public LayerMask SolidLayers => solidLayers;
        /// <summary>The editor replaced the whole track (generator).</summary>
        public void SetEditedTrack(BenchTrack track) => _track = track;

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
        public bool DroneCalibrated => _drone != null;
        public string DroneName => _drone != null ? _drone.Name : "Whoop v0";
        /// <summary>"ai_mission" (the organisers' whoop) or "ai_core" (a team's drone from its config).</summary>
        public string DroneClass => _drone != null ? "ai_core" : "ai_mission";
        private DroneConfig _drone;
        private bool _tofFitted = true;

        /// <summary>False until the track is built and the API port is open.</summary>
        public bool Ready { get; private set; }

        // Start, not Awake: the quad's own Awake applies its default profile first, then the bench configures it.
        private void Start()
        {
            try
            {
                _rules = BenchLauncher.LoadRules();
                ReplayMode = !string.IsNullOrEmpty(BenchLauncher.ReplayFolder);
                EditMode = !ReplayMode && !string.IsNullOrEmpty(BenchLauncher.EditPath);
                _track = ReplayMode ? BenchLauncher.LoadReplayTrack()
                    : EditMode ? BenchTrack.Parse(File.ReadAllText(BenchLauncher.EditPath)) : BenchLauncher.LoadTrack(defaultTrack);
                _signingKey = BenchLauncher.SigningKey();
                _drone = ReplayMode || EditMode ? null : BenchLauncher.LoadDrone();
                if (_drone != null)
                {
                    // The team's file decides the camera's mounting and which sensors exist at all.
                    _rules.camera_tilt = _drone.CameraTiltDeg;
                    _rules.camera_hfov = _drone.CameraHfovDeg;
                    _rules.lidar = _rules.lidar && _drone.Has("lidar");
                    _tofFitted = _drone.Has("tof");
                }
            }
            catch (Exception e)
            {
                LoadError = e.Message;
                Debug.LogError("[Bench] " + e.Message);
                enabled = false;
                return;
            }

            if (EditMode)
            {
                // The editor stands the gates up itself and rebuilds them as they change.
                _referee = new Referee(new List<RefereeGate>());
                SetupDrone();
                Status = "edit";
                return;
            }
            BuildTrack();
            SetupDrone();
            SetupClock();
            ResetWorld(_rules.seed);
            if (ReplayMode)
            {
                // The replay component drives the drone from the recorded trajectory; no API, no attempt.
                Status = "replay";
                return;
            }
#if !USHQYR_BENCH_SERVER
            if (BenchLauncher.Dataset.HasValue && !_rules.Competition)
            {
                // Dataset mode: no API and no attempt; the generator moves the camera and writes images and labels.
                Status = "dataset";
                Dataset = gameObject.AddComponent<BenchDataset>();
                Dataset.Run(this, BenchLauncher.Dataset.Value);
                return;
            }
#endif
            bool video = _rules.video == "on" || (_rules.video != "off" && _rules.Competition);
            _ffmpeg = video ? BenchLauncher.FindFfmpeg() : null;
            if (video && _ffmpeg == null) Debug.LogWarning("[Bench] no ffmpeg found: attempts are recorded without video");
            _wallStart = Time.realtimeSinceStartup;

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
            BuildProps(_track.props, gatesRoot, gatePrefab);
            var lights = FindAnyObjectByType<BenchVenueLights>();
            if (lights != null) lights.Apply(_track.lighting);
            _flown = _track;
            int window = _track.rules != null && _track.rules.repeat_window >= 0 ? _track.rules.repeat_window : _rules.repeat_window;
            _referee = new Referee(refGates, window);
            _timeLimit = _track.rules != null && _track.rules.time_limit > 0f ? _track.rules.time_limit : _rules.time_limit;
            // Instantiated colliders must be known to the physics queries before the first step.
            Physics.SyncTransforms();
        }

        /// <summary>Coloured panels and boxes of the track file: solid, painted or glowing.</summary>
        public static void BuildProps(BenchTrack track, Transform parent, BenchGate gatePrefab) => BuildProps(track.props, parent, gatePrefab);

#if !USHQYR_BENCH_SERVER
        // ---------- dataset ----------

        /// <summary>The dataset generator at work in this scene, or null.</summary>
        public BenchDataset Dataset { get; private set; }
        private bool _datasetReported;
        public Transform CameraTransform => _camera.transform;
        public float CameraFocal => _camera.FocalPixels;
        public float CameraDistortion => _camera.Distortion;

        /// <summary>Colour that marks gate <paramref name="index"/> in the flat-colour render.</summary>
        private static Color32 IdColour(int index) => new Color32((byte)(40 + 24 * (index % 8)), (byte)(40 + 24 * (index / 8)), 255, 255);

        /// <summary>
        /// One dataset image from the drone's current pose, through the same lens and video link as a run
        /// (rows top-down), and the matching mask: 0 = no gate, n = gate number n (1-based) in track order.
        /// </summary>
        public void RenderDatasetFrame(long index, float signal, float gain, byte[] frame, byte[] mask)
        {
            quad.SyncPose();
            int w = _rules.camera_width, h = _rules.camera_height;
            if (!_processed.IsCreated || _processed.Length != w * h * 3)
            {
                if (_processed.IsCreated) _processed.Dispose();
                _processed = new Unity.Collections.NativeArray<byte>(w * h * 3, Unity.Collections.Allocator.Persistent);
            }
            Unity.Collections.NativeArray<byte> raw = _camera.Capture();
            _video.Process(raw, _processed, _camera.LensMap, w, h, index, signal, gain);
            _processed.CopyTo(frame);

            // The same view with the lights out and every gate glowing in its own colour.
            var lights = FindAnyObjectByType<BenchVenueLights>();
            if (lights != null) lights.Dark(true);
            for (int i = 0; i < _gates.Count; i++) _gates[i].Paint(IdColour(i));
            raw = _camera.Capture();
            Unity.Collections.NativeArray<int> map = _camera.LensMap;
            for (int p = 0; p < mask.Length; p++)
            {
                int s = map[p];
                int r = raw[s], g = raw[s + 1], b = raw[s + 2];
                int col = Mathf.RoundToInt((r - 40) / 24f), row = Mathf.RoundToInt((g - 40) / 24f);
                bool gate = b > 235 && col >= 0 && col < 8 && row >= 0 && Mathf.Abs(r - (40 + 24 * col)) <= 9 && Mathf.Abs(g - (40 + 24 * row)) <= 9
                            && row * 8 + col < _gates.Count;
                mask[p] = gate ? (byte)(row * 8 + col + 1) : (byte)0;
            }
            foreach (BenchGate g in _gates) g.Unpaint();
            if (lights != null) lights.Dark(false);
        }
#endif

        // ---------- randomisation ----------

        private BenchTrack _flown;
        private Transform _clutter;
        private float _noise = 1f;

        /// <summary>The track as it stands in the scene right now: the file's track, or this attempt's variation of it.</summary>
        public BenchTrack FlownTrack => _flown ?? _track;

        /// <summary>
        /// Puts a drawn variation into the scene: gates and pad moved, LEDs, lights, surfaces, clutter, video noise.
        /// The referee is rebuilt for the gates' new places.
        /// </summary>
        public void ApplyVariation(SceneVariation v)
        {
            _flown = v.Track;
            var refGates = new List<RefereeGate>();
            for (int i = 0; i < _gates.Count; i++)
            {
                TrackGate g = v.Track.gates[i];
                BenchGate gate = _gates[i];
                gate.transform.SetPositionAndRotation(BenchTrack.V(g.position), Quaternion.Euler(0f, g.yaw, 0f));
                gate.SetLed(BenchTrack.LedColor(g.led), g.led.brightness, g.led.mode == "blink");
                refGates.Add(RefereeGate.At(g.id, CollisionHook.FromUnity(gate.OpeningCentre), CollisionHook.FromUnity(gate.transform.rotation),
                    g.direction == "front"));
            }
            _referee = new Referee(refGates, _referee.RepeatWindow);
            spawn.SetPositionAndRotation(BenchTrack.V(v.Track.start.position) + Vector3.up * (quad.BaseCollisionRadius + 0.002f),
                Quaternion.Euler(0f, v.Track.start.yaw, 0f));

            if (_clutter != null) Destroy(_clutter.gameObject);
            _clutter = new GameObject("Clutter").transform;
            _clutter.SetParent(gatesRoot, false);
            BuildProps(v.Clutter, _clutter, gatePrefab);
            var lights = FindAnyObjectByType<BenchVenueLights>();
            if (lights != null) lights.Vary(_track.lighting, v.Light, v.LightColour, v.LightYaw);
            var look = FindAnyObjectByType<BenchVenueLook>();
            if (look != null) look.Apply(v.FloorMaterial, v.WallMaterial);
            _noise = v.Noise;
            Physics.SyncTransforms();
        }

        /// <summary>This attempt's variation of the track, from the rules' randomisation profile and the seed.</summary>
        public SceneVariation DrawVariation(string profile, long seed)
        {
            var look = FindAnyObjectByType<BenchVenueLook>();
            return TrackRandomizer.Draw(_track, RandomizationProfile.Get(profile), seed, look != null ? look.FloorChoices : 1, look != null ? look.WallChoices : 1);
        }

        public static void BuildProps(List<TrackProp> list, Transform parent, BenchGate gatePrefab)
        {
            if (list == null) return;
            Renderer led = null;
            foreach (Renderer r in gatePrefab.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("LED")) { led = r; break; }
            foreach (TrackProp p in list)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Prop " + p.id;
                go.layer = gatePrefab.transform.GetChild(0).gameObject.layer;
                go.transform.SetParent(parent, false);
                var size = new Vector3(p.size[0], p.size[1], p.size[2]);
                go.transform.SetPositionAndRotation(BenchTrack.V(p.position) + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, p.yaw, 0f));
                go.transform.localScale = size;
                var renderer = go.GetComponent<Renderer>();
                if (led != null) renderer.sharedMaterial = led.sharedMaterial;
                ColorUtility.TryParseHtmlString(p.color, out Color c);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", c);
                block.SetColor("_EmissionColor", p.glow ? c * 2.2f : Color.black);
                renderer.SetPropertyBlock(block);
            }
        }

        private void SetupDrone()
        {
            QuadProfile profile = _drone != null ? _drone.ToProfile() : QuadProfile.Whoop65;
            var collision = CollisionSettings.Default;
            collision.Radius = _drone != null ? _drone.CollisionRadius : 0.045f;
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

            var props = quad.GetComponent<PropVisuals>();
            if (_drone == null)
            {
                var model = QuadModelBuilder.Build(QuadPresets.Whoop65, modelMaterials, quad.transform, quadLayer);
                foreach (var t in model.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = quadLayer;
                if (props != null) props.Configure(model.Props, model.BlurDiscs);
            }
            else BuildConfigDrone(profile, props);

            var camGo = new GameObject("FPV Sensor");
            camGo.transform.SetParent(quad.transform, false);
            camGo.transform.localPosition = _drone != null ? new Vector3(0f, _drone.HeightMm * 0.00025f, _drone.LengthMm * 0.0003f) : new Vector3(0f, 0.012f, 0.028f);
            _camera = camGo.AddComponent<BenchCameraSensor>();
            _camera.Setup(_rules.camera_width, _rules.camera_height, _rules.camera_hfov, _rules.camera_tilt, 1 << quadLayer,
                _rules.camera_distortion);
        }

        /// <summary>The team drone as declared: a box of its size, four arms, four propellers. Plain shapes, true scale.</summary>
        private void BuildConfigDrone(in QuadProfile profile, PropVisuals props)
        {
            float w = _drone.WidthMm * 0.001f, l = _drone.LengthMm * 0.001f, h = _drone.HeightMm * 0.001f;
            float prop = _drone.PropDiameterIn * 0.0254f;
            var root = new GameObject("Drone: " + _drone.Name).transform;
            root.SetParent(quad.transform, false);
            Transform Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
            {
                var go = GameObject.CreatePrimitive(type);
                Destroy(go.GetComponent<Collider>());
                go.name = name;
                go.layer = quadLayer;
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = rot;
                go.transform.localScale = scale;
                if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
                return go.transform;
            }
            Part(PrimitiveType.Cube, "Body", Vector3.zero, new Vector3(w * 0.4f, h * 0.7f, l * 0.6f), Quaternion.identity, modelMaterials.Carbon);
            var blades = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                Vec3 m = Mixer.MotorPosition(i, profile.ArmX, profile.ArmZ);
                var at = new Vector3(m.x, 0f, m.z);
                Part(PrimitiveType.Cube, "Arm " + i, at * 0.5f, new Vector3(0.012f, 0.006f, at.magnitude), Quaternion.LookRotation(at.normalized), modelMaterials.Carbon);
                Part(PrimitiveType.Cylinder, "Motor " + i, at + Vector3.up * (h * 0.15f), new Vector3(prop * 0.3f, h * 0.2f, prop * 0.3f), Quaternion.identity, modelMaterials.Bell);
                blades[i] = Part(PrimitiveType.Cylinder, "Prop " + i, at + Vector3.up * (h * 0.36f), new Vector3(prop, 0.0015f, prop), Quaternion.identity, modelMaterials.Prop);
            }
            if (props != null) props.Configure(blades, new Renderer[0]);
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
                RateHz = _rules.control_hz, FailsafeTime = _rules.rc_failsafe_ms * 0.001f,
                // The program's own thinking time is modelled as a fixed extra delay of its commands.
                Latency = (_rules.rc_latency_ms + _rules.compute_delay_ms) * 0.001f,
                Jitter = _rules.rc_jitter_ms * 0.001f, Loss = _rules.rc_loss_percent * 0.01f,
            }, seed);
            var lidar = LidarSettings.Default;
            lidar.RateHz = _rules.lidar_hz;
            _lidar = new LidarModel(lidar, seed);
            _lidarRays = lidar.Rays;
            _lidarMax = lidar.MaxRange;
            if (_lidarScan == null || _lidarScan.Length != lidar.Rays) _lidarScan = new float[lidar.Rays];
            _lidarNext = 0;
            _lidarCarry = 0f;
            _lidarNew = false;
            _exposure = 1f;
            _signal = 1f;
            var imu = ImuSettings.Default;
            imu.RateHz = 1f / (_imuEvery * _dt);
            _imu = new ImuModel(imu, seed);
            _tof = new TofModel(TofSettings.Default, seed);
            _video = new AnalogVideo(_rules.camera_analog ? AnalogVideoSettings.Default.Scaled(_noise) : AnalogVideoSettings.Clean, seed);
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
#if !USHQYR_BENCH_SERVER
            if (Dataset != null && Dataset.Finished && !_datasetReported)
            {
                _datasetReported = true;
                BenchLauncher.DatasetDone();
                if (BenchLauncher.ExitWhenDone) Application.Quit(Dataset.Error == null ? 0 : 1);
            }
#endif
            if (!Ready) return;
            if (_server.Connection != _connection || !_server.Connected) ConnectionChanged();
            Lifecycle();

            // Sync mode paced to real time: simulator time may not run ahead of the wall clock.
            // While frames are being recorded at a fixed rate, one window frame is exactly one frame of simulator time.
            float frameTime = Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Time.unscaledDeltaTime;
            _paceCredit = Math.Min(_paceCredit + frameTime, 0.1);
            double controlPeriod = _stepsPerControl * (double)_dt;
            float deadline = Time.realtimeSinceStartup + 0.012f;

            if (_rules.Realtime)
            {
                // Realtime mode: the simulation follows the wall clock whatever the program does. Its packets are
                // taken as they come; an observation goes out after every control period.
                while (_server.TryDequeue(out _held))
                {
                    if (_held.Connection != _server.Connection) continue;
                    if (_held.Connection != _connection) ConnectionChanged();
                    Handle(_held);
                }
                if (State != AttemptState.Running) _paceCredit = 0;
                while (State == AttemptState.Running && _paceCredit >= controlPeriod)
                {
                    _paceCredit -= controlPeriod;
                    Advance();
                    SendObservation();
                }
                return;
            }

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

        /// <summary>Competition timeouts on the program, the finished result, and quitting after a server run.</summary>
        private void Lifecycle()
        {
            float now = Time.realtimeSinceStartup;
            if (_rules.Competition && !_attemptOver)
            {
                if (State == AttemptState.Waiting && _rules.connect_timeout > 0f && now - _wallStart > _rules.connect_timeout)
                    EndWithoutStart("timeout", "the program did not connect and start in time");
                else if (State == AttemptState.Running && !_rules.Realtime && _rules.step_timeout > 0f && now - _lastStepWall > _rules.step_timeout)
                {
                    End("timeout");
                    _server.Close("step timeout");
                }
            }
            if (_recorder != null && _recorder.Finished && LastResult != _recorder.Result)
            {
                LastResult = _recorder.Result;
                Debug.Log($"[Bench] attempt files written: {_recorder.Folder}");
            }
            if (BenchLauncher.ExitWhenDone && State == AttemptState.Ended && (_recorder == null || _recorder.Finished)) Application.Quit(0);
        }

        private bool Recording => _rules.record == "on" || (_rules.record != "off" && (_rules.Competition || BenchLauncher.OutputGiven));

        private void StartRecorder(long seed)
        {
            _recorder = null;
            if (!Recording) return;
            ref readonly QuadProfile p = ref quad.Profile;
            var info = new AttemptResult
            {
                team = BenchLauncher.Team, track_id = _track.id, track_hash = _track.Hash, attempt = BenchLauncher.Attempt,
                mode = _rules.Competition ? "competition" : "training", seed = seed, time_mode = _rules.Realtime ? "realtime" : "sync",
                build = Application.version + "+" + Application.buildGUID,
                rules_hash = _rules.Hash(), drone = DroneName, drone_calibrated = DroneCalibrated,
                // A team drone is identified by its file; the built-in whoop by the numbers that define it.
                drone_config_hash = _drone != null ? _drone.Hash : BenchHash.Sha256(FormattableString.Invariant(
                    $"{DroneName}|{p.Mass}|{p.Motors.MaxThrustPerMotor}|{p.ArmX}|{p.ArmZ}|{p.Battery.Cells}|{p.Battery.CapacityMah}")),
                solution_digest = BenchLauncher.SolutionDigest, gates_total = _gates.Count,
            };
            try
            {
                bool film = _ffmpeg != null && trackside != null;
                if (film) trackside.Setup(_rules.trackside_width, _rules.trackside_height);
                // The attempt folder gets the track as it was flown (with this attempt's variation), for the replay.
                string flown = _flown != null && _flown != _track ? _flown.ToJson() + "\n" : _track.Json;
                _recorder = new BenchRecorder(BenchLauncher.OutputFolder, info, flown, _referee.RepeatWindow, _signingKey, _ffmpeg,
                    _rules.camera_width, _rules.camera_height, _rules.camera_fps, film ? _rules.trackside_width : 0, _rules.trackside_height);
            }
            catch (Exception e)
            {
                Debug.LogError("[Bench] can't record the attempt: " + e.Message);
                _recorder = null;
            }
            _eventsLogged = 0;
            _impactsSeen = quad.ImpactCount;
            _failsafeWas = false;
        }

        /// <summary>The attempt is over before it began (competition): still a result, with zero points.</summary>
        private void EndWithoutStart(string status, string why)
        {
            if (_attemptOver) return;
            StartRecorder(_rules.seed);
            State = AttemptState.Ended;
            Status = status;
            _attemptOver = true;
            Debug.Log($"[Bench] attempt ended before the start: {status} ({why})");
            if (_recorder == null) return;
            _recorder.Note(0, "end", status + ": " + why);
            _recorder.Finish(0, 0, 0, status, 0);
        }

        private void ConnectionChanged()
        {
            bool had = _helloDone;
            if (had && _rules.Competition && State == AttemptState.Waiting) EndWithoutStart("disconnected", "the program left before the start");
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
            if (_rules.Competition && _attemptOver) { Refuse("attempt_over", "this attempt is over"); return; }
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
            // Training tells which track is loaded (tools check it); in competition the track stays unnamed.
            _header.Map(_rules.Competition ? 9 : 10);
            if (!_rules.Competition) _header.Key("track").Map(2).Key("id").Str(_track.id).Key("hash").Str(_track.Hash);
            _header
                .Key("proto").Int(BenchProtocol.Version)
                .Key("mode").Str(_rules.Competition ? "competition" : "training")
                .Key("sim").Str(Application.version)
                .Key("time_mode").Str(_rules.Realtime ? "realtime" : "sync")
                .Key("control_hz").Double(1.0 / (_stepsPerControl * (double)_dt))
                .Key("time_limit").Double(_timeLimit)
                .Key("drone").Map(3)
                    .Key("class").Str(DroneClass)
                    .Key("name").Str(DroneName)
                    .Key("calibrated").Bool(DroneCalibrated)
                .Key("rc").Map(4)
                    .Key("channels").Int(RcLink.Channels)
                    .Key("rate_hz").Double(_rules.control_hz)
                    .Key("latency_ms").Double(_rules.rc_latency_ms)
                    .Key("failsafe_ms").Double(_rules.rc_failsafe_ms)
                .Key("sensors").Map(_rules.lidar ? 5 : 4)
                    .Key("camera").Map(13)
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
                        // x_u = x_d * (1 + k1 * r_d^2): x_d = (u - cx) / fx is the picture, x_u the tangent of the true angle.
                        .Key("distortion").Map(2).Key("model").Str("radial_k1").Key("k1").Double(_camera.Distortion)
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
            if (_rules.lidar)
                _header.Key("lidar").Map(6)
                    .Key("rate_hz").Double(_rules.lidar_hz)
                    .Key("rays").Int(_lidarRays)
                    .Key("min_m").Double(LidarSettings.Default.MinRange)
                    .Key("max_m").Double(_lidarMax)
                    .Key("angle_step_deg").Double(360.0 / _lidarRays)
                    .Key("frame").Str("ray k at angle k * angle_step from forward, turning left; 0 = nothing measured");
            _server.Send(_connection, BenchProtocol.Welcome, _header);
            Debug.Log("[Bench] program connected: " + BenchMessages.Str(h, "client", "unknown client"));
        }

        private void Begin(long seed)
        {
            // A restart in training: the unfinished attempt before it is closed as "invalid" and doesn't count.
            if (State == AttemptState.Running) End("invalid");
            if (RandomizationProfile.Get(_rules.randomization).Any) ApplyVariation(DrawVariation(_rules.randomization, seed));
            ResetWorld(seed);
            StartRecorder(seed);
            if (_rules.randomization != "none") _recorder?.Note(0, "randomization", _rules.randomization + ", seed " + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _recorder?.Note(0, "start", "start signal");
            State = AttemptState.Running;
            Status = "running";
            _startedOnce = true;
            _paceCredit = 0;
            _lastStepWall = Time.realtimeSinceStartup;
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
            _attemptOver = true;
            if (_recorder != null)
            {
                _recorder.Note(SimTime, "end", status);
                _recorder.Finish(Points, LastScoreTime, SimTime, status, _referee.Events.Count);
            }
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
            _lastStepWall = Time.realtimeSinceStartup;
            _rcOk = BenchMessages.Channels(h, "rc", _channels);
            if (_rcOk) _link.Send(SimTime, _channels);
            // In realtime mode a packet only reaches the link; the clock, not the packet, moves the simulation.
            if (_rules.Realtime) return;
            Advance();
            SendObservation();
        }

        /// <summary>One control period: physics steps, referee, sensors, logs, end conditions.</summary>
        private void Advance()
        {

            for (int i = 0; i < _stepsPerControl; i++)
            {
                PilotInput input = _link.Sample(SimTime);
                QuadState before = quad.State;
                quad.StepExternal(input);
                _tick++;
                ref readonly QuadState after = ref quad.State;
                _referee.Step(before.Position, after.Position, (_tick - 1) * (double)_dt, SimTime);
                if (_tick % _imuEvery == 0)
                {
                    _lastImu = _imu.Read(before, after, _dt, quad.Gravity);
                    _imuBatch.Add(_lastImu);
                }
            }
            quad.SyncPose();

            SweepLidar();
            if (_tick % _tofEvery == 0) ReadTof();
            if (_tick % _batteryEvery == 0) ReadBattery();
            if (SimTime + 1e-9 >= _cameraFrames / (double)_rules.camera_fps)
            {
                foreach (var g in _gates) g.Tick(SimTime);
                CaptureFrame();
                if (_recorder != null && _recorder.Trackside)
                {
                    byte[] film = trackside.Capture(quad.transform.position, SimTime);
                    _recorder.TracksideFrame(film, film.Length);
                }
            }
            DeliverFrames(force: false);
            Record();

            if (SimTime >= _timeLimit) End("time_limit");
            else if (_rules.stuck_time > 0f) CheckStuck();
            _rcOk = false;
        }

        /// <summary>This step's line in the logs, and any events since the last one.</summary>
        private void Record()
        {
            if (_recorder == null) return;
            _recorder.Step(_tick, SimTime, _rcOk ? _channels : null, quad.State.Armed, _link.Failsafe, _lastImu, _tofRange, _tofValid,
                _batteryVoltage, _cameraFrames - 1, quad.State);
            for (; _eventsLogged < _referee.Events.Count; _eventsLogged++) _recorder.Gate(_referee.Events[_eventsLogged]);
            if (quad.ImpactCount != _impactsSeen)
            {
                _impactsSeen = quad.ImpactCount;
                _recorder.Note(SimTime, "collision", FormattableString.Invariant($"impact {quad.LastImpactSpeed:F1} m/s"));
            }
            if (_link.Failsafe != _failsafeWas)
            {
                _failsafeWas = _link.Failsafe;
                _recorder.Note(SimTime, _failsafeWas ? "failsafe" : "failsafe_end", _failsafeWas ? "no RC packet in time: motors cut" : "RC packets again");
            }
        }

        // ---------- LiDAR ----------

        private LidarModel _lidar;
        private float[] _lidarScan, _lidarSent;
        private int _lidarRays, _lidarNext;
        private float _lidarMax, _lidarCarry;
        private bool _lidarNew;
        private double _lidarTime;

        /// <summary>
        /// The scanner turns while the drone flies: every control step casts the rays of that slice of the turn from
        /// the pose at that moment. A full turn becomes one scan.
        /// </summary>
        private void SweepLidar()
        {
            if (!_rules.lidar) return;
            _lidarCarry += _lidarRays * _rules.lidar_hz * _stepsPerControl * _dt;
            int count = (int)_lidarCarry;
            _lidarCarry -= count;
            Transform t = quad.transform;
            Vector3 origin = t.position;
            for (int i = 0; i < count; i++)
            {
                Vec3 d = LidarModel.Direction(_lidarNext, _lidarRays);
                Vector3 dir = t.rotation * new Vector3(d.x, d.y, d.z);
                float truth = Physics.Raycast(origin, dir, out RaycastHit hit, _lidarMax + 1f, solidLayers, QueryTriggerInteraction.Ignore) ? hit.distance : -1f;
                _lidarScan[_lidarNext] = _lidar.Read(truth);
                if (++_lidarNext < _lidarRays) continue;
                _lidarNext = 0;
                if (_lidarSent == null || _lidarSent.Length != _lidarRays) _lidarSent = new float[_lidarRays];
                Array.Copy(_lidarScan, _lidarSent, _lidarRays);
                _lidarNew = true;
                _lidarTime = SimTime;
            }
        }

        // ---------- video link ----------

        private float _exposure = 1f, _signal = 1f;

        /// <summary>
        /// Quality of the analog video link, 1 = perfect: it falls with the distance from the receiver (which stands at
        /// the start pad) and when something solid is between the two.
        /// </summary>
        private float SignalQuality()
        {
            Vector3 receiver = spawn.position + Vector3.up * 1.2f;
            Vector3 to = quad.transform.position - receiver;
            float distance = to.magnitude;
            float q = 1f - Mathf.Pow(Mathf.Clamp01(distance / Mathf.Max(1f, _rules.video_range)), 2f);
            if (distance > 0.5f && Physics.Raycast(receiver, to / distance, distance - 0.2f, solidLayers, QueryTriggerInteraction.Ignore)) q *= 0.55f;
            _signal += (q - _signal) * 0.35f;
            return _signal;
        }

        /// <summary>Exposure follows the brightness of the last frame, slowly, like a camera's own control loop.</summary>
        private void UpdateExposure()
        {
            if (!_rules.camera_auto_exposure) return;
            long sum = 0;
            int n = 0;
            for (int i = 0; i < _processed.Length; i += 3 * 37, n++) sum += 77 * _processed[i] + 150 * _processed[i + 1] + 29 * _processed[i + 2];
            float mean = n > 0 ? sum / (256f * n) : 110f;
            float wanted = Mathf.Clamp(_exposure * 105f / Mathf.Max(8f, mean), 0.6f, 2.5f);
            _exposure += (wanted - _exposure) * 0.08f;
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
            if (!_tofFitted) return;
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
            int frameBytes = _rules.camera_width * _rules.camera_height * 3;
            if (!_processed.IsCreated || _processed.Length != frameBytes)
            {
                if (_processed.IsCreated) _processed.Dispose();
                _processed = new Unity.Collections.NativeArray<byte>(frameBytes, Unity.Collections.Allocator.Persistent);
            }
            _video.Process(raw, _processed, _camera.LensMap, _rules.camera_width, _rules.camera_height, _cameraFrames,
                _rules.camera_analog ? SignalQuality() : 1f, _exposure);
            UpdateExposure();
            byte[] pixels = _framePool.Count > 0 ? _framePool.Pop() : new byte[frameBytes];
            _processed.CopyTo(pixels);
            _recorder?.OnboardFrame(pixels, pixels.Length);
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
            // The scan travels in the same buffer, right after the IMU samples.
            int lidarLength = _lidarNew ? _lidarRays * 4 : 0;
            if (_lidarNew)
            {
                if (_imuBytes.Length < imuLength + lidarLength) Array.Resize(ref _imuBytes, (imuLength + lidarLength) * 2);
                for (int i = 0; i < _lidarRays; i++) Put(_imuBytes, imuLength + i * 4, _lidarSent[i]);
            }
            int fields = 5 + (camera ? 1 : 0) + (_tofNew ? 1 : 0) + (_batteryNew ? 1 : 0) + (truth ? 1 : 0) + (_lidarNew ? 1 : 0);
            _header.Reset();
            _header.Map(fields)
                .Key("t").Double(SimTime)
                .Key("tick").Int(_tick)
                .Key("running").Bool(State == AttemptState.Running)
                .Key("status").Str(Status)
                .Key("imu_n").Int(imuCount);
            if (_tofNew) _header.Key("tof").Map(3).Key("t").Double(_tofTime).Key("range").Double(_tofRange).Key("valid").Bool(_tofValid);
            if (_batteryNew) _header.Key("battery").Map(2).Key("t").Double(_batteryTime).Key("voltage").Double(_batteryVoltage);
            if (_lidarNew) _header.Key("lidar").Map(2).Key("t").Double(_lidarTime).Key("n").Int(_lidarRays);
            if (camera) _header.Key("camera").Map(3).Key("t").Double(_deliveredTime).Key("seq").Int(_deliveredSeq).Key("size").Int(_delivered.Length);
#if !USHQYR_BENCH_SERVER
            if (truth) GroundTruth.Write(_header, this);
#endif
            _tofNew = _batteryNew = false;

            _server.Send(_connection, BenchProtocol.Observation, _header, _imuBytes, imuLength + lidarLength,
                camera ? _delivered : null, camera ? _delivered.Length : 0);
            _lidarNew = false;
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
