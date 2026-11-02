using System.IO;
using GateRush.Track;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Materials and procedural textures for generated spline-track geometry (lane LEDs, hardware, chevron boards,
    /// supports, wear marks, start pad), assigned to <see cref="TrackPrefabs.RouteKit"/>. LEDs are HDR emissive (they
    /// glow through bloom; no lights). Textures are our own, written as PNGs to Art/Route.
    /// </summary>
    public static class RouteKitBuilder
    {
        public const string Dir = "Assets/_Project/Art/Route";

        public static void Build(TrackPrefabs prefabs)
        {
            Directory.CreateDirectory(Dir);
            var chevron = Texture("Chevron", 256, 192, ChevronPixel, TextureWrapMode.Clamp);
            var pad = Texture("StartPad", 512, 512, StartPadPixel, TextureWrapMode.Clamp);
            var wear = Texture("Wear", 256, 256, WearPixel, TextureWrapMode.Clamp);

            var kit = prefabs.RouteKit ?? new TrackPrefabs.RouteKitMaterials();
            kit.LaneWarm = Emissive("M_Route_LaneWarm", new Color(1f, 0.82f, 0.55f), 7f);
            kit.LaneRed = Emissive("M_Route_LaneRed", new Color(1f, 0.02f, 0.015f), 4.5f);
            kit.Hardware = Lit("M_Route_Hardware", new Color(0.09f, 0.09f, 0.1f), 0.45f, 0.6f);
            kit.Support = Lit("M_Route_Support", new Color(0.6f, 0.07f, 0.06f), 0.5f, 0.35f);
            kit.Chevron = Lit("M_Route_Chevron", Color.white, 0.25f, 0f, chevron);
            kit.StartPad = Lit("M_Route_StartPad", Color.white, 0.15f, 0f, pad);
            var w = Materials.Unlit("M_Route_Wear", new Color(0.2f, 0.15f, 0.09f, 0.9f), true);
            w.SetTexture("_BaseMap", wear);
            // Ground-level transparent: draw after opaque geometry but before other transparents.
            w.renderQueue = 2950;
            EditorUtility.SetDirty(w);
            kit.Wear = w;
            kit.RacingLine = Led("M_Route_RacingLine", 2.6f);
            prefabs.RouteKit = kit;
            EditorUtility.SetDirty(prefabs);
            AssetDatabase.SaveAssets();
        }

        private static Material Led(string name, float intensity)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("GateRush/LED"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("GateRush/LED");
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Intensity", intensity);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Emissive(string name, Color color, float intensity)
        {
            var m = Materials.Emissive(name, color * 0.25f, 1f);
            m.SetColor("_EmissionColor", color * intensity);
            // Generated at runtime: nothing to bake.
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Lit(string name, Color color, float smoothness, float metallic, Texture2D map = null)
        {
            var m = Materials.Lit(name, color, smoothness);
            m.SetFloat("_Metallic", metallic);
            if (map != null) m.SetTexture("_BaseMap", map);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private delegate Color Pixel(float u, float v);

        private static Texture2D Texture(string name, int w, int h, Pixel pixel, TextureWrapMode wrap)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / h);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{Dir}/T_Route_{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Black board with a white border and two white chevrons pointing along +V (the flying direction).</summary>
        private static Color ChevronPixel(float u, float v)
        {
            float x = Mathf.Abs(u - 0.5f) * 2f; // 0 centre .. 1 edge
            bool border = u < 0.04f || u > 0.96f || v < 0.05f || v > 0.95f;
            bool arrow = false;
            foreach (float tip in new[] { 0.86f, 0.52f })
            {
                float line = tip - 0.42f * x; // the chevron's V shape
                if (v <= line && v >= line - 0.16f && x < 0.85f) arrow = true;
            }
            return border || arrow ? new Color(0.92f, 0.92f, 0.9f) : new Color(0.04f, 0.04f, 0.045f);
        }

        /// <summary>White rubber mat with a light grid, a green border line and two green chevrons pointing along +V.</summary>
        private static Color StartPadPixel(float u, float v)
        {
            var white = new Color(0.9f, 0.9f, 0.88f);
            var green = new Color(0.1f, 0.65f, 0.25f);
            if (u < 0.035f || u > 0.965f || v < 0.035f || v > 0.965f) return green;
            float x = Mathf.Abs(u - 0.5f) * 2f;
            foreach (float tip in new[] { 0.78f, 0.55f })
            {
                float line = tip - 0.3f * x;
                if (v <= line && v >= line - 0.08f && x < 0.6f) return green;
            }
            bool grid = Mathf.Repeat(u * 8f, 1f) < 0.03f || Mathf.Repeat(v * 8f, 1f) < 0.03f;
            return grid ? new Color(0.78f, 0.78f, 0.76f) : white;
        }

        /// <summary>Soft worn patch: alpha falls off towards the edges with a little noise.</summary>
        private static Color WearPixel(float u, float v)
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float noise = Mathf.PerlinNoise(u * 9f + 3.1f, v * 9f + 7.7f) * 0.6f + Mathf.PerlinNoise(u * 31f, v * 31f) * 0.4f;
            float a = Mathf.Clamp01((1f - r) * 1.6f) * Mathf.Lerp(0.35f, 1f, noise);
            return new Color(1f, 1f, 1f, Mathf.Pow(a, 1.4f));
        }
    }
}
