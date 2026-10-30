using System.Reflection;
using GateRush.Flight;
using GateRush.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;

namespace GateRush.Tests
{
    /// <summary>Simulated USB radios through the real Input System (InputTestFixture isolates the device list).</summary>
    public class RadioDeviceTests : InputTestFixture
    {
        // An 8-channel HID joystick like a BetaFPV LiteRadio in joystick mode: 4 stick axes, 2 switch axes, 2 buttons.
        private const string RadioLayout = @"{
            ""name"": ""TestRadio"",
            ""extend"": ""Joystick"",
            ""device"": { ""deviceClass"": ""TestRadio"" },
            ""controls"": [
                { ""name"": ""z"", ""layout"": ""Axis"", ""format"": ""FLT"", ""offset"": 12 },
                { ""name"": ""rx"", ""layout"": ""Axis"", ""format"": ""FLT"", ""offset"": 16 },
                { ""name"": ""ry"", ""layout"": ""Axis"", ""format"": ""FLT"", ""offset"": 20 },
                { ""name"": ""rz"", ""layout"": ""Axis"", ""format"": ""FLT"", ""offset"": 24 },
                { ""name"": ""button2"", ""layout"": ""Button"", ""format"": ""BIT"", ""offset"": 28, ""bit"": 0 }
            ]
        }";

        private InputDevice AddRadio(string product)
        {
            InputSystem.RegisterLayout(RadioLayout);
            return InputSystem.AddDevice(new InputDeviceDescription { interfaceName = "HID", product = product, deviceClass = "TestRadio" });
        }

        [Test]
        public void ChannelReader_ListsAxesThenButtons_WithoutHardCodedLayout()
        {
            var radio = AddRadio("BETAFPV Joystick");
            Assert.IsTrue(InputSystemChannelReader.IsRadioCandidate(radio));
            var reader = new InputSystemChannelReader(radio);
            // stick/x, stick/y, z, rx, ry, rz = 6 axes; trigger + button2 = 2 buttons (stick up/down/left/right are synthetic).
            Assert.AreEqual(6, reader.AxisCount);
            Assert.AreEqual(8, reader.ChannelCount);
            Assert.AreEqual("x", reader.ChannelName(0));
            Assert.AreEqual("z", reader.ChannelName(2));
            StringAssert.StartsWith("BETAFPV Joystick|", reader.DeviceKey);

            Set((AxisControl)radio["z"], 0.75f);
            Assert.AreEqual(0.75f, reader.Read(2), 1e-4f);
            Press((ButtonControl)radio["button2"]);
            int button2 = -1;
            for (int i = reader.AxisCount; i < reader.ChannelCount; i++) if (reader.ChannelName(i) == "button2") button2 = i;
            Assert.That(button2, Is.GreaterThanOrEqualTo(reader.AxisCount), "buttons come after axes");
            Assert.AreEqual(1f, reader.Read(button2), 1e-4f);
            Assert.AreEqual(0f, reader.Read(99), "out of range reads 0");
        }

        [Test]
        public void GamepadsAndXrControllersAreNotRadios()
        {
            var pad = InputSystem.AddDevice<Gamepad>();
            Assert.IsFalse(InputSystemChannelReader.IsRadioCandidate(pad));
            var kb = InputSystem.AddDevice<Keyboard>();
            Assert.IsFalse(InputSystemChannelReader.IsRadioCandidate(kb));
        }

        [Test]
        public void RcRadioSource_FliesFromPresetMapping_WithArmAndModeSwitches()
        {
            var radio = AddRadio("BETAFPV Joystick");

            var preset = ScriptableObject.CreateInstance<ControllerPreset>();
            preset.DisplayName = "Test LR2";
            preset.NamePattern = "^BETAFPV Joystick$";
            preset.Mapping = new RadioMapping
            {
                Roll = new AxisAssignment(0), Pitch = new AxisAssignment(1),
                Throttle = new AxisAssignment(2), Yaw = new AxisAssignment(3),
                Arm = new SwitchAssignment(4, -1f, 1f),
                Mode = new SwitchAssignment(5, -1f, 0f) { Position2 = 1f, ThreePosition = true },
            };
            var library = ScriptableObject.CreateInstance<ControllerPresetLibrary>();
            library.Presets.Add(preset);

            var go = new GameObject("Radio");
            try
            {
                var provider = go.AddComponent<RadioDeviceProvider>();
                SetField(provider, "presets", library);
                var source = go.AddComponent<RcRadioSource>();
                source.Provider = provider;
                provider.Select();

                Assert.IsNotNull(provider.Reader);
                Assert.AreSame(preset, provider.Preset);
                Assert.IsFalse(provider.IsCalibrated);
                Assert.IsTrue(source.IsConnected);
                Assert.AreEqual(30, source.Priority);

                var stick = (StickControl)radio["stick"];
                var z = (AxisControl)radio["z"];
                var rx = (AxisControl)radio["rx"];
                var ry = (AxisControl)radio["ry"];
                var rz = (AxisControl)radio["rz"];

                // Throttle low, disarmed, acro.
                Set(z, -1f);
                Set(ry, -1f);
                Set(rz, -1f);
                Tick(source);
                Assert.IsFalse(source.Current.Arm);
                Assert.AreEqual(0f, source.Current.Throttle, 1e-4f);

                // Arm switch on with throttle low → armed.
                Set(ry, 1f);
                Tick(source);
                Assert.IsTrue(source.Current.Arm);

                // Sticks and mode.
                Set(stick, new Vector2(0.5f, -0.25f));
                Set(z, 0f);
                Set(rx, 1f);
                Set(rz, 0f);
                Tick(source);
                Assert.AreEqual(0.5f, source.Current.Throttle, 1e-3f);
                Assert.AreEqual(1f, source.Current.Yaw, 1e-3f);
                Assert.That(source.Current.Roll, Is.GreaterThan(0.3f));
                Assert.That(source.Current.Pitch, Is.LessThan(-0.1f));
                Assert.AreEqual(FlightMode.Angle, source.Current.Mode);

                Set(rz, 1f);
                Tick(source);
                Assert.AreEqual(FlightMode.Horizon, source.Current.Mode);

                // Switch off disarms.
                Set(ry, -1f);
                Tick(source);
                Assert.IsFalse(source.Current.Arm);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(library);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void Provider_PrefersSavedCalibrationOverPreset()
        {
            var radio = AddRadio("Some Radio");
            var go = new GameObject("Radio");
            try
            {
                var provider = go.AddComponent<RadioDeviceProvider>();
                provider.Select();
                Assert.IsNotNull(provider.Reader);
                Assert.IsNull(provider.Preset, "unknown radio → generic AETR guess");
                Assert.AreEqual(2, provider.Mapping.Throttle.Channel);

                var calibrated = new RadioMapping { Throttle = new AxisAssignment(5) };
                provider.Store.Set(provider.Reader.DeviceKey, calibrated); // in memory only (no Save)
                provider.Select();
                Assert.IsTrue(provider.IsCalibrated);
                Assert.AreEqual(5, provider.Mapping.Throttle.Channel);

                InputSystem.RemoveDevice(radio);
                provider.Select();
                Assert.IsNull(provider.Reader, "hot-unplug clears the radio");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void Tick(PilotInputSource source) =>
            typeof(PilotInputSource).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(source, null);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }
}
