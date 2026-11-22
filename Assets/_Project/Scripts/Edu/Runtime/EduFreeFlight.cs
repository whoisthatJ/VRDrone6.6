using System.Globalization;
using TMPro;
using Ushqyr.Core;
using Ushqyr.Flight;
using Ushqyr.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ushqyr.Edu
{
    /// <summary>
    /// What the Education Suite adds to ordinary flying in a track scene (EDU_PLAN F2, F3): the assisted flight modes
    /// for the person at the sticks, a recording of the last ten minutes, and the replay of it.
    /// <para>G steps through the assists (off, altitude hold, position hold); L opens the replay of the last flight.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EduFreeFlight : MonoBehaviour
    {
        /// <summary>The setting "Flight assist": 0 chooses by the drone, then off, altitude hold, position hold.</summary>
        public static readonly string[] SettingNames = { "By the drone", "Off", "Altitude hold", "Position hold" };

        private QuadVehicle _vehicle;
        private EduFlight _flight;
        private EduRecorder _recorder;
        private TextMeshProUGUI _label;
        private float _showUntil;
        private string _shown = "";

        public AssistMode Assist { get; private set; }
        public EduRecorder Recorder => _recorder;

        /// <summary>The assist a setting means for a drone: the Education Suite's own drones hold position unless told otherwise.</summary>
        public static AssistMode Resolve(int setting, bool eduDrone)
        {
            switch (setting)
            {
                case 1: return AssistMode.Off;
                case 2: return AssistMode.AltitudeHold;
                case 3: return AssistMode.Position;
                default: return eduDrone ? AssistMode.Position : AssistMode.Off;
            }
        }

        /// <summary>Puts the component on the drone (once) and applies the assist setting.</summary>
        public static EduFreeFlight Attach(QuadVehicle vehicle, int setting, bool eduDrone)
        {
            if (vehicle == null) return null;
            EduFreeFlight free = vehicle.GetComponent<EduFreeFlight>();
            if (free == null) free = vehicle.gameObject.AddComponent<EduFreeFlight>();
            free._vehicle = vehicle;
            free.Set(Resolve(setting, eduDrone), false);
            if (free._recorder == null)
            {
                free._recorder = vehicle.GetComponent<EduRecorder>();
                if (free._recorder == null) free._recorder = vehicle.gameObject.AddComponent<EduRecorder>();
            }
            return free;
        }

        /// <summary>Switches the assist. The flight layer is on the drone only while an assist is; with none the sticks go straight through, as always.</summary>
        public void Set(AssistMode mode, bool announce)
        {
            Assist = mode;
            if (_flight == null) _flight = _vehicle.GetComponent<EduFlight>();
            if (mode == AssistMode.Off)
            {
                // Leave an autopilot that is flying (a mission, a return) alone.
                // …and one that holds an exercise's flight mode.
                if (_flight != null && _flight.Autopilot.Mode == GuidedMode.Manual && !_flight.ArmRequested && !_flight.ForcedMode.HasValue) _flight.enabled = false;
                else if (_flight != null) _flight.PilotAssist = AssistMode.Off;
            }
            else
            {
                if (_flight == null) _flight = _vehicle.gameObject.AddComponent<EduFlight>();
                _flight.enabled = true;
                _flight.PilotAssist = mode;
            }
            if (announce) Say(L.T("Flight assist") + ": " + L.T(Name(mode)), 3f);
        }

        private static string Name(AssistMode mode) =>
            mode == AssistMode.Position ? "Position hold" : mode == AssistMode.AltitudeHold ? "Altitude hold" : "Off";

        private void Say(string text, float seconds)
        {
            if (_label == null)
            {
                Canvas canvas = EduUi.Canvas("Edu Flight Note", 40);
                canvas.transform.SetParent(transform, false);
                canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().enabled = false;
                RectTransform box = EduUi.Rect(canvas.transform, "Note", 0f, 0f, 900f, 44f);
                box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 1f);
                box.anchoredPosition = new Vector2(0f, -110f);
                _label = EduUi.Text(box, "", 0f, 0f, 900f, 44f, 28f, EduUi.Accent, TextAlignmentOptions.Center);
            }
            _shown = text;
            _label.text = text;
            _showUntil = Time.unscaledTime + seconds;
        }

        private void Update()
        {
            if (_vehicle == null) return;
            if (_label != null && _shown.Length > 0 && Time.unscaledTime > _showUntil)
            {
                _shown = "";
                _label.text = "";
            }
            Keyboard kb = Keyboard.current;
            if (kb == null || EduReplayViewer.Current != null) return;
            // In an exercise the assist is part of the exercise: the keys of free flight do nothing.
            if (EduActivityRunner.Current != null) return;
            if (kb.gKey.wasPressedThisFrame)
                Set(Assist == AssistMode.Off ? AssistMode.AltitudeHold : Assist == AssistMode.AltitudeHold ? AssistMode.Position : AssistMode.Off, true);
            if (kb.lKey.wasPressedThisFrame) WatchLastFlight();
        }

        /// <summary>Opens the replay viewer on what was recorded so far; false when there is too little to watch.</summary>
        public bool WatchLastFlight()
        {
            FlightRecording recording = _recorder != null ? _recorder.Recording : null;
            if (recording == null || recording.Duration < 2f)
            {
                Say(L.T("Nothing recorded yet"), 2f);
                return false;
            }
            // The viewer gets its own copy: the recorder goes on adding to its own once the viewer is closed.
            var copy = new FlightRecording { Rate = recording.Rate };
            foreach (var note in recording.Notes) copy.Notes[note.Key] = note.Value;
            copy.Samples.AddRange(recording.Samples);
            copy.Events.AddRange(recording.Events);
            EduReplayViewer.Open(_vehicle, copy, null, L.T("Last flight") + "  ·  " + EduUi.Clock(copy.Duration).ToString(CultureInfo.InvariantCulture));
            return true;
        }
    }
}
