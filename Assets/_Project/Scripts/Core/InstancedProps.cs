using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.Core
{
    /// <summary>
    /// GPU-instanced street props (trees, cars, lamps, benches, solar panels): each group is one mesh submesh with one
    /// instancing-enabled material and its matrices, kept per 200 m cell so off-screen cells are culled by their
    /// bounds. Drawn with Graphics.RenderMeshInstanced (1023 per call); no GameObject per prop. Edit-mode screenshots
    /// call <see cref="DrawAll"/> for their camera before rendering.
    /// </summary>
    [ExecuteAlways]
    public sealed class InstancedProps : MonoBehaviour
    {
        private const int Batch = 1023;
        private static readonly List<InstancedProps> Active = new List<InstancedProps>();

        [System.Serializable]
        public sealed class Group
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;
            public bool CastShadows = true;
            public Bounds Bounds;
            public Matrix4x4[] Matrices = new Matrix4x4[0];
        }

        [SerializeField] private Group[] groups = new Group[0];
        [SerializeField] private int layer;

        public int Count
        {
            get
            {
                int n = 0;
                foreach (var g in groups) n += g.Matrices.Length;
                return n;
            }
        }

        public void Configure(Group[] propGroups, int renderLayer)
        {
            groups = propGroups;
            layer = renderLayer;
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        private void LateUpdate() => Draw(null);

        // ---------- CPU culling (P7) ----------
        // A big district has hundreds of groups (Track_Expo: 721, 50 000 props): submitting all of them every frame cost
        // ~2 ms of main thread even though the GPU culled most. Groups are skipped here when they are both outside the
        // view (beyond the shadow range, so off-screen casters still shade the view) or too far for their size to see.

        /// <summary>Groups this close to the camera are always drawn (their shadows can reach the view).</summary>
        public const float ShadowKeep = 160f;
        /// <summary>Visible distance per metre of prop size (a 5 m car is ~3 px wide at 1080p by its ~900 m limit).</summary>
        public const float SightPerMetre = 180f;
        public const float MinSight = 200f, MaxSight = 4000f;

        private float[] _sight;
        private static readonly Plane[] Planes = new Plane[6];
        private static int _planesFrame = -1;
        private static Camera _planesCamera;

        /// <summary>Groups submitted / skipped in the last runtime frame (for profiling).</summary>
        public int LastDrawn { get; private set; }
        public int LastSkipped { get; private set; }

        private void PrepareSight()
        {
            if (_sight != null && _sight.Length == groups.Length) return;
            _sight = new float[groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                var g = groups[i];
                float size = 1f;
                if (g.Mesh != null && g.Matrices.Length > 0)
                {
                    var m = g.Matrices[0];
                    float scale = Mathf.Max(m.GetColumn(0).magnitude, Mathf.Max(m.GetColumn(1).magnitude, m.GetColumn(2).magnitude));
                    size = g.Mesh.bounds.size.magnitude * scale;
                }
                _sight[i] = Mathf.Clamp(size * SightPerMetre, MinSight, MaxSight);
            }
        }

        private static Camera CullCamera(Camera camera)
        {
            var cam = camera != null ? camera : Application.isPlaying ? Camera.main : null;
            if (cam == null) return null;
            if (_planesFrame != Time.frameCount || _planesCamera != cam)
            {
                GeometryUtility.CalculateFrustumPlanes(cam, Planes);
                _planesFrame = Time.frameCount;
                _planesCamera = cam;
            }
            return cam;
        }

        /// <summary>Should group <paramref name="i"/> be submitted for this camera?</summary>
        private bool Wanted(int i, Camera cam)
        {
            var b = groups[i].Bounds;
            float dist = Mathf.Sqrt(b.SqrDistance(cam.transform.position));
            if (dist > _sight[i] || dist > cam.farClipPlane) return false;
            return dist <= ShadowKeep || GeometryUtility.TestPlanesAABB(Planes, b);
        }

        /// <summary>Queues every group for <paramref name="camera"/> (null = all cameras this frame).</summary>
        public void Draw(Camera camera)
        {
            PrepareSight();
            var cull = CullCamera(camera);
            int drawn = 0, skipped = 0;
            for (int i = 0; i < groups.Length; i++)
            {
                var g = groups[i];
                if (g.Mesh == null || g.Material == null || g.Matrices.Length == 0) continue;
                if (cull != null && !Wanted(i, cull))
                {
                    skipped++;
                    continue;
                }
                drawn++;
                var rp = new RenderParams(g.Material)
                {
                    camera = camera,
                    layer = layer,
                    worldBounds = g.Bounds,
                    shadowCastingMode = g.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.Off,
                };
                if (g.Material.IsKeywordEnabled("_EMISSION"))
                {
                    _mpb ??= new MaterialPropertyBlock();
                    _mpb.SetColor(EmissionId, g.Material.GetColor(EmissionId) * EmissionScale);
                    rp.matProps = _mpb;
                }
                for (int start = 0; start < g.Matrices.Length; start += Batch)
                    Graphics.RenderMeshInstanced(rp, g.Mesh, g.Submesh, g.Matrices, Mathf.Min(Batch, g.Matrices.Length - start), start);
            }
            if (camera == null)
            {
                LastDrawn = drawn;
                LastSkipped = skipped;
            }
        }

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _mpb;

        /// <summary>Multiplier on the emission of emissive groups (lamps): low by day, full at night. Set by the venue lighting.</summary>
        public static float EmissionScale { get; set; } = 1f;

        public static void DrawAll(Camera camera)
        {
            foreach (var s in Active) s.Draw(camera);
        }
    }
}
