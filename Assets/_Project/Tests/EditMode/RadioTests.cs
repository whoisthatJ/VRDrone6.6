using System.Collections.Generic;
using GateRush.Flight;
using GateRush.Input;
using NUnit.Framework;
using UnityEngine;

namespace GateRush.Tests
{
    public class RadioMappingTests
    {
        [Test]
        public void Centered_UsesSeparateScaleEachSideOfCentre()
        {
            var a = new AxisAssignment(0) { Range = new ChannelRange(-0.8f, 0.1f, 1f) };
            Assert.AreEqual(0f, RadioMapping.Centered(0.1f, a), 1e-5f);
            Assert.AreEqual(1f, RadioMapping.Centered(1f, a), 1e-5f);
            Assert.AreEqual(-1f, RadioMapping.Centered(-0.8f, a), 1e-5f);
            Assert.AreEqual(0.5f, RadioMapping.Centered(0.55f, a), 1e-5f);
            Assert.AreEqual(-0.5f, RadioMapping.Centered(-0.35f, a), 1e-5f);
            Assert.AreEqual(1f, RadioMapping.Centered(5f, a), 1e-5f, "clamped");
        }

        [Test]
        public void Centered_Invert()
        {
            var a = new AxisAssignment(0, invert: true);
            Assert.AreEqual(-0.5f, RadioMapping.Centered(0.5f, a), 1e-5f);
        }

        [Test]
        public void Full_MapsThrottleTravelToZeroOne()
        {
            var a = new AxisAssignment(0) { Range = new ChannelRange(-1f, 0f, 1f) };
            Assert.AreEqual(0f, RadioMapping.Full(-1f, a), 1e-5f);
            Assert.AreEqual(0.5f, RadioMapping.Full(0f, a), 1e-5f);
            Assert.AreEqual(1f, RadioMapping.Full(1f, a), 1e-5f);
            a.Invert = true;
            Assert.AreEqual(1f, RadioMapping.Full(-1f, a), 1e-5f);
            // A 0..1 HID throttle.
            var b = new AxisAssignment(0) { Range = new ChannelRange(0f, 0.5f, 1f) };
            Assert.AreEqual(0.25f, RadioMapping.Full(0.25f, b), 1e-5f);
        }

        [Test]
        public void InvalidRange_FallsBackToDefault()
        {
            var a = new AxisAssignment(0) { Range = new ChannelRange(0f, 0f, 0f) };
            Assert.AreEqual(0.5f, RadioMapping.Centered(0.5f, a), 1e-5f);
        }

        [Test]
        public void SwitchPosition_TwoAndThreePosition()
        {
            var two = new SwitchAssignment(4, -1f, 1f);
            Assert.AreEqual(0, RadioMapping.SwitchPosition(-0.9f, two));
            Assert.AreEqual(1, RadioMapping.SwitchPosition(0.7f, two));

            var three = new SwitchAssignment(5, -1f, 0f) { Position2 = 1f, ThreePosition = true };
            Assert.AreEqual(0, RadioMapping.SwitchPosition(-0.95f, three));
            Assert.AreEqual(1, RadioMapping.SwitchPosition(0.1f, three));
            Assert.AreEqual(2, RadioMapping.SwitchPosition(0.9f, three));
            Assert.AreEqual(FlightMode.Acro, RadioMapping.ModeFor(0));
            Assert.AreEqual(FlightMode.Angle, RadioMapping.ModeFor(1));
            Assert.AreEqual(FlightMode.Horizon, RadioMapping.ModeFor(2));
        }

        [Test]
        public void SwitchArming_ArmsOnlyWithThrottleLow_AndBlocksIfFlippedWithThrottleUp()
        {
            var arming = new SwitchArming();
            bool armed = false;
            void Step(bool sw, float thr)
            {
                if (arming.Update(sw, thr, armed, 0.05f)) armed = !armed;
            }

            Step(false, 0f);
            Assert.IsFalse(armed);
            Step(true, 0f);
            Assert.IsTrue(armed, "switch on, throttle low");
            Step(true, 0.6f);
            Assert.IsTrue(armed, "stays armed while flying");
            Step(false, 0.6f);
            Assert.IsFalse(armed, "switch off always disarms");

            Step(true, 0.6f);
            Assert.IsFalse(armed, "flipped on with throttle up");
            Assert.IsTrue(arming.Blocked);
            Step(true, 0f);
            Assert.IsFalse(armed, "still blocked after lowering throttle");
            Step(false, 0f);
            Step(true, 0f);
            Assert.IsTrue(armed, "cycling the switch clears the block");
        }

        [Test]
        public void SwitchArming_ReArmsAfterRespawnDisarm()
        {
            var arming = new SwitchArming();
            Assert.IsTrue(arming.Update(true, 0f, false, 0.05f));
            // Respawn disarmed the quad; switch still on and throttle low → arm again.
            Assert.IsTrue(arming.Update(true, 0f, false, 0.05f));
        }
    }

    public class ControllerPresetTests
    {
        private static ControllerPreset Make(string pattern, int priority = 0)
        {
            var p = ScriptableObject.CreateInstance<ControllerPreset>();
            p.NamePattern = pattern;
            p.Priority = priority;
            return p;
        }

        [Test]
        public void Find_MatchesByRegexIgnoringCase_HighestPriorityWins()
        {
            var generic = Make("BETAFPV", 0);
            var lr2 = Make("^BETAFPV Joystick$", 10);
            var lr4 = Make("LiteRadio.?4", 10);
            var list = new List<ControllerPreset> { generic, lr2, lr4 };
            try
            {
                Assert.AreSame(lr2, ControllerPreset.Find(list, "BETAFPV Joystick", "BETAFPV Joystick"));
                Assert.AreSame(generic, ControllerPreset.Find(list, "betafpv something", ""));
                Assert.AreSame(lr4, ControllerPreset.Find(list, "", "LiteRadio 4 SE"));
                Assert.IsNull(ControllerPreset.Find(list, "RadioMaster TX16S", "TX16S"));
                Assert.IsNull(ControllerPreset.Find(list, null, null));
            }
            finally
            {
                foreach (var p in list) Object.DestroyImmediate(p);
            }
        }

        [Test]
        public void CalibrationStore_JsonRoundTrip()
        {
            var store = new RadioCalibrationStore();
            var m = new RadioMapping { Arm = new SwitchAssignment(5, -1f, 1f) };
            m.Throttle.Range = new ChannelRange(-0.9f, 0f, 0.95f);
            store.Set("BETAFPV Joystick|1209|4F54", m);
            var copy = new RadioCalibrationStore();
            copy.FromJson(store.ToJson());
            var back = copy.Find("BETAFPV Joystick|1209|4F54");
            Assert.IsNotNull(back);
            Assert.AreEqual(5, back.Arm.Channel);
            Assert.AreEqual(0.95f, back.Throttle.Range.Max);
            Assert.IsNull(copy.Find("other"));
            Assert.IsTrue(copy.Remove("BETAFPV Joystick|1209|4F54"));
            Assert.IsNull(copy.Find("BETAFPV Joystick|1209|4F54"));
        }
    }

    public class CalibrationWizardTests
    {
        // Simulated radio: 0 roll, 1 pitch, 2 throttle (inverted wiring), 3 yaw, 4 arm switch, 5 mode switch (3-pos).
        private readonly float[] _v = new float[6];
        private CalibrationWizard _w;
        private const float Dt = 1f / 72f;

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt) _w.Feed(_v, Dt);
        }

        private void Centre()
        {
            _v[0] = 0f; _v[1] = 0f; _v[3] = 0f;
            _v[2] = 1f; // throttle DOWN reads +1 on this radio (inverted)
        }

        [SetUp]
        public void SetUp()
        {
            _w = new CalibrationWizard(_v.Length);
            Centre();
            _v[4] = -1f; // disarmed
            _v[5] = -1f; // acro
        }

        [Test]
        public void FullFlow_DetectsAxesDirectionsAndSwitches()
        {
            // Stir the sticks.
            foreach (var corner in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                _v[0] = corner.Item1; _v[1] = corner.Item2; _v[2] = corner.Item1; _v[3] = corner.Item2;
                Run(0.1f);
            }
            Assert.AreEqual(CalibrationStep.Sticks, _w.Step);
            Assert.AreEqual(4, _w.StickChannelsFound);
            Centre();
            Run(1.2f);
            Assert.AreEqual(CalibrationStep.Throttle, _w.Step, "centre captured after holding still");

            _v[2] = -1f; // throttle up (inverted wiring)
            Run(1.2f);
            Assert.AreEqual(CalibrationStep.Yaw, _w.Step);
            _v[2] = 1f;
            _v[3] = 1f; // yaw right
            Run(1.2f);
            Assert.AreEqual(CalibrationStep.Pitch, _w.Step);
            _v[3] = 0f;
            _v[1] = -1f; // pitch forward reads negative on this radio
            Run(1.2f);
            Assert.AreEqual(CalibrationStep.Roll, _w.Step);
            _v[1] = 0f;
            _v[0] = 1f; // roll right
            Run(1.2f);
            Assert.AreEqual(CalibrationStep.ArmSwitch, _w.Step);
            _v[0] = 0f;
            _v[4] = 1f; // armed
            Run(1.2f);
            Assert.AreEqual(CalibrationStep.ModeSwitch, _w.Step);
            _v[5] = 0f; // angle
            Run(1.2f);
            Assert.AreEqual(CalibrationStep.ModeThird, _w.Step);
            _v[5] = 1f; // horizon
            Run(1.2f);
            Assert.AreEqual(CalibrationStep.Done, _w.Step);

            var m = _w.Result;
            Assert.AreEqual(2, m.Throttle.Channel);
            Assert.IsTrue(m.Throttle.Invert);
            Assert.AreEqual(3, m.Yaw.Channel);
            Assert.IsFalse(m.Yaw.Invert);
            Assert.AreEqual(1, m.Pitch.Channel);
            Assert.IsTrue(m.Pitch.Invert);
            Assert.AreEqual(0, m.Roll.Channel);
            Assert.IsFalse(m.Roll.Invert);
            Assert.AreEqual(4, m.Arm.Channel);
            Assert.AreEqual(1, RadioMapping.SwitchPosition(1f, m.Arm));
            Assert.AreEqual(5, m.Mode.Channel);
            Assert.IsTrue(m.Mode.ThreePosition);
            Assert.AreEqual(FlightMode.Horizon, RadioMapping.ModeFor(RadioMapping.SwitchPosition(1f, m.Mode)));

            // The result maps the radio correctly.
            Assert.AreEqual(0f, RadioMapping.Full(1f, m.Throttle), 1e-4f, "throttle down");
            Assert.AreEqual(1f, RadioMapping.Full(-1f, m.Throttle), 1e-4f, "throttle up");
            Assert.AreEqual(1f, RadioMapping.Centered(-1f, m.Pitch), 1e-4f, "pitch forward = +1");
        }

        [Test]
        public void SwitchSteps_CanBeSkipped_AndModeThirdTimesOut()
        {
            _v[0] = -1f; _v[1] = -1f; _v[2] = -1f; _v[3] = -1f; Run(0.05f);
            _v[0] = 1f; _v[1] = 1f; _v[2] = 1f; _v[3] = 1f; Run(0.05f);
            Centre(); Run(1.2f);
            _v[2] = -1f; Run(1.2f); _v[2] = 1f;
            _v[3] = 1f; Run(1.2f); _v[3] = 0f;
            _v[1] = 1f; Run(1.2f); _v[1] = 0f;
            _v[0] = 1f; Run(1.2f); _v[0] = 0f;
            Assert.AreEqual(CalibrationStep.ArmSwitch, _w.Step);
            Assert.IsTrue(_w.CanSkip);
            _w.Skip(_v);
            Assert.AreEqual(CalibrationStep.ModeSwitch, _w.Step);
            _v[5] = 0f; Run(1.2f);
            Assert.AreEqual(CalibrationStep.ModeThird, _w.Step);
            Run(CalibrationWizard.ModeThirdTimeout + 0.5f);
            Assert.AreEqual(CalibrationStep.Done, _w.Step);
            Assert.IsFalse(_w.Result.Arm.IsAssigned);
            Assert.IsFalse(_w.Result.Mode.ThreePosition);
        }

        [Test]
        public void StickStep_DoesNotAdvanceUntilFourAxesMoved()
        {
            _v[0] = 1f; Run(0.1f); _v[0] = -1f; Run(0.1f); _v[0] = 0f;
            Run(3f);
            Assert.AreEqual(CalibrationStep.Sticks, _w.Step);
            Assert.AreEqual(1, _w.StickChannelsFound);
        }

        [Test]
        public void Cancel_StopsTheWizard()
        {
            _w.Cancel();
            Assert.IsTrue(_w.IsFinished);
            Run(1f);
            Assert.AreEqual(CalibrationStep.Cancelled, _w.Step);
        }
    }
}
