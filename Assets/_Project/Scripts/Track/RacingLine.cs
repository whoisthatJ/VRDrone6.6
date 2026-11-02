using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.Track
{
    /// <summary>
    /// Trainer racing line (T5): a thin glowing ribbon just under the flight line, coloured by recommended speed: green
    /// where you can fly fast, yellow where you should brake for what comes next, red in the slow corners. The speed
    /// comes from the route's curvature (lateral grip) plus a backward braking pass; the colours are a generated strip
    /// texture sampled along the ribbon (u = distance / length), so the LED shader draws it without vertex colours.
    /// </summary>
    public static class RacingLine
    {
        /// <summary>Top speed on straights (m/s).</summary>
        public const float MaxSpeed = 20f;
        /// <summary>Lateral acceleration a 5" quad holds in a turn (m/s²).</summary>
        public const float LateralAccel = 14f;
        /// <summary>Braking deceleration used for the look-ahead (m/s²).</summary>
        public const float Braking = 9f;
        public const float Step = 1f;
        public const float Width = 0.12f;
        /// <summary>Below the flight line, so it reads as a guide without covering the gates.</summary>
        public const float Drop = 0.45f;

        public static readonly Color Fast = new Color(0.15f, 1f, 0.3f);
        public static readonly Color Brake = new Color(1f, 0.8f, 0.1f);
        public static readonly Color Slow = new Color(1f, 0.12f, 0.08f);

        /// <summary>Curvature-limited speed at each sample (no braking).</summary>
        public static float[] CornerSpeeds(TrackRoute route, float step = Step)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(route.Length / step));
            var v = new float[n];
            const float span = 3f;
            for (int i = 0; i < n; i++)
            {
                float d = i * step;
                Vector3 a = route.Sample(d - span).Forward, b = route.Sample(d + span).Forward;
                float k = Vector3.Angle(a, b) * Mathf.Deg2Rad / (2f * span); // 1 / radius
                v[i] = k > 1e-4f ? Mathf.Min(MaxSpeed, Mathf.Sqrt(LateralAccel / k)) : MaxSpeed;
            }
            return v;
        }

        /// <summary>Recommended speed: corner speeds with a backward braking pass (closed loop: run it twice).</summary>
        public static float[] RecommendedSpeeds(float[] corner, float step = Step)
        {
            var v = (float[])corner.Clone();
            int n = v.Length;
            for (int pass = 0; pass < 2; pass++)
                for (int i = n - 1; i >= 0; i--)
                {
                    float ahead = v[(i + 1) % n];
                    v[i] = Mathf.Min(v[i], Mathf.Sqrt(ahead * ahead + 2f * Braking * step));
                }
            return v;
        }

        /// <summary>Colour for a sample: red in slow corners, yellow while braking for one, green otherwise.</summary>
        public static Color ColorFor(float recommended, float corner)
        {
            float slow = Mathf.InverseLerp(MaxSpeed * 0.7f, MaxSpeed * 0.5f, recommended);
            float braking = Mathf.Clamp01((corner - recommended) / 3f);
            Color c = Color.Lerp(Fast, Brake, braking);
            return Color.Lerp(c, Slow, slow);
        }

        public static Color[] Colors(TrackRoute route, float step = Step)
        {
            var corner = CornerSpeeds(route, step);
            var rec = RecommendedSpeeds(corner, step);
            var c = new Color[corner.Length];
            for (int i = 0; i < c.Length; i++) c[i] = ColorFor(rec[i], corner[i]);
            return c;
        }

        public static GameObject Build(TrackRoute route, RouteData data, Material material, Transform parent)
        {
            var go = new GameObject("Racing Line");
            go.transform.SetParent(parent, false);
            var colors = Colors(route);

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            int n = colors.Length;
            for (int i = 0; i <= n; i++)
            {
                float d = Mathf.Min(i * Step, route.Length);
                var p = route.Sample(d);
                Vector3 c = p.Position - Vector3.up * Drop;
                Vector3 side = p.Right * (Width * 0.5f);
                float u = (i + 0.5f) / (n + 1f);
                verts.Add(c - side);
                verts.Add(c + side);
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                if (i > 0)
                {
                    int b = verts.Count - 4;
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { name = "Racing Line", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            // One texel per sample (wrapping to rows past 4096 would need a 2D lookup; tracks are far shorter).
            int w = Mathf.Min(n + 1, 4096);
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { name = "Racing Line Colours", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w];
            for (int i = 0; i < w; i++) px[i] = colors[Mathf.Min(i, n - 1)];
            tex.SetPixels(px);
            tex.Apply(false, true);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = material != null ? new Material(material) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.name = "Racing Line (instance)";
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
    }
}
