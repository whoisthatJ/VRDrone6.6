using UnityEngine;
using UnityEngine.InputSystem;

namespace GateRush.Input
{
    /// <summary>
    /// Gamepad (Mode 2) with keyboard fallback, read directly from devices (no allocations).
    /// Gamepad: left stick = throttle/yaw, right stick = pitch/roll, Start = arm, Select = mode, Y = reset.
    /// Keyboard: W/S throttle (ramps), A/D yaw, arrows pitch/roll, Space = arm, M = mode, R = reset.
    /// </summary>
    public sealed class KeyboardGamepadSource : PilotInputSource
    {
        [SerializeField] private ThrottleMapping gamepadThrottle = ThrottleMapping.SpringCenterHover;
        [SerializeField] private float keyboardThrottleRate = 0.6f;
        [SerializeField] private float keyboardStickRate = 6f;

        private float _kbThrottle, _kbYaw, _kbPitch, _kbRoll;

        public override bool IsConnected => Gamepad.current != null || Keyboard.current != null;

        public override int Priority => Gamepad.current != null ? 20 : 1;

        protected override void ReadRaw(out float throttle, out float yaw, out float pitch, out float roll,
            out bool armToggle, out bool modeCycle, out bool reset)
        {
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;

            if (kb != null)
            {
                _kbThrottle = StickShaping.Clamp(_kbThrottle + Axis(kb.wKey, kb.sKey) * keyboardThrottleRate * dt, 0f, 1f);
                _kbYaw = Mathf.MoveTowards(_kbYaw, Axis(kb.dKey, kb.aKey), keyboardStickRate * dt);
                _kbPitch = Mathf.MoveTowards(_kbPitch, Axis(kb.upArrowKey, kb.downArrowKey), keyboardStickRate * dt);
                _kbRoll = Mathf.MoveTowards(_kbRoll, Axis(kb.rightArrowKey, kb.leftArrowKey), keyboardStickRate * dt);
            }

            throttle = _kbThrottle;
            yaw = _kbYaw;
            pitch = _kbPitch;
            roll = _kbRoll;
            armToggle = kb != null && kb.spaceKey.wasPressedThisFrame;
            modeCycle = kb != null && kb.mKey.wasPressedThisFrame;
            reset = kb != null && kb.rKey.wasPressedThisFrame;

            if (pad == null) return;

            Vector2 left = pad.leftStick.ReadValue();
            Vector2 right = pad.rightStick.ReadValue();
            float padThrottle = gamepadThrottle == ThrottleMapping.Trigger
                ? StickShaping.MapThrottle(gamepadThrottle, pad.rightTrigger.ReadValue(), HoverThrottle)
                : StickShaping.MapThrottle(gamepadThrottle, left.y, HoverThrottle);

            // The gamepad wins whenever it is being touched; the keyboard keeps its own ramped throttle.
            bool padActive = left.sqrMagnitude > 0.01f || right.sqrMagnitude > 0.01f || pad.rightTrigger.ReadValue() > 0.01f;
            if (padActive || gamepadThrottle == ThrottleMapping.SpringCenterHover && _kbThrottle == 0f)
            {
                throttle = padThrottle;
                yaw = left.x;
                pitch = right.y;
                roll = right.x;
            }

            armToggle |= pad.startButton.wasPressedThisFrame;
            modeCycle |= pad.selectButton.wasPressedThisFrame;
            reset |= pad.buttonNorth.wasPressedThisFrame;
        }

        private static float Axis(UnityEngine.InputSystem.Controls.KeyControl pos, UnityEngine.InputSystem.Controls.KeyControl neg) =>
            (pos.isPressed ? 1f : 0f) - (neg.isPressed ? 1f : 0f);
    }
}
