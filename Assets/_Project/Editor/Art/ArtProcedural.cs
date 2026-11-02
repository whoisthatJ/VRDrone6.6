using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Original textures generated in the editor (no external source): macro-variation noise, carbon-fibre twill,
    /// safety stripes, grass blades, chalk, water ripples, a generic LiPo label and a start/finish checker.
    /// All tile seamlessly; names follow the import-rule suffixes (_Albedo, _Normal, _ORM, _Mask).
    /// </summary>
    public static class ArtProcedural
    {
        public const string Root = "Assets/_Project/Art/Materials/Procedural";
        public static string Dir(string name) => $"{Root}/{name}";
        public static string File(string name, string suffix, string ext = "png") => $"{Root}/{name}/{name}_{suffix}.{ext}";

        [MenuItem("Tools/Art/Generate Procedural Textures")]
        public static string GenerateAll()
        {
            MacroNoise();
            CarbonFiber();
            SafetyStripes();
            GrassBlades();
            Chalk();
            WaterRipples();
            LipoLabel();
            Checker();
            Decals();
            AssetDatabase.Refresh();
            return "procedural textures written to " + Root;
        }

        // ---------- noise helpers (tileable) ----------

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Smooth value noise on a lattice that wraps every <paramref name="period"/> cells.</summary>
        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            int Wrap(int v) => ((v % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Tileable fBm in [0,1]; u, v in [0,1).</summary>
        internal static float Fbm(float u, float v, int basePeriod, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int period = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * period, v * period, period, seed + o * 31) * amp;
                norm += amp;
                amp *= gain;
                period *= 2;
            }
            return sum / norm;
        }

        internal static Color32[] NormalFromHeight(float[] h, int w, int hgt, float strength)
        {
            var px = new Color32[w * hgt];
            for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                float l = h[y * w + (x - 1 + w) % w], r = h[y * w + (x + 1) % w];
                float d = h[((y - 1 + hgt) % hgt) * w + x], u = h[((y + 1) % hgt) * w + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                px[y * w + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255), (byte)((n.z * 0.5f + 0.5f) * 255), 255);
            }
            return px;
        }

        internal static void Save(string path, int w, int h, Color32[] px, bool alpha = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tex = new Texture2D(w, h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false, true);
            tex.SetPixels32(px);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        internal static Color32 C(float r, float g, float b, float a = 1f) => new Color32(B(r), B(g), B(b), B(a));

        // ---------- generators ----------

        /// <summary>Linear grey noise centred on 0.5 for URP's detail albedo (×2 overlay): large-scale tone variation.</summary>
        public static void MacroNoise()
        {
            const int n = 512;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float f = Fbm(x / (float)n, y / (float)n, 4, 5, 11, 0.55f);
                float v = Mathf.Clamp01(0.5f + (f - 0.5f) * 1.5f);
                px[y * n + x] = C(v, v, v);
            }
            Save($"{Root}/Macro/Macro_Noise_Mask.png", n, n, px);
        }

        /// <summary>2×2 twill carbon weave: 8×8 tows per tile, dark albedo, low roughness, fibre striations in the normal map.</summary>
        public static void CarbonFiber()
        {
            const int n = 512, tows = 8, cell = n / tows;
            var height = new float[n * n];
            var albedo = new Color32[n * n];
            var orm = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int cx = x / cell, cy = y / cell;
                bool horizontal = ((cx + cy) / 2) % 2 == 0;
                float across = horizontal ? (y % cell) / (float)cell : (x % cell) / (float)cell;
                float along = horizontal ? x / (float)n : y / (float)n;
                float bump = Mathf.Sin(across * Mathf.PI);
                float fibre = Fbm(horizontal ? along * 8f : across, horizontal ? across : along * 8f, 64, 2, 5);
                height[y * n + x] = bump * 0.8f + fibre * 0.2f;
                float sheen = 0.028f + 0.035f * bump * (0.6f + 0.4f * fibre) * (horizontal ? 1f : 0.7f);
                albedo[y * n + x] = C(sheen, sheen, sheen * 1.05f);
                float rough = 0.28f + 0.12f * (1f - bump);
                orm[y * n + x] = C(0.85f + 0.15f * bump, rough, 0f);
            }
            Save(File("CarbonFiber", "Albedo"), n, n, albedo);
            Save(File("CarbonFiber", "Normal"), n, n, NormalFromHeight(height, n, n, 2.5f));
            Save(File("CarbonFiber", "ORM"), n, n, orm);
        }

        /// <summary>45° yellow/black hazard stripes with light grime.</summary>
        public static void SafetyStripes()
        {
            const int n = 256;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool yellow = ((x + y) / 64) % 2 == 0;
                float grime = 0.8f + 0.2f * Fbm(x / (float)n, y / (float)n, 8, 4, 3);
                px[y * n + x] = yellow ? C(0.92f * grime, 0.70f * grime, 0.06f * grime) : C(0.035f * grime, 0.035f * grime, 0.035f * grime);
            }
            Save(File("SafetyStripes", "Albedo"), n, n, px);
        }

        /// <summary>Alpha-tested grass tuft for terrain details: 26 tapered, slightly bent blades.</summary>
        public static void GrassBlades()
        {
            const int w = 256, h = 256;
            var px = new Color32[w * h];
            var rng = new System.Random(7);
            for (int b = 0; b < 26; b++)
            {
                float x0 = 20f + (float)rng.NextDouble() * (w - 40);
                float top = h * (0.55f + 0.42f * (float)rng.NextDouble());
                float bend = ((float)rng.NextDouble() - 0.5f) * 60f;
                float width = 3.5f + 3.5f * (float)rng.NextDouble();
                float hue = (float)rng.NextDouble();
                for (int y = 0; y < (int)top; y++)
                {
                    float t = y / top;
                    float cx = x0 + bend * t * t;
                    float half = width * (1f - t * 0.92f);
                    for (int x = Mathf.Max(0, (int)(cx - half - 1)); x <= Mathf.Min(w - 1, (int)(cx + half + 1)); x++)
                    {
                        float cover = Mathf.Clamp01(half + 0.5f - Mathf.Abs(x - cx));
                        if (cover <= 0f) continue;
                        float g = Mathf.Lerp(0.42f, 0.78f, t) * (0.85f + 0.3f * hue);
                        var col = C(g * 0.66f, g, g * 0.32f, Mathf.Max(cover, px[y * w + x].a / 255f));
                        px[y * w + x] = col;
                    }
                }
            }
            Save(File("GrassBlades", "Albedo"), w, h, px, alpha: true);
        }

        /// <summary>Line-marking chalk for the field (terrain layer): off-white with powdery noise.</summary>
        public static void Chalk()
        {
            const int n = 256;
            var px = new Color32[n * n];
            var height = new float[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float f = Fbm(x / (float)n, y / (float)n, 16, 4, 21);
                float v = 0.78f + 0.14f * f;
                px[y * n + x] = C(v, v, v * 0.97f);
                height[y * n + x] = f;
            }
            Save(File("Chalk", "Albedo"), n, n, px);
            Save(File("Chalk", "Normal"), n, n, NormalFromHeight(height, n, n, 1.5f));
        }

        /// <summary>Soft wind ripples for still water.</summary>
        public static void WaterRipples()
        {
            const int n = 512;
            var height = new float[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float waves = Mathf.Sin((u * 6f + v * 2f) * Mathf.PI * 2f) * 0.25f + Mathf.Sin((u * -3f + v * 7f) * Mathf.PI * 2f) * 0.2f;
                height[y * n + x] = Fbm(u, v, 8, 4, 41) + waves * 0.15f;
            }
            Save(File("Water", "Normal"), n, n, NormalFromHeight(height, n, n, 3f));
        }

        /// <summary>Generic LiPo shrink-wrap: dark wrap, one coloured band, white label blocks (no brand).</summary>
        public static void LipoLabel()
        {
            const int w = 256, h = 128;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 c = C(0.05f, 0.05f, 0.06f);
                if (y > 52 && y < 76) c = C(0.95f, 0.62f, 0.05f);
                bool label = (x > 24 && x < 110 && y > 90 && y < 104) || (x > 24 && x < 70 && y > 20 && y < 30) || (x > 150 && x < 232 && y > 20 && y < 40);
                if (label) c = C(0.9f, 0.9f, 0.9f);
                px[y * w + x] = c;
            }
            Save(File("Lipo", "Albedo"), w, h, px);
        }

        /// <summary>Decal sheets (RGBA, alpha = coverage): oil stain, asphalt crack (+ normal), puddle mask, tyre marks.</summary>
        public static void Decals()
        {
            const int n = 512;
            var oil = new Color32[n * n];
            var puddle = new Color32[n * n];
            var tyre = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n - 0.5f, v = y / (float)n - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v) * 2f;
                float edge = Fbm(x / (float)n, y / (float)n, 4, 4, 61) * 0.6f;
                float a = Mathf.Clamp01((1f - r - edge * 0.7f + 0.25f) * 3f);
                float tint = Fbm(x / (float)n, y / (float)n, 16, 3, 63);
                oil[y * n + x] = C(0.02f + 0.03f * tint, 0.018f + 0.025f * tint, 0.015f + 0.02f * tint, a * (0.75f + 0.25f * tint));
                float pa = Mathf.Clamp01((0.85f - r - edge * 0.5f) * 5f);
                puddle[y * n + x] = C(0.03f, 0.035f, 0.04f, pa * 0.85f);
                // two worn tyre tracks running along V with a gentle S-curve
                float centre = Mathf.Sin(v * Mathf.PI * 1.5f) * 0.12f;
                float d1 = Mathf.Abs(u - centre - 0.17f), d2 = Mathf.Abs(u - centre + 0.17f);
                float track = Mathf.Clamp01(1f - Mathf.Min(d1, d2) / 0.06f) * Mathf.Clamp01(1.2f - Mathf.Abs(v) * 2.2f);
                tyre[y * n + x] = C(0.015f, 0.015f, 0.015f, track * (0.35f + 0.4f * tint));
            }
            Save(File("DecalOil", "Albedo"), n, n, oil, alpha: true);
            Save(File("DecalPuddle", "Albedo"), n, n, puddle, alpha: true);
            Save(File("DecalTyre", "Albedo"), n, n, tyre, alpha: true);

            // Branching crack: random walks from the centre, dark core, slight depth in the normal map.
            var crackA = new float[n * n];
            var rng = new System.Random(19);
            void Walk(float x, float y, float dir, int steps, float width)
            {
                for (int s = 0; s < steps; s++)
                {
                    dir += ((float)rng.NextDouble() - 0.5f) * 0.6f;
                    x += Mathf.Cos(dir) * 2f;
                    y += Mathf.Sin(dir) * 2f;
                    int w = Mathf.Max(1, Mathf.RoundToInt(width));
                    for (int dy = -w; dy <= w; dy++)
                    for (int dx = -w; dx <= w; dx++)
                    {
                        int xx = (int)x + dx, yy = (int)y + dy;
                        if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                        float f = 1f - Mathf.Sqrt(dx * dx + dy * dy) / (w + 1f);
                        crackA[yy * n + xx] = Mathf.Max(crackA[yy * n + xx], f);
                    }
                    width = Mathf.Max(0.6f, width * 0.995f);
                    if (rng.NextDouble() < 0.025 && steps - s > 20) Walk(x, y, dir + ((float)rng.NextDouble() - 0.5f) * 2f, (steps - s) / 2, width * 0.7f);
                }
            }
            for (int k = 0; k < 4; k++) Walk(n / 2f, n / 2f, k * Mathf.PI / 2f + 0.4f, 110, 2.6f);
            var crack = new Color32[n * n];
            var height = new float[n * n];
            for (int i = 0; i < crack.Length; i++)
            {
                crack[i] = C(0.03f, 0.03f, 0.03f, Mathf.Clamp01(crackA[i] * 1.6f));
                height[i] = -crackA[i];
            }
            Save(File("DecalCrack", "Albedo"), n, n, crack, alpha: true);
            Save(File("DecalCrack", "Normal"), n, n, NormalFromHeight(height, n, n, 3f));
        }

        /// <summary>Black and white checker for start/finish banners and the timing arch.</summary>
        public static void Checker()
        {
            const int n = 256;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool white = ((x / 32) + (y / 32)) % 2 == 0;
                px[y * n + x] = white ? C(0.85f, 0.85f, 0.85f) : C(0.03f, 0.03f, 0.03f);
            }
            Save(File("Checker", "Albedo"), n, n, px);
        }
    }
}
