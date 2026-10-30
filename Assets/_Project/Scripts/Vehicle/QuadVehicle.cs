using System;
using GateRush.Flight;
using GateRush.Input;
using UnityEngine;

namespace GateRush.Vehicle
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
        public bool IsCrashed => _crashed;
        public float LastImpactSpeed => _collision.LastImpactSpeed;
        public float HoverThrottle { get; private set; }
        public PilotInputSource InputSource
        {
            get => input;
            set => input = value;
        }

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

        public void Respawn()
        {
            Transform t = spawnPoint != null ? spawnPoint : transform;
            _state = QuadState.AtRest(CollisionHook.FromUnity(t.position), CollisionHook.FromUnity(t.rotation));
            _collision.PrevPosition = _state.Position;
            _collision.PrevOrientation = _state.Orientation;
            _collision.Crashed = false;
            _accumulator = 0f;
            _crashed = false;
            if (input != null) input.Disarm();
            ApplyPose(1f);
            Respawned?.Invoke();
        }

        private void Update()
        {
            PilotInput pilot = default;
            if (input != null)
            {
                input.HoverThrottle = HoverThrottle;
                pilot = input.Current;
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
                if (_crashTimer <= 0f) Respawn();
                return;
            }

            if (HoldAtSpawn)
            {
                Hold(pilot.Arm);
                return;
            }

            QuadSimulator.Advance(ref _state, pilot, _profile, _env, ref _accumulator, Time.deltaTime, ref _collision);

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
}
