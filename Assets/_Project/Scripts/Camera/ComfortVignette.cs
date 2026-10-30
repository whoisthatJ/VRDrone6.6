using GateRush.Vehicle;
using UnityEngine;

namespace GateRush.Cameras
{
    /// <summary>
    /// Rate-based tunnel vignette for Immersive mode: a camera-attached quad whose radial alpha
    /// fades in with the quad's angular speed. Cheap: one transparent quad, no post-processing.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class ComfortVignette : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        [SerializeField] private QuadVehicle vehicle;
        [SerializeField, Range(0f, 1f)] private float strength = 0.7f;
        [Tooltip("Angular speed (deg/s) where the vignette starts.")]
        [SerializeField] private float startRate = 90f;
        [Tooltip("Angular speed (deg/s) where it reaches full strength.")]
        [SerializeField] private float fullRate = 400f;
        [SerializeField] private float fadeSharpness = 8f;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Texture2D _mask;
        private float _alpha;

        public float Strength
        {
            get => strength;
            set => strength = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            _mask = BuildMask(128);
            _mpb.SetTexture(BaseMap, _mask);
            Apply(0f);
        }

        private void LateUpdate()
        {
            float target = 0f;
            if (vehicle != null && strength > 0f)
            {
                float rate = vehicle.State.AngularVelocity.Magnitude * Mathf.Rad2Deg;
                target = strength * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(startRate, fullRate, rate));
            }
            _alpha = Mathf.Lerp(_alpha, target, 1f - Mathf.Exp(-fadeSharpness * Time.deltaTime));
            Apply(_alpha);
        }

        private void Apply(float alpha)
        {
            _renderer.enabled = alpha > 0.005f;
            _mpb.SetColor(BaseColor, new Color(0f, 0f, 0f, alpha));
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>White texture whose alpha is 0 in the middle and 1 at the edge (radial).</summary>
        private static Texture2D BuildMask(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VignetteMask" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.85f, r)) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private void OnDestroy()
        {
            if (_mask != null) Destroy(_mask);
        }
    }
}
