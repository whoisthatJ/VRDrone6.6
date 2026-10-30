using UnityEngine;

namespace GateRush.Input
{
    /// <summary>
    /// Input source driven from code: smoke tests, demos and automated play-mode checks.
    /// Set the stick fields, and call <see cref="PressArm"/> / <see cref="PressMode"/> for button edges.
    /// </summary>
    public sealed class ScriptedInputSource : PilotInputSource
    {
        [Range(0f, 1f)] public float Throttle;
        [Range(-1f, 1f)] public float Yaw;
        [Range(-1f, 1f)] public float Pitch;
        [Range(-1f, 1f)] public float Roll;

        private bool _arm, _mode, _reset;

        public override bool IsConnected => true;

        /// <summary>Above every hardware source, so adding this component takes over the quad.</summary>
        public override int Priority => 100;

        public void PressArm() => _arm = true;
        public void PressMode() => _mode = true;
        public void PressReset() => _reset = true;

        protected override void ReadRaw(out float throttle, out float yaw, out float pitch, out float roll,
            out bool armToggle, out bool modeCycle, out bool reset)
        {
            throttle = Throttle;
            yaw = Yaw;
            pitch = Pitch;
            roll = Roll;
            armToggle = _arm;
            modeCycle = _mode;
            reset = _reset;
            _arm = _mode = _reset = false;
        }
    }
}
