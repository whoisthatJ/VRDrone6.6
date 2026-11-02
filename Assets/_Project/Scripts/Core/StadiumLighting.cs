using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.Core
{
    public enum TimeOfDay
    {
        Night = 0,
        Dusk = 1,
        Day = 2,
    }

    /// <summary>
    /// Runtime time-of-day for the stadium: each preset sets the sky, ambient, sun (or moon), fog, the floodlights
    /// (realtime spots + emissive heads) and its own post-processing volume. Nothing is baked, so presets switch live
    /// (settings: Stadium lighting). Built by the stadium art pass.
    /// </summary>
    public sealed class StadiumLighting : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Serializable]
        public sealed class Preset
        {
            public string Name;
            public Material Sky;
            public float AmbientIntensity = 1f;
            public float ReflectionIntensity = 1f;
            public bool SunEnabled = true;
            public Quaternion SunRotation = Quaternion.Euler(45f, 0f, 0f);
            public Color SunColor = Color.white;
            public float SunIntensity = 1.5f;
            public float ShadowStrength = 0.85f;
            public Color FogColor = Color.grey;
            public float FogDensity = 0.002f;
            [Tooltip("Spot light intensity of each floodlight (0 = off).")]
            public float FloodlightIntensity;
            [Tooltip("HDR intensity of the floodlight lamp heads.")]
            public float LampIntensity = 1f;
            public Volume Volume;
        }

        [SerializeField] private Preset[] presets = new Preset[3];
        [SerializeField] private Light sun;
        [SerializeField] private Light[] floodlights = new Light[0];
        [Tooltip("Per-floodlight multiplier on the preset intensity (stand lights under the roofs are much closer).")]
        [SerializeField] private float[] floodlightScale = new float[0];
        [SerializeField] private Renderer[] lampHeads = new Renderer[0];
        [SerializeField] private TimeOfDay initial = TimeOfDay.Night;

        private MaterialPropertyBlock _mpb;
        private bool _applied;

        public TimeOfDay Current { get; private set; }
        public Preset[] Presets => presets;

        public void Configure(Preset[] all, Light sunLight, Light[] spots, Renderer[] heads, float[] spotScale = null)
        {
            presets = all;
            sun = sunLight;
            floodlights = spots;
            floodlightScale = spotScale ?? new float[0];
            lampHeads = heads;
        }

        // The track scene applies the player's setting first; otherwise start with the default.
        private void Start()
        {
            if (!_applied) Apply(initial);
        }

        public void Apply(TimeOfDay time)
        {
            int i = Mathf.Clamp((int)time, 0, presets.Length - 1);
            var p = presets[i];
            if (p == null) return;
            Current = (TimeOfDay)i;
            _applied = true;

            if (p.Sky != null) RenderSettings.skybox = p.Sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = p.AmbientIntensity;
            RenderSettings.reflectionIntensity = p.ReflectionIntensity;
            RenderSettings.fog = p.FogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.FogColor;
            RenderSettings.fogDensity = p.FogDensity;

            if (sun != null)
            {
                sun.enabled = p.SunEnabled;
                sun.transform.rotation = p.SunRotation;
                sun.color = p.SunColor;
                sun.intensity = p.SunIntensity;
                sun.shadowStrength = p.ShadowStrength;
                RenderSettings.sun = p.SunEnabled ? sun : null;
            }
            for (int k = 0; k < floodlights.Length; k++)
            {
                var l = floodlights[k];
                if (l == null) continue;
                l.enabled = p.FloodlightIntensity > 0f;
                l.intensity = p.FloodlightIntensity * (k < floodlightScale.Length ? floodlightScale[k] : 1f);
            }
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetFloat(IntensityId, p.LampIntensity);
            foreach (var r in lampHeads)
                if (r != null) r.SetPropertyBlock(_mpb);
            for (int k = 0; k < presets.Length; k++)
                if (presets[k]?.Volume != null) presets[k].Volume.weight = k == i ? 1f : 0f;

            DynamicGI.UpdateEnvironment();
        }
    }
}
