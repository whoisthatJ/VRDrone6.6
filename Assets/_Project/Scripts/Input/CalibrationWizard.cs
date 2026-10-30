using System;

namespace GateRush.Input
{
    public enum CalibrationStep
    {
        /// <summary>Stir both sticks to every corner, then let them centre (throttle down) and hold still.</summary>
        Sticks = 0,
        Throttle = 1,
        Yaw = 2,
        Pitch = 3,
        Roll = 4,
        ArmSwitch = 5,
        ModeSwitch = 6,
        ModeThird = 7,
        Done = 8,
        Cancelled = 9,
    }

    /// <summary>
    /// Radio calibration as a state machine fed with raw channel values every frame. Steps advance by themselves
    /// when the pilot holds the requested position (so it works with both hands on the radio); Next/Skip are also
    /// available. Detects stick ranges and centres, which channel is which axis and its direction, the arm switch
    /// and a 2- or 3-position mode switch. Pure C#: no engine or device code.
    /// </summary>
    public sealed class CalibrationWizard
    {
        /// <summary>Minimum raw travel for a channel to count as a stick axis.</summary>
        public const float StickRange = 0.6f;
        /// <summary>A stick held this fraction of its half-range away from centre counts as "fully" deflected.</summary>
        public const float Deflection = 0.6f;
        /// <summary>Raw change that counts as a switch flip.</summary>
        public const float SwitchDelta = 0.4f;
        /// <summary>Seconds a position must be held to auto-advance.</summary>
        public const float HoldSeconds = 1f;
        /// <summary>Movement below this (raw units per frame) counts as "still".</summary>
        public const float StillThreshold = 0.02f;
        /// <summary>The optional third mode position auto-skips after this long.</summary>
        public const float ModeThirdTimeout = 8f;

        private readonly int _channels;
        private readonly float[] _min, _max, _center, _baseline, _last;
        private readonly bool[] _isStick, _used;
        private float _hold, _stepTime, _dt;
        private int _candidate = -1;
        private float _candidateValue;

        public CalibrationWizard(int channelCount)
        {
            _channels = channelCount;
            _min = new float[channelCount];
            _max = new float[channelCount];
            _center = new float[channelCount];
            _baseline = new float[channelCount];
            _last = new float[channelCount];
            _isStick = new bool[channelCount];
            _used = new bool[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                _min[i] = float.MaxValue;
                _max[i] = float.MinValue;
            }
            Result = new RadioMapping
            {
                Throttle = new AxisAssignment(-1), Yaw = new AxisAssignment(-1),
                Pitch = new AxisAssignment(-1), Roll = new AxisAssignment(-1),
                Arm = SwitchAssignment.None, Mode = SwitchAssignment.None, Reset = SwitchAssignment.None,
            };
        }

        public CalibrationStep Step { get; private set; } = CalibrationStep.Sticks;
        public RadioMapping Result { get; }
        /// <summary>0..1 progress of the current hold.</summary>
        public float HoldProgress => Math.Min(1f, _hold / HoldSeconds);
        public bool IsFinished => Step == CalibrationStep.Done || Step == CalibrationStep.Cancelled;
        /// <summary>Number of channels that have moved enough to be stick axes so far.</summary>
        public int StickChannelsFound { get; private set; }
        public bool CanSkip => Step == CalibrationStep.ArmSwitch || Step == CalibrationStep.ModeSwitch || Step == CalibrationStep.ModeThird;

        public string Instruction
        {
            get
            {
                switch (Step)
                {
                    case CalibrationStep.Sticks:
                        return StickChannelsFound < 4
                            ? $"Move BOTH sticks to every corner a few times. ({StickChannelsFound}/4 axes found)\nDon't touch the switches."
                            : "Now let both sticks centre, THROTTLE DOWN, and hold still...";
                    case CalibrationStep.Throttle: return "Push THROTTLE fully UP and hold.";
                    case CalibrationStep.Yaw: return "Throttle back down. Push YAW fully RIGHT and hold.";
                    case CalibrationStep.Pitch: return "Centre yaw. Push PITCH fully FORWARD (nose down) and hold.";
                    case CalibrationStep.Roll: return "Centre pitch. Push ROLL fully RIGHT and hold.";
                    case CalibrationStep.ArmSwitch: return "Centre the sticks. Flip your ARM switch to ARMED and hold.\n(Skip if you want to arm with throttle low instead.)";
                    case CalibrationStep.ModeSwitch: return "Put the MODE switch in ACRO, then move it to ANGLE and hold.\n(Skip if you don't use a mode switch.)";
                    case CalibrationStep.ModeThird: return "If the mode switch has a third position, move it to HORIZON and hold.\n(Skips by itself after a few seconds.)";
                    case CalibrationStep.Done: return "Done! Flip the arm switch off.";
                    default: return "Cancelled.";
                }
            }
        }

        public void Cancel() => Step = CalibrationStep.Cancelled;

        /// <summary>Feeds one frame of raw channel values.</summary>
        public void Feed(float[] values, float dt)
        {
            if (IsFinished) return;
            _dt = dt;
            _stepTime += dt;
            int n = Math.Min(_channels, values.Length);

            bool still = true;
            for (int i = 0; i < n; i++)
            {
                float v = values[i];
                if (v < _min[i]) _min[i] = v;
                if (v > _max[i]) _max[i] = v;
                if (Math.Abs(v - _last[i]) > StillThreshold) still = false;
                _last[i] = v;
            }

            switch (Step)
            {
                case CalibrationStep.Sticks:
                    UpdateStickChannels(n);
                    Hold(StickChannelsFound >= 4 && still, values);
                    break;
                case CalibrationStep.Throttle:
                case CalibrationStep.Yaw:
                case CalibrationStep.Pitch:
                case CalibrationStep.Roll:
                    TrackDeflection(values, n, sticks: true);
                    break;
                case CalibrationStep.ArmSwitch:
                case CalibrationStep.ModeSwitch:
                    TrackDeflection(values, n, sticks: false);
                    break;
                case CalibrationStep.ModeThird:
                    TrackModeThird(values);
                    if (!IsFinished && _stepTime > ModeThirdTimeout) Advance(values);
                    break;
            }
        }

        /// <summary>Accepts the current step now (pointer/keyboard "Next").</summary>
        public void Next(float[] values) => Advance(values);

        /// <summary>Skips an optional step (switches).</summary>
        public void Skip(float[] values)
        {
            if (!CanSkip) return;
            if (Step == CalibrationStep.ModeSwitch) Step = CalibrationStep.ModeThird; // nothing to refine
            BeginStep(Step + 1, values);
        }

        private void UpdateStickChannels(int n)
        {
            int found = 0;
            for (int i = 0; i < n; i++)
            {
                _isStick[i] = _max[i] - _min[i] >= StickRange;
                if (_isStick[i]) found++;
            }
            StickChannelsFound = found;
        }

        private void Hold(bool condition, float[] values)
        {
            _hold = condition ? _hold + _dt : 0f;
            if (_hold >= HoldSeconds) Advance(values);
        }

        /// <summary>Finds the unassigned channel deflected furthest from its baseline and waits for it to be held.</summary>
        private void TrackDeflection(float[] values, int n, bool sticks)
        {
            int best = -1;
            float bestAmount = 0f;
            for (int i = 0; i < n; i++)
            {
                if (_used[i] || _isStick[i] != sticks) continue;
                float delta = Math.Abs(values[i] - _baseline[i]);
                float needed = sticks ? Deflection * HalfRange(i) : SwitchDelta;
                if (delta < needed) continue;
                float amount = sticks ? delta / Math.Max(HalfRange(i), 1e-4f) : delta;
                if (amount > bestAmount)
                {
                    bestAmount = amount;
                    best = i;
                }
            }

            if (best >= 0 && best == _candidate && Math.Abs(values[best] - _candidateValue) < 0.1f)
                _hold += _dt;
            else
            {
                _hold = 0f;
                _candidate = best;
                if (best >= 0) _candidateValue = values[best];
            }
            if (best >= 0 && _hold >= HoldSeconds) Advance(values);
        }

        private void TrackModeThird(float[] values)
        {
            int ch = Result.Mode.Channel;
            if (ch < 0)
            {
                Advance(values);
                return;
            }
            float v = values[ch];
            bool distinct = Math.Abs(v - Result.Mode.Position0) >= SwitchDelta && Math.Abs(v - Result.Mode.Position1) >= SwitchDelta;
            Hold(distinct, values);
        }

        private float HalfRange(int i) => Math.Max((_max[i] - _min[i]) * 0.5f, 1e-4f);

        private void Advance(float[] values)
        {
            switch (Step)
            {
                case CalibrationStep.Sticks:
                    for (int i = 0; i < _channels && i < values.Length; i++) _center[i] = values[i];
                    break;
                case CalibrationStep.Throttle:
                    if (_candidate < 0) return;
                    // Centre snapshot was taken with throttle down, so "up" tells the direction.
                    Result.Throttle = new AxisAssignment(_candidate, values[_candidate] < _center[_candidate])
                    {
                        Range = new ChannelRange(_min[_candidate], (_min[_candidate] + _max[_candidate]) * 0.5f, _max[_candidate]),
                    };
                    break;
                case CalibrationStep.Yaw: Result.Yaw = Centred(values); break;
                case CalibrationStep.Pitch: Result.Pitch = Centred(values); break;
                case CalibrationStep.Roll: Result.Roll = Centred(values); break;
                case CalibrationStep.ArmSwitch:
                    if (_candidate < 0) return;
                    Result.Arm = new SwitchAssignment(_candidate, _baseline[_candidate], values[_candidate]);
                    break;
                case CalibrationStep.ModeSwitch:
                    if (_candidate < 0) return;
                    Result.Mode = new SwitchAssignment(_candidate, _baseline[_candidate], values[_candidate]);
                    break;
                case CalibrationStep.ModeThird:
                    var mode = Result.Mode;
                    if (mode.IsAssigned && _hold >= HoldSeconds)
                    {
                        mode.Position2 = values[mode.Channel];
                        mode.ThreePosition = true;
                        Result.Mode = mode;
                    }
                    break;
            }

            if (Step is CalibrationStep.Throttle or CalibrationStep.Yaw or CalibrationStep.Pitch or CalibrationStep.Roll
                or CalibrationStep.ArmSwitch or CalibrationStep.ModeSwitch)
                _used[_candidate] = true;

            BeginStep(Step + 1, values);
        }

        private AxisAssignment Centred(float[] values)
        {
            int c = _candidate;
            if (c < 0) return new AxisAssignment(-1);
            return new AxisAssignment(c, values[c] < _center[c]) { Range = new ChannelRange(_min[c], _center[c], _max[c]) };
        }

        private void BeginStep(CalibrationStep step, float[] values)
        {
            Step = step;
            _hold = 0f;
            _stepTime = 0f;
            _candidate = -1;
            for (int i = 0; i < _channels && i < values.Length; i++) _baseline[i] = step <= CalibrationStep.Roll ? _center[i] : values[i];
        }
    }
}
