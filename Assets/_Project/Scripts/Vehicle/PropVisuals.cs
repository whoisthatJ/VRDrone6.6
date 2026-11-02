using UnityEngine;

namespace GateRush.Vehicle
{
    /// <summary>
    /// Spins the four prop transforms from motor speed and cross-fades to translucent blur discs at speed.
    /// Order matches the mixer (RR, FR, RL, FL).
    /// </summary>
    public sealed class PropVisuals : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private Transform[] props = new Transform[4];
        [Tooltip("Optional blur discs, one per prop (transparent material).")]
        [SerializeField] private Renderer[] blurDiscs = new Renderer[0];
        [Tooltip("Visual spin at full motor speed (deg/s). Kept low so the props read as blurred discs, not strobing.")]
        [SerializeField] private float maxVisualDegPerSec = 2200f;
        [SerializeField] private Color blurColor = new Color(0.15f, 0.15f, 0.15f, 0.45f);

        private static readonly float[] Direction = { -1f, 1f, 1f, -1f }; // CW, CCW, CCW, CW seen from above

        private Renderer[] _propRenderers = new Renderer[0];
        private MaterialPropertyBlock _mpb;

        public Transform[] Props => props;
        public Renderer[] BlurDiscs => blurDiscs;

        /// <summary>Points the spinner at another model's props (a garage build replacing the stock visual).</summary>
        public void Configure(Transform[] propTransforms, Renderer[] discs)
        {
            props = propTransforms;
            blurDiscs = discs ?? new Renderer[0];
            Awake();
        }

        private void Awake()
        {
            _propRenderers = new Renderer[props.Length];
            for (int i = 0; i < props.Length; i++) _propRenderers[i] = props[i] != null ? props[i].GetComponent<Renderer>() : null;
            _mpb = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (vehicle == null) return;
            ref readonly var s = ref vehicle.State;
            float dt = Time.deltaTime;
            for (int i = 0; i < props.Length && i < 4; i++)
            {
                if (props[i] == null) continue;
                float speed = s.MotorSpeed[i];
                props[i].Rotate(0f, Direction[i] * speed * maxVisualDegPerSec * dt, 0f, Space.Self);

                if (i >= blurDiscs.Length || blurDiscs[i] == null) continue;
                // Blades fade out as the disc fades in.
                float blur = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.45f, speed));
                if (_propRenderers[i] != null) _propRenderers[i].enabled = blur < 0.6f;
                blurDiscs[i].enabled = blur > 0.02f;
                var c = blurColor;
                c.a *= blur;
                _mpb.SetColor(BaseColor, c);
                blurDiscs[i].SetPropertyBlock(_mpb);
            }
        }
    }
}
