using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The lights of a venue and its lighting variants ("default", "dim", "warm", "bright"), chosen by the track file.
    /// The scene builder records the default state; a variant scales and tints it.
    /// </summary>
    public sealed class BenchVenueLights : MonoBehaviour
    {
        [SerializeField] private Light main;
        [SerializeField] private Light bounce;
        [SerializeField] private float mainIntensity = 1f, bounceIntensity = 0.4f;
        [SerializeField] private Color mainColor = Color.white, ambientSky = Color.grey, ambientEquator = Color.grey, ambientGround = Color.grey;

        public void Configure(Light mainLight, Light bounceLight)
        {
            main = mainLight;
            bounce = bounceLight;
            mainIntensity = main.intensity;
            bounceIntensity = bounce.intensity;
            mainColor = main.color;
            ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor;
        }

        public void Apply(string variant)
        {
            float level = 1f;
            Color tint = Color.white;
            switch (variant)
            {
                case "dim": level = 0.45f; break;
                case "bright": level = 1.6f; break;
                case "warm": tint = new Color(1f, 0.84f, 0.64f); break;
            }
            if (main != null)
            {
                main.intensity = mainIntensity * level;
                main.color = mainColor * tint;
            }
            if (bounce != null) bounce.intensity = bounceIntensity * level;
            RenderSettings.ambientSkyColor = ambientSky * tint * level;
            RenderSettings.ambientEquatorColor = ambientEquator * tint * level;
            RenderSettings.ambientGroundColor = ambientGround * tint * level;
        }
    }
}
