using UnityEngine;

namespace GateRush.Core
{
    /// <summary>
    /// Perimeter LED advertising boards: scrolls the banner texture along the boards (one property block for all of
    /// them, so they stay in sync like one long screen) and flashes brighter now and then.
    /// </summary>
    public sealed class LedBoardScroller : MonoBehaviour
    {
        private static readonly int StId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] private Renderer[] boards = new Renderer[0];
        [Tooltip("Banner repeats per board.")]
        [SerializeField] private float repeats = 1f;
        [Tooltip("Banner widths per second.")]
        [SerializeField] private float speed = 0.12f;
        [SerializeField] private float intensity = 2.2f;

        private MaterialPropertyBlock _mpb;

        public void Configure(Renderer[] boardRenderers, float bannerRepeats)
        {
            boards = boardRenderers;
            repeats = bannerRepeats;
        }

        private void Update()
        {
            _mpb ??= new MaterialPropertyBlock();
            float t = Time.time;
            _mpb.SetVector(StId, new Vector4(repeats, 1f, -Mathf.Repeat(t * speed, 1f), 0f));
            // A short brighter pulse every 8 s, like a goal-replay flash.
            float flash = Mathf.Repeat(t, 8f) < 0.25f ? 1.6f : 1f;
            _mpb.SetFloat(IntensityId, intensity * flash);
            foreach (var b in boards)
                if (b != null) b.SetPropertyBlock(_mpb);
        }
    }
}
