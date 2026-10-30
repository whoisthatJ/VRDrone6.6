using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace GateRush.Input
{
    /// <summary>
    /// Meta Quest Touch controllers, Mode 2.
    /// Left stick: throttle (Y) / yaw (X). Right stick: pitch (Y) / roll (X).
    /// A = arm toggle, B = flight mode, X = reset. Throttle is either spring-centred with hover assist or the left trigger.
    /// </summary>
    public sealed class TouchControllerSource : PilotInputSource
    {
        [SerializeField] private ThrottleMapping throttleMapping = ThrottleMapping.SpringCenterHover;

        private Vector2Control _leftStick, _rightStick;
        private AxisControl _leftTrigger;
        private ButtonControl _arm, _mode, _reset;
        private bool _dirty = true;

        public ThrottleMapping ThrottleMapping
        {
            get => throttleMapping;
            set => throttleMapping = value;
        }

        public override bool IsConnected
        {
            get
            {
                if (_dirty) Bind();
                return _leftStick != null && _rightStick != null;
            }
        }

        public override int Priority => 10;

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is XRController) _dirty = true;
        }

        private void Bind()
        {
            _dirty = false;
            var left = XRController.leftHand;
            var right = XRController.rightHand;
            _leftStick = left?.TryGetChildControl<Vector2Control>("thumbstick");
            _leftTrigger = left?.TryGetChildControl<AxisControl>("trigger");
            _reset = left?.TryGetChildControl<ButtonControl>("primaryButton");
            _rightStick = right?.TryGetChildControl<Vector2Control>("thumbstick");
            _arm = right?.TryGetChildControl<ButtonControl>("primaryButton");
            _mode = right?.TryGetChildControl<ButtonControl>("secondaryButton");
        }

        protected override void ReadRaw(out float throttle, out float yaw, out float pitch, out float roll,
            out bool armToggle, out bool modeCycle, out bool reset)
        {
            Vector2 l = _leftStick.ReadValue();
            Vector2 r = _rightStick.ReadValue();
            throttle = throttleMapping == ThrottleMapping.Trigger
                ? StickShaping.MapThrottle(ThrottleMapping.Trigger, _leftTrigger != null ? _leftTrigger.ReadValue() : 0f, HoverThrottle)
                : StickShaping.MapThrottle(throttleMapping, l.y, HoverThrottle);
            yaw = l.x;
            pitch = r.y;
            roll = r.x;
            armToggle = _arm != null && _arm.wasPressedThisFrame;
            modeCycle = _mode != null && _mode.wasPressedThisFrame;
            reset = _reset != null && _reset.wasPressedThisFrame;
        }
    }
}
