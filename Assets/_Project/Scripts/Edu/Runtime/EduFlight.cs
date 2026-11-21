using Ushqyr.Flight;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Puts the Education Suite's flight layer on a drone in the scene: assisted modes for the pilot, the autopilot,
    /// position sources with errors, weather and failures (EDU_PLAN F3, F4). It only ever changes the sticks the
    /// flight controller receives, the wind and the health of the motors; the flight model is the game's own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EduFlight : MonoBehaviour, IFlightDirector
    {
        /// <summary>How much the drone helps the person flying it.</summary>
        public AssistMode PilotAssist = AssistMode.Off;
        public PositionSourceSettings Source = PositionSourceSettings.Perfect;
        /// <summary>Seed of everything random here (sensor errors, gusts): the same seed gives the same conditions.</summary>
        public int Seed = 1;
        /// <summary>Seconds a change of weather takes to come in.</summary>
        public float WeatherBlendSeconds = 4f;

        public readonly Autopilot Autopilot = new Autopilot();
        public PositionFaults Faults;
        public Motor4 MotorLoss;
        public bool RadioOk = true;
        /// <summary>
        /// Motors on without a person holding the arm switch: set by a mission, a student's program or a ground
        /// station. The pilot's own disarm still wins when a pilot is present (see <see cref="PilotPresent"/>).
        /// </summary>
        public bool ArmRequested;
        /// <summary>False when nobody holds the sticks (a programming task): then only <see cref="ArmRequested"/> arms the drone.</summary>
        public bool PilotPresent = true;
        /// <summary>A battery fault: this share of the charge is gone (0 = none).</summary>
        public float BatteryDrop;

        private QuadVehicle _vehicle;
        private PositionEstimator _estimator;
        private PositionSourceKind _estimatorKind;
        private int _estimatorSeed;
        private WeatherSettings _from = WeatherSettings.Calm, _to = WeatherSettings.Calm;
        private float _blend = 1f, _groundY;
        private double _clock;
        private float _tunedMass, _tunedThrust;

        /// <summary>The weather right now (on the way to the last one asked for).</summary>
        public WeatherSettings Weather => Flight.Weather.Blend(_from, _to, _blend);

        /// <summary>Height of the ground under the drone, found by a ray straight down.</summary>
        public float GroundY => _groundY;
        public bool PositionOk => _estimator == null || _estimator.PositionOk;
        /// <summary>Battery left, 0..1, after any injected drop.</summary>
        public float Battery { get; private set; } = 1f;

        /// <summary>Changes the weather; it comes in over <see cref="WeatherBlendSeconds"/>. Pass true for "now" (the start of an activity).</summary>
        public void SetWeather(in WeatherSettings weather, bool atOnce = false)
        {
            _from = Weather;
            _to = weather;
            _blend = atOnce ? 1f : 0f;
        }

        private void OnEnable()
        {
            _vehicle = GetComponent<QuadVehicle>();
            if (_vehicle == null)
            {
                Debug.LogError("[Edu] EduFlight needs a QuadVehicle on the same object.");
                enabled = false;
                return;
            }
            _vehicle.Director = this;
            _groundY = transform.position.y;
        }

        private void OnDisable()
        {
            if (_vehicle != null && ReferenceEquals(_vehicle.Director, this))
            {
                _vehicle.Director = null;
                _vehicle.SetWind(Vector3.zero);
                _vehicle.SetMotorLoss(default);
            }
        }

        public PilotInput Direct(QuadVehicle vehicle, in PilotInput pilot, float dt)
        {
            _clock += dt;
            if (_blend < 1f) _blend = Mathf.Min(1f, _blend + dt / Mathf.Max(WeatherBlendSeconds, 0.01f));
            WeatherSettings weather = Weather;
            Vec3 wind = Flight.Weather.WindAt(weather, Seed, _clock);
            vehicle.SetWind(new Vector3(wind.x, wind.y, wind.z));
            vehicle.SetMotorLoss(MotorLoss);

            Vector3 p = transform.position;
            if (Physics.Raycast(p + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
                _groundY = hit.point.y;

            if (_estimator == null || _estimatorSeed != Seed || _estimatorKind != Source.Kind)
            {
                _estimator = new PositionEstimator(Source, Seed);
                _estimatorKind = Source.Kind;
                _estimatorSeed = Seed;
            }
            ref readonly QuadState truth = ref vehicle.State;
            QuadState believed = _estimator.Estimate(truth, _groundY, Faults, dt);

            ref readonly QuadProfile profile = ref vehicle.Profile;
            // The assist's limits follow the drone that is flown (and its load): a delivery drone is not a racer.
            if (profile.Mass != _tunedMass || profile.Motors.MaxThrustPerMotor != _tunedThrust)
            {
                _tunedMass = profile.Mass;
                _tunedThrust = profile.Motors.MaxThrustPerMotor;
                Autopilot.Settings = AssistTuning.For(profile, vehicle.Gravity);
            }
            float capacity = profile.Battery.CapacityMah;
            float left = capacity > 0f ? 1f - truth.BatteryUsedMah / capacity : 1f;
            Battery = Mathf.Clamp01(left - Mathf.Clamp01(BatteryDrop));
            var sensors = new AutopilotSensors
            {
                GroundY = _groundY,
                Battery = capacity > 0f ? Battery : -1f,
                RadioOk = RadioOk,
                PositionOk = _estimator.PositionOk,
            };
            PilotInput sticks = pilot;
            if (!PilotPresent) sticks = new PilotInput { Arm = ArmRequested, Throttle = 0.5f, Mode = FlightMode.Angle };
            else if (ArmRequested) sticks.Arm = true;
            if (Autopilot.Landed && ArmRequested && !truth.Armed) ArmRequested = false; // it landed by itself: the request is spent
            if (!RadioOk && Autopilot.Mode == GuidedMode.Manual)
            {
                // No radio: the last thing the pilot did no longer reaches the drone. It holds level until the failsafe acts.
                sticks.Pitch = sticks.Roll = sticks.Yaw = 0f;
                sticks.Throttle = 0.5f;
            }
            AssistMode assist = !RadioOk && PilotAssist == AssistMode.Off ? AssistMode.AltitudeHold : PilotAssist;
            return Autopilot.Update(believed, sticks, assist, sensors, profile, vehicle.Environment, dt);
        }
    }
}
