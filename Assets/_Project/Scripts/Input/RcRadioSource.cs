using UnityEngine;

namespace GateRush.Input
{
    /// <summary>
    /// RC radio (BetaFPV LiteRadio and any other joystick-mode radio) as a pilot input. Sticks and switches come
    /// from the <see cref="RadioDeviceProvider"/>'s mapping. Arm and mode follow physical switches; without an
    /// arm switch the radio arms whenever throttle is low.
    /// </summary>
    public sealed class RcRadioSource : PilotInputSource
    {
        [SerializeField] private RadioDeviceProvider provider;

        private SwitchArming _arming;
        private bool _resetWasOn, _viewWasOn;

        /// <summary>Counts flips of the view switch to its "on" position; consumers compare against the last count they saw.</summary>
        public int ViewPresses { get; private set; }

        public RadioDeviceProvider Provider
        {
            get => provider;
            set => provider = value;
        }

        /// <summary>The arm switch was flipped on with throttle up: arming is blocked until it is switched off.</summary>
        public bool ArmBlocked => _arming.Blocked;

        public override bool IsConnected => provider != null && provider.Reader != null && provider.Reader.IsConnected;

        public override int Priority => 30;

        protected override void ReadRaw(out float throttle, out float yaw, out float pitch, out float roll,
            out bool armToggle, out bool modeCycle, out bool reset)
        {
            var reader = provider.Reader;
            var m = provider.Mapping;

            throttle = m.Throttle.IsAssigned ? RadioMapping.Full(reader.Read(m.Throttle.Channel), m.Throttle) : 0f;
            yaw = m.Yaw.IsAssigned ? RadioMapping.Centered(reader.Read(m.Yaw.Channel), m.Yaw) : 0f;
            pitch = m.Pitch.IsAssigned ? RadioMapping.Centered(reader.Read(m.Pitch.Channel), m.Pitch) : 0f;
            roll = m.Roll.IsAssigned ? RadioMapping.Centered(reader.Read(m.Roll.Channel), m.Roll) : 0f;

            bool armOn = !m.Arm.IsAssigned || RadioMapping.SwitchPosition(reader.Read(m.Arm.Channel), m.Arm) == 1;
            bool wasBlocked = _arming.Blocked;
            armToggle = _arming.Update(armOn, throttle, IsArmed, ArmThrottleLimit);
            if (_arming.Blocked && !wasBlocked) ReportRefusal(ArmRefusal.SwitchBlocked);
            // Switch on, not blocked, but waiting for throttle low (e.g. after a crash): say so while they wait.
            if (armOn && !IsArmed && !_arming.Blocked && throttle > ArmThrottleLimit && Time.frameCount % 30 == 0) ReportRefusal(ArmRefusal.ThrottleHigh);

            HasAbsoluteMode = m.Mode.IsAssigned;
            if (HasAbsoluteMode) AbsoluteMode = RadioMapping.ModeFor(RadioMapping.SwitchPosition(reader.Read(m.Mode.Channel), m.Mode));
            modeCycle = false;

            bool resetOn = m.Reset.IsAssigned && RadioMapping.SwitchPosition(reader.Read(m.Reset.Channel), m.Reset) == 1;
            reset = resetOn && !_resetWasOn;
            _resetWasOn = resetOn;

            bool viewOn = m.View.IsAssigned && RadioMapping.SwitchPosition(reader.Read(m.View.Channel), m.View) == 1;
            if (viewOn && !_viewWasOn) ViewPresses++;
            _viewWasOn = viewOn;
        }
    }
}
