using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.Race
{
    /// <summary>
    /// P3 holographic ring (HoloRing gate, Holo Halo): light, not a solid, so it has no collider. The race checks the
    /// rim (<see cref="RimDistance"/>): touching it glitches the ring (<see cref="Glitch"/>) and costs a small time
    /// penalty. The Halo also drops projector beams to the ground and turns slowly about its axis.
    /// The ring lies in the local XY plane, facing local +Z (the flying direction), centred on <see cref="Centre"/>.
    /// </summary>
    [ExecuteAlways]
    public sealed class HoloRing : MonoBehaviour
    {
        private static readonly int GlitchId = Shader.PropertyToID("_Glitch");

        [Tooltip("Ring radius to the centre of the tube (m, before scale).")]
        public float Radius = 3f;
        [Tooltip("Tube radius (m, before scale).")]
        public float Tube = 0.12f;
        [Tooltip("Centre of the ring in local space.")]
        public Vector3 Centre;
        [Tooltip("Degrees per second about the ring's axis while playing (the Halo turns slowly).")]
        public float SpinDegPerSec;
        [Tooltip("Projector beams from the rim down to the ground (the Halo).")]
        public int Projectors;
        public Material BeamMaterial;
        public Renderer[] Renderers = new Renderer[0];

        private MaterialPropertyBlock _mpb;
        private float _glitchUntil;
        private Transform _beams;

        public Vector3 CentreWorld => transform.TransformPoint(Centre);
        public float WorldRadius => Radius * transform.lossyScale.x;
        public float WorldTube => Tube * transform.lossyScale.x;

        /// <summary>Distance from <paramref name="p"/> to the rim's centreline (m, world).</summary>
        public float RimDistance(Vector3 p)
        {
            Vector3 local = transform.InverseTransformPoint(p) - Centre;
            float s = transform.lossyScale.x;
            float rho = new Vector2(local.x, local.y).magnitude * s;
            return new Vector2(rho - WorldRadius, local.z * s).magnitude;
        }

        /// <summary>A short glitch (the rim was touched).</summary>
        public void Glitch(float seconds = 0.6f) => _glitchUntil = Time.time + seconds;

        private void OnEnable()
        {
            if (Projectors > 0 && BeamMaterial != null && _beams == null) BuildBeams();
        }

        private void OnDisable()
        {
            if (_beams != null) DestroyImmediate(_beams.gameObject);
            _beams = null;
        }

        private void Update()
        {
            if (Application.isPlaying && SpinDegPerSec != 0f)
                transform.RotateAround(CentreWorld, transform.forward, SpinDegPerSec * Time.deltaTime);
            float g = Mathf.Clamp01((_glitchUntil - Time.time) / 0.6f);
            _mpb ??= new MaterialPropertyBlock();
            foreach (var r in Renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(GlitchId, g);
                r.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>Thin light beams from points round the rim straight down to the ground (y = 0), in world space.</summary>
        private void BuildBeams()
        {
            var go = new GameObject("Projector Beams") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
            _beams = go.transform;
            _beams.SetParent(transform.parent, false);
            var mesh = new Mesh { name = "Beams" };
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            Vector3 c = CentreWorld;
            Vector3 a = transform.right, b = transform.up;
            for (int i = 0; i < Projectors; i++)
            {
                float ang = i * Mathf.PI * 2f / Projectors;
                Vector3 top = c + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * WorldRadius * 0.92f;
                Vector3 bottom = new Vector3(top.x * 1.15f, 0f, top.z * 1.15f) + (new Vector3(top.x - c.x, 0f, top.z - c.z)) * 0.3f;
                // A flat cross of two quads, 0.4 m wide at the top, 2.5 m at the ground (the projector's cone).
                foreach (var side in new[] { Vector3.right, Vector3.forward })
                {
                    int k = v.Count;
                    v.Add(top - side * 0.2f); v.Add(top + side * 0.2f); v.Add(bottom + side * 1.25f); v.Add(bottom - side * 1.25f);
                    for (int q = 0; q < 4; q++) n.Add(Vector3.Cross(side, Vector3.up));
                    t.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
                }
            }
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BeamMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}
