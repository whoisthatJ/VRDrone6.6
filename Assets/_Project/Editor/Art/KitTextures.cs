using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static Ushqyr.EditorTools.Art.ArtProcedural;

namespace Ushqyr.EditorTools.Art
{
    /// <summary>
    /// T2 obstacle-kit textures, all procedural (our own, no licence questions), written next to the other procedural
    /// sets so <see cref="MaterialLibrary"/> picks them up: padded fabric with stitched seams (normal map), safety net
    /// (alpha), USHQYR feather-flag print, landing pad, numbered start grid and chevron turn sign.
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
            LedBanner();
            CuttingMat();
            Pegboard();
            ExpoCurtainWall();
            ExpoSphereGlass();
            ExpoRoof();
            ExpoApartment();
            ExpoLeaves();
            SphereFrit();
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

        /// <summary>Feather flag print: navy with an orange edge band, "USHQYR" running up the flag and a chevron emblem on top.</summary>
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
            DrawText(px, w, h, "USHQYR", x0: 0.3f, y0: 0.06f, cell: 0.06f, vertical: true, color: Color.white);
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

        /// <summary>
        /// Stadium perimeter LED board content (T3): "USHQYR" in white between orange chevrons on a dark navy
        /// screen with a fine pixel grid. U runs along the board, repeated and scrolled by LedBoardScroller.
        /// </summary>
        public static void LedBanner()
        {
            const int w = 1024, h = 128;
            var px = new Color32[w * h];
            var navy = new Color(0.02f, 0.04f, 0.12f);
            var orange = new Color(1f, 0.38f, 0.04f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                var c = navy;
                // Chevrons at both ends, pointing in the scroll direction.
                foreach (float tip in new[] { 0.09f, 0.15f, 0.87f, 0.93f })
                {
                    float line = tip - 0.035f * Mathf.Abs(v - 0.5f) * 2f;
                    if (u <= line && u >= line - 0.022f && Mathf.Abs(v - 0.5f) < 0.38f) c = orange;
                }
                // Orange rules along the top and bottom edges.
                if (v < 0.06f || v > 0.94f) c = orange * 0.8f;
                px[y * w + x] = C(c.r, c.g, c.b);
            }
            DrawText(px, w, h, "USHQYR", x0: 0.255f, y0: 0.12f, cell: 0.0137f, vertical: false, color: Color.white);
            // LED pixel grid: dark gaps every 4 px.
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x % 4 == 3 || y % 4 == 3)
                {
                    var p = px[y * w + x];
                    px[y * w + x] = new Color32((byte)(p.r * 0.35f), (byte)(p.g * 0.35f), (byte)(p.b * 0.35f), 255);
                }
            Save(File("LedBanner", "Albedo"), w, h, px);
        }

        /// <summary>
        /// Garage workbench mat (T7): blue self-healing cutting mat, one texture = 30 cm: faint 1 cm grid, brighter 5 cm
        /// lines, 45° guide lines and knife scuffs.
        /// </summary>
        public static void CuttingMat()
        {
            const int n = 1024;
            var px = new Color32[n * n];
            var baseC = new Color(0.07f, 0.42f, 0.78f);
            float cm = n / 30f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float noise = Fbm(u, v, 16, 4, 77);
                var c = baseC * (0.92f + noise * 0.14f);
                float fx = Mathf.Repeat(x / cm, 1f), fy = Mathf.Repeat(y / cm, 1f);
                bool minor = Mathf.Min(fx, 1f - fx) * cm < 0.9f || Mathf.Min(fy, 1f - fy) * cm < 0.9f;
                float f5x = Mathf.Repeat(x / (cm * 5f), 1f), f5y = Mathf.Repeat(y / (cm * 5f), 1f);
                bool major = Mathf.Min(f5x, 1f - f5x) * cm * 5f < 1.6f || Mathf.Min(f5y, 1f - f5y) * cm * 5f < 1.6f;
                float d = Mathf.Repeat((x - y) / (cm * 10f), 1f);
                bool diag = Mathf.Min(d, 1f - d) * cm * 10f < 1.0f;
                if (minor) c = Color.Lerp(c, new Color(0.55f, 0.8f, 1f), 0.35f);
                if (diag) c = Color.Lerp(c, new Color(0.6f, 0.85f, 1f), 0.3f);
                if (major) c = Color.Lerp(c, new Color(0.85f, 0.95f, 1f), 0.8f);
                // Knife scuffs: short bright scratches.
                float s = Fbm(u * 3f, v * 0.2f, 64, 2, 9);
                if (s > 0.72f) c = Color.Lerp(c, Color.white, (s - 0.72f) * 1.2f);
                px[y * n + x] = C(c.r, c.g, c.b);
            }
            Save(File("CuttingMat", "Albedo"), n, n, px);
        }

        /// <summary>Hardboard pegboard: tan board with dark 6 mm holes on a 25 mm grid; one texture = 20 cm.</summary>
        public static void Pegboard()
        {
            const int n = 512;
            var px = new Color32[n * n];
            float pitch = n / 8f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float grain = Fbm(u * 6f, v, 32, 3, 13);
                var c = new Color(0.62f, 0.47f, 0.3f) * (0.88f + grain * 0.2f);
                float dx = Mathf.Repeat(x, pitch) - pitch * 0.5f, dy = Mathf.Repeat(y, pitch) - pitch * 0.5f;
                if (dx * dx + dy * dy < (pitch * 0.12f) * (pitch * 0.12f)) c = new Color(0.04f, 0.035f, 0.03f);
                px[y * n + x] = C(c.r, c.g, c.b);
            }
            Save(File("Pegboard", "Albedo"), n, n, px);
        }

        // ---------- Expo (Astana EXPO 2017 site) ----------

        /// <summary>
        /// Curtain wall for the Expo pavilions, one texture = 6 m wide × 4.5 m (one floor): four tall blue-grey glass
        /// panels with light aluminium mullions and a darker spandrel band at the slab.
        /// </summary>
        public static void ExpoCurtainWall()
        {
            const int w = 512, h = 384;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                // Sky reflection gradient in each pane (brighter at the top), with slight per-pane tint variation.
                int pane = (int)(u * 4f);
                float tint = 0.92f + 0.08f * Mathf.Sin(pane * 2.7f + 1.3f);
                var c = Color.Lerp(new Color(0.16f, 0.26f, 0.33f), new Color(0.42f, 0.55f, 0.64f), v * 0.85f) * tint;
                if (v < 0.14f) c = new Color(0.2f, 0.23f, 0.26f);                       // spandrel at the floor slab
                float mu = Mathf.Repeat(u * 4f, 1f);
                if (mu < 0.02f || mu > 0.98f || v < 0.012f || Mathf.Abs(v - 0.14f) < 0.01f || v > 0.99f)
                    c = new Color(0.78f, 0.8f, 0.82f);                                    // mullions and transoms
                px[y * w + x] = C(c.r, c.g, c.b);
            }
            Save(File("ExpoCurtainWall", "Albedo"), w, h, px);
        }

        /// <summary>
        /// Nur Alem sphere glazing, one texture = 4 m: diamond-shaped glass panels in a fine silver grid over a deep
        /// blue reflection, so the sphere reads as a faceted glass ball from the air.
        /// </summary>
        public static void ExpoSphereGlass()
        {
            const int n = 512;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float a = Mathf.Repeat(u + v, 0.5f) / 0.5f, b = Mathf.Repeat(u - v, 0.5f) / 0.5f;
                float edge = Mathf.Min(Mathf.Min(a, 1f - a), Mathf.Min(b, 1f - b));
                float shade = 0.9f + 0.1f * Mathf.Sin((Mathf.Floor((u + v) * 2f) * 3.1f + Mathf.Floor((u - v) * 2f + 4f) * 1.7f));
                var c = new Color(0.22f, 0.34f, 0.44f) * shade;
                if (edge < 0.02f) c = new Color(0.72f, 0.76f, 0.8f);
                px[y * n + x] = C(c.r, c.g, c.b);
            }
            Save(File("ExpoSphereGlass", "Albedo"), n, n, px);
        }

        /// <summary>
        /// Standing-seam metal roof of the Expo pavilions, one texture = 4 m: silver panels 0.5 m wide with raised
        /// seams and faint weathering.
        /// </summary>
        public static void ExpoRoof()
        {
            const int n = 256;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float grime = Fbm(u * 2f, v * 2f, 16, 3, 41);
                var c = new Color(0.72f, 0.74f, 0.77f) * (0.9f + grime * 0.12f);
                float s = Mathf.Repeat(u * 8f, 1f);
                if (s < 0.06f) c *= 1.12f;
                else if (s < 0.1f) c *= 0.78f;
                px[y * n + x] = C(c.r, c.g, c.b);
            }
            Save(File("ExpoRoof", "Albedo"), n, n, px);
        }

        /// <summary>
        /// Astana apartment block facade, one texture = 7.2 m wide × 3 m (one floor): warm render with two windows,
        /// one with a glazed loggia. Tinted per building.
        /// </summary>
        public static void ExpoApartment()
        {
            const int w = 512, h = 256;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                var c = new Color(0.9f, 0.86f, 0.78f) * (0.94f + Fbm(u * 3f, v * 3f, 16, 2, 77) * 0.08f);
                bool win1 = u > 0.08f && u < 0.4f && v > 0.25f && v < 0.85f;
                bool win2 = u > 0.56f && u < 0.92f && v > 0.18f && v < 0.85f;
                if (win1 || win2)
                {
                    c = Color.Lerp(new Color(0.12f, 0.16f, 0.2f), new Color(0.35f, 0.45f, 0.52f), v);
                    float wu = win1 ? (u - 0.08f) / 0.32f : (u - 0.56f) / 0.36f;
                    if (Mathf.Abs(wu - 0.5f) < 0.02f) c = new Color(0.85f, 0.85f, 0.83f);
                }
                if (win2 && v < 0.3f) c = new Color(0.82f, 0.8f, 0.76f);                 // loggia parapet
                if (v < 0.04f) c = new Color(0.7f, 0.66f, 0.6f);                          // floor band
                px[y * w + x] = C(c.r, c.g, c.b);
            }
            Save(File("ExpoApartment", "Albedo"), w, h, px);
        }

        /// <summary>
        /// Leaf cluster for the procedural trees (alpha-cut, one texture per card): ~260 overlapping leaves in a rough
        /// disc, each an ellipse with a midrib, in several greens, lighter towards the top (sky light), with gaps.
        /// </summary>
        public static void ExpoLeaves()
        {
            const int n = 512;
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(52, 74, 30, 0);
            var rng = new System.Random(77);
            for (int k = 0; k < 260; k++)
            {
                // Leaf centre inside a disc, denser towards the middle.
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, rad = Mathf.Sqrt((float)rng.NextDouble()) * 0.42f;
                float cx = 0.5f + Mathf.Cos(ang) * rad, cy = 0.5f + Mathf.Sin(ang) * rad;
                float len = 0.035f + (float)rng.NextDouble() * 0.03f, wid = len * 0.45f;
                float rot = (float)rng.NextDouble() * Mathf.PI;
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                float shade = 0.75f + (float)rng.NextDouble() * 0.35f + (cy - 0.5f) * 0.5f;
                var baseCol = new Color(0.2f + (float)rng.NextDouble() * 0.08f, 0.36f + (float)rng.NextDouble() * 0.12f, 0.1f) * shade;
                int x0 = Mathf.Max(0, (int)((cx - len) * n)), x1 = Mathf.Min(n - 1, (int)((cx + len) * n));
                int y0 = Mathf.Max(0, (int)((cy - len) * n)), y1 = Mathf.Min(n - 1, (int)((cy + len) * n));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f) / n - cx, dy = (y + 0.5f) / n - cy;
                    float u = dx * cs + dy * sn, v = -dx * sn + dy * cs;
                    float e = (u * u) / (len * len) + (v * v) / (wid * wid);
                    if (e > 1f) continue;
                    var c = baseCol * (0.85f + 0.15f * (1f - e));
                    if (Mathf.Abs(v) < wid * 0.08f) c *= 1.25f;            // midrib
                    px[y * n + x] = C(c.r, c.g, c.b, 1f);
                }
            }
            Save(File("ExpoLeaves", "Albedo"), n, n, px, alpha: true);
        }

        /// <summary>
        /// Sphere District sphere glazing (fritted hemisphere), one texture = 3 m: 2 × 2 panels of low-iron glass with
        /// a ceramic frit dot screen whose density changes from panel to panel, thin silver joints, sky gradient.
        /// </summary>
        public static void SphereFrit()
        {
            const int n = 512;
            var px = new Color32[n * n];
            var rng = new System.Random(2017);
            var density = new float[4];
            for (int i = 0; i < 4; i++) density[i] = 0.25f + (float)rng.NextDouble() * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                int panel = (u < 0.5f ? 0 : 1) + (v < 0.5f ? 0 : 2);
                var c = Color.Lerp(new Color(0.1f, 0.2f, 0.24f), new Color(0.26f, 0.4f, 0.44f), Mathf.Repeat(v * 2f, 1f) * 0.6f + 0.2f);
                // Frit dots on a 2 cm grid (16 px), radius set by the panel's density.
                float gx = Mathf.Repeat(x, 16f) - 8f, gy = Mathf.Repeat(y, 16f) - 8f;
                float rad = 7f * Mathf.Sqrt(density[panel]);
                if (gx * gx + gy * gy < rad * rad) c = Color.Lerp(c, new Color(0.62f, 0.7f, 0.72f), 0.3f);
                float pu = Mathf.Repeat(u * 2f, 1f), pv = Mathf.Repeat(v * 2f, 1f);
                if (pu < 0.012f || pu > 0.988f || pv < 0.012f || pv > 0.988f) c = new Color(0.36f, 0.42f, 0.45f);
                px[y * n + x] = C(c.r, c.g, c.b);
            }
            Save(File("SphereFrit", "Albedo"), n, n, px);
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
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
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
