using System;
using System.IO;
using Ushqyr.Flight;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Records the flight of the drone it sits on (EDU_PLAN F2): state and the pilot's sticks at 100 samples a second,
    /// crashes and respawns as events. In free flight it keeps the last ten minutes; an activity starts it afresh and
    /// keeps everything. It is not used on AI Bench drones, which have their own signed recorder.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EduRecorder : MonoBehaviour
    {
        /// <summary>Seconds kept while nothing asked for a full recording (free flight).</summary>
        public const float FreeFlightSeconds = 600f;

        private QuadVehicle _vehicle;
        private EduFlight _flight;
        private FlightRecorderCore _core;
        private double _clock;
        private bool _pilotSticks = true;
        private string _droneNoted;
        private bool _keepEverything;

        public FlightRecording Recording => _core?.Recording;
        public bool Paused { get; set; }

        private void OnEnable()
        {
            _vehicle = GetComponent<QuadVehicle>();
            if (_vehicle == null)
            {
                Debug.LogError("[Edu] EduRecorder needs a QuadVehicle on the same object.");
                enabled = false;
                return;
            }
            _flight = GetComponent<EduFlight>();
            _vehicle.Crashed += OnCrashed;
            _vehicle.Respawned += OnRespawned;
            if (_core == null) Begin(false);
        }

        private void OnDisable()
        {
            if (_vehicle == null) return;
            _vehicle.Crashed -= OnCrashed;
            _vehicle.Respawned -= OnRespawned;
        }

        /// <summary>Starts a new recording: the whole flight for an activity, the rolling last ten minutes otherwise.</summary>
        public void Begin(bool keepEverything)
        {
            _keepEverything = keepEverything;
            _droneNoted = null;
            _core = new FlightRecorderCore(100f, keepEverything ? 0f : FreeFlightSeconds);
            _clock = 0;
            Note("build", Application.version);
            Note("scene", gameObject.scene.name);
            Note("drone", _vehicle != null ? _vehicle.Profile.Name ?? "" : "");
            Note("started", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));
            _pilotSticks = true;
            Note("sticks", "pilot");
        }

        public void Note(string key, string value) => _core.Recording.Notes[key] = value ?? "";

        public void Event(string type, string data = "") => _core?.Event(type, data);

        private void OnCrashed() => Event("crash", _vehicle.LastImpactSpeed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));

        private void OnRespawned() => Event("respawn");

        private void LateUpdate()
        {
            if (Paused || _core == null || _vehicle.ExternalStepping) return;
            _clock += Time.deltaTime; // game time: a paused game records nothing, slow motion is recorded as flown
            ref readonly QuadState s = ref _vehicle.State;
            float capacity = _vehicle.Profile.Battery.CapacityMah;
            float battery = _flight != null ? _flight.Battery : capacity > 0f ? Mathf.Clamp01(1f - s.BatteryUsedMah / capacity) : 1f;
            // With a person flying, the sticks as that person holds them. With nobody at the sticks (a mission, a
            // student's program) there is nothing to show there, so the commands the flight controller was given.
            bool pilot = _flight == null || _flight.PilotPresent;
            if (pilot != _pilotSticks)
            {
                _pilotSticks = pilot;
                Note("sticks", pilot ? "pilot" : "commands");
            }
            PilotInput sticks = pilot ? _vehicle.LastInput : _vehicle.FlownInput;
            // The mode kept with each sample is how the drone was really flown: an assist or the autopilot, if one was on.
            if (_flight == null) _flight = GetComponent<EduFlight>();
            if (_flight != null) sticks.Mode = (FlightMode)_flight.ModeCode(_vehicle.LastInput.Mode);
            // The drone can be changed after the recording began (another drone chosen in the menu).
            string drone = _vehicle.Profile.Name ?? "";
            if (!ReferenceEquals(drone, _droneNoted) && drone != _droneNoted)
            {
                // Another drone is another flight: what the first one flew is not part of it.
                if (_droneNoted != null && !_keepEverything) Begin(false);
                _droneNoted = drone;
                Note("drone", drone);
            }
            _core.Push(_clock, s, sticks, battery);
        }

        /// <summary>Writes the recording so far to a `.grreplay` file and returns its hash.</summary>
        public string Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            string temp = path + ".tmp";
            _core.Recording.Save(temp);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            return _core.Recording.Hash();
        }
    }
}
