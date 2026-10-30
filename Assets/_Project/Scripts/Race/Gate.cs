using UnityEngine;

namespace GateRush.Race
{
    public enum GateVisualState
    {
        Idle = 0,
        /// <summary>The gate to fly now.</summary>
        Next = 1,
        /// <summary>The one after it (look-ahead).</summary>
        Upcoming = 2,
        Passed = 3,
    }

    /// <summary>
    /// A race gate: an opening the quad must fly through along the gate's local +Z.
    /// The frame (colliders on TrackGeometry) is separate; this component only defines the opening and its look.
    /// </summary>
    public sealed class Gate : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Tooltip("Position in the course (0 = start/finish). Assigned by the track.")]
        [SerializeField] private int order;
        [Tooltip("Opening width/height in metres, before transform scale.")]
        [SerializeField] private Vector2 openingSize = new Vector2(1.52f, 1.52f);
        [Tooltip("Opening centre in local space.")]
        [SerializeField] private Vector3 openingCenter = new Vector3(0f, 1.76f, 0f);
        [Tooltip("Respawn this far before the opening (m).")]
        [SerializeField] private float respawnDistance = 2.5f;
        [SerializeField] private Renderer[] frameRenderers = new Renderer[0];
        [SerializeField] private Color idleColor = new Color(0.55f, 0.55f, 0.6f);
        [SerializeField] private Color nextColor = new Color(0.2f, 1f, 0.35f);
        [SerializeField] private Color upcomingColor = new Color(1f, 0.6f, 0.1f);
        [SerializeField] private Color passedColor = new Color(0.3f, 0.3f, 0.35f);

        private MaterialPropertyBlock _mpb;
        private GateVisualState _state = (GateVisualState)(-1);

        public int Order
        {
            get => order;
            set => order = value;
        }

        public Vector2 OpeningSize => Vector2.Scale(openingSize, transform.lossyScale);
        public Vector3 OpeningCenterWorld => transform.TransformPoint(openingCenter);

        /// <summary>Respawn pose: in front of the opening, level, facing through it.</summary>
        public void GetRespawnPose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 fwd = transform.forward;
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            rotation = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat : Vector3.forward, Vector3.up);
            position = OpeningCenterWorld - rotation * Vector3.forward * respawnDistance;
        }

        public bool TryCross(Vector3 from, Vector3 to, out float fraction) =>
            GateCrossing.TryCross(from, to, OpeningCenterWorld, transform.rotation, OpeningSize * 0.5f, out fraction);

        public void SetVisualState(GateVisualState state)
        {
            if (state == _state) return;
            _state = state;
            _mpb ??= new MaterialPropertyBlock();
            Color c = state switch
            {
                GateVisualState.Next => nextColor,
                GateVisualState.Upcoming => upcomingColor,
                GateVisualState.Passed => passedColor,
                _ => idleColor,
            };
            _mpb.SetColor(BaseColor, c);
            for (int i = 0; i < frameRenderers.Length; i++)
                if (frameRenderers[i] != null) frameRenderers[i].SetPropertyBlock(_mpb);
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(OpeningCenterWorld, transform.rotation, Vector3.one);
            Vector2 s = OpeningSize;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(s.x, s.y, 0.01f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 1.5f);
        }
    }
}
