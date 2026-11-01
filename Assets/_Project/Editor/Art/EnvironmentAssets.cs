using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Sky materials from the HDRIs and the Field terrain layers (diffuse, normal and a URP terrain mask map:
    /// R metallic, G AO, B height, A smoothness) plus the height-blended terrain material.
    /// </summary>
    public static class EnvironmentAssets
    {
        public const string SkyDir = "Assets/_Project/Art/HDRI";
        public const string TerrainDir = "Assets/_Project/Art/Terrain";
        public const string TerrainMaterialPath = TerrainDir + "/M_Terrain_Field.mat";

        public sealed class LayerDef
        {
            public string Name;
            public string Texture;      // catalog set, or "proc:Chalk"
            public float TileMeters;
            public Color Tint = Color.white;
            public float Smoothness = 1f; // multiplier
        }

        public static readonly List<LayerDef> FieldLayers = new List<LayerDef>
        {
            new LayerDef { Name = "TL_Grass_Lawn", Texture = "Grass_Lawn", TileMeters = 2f },
            new LayerDef { Name = "TL_Grass_Worn", Texture = "Grass_Worn", TileMeters = 2f },
            new LayerDef { Name = "TL_Dirt_Field", Texture = "Dirt_Field", TileMeters = 2.07f },
            new LayerDef { Name = "TL_Chalk", Texture = "proc:Chalk", TileMeters = 0.5f },
        };

        [MenuItem("Tools/Art/Build Skies and Terrain Layers")]
        private static void Menu() => Debug.Log(BuildAll());

        public static string BuildAll() => BuildSkies() + " | " + BuildTerrainLayers();

        public static string SkyPath(string hdriId) => $"{SkyDir}/Sky_{hdriId}.mat";

        public static Material Sky(string hdriId) => AssetDatabase.LoadAssetAtPath<Material>(SkyPath(hdriId));

        public static Cubemap SkyCube(string hdriId)
        {
            var src = ArtCatalog.Find(hdriId);
            return src == null ? null : AssetDatabase.LoadAssetAtPath<Cubemap>($"{SkyDir}/{src.Id}_{src.Resolution}.hdr");
        }

        public static string BuildSkies()
        {
            var shader = Shader.Find("Skybox/Cubemap");
            int n = 0;
            foreach (var s in ArtCatalog.Hdris)
            {
                var cube = SkyCube(s.Id);
                if (cube == null) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(SkyPath(s.Id));
                if (m == null)
                {
                    m = new Material(shader) { name = "Sky_" + s.Id };
                    AssetDatabase.CreateAsset(m, SkyPath(s.Id));
                }
                m.shader = shader;
                m.SetTexture("_Tex", cube);
                m.SetFloat("_Exposure", 1f);
                m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            return $"{n} sky materials";
        }

        public static TerrainLayer Layer(string name) => AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{TerrainDir}/{name}.terrainlayer");

        public static string BuildTerrainLayers()
        {
            Directory.CreateDirectory(TerrainDir);
            int n = 0;
            foreach (var def in FieldLayers)
            {
                string dir, set;
                if (def.Texture.StartsWith("proc:"))
                {
                    set = def.Texture.Substring(5);
                    dir = ArtProcedural.Dir(set);
                }
                else
                {
                    var src = ArtCatalog.Find(def.Texture);
                    dir = src.Folder;
                    set = src.Name;
                }
                var albedo = MaterialLibrary.LoadAny(dir, set + "_Albedo");
                var normal = MaterialLibrary.LoadAny(dir, set + "_Normal");
                string maskPath = $"{TerrainDir}/{def.Name}_Mask.png";
                if (!File.Exists(maskPath)) WriteMask(dir, set, maskPath);
                AssetDatabase.ImportAsset(maskPath);
                var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);

                string path = $"{TerrainDir}/{def.Name}.terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null)
                {
                    layer = new TerrainLayer { name = def.Name };
                    AssetDatabase.CreateAsset(layer, path);
                }
                layer.diffuseTexture = albedo;
                layer.normalMapTexture = normal;
                layer.maskMapTexture = mask;
                layer.tileSize = new Vector2(def.TileMeters, def.TileMeters);
                layer.normalScale = 1f;
                layer.diffuseRemapMin = Vector4.zero;
                layer.diffuseRemapMax = new Vector4(def.Tint.r, def.Tint.g, def.Tint.b, 1f);
                layer.maskMapRemapMin = Vector4.zero;
                layer.maskMapRemapMax = new Vector4(1f, 1f, 1f, def.Smoothness);
                EditorUtility.SetDirty(layer);
                n++;
            }

            var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            if (mat == null)
            {
                mat = new Material(terrainShader) { name = "M_Terrain_Field" };
                AssetDatabase.CreateAsset(mat, TerrainMaterialPath);
            }
            mat.shader = terrainShader;
            mat.SetFloat("_EnableHeightBlend", 1f);
            mat.SetFloat("_HeightTransition", 0.25f);
            mat.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            mat.SetFloat("_EnableInstancedPerPixelNormal", 1f);
            mat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            mat.EnableKeyword("_MASKMAP");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return $"{n} terrain layers + terrain material";
        }

        /// <summary>Packs ORM (+ height) into URP's terrain mask layout at 1024².</summary>
        private static void WriteMask(string dir, string set, string target)
        {
            byte[] Read(string baseName)
            {
                foreach (var ext in new[] { ".jpg", ".png" })
                    if (File.Exists($"{dir}/{baseName}{ext}")) return File.ReadAllBytes($"{dir}/{baseName}{ext}");
                return null;
            }

            const int size = 1024;
            Color32[] Sample(byte[] bytes)
            {
                if (bytes == null) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                t.LoadImage(bytes);
                var src = t.GetPixels32();
                var dst = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    dst[y * size + x] = src[(y * t.height / size) * t.width + x * t.width / size];
                Object.DestroyImmediate(t);
                return dst;
            }

            var orm = Sample(Read(set + "_ORM"));
            var height = Sample(Read(set + "_Height"));
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                byte ao = orm != null ? orm[i].r : (byte)255;
                byte rough = orm != null ? orm[i].g : (byte)220;
                byte metal = orm != null ? orm[i].b : (byte)0;
                byte h = height != null ? height[i].r : (byte)128;
                px[i] = new Color32(metal, ao, h, (byte)(255 - rough));
            }
            var mask = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            mask.SetPixels32(px);
            File.WriteAllBytes(target, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);
        }
    }
}
