using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Bakes a heavy CC0 tree (git-ignored cache) into a 3-view billboard atlas (alpha-cut albedo, rendered under flat
    /// light) and a crossed-quad mesh, so a tree line costs a few triangles per tree. The atlas, material and mesh are
    /// committed; the source model is not.
    /// </summary>
    public static class ImpostorBaker
    {
        public const string Dir = "Assets/_Project/Art/Materials/Vegetation/TreeImpostor";
        public const string AtlasPath = Dir + "/TreeImpostor_Albedo.png";
        public const string MaterialPath = Dir + "/M_TreeImpostor.mat";
        public const int Views = 3, Cell = 1024, BakeLayer = 31;
        public const float TreeHeight = 11f;

        public static Vector2 CardSize;

        /// <summary>Bakes (once) and returns the impostor material; <see cref="CardSize"/> gets the card width/height.</summary>
        public static Material Bake(string modelId = "island_tree_02", bool force = false)
        {
            var src = ArtCatalog.Find(modelId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{src.Folder}/{src.Id}_{src.Resolution}.fbx");
            Directory.CreateDirectory(Dir);
            var tree = (GameObject)Object.Instantiate(prefab);
            tree.hideFlags = HideFlags.DontSave;
            tree.transform.position = new Vector3(0f, -5000f, 0f);
            var renderers = tree.GetComponentsInChildren<MeshRenderer>();
            var bounds = ArtPass.Bounds(renderers);
            tree.transform.localScale *= TreeHeight / bounds.size.y;
            bounds = ArtPass.Bounds(renderers);
            float width = Mathf.Max(bounds.size.x, bounds.size.z) * 1.02f;
            CardSize = new Vector2(width, bounds.size.y * 1.02f);

            if (!force && File.Exists(AtlasPath) && File.Exists(MaterialPath))
            {
                Object.DestroyImmediate(tree);
                return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            }

            foreach (var r in renderers)
            {
                r.gameObject.layer = BakeLayer;
                r.sharedMaterials = r.sharedMaterials.Select(m => BakeMaterial(src, m)).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
            }

            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            var fog = RenderSettings.fog;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.9f, 0.9f, 0.9f);
            RenderSettings.fog = false;
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).Where(l => l.enabled).ToList();
            foreach (var l in lights) l.enabled = false;

            var camGo = new GameObject("__ImpostorCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(CardSize.y, CardSize.x) * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << BakeLayer;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            var atlas = new Texture2D(Cell * Views, Cell, TextureFormat.RGBA32, false, false);
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(Cell, Cell, RenderTextureFormat.ARGB32, 24) { sRGB = true });
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                Vector3 centre = new Vector3(bounds.center.x, bounds.min.y + cam.orthographicSize, bounds.center.z);
                for (int i = 0; i < Views; i++)
                {
                    float a = i * Mathf.PI / Views;
                    var n = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    cam.transform.SetPositionAndRotation(centre - n * 60f, Quaternion.LookRotation(n, Vector3.up));
                    cam.targetTexture = rt;
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else cam.Render();
                    RenderTexture.active = rt;
                    atlas.ReadPixels(new Rect(0, 0, Cell, Cell), i * Cell, 0);
                }
                atlas.Apply();
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                RenderTexture.active = null;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(tree);
                foreach (var l in lights) l.enabled = true;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.fog = fog;
            }

            Dilate(atlas, 8);
            File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(AtlasPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = 0.5f;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find(MatDef.EnvLit));
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetColor("_BaseColor", new Color(0.92f, 0.95f, 0.88f));
            mat.SetFloat("_Smoothness", 0.08f);
            mat.SetFloat("_Metallic", 0f);
            MaterialLibrary.ConfigureSurface(mat, new MatDef { AlphaClip = true, Cutoff = 0.5f, DoubleSided = true });
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>A transient material for the bake: Env Lit from the model's set, leaves/branches alpha-cut.</summary>
        private static Material BakeMaterial(ArtSource src, Material embedded)
        {
            var baseMat = ArtPass.ModelMaterial(src, embedded);
            var m = new Material(baseMat) { hideFlags = HideFlags.DontSave };
            string name = embedded != null ? embedded.name.ToLowerInvariant() : "";
            bool foliage = name.Contains("leaf") || name.Contains("leaves") || name.Contains("branch");
            if (!foliage) return m;
            string texDir = $"{src.Folder}/textures";
            string kind = name.Contains("branch") ? "branches" : "leaves";
            string diff = Directory.GetFiles(texDir, $"*_{kind}_diff_*").FirstOrDefault(f => !f.EndsWith(".meta"));
            string alpha = Directory.GetFiles(texDir, $"*_{kind}_alpha_*").FirstOrDefault(f => !f.EndsWith(".meta"));
            if (diff != null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { hideFlags = HideFlags.DontSave };
                tex.LoadImage(File.ReadAllBytes(diff));
                if (alpha != null)
                {
                    var a = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    a.LoadImage(File.ReadAllBytes(alpha));
                    var px = tex.GetPixels32();
                    var ap = a.GetPixels32();
                    for (int i = 0; i < px.Length && i < ap.Length; i++) px[i].a = ap[i].r;
                    tex.SetPixels32(px);
                    tex.Apply(true);
                    Object.DestroyImmediate(a);
                }
                m.SetTexture("_BaseMap", tex);
            }
            MaterialLibrary.ConfigureSurface(m, new MatDef { AlphaClip = true, Cutoff = 0.45f, DoubleSided = true });
            return m;
        }

        /// <summary>Pushes opaque colours into transparent texels so mip-mapped alpha-cut edges don't go dark.</summary>
        private static void Dilate(Texture2D t, int passes)
        {
            int w = t.width, h = t.height;
            var px = t.GetPixels32();
            for (int pass = 0; pass < passes; pass++)
            {
                var next = (Color32[])px.Clone();
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (px[i].a > 0) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        var c = px[yy * w + xx];
                        if (c.a == 0 && !(c.r > 0 || c.g > 0 || c.b > 0)) continue;
                        r += c.r; g += c.g; b += c.b; n++;
                    }
                    if (n > 0) next[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                }
                px = next;
            }
            t.SetPixels32(px);
            t.Apply();
        }
    }
}
