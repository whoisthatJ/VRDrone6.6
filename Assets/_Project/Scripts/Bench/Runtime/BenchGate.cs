using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The standard gate (2.5 x 2.5 m, opening 1.95 x 1.95 m) with its own LED strip. The object's origin is the middle of
    /// the base on the floor; +Z is the front-to-back direction.
    /// </summary>
    public sealed class BenchGate : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Renderer[] leds;
        [SerializeField] private Renderer[] frame;

        private MaterialPropertyBlock _block;
        private Color _color = Color.magenta;
        private float _brightness = 1f;
        private bool _blink;

        public string Id { get; set; }
        /// <summary>Centre of the opening in world space.</summary>
        public Vector3 OpeningCentre => transform.TransformPoint(new Vector3(0f, GateGeometry.CentreHeight, 0f));

        public void Configure(Renderer[] ledRenderers, Renderer[] frameRenderers)
        {
            leds = ledRenderers;
            frame = frameRenderers;
        }

        public void SetLed(Color color, float brightness, bool blink)
        {
            _color = color;
            _brightness = Mathf.Clamp01(brightness);
            _blink = blink;
            Apply(1f);
        }

        /// <summary>
        /// Paints the whole gate in one flat colour that owes nothing to the lights (dataset masks);
        /// <see cref="Unpaint"/> brings its own look back.
        /// </summary>
        public void Paint(Color32 colour)
        {
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColor, Color.black);
            // SetColor takes the colour as seen on screen and converts it itself.
            _block.SetColor(EmissionColor, (Color)colour);
            if (leds != null)
                foreach (var r in leds) if (r != null) r.SetPropertyBlock(_block);
            if (frame != null)
                foreach (var r in frame) if (r != null) r.SetPropertyBlock(_block);
        }

        public void Unpaint()
        {
            if (frame != null)
                foreach (var r in frame) if (r != null) r.SetPropertyBlock(null);
            Apply(1f);
        }

        /// <summary>Blinking follows simulator time, so the camera picture is the same on every run.</summary>
        public void Tick(double simTime)
        {
            if (!_blink) return;
            Apply((simTime % 1.0) < 0.5 ? 1f : 0.08f);
        }

        private void Apply(float gain)
        {
            if (leds == null) return;
            _block ??= new MaterialPropertyBlock();
            float k = _brightness * gain;
            // The strip keeps its colour when it gets brighter: emission above 1 only feeds the glow around it.
            _block.SetColor(BaseColor, _color * Mathf.Min(1f, k));
            _block.SetColor(EmissionColor, _color * (k * 2.2f));
            foreach (var r in leds)
                if (r != null) r.SetPropertyBlock(_block);
        }
    }
}
