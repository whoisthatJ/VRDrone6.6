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
        private bool _ownsWind;
        private float _baroFault;
        private Ushqyr.Cameras.VtxSignal _video;

        /// <summary>The weather right now (on the way to the last one asked for).</summary>
        public WeatherSettings Weather => Flight.Weather.Blend(_from, _to, _blend);

        /// <summary>Height of the ground under the drone, found by a ray straight down.</summary>
        public float GroundY => _groundY;
        public bool PositionOk => _estimator == null || _estimator.PositionOk;
        /// <summary>Battery left, 0..1, after any injected drop.</summary>
        public float Battery { get; private set; } = 1f;

        /// <summary>
        /// Failures set by name (EDU_PLAN F4), by an activity's timeline or by the teacher. They add to the separate
        /// fields above (<see cref="MotorLoss"/>, <see cref="Faults"/>, <see cref="RadioOk"/>, <see cref="BatteryDrop"/>).
        /// </summary>
        public FailureSet Failures;

        /// <summary>
        /// Sets one failure by its name (see <see cref="FailureSet.Names"/>); 0 mends it. The change is written into
        /// the flight's recording. False for a name that is not known.
        /// </summary>
        public bool SetFailure(string name, float value)
        {
            if (!Failures.Set(name, value)) return false;
            if (name == "baro_drift" && value == 0f) _baroFault = 0f;
            EduRecorder recorder = GetComponent<EduRecorder>();
            if (recorder != null)
                recorder.Event("failure", name + "=" + Failures.Get(name).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            ApplyEquipmentFailures();
            return true;
        }

        /// <summary>Mends everything set through <see cref="SetFailure"/>.</summary>
        public void ClearFailures()
        {
            if (Failures.Healthy) return;
            Failures = default;
            _baroFault = 0f;
            EduRecorder recorder = GetComponent<EduRecorder>();
            if (recorder != null) recorder.Event("failure", "none");
            ApplyEquipmentFailures();
        }

        /// <summary>The failures that live in other parts of the drone: the gimbal, the payload release, the picture.</summary>
        private void ApplyEquipmentFailures()
        {
            EduGimbal gimbal = GetComponent<EduGimbal>();
            if (gimbal != null) gimbal.Stuck = Failures.GimbalStuck;
            EduPayload payload = GetComponent<EduPayload>();
            if (payload != null) payload.Jammed = Failures.PayloadJam;
            if (_video == null) _video = FindAnyObjectByType<Ushqyr.Cameras.VtxSignal>();
            if (_video != null) _video.Fault = Failures.VideoLost ? 1f : Failures.VideoNoise;
        }

        /// <summary>Names of the values <see cref="ModeCode"/> gives, in order (keys of the language table).</summary>
        public static readonly string[] ModeNames = { "Acro", "Angle", "Horizon", "Altitude hold", "Position hold", "Autopilot" };

        /// <summary>
        /// How the drone is being flown, as one number for recordings and displays: 0 to 2 are the flight
        /// controller's own modes (as the pilot's switch says), 3 altitude hold, 4 position hold, 5 the autopilot.
        /// </summary>
        public byte ModeCode(FlightMode sticks)
        {
            if (!enabled) return (byte)sticks;
            if (Autopilot.Mode != GuidedMode.Manual) return 5;
            return PilotAssist == AssistMode.Position ? (byte)4 : PilotAssist == AssistMode.AltitudeHold ? (byte)3 : (byte)sticks;
        }

        /// <summary>Changes the weather; it comes in over <see cref="WeatherBlendSeconds"/>. Pass true for "now" (the start of an activity).</summary>
        public void SetWeather(in WeatherSettings weather, bool atOnce = false)
        {
            _from = Weather;
            _to = weather;
            _blend = atOnce ? 1f : 0f;
            _ownsWind = true;
            // Every change of weather is written into the flight's recording (F4).
            EduRecorder recorder = GetComponent<EduRecorder>();
            if (recorder != null)
                recorder.Event("weather", string.Format(System.Globalization.CultureInfo.InvariantCulture, "wind={0:0.#};from={1:0};gusts={2:0.##};rain={3:0.##};visibility={4:0};temperature={5:0.#}",
                    weather.Wind, weather.WindFromDeg, weather.Gusts, weather.Rain, weather.Visibility, weather.TemperatureC));
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
                if (_ownsWind) _vehicle.SetWind(Vector3.zero);
                _vehicle.SetMotorLoss(default);
            }
        }

        public PilotInput Direct(QuadVehicle vehicle, in PilotInput pilot, float dt)
        {
            _clock += dt;
            if (_blend < 1f) _blend = Mathf.Min(1f, _blend + dt / Mathf.Max(WeatherBlendSeconds, 0.01f));
            WeatherSettings weather = Weather;
            Vec3 wind = Flight.Weather.WindAt(weather, Seed, _clock);
            // Until it is given weather of its own it leaves the scene's wind alone (the Strike Range has one).
            if (_ownsWind)
            {
                vehicle.SetWind(new Vector3(wind.x, wind.y, wind.z));
                EduWeatherVisuals.Ensure().Show(weather, wind);
            }
            Motor4 lost = Failures.MotorLossAt(_clock);
            vehicle.SetMotorLoss(new Motor4
            {
                M0 = Mathf.Max(MotorLoss.M0, lost.M0), M1 = Mathf.Max(MotorLoss.M1, lost.M1),
                M2 = Mathf.Max(MotorLoss.M2, lost.M2), M3 = Mathf.Max(MotorLoss.M3, lost.M3),
            });

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
            PositionFaults faults = Faults;
            faults.Lost |= Failures.GpsLost;
            faults.Degraded = Mathf.Max(faults.Degraded, Failures.GpsDegraded);
            faults.HeadingOffsetDeg += Failures.CompassOffsetDeg;
            QuadState believed = _estimator.Estimate(truth, _groundY, faults, dt);
            // A barometer that runs away: the drone believes it is higher and higher, and so sinks.
            _baroFault += Failures.BaroDriftRate * dt;
            if (_baroFault != 0f) believed.Position = new Vec3(believed.Position.x, believed.Position.y + _baroFault, believed.Position.z);
            bool radioOk = RadioOk && !Failures.RadioLost;

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
            Battery = Mathf.Clamp01(left - Mathf.Clamp01(Mathf.Max(BatteryDrop, Failures.BatteryDrop)));
            var sensors = new AutopilotSensors
            {
                GroundY = _groundY,
                Battery = capacity > 0f ? Battery : -1f,
                RadioOk = radioOk,
                PositionOk = _estimator.PositionOk,
            };
            PilotInput sticks = pilot;
            if (!PilotPresent) sticks = new PilotInput { Arm = ArmRequested, Throttle = 0.5f, Mode = FlightMode.Angle };
            else if (ArmRequested) sticks.Arm = true;
            if (Autopilot.Landed && ArmRequested && !truth.Armed) ArmRequested = false; // it landed by itself: the request is spent
            if (!radioOk && Autopilot.Mode == GuidedMode.Manual)
            {
                // No radio: the last thing the pilot did no longer reaches the drone. It holds level until the failsafe acts.
                sticks.Pitch = sticks.Roll = sticks.Yaw = 0f;
                sticks.Throttle = 0.5f;
            }
            AssistMode assist = !radioOk && PilotAssist == AssistMode.Off ? AssistMode.AltitudeHold : PilotAssist;
            PilotInput flown = Autopilot.Update(believed, sticks, assist, sensors, profile, vehicle.Environment, dt);
            return Failures.WithImuError(flown, profile);
        }
    }
}
