using GateRush.Input;
using NUnit.Framework;
using UnityEditor;

namespace GateRush.Tests
{
    public class PcPlatformTests
    {
        private const string LibraryPath = "Assets/_Project/Data/Radios/ControllerPresets.asset";

        [TestCase("EdgeTX Radiomaster Pocket Joystick", "RadioMaster Pocket (EdgeTX USB joystick)")]
        [TestCase("RadioMaster Pocket", "RadioMaster Pocket (EdgeTX USB joystick)")]
        [TestCase("EdgeTX Radiomaster TX16S Joystick", "EdgeTX / OpenTX radio (generic)")]
        [TestCase("FrSky Taranis Joystick", "EdgeTX / OpenTX radio (generic)")]
        [TestCase("BETAFPV Joystick", "BetaFPV LiteRadio 2 SIM (joystick mode)")]
        public void ShippedLibrary_RoutesRadioNamesToPresets(string product, string expected)
        {
            var library = AssetDatabase.LoadAssetAtPath<ControllerPresetLibrary>(LibraryPath);
            Assert.IsNotNull(library, "run GateRush/Builders/Build Radio Presets");
            var preset = library.Find(product, product);
            Assert.IsNotNull(preset, product);
            Assert.AreEqual(expected, preset.DisplayName);
        }

        [Test]
        public void ShippedLibrary_DoesNotClaimUnrelatedJoysticks()
        {
            var library = AssetDatabase.LoadAssetAtPath<ControllerPresetLibrary>(LibraryPath);
            Assert.IsNull(library.Find("Logitech Extreme 3D", "Logitech Extreme 3D"));
            Assert.IsNull(library.Find("Pocket Gamepad", "Pocket Gamepad"));
        }

        [Test]
        public void PocketPreset_UnInvertsHidY_AndFlipsArmOnRy()
        {
            var pocket = AssetDatabase.LoadAssetAtPath<ControllerPresetLibrary>(LibraryPath).Find("RadioMaster Pocket", "");
            var m = pocket.Mapping;
            Assert.AreEqual(1, m.Pitch.Channel);
            Assert.IsTrue(m.Pitch.Invert);
            Assert.IsFalse(m.Roll.Invert);
            Assert.AreEqual(4, m.Arm.Channel);
            Assert.AreEqual(1, RadioMapping.SwitchPosition(-1f, m.Arm), "Unity's HID fallback inverts Ry: -1 means the switch is at +100 (armed)");
            Assert.AreEqual(0, RadioMapping.SwitchPosition(1f, m.Arm));
        }
    }
}
