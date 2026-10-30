using System.Globalization;
using GateRush.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GateRush.UI
{
    /// <summary>Radio setup: status, live channels, calibration wizard and the BetaFPV connection guide.</summary>
    public sealed partial class MenuController
    {
        private const int LiveChannels = 12;

        [SerializeField] private RadioDeviceProvider radio;

        private TMP_Text _radioStatus;
        private TMP_Text[] _channelLabels;
        private TMP_Text[] _channelValues;
        private RectTransform[] _channelMarkers;
        private CalibrationWizard _wizard;
        private TMP_Text _wizardText;
        private RectTransform _wizardProgress;
        private float[] _values = new float[0];
        private float _statusTimer;
        private string _radioMessage = "";

        private void ClearRadioPage()
        {
            _radioStatus = null;
            _channelLabels = null;
            _channelValues = null;
            _channelMarkers = null;
            _wizard = null;
            _wizardText = null;
            _wizardProgress = null;
        }

        private void ShowRadio()
        {
            var p = NewPage("Radio setup");
            _radioStatus = UiKit.Label(p, "", new Vector2(Margin, -110f), new Vector2(ContentWidth, 150f), 28f, TextAlignmentOptions.TopLeft);
            BuildChannelBars(p, -270f);

            float y = -(Height - 200f);
            float w = (ContentWidth - 3f * 20f) / 4f;
            UiKit.Button(p, "Calibrate", new Vector2(Margin, y), new Vector2(w, 80f), StartWizard, 30f);
            UiKit.Button(p, "Use preset", new Vector2(Margin + (w + 20f), y), new Vector2(w, 80f), () =>
            {
                if (radio != null) radio.ClearCalibration();
                _radioMessage = "Calibration cleared: using the preset / default mapping.";
            }, 30f);
            UiKit.Button(p, "Reader: " + BackendName(S.Input.RadioBackend), new Vector2(Margin + 2f * (w + 20f), y), new Vector2(w, 80f), () =>
            {
                S.Input.RadioBackend = (S.Input.RadioBackend + 1) % 3;
                if (radio != null) radio.Backend = (RadioBackend)S.Input.RadioBackend;
                _dirty = true;
                ShowRadio();
            }, 26f);
            UiKit.Button(p, "Connection help", new Vector2(Margin + 3f * (w + 20f), y), new Vector2(w, 80f), ShowRadioHelp, 30f);
            BackButton(p);
            UpdateRadioStatus();
        }

        private static string BackendName(int b) => b == 1 ? "Input System" : b == 2 ? "Android raw" : "Auto";

        private void BuildChannelBars(RectTransform p, float top)
        {
            _channelLabels = new TMP_Text[LiveChannels];
            _channelValues = new TMP_Text[LiveChannels];
            _channelMarkers = new RectTransform[LiveChannels];
            float colW = (ContentWidth - 40f) * 0.5f;
            for (int i = 0; i < LiveChannels; i++)
            {
                float x = Margin + (i / 6) * (colW + 40f);
                float y = top - (i % 6) * 52f;
                _channelLabels[i] = UiKit.Label(p, "", new Vector2(x, y), new Vector2(230f, 46f), 24f);
                var track = UiKit.Rect(p, "Track", new Vector2(x + 240f, y - 10f), new Vector2(colW - 380f, 26f));
                UiKit.Image(track, new Color(1f, 1f, 1f, 0.12f));
                var mid = UiKit.Rect(track, "Centre", Vector2.zero, new Vector2(2f, 0f));
                mid.anchorMin = new Vector2(0.5f, 0f);
                mid.anchorMax = new Vector2(0.5f, 1f);
                mid.pivot = new Vector2(0.5f, 0.5f);
                UiKit.Image(mid, new Color(1f, 1f, 1f, 0.35f));
                var marker = UiKit.Rect(track, "Marker", Vector2.zero, new Vector2(10f, 0f));
                marker.anchorMin = new Vector2(0.5f, 0f);
                marker.anchorMax = new Vector2(0.5f, 1f);
                marker.pivot = new Vector2(0.5f, 0.5f);
                UiKit.Image(marker, UiKit.AccentColor);
                _channelMarkers[i] = marker;
                _channelValues[i] = UiKit.Label(p, "", new Vector2(x + colW - 130f, y), new Vector2(130f, 46f), 24f, TextAlignmentOptions.MidlineRight);
            }
        }

        private void Update()
        {
            if (_channelMarkers == null) return;
            var reader = radio != null ? radio.Reader : null;
            int count = reader != null ? reader.ChannelCount : 0;
            if (_values.Length != count) _values = new float[count];
            for (int i = 0; i < count; i++) _values[i] = reader.Read(i);

            for (int i = 0; i < LiveChannels; i++)
            {
                bool used = i < count;
                _channelMarkers[i].parent.gameObject.SetActive(used);
                _channelValues[i].gameObject.SetActive(used);
                _channelLabels[i].gameObject.SetActive(used);
                if (!used) continue;
                float x = Mathf.Clamp(_values[i], -1f, 1f) * 0.5f + 0.5f;
                _channelMarkers[i].anchorMin = new Vector2(x, 0f);
                _channelMarkers[i].anchorMax = new Vector2(x, 1f);
                _channelValues[i].SetText("{0:2}", _values[i]);
            }

            if (_wizard != null) UpdateWizard();

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer = 0.25f;
                UpdateRadioStatus();
            }
        }

        private void UpdateRadioStatus()
        {
            var reader = radio != null ? radio.Reader : null;
            if (_channelLabels != null)
            {
                for (int i = 0; i < LiveChannels; i++)
                    _channelLabels[i].text = reader != null && i < reader.ChannelCount ? $"ch{i} {reader.ChannelName(i)} {Role(i)}" : "";
            }
            if (_radioStatus == null) return;
            if (reader == null)
            {
                string pad = radio != null && radio.GamepadPreset != null
                    ? $"\n<color=#ffb347>{radio.GamepadPreset.DisplayName} detected in Xbox mode: it flies as a gamepad with radio-style throttle.</color>"
                    : "";
                _radioStatus.text = "<b>No radio found.</b> Plug it in with a USB-C OTG cable, or pair it over Bluetooth in the Quest settings.\nSee Connection help." + pad;
                return;
            }
            var preset = radio.Preset;
            _radioStatus.text =
                $"<b>{reader.DisplayName}</b>   via {reader.Backend}, {reader.ChannelCount} channels\n" +
                $"Preset: {(preset != null ? preset.DisplayName + (preset.Verified ? " <color=#5f5>(verified)</color>" : " <color=#ffb347>(not yet verified on hardware)</color>") : "none (generic AETR guess)")}\n" +
                $"Mapping: {(radio.IsCalibrated ? "<color=#5f5>your calibration</color>" : "preset/default. Calibrate for best results")}\n" +
                (_radioMessage.Length > 0 ? $"<color=#ffb347>{_radioMessage}</color>" : "");
        }

        /// <summary>Role tag for a channel in the current mapping (THR/YAW/PIT/ROL/ARM/MODE).</summary>
        private string Role(int ch)
        {
            var m = _wizard != null ? _wizard.Result : radio?.Mapping;
            if (m == null) return "";
            if (m.Throttle.Channel == ch) return "<color=#ffb347>THR</color>";
            if (m.Yaw.Channel == ch) return "<color=#ffb347>YAW</color>";
            if (m.Pitch.Channel == ch) return "<color=#ffb347>PIT</color>";
            if (m.Roll.Channel == ch) return "<color=#ffb347>ROL</color>";
            if (m.Arm.Channel == ch) return "<color=#5f5>ARM</color>";
            if (m.Mode.Channel == ch) return "<color=#5f5>MODE</color>";
            return "";
        }

        // ---------- calibration wizard ----------

        private void StartWizard()
        {
            if (radio == null || radio.Reader == null)
            {
                _radioMessage = "Connect a radio first.";
                UpdateRadioStatus();
                return;
            }
            var p = NewPage("Calibrate radio");
            _wizard = new CalibrationWizard(radio.Reader.ChannelCount);
            _wizardText = UiKit.Label(p, "", new Vector2(Margin, -110f), new Vector2(ContentWidth, 130f), 34f, TextAlignmentOptions.TopLeft);
            var bar = UiKit.Rect(p, "Progress", new Vector2(Margin, -245f), new Vector2(ContentWidth, 14f));
            UiKit.Image(bar, new Color(1f, 1f, 1f, 0.12f));
            _wizardProgress = UiKit.Rect(bar, "Fill", Vector2.zero, Vector2.zero);
            _wizardProgress.anchorMin = Vector2.zero;
            _wizardProgress.anchorMax = new Vector2(0f, 1f);
            _wizardProgress.offsetMin = _wizardProgress.offsetMax = Vector2.zero;
            UiKit.Image(_wizardProgress, UiKit.AccentColor);
            BuildChannelBars(p, -280f);

            float y = -(Height - 110f);
            UiKit.Button(p, "Cancel", new Vector2(Margin, y), new Vector2(240f, 70f), () => { _wizard?.Cancel(); ShowRadio(); });
            UiKit.Button(p, "Skip", new Vector2(Width - Margin - 540f, y), new Vector2(240f, 70f), () => _wizard?.Skip(_values));
            UiKit.Button(p, "Next", new Vector2(Width - Margin - 280f, y), new Vector2(280f, 70f), () => _wizard?.Next(_values));
        }

        private void UpdateWizard()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.enterKey.wasPressedThisFrame) _wizard.Next(_values);

            _wizard.Feed(_values, Time.unscaledDeltaTime);
            _wizardText.text = $"Step {Mathf.Min((int)_wizard.Step + 1, 8)}/8\n{_wizard.Instruction}";
            _wizardProgress.anchorMax = new Vector2(_wizard.HoldProgress, 1f);

            if (_wizard.Step == CalibrationStep.Done)
            {
                var result = _wizard.Result;
                _wizard = null;
                if (!result.Throttle.IsAssigned || !result.Yaw.IsAssigned || !result.Pitch.IsAssigned || !result.Roll.IsAssigned)
                {
                    _radioMessage = "Calibration incomplete: not all four stick axes were found. Try again.";
                }
                else
                {
                    radio.SaveCalibration(result);
                    _radioMessage = string.Format(CultureInfo.InvariantCulture, "Calibration saved ({0}). Throttle ch{1}, yaw ch{2}, pitch ch{3}, roll ch{4}{5}.",
                        radio.Reader?.DisplayName, result.Throttle.Channel, result.Yaw.Channel, result.Pitch.Channel, result.Roll.Channel,
                        result.Arm.IsAssigned ? $", arm ch{result.Arm.Channel}" : ", arm with throttle low");
                }
                ShowRadio();
            }
        }

        // ---------- help ----------

        private void ShowRadioHelp()
        {
            var p = NewPage("Connecting a BetaFPV radio");
            UiKit.Label(p,
                "<b>LiteRadio 2 SE / LiteRadio 3 (USB-C):</b> set the radio to <b>joystick (HID) mode</b> and plug it into the Quest with a " +
                "USB-C OTG cable. If the Quest doesn't see it, open <b>BETAFPV Configurator</b> on a PC and enable Android/joystick compatibility.\n\n" +
                "<b>LiteRadio 2 SIM:</b> joystick mode (default) shows up as \"BETAFPV Joystick\". In Xbox mode it works as a gamepad; " +
                "throttle is then full-range, like a radio.\n\n" +
                "<b>LiteRadio 4 / 4 SE (ELRS):</b> best over <b>Bluetooth joystick mode</b>: pair it in <b>Quest Settings > Devices > Bluetooth</b>. " +
                "Over USB-C, joystick mode only works with the radio <b>switched off</b> and powered by the cable.\n\n" +
                "<b>Then:</b> Menu > Radio setup > <b>Calibrate</b>. Arm with your arm switch (throttle low). " +
                "If some sticks or switches don't move in Radio setup, try <b>Reader: Android raw</b>, and check Input diagnostics.",
                new Vector2(Margin, -110f), new Vector2(ContentWidth, 720f), 30f, TextAlignmentOptions.TopLeft);
            BackButton(p, ShowRadio);
        }
    }
}
