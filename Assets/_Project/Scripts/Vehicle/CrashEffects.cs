using UnityEngine;

namespace GateRush.Vehicle
{
    /// <summary>
    /// Sparks and debris for crashes and hard bounces. One particle system, created once and reused with
    /// <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>: pooled, no instantiation or GC at runtime.
    /// </summary>
    public sealed class CrashEffects : MonoBehaviour
    {
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private Material particleMaterial;
        [SerializeField] private int crashParticles = 60;
        [Tooltip("Bounces faster than this (m/s) throw a few sparks.")]
        [SerializeField] private float sparkImpactSpeed = 2.5f;

        private ParticleSystem _ps;
        private int _seenImpacts;

        private void Awake()
        {
            var go = new GameObject("Crash Particles");
            go.transform.SetParent(transform.parent, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = crashParticles * 3;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _ps.emission;
            emission.enabled = false;
            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = particleMaterial;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (vehicle != null) vehicle.Crashed += OnCrash;
        }

        private void OnDestroy()
        {
            if (vehicle != null) vehicle.Crashed -= OnCrash;
        }

        private void Update()
        {
            if (vehicle == null || vehicle.ImpactCount == _seenImpacts) return;
            _seenImpacts = vehicle.ImpactCount;
            if (!vehicle.IsCrashed && vehicle.LastImpactSpeed >= sparkImpactSpeed)
                Burst(vehicle.transform.position, Mathf.RoundToInt(Mathf.Lerp(4f, 16f, Mathf.InverseLerp(sparkImpactSpeed, 7f, vehicle.LastImpactSpeed))));
        }

        private void OnCrash() => Burst(vehicle.transform.position, crashParticles);

        private void Burst(Vector3 at, int count)
        {
            _ps.transform.position = at;
            _ps.Emit(count);
        }
    }
}
