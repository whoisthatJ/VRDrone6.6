using GateRush.Input;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Creates the shipped radio presets and their library in Assets/_Project/Data/Radios.
    /// Every mapping is a best guess (AETR on X/Y/Z/Rx, arm on Ry, 3-position mode on Rz) and stays
    /// Verified = false until checked on real hardware with Input diagnostics.
    /// </summary>
    public static class PresetBuilder
    {
        public const string Dir = "Assets/_Project/Data/Radios";
        public const string LibraryPath = Dir + "/ControllerPresets.asset";

        [MenuItem("GateRush/Builders/Build Radio Presets")]
        public static ControllerPresetLibrary BuildAll()
        {
            System.IO.Directory.CreateDirectory(Dir);
            var library = LoadOrCreate<ControllerPresetLibrary>(LibraryPath);
            library.Presets.Clear();

            library.Presets.Add(Preset("LR2SIM_Joystick", "BetaFPV LiteRadio 2 SIM (joystick mode)", "^BETAFPV Joystick$", 20, BetaFpvJoystick(),
                "USB-C, joystick mode (default). Reported name \"BETAFPV Joystick\". 8 channels, switches A-D. Mapping is a guess: please verify with Input diagnostics."));
            library.Presets.Add(Preset("LR2SIM_Xbox", "BetaFPV LiteRadio 2 SIM (Xbox mode)", "BETAFPV.*(Xbox|XInput|Gamepad)|(Xbox|XInput).*BETAFPV", 30, Aetr(),
                "Xbox mode appears as a gamepad: flown through the gamepad path with full-range (radio-style) throttle.", gamepadPath: true));
            library.Presets.Add(Preset("LR3", "BetaFPV LiteRadio 3", "LiteRadio.?3|BETAFPV.*LR3", 20, Aetr(),
                "USB-C HID joystick. May need Android/joystick compatibility enabled in BETAFPV Configurator."));
            library.Presets.Add(Preset("LR4_USB", "BetaFPV LiteRadio 4 / 4 SE (USB)", "LiteRadio.?4|BETAFPV.*(LR4|ELRS)", 20, Aetr(),
                "USB-C joystick only outputs while the radio is switched OFF and powered by the cable. 10 channels."));
            library.Presets.Add(Preset("LR4_BT", "BetaFPV LiteRadio 4 / 4 SE (Bluetooth)", "(LiteRadio|BETAFPV|ELRS).*(BLE|Bluetooth|BT\\b)", 25, Aetr(),
                "Bluetooth joystick mode, paired in Quest Settings > Devices > Bluetooth."));
            library.Presets.Add(Preset("BetaFPV_Generic", "BetaFPV radio (generic)", "BETAFPV|LiteRadio", 0, BetaFpvGeneric(),
                "Fallback for other BetaFPV radios."));
            library.Presets.Add(Preset("RM_Pocket", "RadioMaster Pocket (EdgeTX USB joystick)", "(RadioMaster|EdgeTX|OpenTX).*Pocket|Pocket.*(Joystick|EdgeTX)", 20, EdgeTxAetr(),
                "Power the radio ON, plug in USB and pick \"USB Joystick (HID)\". EdgeTX sends CH1-CH8 on X/Y/Z/Rx/Ry/Rz/Slider/Dial. " +
                "Model needs AETR order plus mixes: SA -> CH5 (arm), SB -> CH6 (flight mode). No driver needed on Windows."));
            library.Presets.Add(Preset("EdgeTX_Generic", "EdgeTX / OpenTX radio (generic)", "EdgeTX|OpenTX|RadioMaster|Jumper|FrSky|\\bTBS\\b", 0, EdgeTxAetr(),
                "Fallback for other EdgeTX/OpenTX radios in USB joystick mode (TX16S, Boxer, Zorro, Jumper, Taranis...)."));

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static RadioMapping Aetr() => new RadioMapping
        {
            Roll = new AxisAssignment(0),
            Pitch = new AxisAssignment(1),
            Throttle = new AxisAssignment(2),
            Yaw = new AxisAssignment(3),
            Arm = new SwitchAssignment(4, -1f, 1f),
            Mode = new SwitchAssignment(5, -1f, 0f) { Position2 = 1f, ThreePosition = true },
            Reset = SwitchAssignment.None,
        };

        /// <summary>
        /// "BETAFPV Joystick" (VID 0483 / PID 572B) over USB: the arm switch is a button, not an axis. It shows up as
        /// button4, which is channel 17 in <see cref="InputSystemChannelReader"/> order (8 axes, then buttons sorted by
        /// path: button10..16, button2..9, trigger). Checked on hardware with Input diagnostics.
        /// The gimbals only cover about ±0.8 of the HID range (throttle bottom reads -0.8), so the ranges are narrowed;
        /// throttle min sits just above that so stick-down reads 0 and the arm check (throttle ≤ 5%) passes.
        /// </summary>
        private static RadioMapping BetaFpvJoystick()
        {
            var m = Aetr();
            var sticks = new ChannelRange(-0.8f, 0f, 0.8f);
            m.Throttle.Range = new ChannelRange(-0.78f, 0f, 0.8f);
            m.Yaw.Range = sticks;
            m.Pitch.Range = sticks;
            m.Roll.Range = sticks;
            m.Arm = new SwitchAssignment(17, 0f, 1f);
            return m;
        }

        /// <summary>Other BetaFPV radios: AETR with the arm switch on button4 (channel 17) like the LiteRadio 2 SIM, full stick ranges.</summary>
        private static RadioMapping BetaFpvGeneric()
        {
            var m = Aetr();
            m.Arm = new SwitchAssignment(17, 0f, 1f);
            return m;
        }

        /// <summary>
        /// AETR for EdgeTX's classic USB joystick (CH1..CH8 on X, Y, Z, Rx, Ry, Rz, Slider, Dial).
        /// The Input System's HID fallback inverts Y and Ry, so pitch (CH2) is un-inverted here and the
        /// arm switch on CH5 reads +1 when the switch is in its EdgeTX -100 (disarmed) position.
        /// </summary>
        private static RadioMapping EdgeTxAetr()
        {
            var m = Aetr();
            m.Pitch = new AxisAssignment(1, invert: true);
            m.Arm = new SwitchAssignment(4, 1f, -1f);
            return m;
        }

        private static ControllerPreset Preset(string file, string name, string pattern, int priority, RadioMapping mapping, string notes, bool gamepadPath = false)
        {
            var p = LoadOrCreate<ControllerPreset>($"{Dir}/{file}.asset");
            p.DisplayName = name;
            p.NamePattern = pattern;
            p.Priority = priority;
            p.Verified = false;
            p.UsesGamepadPath = gamepadPath;
            p.Notes = notes;
            p.Mapping = mapping;
            EditorUtility.SetDirty(p);
            return p;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
