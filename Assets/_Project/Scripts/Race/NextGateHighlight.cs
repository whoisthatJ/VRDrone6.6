using UnityEngine;

namespace GateRush.Race
{
    /// <summary>
    /// The LED strips of an obstacle: colour from the gate state (see <see cref="Gate.SetVisualState"/>) and an HDR
    /// intensity per state; the next gate pulses. Works with the GateRush/LED shader (_BaseColor × _Intensity) through
    /// a property block, so all obstacles share one material.
    /// </summary>
    public sealed class NextGateHighlight : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] private Renderer[] leds = new Renderer[0];
        [SerializeField] private float idleIntensity = 2.5f;
        [SerializeField] private float nextIntensity = 6f;
        [SerializeField] private float upcomingIntensity = 3.5f;
        [SerializeField] private float passedIntensity = 0.8f;
        [Tooltip("Pulses per second on the next gate.")]
        [SerializeField] private float pulseRate = 1.6f;
        [Tooltip("How far the pulse dips below the next-gate intensity (0..1).")]
        [SerializeField, Range(0f, 1f)] private float pulseDepth = 0.55f;

        private MaterialPropertyBlock _mpb;
        private Color _color = Color.white;

        public Renderer[] Leds
        {
            get => leds;
            set => leds = value;
        }

        public GateVisualState State { get; private set; } = GateVisualState.Idle;

        /// <summary>Global multiplier (route guidance "Off" dims gates to plain LEDs, T5).</summary>
        public float Strength { get; set; } = 1f;

        /// <summary>LED intensity for a state at time <paramref name="time"/> (pulsing only when next).</summary>
        public float IntensityFor(GateVisualState state, float time)
        {
            switch (state)
            {
                case GateVisualState.Next:
                    float wave = 0.5f + 0.5f * Mathf.Sin(time * pulseRate * Mathf.PI * 2f);
                    return nextIntensity * (1f - pulseDepth * wave);
                case GateVisualState.Upcoming: return upcomingIntensity;
                case GateVisualState.Passed: return passedIntensity;
                default: return idleIntensity;
            }
        }

        public void Show(GateVisualState state, Color color)
        {
            State = state;
            _color = color;
            Apply(IntensityFor(state, Time.time));
            enabled = state == GateVisualState.Next;
        }

        private void Update() => Apply(IntensityFor(State, Time.time));

        private void Apply(float intensity)
        {
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetColor(BaseColorId, _color);
            _mpb.SetFloat(IntensityId, intensity * Strength);
            for (int i = 0; i < leds.Length; i++)
                if (leds[i] != null) leds[i].SetPropertyBlock(_mpb);
        }
    }
}
