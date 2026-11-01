using System.IO;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Import presets for everything under Assets/_Project/Art (and the impostor cache), chosen by file-name suffix:
    /// _Albedo/_diff → sRGB BC7; _Normal/_nor_gl → normal map BC5; _ORM/_arm/_Mask/_AO/_rough/_metal → linear BC7;
    /// _Height/_disp → linear BC7. Max 2048 px (4096 in folders named "hero"), aniso 8, trilinear, mipmap streaming.
    /// HDRIs (.hdr) become BC6H cubemaps; models import without materials (the art pass assigns its own).
    /// </summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        private const string ArtRoot = "Assets/_Project/Art/";
        private enum Role { None, Albedo, Normal, Mask, Height }

        private static bool Managed(string path) => path.StartsWith(ArtRoot) || path.StartsWith(ArtCatalog.CacheRoot + "/");

        private static Role Classify(string fileName)
        {
            string f = fileName.ToLowerInvariant();
            if (f.EndsWith("_albedo") || f.Contains("_diff_") || f.EndsWith("_diff") || f.Contains("_color")) return Role.Albedo;
            if (f.EndsWith("_normal") || f.Contains("_nor_gl")) return Role.Normal;
            if (f.EndsWith("_orm") || f.Contains("_arm") || f.EndsWith("_mask") || f.Contains("_ao_") || f.Contains("_rough") || f.Contains("_metal_")) return Role.Mask;
            if (f.EndsWith("_height") || f.Contains("_disp")) return Role.Height;
            return Role.None;
        }

        private void OnPreprocessTexture()
        {
            if (!Managed(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            string ext = Path.GetExtension(assetPath).ToLowerInvariant();
            var standalone = ti.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;

            if ((ext == ".hdr" || ext == ".exr") && assetPath.Contains("/HDRI/"))
            {
                ti.textureShape = TextureImporterShape.TextureCube;
                ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 4096;
                standalone.maxTextureSize = 4096;
                standalone.format = TextureImporterFormat.BC6H;
                ti.SetPlatformTextureSettings(standalone);
                return;
            }

            var role = Classify(Path.GetFileNameWithoutExtension(assetPath));
            if (role == Role.None) return;
            int max = assetPath.ToLowerInvariant().Contains("/hero/") ? 4096 : 2048;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.mipmapEnabled = true;
            ti.streamingMipmaps = true;
            ti.anisoLevel = 8;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = max;
            standalone.maxTextureSize = max;
            standalone.compressionQuality = 50;

            switch (role)
            {
                case Role.Albedo:
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = ext == ".png";
                    standalone.format = TextureImporterFormat.BC7;
                    break;
                case Role.Normal:
                    ti.textureType = TextureImporterType.NormalMap;
                    standalone.format = TextureImporterFormat.BC5;
                    break;
                default:
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = false;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput; // terrain mask maps keep smoothness in alpha
                    standalone.format = TextureImporterFormat.BC7;
                    break;
            }
            ti.SetPlatformTextureSettings(standalone);
        }

        private void OnPreprocessModel()
        {
            if (!Managed(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            bool cache = assetPath.StartsWith(ArtCatalog.CacheRoot + "/");
            // Embedded materials only carry the FBX material names; the art pass swaps them for Env Lit materials
            // built from the matching Poly Haven texture set (ArtPass.ModelMaterials).
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true; // Z-up FBX → Y-up baked into the mesh, so placing code can set any yaw
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = cache; // impostor sources are read back when baking
            // Dressing models are lit by light probes (ContributeGI off), so they need no lightmap UVs; Unity's
            // unwrapper also fails on several of these small CC0 meshes ("Could not pack uvs").
            mi.generateSecondaryUV = false;
        }
    }
}
