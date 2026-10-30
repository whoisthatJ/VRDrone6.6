using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GateRush.Input
{
    /// <summary>
    /// Text descriptions of Input System devices for diagnostics: name, layout, interface, VID/PID,
    /// capabilities JSON and every leaf axis/button. Also logs device changes and keeps a report file
    /// up to date (adb pull /sdcard/Android/data/com.gaterush.vr/files/input-devices.txt).
    /// </summary>
    public static class InputDeviceReport
    {
        public const string ReportFileName = "input-devices.txt";

        private static readonly Regex VendorRx = new Regex("\"vendorId\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex ProductRx = new Regex("\"productId\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        public static string ReportPath => Path.Combine(Application.persistentDataPath, ReportFileName);

        /// <summary>Extracts vendor/product IDs from a capabilities JSON (HID and Android both use vendorId/productId).</summary>
        public static bool TryGetVidPid(string capabilities, out int vid, out int pid)
        {
            vid = pid = 0;
            if (string.IsNullOrEmpty(capabilities)) return false;
            var v = VendorRx.Match(capabilities);
            var p = ProductRx.Match(capabilities);
            if (v.Success) vid = int.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture);
            if (p.Success) pid = int.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
            return v.Success || p.Success;
        }

        public static string VidPidText(InputDevice device) =>
            TryGetVidPid(device.description.capabilities, out int vid, out int pid) ? $"VID 0x{vid:X4}  PID 0x{pid:X4}" : "VID/PID n/a";

        /// <summary>One-line summary for lists.</summary>
        public static string Summary(InputDevice device) =>
            $"{device.displayName}  [{device.layout}]  {device.description.interfaceName}  {VidPidText(device)}";

        /// <summary>Full multi-line description including every leaf axis with its current value.</summary>
        public static string Describe(InputDevice device, bool includeValues = true)
        {
            var d = device.description;
            var sb = new StringBuilder();
            sb.AppendLine($"Device: {device.displayName}");
            sb.AppendLine($"  name={device.name} id={device.deviceId} layout={device.layout} enabled={device.enabled}");
            sb.AppendLine($"  interface={d.interfaceName} product={d.product} manufacturer={d.manufacturer} serial={d.serial} version={d.version}");
            sb.AppendLine($"  deviceClass={d.deviceClass} {VidPidText(device)}");
            sb.AppendLine($"  capabilities={d.capabilities}");
            sb.AppendLine("  controls:");
            foreach (var c in device.allControls)
            {
                if (c.children.Count > 0 || !(c is AxisControl axis)) continue;
                sb.Append("    ").Append(c.path).Append("  (").Append(c.layout).Append(')');
                if (includeValues) sb.Append(" = ").Append(axis.ReadValue().ToString("F3", CultureInfo.InvariantCulture));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string DescribeAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"GateRush input report  {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  {SystemInfo.deviceModel}  {SystemInfo.operatingSystem}");
            sb.AppendLine($"{InputSystem.devices.Count} devices");
            foreach (var d in InputSystem.devices) sb.AppendLine(Describe(d));
            return sb.ToString();
        }

        public static string WriteReport()
        {
            string path = ReportPath;
            try
            {
                File.WriteAllText(path, DescribeAll());
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[GateRush][Input] Could not write {path}: {e.Message}");
            }
            return path;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallLogger()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onDeviceChange += OnDeviceChange;
            foreach (var d in InputSystem.devices) Debug.Log("[GateRush][Input] Present: " + Summary(d));
            WriteReport();
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.UsageChanged || change == InputDeviceChange.ConfigurationChanged) return;
            Debug.Log($"[GateRush][Input] {change}: {Summary(device)}\n{Describe(device, false)}");
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed || change == InputDeviceChange.Reconnected)
                WriteReport();
        }
    }
}
