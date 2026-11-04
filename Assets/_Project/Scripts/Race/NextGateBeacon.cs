using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.Race
{
    /// <summary>
    /// E6: a soft vertical light column on the next gate of a city course, so a gate hidden behind buildings can still be
    /// found from far away. Shown while racing (and on the first gate before the start) when route guidance is on and
    /// the track asks for it (<see cref="Show"/>, set from the track's air-course flag). The column rises from the
    /// ground through the gate to 150 m.
    /// </summary>
    public sealed class NextGateBeacon : MonoBehaviour
    {
        [SerializeField] private RaceSession session;
        [SerializeField] private Material material;
        [SerializeField] private float radius = 0.9f;
        [SerializeField] private float height = 150f;

        private Transform _beam;
        private Renderer _renderer;

        /// <summary>Whether this track uses beacons (city courses).</summary>
        public bool Show { get; set; }

        public void Configure(RaceSession race, Material beam)
        {
            session = race;
            material = beam;
        }

        private void Awake()
        {
            var go = new GameObject("Next Gate Beacon");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Column(radius, height, 16);
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _beam = go.transform;
            go.SetActive(false);
        }

        private void LateUpdate()
        {
            Gate gate = null;
            if (Show && session != null && session.Guidance != GateRush.Core.RouteGuidanceLevel.Off)
            {
                if (session.Phase == RacePhase.Racing) gate = session.NextGate;
                else if (session.Phase != RacePhase.Finished && session.GateCount > 1) gate = session.FinishGate;
            }
            bool on = gate != null && material != null;
            if (_beam.gameObject.activeSelf != on) _beam.gameObject.SetActive(on);
            if (!on) return;
            var p = gate.AimPointWorld;
            _beam.position = new Vector3(p.x, 0f, p.z);
        }

        /// <summary>An open cylinder from y = 0 to <paramref name="h"/>.</summary>
        private static Mesh Column(float r, float h, int sides)
        {
            var v = new Vector3[(sides + 1) * 2];
            var uv = new Vector2[v.Length];
            var n = new Vector3[v.Length];
            var t = new int[sides * 6];
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = dir * r;
                v[i * 2 + 1] = dir * r + Vector3.up * h;
                n[i * 2] = n[i * 2 + 1] = dir;
                uv[i * 2] = new Vector2((float)i / sides, 0f);
                uv[i * 2 + 1] = new Vector2((float)i / sides, 1f);
                if (i == sides) break;
                int k = i * 6, b = i * 2;
                t[k] = b; t[k + 1] = b + 1; t[k + 2] = b + 2;
                t[k + 3] = b + 2; t[k + 4] = b + 1; t[k + 5] = b + 3;
            }
            var mesh = new Mesh { name = "Beacon Column", vertices = v, normals = n, uv = uv, triangles = t };
            mesh.bounds = new Bounds(new Vector3(0f, h / 2, 0f), new Vector3(r * 2, h, r * 2));
            return mesh;
        }
    }
}
