using Ushqyr.Flight;
using UnityEngine;

namespace Ushqyr.Edu
{
    /// <summary>
    /// What the weather looks like (EDU_PLAN F4): rain falling around whoever is watching, slanted by the wind, and
    /// fog that hides what is further away than the visibility. The wind itself, the gusts and the cold are in the
    /// flight model; this is only the picture. One of these exists per scene, made when weather is first set, and it
    /// gives the scene its own fog settings back when the weather clears or the scene is left.
    /// </summary>
    public sealed class EduWeatherVisuals : MonoBehaviour
    {
        private static EduWeatherVisuals _instance;

        private ParticleSystem _rain;
        private Material _rainMaterial;
        private bool _savedFog, _fog;
        private FogMode _fogMode;
        private float _fogDensity;
        private Color _fogColor;
        private WeatherSettings _shown;
        private Vec3 _wind;

        /// <summary>Rain, 0..1, and visibility in metres (0 = clear) as shown now.</summary>
        public WeatherSettings Shown => _shown;

        public static EduWeatherVisuals Ensure()
        {
            if (_instance != null) return _instance;
            _instance = new GameObject("Edu Weather").AddComponent<EduWeatherVisuals>();
            return _instance;
        }

        /// <summary>Shows this weather; called every frame while weather is in force, so changes blend in as the flight model's do.</summary>
        public void Show(in WeatherSettings weather, Vec3 wind)
        {
            _shown = weather;
            _wind = wind;
            ShowFog(weather.Visibility);
            ShowRain(Mathf.Clamp01(weather.Rain));
        }

        private void ShowFog(float visibility)
        {
            bool foggy = visibility > 1f && visibility < 5000f;
            if (!foggy)
            {
                RestoreFog();
                return;
            }
            if (!_savedFog)
            {
                _savedFog = true;
                _fog = RenderSettings.fog;
                _fogMode = RenderSettings.fogMode;
                _fogDensity = RenderSettings.fogDensity;
                _fogColor = RenderSettings.fogColor;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            // At the visibility distance about 5% of an object's contrast is left.
            RenderSettings.fogDensity = 3f / visibility;
            RenderSettings.fogColor = new Color(0.64f, 0.67f, 0.70f);
        }

        private void RestoreFog()
        {
            if (!_savedFog) return;
            _savedFog = false;
            RenderSettings.fog = _fog;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogDensity = _fogDensity;
            RenderSettings.fogColor = _fogColor;
        }

        private void ShowRain(float amount)
        {
            if (amount <= 0.01f)
            {
                if (_rain != null && _rain.isPlaying) _rain.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            if (_rain == null) BuildRain();
            ParticleSystem.EmissionModule emission = _rain.emission;
            emission.rateOverTime = amount * 5000f;
            // The drops fall at about 9 m/s and are carried sideways by the wind.
            ParticleSystem.VelocityOverLifetimeModule velocity = _rain.velocityOverLifetime;
            velocity.x = _wind.x;
            velocity.y = -9f;
            velocity.z = _wind.z;
            if (!_rain.isPlaying) _rain.Play();
        }

        private void BuildRain()
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            _rain = go.AddComponent<ParticleSystem>();
            _rain.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _rain.main;
            main.loop = true;
            main.startLifetime = 1.6f;
            main.startSpeed = 0f;
            main.startSize = 0.012f;
            main.startColor = new Color(0.8f, 0.85f, 0.95f, 0.35f);
            main.maxParticles = 12000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            ParticleSystem.ShapeModule shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 0.5f, 34f);
            ParticleSystem.VelocityOverLifetimeModule velocity = _rain.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.035f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _rainMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            renderer.sharedMaterial = _rainMaterial;
        }

        private void LateUpdate()
        {
            if (_rain == null) return;
            // The rain is around the eye: over whichever camera draws the picture.
            Camera cam = Camera.main;
            if (cam == null) return;
            _rain.transform.position = cam.transform.position + Vector3.up * 9f + cam.transform.forward * 6f;
        }

        private void OnDestroy()
        {
            RestoreFog();
            if (_rainMaterial != null) Destroy(_rainMaterial);
            if (_instance == this) _instance = null;
        }
    }
}
