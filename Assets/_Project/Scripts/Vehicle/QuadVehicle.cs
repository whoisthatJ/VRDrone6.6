using System;
using Ushqyr.Flight;
using Ushqyr.Input;
using UnityEngine;

namespace Ushqyr.Vehicle
{
    /// <summary>
    /// MonoBehaviour bridge: feeds pilot input into <see cref="QuadSimulator"/> with fixed sub-steps,
    /// sweeps collisions each sub-step, handles crash → respawn, and writes an interpolated pose to the Transform.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class QuadVehicle : MonoBehaviour
    {
        [SerializeField] private QuadProfileAsset profile;
        [Tooltip("Optional pilot rates; overrides the quad profile's rates.")]
        [SerializeField] private RateProfileAsset rates;
        [SerializeField] private PhysicsSettingsAsset physics;
        [SerializeField] private PilotInputSource input;
        [SerializeField] private Transform spawnPoint;

        private QuadState _state;
        private QuadProfile _profile;
        private PhysicsSettings _env;
        private CollisionHook _collision;
        private float _accumulator;
        private float _crashTimer;
        private bool _crashed;

        public event Action Crashed;
        public event Action Respawned;

        public ref readonly QuadState State => ref _state;
        public ref readonly QuadProfile Profile => ref _profile;
        public PilotInput LastInput { get; private set; }
        /// <summary>
        /// What the flight controller was given in the last frame. The same as <see cref="LastInput"/> unless a
        /// <see cref="Director"/> stands between the pilot and the controller (assisted modes, the autopilot).
        /// </summary>
        public PilotInput FlownInput { get; private set; }
        /// <summary>
        /// Optional layer between the pilot's sticks and the flight controller: assisted flight modes and the
        /// autopilot of the Education Suite. Null (the default) means the sticks go straight through, as always.
        /// It is not used with <see cref="ExternalStepping"/> (AI Bench).
        /// </summary>
        public IFlightDirector Director { get; set; }
        /// <summary>Gravity, step size and wind this quad flies in.</summary>
        public ref readonly PhysicsSettings Environment => ref _env;
        /// <summary>Thrust lost per motor (0 = healthy), for failure scenarios. Cleared by a respawn.</summary>
        public void SetMotorLoss(in Motor4 loss) => _state.MotorLoss = loss;
        public bool IsCrashed => _crashed;
        public float LastImpactSpeed => _collision.LastImpactSpeed;
        public int ImpactCount => _collision.ImpactCount;

        /// <summary>Battery sag on/off (the pack state itself lives in the flight state).</summary>
        public void SetBatterySag(bool enabled) => _env.BatterySag = enabled;
        /// <summary>Wind the quad flies in (m/s, world); zero by default (P7, Strike Range).</summary>
        public void SetWind(Vector3 wind) => _env.Wind = CollisionHook.FromUnity(wind);
        public float HoverThrottle { get; private set; }
        public PilotInputSource InputSource
        {
            get => input;
            set => input = value;
        }

        /// <summary>The other connected controllers' input, mixed on top of <see cref="InputSource"/>. Set by the router each frame.</summary>
        public PilotInput AssistInput { get; set; }

        public Transform SpawnPoint
        {
            get => spawnPoint;
            set => spawnPoint = value;
        }

        /// <summary>
        /// While true the quad is pinned to its spawn point (motors follow arming, but nothing moves).
        /// Used for the start countdown and for checkpoint respawns in the air.
        /// </summary>
        public bool HoldAtSpawn { get; set; }

        private void Awake()
        {
            ApplyProfile();
            Respawn();
        }

        /// <summary>Re-reads the profile/rates/physics assets (call after editing them at runtime).</summary>
        public void ApplyProfile()
        {
            _profile = profile != null ? profile.Profile : QuadProfile.Race5Inch;
            if (rates != null) _profile.Rates = rates.Rates;
            _env = physics != null ? physics.Simulation : PhysicsSettings.Default;
            _collision.Settings = physics != null ? physics.Collision : CollisionSettings.Default;
            HoverThrottle = MotorModel.HoverCommand(_profile.Motors, _profile.Mass * _env.Gravity);
        }

        public void SetProfile(QuadProfileAsset newProfile)
        {
            profile = newProfile;
            ApplyProfile();
        }

        /// <summary>
        /// Uses an already-customised profile (settings: rates, drag/motor scaling) and collision settings
        /// instead of the assets. Takes effect immediately; call <see cref="Respawn"/> for a clean start.
        /// </summary>
        public void Configure(in QuadProfile customProfile, in CollisionSettings collision)
        {
            _profile = customProfile;
            _collision.Settings = collision;
            HoverThrottle = MotorModel.HoverCommand(_profile.Motors, _profile.Mass * _env.Gravity);
        }

        /// <summary>Collision settings from the physics asset (defaults if none), as a base for <see cref="Configure"/>.</summary>
        public CollisionSettings BaseCollision => physics != null ? physics.Collision : CollisionSettings.Default;

        /// <summary>False when the last respawn followed a crash: sources were disarmed at the crash, and an arm press
        /// made during the respawn delay is kept instead of being wiped.</summary>
        public bool LastRespawnDisarmed { get; private set; } = true;

        /// <summary>A midair hit from another quad (P4 race mode): a velocity change, or a crash when it was hard.</summary>
        public void ApplyImpulse(Vector3 dv, bool crash)
        {
            if (_crashed || HoldAtSpawn) return;
            _state.Velocity += CollisionHook.FromUnity(dv);
            if (crash) _collision.Crashed = true;
        }

        public void Respawn() => Respawn(true);

        // ---------- stepped from outside (AI Bench) ----------

        /// <summary>
        /// True: the quad no longer advances with the frame clock. Something else calls <see cref="StepExternal"/> once
        /// per physics step (the AI Bench session, which keeps its own simulator clock) with the same flight
        /// controller, flight model and collision sweep as in normal play.
        /// </summary>
        public bool ExternalStepping { get; set; }

        public float FixedStep => _env.FixedStep;
        /// <summary>Collision sphere radius in use (m).</summary>
        public float BaseCollisionRadius => _collision.Settings.Radius;
        public float Gravity => _env.Gravity;

        /// <summary>One physics step with <paramref name="pilot"/> as the input. Only with <see cref="ExternalStepping"/>.</summary>
        public void StepExternal(in PilotInput pilot)
        {
            LastInput = pilot;
            QuadState prev = _state;
            QuadSimulator.Step(ref _state, pilot, _profile, _env, _env.FixedStep);
            _collision.AfterStep(ref _state, prev, _env.FixedStep);
            // A stepped quad never "crashes and respawns": it stays where it fell (the hook already stopped it).
            _collision.Crashed = false;
        }

        /// <summary>Places the quad at a pose, at rest (replaying a recorded flight). Only with <see cref="ExternalStepping"/>.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _state.Position = CollisionHook.FromUnity(position);
            _state.Orientation = CollisionHook.FromUnity(rotation);
            _state.Velocity = Vec3.Zero;
            _state.AngularVelocity = Vec3.Zero;
            _collision.PrevPosition = _state.Position;
            _collision.PrevOrientation = _state.Orientation;
            ApplyPose(1f);
        }

        /// <summary>Puts the Transform exactly on the simulated pose (no interpolation).</summary>
        public void SyncPose() => ApplyPose(1f);

        private void Respawn(bool disarm)
        {
            LastRespawnDisarmed = disarm;
            Transform t = spawnPoint != null ? spawnPoint : transform;
            _state = QuadState.AtRest(CollisionHook.FromUnity(t.position), CollisionHook.FromUnity(t.rotation));
            _collision.PrevPosition = _state.Position;
            _collision.PrevOrientation = _state.Orientation;
            _collision.Crashed = false;
            _accumulator = 0f;
            _crashed = false;
            if (disarm && input != null) input.Disarm();
            ApplyPose(1f);
            Respawned?.Invoke();
        }

        private void Update()
        {
            if (ExternalStepping)
            {
                ApplyPose(1f);
                return;
            }

            PilotInput pilot = default;
            if (input != null)
            {
                input.HoverThrottle = HoverThrottle;
                pilot = PilotInput.Mix(input.Current, AssistInput);
            }
            LastInput = pilot;

            if (pilot.Reset)
            {
                Respawn();
                return;
            }

            if (_crashed)
            {
                _crashTimer -= Time.deltaTime;
                if (_crashTimer <= 0f) Respawn(false);
                return;
            }

            if (HoldAtSpawn)
            {
                Hold(pilot.Arm);
                return;
            }

            PilotInput flown = Director != null ? Director.Direct(this, pilot, Time.deltaTime) : pilot;
            FlownInput = flown;
            QuadSimulator.Advance(ref _state, flown, _profile, _env, ref _accumulator, Time.deltaTime, ref _collision);

            if (_state.Position.y < _collision.Settings.OutOfBoundsY) _collision.Crashed = true;

            if (_collision.Crashed)
            {
                _crashed = true;
                _crashTimer = _collision.Settings.RespawnDelay;
                _state.Armed = false;
                _state.MotorSpeed = default;
                if (input != null) input.Disarm();
                Crashed?.Invoke();
                ApplyPose(1f);
                return;
            }

            ApplyPose(Mathf.Clamp01(_accumulator / _env.FixedStep));
        }

        private void Hold(bool armed)
        {
            Transform t = spawnPoint != null ? spawnPoint : transform;
            _state.Position = CollisionHook.FromUnity(t.position);
            _state.Orientation = CollisionHook.FromUnity(t.rotation);
            _state.Velocity = Vec3.Zero;
            _state.AngularVelocity = Vec3.Zero;
            _state.Pid = default;
            _state.Armed = armed;
            float idle = armed ? _profile.Motors.IdleFraction : 0f;
            for (int i = 0; i < 4; i++) _state.MotorSpeed[i] = idle;
            _collision.PrevPosition = _state.Position;
            _collision.PrevOrientation = _state.Orientation;
            _accumulator = 0f;
            ApplyPose(1f);
        }

        private void ApplyPose(float alpha)
        {
            Vector3 prev = CollisionHook.ToUnity(_collision.PrevPosition);
            Vector3 cur = CollisionHook.ToUnity(_state.Position);
            Quaternion prevRot = CollisionHook.ToUnity(_collision.PrevOrientation);
            Quaternion curRot = CollisionHook.ToUnity(_state.Orientation);
            // The state is one sub-step ahead of render time by 'alpha'; blend from the previous sub-step.
            transform.SetPositionAndRotation(Vector3.LerpUnclamped(prev, cur, alpha), Quaternion.SlerpUnclamped(prevRot, curRot, alpha));
        }
    }

    /// <summary>Stands between the pilot's sticks and the flight controller of a <see cref="QuadVehicle"/>.</summary>
    public interface IFlightDirector
    {
        /// <summary>The sticks to give the flight controller this frame, given what the pilot is doing.</summary>
        PilotInput Direct(QuadVehicle vehicle, in PilotInput pilot, float dt);
    }
}
