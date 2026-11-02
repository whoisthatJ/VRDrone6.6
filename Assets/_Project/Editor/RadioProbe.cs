using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace GateRush.EditorTools
{
    /// <summary>
    /// GateRush > Diagnostics > Probe Radio: lists every Input System device with its controls and raw values, then
    /// records live input for a few seconds (events per device, min/max of every raw axis and button). Works in edit
    /// mode and Play mode. Report in <see cref="OutPath"/>. Use it when a radio shows up but its sticks do nothing.
    /// </summary>
    public static class RadioProbe
    {
        public const string OutPath = "Builds/radio-probe.txt";

        private sealed class Range
        {
            public float Min = float.MaxValue, Max = float.MinValue, First = float.NaN;
        }

        private static readonly Dictionary<InputDevice, int> Events = new Dictionary<InputDevice, int>();
        private static readonly Dictionary<InputControl, Range> Ranges = new Dictionary<InputControl, Range>();
        private static double _end;
        private static bool _running;

        [MenuItem("GateRush/Diagnostics/Probe Radio (20 s)")]
        private static void Menu() => Start(20f);

        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Input System {InputSystem.version}, background behaviour {InputSystem.settings.backgroundBehavior}, " +
                          $"editor play-mode input {InputSystem.settings.editorInputBehaviorInPlayMode}, update mode {InputSystem.settings.updateMode}, " +
                          $"Application.isFocused {Application.isFocused}, runInBackground {Application.runInBackground}");
            foreach (var d in InputSystem.devices)
            {
                var desc = d.description;
                sb.AppendLine($"* {d.name} | layout {d.layout} ({d.GetType().Name}) | interface {desc.interfaceName} | product '{desc.product}' | " +
                              $"manufacturer '{desc.manufacturer}' | enabled {d.enabled} | native {d.native} | id {d.deviceId} | last update {d.lastUpdateTime:F2}");
                if (d is Keyboard || d is Mouse || d is Touchscreen) continue;
                foreach (var c in Leaves(d))
                    sb.AppendLine($"    {c.path} [{c.layout}] raw={Raw(c):0.####}");
                string caps = desc.capabilities ?? "";
                sb.AppendLine("    capabilities: " + (caps.Length > 1500 ? caps.Substring(0, 1500) + "…" : caps));
            }
            foreach (var d in InputSystem.disconnectedDevices) sb.AppendLine($"(disconnected) {d.name}");
            return sb.ToString();
        }

        private static IEnumerable<InputControl> Leaves(InputDevice d) =>
            d.allControls.Where(c => !c.synthetic && c.children.Count == 0);

        private static float Raw(InputControl c)
        {
            try
            {
                return c switch
                {
                    AxisControl a => a.ReadUnprocessedValue(),
                    _ => c.ReadValueAsObject() is float f ? f : 0f,
                };
            }
            catch
            {
                return float.NaN;
            }
        }

        public static string Start(float seconds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, $"running probe for {seconds:F0} s — move every stick and flip every switch now\n\n" + Describe());
            Events.Clear();
            Ranges.Clear();
            _end = EditorApplication.timeSinceStartup + seconds;
            if (!_running)
            {
                InputSystem.onEvent += OnEvent;
                EditorApplication.update += Tick;
                _running = true;
            }
            return OutPath;
        }

        private static void OnEvent(InputEventPtr e, InputDevice device)
        {
            if (device == null || device is Keyboard || device is Mouse || device is Touchscreen) return;
            Events[device] = Events.TryGetValue(device, out int n) ? n + 1 : 1;
        }

        private static void Tick()
        {
            // Sample every control each editor tick (the event hook above only counts).
            foreach (var d in InputSystem.devices)
            {
                if (d is Keyboard || d is Mouse || d is Touchscreen) continue;
                foreach (var c in Leaves(d))
                {
                    float v = Raw(c);
                    if (float.IsNaN(v)) continue;
                    if (!Ranges.TryGetValue(c, out var r)) Ranges[c] = r = new Range { First = v };
                    r.Min = Mathf.Min(r.Min, v);
                    r.Max = Mathf.Max(r.Max, v);
                }
            }
            if (EditorApplication.timeSinceStartup < _end) return;

            InputSystem.onEvent -= OnEvent;
            EditorApplication.update -= Tick;
            _running = false;
            var sb = new StringBuilder("done probe\n\n");
            foreach (var d in InputSystem.devices)
            {
                if (d is Keyboard || d is Mouse || d is Touchscreen) continue;
                sb.AppendLine($"* {d.name} ({d.layout}): {(Events.TryGetValue(d, out int n) ? n : 0)} input events");
                foreach (var c in Leaves(d))
                {
                    if (!Ranges.TryGetValue(c, out var r)) continue;
                    bool moved = r.Max - r.Min > 0.02f;
                    sb.AppendLine($"    {(moved ? "MOVED " : "      ")}{c.path}: {r.Min:0.###} .. {r.Max:0.###} (first {r.First:0.###})");
                }
            }
            sb.AppendLine().AppendLine(Describe());
            File.WriteAllText(OutPath, sb.ToString());
        }
    }
}
