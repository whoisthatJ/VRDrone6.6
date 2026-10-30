using UnityEngine;

namespace GateRush.Cameras
{
    public enum ScreenFollowMode
    {
        /// <summary>Screen stays where it was placed in the room.</summary>
        Fixed = 0,
        /// <summary>Screen smoothly re-centres in front of the head.</summary>
        SmoothFollow = 1,
    }

    /// <summary>
    /// Curved "goggles" screen showing the drone camera's RenderTexture (mono: the same image in both eyes).
    /// The transform sits at the viewer's head; the mesh is a cylinder arc at <see cref="distance"/> in front.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class GogglesScreen : MonoBehaviour
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        [SerializeField] private Transform head;
        [SerializeField, Range(30f, 100f)] private float arcDegrees = 60f;
        [SerializeField, Range(1f, 5f)] private float distance = 2f;
        [SerializeField] private float aspect = 16f / 9f;
        [SerializeField] private ScreenFollowMode followMode = ScreenFollowMode.SmoothFollow;
        [Tooltip("Follow speed (1/s). Higher = snappier.")]
        [SerializeField] private float followSharpness = 3f;
        [SerializeField, Range(4, 64)] private int segments = 32;

        private MaterialPropertyBlock _mpb;
        private Mesh _mesh;
        private float _builtArc = -1f, _builtDistance = -1f, _builtAspect = -1f;

        public ScreenFollowMode FollowMode
        {
            get => followMode;
            set => followMode = value;
        }

        public float ArcDegrees
        {
            get => arcDegrees;
            set => arcDegrees = Mathf.Clamp(value, 30f, 100f);
        }

        public Transform Head
        {
            get => head;
            set => head = value;
        }

        public void SetTexture(Texture texture)
        {
            _mpb ??= new MaterialPropertyBlock();
            var r = GetComponent<MeshRenderer>();
            r.GetPropertyBlock(_mpb);
            _mpb.SetTexture(BaseMap, texture);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Places the screen straight ahead of the head right now.</summary>
        public void Recenter()
        {
            if (head == null) return;
            transform.SetPositionAndRotation(head.position, YawPitchOnly(head.rotation));
        }

        private void OnEnable() => Recenter();

        private void LateUpdate()
        {
            if (arcDegrees != _builtArc || distance != _builtDistance || aspect != _builtAspect) BuildMesh();
            if (head == null) return;

            // The head can move around in the room; the arc's centre always tracks the head position.
            if (followMode == ScreenFollowMode.Fixed)
            {
                transform.position = head.position;
                return;
            }

            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.SetPositionAndRotation(head.position, Quaternion.Slerp(transform.rotation, YawPitchOnly(head.rotation), k));
        }

        private static Quaternion YawPitchOnly(Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            return Quaternion.Euler(e.x, e.y, 0f);
        }

        private void BuildMesh()
        {
            _builtArc = arcDegrees;
            _builtDistance = distance;
            _builtAspect = aspect;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "GogglesScreen" };
                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }

            int cols = segments + 1;
            var verts = new Vector3[cols * 2];
            var uvs = new Vector2[cols * 2];
            var tris = new int[segments * 6];
            float arc = arcDegrees * Mathf.Deg2Rad;
            float height = distance * arc / aspect;

            for (int i = 0; i < cols; i++)
            {
                float u = (float)i / segments;
                float a = (u - 0.5f) * arc;
                float x = Mathf.Sin(a) * distance;
                float z = Mathf.Cos(a) * distance;
                verts[i] = new Vector3(x, -height * 0.5f, z);
                verts[i + cols] = new Vector3(x, height * 0.5f, z);
                uvs[i] = new Vector2(u, 0f);
                uvs[i + cols] = new Vector2(u, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int t = i * 6;
                // Clockwise as seen from the centre, so the inside faces the viewer.
                tris[t] = i;
                tris[t + 1] = i + cols;
                tris[t + 2] = i + 1;
                tris[t + 3] = i + 1;
                tris[t + 4] = i + cols;
                tris[t + 5] = i + cols + 1;
            }

            _mesh.Clear();
            _mesh.vertices = verts;
            _mesh.uv = uvs;
            _mesh.triangles = tris;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
