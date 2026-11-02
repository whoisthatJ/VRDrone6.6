using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>One master material. Textured ones use a catalog texture set; tiling assumes metric UVs (BoxUvMesh).</summary>
    public sealed class MatDef
    {
        public string Name;
        /// <summary>Catalog texture-set name, a procedural set ("proc:CarbonFiber") or null for a plain colour.</summary>
        public string Texture;
        public Color Tint = Color.white;
        /// <summary>Textured: multiplier on (1 − roughness). Plain: absolute smoothness.</summary>
        public float Smoothness = 1f;
        /// <summary>Textured: multiplier on the mask's metallic. Plain: absolute metallic.</summary>
        public float Metallic = 1f;
        public float NormalScale = 1f;
        public float Occlusion = 1f;
        /// <summary>Macro-variation strength (detail-albedo overlay); 0 turns it off.</summary>
        public float Macro = 0.35f;
        public float MacroMeters = 30f;
        /// <summary>Overrides the catalog tile size (metres per texture repeat).</summary>
        public float TileMeters;
        public bool AlphaClip;
        public float Cutoff = 0.5f;
        public bool DoubleSided;
        public bool Transparent;
        public Color Emission = Color.black;
        public string Shader = EnvLit;
        public float LedIntensity = 6f;

        public const string EnvLit = "GateRush/Env Lit";
        public const string Led = "GateRush/LED";
    }

    /// <summary>
    /// Tools > Art > Build Material Library: the master materials (GateRush/Env Lit) for every scene, built from the
    /// downloaded CC0 sets and the procedural textures. Rebuilding overwrites settings from these definitions, so tweak
    /// values here, not in the inspector. Masters live next to their textures; plain and procedural ones in Library/.
    /// </summary>
    public static class MaterialLibrary
    {
        public const string LibraryDir = "Assets/_Project/Art/Materials/Library";
        public const string MacroNoise = ArtProcedural.Root + "/Macro/Macro_Noise_Mask.png";

        private static MatDef T(string name, string texture, float smooth = 1f, float metal = 1f, float macro = 0.35f, float macroM = 30f) =>
            new MatDef { Name = name, Texture = texture, Smoothness = smooth, Metallic = metal, Macro = macro, MacroMeters = macroM };

        private static MatDef Tint(string name, string texture, Color tint, float smooth = 1f, float metal = 1f, float macro = 0.3f) =>
            new MatDef { Name = name, Texture = texture, Tint = tint, Smoothness = smooth, Metallic = metal, Macro = macro };

        private static MatDef Plain(string name, Color color, float smooth, float metal = 0f) =>
            new MatDef { Name = name, Tint = color, Smoothness = smooth, Metallic = metal, Macro = 0f };

        public static readonly List<MatDef> Defs = new List<MatDef>
        {
            // Ground
            T("M_Grass_Lawn", "Grass_Field", smooth: 0.25f, macro: 0.45f, macroM: 25f),
            T("M_Grass_Worn", "Grass_Worn", smooth: 0.3f, macro: 0.4f),
            T("M_Dirt_Field", "Dirt_Field", macro: 0.4f),
            T("M_Asphalt_PitLane", "Asphalt_PitLane", macro: 0.3f),
            T("M_Asphalt_Old", "Asphalt_Old", macro: 0.45f, macroM: 40f),
            T("M_Gravel", "Gravel", macro: 0.3f),
            // Concrete and masonry
            T("M_Concrete_HangarFloor", "Concrete_GarageFloor", smooth: 1.55f, macro: 0.3f, macroM: 18f),
            T("M_Concrete_PaintedFloor", "Concrete_PaintedFloor", smooth: 1.25f, macro: 0.25f),
            T("M_Concrete_Wall", "Concrete_Wall", macro: 0.35f, macroM: 20f),
            Tint("M_Concrete_Pillar", "Concrete_Damaged", new Color(1.2f, 1.2f, 1.22f), macro: 0.35f),
            T("M_Concrete_Damaged", "Concrete_Damaged", macro: 0.35f, macroM: 35f),
            T("M_Concrete_Blocks", "Concrete_Blocks", macro: 0.25f),
            T("M_Brick_Factory", "Brick_Factory", macro: 0.3f, macroM: 25f),
            // Metal
            T("M_Steel_Corrugated", "Steel_Corrugated", macro: 0.25f),
            Tint("M_Steel_CorrugatedDark", "Steel_Corrugated", new Color(0.42f, 0.44f, 0.47f), smooth: 0.8f, macro: 0.2f),
            T("M_Steel_CorrugatedRusty", "Steel_CorrugatedRusty", macro: 0.25f),
            T("M_Iron_CorrugatedRusty", "Iron_CorrugatedRusty", macro: 0.3f),
            T("M_Metal_ShutterRusty", "Metal_ShutterRusty", macro: 0.2f),
            T("M_Metal_ShutterPainted", "Metal_ShutterPainted", macro: 0.2f),
            T("M_Steel_PaintedYellow", "Steel_PaintedYellow", macro: 0.2f),
            T("M_Steel_PaintedGreen", "Steel_PaintedGreen", macro: 0.2f),
            T("M_Steel_PaintedRed", "Steel_PaintedRed", macro: 0.2f),
            T("M_Aluminium_Brushed", "Aluminium_Brushed", smooth: 0.82f, macro: 0f),
            T("M_Steel_Brushed", "Steel_Brushed", smooth: 0.75f, macro: 0f),
            T("M_Steel_Rusty", "Steel_Rusty", macro: 0.25f),
            T("M_Steel_Plate", "Steel_Plate", macro: 0.2f),
            Tint("M_Steel_Truss", "Steel_Plate", new Color(0.55f, 0.6f, 0.66f), smooth: 0.9f, metal: 0.4f, macro: 0.15f),
            Tint("M_Crane_Yellow", "Steel_Plate", new Color(1.75f, 1.32f, 0.16f), smooth: 0.8f, metal: 0.1f, macro: 0.3f),
            Tint("M_Container_Blue", "Steel_Corrugated", new Color(0.16f, 0.33f, 0.62f), smooth: 0.75f, metal: 0.1f),
            Tint("M_Container_Red", "Steel_Corrugated", new Color(0.62f, 0.14f, 0.1f), smooth: 0.75f, metal: 0.1f),
            Tint("M_Container_Green", "Steel_Corrugated", new Color(0.18f, 0.42f, 0.24f), smooth: 0.75f, metal: 0.1f),
            Tint("M_Container_Yellow", "Steel_Corrugated", new Color(0.78f, 0.58f, 0.12f), smooth: 0.75f, metal: 0.1f),
            // Wood
            T("M_Wood_Planks", "Wood_Planks", macro: 0.2f),
            T("M_Wood_Plywood", "Wood_Plywood", macro: 0.15f),
            T("M_Wood_Tabletop", "Wood_Tabletop", macro: 0.1f),
            // Fabric, rubber, plastic, fence
            Tint("M_Fabric_GateOrange", "Fabric_Woven", new Color(1.85f, 0.72f, 0.1f), smooth: 0.9f, macro: 0f),
            Tint("M_Fabric_GateBlue", "Fabric_Woven", new Color(0.22f, 0.62f, 1.9f), smooth: 0.9f, macro: 0f),
            Tint("M_Fabric_GateRed", "Fabric_Woven", new Color(1.8f, 0.16f, 0.2f), smooth: 0.9f, macro: 0f),
            Tint("M_Fabric_Black", "Fabric_Woven", new Color(0.18f, 0.18f, 0.2f), smooth: 0.9f, macro: 0f),
            T("M_Fabric_Canvas", "Fabric_Canvas", macro: 0f),
            Tint("M_Fabric_CanvasRed", "Fabric_Canvas", new Color(0.85f, 0.16f, 0.12f), macro: 0f),
            Tint("M_Fabric_Sandbag", "Fabric_Linen", new Color(0.95f, 0.85f, 0.65f), macro: 0f),
            T("M_Rubber_Mat", "Rubber_Mat", macro: 0.2f),
            T("M_Rubber_Black", "Rubber_Black", macro: 0f),
            T("M_Plastic_Black", "Plastic_Matte", macro: 0f),
            new MatDef { Name = "M_Fence_Chainlink", Texture = "Fence_Chainlink", AlphaClip = true, Cutoff = 0.45f, DoubleSided = true, Macro = 0f },
            Tint("M_Fabric_GateGreen", "Fabric_Woven", new Color(0.25f, 1.45f, 0.42f), smooth: 0.9f, macro: 0f),
            // T2 obstacle kit: padded covers (stitched-seam fabric), powder-coated truss, net, flag, pads, signs
            Pad("M_Kit_Pad_Red", new Color(0.86f, 0.09f, 0.08f)),
            Pad("M_Kit_Pad_Green", new Color(0.12f, 0.62f, 0.22f)),
            Pad("M_Kit_Pad_Blue", new Color(0.08f, 0.26f, 0.86f)),
            Pad("M_Kit_Pad_White", new Color(0.92f, 0.92f, 0.9f)),
            Pad("M_Kit_Pad_Black", new Color(0.09f, 0.09f, 0.1f)),
            Tint("M_Kit_Truss_Black", "Steel_Plate", new Color(0.09f, 0.09f, 0.1f), smooth: 0.85f, metal: 0.4f, macro: 0.1f),
            Tint("M_Kit_Truss_Green", "Steel_Plate", new Color(0.18f, 0.66f, 0.24f), smooth: 0.85f, metal: 0.25f, macro: 0.1f),
            Tint("M_Kit_Truss_Red", "Steel_Plate", new Color(0.75f, 0.1f, 0.08f), smooth: 0.85f, metal: 0.25f, macro: 0.1f),
            new MatDef { Name = "M_Kit_Net", Texture = "proc:SafetyNet", TileMeters = 0.2f, AlphaClip = true, Cutoff = 0.5f, DoubleSided = true, Smoothness = 0.3f, Metallic = 0f, Macro = 0f },
            new MatDef { Name = "M_Kit_Flag", Texture = "proc:BrandFlag", TileMeters = 1f, DoubleSided = true, Smoothness = 0.3f, Metallic = 0f, Macro = 0f },
            new MatDef { Name = "M_Kit_LandingPad", Texture = "proc:LandingPad", TileMeters = 1f, Smoothness = 1f, Metallic = 0f, Macro = 0f },
            new MatDef { Name = "M_Kit_StartGrid", Texture = "proc:StartGrid", TileMeters = 1f, Smoothness = 0.35f, Metallic = 0f, Macro = 0f },
            // T3 stadium: mown pitch stripes, red tartan running track, line paint, seats, LED boards and lamps
            Tint("M_Stadium_Grass_Light", "Grass_Field", new Color(0.92f, 1.08f, 0.86f), smooth: 0.3f, macro: 0.25f),
            Tint("M_Stadium_Grass_Dark", "Grass_Field", new Color(0.68f, 0.82f, 0.62f), smooth: 0.3f, macro: 0.25f),
            Tint("M_Stadium_Tartan", "Rubber_Mat", new Color(2.1f, 0.55f, 0.36f), smooth: 0.6f, macro: 0.2f),
            Plain("M_Stadium_LinePaint", new Color(0.86f, 0.86f, 0.84f), 0.3f),
            Plain("M_Stadium_Seat_Red", new Color(0.62f, 0.07f, 0.06f), 0.55f),
            Plain("M_Stadium_Seat_Blue", new Color(0.06f, 0.16f, 0.52f), 0.55f),
            new MatDef { Name = "M_Stadium_LedBanner", Shader = MatDef.Led, Texture = "proc:LedBanner", Tint = Color.white, LedIntensity = 2.2f },
            new MatDef { Name = "M_Stadium_Floodlamp", Shader = MatDef.Led, Tint = new Color(1f, 0.96f, 0.88f), LedIntensity = 12f },
            // T7 garage
            new MatDef { Name = "M_Garage_CuttingMat", Texture = "proc:CuttingMat", TileMeters = 0.3f, Smoothness = 0.35f, Metallic = 0f, Macro = 0f },
            new MatDef { Name = "M_Garage_Pegboard", Texture = "proc:Pegboard", TileMeters = 0.2f, Smoothness = 0.2f, Metallic = 0f, Macro = 0f },
            Plain("M_Garage_Gold", new Color(1f, 0.74f, 0.3f), 0.75f, 1f),
            Plain("M_Garage_ToolRed", new Color(0.75f, 0.08f, 0.06f), 0.45f),
            Plain("M_Garage_ToolYellow", new Color(0.9f, 0.7f, 0.05f), 0.45f),
            new MatDef { Name = "M_Garage_Moi", Tint = new Color(0.2f, 0.85f, 1f, 0.22f), Smoothness = 0.9f, Metallic = 0f, Macro = 0f, Transparent = true, DoubleSided = true },
            new MatDef { Name = "M_Kit_ChevronSign", Texture = "proc:ChevronSign", TileMeters = 1f, Smoothness = 0.4f, Metallic = 0f, Macro = 0f },
            // Plain and procedural
            Plain("M_Paint_Yellow", new Color(0.88f, 0.66f, 0.06f), 0.42f),
            Plain("M_Paint_White", new Color(0.82f, 0.82f, 0.8f), 0.42f),
            Plain("M_Glass_Night", new Color(0.02f, 0.035f, 0.07f), 0.96f),
            Plain("M_Glass_Dirty", new Color(0.05f, 0.06f, 0.065f), 0.82f),
            new MatDef { Name = "M_Prop_Front", Tint = new Color(1f, 0.45f, 0.08f, 0.6f), Smoothness = 0.85f, Metallic = 0f, Macro = 0f, Transparent = true, DoubleSided = true },
            new MatDef { Name = "M_Prop_Rear", Tint = new Color(0.1f, 0.1f, 0.12f, 0.7f), Smoothness = 0.85f, Metallic = 0f, Macro = 0f, Transparent = true, DoubleSided = true },
            new MatDef { Name = "M_Puddle", Texture = "proc:DecalPuddle", TileMeters = 1f, Tint = Color.white, Smoothness = 0.97f, Metallic = 0f, Macro = 0f, Transparent = true },
            Plain("M_Plastic_ConeOrange", new Color(0.95f, 0.32f, 0.05f), 0.42f),
            Plain("M_Plastic_White", new Color(0.82f, 0.82f, 0.8f), 0.45f),
            Plain("M_Plastic_Grey", new Color(0.22f, 0.23f, 0.25f), 0.35f),
            Plain("M_Reflective_White", new Color(0.9f, 0.9f, 0.9f), 0.7f),
            Plain("M_Metal_Anodized", new Color(0.55f, 0.12f, 0.75f), 0.62f, 1f),
            Plain("M_Metal_MotorBell", new Color(0.12f, 0.12f, 0.13f), 0.55f, 1f),
            Plain("M_Copper", new Color(0.95f, 0.64f, 0.54f), 0.6f, 1f),
            new MatDef { Name = "M_CarbonFiber", Texture = "proc:CarbonFiber", TileMeters = 0.05f, Smoothness = 1f, Metallic = 0f, Macro = 0f },
            new MatDef { Name = "M_SafetyStripes", Texture = "proc:SafetyStripes", TileMeters = 0.6f, Smoothness = 0.45f, Metallic = 0f, Macro = 0f },
            new MatDef { Name = "M_Lipo", Texture = "proc:Lipo", TileMeters = 1f, Smoothness = 0.55f, Metallic = 0f, Macro = 0f },
            new MatDef { Name = "M_Checker", Texture = "proc:Checker", TileMeters = 0.5f, Smoothness = 0.3f, Metallic = 0f, Macro = 0f },
            new MatDef { Name = "M_Water", Texture = "proc:Water", Tint = new Color(0.035f, 0.06f, 0.07f), TileMeters = 6f, Smoothness = 0.93f, Metallic = 0f, NormalScale = 0.35f, Macro = 0f },
            new MatDef { Name = "M_Prop_Polycarbonate", Tint = new Color(0.15f, 0.5f, 0.95f, 0.55f), Smoothness = 0.85f, Metallic = 0f, Macro = 0f, Transparent = true, DoubleSided = true },
            new MatDef { Name = "M_Lamp_Emitter", Tint = new Color(0.9f, 0.9f, 0.88f), Smoothness = 0.5f, Metallic = 0f, Macro = 0f, Emission = new Color(1f, 0.93f, 0.82f) * 6f },
            new MatDef { Name = "M_Lamp_Warm", Tint = new Color(0.9f, 0.85f, 0.75f), Smoothness = 0.5f, Metallic = 0f, Macro = 0f, Emission = new Color(1f, 0.72f, 0.4f) * 5f },
            new MatDef { Name = "M_LED_Gate", Shader = MatDef.Led, Tint = Color.white, LedIntensity = 7f },
            new MatDef { Name = "M_LED_Red", Shader = MatDef.Led, Tint = new Color(1f, 0.06f, 0.04f), LedIntensity = 5f },
            new MatDef { Name = "M_LED_Blue", Shader = MatDef.Led, Tint = new Color(0.1f, 0.35f, 1f), LedIntensity = 5f },
        };

        private static MatDef Pad(string name, Color color) =>
            new MatDef { Name = name, Texture = "proc:PadFabric", Tint = color, TileMeters = 0.25f, Smoothness = 1f, Metallic = 0f, Macro = 0f };

        public static MatDef Def(string name) => Defs.FirstOrDefault(d => d.Name == name);

        public static string PathOf(MatDef d)
        {
            if (d.Texture != null && !d.Texture.StartsWith("proc:"))
            {
                var src = ArtCatalog.Find(d.Texture);
                if (src != null) return $"{src.Folder}/{d.Name}.mat";
            }
            return $"{LibraryDir}/{d.Name}.mat";
        }

        public static Material Get(string name)
        {
            var d = Def(name);
            var m = d != null ? AssetDatabase.LoadAssetAtPath<Material>(PathOf(d)) : null;
            if (m == null) Debug.LogWarning($"[GateRush][Art] material {name} missing: run Tools > Art > Build Material Library");
            return m;
        }

        [MenuItem("Tools/Art/Build Material Library")]
        private static void Menu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            Directory.CreateDirectory(LibraryDir);
            var macro = AssetDatabase.LoadAssetAtPath<Texture2D>(MacroNoise);
            int built = 0;
            var missing = new List<string>();
            foreach (var d in Defs)
            {
                if (!Build(d, macro)) missing.Add(d.Name);
                else built++;
            }
            AssetDatabase.SaveAssets();
            return $"built {built} materials" + (missing.Count > 0 ? $"; missing textures for {string.Join(", ", missing)}" : "");
        }

        private static bool Build(MatDef d, Texture2D macro)
        {
            var shader = Shader.Find(d.Shader);
            if (shader == null) throw new System.Exception("shader not found: " + d.Shader);
            string path = PathOf(d);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = d.Name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.shaderKeywords = new string[0];

            if (d.Shader == MatDef.Led)
            {
                m.SetColor("_BaseColor", d.Tint);
                m.SetFloat("_Intensity", d.LedIntensity);
                // An LED screen can show a procedural pattern (T3 stadium banner); otherwise plain white.
                Texture2D pattern = d.Texture != null && d.Texture.StartsWith("proc:")
                    ? LoadAny(ArtProcedural.Dir(d.Texture.Substring(5)), d.Texture.Substring(5) + "_Albedo") : null;
                m.SetTexture("_BaseMap", pattern);
                EditorUtility.SetDirty(m);
                return true;
            }

            Texture2D albedo = null, normal = null, orm = null;
            float tile = d.TileMeters;
            if (d.Texture != null)
            {
                string dir, name;
                if (d.Texture.StartsWith("proc:"))
                {
                    name = d.Texture.Substring(5);
                    dir = ArtProcedural.Dir(name);
                }
                else
                {
                    var src = ArtCatalog.Find(d.Texture);
                    dir = src.Folder;
                    name = src.Name;
                    if (tile <= 0f) tile = src.TileMeters;
                }
                albedo = LoadAny(dir, name + "_Albedo");
                normal = LoadAny(dir, name + "_Normal");
                orm = LoadAny(dir, name + "_ORM");
                if (albedo == null && normal == null) return false;
            }
            if (tile <= 0f) tile = 1f;

            m.SetFloat("_WorkflowMode", 1f);
            m.SetTexture("_BaseMap", albedo);
            m.SetTextureScale("_BaseMap", Vector2.one / tile);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", d.Tint);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", d.NormalScale);
            m.SetTexture("_MetallicGlossMap", orm);
            m.SetFloat("_Smoothness", d.Smoothness);
            m.SetFloat("_Metallic", d.Metallic);
            m.SetFloat("_OcclusionStrength", d.Occlusion);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetKeyword(new LocalKeyword(m.shader, "_NORMALMAP"), normal != null);
            m.SetKeyword(new LocalKeyword(m.shader, "_METALLICSPECGLOSSMAP"), orm != null);

            bool useMacro = d.Macro > 0f && macro != null;
            m.SetTexture("_DetailAlbedoMap", useMacro ? macro : null);
            m.SetTextureScale("_DetailAlbedoMap", Vector2.one * (tile / Mathf.Max(1f, d.MacroMeters)));
            m.SetFloat("_DetailAlbedoMapScale", d.Macro);
            m.SetKeyword(new LocalKeyword(m.shader, "_DETAIL_SCALED"), useMacro);

            bool emissive = d.Emission.maxColorComponent > 0f;
            m.SetColor("_EmissionColor", d.Emission);
            m.SetKeyword(new LocalKeyword(m.shader, "_EMISSION"), emissive);
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            ConfigureSurface(m, d);
            EditorUtility.SetDirty(m);
            return true;
        }

        /// <summary>What URP's Lit inspector would set for opaque / alpha-clipped / transparent surfaces (Env Lit has no custom inspector).</summary>
        public static void ConfigureSurface(Material m, MatDef d)
        {
            m.SetFloat("_Surface", d.Transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", d.AlphaClip ? 1f : 0f);
            m.SetFloat("_Cutoff", d.Cutoff);
            m.SetFloat("_Cull", d.DoubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.SetFloat("_ReceiveShadows", 1f);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.SetKeyword(new LocalKeyword(m.shader, "_ALPHATEST_ON"), d.AlphaClip);
            m.SetKeyword(new LocalKeyword(m.shader, "_SURFACE_TYPE_TRANSPARENT"), d.Transparent);
            if (d.Transparent)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("DepthOnly", false);
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.SetOverrideTag("RenderType", d.AlphaClip ? "TransparentCutout" : "Opaque");
                m.renderQueue = d.AlphaClip ? (int)RenderQueue.AlphaTest : -1;
                m.SetShaderPassEnabled("DepthOnly", true);
            }
        }

        public static Texture2D LoadAny(string dir, string baseName)
        {
            foreach (var ext in new[] { ".jpg", ".png", ".tga" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{baseName}{ext}");
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>A variant of <paramref name="master"/> for a surface whose UVs are not metric (e.g. Unity's Plane: 0–1 over 10 m × scale).</summary>
        public static Material TiledVariant(Material master, string assetPath, float surfaceMeters, float tileMeters)
        {
            var v = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (v == null)
            {
                v = new Material(master);
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                AssetDatabase.CreateAsset(v, assetPath);
            }
            v.parent = master;
            float repeats = surfaceMeters / Mathf.Max(0.01f, tileMeters);
            v.SetTextureScale("_BaseMap", Vector2.one * repeats);
            var macroScale = master.GetTextureScale("_DetailAlbedoMap");
            v.SetTextureScale("_DetailAlbedoMap", macroScale);
            EditorUtility.SetDirty(v);
            return v;
        }
    }
}
