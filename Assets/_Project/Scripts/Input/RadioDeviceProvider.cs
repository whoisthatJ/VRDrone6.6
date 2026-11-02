using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GateRush.Input
{
    public enum RadioBackend
    {
        /// <summary>
        /// Windows in Play mode: the radio straight from USB (Windows HID). Otherwise the Input System, unless the
        /// Android raw reader sees more axes for the radio.
        /// </summary>
        Auto = 0,
        InputSystem = 1,
        AndroidRaw = 2,
        /// <summary>Windows only: read the radio's USB HID reports directly (falls back to the Input System).</summary>
        WindowsHid = 3,
    }

    /// <summary>
    /// Finds the connected RC radio (any joystick/HID via the Input System, or the Android raw reader), picks its
    /// preset by name, and resolves the mapping to use: saved calibration, else preset, else a generic AETR guess.
    /// Re-selects on hot-plug. One per scene; <see cref="RcRadioSource"/> and the radio menu read from it.
    /// </summary>
    [DefaultExecutionOrder(-120)]
    public sealed class RadioDeviceProvider : MonoBehaviour
    {
        [SerializeField] private ControllerPresetLibrary presets;
        [SerializeField] private RadioBackend backend = RadioBackend.Auto;

        private readonly RadioCalibrationStore _store = new RadioCalibrationStore();
        private readonly int[] _androidIds = new int[AndroidJoystickBridge.MaxDevices];
        private bool _dirty = true;

        /// <summary>Fired after the radio (or its mapping) changes: connect, disconnect, calibration.</summary>
        public event Action Changed;

        public IChannelReader Reader { get; private set; }
        public ControllerPreset Preset { get; private set; }
        public RadioMapping Mapping { get; private set; } = new RadioMapping();
        public bool IsCalibrated { get; private set; }
        /// <summary>A gamepad that is really a radio in Xbox mode (preset with UsesGamepadPath), or null.</summary>
        public ControllerPreset GamepadPreset { get; private set; }
        public ControllerPresetLibrary Presets => presets;
        public RadioCalibrationStore Store => _store;

        public RadioBackend Backend
        {
            get => backend;
            set
            {
                if (backend == value) return;
                backend = value;
                _dirty = true;
            }
        }

        private void Awake()
        {
            _store.Load(RadioCalibrationStore.DefaultPath);
            AndroidJoystickBridge.Install();
        }

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void OnDestroy()
        {
            (Reader as IDisposable)?.Dispose();
            Reader = null;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected)
                _dirty = true;
        }

        private void Update()
        {
            AndroidJoystickBridge.Poll();
            if (AndroidJoystickBridge.Available)
            {
                for (int s = 0; s < _androidIds.Length; s++)
                {
                    int id = AndroidJoystickBridge.Get(s).DeviceId;
                    if (id == _androidIds[s]) continue;
                    _androidIds[s] = id;
                    _dirty = true;
                }
            }
            if (Reader != null && !Reader.IsConnected) _dirty = true;
            if (_dirty) Select();
        }

        /// <summary>Saves a calibration for the current radio and starts using it.</summary>
        public void SaveCalibration(RadioMapping mapping)
        {
            if (Reader == null) return;
            _store.Set(Reader.DeviceKey, mapping);
            _store.Save(RadioCalibrationStore.DefaultPath);
            Select();
        }

        /// <summary>Forgets the current radio's calibration (back to its preset).</summary>
        public void ClearCalibration()
        {
            if (Reader == null) return;
            _store.Remove(Reader.DeviceKey);
            _store.Save(RadioCalibrationStore.DefaultPath);
            Select();
        }

        public void Select()
        {
            _dirty = false;
            string previous = Reader?.DeviceKey;

            InputSystemChannelReader inputSystem = null;
            foreach (var d in InputSystem.devices)
            {
                if (!InputSystemChannelReader.IsRadioCandidate(d)) continue;
                inputSystem = new InputSystemChannelReader(d);
                break;
            }

            AndroidChannelReader android = null;
            if (AndroidJoystickBridge.Available)
            {
                for (int s = 0; s < AndroidJoystickBridge.MaxDevices; s++)
                {
                    if (!AndroidJoystickBridge.Get(s).Present) continue;
                    android = new AndroidChannelReader(s);
                    break;
                }
            }

            var old = Reader;
            IChannelReader windowsHid = null;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // The Input System can drop a USB radio's reports (focus resets, devices that only report on change), so
            // on Windows the radio is read from USB directly. Edit mode (tests, tools) keeps the Input System.
            if (backend == RadioBackend.WindowsHid || backend == RadioBackend.Auto && Application.isPlaying)
            {
                if (old is WindowsHidChannelReader current && current.IsConnected) windowsHid = current;
                else
                {
                    var gamepads = new HashSet<int>();
                    foreach (var pad in Gamepad.all)
                        if (InputDeviceReport.TryGetVidPid(pad.description.capabilities, out int vid, out int pid)) gamepads.Add(vid << 16 | pid);
                    windowsHid = WindowsHidChannelReader.OpenFirst(gamepads);
                }
            }
#endif

            switch (backend)
            {
                case RadioBackend.WindowsHid: Reader = windowsHid ?? inputSystem; break;
                case RadioBackend.InputSystem: Reader = inputSystem; break;
                case RadioBackend.AndroidRaw: Reader = (IChannelReader)android ?? inputSystem; break;
                default:
                    // Prefer the Input System; fall back to the raw reader when it sees more axes (or is the only one).
                    Reader = windowsHid ?? (inputSystem != null && (android == null || android.AxisCount <= inputSystem.AxisCount)
                        ? inputSystem
                        : (IChannelReader)android);
                    break;
            }
            if (old != Reader) (old as IDisposable)?.Dispose();
            if (windowsHid != null && windowsHid != Reader) ((IDisposable)windowsHid).Dispose();

            Preset = Reader != null && presets != null ? presets.Find(Reader.ProductName, Reader.DisplayName) : null;
            var saved = Reader != null ? _store.Find(Reader.DeviceKey) : null;
            IsCalibrated = saved != null;
            Mapping = saved?.Clone() ?? Preset?.Mapping.Clone() ?? new RadioMapping();

            GamepadPreset = null;
            if (presets != null)
            {
                foreach (var pad in Gamepad.all)
                {
                    var p = presets.Find(pad.description.product, pad.displayName);
                    if (p != null && p.UsesGamepadPath) GamepadPreset = p;
                }
            }

            if (Reader?.DeviceKey != previous || Reader?.Backend != old?.Backend)
            {
                Debug.Log(Reader != null
                    ? $"[GateRush][Input] Radio: {Reader.DisplayName} via {Reader.Backend}, {Reader.ChannelCount} channels, " +
                      $"preset {(Preset != null ? Preset.DisplayName + (Preset.Verified ? "" : " (unverified)") : "none")}, " +
                      $"{(IsCalibrated ? "calibrated" : "not calibrated")}"
                    : "[GateRush][Input] Radio: none");
            }
            Changed?.Invoke();
        }
    }
}
