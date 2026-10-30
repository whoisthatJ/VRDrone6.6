using System.Collections.Generic;
using UnityEngine;

namespace GateRush.Input
{
    /// <summary>All shipped presets (referenced from scenes so they ship in the build).</summary>
    [CreateAssetMenu(menuName = "GateRush/Controller Preset Library", fileName = "ControllerPresets")]
    public sealed class ControllerPresetLibrary : ScriptableObject
    {
        public List<ControllerPreset> Presets = new List<ControllerPreset>();

        public ControllerPreset Find(string productName, string displayName) => ControllerPreset.Find(Presets, productName, displayName);
    }
}
