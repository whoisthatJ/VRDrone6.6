using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GateRush.Input
{
    /// <summary>A radio as a flat list of raw channels (axes first, then buttons), whatever the backend.</summary>
    public interface IChannelReader
    {
        /// <summary>Stable key for storing a calibration (name + VID/PID).</summary>
        string DeviceKey { get; }
        string ProductName { get; }
        string DisplayName { get; }
        /// <summary>"Input System" or "Android raw".</summary>
        string Backend { get; }
        bool IsConnected { get; }
        int ChannelCount { get; }
        string ChannelName(int index);
        /// <summary>Raw value; buttons read 0 or 1. Out-of-range indices read 0.</summary>
        float Read(int index);
    }

    /// <summary>
    /// Generic reader for any Input System joystick/HID device: every non-synthetic leaf axis, then every button.
    /// No hard-coded layout, so unknown radios work too.
    /// </summary>
    public sealed class InputSystemChannelReader : IChannelReader
    {
        private readonly InputDevice _device;
        private readonly List<AxisControl> _channels = new List<AxisControl>();

        // Canonical HID axis order, so channel numbers (used by presets) don't depend on control enumeration order.
        private static readonly string[] AxisOrder = { "x", "y", "z", "rx", "ry", "rz", "throttle", "rudder", "slider", "dial", "wheel" };

        public InputSystemChannelReader(InputDevice device)
        {
            _device = device;
            var axes = new List<AxisControl>();
            var buttons = new List<AxisControl>();
            foreach (var c in device.allControls)
            {
                if (c.children.Count > 0 || c.synthetic || !(c is AxisControl a)) continue;
                if (c is ButtonControl) buttons.Add(a);
                else axes.Add(a);
            }
            axes.Sort(CompareAxes);
            buttons.Sort((p, q) => string.CompareOrdinal(p.path, q.path));
            _channels.AddRange(axes);
            AxisCount = axes.Count;
            _channels.AddRange(buttons);
            DeviceKey = MakeKey(device);
        }

        private static int CompareAxes(AxisControl p, AxisControl q)
        {
            int rp = Rank(p.name), rq = Rank(q.name);
            return rp != rq ? rp.CompareTo(rq) : string.CompareOrdinal(p.path, q.path);
        }

        private static int Rank(string name)
        {
            int i = System.Array.IndexOf(AxisOrder, name);
            return i < 0 ? AxisOrder.Length : i;
        }

        public InputDevice Device => _device;
        public int AxisCount { get; }
        public string DeviceKey { get; }
        public string ProductName => _device.description.product;
        public string DisplayName => _device.displayName;
        public string Backend => "Input System";
        public bool IsConnected => _device.added;
        public int ChannelCount => _channels.Count;
        public string ChannelName(int index) => index >= 0 && index < _channels.Count ? _channels[index].name : "-";
        public AxisControl Control(int index) => _channels[index];
        // Unprocessed: joystick layouts put a stick deadzone processor on stick/x,y, which would add a hidden ~12% deadband
        // to radio gimbals. Calibration and StickShaping handle deadband explicitly instead.
        public float Read(int index) => index >= 0 && index < _channels.Count && _device.added ? _channels[index].ReadUnprocessedValue() : 0f;

        public static string MakeKey(InputDevice d)
        {
            InputDeviceReport.TryGetVidPid(d.description.capabilities, out int vid, out int pid);
            string name = string.IsNullOrEmpty(d.description.product) ? d.displayName : d.description.product;
            return $"{name}|{vid:X4}|{pid:X4}";
        }

        /// <summary>
        /// True for devices that could be an RC radio: joysticks and generic HIDs, not gamepads, XR controllers,
        /// keyboards, mice or sensors.
        /// </summary>
        public static bool IsRadioCandidate(InputDevice d)
        {
            if (d is Joystick) return true;
            if (d is Gamepad || d is Keyboard || d is Mouse || d is Pointer || d is Sensor) return false;
            return d.description.interfaceName == "HID";
        }
    }
}
