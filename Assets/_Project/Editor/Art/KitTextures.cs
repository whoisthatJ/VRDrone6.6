using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static GateRush.EditorTools.Art.ArtProcedural;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// T2 obstacle-kit textures, all procedural (our own, no licence questions), written next to the other procedural
    /// sets so <see cref="MaterialLibrary"/> picks them up: padded fabric with stitched seams (normal map), safety net
    /// (alpha), GATERUSH feather-flag print, landing pad, numbered start grid and chevron turn sign.
    /// </summary>
    public static class KitTextures
    {
        public static string GenerateAll()
        {
            PadFabric();
            SafetyNet();
            BrandFlag();
            LandingPad();
            StartGrid();
            ChevronSign();
            AssetDatabase.Refresh();
            return "kit textures written";
        }

        /// <summary>Woven foam-cover fabric (white, tinted per material), a lengthwise seam with two stitch rows per 0.25 m tile.</summary>
        public static void PadFabric()
        {
            const int n = 512;
            var height = new float[n * n];
            var albedo = new Color32[n * n];
            var orm = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                // Basket weave: two thread directions alternating in small cells.
                float wu = Mathf.Sin(u * Mathf.PI * 2f * 96f), wv = Mathf.Sin(v * Mathf.PI * 2f * 96f);
                bool cell = ((int)(u * 48f) + (int)(v * 48f)) % 2 == 0;
                float weave = cell ? wu * 0.5f + 0.5f : wv * 0.5f + 0.5f;
                float noise = Fbm(u, v, 8, 4, 11);
                // Seam groove at u = 0 (wraps) and stitch dashes either side of it.
                float du = Mathf.Min(u, 1f - u);
                float seam = Mathf.Clamp01(1f - du / 0.012f);
                bool stitchRow = Mathf.Abs(du - 0.03f) < 0.004f;
                bool stitch = stitchRow && Mathf.Repeat(v * 40f, 1f) < 0.55f;
                height[y * n + x] = 0.55f + weave * 0.12f + noise * 0.1f - seam * 0.5f + (stitch ? 0.18f : 0f);
                float shade = 0.82f + weave * 0.08f + (noise - 0.5f) * 0.1f - seam * 0.25f;
                if (stitch) shade *= 0.8f;
                albedo[y * n + x] = C(shade, shade, shade);
                orm[y * n + x] = C(1f - seam * 0.5f, 0.72f + weave * 0.1f, 0f);
            }
            Save(File("PadFabric", "Albedo"), n, n, albedo);
            Save(File("PadFabric", "Normal"), n, n, NormalFromHeight(height, n, n, 3f));
            Save(File("PadFabric", "ORM"), n, n, orm);
        }

        /// <summary>Knotted black safety net, 10 cm diamond mesh (two mesh cells per tile), alpha-tested.</summary>
        public static void SafetyNet()
        {
            const int n = 256;
            var albedo = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f, v = (y + 0.5f) / n * 2f;
                // Diamonds: distance to the two diagonal line families.
                float a = Mathf.Abs(Mathf.Repeat(u + v, 1f) - 0.5f), b = Mathf.Abs(Mathf.Repeat(u - v, 1f) - 0.5f);
                float d = Mathf.Min(0.5f - a, 0.5f - b);
                bool knot = (0.5f - a) < 0.07f && (0.5f - b) < 0.07f;
                bool cord = d < 0.06f || knot;
                float g = 0.05f + Fbm(u * 0.5f, v * 0.5f, 8, 2, 3) * 0.05f;
                albedo[y * n + x] = C(g, g, g * 1.1f, cord ? 1f : 0f);
            }
            string path = File("SafetyNet", "Albedo");
            Save(path, n, n, albedo, alpha: true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            // Keep the cords visible at a distance: mip levels preserve the alpha-test coverage.
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.5f;
                ti.SaveAndReimport();
            }
        }

        /// <summary>Feather flag print: navy with an orange edge band, "GATERUSH" running up the flag and a chevron emblem on top.</summary>
        public static void BrandFlag()
        {
            const int w = 256, h = 1024;
            var px = new Color32[w * h];
            var navy = new Color(0.04f, 0.07f, 0.16f);
            var orange = new Color(1f, 0.42f, 0.05f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                var c = u < 0.14f ? orange : navy;
                // Chevron emblem near the top.
                float cx = Mathf.Abs(u - 0.55f) * 2f;
                float line = 0.93f - 0.12f * cx;
                if (v <= line && v >= line - 0.035f && cx < 0.7f) c = orange;
                px[y * w + x] = C(c.r, c.g, c.b);
            }
            // Letters run bottom → top, rotated 90° (read with your head tilted left, like most feather flags).
            DrawText(px, w, h, "GATERUSH", x0: 0.3f, y0: 0.06f, cell: 0.06f, vertical: true, color: Color.white);
            Save(File("BrandFlag", "Albedo"), w, h, px);
        }

        /// <summary>Dark rubber landing pad with a white ring and centre dot (UV 0..1 across the disc).</summary>
        public static void LandingPad()
        {
            const int n = 512;
            var albedo = new Color32[n * n];
            var orm = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v) * 2f;
                float grit = Fbm((x + 0.5f) / n, (y + 0.5f) / n, 32, 3, 7);
                bool white = (r > 0.78f && r < 0.86f) || r < 0.12f;
                float g = white ? 0.86f : 0.07f + grit * 0.05f;
                albedo[y * n + x] = C(g, g, g);
                orm[y * n + x] = C(1f, white ? 0.55f : 0.85f, 0f);
            }
            Save(File("LandingPad", "Albedo"), n, n, albedo);
            Save(File("LandingPad", "ORM"), n, n, orm);
        }

        /// <summary>Start mat: light grey grid, four numbered slots and green chevrons pointing along +V (UV 0..1 over the mat).</summary>
        public static void StartGrid()
        {
            const int w = 1024, h = 512;
            var px = new Color32[w * h];
            var green = new Color(0.08f, 0.62f, 0.22f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                var c = new Color(0.88f, 0.88f, 0.86f);
                if (Mathf.Repeat(u * 16f, 1f) < 0.02f || Mathf.Repeat(v * 8f, 1f) < 0.02f) c = new Color(0.76f, 0.76f, 0.74f);
                // Four slots side by side, each with a green outline and two chevrons.
                float su = Mathf.Repeat(u * 4f, 1f);
                if (su < 0.05f || su > 0.95f || v < 0.06f || v > 0.94f) c = green;
                float cx = Mathf.Abs(su - 0.5f) * 2f;
                foreach (float tip in new[] { 0.62f, 0.45f })
                {
                    float line = tip - 0.18f * cx;
                    if (v <= line && v >= line - 0.05f && cx < 0.55f) c = green;
                }
                px[y * w + x] = C(c.r, c.g, c.b);
            }
            for (int s = 0; s < 4; s++)
                DrawText(px, w, h, (s + 1).ToString(), x0: s * 0.25f + 0.11f, y0: 0.74f, cell: 0.012f, vertical: false, color: new Color(0.1f, 0.1f, 0.12f));
            Save(File("StartGrid", "Albedo"), w, h, px);
        }

        /// <summary>Turn sign: white board, three black chevrons pointing right (+U).</summary>
        public static void ChevronSign()
        {
            const int w = 512, h = 256;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                bool black = u < 0.02f || u > 0.98f || v < 0.04f || v > 0.96f;
                float cy = Mathf.Abs(v - 0.5f) * 2f;
                foreach (float tip in new[] { 0.36f, 0.6f, 0.84f })
                {
                    float line = tip - 0.16f * cy;
                    if (u <= line && u >= line - 0.08f && cy < 0.8f) black = true;
                }
                float g = black ? 0.04f : 0.9f;
                px[y * w + x] = C(g, g, g);
            }
            Save(File("ChevronSign", "Albedo"), w, h, px);
        }

        // ---------- 5×7 bitmap font for the brand and slot numbers ----------

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###." },
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
        };

        /// <summary>
        /// Draws <paramref name="text"/> with the 5×7 font. Positions and <paramref name="cell"/> (one font pixel) are in
        /// UV units of the texture width; vertical text runs bottom → top, letters rotated 90° anticlockwise.
        /// </summary>
        private static void DrawText(Color32[] px, int w, int h, string text, float x0, float y0, float cell, bool vertical, Color color)
        {
            var c32 = C(color.r, color.g, color.b);
            int cellPx = Mathf.Max(1, Mathf.RoundToInt(cell * w));
            int ox = Mathf.RoundToInt(x0 * w), oy = Mathf.RoundToInt(y0 * h);
            for (int i = 0; i < text.Length; i++)
            {
                if (!Glyphs.TryGetValue(text[i], out var rows)) continue;
                for (int gy = 0; gy < 7; gy++)
                for (int gx = 0; gx < 5; gx++)
                {
                    if (rows[gy][gx] != '#') continue;
                    for (int py = 0; py < cellPx; py++)
                    for (int pxx = 0; pxx < cellPx; pxx++)
                    {
                        int x, y;
                        if (vertical)
                        {
                            // Glyph row 0 (top) faces left; glyph column advances upwards.
                            x = ox + gy * cellPx + pxx;
                            y = oy + (i * 6 + gx) * cellPx + py;
                        }
                        else
                        {
                            x = ox + (i * 6 + gx) * cellPx + pxx;
                            y = oy + (6 - gy) * cellPx + py;
                        }
                        if (x >= 0 && x < w && y >= 0 && y < h) px[y * w + x] = c32;
                    }
                }
            }
        }
    }
}
