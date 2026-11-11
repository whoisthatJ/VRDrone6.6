using System;
using System.Collections.Generic;
using System.IO;
using Ushqyr.AI;
using Ushqyr.Garage;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Strike
{
    public enum StrikePhase { Idle, Countdown, Round, Finished }

    /// <summary>
    /// The online side of a round (P6), supplied by the game's online driver: the shared round clock, and where the
    /// local pilot's hit claims and crashes go. The host resolves its own claims locally through the same referee.
    /// </summary>
    public interface IStrikeLink
    {
        /// <summary>Seconds since the round's start on the shared clock (negative during the countdown).</summary>
        float Clock { get; }
        bool IsHost { get; }
        /// <summary>The local pilot's key (grid slot).</summary>
        int LocalPilot { get; }
        void ClaimHit(in StrikeWire.HitClaim claim);
        void ReportCrash(float time);
    }

    /// <summary>Lobby choices for a round.</summary>
    [Serializable]
    public struct StrikeOptions
    {
        public RangeDifficulty Difficulty;
        public int Bots;
        public BotLevel BotLevel;
        public int Seed;
        public string PilotName;
    }

    /// <summary>
    /// A Strike Range round (P5): 3-2-1 on the pad lights, 120 s, targets from the <see cref="RangeDirector"/>, the
    /// player and up to 3 bots. A contact with a target at 8 m/s or more is a hit (score, streak); a slower contact or any
    /// other crash resets the streak. 1.5 s later the pilot relaunches from the nearest free pad with a fresh battery.
    /// </summary>
    public sealed class StrikeSession : MonoBehaviour
    {
        [SerializeField] private RangeLayout layout;
        [Tooltip("Countdown lights per pad: 3 renderers each (red, yellow, green), pad by pad.")]
        [SerializeField] private Renderer[] padLights = new Renderer[0];
        [Tooltip("Smoke and scorch materials made by the range builder (assets, so their shaders are in builds).")]
        [SerializeField] private Material smokeMaterial, scorchMaterial, flashMaterial;
        [Tooltip("Vehicle materials per VehicleKit.Slot (paint, rubber, metal, dark, glass, canvas, light).")]
        [SerializeField] private Material[] vehicleMaterials = new Material[0];

        private QuadVehicle _player;
        private Transform _padSpawn;
        private RangeDirector _director;
        private readonly List<RangeTargetView> _views = new List<RangeTargetView>();
        private readonly List<StrikeBot> _bots = new List<StrikeBot>();
        private readonly List<Transform> _botVisuals = new List<Transform>();
        private StrikeOptions _options;
        private float _phaseStart;
        private float _roundStart;
        private float[] _padBusyUntil = new float[0];
        private Vector3 _lastPlayerVelocity;
        private bool _contactHandled;
        private QuadModelMaterials _materials;
        private int _layer;
        private SizeClass _size;
        private bool _restartQueued;

        public StrikePhase Phase { get; private set; }
        public StrikeScore PlayerScore { get; private set; } = new StrikeScore();
        public RangeDirector Director => _director;
        public RangeLayout Layout => layout;
        public IReadOnlyList<StrikeBot> Bots => _bots;
        public StrikeOptions Options => _options;
        /// <summary>The wind right now (m/s, world); zero on Easy.</summary>
        public Vector3 Wind { get; private set; }
        /// <summary>Seconds into the round (0 before the start, RoundSeconds after the end).</summary>
        public float RoundTime => Phase == StrikePhase.Round ? Mathf.Clamp(Link != null ? Link.Clock : Time.time - _roundStart, 0f, StrikeRules.RoundSeconds) : Phase == StrikePhase.Finished ? StrikeRules.RoundSeconds : 0f;
        public float TimeLeft => StrikeRules.RoundSeconds - RoundTime;
        public float CountdownLeft => Phase != StrikePhase.Countdown ? 0f : Link != null ? Mathf.Max(0f, -Link.Clock) : Mathf.Max(0f, StrikeRules.CountdownSeconds - (Time.time - _phaseStart));
        /// <summary>Online round (P6): set before <see cref="Begin"/>; null offline.</summary>
        public IStrikeLink Link { get; set; }
        public bool Online => Link != null;
        /// <summary>Other pilots' scores in an online round (by pilot key), as the host reports them.</summary>
        private readonly Dictionary<int, StrikeScore> _remote = new Dictionary<int, StrikeScore>();
        /// <summary>Host side of an online round: a bot hit (bot index, confirm) or crash (bot index).</summary>
        public event Action<int, StrikeWire.HitConfirm> BotHit;
        public event Action<int> BotCrashed;
        /// <summary>Online: the reset button after the round (the host takes everyone back to the lobby).</summary>
        public event Action RestartRequested;
        /// <summary>The pad the local pilot starts on (online pilots spread over the pads by grid slot).</summary>
        public int StartPad { get; set; }
        /// <summary>The last scoring event for the HUD: text and time.</summary>
        public string LastEvent { get; private set; } = "";
        public float LastEventTime { get; private set; } = -10f;
        /// <summary>Best scores for this difficulty (local leaderboard), loaded at the end of a round.</summary>
        public List<StrikeBoardEntry> Board { get; private set; } = new List<StrikeBoardEntry>();
        public int LastRank { get; private set; } = -1;
        public event Action<StrikePhase> PhaseChanged;

        public void Configure(RangeLayout rangeLayout, Renderer[] lights, Material smoke = null, Material scorch = null, Material[] vehicles = null, Material flash = null)
        {
            flashMaterial = flash;
            vehicleMaterials = vehicles ?? new Material[0];
            smokeMaterial = smoke;
            scorchMaterial = scorch;
            layout = rangeLayout;
            padLights = lights;
        }

        /// <summary>Starts a round with the player's quad (and its build materials for the bots' models).</summary>
        public void Begin(QuadVehicle player, StrikeOptions options, QuadModelMaterials materials, SizeClass size)
        {
            _player = player;
            _options = options;
            _materials = materials;
            _layer = LayerMask.NameToLayer("TrackGeometry");
            if (_options.Seed == 0) _options.Seed = Environment.TickCount & 0x7fffffff;
            if (_padSpawn == null) _padSpawn = new GameObject("Strike Pad Spawn").transform;
            _player.SpawnPoint = _padSpawn;
            _player.Crashed -= OnPlayerCrashed;
            _player.Crashed += OnPlayerCrashed;
            _player.Respawned -= OnPlayerRespawned;
            _player.Respawned += OnPlayerRespawned;
            _size = size;
            Clear();

            if (smokeMaterial != null) RangeTargetView.SmokeOverride = smokeMaterial;
            if (vehicleMaterials != null && vehicleMaterials.Length > 0) RangeVehicles.MaterialsOverride = vehicleMaterials;
            if (scorchMaterial != null) RangeTargetView.ScorchOverride = scorchMaterial;
            if (flashMaterial != null) RangeTargetView.FlashOverride = flashMaterial;
            _director = new RangeDirector(layout, _options.Seed, _options.Difficulty);
            var targets = new GameObject("Targets").transform;
            targets.SetParent(transform, false);
            for (int i = 0; i < _director.Targets.Count; i++)
            {
                var v = RangeTargetView.Create(targets, _director, i, _layer);
                v.Sync(0f);
                _views.Add(v);
            }
            _padBusyUntil = new float[layout.Pads.Length];
            PlayerScore = new StrikeScore { Pilot = string.IsNullOrEmpty(_options.PilotName) ? "Pilot" : _options.PilotName };
            var collision = _player.BaseCollision;
            var level = BotDifficultyProfile.For(_options.BotLevel);
            _remote.Clear();
            int botCount = Link == null || Link.IsHost ? Mathf.Clamp(_options.Bots, 0, 3) : 0;
            for (int i = 0; i < botCount; i++)
            {
                var build = BotRoster.BuildFor(size, i);
                var bot = new StrikeBot(i, BotRoster.NameFor(i, _options.Seed), _director, BuildComposer.Compose(build), level, collision, _options.Seed * 13 + i);
                int index = i;
                bot.Hit = (slot, pts) =>
                {
                    Event($"{bot.Score.Pilot} +{pts}");
                    var target = _director.Targets[slot];
                    BotHit?.Invoke(index, new StrikeWire.HitConfirm { Slot = slot, Generation = target.Plan.Generation, Pilot = StrikeWire.BotKeyBase + index, Points = pts, Time = target.DisabledAt });
                };
                bot.Crashed = () => BotCrashed?.Invoke(index);
                _bots.Add(bot);
                var root = new GameObject("Strike Bot " + bot.Score.Pilot).transform;
                root.SetParent(transform, false);
                if (_materials != null) QuadModelBuilder.Build(build, _materials, root, 0);
                _botVisuals.Add(root);
            }
            // Player on pad 0, bots on the others (they take turns if there are more bots than pads).
            PlacePlayer(StartPad);
            for (int i = 0; i < _bots.Count; i++)
            {
                int pad = (i + 1) % layout.Pads.Length;
                _bots[i].Launch(pad, 0f);
                _padBusyUntil[pad] = 0f;
            }
            SetPhase(StrikePhase.Countdown);
        }

        public void Clear()
        {
            foreach (var v in _views) if (v != null) Destroy(v.gameObject);
            _views.Clear();
            foreach (var b in _botVisuals) if (b != null) Destroy(b.gameObject);
            _botVisuals.Clear();
            _bots.Clear();
            var old = transform.Find("Targets");
            if (old != null) Destroy(old.gameObject);
        }

        private void SetPhase(StrikePhase p)
        {
            Phase = p;
            _phaseStart = Time.time;
            if (p == StrikePhase.Countdown && _player != null) _player.HoldAtSpawn = true;
            if (p == StrikePhase.Round)
            {
                _roundStart = Time.time;
                if (_player != null) _player.HoldAtSpawn = false;
                PlayerScore.Launch();
            }
            if (p == StrikePhase.Finished)
            {
                Wind = Vector3.zero;
                if (_player != null) _player.SetWind(Vector3.zero);
                Finish();
            }
            PhaseChanged?.Invoke(p);
        }

        // The reset button after a round (T / L-stick click) starts a new one.
        private void OnPlayerRespawned()
        {
            if (Phase != StrikePhase.Finished || _player == null || !_player.LastInput.Reset) return;
            if (Link != null) RestartRequested?.Invoke();
            else _restartQueued = true;
        }

        private void Update()
        {
            if (_restartQueued)
            {
                _restartQueued = false;
                Restart(_size);
            }
            if (_director == null) return;
            if (Phase == StrikePhase.Countdown)
            {
                // Online the wait for GO can be longer than 3 s: red until the last three.
                Lights(Mathf.Min(CountdownLeft, StrikeRules.CountdownSeconds));
                if (CountdownLeft <= 0f) SetPhase(StrikePhase.Round);
                return;
            }
            if (Phase != StrikePhase.Round) return;
            float t = RoundTime;
            Lights(-1f);
            Wind = StrikeRules.WindAt(_options.Difficulty, _options.Seed, t);
            if (_player != null) _player.SetWind(Wind);
            foreach (var b in _bots) b.SetWind(Wind);
            _director.Update(t);
            foreach (var v in _views) v.Sync(t);
            Physics.SyncTransforms();
            for (int i = 0; i < _bots.Count; i++)
            {
                var bot = _bots[i];
                bot.Advance(Time.deltaTime, t, NextPadFor);
                if (_botVisuals[i] != null)
                {
                    _botVisuals[i].gameObject.SetActive(!bot.Down);
                    _botVisuals[i].SetPositionAndRotation(bot.Position, bot.Rotation);
                }
            }
            PlayerContact(t);
            if (t >= StrikeRules.RoundSeconds) SetPhase(StrikePhase.Finished);
        }

        private void LateUpdate()
        {
            if (_player != null) _lastPlayerVelocity = TrajectoryFollower.ToU(_player.State.Velocity);
        }

        // The player touching a target: a hit at 8 m/s or more, else a bump; either way the launch ends.
        private void PlayerContact(float t)
        {
            if (_player == null || _player.IsCrashed || _player.HoldAtSpawn) return;
            var p = _player.transform.position;
            var hits = Physics.OverlapSphere(p, _player.BaseCollision.Radius + 0.2f, _player.BaseCollision.TrackGeometry, QueryTriggerInteraction.Ignore);
            foreach (var c in hits)
            {
                var hb = c.GetComponent<StrikeHitbox>();
                if (hb == null || !_director.Targets[hb.Slot].Active) continue;
                float speed = Mathf.Max(_lastPlayerVelocity.magnitude, TrajectoryFollower.ToU(_player.State.Velocity).magnitude);
                ResolvePlayer(hb.Slot, speed, t);
                _contactHandled = true;
                PlacePlayer(NextPadFor(-1));
                _player.ApplyImpulse(Vector3.zero, true);
                return;
            }
        }

        private void OnPlayerCrashed()
        {
            if (Phase != StrikePhase.Round) return;
            if (_contactHandled)
            {
                _contactHandled = false;
                return;
            }
            // A crash next to a target can still be a hit (the collision stopped the quad first).
            float t = RoundTime;
            int slot = -1;
            foreach (var c in Physics.OverlapSphere(_player.transform.position, _player.BaseCollision.Radius + 0.6f, _player.BaseCollision.TrackGeometry, QueryTriggerInteraction.Ignore))
            {
                var hb = c.GetComponent<StrikeHitbox>();
                if (hb != null && _director.Targets[hb.Slot].Active) slot = hb.Slot;
            }
            ResolvePlayer(slot, _lastPlayerVelocity.magnitude, t);
            PlacePlayer(NextPadFor(-1));
        }

        private void ResolvePlayer(int slot, float speed, float t)
        {
            if (Link != null)
            {
                // Online the host decides: a candidate hit is claimed, anything else is a crash.
                if (slot >= 0 && StrikeRules.IsHit(speed))
                {
                    var target = _director.Targets[slot];
                    Link.ClaimHit(new StrikeWire.HitClaim { Slot = slot, Generation = target.Plan.Generation, Time = t, Speed = speed, Position = _player.transform.position });
                    Event("...");
                }
                else
                {
                    Link.ReportCrash(t);
                    Event(slot >= 0 ? Ushqyr.Core.L.T("Too slow: bump") : Ushqyr.Core.L.T("Crash"));
                }
                return;
            }
            if (slot >= 0 && StrikeRules.IsHit(speed) && _director.Hit(slot, t, 100))
            {
                int pts = PlayerScore.Hit(_director.Targets[slot].Plan.Class, speed);
                Event($"+{pts}  x{StrikeRules.Multiplier(PlayerScore.Streak - 1):0.0}");
            }
            else
            {
                PlayerScore.Crash();
                Event(slot >= 0 ? Ushqyr.Core.L.T("Too slow: bump") : Ushqyr.Core.L.T("Crash"));
            }
            PlayerScore.Launch();
        }

        private void Event(string text)
        {
            LastEvent = text;
            LastEventTime = Time.time;
        }

        /// <summary>The free pad nearest the pilot (-1 = the player); pads stay busy 2 s after a launch.</summary>
        private int NextPadFor(int bot)
        {
            Vector3 from = bot < 0 ? (_player != null ? _player.transform.position : Vector3.zero) : _bots[bot].Position;
            int best = 0;
            float bestD = float.MaxValue;
            float now = Time.time;
            for (int i = 0; i < layout.Pads.Length; i++)
            {
                float d = (layout.Pads[i].Position - from).sqrMagnitude + (now < _padBusyUntil[i] ? 1e9f : 0f);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            _padBusyUntil[best] = now + StrikeRules.RelaunchSeconds + 2f;
            return best;
        }

        private void PlacePlayer(int pad)
        {
            var p = layout.Pads[Mathf.Clamp(pad, 0, layout.Pads.Length - 1)];
            _padSpawn.SetPositionAndRotation(p.Position + Vector3.up * 0.15f, Quaternion.Euler(0f, p.Yaw, 0f));
        }

        // Countdown lights: red, yellow, green; all green during the round.
        private void Lights(float left)
        {
            if (padLights == null) return;
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < padLights.Length; i++)
            {
                var r = padLights[i];
                if (r == null) continue;
                int lamp = i % 3;
                bool on = left < 0f ? lamp == 2 : (lamp == 0 && left > 2f) || (lamp == 1 && left <= 2f && left > 1f) || (lamp == 2 && left <= 1f);
                Color c = lamp == 0 ? new Color(1f, 0.1f, 0.05f) : lamp == 1 ? new Color(1f, 0.75f, 0.1f) : new Color(0.15f, 1f, 0.25f);
                block.SetColor("_BaseColor", on ? c : c * 0.15f);
                block.SetColor("_EmissionColor", on ? c * 4f : Color.black);
                r.SetPropertyBlock(block);
            }
        }

        private void Finish()
        {
            if (_player != null) _player.HoldAtSpawn = true;
            var board = StrikeBoard.Load();
            // A round where the pilot never scored is not a "best round".
            if (PlayerScore.Score <= 0)
            {
                LastRank = 0;
                Board = board.For(_options.Difficulty);
                return;
            }
            LastRank = board.Submit(_options.Difficulty, new StrikeBoardEntry
            {
                Pilot = PlayerScore.Pilot, Score = PlayerScore.Score, Hits = PlayerScore.Hits, BestStreak = PlayerScore.BestStreak,
                Date = DateTime.Now.ToString("yyyy-MM-dd"),
            });
            board.Save();
            Board = board.For(_options.Difficulty);
        }

        /// <summary>Everyone's score, best first (player and bots).</summary>
        public List<StrikeScore> Standings()
        {
            var list = new List<StrikeScore> { PlayerScore };
            foreach (var b in _bots) list.Add(b.Score);
            foreach (var r in _remote.Values) list.Add(r);
            list.Sort(StrikeScore.Compare);
            return list;
        }

        // ---------- online (P6) ----------

        /// <summary>A hit the host confirmed: disables the target here too (the host already did) and tells the pilot.</summary>
        public void ApplyConfirm(in StrikeWire.HitConfirm c, string pilotName)
        {
            if (_director == null || c.Slot < 0 || c.Slot >= _director.Targets.Count) return;
            var t = _director.Targets[c.Slot];
            if (t.Plan.Generation == c.Generation && t.Active) _director.Hit(c.Slot, c.Time, c.Pilot);
            bool me = Link != null && c.Pilot == Link.LocalPilot;
            Event(me ? $"+{c.Points}" : $"{pilotName} +{c.Points}");
        }

        /// <summary>The host turned the local pilot's claim down.</summary>
        public void ApplyReject(RefereeVerdict v)
        {
            Event(v == RefereeVerdict.AlreadyDown || v == RefereeVerdict.Stale ? Ushqyr.Core.L.T("Too late: already hit") : Ushqyr.Core.L.T("Not counted"));
        }

        /// <summary>A score from the host: the local pilot's, another pilot's, or (on clients) a bot's.</summary>
        public void ApplyScore(in StrikeWire.ScoreState s, string pilotName)
        {
            if (Link == null) return;
            if (s.Pilot == Link.LocalPilot)
            {
                s.ApplyTo(PlayerScore);
                return;
            }
            if (Link.IsHost && s.Pilot >= StrikeWire.BotKeyBase) return; // the host's own bots keep their scores
            if (!_remote.TryGetValue(s.Pilot, out var r)) _remote[s.Pilot] = r = new StrikeScore();
            r.Pilot = pilotName;
            s.ApplyTo(r);
        }

        /// <summary>A pilot left the online round.</summary>
        public void RemovePilot(int pilot) => _remote.Remove(pilot);

        /// <summary>A new round with a new seed (the same options).</summary>
        public void Restart(SizeClass size)
        {
            if (Link != null) return; // online: the host starts the next round from the lobby
            var o = _options;
            o.Seed = 0;
            if (_player != null) Begin(_player, o, _materials, size);
        }

        private void OnDestroy()
        {
            if (_player != null) _player.SetWind(Vector3.zero);
            if (_player != null)
            {
                _player.Crashed -= OnPlayerCrashed;
                _player.Respawned -= OnPlayerRespawned;
            }
            if (_padSpawn != null) Destroy(_padSpawn.gameObject);
        }
    }

    [Serializable]
    public sealed class StrikeBoardEntry
    {
        public string Pilot;
        public int Score;
        public int Hits;
        public int BestStreak;
        public string Date;
    }

    /// <summary>Local Strike Range leaderboard: the 10 best rounds per difficulty, in persistentDataPath/strike.json.</summary>
    [Serializable]
    public sealed class StrikeBoard
    {
        public List<StrikeBoardEntry> Easy = new List<StrikeBoardEntry>(), Normal = new List<StrikeBoardEntry>(), Hard = new List<StrikeBoardEntry>();

        public static string PathOnDisk => System.IO.Path.Combine(Application.persistentDataPath, "strike.json");

        public List<StrikeBoardEntry> For(RangeDifficulty d) => d == RangeDifficulty.Easy ? Easy : d == RangeDifficulty.Hard ? Hard : Normal;

        /// <summary>Adds a round; returns its rank (1-based) or -1 if it is not in the top 10.</summary>
        public int Submit(RangeDifficulty d, StrikeBoardEntry e)
        {
            var list = For(d);
            list.Add(e);
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            int rank = list.IndexOf(e) + 1;
            if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
            return rank <= 10 ? rank : -1;
        }

        public static StrikeBoard Load()
        {
            try
            {
                if (File.Exists(PathOnDisk))
                {
                    var b = JsonUtility.FromJson<StrikeBoard>(File.ReadAllText(PathOnDisk)) ?? new StrikeBoard();
                    // Rounds without a score are not "best rounds" (early builds saved them).
                    foreach (var list in new[] { b.Easy, b.Normal, b.Hard }) list?.RemoveAll(e => e == null || e.Score <= 0);
                    return b;
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException) { }
            return new StrikeBoard();
        }

        public void Save()
        {
            try { File.WriteAllText(PathOnDisk, JsonUtility.ToJson(this, true)); }
            catch (IOException e) { Debug.LogWarning("[Ushqyr][Strike] could not save the leaderboard: " + e.Message); }
        }
    }
}
