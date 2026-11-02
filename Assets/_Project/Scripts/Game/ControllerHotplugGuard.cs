using System;
using GateRush.Input;
using GateRush.Race;
using GateRush.Vehicle;
using UnityEngine;

namespace GateRush.Game
{
    /// <summary>
    /// Pauses the race when the active controller changes mid-race (a radio unplugged, a Bluetooth link dropped, or a
    /// radio plugged in), and resumes once the now-active controller has its sticks centred and is armed again.
    /// Pausing uses Time.timeScale = 0, which freezes the flight sim and the race clock.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class ControllerHotplugGuard : MonoBehaviour
    {
        [SerializeField] private VehicleInputRouter router;
        [SerializeField] private RaceSession race;
        [SerializeField] private float centreTolerance = 0.15f;
        [SerializeField] private float resumeHoldSeconds = 0.4f;

        private float _held;

        public bool Paused { get; private set; }
        /// <summary>Why we paused, for the overlay.</summary>
        public string Reason { get; private set; } = "";
        /// <summary>0..1 progress of the "centred and armed" hold that resumes.</summary>
        public float ResumeProgress => Mathf.Clamp01(_held / resumeHoldSeconds);

        public event Action<bool> PausedChanged;

        private void OnEnable()
        {
            if (router != null) router.SourceChanged += OnSourceChanged;
        }

        private void OnDisable()
        {
            if (router != null) router.SourceChanged -= OnSourceChanged;
            if (Paused) SetPaused(false, "");
        }

        private void OnSourceChanged(PilotInputSource previous, PilotInputSource next)
        {
            if (previous == null || previous is ScriptedInputSource || next is ScriptedInputSource) return;
            bool racing = race != null && (race.Phase == RacePhase.Racing || race.Phase == RacePhase.Countdown);
            if (!racing) return;

            string reason = !previous.IsConnected
                ? $"{Describe(previous)} disconnected"
                : next != null ? $"{Describe(next)} connected" : "Controller changed";
            SetPaused(true, reason);
        }

        private void Update()
        {
            if (!Paused) return;
            var source = router != null ? router.Active : null;
            bool ready = false;
            if (source != null && source.IsConnected)
            {
                var input = source.Current;
                ready = input.Arm && Mathf.Abs(input.Yaw) < centreTolerance && Mathf.Abs(input.Pitch) < centreTolerance
                        && Mathf.Abs(input.Roll) < centreTolerance;
            }
            _held = ready ? _held + Time.unscaledDeltaTime : 0f;
            if (_held >= resumeHoldSeconds) SetPaused(false, "");
        }

        private void SetPaused(bool paused, string reason)
        {
            Paused = paused;
            Reason = reason;
            _held = 0f;
            Time.timeScale = paused ? 0f : 1f;
            Debug.Log(paused ? $"[GateRush] Race paused: {reason}" : "[GateRush] Race resumed");
            PausedChanged?.Invoke(paused);
        }

        private static string Describe(PilotInputSource s)
        {
            switch (s)
            {
                case RcRadioSource _: return "Radio";
                case KeyboardGamepadSource _: return "Gamepad";
                default: return "Controller";
            }
        }
    }
}
