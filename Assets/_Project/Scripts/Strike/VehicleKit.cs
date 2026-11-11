using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.Strike
{
    /// <summary>
    /// A small mesh kit for the range's vehicles: chamfered boxes, cylinders and wedges with UVs in metres (so the
    /// library textures keep their scale), merged into one mesh per material slot. A vehicle is a few draw calls.
    /// </summary>
    public sealed class VehicleKit
    {
        public enum Slot { Paint = 0, Rubber = 1, Metal = 2, Dark = 3, Glass = 4, Canvas = 5, Light = 6 }
        public const int Slots = 7;

        private sealed class Part
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();
        }

        private readonly Part[] _parts = new Part[Slots];
        /// <summary>Metres per texture repeat.</summary>
        public float Tile = 1.5f;

        public VehicleKit()
        {
            for (int i = 0; i < Slots; i++) _parts[i] = new Part();
        }

        // ---------- primitives ----------

        /// <summary>A box with its edges chamfered by <paramref name="bevel"/> metres.</summary>
        public void Box(Slot slot, Vector3 centre, Vector3 size, float bevel = 0.06f, Quaternion? rotation = null)
        {
            var r = rotation ?? Quaternion.identity;
            Vector3 h = size * 0.5f;
            float c = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.45f);
            // For each corner: its point on the X, Y and Z faces.
            Vector3 P(int axis, float sx, float sy, float sz) => axis switch
            {
                0 => new Vector3(sx * h.x, sy * (h.y - c), sz * (h.z - c)),
                1 => new Vector3(sx * (h.x - c), sy * h.y, sz * (h.z - c)),
                _ => new Vector3(sx * (h.x - c), sy * (h.y - c), sz * h.z),
            };
            var poly = new List<Vector3>(4);
            void Emit()
            {
                for (int i = 0; i < poly.Count; i++) poly[i] = centre + r * poly[i];
                Polygon(slot, poly, centre);
                poly.Clear();
            }
            float[] s = { -1f, 1f };
            // Main faces.
            foreach (float a in s)
            {
                poly.Add(P(0, a, -1, -1)); poly.Add(P(0, a, 1, -1)); poly.Add(P(0, a, 1, 1)); poly.Add(P(0, a, -1, 1)); Emit();
                poly.Add(P(1, -1, a, -1)); poly.Add(P(1, 1, a, -1)); poly.Add(P(1, 1, a, 1)); poly.Add(P(1, -1, a, 1)); Emit();
                poly.Add(P(2, -1, -1, a)); poly.Add(P(2, 1, -1, a)); poly.Add(P(2, 1, 1, a)); poly.Add(P(2, -1, 1, a)); Emit();
            }
            if (c <= 1e-4f) return;
            // Edge bevels and corner triangles.
            foreach (float a in s)
                foreach (float b in s)
                {
                    poly.Add(P(0, a, b, -1)); poly.Add(P(0, a, b, 1)); poly.Add(P(1, a, b, 1)); poly.Add(P(1, a, b, -1)); Emit(); // X-Y edge
                    poly.Add(P(0, a, -1, b)); poly.Add(P(0, a, 1, b)); poly.Add(P(2, a, 1, b)); poly.Add(P(2, a, -1, b)); Emit(); // X-Z edge
                    poly.Add(P(1, -1, a, b)); poly.Add(P(1, 1, a, b)); poly.Add(P(2, 1, a, b)); poly.Add(P(2, -1, a, b)); Emit(); // Y-Z edge
                    foreach (float d in s)
                    {
                        poly.Add(P(0, a, b, d)); poly.Add(P(1, a, b, d)); poly.Add(P(2, a, b, d)); Emit();
                    }
                }
        }

        /// <summary>A cylinder between two points (smooth sides, flat caps).</summary>
        public void Cylinder(Slot slot, Vector3 a, Vector3 b, float radius, int segments = 14, bool caps = true)
        {
            var part = _parts[(int)slot];
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-4f) return;
            Vector3 dir = axis / len;
            Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(dir, u);
            int baseIndex = part.V.Count;
            float circ = 2f * Mathf.PI * radius;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                Vector3 n = u * Mathf.Cos(t) + w * Mathf.Sin(t);
                part.V.Add(a + n * radius); part.N.Add(n); part.UV.Add(new Vector2(i / (float)segments * circ / Tile, 0f));
                part.V.Add(b + n * radius); part.N.Add(n); part.UV.Add(new Vector2(i / (float)segments * circ / Tile, len / Tile));
            }
            for (int i = 0; i < segments; i++)
            {
                int k = baseIndex + i * 2;
                Tri(part, k, k + 1, k + 3, a);
                Tri(part, k, k + 3, k + 2, a);
            }
            if (!caps) return;
            foreach (var (centre, normal) in new[] { (a, -dir), (b, dir) })
            {
                var ring = new List<Vector3>(segments);
                for (int i = 0; i < segments; i++)
                {
                    float t = i / (float)segments * Mathf.PI * 2f;
                    ring.Add(centre + (u * Mathf.Cos(t) + w * Mathf.Sin(t)) * radius);
                }
                Polygon(slot, ring, centre - normal);
            }
        }

        /// <summary>A wheel: tyre, hub and a few bolts, axis along X.</summary>
        public void Wheel(Vector3 centre, float radius, float width, float side)
        {
            Vector3 half = Vector3.right * (width * 0.5f);
            Cylinder(Slot.Rubber, centre - half, centre + half, radius, 18);
            Vector3 face = centre + Vector3.right * (side * width * 0.5f);
            Cylinder(Slot.Metal, face, face + Vector3.right * (side * 0.05f), radius * 0.55f, 12);
            Cylinder(Slot.Dark, face + Vector3.right * (side * 0.05f), face + Vector3.right * (side * 0.11f), radius * 0.18f, 8);
        }

        /// <summary>A track run along Z: belt, road wheels, drive sprocket and idler, return rollers.</summary>
        public void Track(float x, float length, float height, float width, int roadWheels)
        {
            float wheelR = height * 0.36f, y = wheelR + 0.05f;
            float z0 = -length * 0.5f + wheelR * 1.2f, z1 = length * 0.5f - wheelR * 1.2f;
            // Belt: top run, bottom run and the rounded ends (as boxes and cylinders).
            Box(Slot.Rubber, new Vector3(x, height - 0.06f, 0f), new Vector3(width, 0.1f, length - wheelR * 1.2f), 0.03f);
            Box(Slot.Rubber, new Vector3(x, 0.06f, 0f), new Vector3(width, 0.1f, length - wheelR * 2.4f), 0.03f);
            Vector3 hx = Vector3.right * (width * 0.5f);
            float endR = (height - 0.06f) * 0.5f;
            Cylinder(Slot.Rubber, new Vector3(x, endR, z0 - 0.15f) - hx, new Vector3(x, endR, z0 - 0.15f) + hx, endR, 16);
            Cylinder(Slot.Rubber, new Vector3(x, endR + 0.12f, z1 + 0.15f) - hx, new Vector3(x, endR + 0.12f, z1 + 0.15f) + hx, endR - 0.05f, 16);
            // Road wheels (dark rubber rims, steel hubs).
            float side = Mathf.Sign(x);
            for (int i = 0; i < roadWheels; i++)
            {
                float z = Mathf.Lerp(z0 + wheelR * 0.5f, z1 - wheelR * 0.5f, roadWheels == 1 ? 0.5f : i / (float)(roadWheels - 1));
                Vector3 c = new Vector3(x + side * (width * 0.5f + 0.02f), y, z);
                Cylinder(Slot.Dark, c - Vector3.right * 0.06f * side, c + Vector3.right * 0.06f * side, wheelR, 16);
                Cylinder(Slot.Metal, c + Vector3.right * 0.06f * side, c + Vector3.right * 0.1f * side, wheelR * 0.45f, 10);
            }
            // Sprocket (front), idler (rear), return rollers.
            Vector3 sp = new Vector3(x + side * (width * 0.5f + 0.02f), endR + 0.12f, z1 + 0.15f);
            Cylinder(Slot.Metal, sp - Vector3.right * 0.05f * side, sp + Vector3.right * 0.08f * side, endR * 0.75f, 12);
            Vector3 id = new Vector3(x + side * (width * 0.5f + 0.02f), endR, z0 - 0.15f);
            Cylinder(Slot.Metal, id - Vector3.right * 0.05f * side, id + Vector3.right * 0.08f * side, endR * 0.7f, 12);
            for (int i = 0; i < 3; i++)
            {
                Vector3 rr = new Vector3(x + side * (width * 0.5f), height - 0.16f, Mathf.Lerp(z0, z1, (i + 1) / 4f));
                Cylinder(Slot.Metal, rr - Vector3.right * 0.05f * side, rr + Vector3.right * 0.05f * side, 0.09f, 8);
            }
        }

        // ---------- output ----------

        /// <summary>One mesh per used slot (index = slot); empty slots are null.</summary>
        public Mesh[] Build(string name)
        {
            var meshes = new Mesh[Slots];
            for (int i = 0; i < Slots; i++)
            {
                var p = _parts[i];
                if (p.T.Count == 0) continue;
                var m = new Mesh { name = $"{name} {(Slot)i}", indexFormat = p.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(p.V);
                m.SetNormals(p.N);
                m.SetUVs(0, p.UV);
                m.SetTriangles(p.T, 0);
                m.RecalculateTangents();
                m.RecalculateBounds();
                meshes[i] = m;
            }
            return meshes;
        }

        // ---------- internals ----------

        // A flat convex polygon facing away from 'inside': planar UVs in metres along its dominant plane.
        private void Polygon(Slot slot, List<Vector3> pts, Vector3 inside)
        {
            if (pts.Count < 3) return;
            var part = _parts[(int)slot];
            Vector3 n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
            if (n.sqrMagnitude < 1e-10f && pts.Count > 3) n = Vector3.Cross(pts[2] - pts[0], pts[3] - pts[0]);
            n.Normalize();
            Vector3 centroid = Vector3.zero;
            foreach (var p in pts) centroid += p;
            centroid /= pts.Count;
            bool flip = Vector3.Dot(n, centroid - inside) < 0f;
            if (flip) n = -n;
            Vector3 an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            int start = part.V.Count;
            foreach (var p in pts)
            {
                part.V.Add(p);
                part.N.Add(n);
                Vector2 uv = an.x >= an.y && an.x >= an.z ? new Vector2(p.z, p.y) : an.y >= an.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                part.UV.Add(uv / Tile);
            }
            for (int i = 1; i < pts.Count - 1; i++)
            {
                if (flip) { part.T.Add(start); part.T.Add(start + i + 1); part.T.Add(start + i); }
                else { part.T.Add(start); part.T.Add(start + i); part.T.Add(start + i + 1); }
            }
        }

        // A triangle wound to face away from 'inside' (Unity: clockwise seen from the front).
        private static void Tri(Part part, int a, int b, int c, Vector3 inside)
        {
            Vector3 n = Vector3.Cross(part.V[b] - part.V[a], part.V[c] - part.V[a]);
            Vector3 mid = (part.V[a] + part.V[b] + part.V[c]) / 3f;
            Vector3 outward = part.N[a] + part.N[b] + part.N[c];
            if (Vector3.Dot(n, outward) < 0f) { part.T.Add(a); part.T.Add(c); part.T.Add(b); }
            else { part.T.Add(a); part.T.Add(b); part.T.Add(c); }
        }
    }
}
