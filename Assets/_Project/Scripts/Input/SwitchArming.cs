namespace GateRush.Input
{
    /// <summary>
    /// Betaflight-style arm switch: the quad arms when the switch is flipped on with throttle low. Flipping it on
    /// with throttle high blocks arming until the switch is turned off again. Switch off always disarms.
    /// Produces the arm-toggle edge that <see cref="PilotInputSource"/> expects.
    /// </summary>
    public struct SwitchArming
    {
        private bool _previous;
        private bool _blocked;

        public bool Blocked => _blocked;

        /// <returns>True when the source should toggle its armed state this frame.</returns>
        public bool Update(bool switchOn, float throttle, bool armed, float throttleLimit)
        {
            bool rising = switchOn && !_previous;
            _previous = switchOn;

            if (!switchOn)
            {
                _blocked = false;
                return armed; // disarm
            }
            if (rising && throttle > throttleLimit) _blocked = true;
            if (_blocked || armed) return false;
            return throttle <= throttleLimit;
        }
    }
}
