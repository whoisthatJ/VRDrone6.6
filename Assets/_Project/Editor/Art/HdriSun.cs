using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Reads a Radiance .hdr (RGBE, RLE) equirectangular image and finds the sun (centroid of the brightest
    /// pixels), its colour, and the average horizon colour (for fog). Directions follow Unity's lat-long cubemap
    /// convention (the same as Skybox/Panoramic): u = 0.5 − atan2(z, x) / 2π, v = 1 − acos(y) / π.
    /// </summary>
    public static class HdriSun
    {
        public struct Result
        {
            public Vector3 SunDirection;   // towards the sun
            public Color SunColor;         // normalised (max channel 1)
            public float SunElevation;     // degrees
            public Color HorizonColor;     // linear, average around the horizon
            public Color ZenithColor;
        }

        public static Result Analyze(string hdrPath)
        {
            float[] rgb = Load(hdrPath, out int w, out int h);
            // Brightest 0.02 % of the upper hemisphere.
            int count = 0;
            var lum = new float[w * h / 2];
            for (int y = 0; y < h / 2; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 3;
                    lum[count++] = 0.2126f * rgb[i] + 0.7152f * rgb[i + 1] + 0.0722f * rgb[i + 2];
                }
            var sorted = (float[])lum.Clone();
            Array.Sort(sorted);
            float threshold = sorted[Mathf.Clamp((int)(sorted.Length * 0.9998f), 0, sorted.Length - 1)];

            Vector3 dirSum = Vector3.zero;
            Vector3 colSum = Vector3.zero;
            for (int y = 0; y < h / 2; y++)
                for (int x = 0; x < w; x++)
                {
                    float l = lum[y * w + x];
                    if (l < threshold) continue;
                    int i = (y * w + x) * 3;
                    dirSum += Direction((x + 0.5f) / w, 1f - (y + 0.5f) / h) * l;
                    colSum += new Vector3(rgb[i], rgb[i + 1], rgb[i + 2]);
                }
            var result = new Result
            {
                SunDirection = dirSum.normalized,
                SunColor = Normalised(colSum),
                HorizonColor = Band(rgb, w, h, 0.47f, 0.53f),
                ZenithColor = Band(rgb, w, h, 0.02f, 0.12f),
            };
            result.SunElevation = Mathf.Asin(Mathf.Clamp(result.SunDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
            return result;
        }

        /// <summary>Unity lat-long convention: u, v in [0,1] (v = 1 at the top) → world direction.</summary>
        public static Vector3 Direction(float u, float v)
        {
            float latitude = (1f - v) * Mathf.PI;
            float longitude = (0.5f - u) * 2f * Mathf.PI;
            return new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
        }

        private static Color Normalised(Vector3 c)
        {
            float m = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
            return m > 0f ? new Color(c.x / m, c.y / m, c.z / m) : Color.white;
        }

        private static Color Band(float[] rgb, int w, int h, float v0, float v1)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int y = (int)(v0 * h); y < (int)(v1 * h); y++)
                for (int x = 0; x < w; x += 4)
                {
                    int i = (y * w + x) * 3;
                    sum += new Vector3(rgb[i], rgb[i + 1], rgb[i + 2]);
                    n++;
                }
            sum /= Mathf.Max(1, n);
            return new Color(sum.x, sum.y, sum.z);
        }

        /// <summary>Decodes a Radiance RGBE file to linear float RGB, rows top to bottom.</summary>
        public static float[] Load(string path, out int width, out int height)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            string line;
            do line = ReadLine(br); while (line.Length > 0);
            string res = ReadLine(br); // "-Y h +X w"
            var parts = res.Split(' ');
            height = int.Parse(parts[1]);
            width = int.Parse(parts[3]);
            var outRgb = new float[width * height * 3];
            var scan = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                ReadScanline(br, scan, width);
                for (int x = 0; x < width; x++)
                {
                    int e = scan[x * 4 + 3];
                    float f = e == 0 ? 0f : (float)Math.Pow(2, e - 136);
                    int o = (y * width + x) * 3;
                    outRgb[o] = scan[x * 4] * f;
                    outRgb[o + 1] = scan[x * 4 + 1] * f;
                    outRgb[o + 2] = scan[x * 4 + 2] * f;
                }
            }
            return outRgb;
        }

        private static string ReadLine(BinaryReader br)
        {
            var sb = new StringBuilder();
            while (true)
            {
                byte b = br.ReadByte();
                if (b == '\n') return sb.ToString();
                sb.Append((char)b);
            }
        }

        private static void ReadScanline(BinaryReader br, byte[] scan, int width)
        {
            byte r = br.ReadByte(), g = br.ReadByte(), b = br.ReadByte(), e = br.ReadByte();
            if (r != 2 || g != 2 || (b & 0x80) != 0 || width < 8 || width > 0x7fff)
            {
                // Flat (uncompressed) scanline.
                scan[0] = r; scan[1] = g; scan[2] = b; scan[3] = e;
                br.Read(scan, 4, width * 4 - 4);
                return;
            }
            // New-style RLE: four channel planes.
            var plane = new byte[width];
            for (int c = 0; c < 4; c++)
            {
                int x = 0;
                while (x < width)
                {
                    int count = br.ReadByte();
                    if (count > 128)
                    {
                        count -= 128;
                        byte value = br.ReadByte();
                        for (int k = 0; k < count; k++) plane[x++] = value;
                    }
                    else
                    {
                        for (int k = 0; k < count; k++) plane[x++] = br.ReadByte();
                    }
                }
                for (int i = 0; i < width; i++) scan[i * 4 + c] = plane[i];
            }
        }
    }
}
