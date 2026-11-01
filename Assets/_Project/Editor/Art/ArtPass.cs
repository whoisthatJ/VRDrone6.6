using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GateRush.EditorTools.Art
{
    /// <summary>State shared by one scene's art pass.</summary>
    public sealed class ArtContext
    {
        public Scene Scene;
        public string Env;
        public Transform Environment;
        public Transform Dressing;
        public readonly StringBuilder Log = new StringBuilder();
        public Light Sun;
    }

    /// <summary>Per-scene look: sky, sun, fog and post-processing.</summary>
    public sealed class SceneLook
    {
        public string Hdri;
        public float SkyExposure = 1f;
        public float AmbientIntensity = 1f;
        public float ReflectionIntensity = 1f;
        public bool SunEnabled = true;
        public float SunIntensity = 2f;
        public Color SunTint = Color.white;
        public float ShadowStrength = 0.9f;
        public bool Fog = true;
        public FogMode FogMode = FogMode.ExponentialSquared;
        public float FogDensity = 0.0012f;
        public float FogStart = 150f, FogEnd = 1400f;
        public float FogBrightness = 1f;
        public Color FogColorOverride = Color.clear;
        public float PostExposure;
        public float Contrast = 8f;
        public float Saturation;
        public float Temperature;
        public float Tint;
        public float BloomIntensity = 0.35f;
        public float BloomThreshold = 1f;
        public float Vignette = 0.18f;
        public TonemappingMode Tonemapping = TonemappingMode.ACES;
    }

    /// <summary>
    /// Tools > Art > Apply Art Pass: turns each generated scene into its realistic version without touching gameplay
    /// objects, colliders, layers or triggers. Builder materials are remapped to the library by their original names
    /// (so re-running is safe), environment cubes get metric-UV meshes, decorations live under "Art Dressing"
    /// (rebuilt on every run), and lighting/post-processing are set per scene. Then bake with Tools > Art > Bake Lighting.
    /// </summary>
    public static class ArtPass
    {
        public const string DressingRoot = "Art Dressing";
        public const int TrackGeometryLayer = 8;
        public const string MeshDir = "Assets/_Project/Art/Meshes";
        public const string VolumeDir = "Assets/_Project/Art/Volumes";
        public static readonly string[] Scenes = { "Field", "Hangar", "Industrial", "Menu" };

        [MenuItem("Tools/Art/Apply Art Pass (all scenes)")]
        private static void MenuAll() => Debug.Log(ApplyAll());

        public static string ApplyAllDeferred() => ArtJobs.RunDeferred("art pass", ApplyAll);
        public static string ApplyDeferred(string envs) => ArtJobs.RunDeferred("art pass " + envs, () => string.Join("\n", envs.Split(',').Select(e => ApplyOne(e.Trim()))));

        public static string ApplyAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine(PrefabArt.Apply());
            foreach (var env in Scenes) sb.AppendLine(ApplyOne(env));
            return sb.ToString();
        }

        public static string ApplyOne(string env)
        {
            ArtJobs.RequireCleanScenes();
            var scene = EditorSceneManager.OpenScene(ArtJobs.ScenePath(env == "Menu" ? "Menu" : "Track_" + env), OpenSceneMode.Single);
            var ctx = new ArtContext { Scene = scene, Env = env };
            ctx.Environment = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Environment")?.transform;
            ctx.Sun = scene.GetRootGameObjects().Select(g => g.GetComponent<Light>()).FirstOrDefault(l => l != null && l.type == LightType.Directional);
            ctx.Dressing = ResetDressing(scene);
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            try
            {
                switch (env)
                {
                    case "Field": FieldArt.Apply(ctx); break;
                    case "Hangar": HangarArt.Apply(ctx); break;
                    case "Industrial": IndustrialArt.Apply(ctx); break;
                    case "Menu": MenuArt.Apply(ctx); break;
                }
                if (env != "Menu") PrefabArt.ApplyQuad(ctx);
                SetupCameras(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                ArtJobs.ReturnToBoot();
            }
            return $"{env}: {ctx.Log}";
        }

        // ---------- dressing root ----------

        public static Transform ResetDressing(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == DressingRoot) Object.DestroyImmediate(root);
            var go = new GameObject(DressingRoot);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go.transform;
        }

        public static Transform Group(ArtContext ctx, string name)
        {
            var t = ctx.Dressing.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(ctx.Dressing, false);
            return go.transform;
        }

        // ---------- environment remap ----------

        /// <summary>
        /// Remaps every renderer under Environment: builder material name → library material ("" hides the renderer;
        /// the collider stays). Cubes get a metric-UV mesh sized to their scale with UVs continued from their position.
        /// </summary>
        public static void RemapEnvironment(ArtContext ctx, Dictionary<string, string> map)
        {
            if (ctx.Environment == null) return;
            // Re-runs: an object already wearing a target material is processed again (fresh meshes, same material).
            map = new Dictionary<string, string>(map);
            foreach (var target in map.Values.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList())
                if (!map.ContainsKey(target)) map[target] = target;
            int remapped = 0, hidden = 0;
            foreach (var r in ctx.Environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mat = r.sharedMaterial;
                if (mat == null) continue;
                // Only builder materials are in the map, so a second run skips what the first one remapped.
                if (!map.TryGetValue(mat.name, out var target)) continue;
                if (string.IsNullOrEmpty(target))
                {
                    r.enabled = false;
                    hidden++;
                    continue;
                }
                r.enabled = true;
                r.sharedMaterial = MaterialLibrary.Get(target);
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && IsCube(mf.sharedMesh))
                {
                    var t = r.transform;
                    var size = t.lossyScale;
                    var origin = Quaternion.Inverse(t.rotation) * t.position;
                    mf.sharedMesh = ProcMesh.Box(size, origin, true, "BoxUV " + r.name);
                }
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                remapped++;
            }
            ctx.Log.Append($" remapped {remapped}, hidden {hidden};");
        }

        /// <summary>Gives the builder's ground Plane (UV 0–1 over 10 m × scale) a tiled variant of a library material.</summary>
        public static Renderer TileGround(ArtContext ctx, string material)
        {
            var ground = ctx.Environment != null ? ctx.Environment.Find("Ground") : null;
            if (ground == null) return null;
            var r = ground.GetComponent<MeshRenderer>();
            var master = MaterialLibrary.Get(material);
            var def = MaterialLibrary.Def(material);
            var src = def != null && def.Texture != null ? ArtCatalog.Find(def.Texture) : null;
            float tile = def != null && def.TileMeters > 0f ? def.TileMeters : src != null ? src.TileMeters : 2f;
            float meters = 10f * ground.lossyScale.x;
            r.sharedMaterial = MaterialLibrary.TiledVariant(master, $"{MaterialLibrary.LibraryDir}/Variants/{material}_{ctx.Env}Ground.mat", meters, tile);
            r.enabled = true;
            return r;
        }

        public static bool IsCube(Mesh m) => m != null && (m.name == "Cube" || m.name.StartsWith("BoxUV"));

        // ---------- lighting ----------

        public static HdriSun.Result ApplyLook(ArtContext ctx, SceneLook look)
        {
            var src = ArtCatalog.Find(look.Hdri);
            string hdrPath = $"{EnvironmentAssets.SkyDir}/{src.Id}_{src.Resolution}.hdr";
            var hdr = HdriSun.Analyze(hdrPath);

            var sky = EnvironmentAssets.Sky(look.Hdri);
            sky.SetFloat("_Exposure", look.SkyExposure);
            sky.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = look.AmbientIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = look.ReflectionIntensity;
            RenderSettings.defaultReflectionResolution = 256;

            if (ctx.Sun != null)
            {
                var sun = ctx.Sun;
                sun.enabled = look.SunEnabled;
                sun.transform.rotation = Quaternion.LookRotation(-hdr.SunDirection, Vector3.up);
                // The HDRI's sun pixels are saturated by the camera response; keep 60 % of their hue.
                sun.color = Color.Lerp(Color.white, hdr.SunColor, 0.6f) * look.SunTint;
                sun.intensity = look.SunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = look.ShadowStrength;
                sun.shadowBias = 0.05f;
                sun.shadowNormalBias = 0.4f;
                sun.lightmapBakeType = LightmapBakeType.Mixed;
                RenderSettings.sun = look.SunEnabled ? sun : null;
                ctx.Log.Append($" sun elev {hdr.SunElevation:F1}°;");
            }

            RenderSettings.fog = look.Fog;
            if (look.Fog)
            {
                RenderSettings.fogMode = look.FogMode;
                RenderSettings.fogDensity = look.FogDensity;
                RenderSettings.fogStartDistance = look.FogStart;
                RenderSettings.fogEndDistance = look.FogEnd;
                Color c = look.FogColorOverride.a > 0f ? look.FogColorOverride : hdr.HorizonColor * look.SkyExposure * look.FogBrightness;
                float m = c.maxColorComponent;
                if (m > 1f) c /= m;
                c.a = 1f;
                RenderSettings.fogColor = c.gamma;
            }
            SetupVolume(ctx, look);
            DynamicGI.UpdateEnvironment();
            return hdr;
        }

        public static void SetupVolume(ArtContext ctx, SceneLook look)
        {
            Directory.CreateDirectory(VolumeDir);
            string path = $"{VolumeDir}/VP_{ctx.Env}.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var tone = Add<Tonemapping>();
            tone.mode.Override(look.Tonemapping);
            var adj = Add<ColorAdjustments>();
            adj.postExposure.Override(look.PostExposure);
            adj.contrast.Override(look.Contrast);
            adj.saturation.Override(look.Saturation);
            var wb = Add<WhiteBalance>();
            wb.temperature.Override(look.Temperature);
            wb.tint.Override(look.Tint);
            var bloom = Add<Bloom>();
            bloom.threshold.Override(look.BloomThreshold);
            bloom.intensity.Override(look.BloomIntensity);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(true);
            if (look.Vignette > 0f)
            {
                var v = Add<Vignette>();
                v.intensity.Override(look.Vignette);
                v.smoothness.Override(0.45f);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Post Volume");
            go.transform.SetParent(ctx.Dressing, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
            vol.sharedProfile = profile;
        }

        /// <summary>Game cameras render post-processing (GraphicsQuality applies the player's choices at runtime).</summary>
        public static void SetupCameras(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (data.renderType == CameraRenderType.Overlay) continue;
                    data.renderPostProcessing = true;
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                    data.stopNaN = true;
                    data.dithering = true;
                    data.renderShadows = true;
                    cam.allowHDR = true;
                    cam.allowMSAA = true;
                    cam.useOcclusionCulling = true;
                    EditorUtility.SetDirty(data);
                }
        }

        public static ReflectionProbe AddProbe(ArtContext ctx, string name, Vector3 centre, Vector3 size, bool boxProjection, int resolution = 256, float intensity = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Group(ctx, "Probes"), false);
            go.transform.position = centre;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Baked;
            p.size = size;
            p.boxProjection = boxProjection;
            p.resolution = resolution;
            p.intensity = intensity;
            p.hdr = true;
            p.importance = 1;
            p.blendDistance = boxProjection ? 2f : 10f;
            return p;
        }

        // ---------- placing things ----------

        public const StaticEditorFlags LightmappedStatic = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                                                           StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        public const StaticEditorFlags ProbeLitStatic = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        /// <summary>Adds a mesh object. <paramref name="solid"/> adds a collider on TrackGeometry (quads crash into it).</summary>
        public static GameObject AddMesh(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation,
            Vector3 scale, bool lightmapped = false, bool solid = false, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = lightmapped ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;
            r.receiveGI = lightmapped ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            GameObjectUtility.SetStaticEditorFlags(go, lightmapped ? LightmappedStatic : ProbeLitStatic);
            if (solid)
            {
                go.layer = TrackGeometryLayer;
                var mc = go.AddComponent<BoxCollider>();
                mc.center = mesh.bounds.center;
                mc.size = mesh.bounds.size;
            }
            return go;
        }

        public static Material[] Mats(params string[] names) => names.Select(MaterialLibrary.Get).ToArray();

        /// <summary>Loads or (re)writes a shared mesh asset in Art/Meshes so several scenes can reference it.</summary>
        public static Mesh MeshAsset(string name, Func<Mesh> build)
        {
            Directory.CreateDirectory(MeshDir);
            string path = $"{MeshDir}/{name}.asset";
            var fresh = build();
            fresh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            EditorUtility.CopySerialized(fresh, existing);
            existing.name = name;
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>
        /// Instantiates a Poly Haven model (Art/Models/&lt;id&gt;) scaled so its height is <paramref name="height"/> metres
        /// (0 keeps the file scale), with Env Lit materials from its texture sets. Probe-lit; optional box collider.
        /// </summary>
        public static GameObject AddModel(Transform parent, string id, Vector3 position, float yaw, float height = 0f, bool solid = false, bool castShadows = true)
        {
            var src = ArtCatalog.Find(id);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{src.Folder}/{src.Id}_{src.Resolution}.fbx");
            if (prefab == null) throw new Exception("model missing: " + id);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = id;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation);
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = Bounds(renderers);
            if (height > 0f && bounds.size.y > 0.001f) go.transform.localScale *= height / bounds.size.y;
            // Rest on the ground.
            bounds = Bounds(renderers);
            go.transform.position += Vector3.up * (position.y - bounds.min.y);
            foreach (var r in renderers)
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => ModelMaterial(src, m)).ToArray();
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveGI = ReceiveGI.LightProbes;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, ProbeLitStatic);
            }
            if (solid)
            {
                // Axis-aligned box around the model, as a sibling so the model's scale doesn't distort it.
                bounds = Bounds(renderers);
                var col = new GameObject(id + " collider") { layer = TrackGeometryLayer };
                col.transform.SetParent(parent, false);
                col.transform.position = bounds.center;
                col.AddComponent<BoxCollider>().size = bounds.size;
                GameObjectUtility.SetStaticEditorFlags(col, StaticEditorFlags.BatchingStatic);
            }
            return go;
        }

        public static Bounds Bounds(IEnumerable<Renderer> renderers)
        {
            Bounds? b = null;
            foreach (var r in renderers)
            {
                if (b == null) b = r.bounds;
                else
                {
                    var bb = b.Value;
                    bb.Encapsulate(r.bounds);
                    b = bb;
                }
            }
            return b ?? new Bounds();
        }

        /// <summary>Env Lit material for one embedded FBX material, from the model's matching Poly Haven texture set.</summary>
        public static Material ModelMaterial(ArtSource src, Material embedded)
        {
            string matName = embedded != null ? embedded.name : src.Id;
            string texDir = $"{src.Folder}/textures";
            var diffs = Directory.Exists(texDir) ? Directory.GetFiles(texDir, "*_diff_*").Where(f => !f.EndsWith(".meta")).ToArray() : new string[0];
            string Prefix(string f) => Path.GetFileName(f).Substring(0, Path.GetFileName(f).IndexOf("_diff_", StringComparison.Ordinal));
            string diff = diffs.FirstOrDefault(f => string.Equals(Prefix(f), matName, StringComparison.OrdinalIgnoreCase))
                          ?? diffs.FirstOrDefault(f => matName.IndexOf(Prefix(f), StringComparison.OrdinalIgnoreCase) >= 0 || Prefix(f).IndexOf(matName, StringComparison.OrdinalIgnoreCase) >= 0)
                          ?? (diffs.Length == 1 ? diffs[0] : diffs.FirstOrDefault());
            string setName = diff != null ? Prefix(diff) : matName;
            string path = $"{src.Folder}/M_{setName}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(MatDef.EnvLit));
                AssetDatabase.CreateAsset(m, path);
            }
            Texture2D Tex(string kind)
            {
                if (diff == null) return null;
                string file = Directory.GetFiles(texDir, setName + kind + "*").FirstOrDefault(f => !f.EndsWith(".meta"));
                return file == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(file.Replace("\\", "/"));
            }
            var albedo = diff != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(diff.Replace("\\", "/")) : null;
            var normal = Tex("_nor_gl_");
            var orm = Tex("_arm_");
            bool alpha = albedo != null && diff.EndsWith(".png") && setName.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0;
            m.SetTexture("_BaseMap", albedo);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.SetTexture("_MetallicGlossMap", orm);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetKeyword(new LocalKeyword(m.shader, "_NORMALMAP"), normal != null);
            m.SetKeyword(new LocalKeyword(m.shader, "_METALLICSPECGLOSSMAP"), orm != null);
            if (orm == null)
            {
                m.SetFloat("_Smoothness", 0.35f);
                m.SetFloat("_Metallic", 0f);
            }
            MaterialLibrary.ConfigureSurface(m, new MatDef { AlphaClip = alpha, DoubleSided = alpha });
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
