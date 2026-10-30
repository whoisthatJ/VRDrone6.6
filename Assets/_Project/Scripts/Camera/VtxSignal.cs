using GateRush.Vehicle;
using UnityEngine;

namespace GateRush.Cameras
{
    public enum VtxNoiseLevel
    {
        Off = 0,
        Light = 1,
        Realistic = 2,
    }

    /// <summary>
    /// Analog video link model: signal falls off with distance from the receiver (the pilot) and with solid
    /// obstacles in between, plus a burst of static on a crash. Drives the goggles screen's AnalogFeed shader
    /// and exposes a link-quality value for the OSD. Raycasts are non-allocating and run 10× per second.
    /// </summary>
    public sealed class VtxSignal : MonoBehaviour
    {
        private static readonly int NoiseId = Shader.PropertyToID("_Noise");
        private static readonly int BreakupId = Shader.PropertyToID("_Breakup");

        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private Transform receiver;
        [SerializeField] private Renderer screen;
        [SerializeField] private LayerMask obstacles = ~0;
        [SerializeField] private VtxNoiseLevel level = VtxNoiseLevel.Light;
        [Tooltip("Clean picture up to this distance (m).")]
        [SerializeField] private float cleanRange = 120f;
        [Tooltip("Distance over which the picture degrades to nothing (m).")]
        [SerializeField] private float fadeRange = 250f;
        [Tooltip("Signal lost per solid obstacle between pilot and quad.")]
        [SerializeField] private float obstaclePenalty = 0.22f;

        private readonly RaycastHit[] _hits = new RaycastHit[6];
        private MaterialPropertyBlock _mpb;
        private float _obstruction, _nextCast, _crashStatic, _smoothed;

        /// <summary>Link quality 0..1 (1 = perfect), for the OSD.</summary>
        public float LinkQuality { get; private set; } = 1f;

        public VtxNoiseLevel Level
        {
            get => level;
            set => level = value;
        }

        public Transform Receiver
        {
            get => receiver;
            set => receiver = value;
        }

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (vehicle != null) vehicle.Crashed += OnCrash;
        }

        private void OnDestroy()
        {
            if (vehicle != null) vehicle.Crashed -= OnCrash;
        }

        private void OnCrash() => _crashStatic = 1f;

        private void Update()
        {
            if (vehicle == null || receiver == null) return;
            Vector3 rx = receiver.position + Vector3.up * 1.5f;
            Vector3 tx = vehicle.transform.position;
            float distance = Vector3.Distance(rx, tx);

            if (Time.time >= _nextCast)
            {
                _nextCast = Time.time + 0.1f;
                Vector3 dir = tx - rx;
                int n = distance > 0.5f
                    ? Physics.RaycastNonAlloc(rx, dir / distance, _hits, distance - 0.3f, obstacles, QueryTriggerInteraction.Ignore)
                    : 0;
                _obstruction = n * obstaclePenalty;
            }

            float loss = Mathf.Clamp01(Mathf.Max(0f, distance - cleanRange) / fadeRange + _obstruction);
            _crashStatic = Mathf.MoveTowards(_crashStatic, 0f, Time.deltaTime * 1.5f);
            _smoothed = Mathf.Lerp(_smoothed, loss, 1f - Mathf.Exp(-6f * Time.deltaTime));
            LinkQuality = 1f - Mathf.Max(_smoothed, _crashStatic * 0.8f);

            float scale = level == VtxNoiseLevel.Off ? 0f : level == VtxNoiseLevel.Light ? 0.5f : 1f;
            float noise = scale * Mathf.Clamp01(0.04f + _smoothed * 1.2f + _crashStatic);
            float breakup = scale * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, Mathf.Max(_smoothed, _crashStatic)));

            if (screen == null) return;
            screen.GetPropertyBlock(_mpb);
            _mpb.SetFloat(NoiseId, noise);
            _mpb.SetFloat(BreakupId, breakup);
            screen.SetPropertyBlock(_mpb);
        }
    }
}
