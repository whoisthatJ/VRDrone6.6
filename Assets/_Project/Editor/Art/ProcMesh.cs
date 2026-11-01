using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Procedural meshes for the art pass. "Unit-space" meshes (Box, PaddedBar) live in the ±0.5 cube so they can
    /// replace a primitive cube on a scaled transform without touching its BoxCollider; their UVs, normals and
    /// tangents are computed for the real size so textures keep real-world scale. Everything else is built in metres.
    /// </summary>
    public static class ProcMesh
    {
        private sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector4> T = new List<Vector4>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<List<int>> Sub = new List<List<int>> { new List<int>() };

            public int Add(Vector3 v, Vector3 n, Vector4 t, Vector2 uv)
            {
                V.Add(v);
                N.Add(n);
                T.Add(t);
                UV.Add(uv);
                return V.Count - 1;
            }

            public void Quad(int a, int b, int c, int d, int sub = 0)
            {
                while (Sub.Count <= sub) Sub.Add(new List<int>());
                Sub[sub].AddRange(new[] { a, b, c, a, c, d });
            }

            public void Tri(int a, int b, int c, int sub = 0)
            {
                while (Sub.Count <= sub) Sub.Add(new List<int>());
                Sub[sub].AddRange(new[] { a, b, c });
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetTangents(T);
                m.SetUVs(0, UV);
                m.subMeshCount = Sub.Count;
                for (int i = 0; i < Sub.Count; i++) m.SetTriangles(Sub[i], i);
                m.RecalculateBounds();
                return m;
            }
        }

        private static Vector4 Tangent(Vector3 n, Vector3 t, Vector3 bitangent) =>
            new Vector4(t.x, t.y, t.z, Vector3.Dot(Vector3.Cross(n, t), bitangent) >= 0f ? 1f : -1f);

        // ---------- metric-UV box ----------

        private static readonly (Vector3 n, Vector3 u, Vector3 v)[] Faces =
        {
            (Vector3.right, Vector3.forward, Vector3.up),
            (Vector3.left, Vector3.back, Vector3.up),
            (Vector3.forward, Vector3.left, Vector3.up),
            (Vector3.back, Vector3.right, Vector3.up),
            (Vector3.up, Vector3.right, Vector3.forward),
            (Vector3.down, Vector3.left, Vector3.forward),
        };

        /// <summary>
        /// Unit cube (±0.5) for a transform scaled to <paramref name="size"/>: UV0 in metres (texture tiling = 1/tile
        /// size), offset by <paramref name="uvOrigin"/> (object position in its local axes) so neighbours continue the
        /// pattern; UV1 is a proportional lightmap unwrap.
        /// </summary>
        public static Mesh Box(Vector3 size, Vector3 uvOrigin = default, bool lightmapUv = true, string name = "BoxUV")
        {
            var b = new Builder();
            foreach (var (n, u, v) in Faces)
            {
                Vector3 c = n * 0.5f;
                Vector3[] corners = { c - u * 0.5f - v * 0.5f, c + u * 0.5f - v * 0.5f, c + u * 0.5f + v * 0.5f, c - u * 0.5f + v * 0.5f };
                int i0 = b.V.Count;
                foreach (var p in corners)
                {
                    Vector3 real = Vector3.Scale(p, size) + uvOrigin;
                    b.Add(p, n, Tangent(n, u, v), new Vector2(Vector3.Dot(real, u), Vector3.Dot(real, v)));
                }
                b.Quad(i0, i0 + 3, i0 + 2, i0 + 1);
            }
            var mesh = b.Build(name);
            if (lightmapUv) AddLightmapUv(mesh, size);
            return mesh;
        }

        /// <summary>
        /// Unwraps UV1 on a copy scaled to real size (so charts keep their proportions), then takes the whole unwrapped
        /// mesh back into unit space (the unwrapper may split vertices, so every attribute comes from the copy).
        /// </summary>
        public static void AddLightmapUv(Mesh unitMesh, Vector3 size)
        {
            var tmp = Object.Instantiate(unitMesh);
            var verts = tmp.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] = Vector3.Scale(verts[i], size);
            tmp.vertices = verts;
            UnwrapParam.SetDefaults(out var p);
            p.packMargin = 0.02f;
            Unwrapping.GenerateSecondaryUVSet(tmp, p);
            verts = tmp.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(verts[i].x / size.x, verts[i].y / size.y, verts[i].z / size.z);
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            tmp.GetUVs(0, uv0);
            tmp.GetUVs(1, uv1);
            unitMesh.Clear();
            unitMesh.vertices = verts;
            unitMesh.normals = tmp.normals;
            unitMesh.tangents = tmp.tangents;
            unitMesh.SetUVs(0, uv0);
            unitMesh.SetUVs(1, uv1);
            unitMesh.subMeshCount = tmp.subMeshCount;
            for (int s = 0; s < tmp.subMeshCount; s++) unitMesh.SetTriangles(tmp.GetTriangles(s), s);
            unitMesh.RecalculateBounds();
            Object.DestroyImmediate(tmp);
        }

        // ---------- padded bar (gate covers, sandbags) ----------

        /// <summary>
        /// Rounded-rectangle tube along the longest axis of <paramref name="size"/>, in unit-cube space (replaces a
        /// scaled cube). Corner radius = <paramref name="corner"/> × the smaller cross-section side.
        /// </summary>
        public static Mesh PaddedBar(Vector3 size, float corner = 0.38f, int cornerSegments = 4, string name = "PaddedBar")
        {
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
            float w = size[a1], h = size[a2], len = size[axis];
            float r = Mathf.Min(w, h) * corner;

            // Cross-section outline (counter-clockwise) in metres.
            var outline = new List<Vector2>();
            var normals2 = new List<Vector2>();
            Vector2[] centres = { new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r), new Vector2(-w / 2 + r, -h / 2 + r), new Vector2(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
                for (int s = 0; s <= cornerSegments; s++)
                {
                    float ang = (c * 90f + s * 90f / cornerSegments) * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    outline.Add(centres[c] + dir * r);
                    normals2.Add(dir);
                }

            Vector3 Unit(Vector2 p, float along)
            {
                var v = Vector3.zero;
                v[axis] = along / len;
                v[a1] = p.x / w;
                v[a2] = p.y / h;
                return v;
            }

            Vector3 ObjNormal(Vector3 real) => new Vector3(real.x * size.x, real.y * size.y, real.z * size.z).normalized;

            var b = new Builder();
            float perimeter = 0f;
            var u = new float[outline.Count + 1];
            for (int i = 0; i < outline.Count; i++)
            {
                u[i] = perimeter;
                perimeter += Vector2.Distance(outline[i], outline[(i + 1) % outline.Count]);
            }
            u[outline.Count] = perimeter;
            Vector3 along3 = Vector3.zero;
            along3[axis] = 1f;
            for (int i = 0; i <= outline.Count; i++)
            {
                int k = i % outline.Count;
                Vector3 nReal = Vector3.zero;
                nReal[a1] = normals2[k].x;
                nReal[a2] = normals2[k].y;
                Vector3 n = ObjNormal(nReal);
                var t = new Vector4(along3.x, along3.y, along3.z, 1f);
                b.Add(Unit(outline[k], -len / 2), n, t, new Vector2(u[i], -len / 2));
                b.Add(Unit(outline[k], len / 2), n, t, new Vector2(u[i], len / 2));
            }
            for (int i = 0; i < outline.Count; i++)
            {
                int i0 = i * 2;
                b.Quad(i0, i0 + 2, i0 + 3, i0 + 1);
            }
            // End caps (fans).
            foreach (float end in new[] { -1f, 1f })
            {
                Vector3 nReal = along3 * end;
                Vector3 n = ObjNormal(nReal);
                Vector3 tReal = Vector3.zero;
                tReal[a1] = 1f;
                int centre = b.Add(Unit(Vector2.zero, end * len / 2), n, new Vector4(tReal.x, tReal.y, tReal.z, 1f), Vector2.zero);
                int first = b.V.Count;
                foreach (var p in outline) b.Add(Unit(p, end * len / 2), n, new Vector4(tReal.x, tReal.y, tReal.z, 1f), p);
                for (int i = 0; i < outline.Count; i++)
                {
                    int i1 = first + i, i2 = first + (i + 1) % outline.Count;
                    if (end > 0f) b.Tri(centre, i2, i1);
                    else b.Tri(centre, i1, i2);
                }
            }
            return FixWinding(b.Build(name), size);
        }

        /// <summary>Flips triangles whose winding disagrees with their vertex normals (robust against axis permutations).</summary>
        private static Mesh FixWinding(Mesh m, Vector3 size)
        {
            var v = m.vertices;
            var n = m.normals;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var tris = m.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 p0 = Vector3.Scale(v[tris[i]], size), p1 = Vector3.Scale(v[tris[i + 1]], size), p2 = Vector3.Scale(v[tris[i + 2]], size);
                    Vector3 face = Vector3.Cross(p1 - p0, p2 - p0);
                    Vector3 avg = n[tris[i]] + n[tris[i + 1]] + n[tris[i + 2]];
                    avg = new Vector3(avg.x / size.x, avg.y / size.y, avg.z / size.z);
                    if (Vector3.Dot(face, avg) < 0f) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                }
                m.SetTriangles(tris, s);
            }
            return m;
        }

        // ---------- metre-space helpers ----------

        /// <summary>Combines meshes (each with its own transform and submesh index) into one mesh in metres.</summary>
        public static Mesh Combine(string name, List<(Mesh mesh, Matrix4x4 matrix, int submesh)> parts, bool lightmapUv = false)
        {
            int subCount = 0;
            foreach (var p in parts) subCount = Mathf.Max(subCount, p.submesh + 1);
            var perSub = new List<CombineInstance>[subCount];
            for (int i = 0; i < subCount; i++) perSub[i] = new List<CombineInstance>();
            foreach (var p in parts)
                for (int s = 0; s < p.mesh.subMeshCount; s++)
                    perSub[p.submesh].Add(new CombineInstance { mesh = p.mesh, subMeshIndex = s, transform = p.matrix });
            var subMeshes = new List<CombineInstance>();
            for (int i = 0; i < subCount; i++)
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32 };
                m.CombineMeshes(perSub[i].ToArray(), true, true);
                subMeshes.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
            }
            var result = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            result.CombineMeshes(subMeshes.ToArray(), false, false);
            foreach (var c in subMeshes) Object.DestroyImmediate(c.mesh);
            result.RecalculateBounds();
            if (lightmapUv)
            {
                UnwrapParam.SetDefaults(out var up);
                Unwrapping.GenerateSecondaryUVSet(result, up);
            }
            return result;
        }

        /// <summary>A metre-sized box at <paramref name="centre"/> with metric UVs (for Combine).</summary>
        public static (Mesh, Matrix4x4, int) Part(Vector3 centre, Vector3 size, Quaternion rotation, int submesh = 0) =>
            (Box(size, Vector3.zero, false), Matrix4x4.TRS(centre, rotation, size), submesh);

        /// <summary>Warren truss along +Z: top and bottom chords plus alternating diagonals.</summary>
        public static Mesh Truss(float length, float depth, float chord, float web, float panel)
        {
            var parts = new List<(Mesh, Matrix4x4, int)>
            {
                Part(new Vector3(0f, depth / 2 - chord / 2, 0f), new Vector3(chord * 1.6f, chord, length), Quaternion.identity),
                Part(new Vector3(0f, -depth / 2 + chord / 2, 0f), new Vector3(chord * 1.6f, chord, length), Quaternion.identity),
            };
            int panels = Mathf.Max(1, Mathf.RoundToInt(length / panel));
            float step = length / panels, inner = depth - chord;
            for (int i = 0; i < panels; i++)
            {
                float z0 = -length / 2 + i * step, z1 = z0 + step;
                bool up = i % 2 == 0;
                var a = new Vector3(0f, up ? -inner / 2 : inner / 2, z0);
                var b = new Vector3(0f, up ? inner / 2 : -inner / 2, z1);
                float l = Vector3.Distance(a, b);
                parts.Add(Part((a + b) / 2, new Vector3(web, web, l), Quaternion.LookRotation(b - a)));
            }
            return Combine("Truss", parts);
        }

        /// <summary>Vertical I-beam (flanges along X) of the given height.</summary>
        public static Mesh IBeam(float height, float width, float depth, float web, float flange)
        {
            var parts = new List<(Mesh, Matrix4x4, int)>
            {
                Part(new Vector3(0f, 0f, depth / 2 - flange / 2), new Vector3(width, height, flange), Quaternion.identity),
                Part(new Vector3(0f, 0f, -depth / 2 + flange / 2), new Vector3(width, height, flange), Quaternion.identity),
                Part(Vector3.zero, new Vector3(web, height, depth - 2 * flange), Quaternion.identity),
            };
            return Combine("IBeam", parts);
        }

        /// <summary>Traffic cone (submesh 0 orange plastic, 1 reflective band): base plate + frustum, pivot at the base.</summary>
        public static Mesh Cone(float height = 0.5f, float baseRadius = 0.14f, float topRadius = 0.025f, int segments = 18)
        {
            float slope = (baseRadius - topRadius) / height;
            float bandLow = height * 0.45f, bandHigh = height * 0.68f;
            Mesh Frustum(float y0, float y1)
            {
                var b = new Builder();
                float r0 = baseRadius - slope * y0, r1 = baseRadius - slope * y1;
                for (int s = 0; s <= segments; s++)
                {
                    float ang = s * Mathf.PI * 2f / segments;
                    var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    var n = (dir + Vector3.up * slope).normalized;
                    var t = new Vector4(-dir.z, 0f, dir.x, 1f);
                    b.Add(dir * r0 + Vector3.up * y0, n, t, new Vector2(ang * r0, y0));
                    b.Add(dir * r1 + Vector3.up * y1, n, t, new Vector2(ang * r1, y1));
                }
                for (int s = 0; s < segments; s++)
                {
                    int i0 = s * 2;
                    b.Quad(i0, i0 + 1, i0 + 3, i0 + 2);
                }
                return b.Build("Frustum");
            }

            var lower = Frustum(0.03f, bandLow);
            var band = Frustum(bandLow, bandHigh);
            var upper = Frustum(bandHigh, height);
            var parts = new List<(Mesh, Matrix4x4, int)>
            {
                Part(new Vector3(0f, 0.015f, 0f), new Vector3(0.36f, 0.03f, 0.36f), Quaternion.identity, 0),
                (lower, Matrix4x4.identity, 0),
                (upper, Matrix4x4.identity, 0),
                (band, Matrix4x4.identity, 1),
            };
            var mesh = Combine("Cone", parts);
            Object.DestroyImmediate(lower);
            Object.DestroyImmediate(band);
            Object.DestroyImmediate(upper);
            return FixWindingMetres(mesh);
        }

        /// <summary>Flips triangles whose winding disagrees with their normals (metre-space meshes).</summary>
        public static Mesh FixWindingMetres(Mesh m) => FixWinding(m, Vector3.one);

        /// <summary>Flat horizontal strip (paint lines, chalk) from a to b, <paramref name="width"/> wide, normal up.</summary>
        public static void Strip(List<(Mesh, Matrix4x4, int)> parts, Vector3 a, Vector3 b, float width, float thickness = 0.004f, int submesh = 0)
        {
            Vector3 d = b - a;
            d.y = 0f;
            parts.Add(Part((a + b) / 2 + Vector3.up * thickness / 2, new Vector3(width, thickness, d.magnitude), Quaternion.LookRotation(d.normalized), submesh));
        }

        /// <summary>Ring of flat strips (centre circle).</summary>
        public static void Ring(List<(Mesh, Matrix4x4, int)> parts, Vector3 centre, float radius, float width, int segments = 48)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Strip(parts, centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius, centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius, width);
            }
        }

        /// <summary>Pop-up canopy: 4 legs (submesh 0) and a pyramid roof with valance (submesh 1, double-sided fabric).</summary>
        public static Mesh Canopy(float size = 3f, float eave = 2.15f, float apex = 2.8f, float valance = 0.25f)
        {
            float h = size / 2f, leg = 0.035f;
            var parts = new List<(Mesh, Matrix4x4, int)>();
            foreach (var c in new[] { new Vector2(h, h), new Vector2(-h, h), new Vector2(-h, -h), new Vector2(h, -h) })
                parts.Add(Part(new Vector3(c.x, eave / 2, c.y), new Vector3(leg, eave, leg), Quaternion.identity));
            // frame rails under the roof edge
            parts.Add(Part(new Vector3(0f, eave - 0.02f, h), new Vector3(size, 0.03f, 0.03f), Quaternion.identity));
            parts.Add(Part(new Vector3(0f, eave - 0.02f, -h), new Vector3(size, 0.03f, 0.03f), Quaternion.identity));
            parts.Add(Part(new Vector3(h, eave - 0.02f, 0f), new Vector3(0.03f, 0.03f, size), Quaternion.identity));
            parts.Add(Part(new Vector3(-h, eave - 0.02f, 0f), new Vector3(0.03f, 0.03f, size), Quaternion.identity));

            var b = new Builder();
            Vector3 top = new Vector3(0f, apex, 0f);
            Vector3[] corners = { new Vector3(h, eave, h), new Vector3(-h, eave, h), new Vector3(-h, eave, -h), new Vector3(h, eave, -h) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 c0 = corners[i], c1 = corners[(i + 1) % 4];
                Vector3 n = Vector3.Cross(c1 - c0, top - c0).normalized;
                if (n.y < 0f) n = -n;
                var t = new Vector4((c1 - c0).normalized.x, 0f, (c1 - c0).normalized.z, 1f);
                int a = b.Add(c0, n, t, new Vector2(0f, 0f)), bb = b.Add(c1, n, t, new Vector2(size, 0f)), cc = b.Add(top, n, t, new Vector2(size / 2, h * 1.2f));
                b.Tri(a, cc, bb, 0);
                // valance skirt
                Vector3 outN = new Vector3((c0 + c1).x, 0f, (c0 + c1).z).normalized;
                int v0 = b.Add(c0, outN, t, new Vector2(0f, valance)), v1 = b.Add(c1, outN, t, new Vector2(size, valance));
                int v2 = b.Add(c1 - Vector3.up * valance, outN, t, new Vector2(size, 0f)), v3 = b.Add(c0 - Vector3.up * valance, outN, t, new Vector2(0f, 0f));
                b.Quad(v0, v1, v2, v3, 0);
            }
            var roof = b.Build("CanopyRoof");
            parts.Add((roof, Matrix4x4.identity, 1));
            var mesh = Combine("Canopy", parts);
            Object.DestroyImmediate(roof);
            return mesh;
        }

        /// <summary>Feather flag on a pole (submesh 0 pole, 1 fabric, double-sided). Pivot at the ground.</summary>
        public static Mesh FeatherFlag(float height = 3.2f, float width = 0.75f)
        {
            var parts = new List<(Mesh, Matrix4x4, int)> { Part(new Vector3(0f, height / 2, 0f), new Vector3(0.03f, height, 0.03f), Quaternion.identity) };
            var b = new Builder();
            int rows = 10;
            for (int i = 0; i <= rows; i++)
            {
                float t = i / (float)rows;
                float y = 0.6f + t * (height - 0.65f);
                float w = width * (t < 0.8f ? 1f : Mathf.Lerp(1f, 0.35f, (t - 0.8f) / 0.2f));
                float bend = Mathf.Sin(t * Mathf.PI) * 0.12f;
                var n = new Vector3(0f, 0f, -1f);
                b.Add(new Vector3(0.02f, y, 0f), n, new Vector4(1, 0, 0, 1), new Vector2(0f, y));
                b.Add(new Vector3(0.02f + w, y + (t > 0.8f ? (t - 0.8f) * 0.8f : 0f), bend), n, new Vector4(1, 0, 0, 1), new Vector2(w, y));
            }
            for (int i = 0; i < rows; i++)
            {
                int i0 = i * 2;
                b.Quad(i0, i0 + 2, i0 + 3, i0 + 1);
            }
            var cloth = b.Build("FlagCloth");
            parts.Add((cloth, Matrix4x4.identity, 1));
            var mesh = Combine("FeatherFlag", parts);
            Object.DestroyImmediate(cloth);
            return mesh;
        }

        /// <summary>
        /// Impostor tree: <paramref name="planes"/> vertical quads crossed around Y, plane i textured with atlas column i.
        /// Normals bend outward from the canopy centre so the card shades like a rounded crown.
        /// </summary>
        public static Mesh CrossedQuads(int planes, float width, float height, string name = "Impostor")
        {
            var b = new Builder();
            Vector3 crown = new Vector3(0f, height * 0.6f, 0f);
            for (int i = 0; i < planes; i++)
            {
                float ang = i * Mathf.PI / planes;
                var right = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float u0 = i / (float)planes, u1 = (i + 1) / (float)planes;
                Vector3[] p = { -right * width / 2, right * width / 2, right * width / 2 + Vector3.up * height, -right * width / 2 + Vector3.up * height };
                Vector2[] uv = { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f) };
                int first = b.V.Count;
                for (int k = 0; k < 4; k++)
                {
                    var n = ((p[k] - crown).normalized * 0.7f + Vector3.up * 0.5f).normalized;
                    b.Add(p[k], n, new Vector4(right.x, 0f, right.z, 1f), uv[k]);
                }
                b.Quad(first, first + 3, first + 2, first + 1);
            }
            return b.Build(name);
        }

        /// <summary>Jersey (F-shape) concrete barrier, 3 m long along X, pivot at the base.</summary>
        public static Mesh JerseyBarrier(float length = 3f)
        {
            Vector2[] profile =
            {
                new Vector2(-0.3f, 0f), new Vector2(0.3f, 0f), new Vector2(0.3f, 0.08f), new Vector2(0.18f, 0.25f),
                new Vector2(0.08f, 0.81f), new Vector2(-0.08f, 0.81f), new Vector2(-0.18f, 0.25f), new Vector2(-0.3f, 0.08f),
            };
            var b = new Builder();
            float per = 0f;
            for (int i = 0; i < profile.Length; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[(i + 1) % profile.Length];
                Vector2 e = p1 - p0;
                // Profile is counter-clockwise in (x = across, y = up); across maps to Z, so the outward normal is (0, -e.x, e.y).
                var n3 = new Vector3(0f, -e.x, e.y).normalized;
                float len = e.magnitude;
                var t = new Vector4(1f, 0f, 0f, 1f);
                int a = b.Add(new Vector3(-length / 2, p0.y, p0.x), new Vector3(0f, n3.y, n3.z), t, new Vector2(0f, per));
                int c = b.Add(new Vector3(length / 2, p0.y, p0.x), new Vector3(0f, n3.y, n3.z), t, new Vector2(length, per));
                int d = b.Add(new Vector3(length / 2, p1.y, p1.x), new Vector3(0f, n3.y, n3.z), t, new Vector2(length, per + len));
                int f = b.Add(new Vector3(-length / 2, p1.y, p1.x), new Vector3(0f, n3.y, n3.z), t, new Vector2(0f, per + len));
                b.Quad(a, f, d, c);
                per += len;
            }
            foreach (float end in new[] { -1f, 1f })
            {
                int first = b.V.Count;
                foreach (var p in profile) b.Add(new Vector3(end * length / 2, p.y, p.x), new Vector3(end, 0f, 0f), new Vector4(0f, 0f, 1f, 1f), new Vector2(p.x, p.y));
                for (int i = 1; i < profile.Length - 1; i++)
                    if (end > 0f) b.Tri(first, first + i, first + i + 1);
                    else b.Tri(first, first + i + 1, first + i);
            }
            var m = b.Build("JerseyBarrier");
            return FixWinding(m, Vector3.one);
        }

        /// <summary>1 × 1 m horizontal quad, normal up, UV 0–1 (scale the transform to size it).</summary>
        public static Mesh UnitQuadXZ()
        {
            var b = new Builder();
            var n = Vector3.up;
            var t = new Vector4(1f, 0f, 0f, 1f);
            int a = b.Add(new Vector3(-0.5f, 0f, -0.5f), n, t, new Vector2(0f, 0f));
            int c = b.Add(new Vector3(0.5f, 0f, -0.5f), n, t, new Vector2(1f, 0f));
            int d = b.Add(new Vector3(0.5f, 0f, 0.5f), n, t, new Vector2(1f, 1f));
            int e = b.Add(new Vector3(-0.5f, 0f, 0.5f), n, t, new Vector2(0f, 1f));
            b.Quad(a, e, d, c);
            return b.Build("UnitQuadXZ");
        }

        /// <summary>EUR pallet 1.2 × 0.8 × 0.144 m (wood), pivot at the base.</summary>
        public static Mesh Pallet()
        {
            var parts = new List<(Mesh, Matrix4x4, int)>();
            for (int i = 0; i < 5; i++) parts.Add(Part(new Vector3(0f, 0.133f, -0.35f + i * 0.175f), new Vector3(1.2f, 0.022f, 0.1f), Quaternion.identity));
            for (int i = 0; i < 3; i++) parts.Add(Part(new Vector3(-0.55f + i * 0.55f, 0.072f, 0f), new Vector3(0.1f, 0.1f, 0.8f), Quaternion.identity));
            for (int i = 0; i < 3; i++) parts.Add(Part(new Vector3(0f, 0.011f, -0.35f + i * 0.35f), new Vector3(1.2f, 0.022f, 0.1f), Quaternion.identity));
            return Combine("Pallet", parts);
        }
    }
}
