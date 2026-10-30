using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GateRush.Input
{
    /// <summary>
    /// Default mapping for a known radio, picked automatically when the device name matches <see cref="NamePattern"/>.
    /// Shipped values are educated guesses until confirmed on hardware with the diagnostics screen (<see cref="Verified"/>).
    /// A per-device calibration always wins over a preset.
    /// </summary>
    [CreateAssetMenu(menuName = "GateRush/Controller Preset", fileName = "ControllerPreset")]
    public sealed class ControllerPreset : ScriptableObject
    {
        public string DisplayName = "Radio";
        [Tooltip("Case-insensitive regex matched against the device's product name and display name.")]
        public string NamePattern = "";
        [Tooltip("Higher wins when several presets match.")]
        public int Priority;
        [Tooltip("False until the mapping has been checked on real hardware.")]
        public bool Verified;
        [Tooltip("The radio shows up as an Xbox-style gamepad: fly it through the gamepad path with radio-style (full-range) throttle.")]
        public bool UsesGamepadPath;
        [TextArea] public string Notes;
        public RadioMapping Mapping = new RadioMapping();

        public bool Matches(string productName, string displayName) =>
            !string.IsNullOrEmpty(NamePattern) && (IsMatch(productName) || IsMatch(displayName));

        private bool IsMatch(string s) =>
            !string.IsNullOrEmpty(s) && Regex.IsMatch(s, NamePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Best matching preset (highest priority), or null.</summary>
        public static ControllerPreset Find(IList<ControllerPreset> presets, string productName, string displayName)
        {
            ControllerPreset best = null;
            if (presets == null) return null;
            foreach (var p in presets)
            {
                if (p == null || !p.Matches(productName, displayName)) continue;
                if (best == null || p.Priority > best.Priority) best = p;
            }
            return best;
        }
    }
}
