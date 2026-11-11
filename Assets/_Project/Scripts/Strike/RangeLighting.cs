using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.Strike
{
    public enum RangeWeather
    {
        Day = 0,
        Overcast = 1,
        Dusk = 2,
    }

    /// <summary>
    /// Strike Range lighting (P5): three realtime presets for the steppe — clear day, overcast, dusk. Sets the sun, the
    /// procedural sky's tint and exposure, ambient and fog. Nothing is baked, so it switches live from the lobby.
    /// </summary>
    public sealed class RangeLighting : MonoBehaviour
    {
        public static RangeWeather Requested = RangeWeather.Day;

        private Light _sun;
        public RangeWeather Current { get; private set; }

        private void Start() => Apply(Requested);

        public void Apply(RangeWeather w)
        {
            Current = w;
            if (_sun == null) _sun = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
            var sky = RenderSettings.skybox;
            Color sunColor, fog, skyTint;
            float sunI, pitch, yaw, exposure, ambient, fogStart, fogEnd, shadow;
            switch (w)
            {
                case RangeWeather.Overcast:
                    sunColor = new Color(0.86f, 0.88f, 0.92f); sunI = 0.55f; pitch = 55f; yaw = 30f; shadow = 0.25f;
                    skyTint = new Color(0.55f, 0.57f, 0.6f); exposure = 0.9f; ambient = 1.15f;
                    fog = new Color(0.68f, 0.7f, 0.72f); fogStart = 120f; fogEnd = 1400f;
                    break;
                case RangeWeather.Dusk:
                    sunColor = new Color(1f, 0.6f, 0.35f); sunI = 0.9f; pitch = 7f; yaw = 250f; shadow = 0.7f;
                    skyTint = new Color(0.6f, 0.45f, 0.4f); exposure = 0.8f; ambient = 0.7f;
                    fog = new Color(0.62f, 0.46f, 0.38f); fogStart = 200f; fogEnd = 2000f;
                    break;
                default:
                    sunColor = new Color(1f, 0.96f, 0.88f); sunI = 1.3f; pitch = 48f; yaw = 140f; shadow = 0.85f;
                    skyTint = new Color(0.5f, 0.5f, 0.5f); exposure = 1.3f; ambient = 1f;
                    fog = new Color(0.76f, 0.8f, 0.84f); fogStart = 180f; fogEnd = 2100f;
                    break;
            }
            if (_sun != null)
            {
                _sun.color = sunColor;
                _sun.intensity = sunI;
                _sun.shadowStrength = shadow;
                _sun.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            if (sky != null)
            {
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", skyTint);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", exposure);
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", w == RangeWeather.Overcast ? 0.6f : w == RangeWeather.Dusk ? 1.6f : 1f);
            }
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            DynamicGI.UpdateEnvironment();
        }
    }
}
