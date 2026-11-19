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

        /// <summary>A drawn variation on top of the track's lighting variant: level, colour and direction of the main light.</summary>
        public void Vary(string variant, float level, Color colour, float yaw)
        {
            Apply(variant);
            if (main != null)
            {
                main.intensity *= level;
                main.color *= colour;
                Vector3 e = main.transform.eulerAngles;
                main.transform.rotation = Quaternion.Euler(e.x, yaw, 0f);
            }
            if (bounce != null) bounce.intensity *= level;
            RenderSettings.ambientSkyColor *= colour * level;
            RenderSettings.ambientEquatorColor *= colour * level;
            RenderSettings.ambientGroundColor *= colour * level;
        }

        private Color _sky, _equator, _ground;

        /// <summary>All light off (and back on): only things that shine by themselves stay visible.</summary>
        public void Dark(bool dark)
        {
            if (main != null) main.enabled = !dark;
            if (bounce != null) bounce.enabled = !dark;
            if (dark)
            {
                _sky = RenderSettings.ambientSkyColor; _equator = RenderSettings.ambientEquatorColor; _ground = RenderSettings.ambientGroundColor;
                RenderSettings.ambientSkyColor = RenderSettings.ambientEquatorColor = RenderSettings.ambientGroundColor = Color.black;
            }
            else
            {
                RenderSettings.ambientSkyColor = _sky; RenderSettings.ambientEquatorColor = _equator; RenderSettings.ambientGroundColor = _ground;
            }
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
