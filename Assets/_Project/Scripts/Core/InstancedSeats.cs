using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.Core
{
    /// <summary>
    /// Draws thousands of identical stadium seats with GPU instancing (Graphics.RenderMeshInstanced, 1023 per call):
    /// one mesh, one instancing-enabled material per colour group, matrices stored in the scene. No GameObject per
    /// seat. Edit-mode screenshots call <see cref="DrawAll"/> for their camera before rendering.
    /// </summary>
    [ExecuteAlways]
    public sealed class InstancedSeats : MonoBehaviour
    {
        private const int Batch = 1023;
        private static readonly List<InstancedSeats> Active = new List<InstancedSeats>();

        [System.Serializable]
        public sealed class Group
        {
            public Material Material;
            public Matrix4x4[] Matrices = new Matrix4x4[0];
        }

        [SerializeField] private Mesh mesh;
        [SerializeField] private Group[] groups = new Group[0];
        [SerializeField] private Bounds bounds;
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

        public void Configure(Mesh seatMesh, Group[] colourGroups, Bounds worldBounds, int renderLayer)
        {
            mesh = seatMesh;
            groups = colourGroups;
            bounds = worldBounds;
            layer = renderLayer;
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        private void LateUpdate() => Draw(null);

        /// <summary>Queues every seat group for <paramref name="camera"/> (null = all cameras this frame).</summary>
        public void Draw(Camera camera)
        {
            if (mesh == null) return;
            foreach (var g in groups)
            {
                if (g.Material == null || g.Matrices.Length == 0) continue;
                var rp = new RenderParams(g.Material)
                {
                    camera = camera,
                    layer = layer,
                    worldBounds = bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.Off,
                };
                for (int start = 0; start < g.Matrices.Length; start += Batch)
                    Graphics.RenderMeshInstanced(rp, mesh, 0, g.Matrices, Mathf.Min(Batch, g.Matrices.Length - start), start);
            }
        }

        public static void DrawAll(Camera camera)
        {
            foreach (var s in Active) s.Draw(camera);
        }
    }
}
