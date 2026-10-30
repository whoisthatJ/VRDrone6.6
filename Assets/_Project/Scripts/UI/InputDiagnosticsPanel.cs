using System.Collections.Generic;
using GateRush.Core;
using GateRush.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;

namespace GateRush.UI
{
    /// <summary>
    /// World-space input diagnostics: every Input System device with layout, interface, VID/PID and capabilities,
    /// plus a live bar (with min/max seen) for every leaf axis and button of the selected device.
    /// Navigation: ←/→ device, ↑/↓ page (keyboard arrows, gamepad d-pad, right Touch stick);
    /// save report: Enter / gamepad A / Touch A; toggle 72/90 Hz: F / Touch B.
    /// </summary>
    public sealed class InputDiagnosticsPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text deviceList;
        [SerializeField] private TMP_Text details;
        [SerializeField] private TMP_Text footer;
        [SerializeField] private RectTransform rowsRoot;
        [SerializeField] private int rowCount = 20;
        [SerializeField] private float rowHeight = 30f;

        private struct Row
        {
            public TMP_Text Name;
            public TMP_Text Value;
            public RectTransform Marker;
            public RectTransform Track;
        }

        private readonly List<InputDevice> _devices = new List<InputDevice>();
        private readonly List<AxisControl> _controls = new List<AxisControl>();
        private float[] _min = new float[0], _max = new float[0];
        private Row[] _rows;
        private int _deviceIndex, _page;
        private bool _devicesDirty = true, _pageDirty = true;
        private string _status = "";
        private XrDisplayConfig _display;

        private Vector2Control _touchStick;
        private ButtonControl _touchA, _touchB;
        private bool _touchDirty = true, _stickLatched;

        private void Awake() => BuildRows();

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            _devicesDirty = true;
            if (device is XRController) _touchDirty = true;
            _status = $"{change}: {device.displayName}";
            _pageDirty = true;
        }

        private void Update()
        {
            HandleNavigation();
            if (_devicesDirty) RebuildDevices();
            if (_pageDirty) RebuildPage();
            UpdateRows();
        }

        // ---------- navigation ----------

        private void HandleNavigation()
        {
            int dx = 0, dy = 0;
            bool save = false, toggleHz = false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.rightArrowKey.wasPressedThisFrame) dx++;
                if (kb.leftArrowKey.wasPressedThisFrame) dx--;
                if (kb.downArrowKey.wasPressedThisFrame) dy++;
                if (kb.upArrowKey.wasPressedThisFrame) dy--;
                save |= kb.enterKey.wasPressedThisFrame;
                toggleHz |= kb.fKey.wasPressedThisFrame;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.dpad.right.wasPressedThisFrame) dx++;
                if (pad.dpad.left.wasPressedThisFrame) dx--;
                if (pad.dpad.down.wasPressedThisFrame) dy++;
                if (pad.dpad.up.wasPressedThisFrame) dy--;
                save |= pad.buttonSouth.wasPressedThisFrame;
            }

            if (_touchDirty)
            {
                _touchDirty = false;
                var right = XRController.rightHand;
                _touchStick = right?.TryGetChildControl<Vector2Control>("thumbstick");
                _touchA = right?.TryGetChildControl<ButtonControl>("primaryButton");
                _touchB = right?.TryGetChildControl<ButtonControl>("secondaryButton");
            }
            if (_touchStick != null && !_touchStick.device.added) _touchDirty = true;
            else if (_touchStick != null)
            {
                Vector2 s = _touchStick.ReadValue();
                if (!_stickLatched && s.magnitude > 0.7f)
                {
                    _stickLatched = true;
                    if (Mathf.Abs(s.x) > Mathf.Abs(s.y)) dx += s.x > 0f ? 1 : -1;
                    else dy += s.y < 0f ? 1 : -1;
                }
                else if (s.magnitude < 0.3f) _stickLatched = false;
            }
            save |= _touchA != null && _touchA.device.added && _touchA.wasPressedThisFrame;
            toggleHz |= _touchB != null && _touchB.device.added && _touchB.wasPressedThisFrame;

            if (dx != 0 && _devices.Count > 0)
            {
                _deviceIndex = (_deviceIndex + dx + _devices.Count) % _devices.Count;
                SelectDevice();
            }
            if (dy != 0)
            {
                int pages = Mathf.Max(1, (_controls.Count + rowCount - 1) / rowCount);
                _page = Mathf.Clamp(_page + dy, 0, pages - 1);
                _pageDirty = true;
            }
            if (save)
            {
                _status = "Report saved: " + InputDeviceReport.WriteReport();
                _pageDirty = true;
            }
            if (toggleHz) ToggleRefreshRate();
        }

        private void ToggleRefreshRate()
        {
            if (_display == null) _display = FindAnyObjectByType<XrDisplayConfig>();
            if (_display == null)
            {
                _status = "No XrDisplayConfig (start from the Boot scene)";
                return;
            }
            _display.SetRefreshRate(_display.TargetRefreshRate >= 90f ? 72f : 90f);
            _status = $"Refresh rate requested: {_display.TargetRefreshRate:0} Hz";
            _pageDirty = true;
        }

        // ---------- content ----------

        private void RebuildDevices()
        {
            _devicesDirty = false;
            InputDevice selected = _deviceIndex < _devices.Count ? _devices[_deviceIndex] : null;
            _devices.Clear();
            foreach (var d in InputSystem.devices) _devices.Add(d);

            // Keep the selection if it still exists; otherwise prefer a joystick/HID (the radio).
            int keep = selected != null ? _devices.IndexOf(selected) : -1;
            if (keep < 0)
            {
                keep = 0;
                for (int i = 0; i < _devices.Count; i++)
                    if (_devices[i] is Joystick || _devices[i].description.interfaceName == "HID") { keep = i; break; }
            }
            _deviceIndex = keep;

            SelectDevice();
        }

        private void SelectDevice()
        {
            _controls.Clear();
            _page = 0;
            _pageDirty = true;
            if (_devices.Count == 0)
            {
                details.SetText("No devices");
                return;
            }

            var device = _devices[_deviceIndex];
            // Same channel order and numbering as the radio mapping (axes in HID order, then buttons).
            var reader = new InputSystemChannelReader(device);
            for (int i = 0; i < reader.ChannelCount; i++) _controls.Add(reader.Control(i));
            _min = new float[_controls.Count];
            _max = new float[_controls.Count];
            for (int i = 0; i < _controls.Count; i++)
            {
                _min[i] = float.MaxValue;
                _max[i] = float.MinValue;
            }

            string caps = device.description.capabilities ?? "";
            if (caps.Length > 700) caps = caps.Substring(0, 700) + "...";
            details.SetText(
                $"<b>{device.displayName}</b>\nlayout {device.layout}   interface {device.description.interfaceName}   {InputDeviceReport.VidPidText(device)}\n" +
                $"product \"{device.description.product}\"   manufacturer \"{device.description.manufacturer}\"   {_controls.Count} axes/buttons\n" +
                $"<size=70%>{caps}</size>");

            DrawDeviceList();
        }

        private void DrawDeviceList()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>{_devices.Count} input devices</b>   Left/Right: select   Up/Down: page");
            for (int i = 0; i < _devices.Count; i++)
                sb.AppendLine((i == _deviceIndex ? "<color=#ffb347>> " : "   ") + InputDeviceReport.Summary(_devices[i]) + (i == _deviceIndex ? "</color>" : ""));
            deviceList.SetText(sb.ToString());
        }

        private void RebuildPage()
        {
            _pageDirty = false;
            int start = _page * rowCount;
            for (int r = 0; r < _rows.Length; r++)
            {
                int i = start + r;
                bool used = i < _controls.Count;
                _rows[r].Name.gameObject.SetActive(used);
                _rows[r].Value.gameObject.SetActive(used);
                _rows[r].Track.gameObject.SetActive(used);
                // Path is "/<device>/<control>"; show just the control part.
                if (used) _rows[r].Name.SetText($"ch{i}  {_controls[i].path.Substring(_controls[i].device.name.Length + 2)}");
            }

            int pages = Mathf.Max(1, (_controls.Count + rowCount - 1) / rowCount);
            string hz = _display != null ? $"{_display.TargetRefreshRate:0} Hz" : "72 Hz (default)";
            footer.SetText($"page {_page + 1}/{pages}    Enter / A: save report    F / B: toggle 72/90 Hz ({hz})\n{_status}");
        }

        private void UpdateRows()
        {
            int start = _page * rowCount;
            for (int r = 0; r < _rows.Length; r++)
            {
                int i = start + r;
                if (i >= _controls.Count) break;
                float v = _controls[i].ReadValue();
                if (v < _min[i]) _min[i] = v;
                if (v > _max[i]) _max[i] = v;

                // Marker over a -1..1 track (0..1 controls use the right half).
                float x = Mathf.Clamp(v, -1f, 1f) * 0.5f + 0.5f;
                var row = _rows[r];
                row.Marker.anchorMin = new Vector2(x, 0f);
                row.Marker.anchorMax = new Vector2(x, 1f);
                row.Value.SetText("{0:3}   [{1:2} .. {2:2}]", v, _min[i], _max[i]);
            }

        }

        // ---------- layout ----------

        private void BuildRows()
        {
            _rows = new Row[rowCount];
            for (int r = 0; r < rowCount; r++)
            {
                float y = -r * rowHeight;
                var name = Text(rowsRoot, "Name", new Vector2(0f, y), new Vector2(520f, rowHeight), TextAlignmentOptions.MidlineLeft);
                var track = Rect(rowsRoot, "Track", new Vector2(540f, y - 6f), new Vector2(420f, rowHeight - 12f), new Color(1f, 1f, 1f, 0.12f));
                Rect(track, "Centre", Vector2.zero, new Vector2(2f, 0f), new Color(1f, 1f, 1f, 0.35f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f));
                var marker = Rect(track, "Marker", Vector2.zero, new Vector2(8f, 0f), new Color(1f, 0.7f, 0.28f, 1f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f));
                var value = Text(rowsRoot, "Value", new Vector2(980f, y), new Vector2(420f, rowHeight), TextAlignmentOptions.MidlineLeft);
                _rows[r] = new Row { Name = name, Value = value, Marker = marker, Track = track };
            }
        }

        private static TMP_Text Text(RectTransform parent, string name, Vector2 pos, Vector2 size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = 20f;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        private static RectTransform Rect(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color,
            Vector2? anchorMin = null, Vector2? anchorMax = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin ?? new Vector2(0f, 1f);
            rt.anchorMax = anchorMax ?? new Vector2(0f, 1f);
            rt.pivot = anchorMin.HasValue ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }
    }
}
