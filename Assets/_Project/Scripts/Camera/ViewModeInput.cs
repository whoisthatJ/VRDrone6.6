using GateRush.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GateRush.Cameras
{
    /// <summary>Cycles the camera view: keyboard V, gamepad left shoulder, or the radio's view switch (if calibrated).</summary>
    public sealed class ViewModeInput : MonoBehaviour
    {
        [SerializeField] private ViewModeManager views;
        [SerializeField] private RcRadioSource radio;

        private int _radioPresses;

        private void Start()
        {
            if (radio != null) _radioPresses = radio.ViewPresses;
        }

        private void Update()
        {
            if (views == null || !views.isActiveAndEnabled) return;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool cycle = (kb != null && kb.vKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame);
            if (radio != null && radio.ViewPresses != _radioPresses)
            {
                _radioPresses = radio.ViewPresses;
                cycle = true;
            }
            if (cycle) views.Cycle();
        }
    }
}
