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

            library.Presets.Add(Preset("LR2SIM_Joystick", "BetaFPV LiteRadio 2 SIM (joystick mode)", "^BETAFPV Joystick$", 20, Aetr(),
                "USB-C, joystick mode (default). Reported name \"BETAFPV Joystick\". 8 channels, switches A-D. Mapping is a guess: please verify with Input diagnostics."));
            library.Presets.Add(Preset("LR2SIM_Xbox", "BetaFPV LiteRadio 2 SIM (Xbox mode)", "BETAFPV.*(Xbox|XInput|Gamepad)|(Xbox|XInput).*BETAFPV", 30, Aetr(),
                "Xbox mode appears as a gamepad: flown through the gamepad path with full-range (radio-style) throttle.", gamepadPath: true));
            library.Presets.Add(Preset("LR3", "BetaFPV LiteRadio 3", "LiteRadio.?3|BETAFPV.*LR3", 20, Aetr(),
                "USB-C HID joystick. May need Android/joystick compatibility enabled in BETAFPV Configurator."));
            library.Presets.Add(Preset("LR4_USB", "BetaFPV LiteRadio 4 / 4 SE (USB)", "LiteRadio.?4|BETAFPV.*(LR4|ELRS)", 20, Aetr(),
                "USB-C joystick only outputs while the radio is switched OFF and powered by the cable. 10 channels."));
            library.Presets.Add(Preset("LR4_BT", "BetaFPV LiteRadio 4 / 4 SE (Bluetooth)", "(LiteRadio|BETAFPV|ELRS).*(BLE|Bluetooth|BT\\b)", 25, Aetr(),
                "Bluetooth joystick mode, paired in Quest Settings > Devices > Bluetooth."));
            library.Presets.Add(Preset("BetaFPV_Generic", "BetaFPV radio (generic)", "BETAFPV|LiteRadio", 0, Aetr(),
                "Fallback for other BetaFPV radios."));

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
