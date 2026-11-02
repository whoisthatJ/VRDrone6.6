using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.Track
{
    /// <summary>
    /// Accumulates simple procedural geometry (tubes, boxes, beams, quads) into one mesh with a sub-mesh per material,
    /// so a whole route segment is one renderer. Positions are in the space of the object the mesh goes on.
    /// </summary>
    public sealed class TrackMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int>[] _triangles;

        public TrackMesh(int subMeshes)
        {
            _triangles = new List<int>[subMeshes];
            for (int i = 0; i < subMeshes; i++) _triangles[i] = new List<int>();
        }

        public bool IsEmpty => _vertices.Count == 0;
        public bool HasSubMesh(int sub) => _triangles[sub].Count > 0;

        /// <summary>A round tube through <paramref name="path"/> (open ends), e.g. an LED tube or a cable.</summary>
        public void AddTube(int sub, IReadOnlyList<Vector3> path, float radius, int sides)
        {
            if (path.Count < 2) return;
            int start = _vertices.Count;
            float along = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 t = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 side = Vector3.Cross(Mathf.Abs(t.y) > 0.95f ? Vector3.forward : Vector3.up, t).normalized;
                Vector3 up = Vector3.Cross(t, side);
                if (i > 0) along += Vector3.Distance(path[i - 1], path[i]);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    Vector3 n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    _vertices.Add(path[i] + n * radius);
                    _normals.Add(n);
                    _uvs.Add(new Vector2((float)s / sides, along));
                }
            }
            int ring = sides + 1;
            var tri = _triangles[sub];
            for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * ring + s, b = a + ring;
                tri.Add(a); tri.Add(a + 1); tri.Add(b);
                tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
            }
        }

        /// <summary>An oriented box.</summary>
        public void AddBox(int sub, Vector3 center, Quaternion rotation, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward;
            Face(sub, center + x * h.x, x, y * h.y, z * h.z);
            Face(sub, center - x * h.x, -x, y * h.y, -z * h.z);
            Face(sub, center + y * h.y, y, z * h.z, x * h.x);
            Face(sub, center - y * h.y, -y, -z * h.z, x * h.x);
            Face(sub, center + z * h.z, z, y * h.y, -x * h.x);
            Face(sub, center - z * h.z, -z, y * h.y, x * h.x);
        }

        /// <summary>A square-section beam from <paramref name="a"/> to <paramref name="b"/> (truss members, legs, rails).</summary>
        public void AddBeam(int sub, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 dir = d / len;
            var rot = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up);
            AddBox(sub, (a + b) * 0.5f, rot, new Vector3(thickness, thickness, len));
        }

        /// <summary>A flat quad facing the rotation's +Y (decals, chevron boards, pads). UV 0..1 (or metres), V along +Z.</summary>
        public void AddQuad(int sub, Vector3 center, Quaternion rotation, Vector2 size, bool metricUv = false)
        {
            Vector3 x = rotation * Vector3.right * (size.x * 0.5f);
            Vector3 z = rotation * Vector3.forward * (size.y * 0.5f);
            Vector3 n = rotation * Vector3.up;
            int i = _vertices.Count;
            // Metric UVs (1 unit = 1 m) let tiling textures keep their real size; otherwise 0..1 across the quad.
            float u = metricUv ? size.x : 1f, v = metricUv ? size.y : 1f;
            _vertices.Add(center - x - z); _uvs.Add(new Vector2(0f, 0f));
            _vertices.Add(center + x - z); _uvs.Add(new Vector2(u, 0f));
            _vertices.Add(center + x + z); _uvs.Add(new Vector2(u, v));
            _vertices.Add(center - x + z); _uvs.Add(new Vector2(0f, v));
            for (int k = 0; k < 4; k++) _normals.Add(n);
            var tri = _triangles[sub];
            tri.Add(i); tri.Add(i + 2); tri.Add(i + 1);
            tri.Add(i); tri.Add(i + 3); tri.Add(i + 2);
        }

        /// <summary>A flat disc facing +Y at <paramref name="center"/>, UV 0..1 across its bounding square.</summary>
        public void AddDisc(int sub, Vector3 center, float radius, int segments)
        {
            int c = _vertices.Count;
            _vertices.Add(center); _normals.Add(Vector3.up); _uvs.Add(new Vector2(0.5f, 0.5f));
            for (int k = 0; k <= segments; k++)
            {
                float a = k * Mathf.PI * 2f / segments;
                float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                _vertices.Add(center + new Vector3(cx * radius, 0f, cz * radius));
                _normals.Add(Vector3.up);
                _uvs.Add(new Vector2(0.5f + cx * 0.5f, 0.5f + cz * 0.5f));
            }
            var tri = _triangles[sub];
            for (int k = 0; k < segments; k++)
            {
                tri.Add(c); tri.Add(c + k + 2); tri.Add(c + k + 1);
            }
        }

        // One box face: centre c, normal n, half-extent axes u (V direction) and v (U direction).
        private void Face(int sub, Vector3 c, Vector3 n, Vector3 u, Vector3 v)
        {
            int i = _vertices.Count;
            _vertices.Add(c - u - v); _uvs.Add(new Vector2(0f, 0f));
            _vertices.Add(c - u + v); _uvs.Add(new Vector2(1f, 0f));
            _vertices.Add(c + u + v); _uvs.Add(new Vector2(1f, 1f));
            _vertices.Add(c + u - v); _uvs.Add(new Vector2(0f, 1f));
            for (int k = 0; k < 4; k++) _normals.Add(n);
            var tri = _triangles[sub];
            // Wind so the face points along n.
            if (Vector3.Dot(Vector3.Cross(v, u), n) > 0f)
            {
                tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
                tri.Add(i); tri.Add(i + 2); tri.Add(i + 3);
            }
            else
            {
                tri.Add(i); tri.Add(i + 2); tri.Add(i + 1);
                tri.Add(i); tri.Add(i + 3); tri.Add(i + 2);
            }
        }

        /// <summary>Builds the mesh; sub-meshes keep their index even when empty so materials line up.</summary>
        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _triangles.Length;
            for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
