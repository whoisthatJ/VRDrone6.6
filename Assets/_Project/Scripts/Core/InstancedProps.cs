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

        /// <summary>Queues every group for <paramref name="camera"/> (null = all cameras this frame).</summary>
        public void Draw(Camera camera)
        {
            foreach (var g in groups)
            {
                if (g.Mesh == null || g.Material == null || g.Matrices.Length == 0) continue;
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
