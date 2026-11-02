using GateRush.Track;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Builds track prop prefabs (pivot at the base centre, colliders on TrackGeometry) and the
    /// <see cref="TrackPrefabs"/> asset that maps JSON type names to gate and prop prefabs.
    /// </summary>
    public static class PropBuilder
    {
        public const string PrefabDir = "Assets/_Project/Prefabs/Props";
        public const string TrackPrefabsPath = "Assets/_Project/Data/TrackPrefabs.asset";

        [MenuItem("GateRush/Builders/Build Props + Track Prefabs")]
        public static TrackPrefabs BuildAll()
        {
            GateBuilder.BuildAll();
            System.IO.Directory.CreateDirectory(PrefabDir);
            int layer = LayerMask.NameToLayer(TrackSceneBuilder.TrackLayerName);

            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(TrackPrefabsPath);
            if (prefabs == null)
            {
                prefabs = ScriptableObject.CreateInstance<TrackPrefabs>();
                AssetDatabase.CreateAsset(prefabs, TrackPrefabsPath);
            }
            prefabs.Gates.Clear();
            prefabs.Props.Clear();
            foreach (GateKind kind in System.Enum.GetValues(typeof(GateKind)))
                prefabs.Gates.Add(new TrackPrefabs.Entry { Type = kind.ToString(), Prefab = GateBuilder.Load(kind) });

            Add(prefabs, "Crate", PrimitiveType.Cube, new Vector3(1f, 1f, 1f), Materials.Lit("Crate", new Color(0.62f, 0.45f, 0.26f)), layer);
            Add(prefabs, "Barrel", PrimitiveType.Cylinder, new Vector3(0.6f, 0.9f, 0.6f), Materials.Lit("Barrel", new Color(0.75f, 0.2f, 0.12f)), layer);
            Add(prefabs, "Pillar", PrimitiveType.Cube, new Vector3(0.6f, 4f, 0.6f), Materials.Lit("Pillar", new Color(0.75f, 0.72f, 0.68f)), layer);
            Add(prefabs, "Wall", PrimitiveType.Cube, new Vector3(4f, 2f, 0.3f), Materials.Lit("Wall", new Color(0.45f, 0.48f, 0.52f)), layer);
            // 20 ft shipping container: 2.44 wide, 2.59 high, 6.06 long (along Z).
            var containerSize = new Vector3(2.44f, 2.59f, 6.06f);
            Add(prefabs, "Container", PrimitiveType.Cube, containerSize, Materials.Lit("ContainerBlue", new Color(0.16f, 0.32f, 0.6f)), layer);
            Add(prefabs, "ContainerRed", PrimitiveType.Cube, containerSize, Materials.Lit("ContainerRed", new Color(0.62f, 0.16f, 0.12f)), layer);
            Add(prefabs, "ContainerGreen", PrimitiveType.Cube, containerSize, Materials.Lit("ContainerGreen", new Color(0.2f, 0.45f, 0.25f)), layer);
            RouteKitBuilder.Build(prefabs);

            EditorUtility.SetDirty(prefabs);
            AssetDatabase.SaveAssets();
            return prefabs;
        }

        private static void Add(TrackPrefabs prefabs, string type, PrimitiveType shape, Vector3 size, Material mat, int layer)
        {
            var root = new GameObject("Prop_" + type);
            var body = GameObject.CreatePrimitive(shape);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            // Unity's cylinder is 2 units tall.
            float h = shape == PrimitiveType.Cylinder ? size.y * 0.5f : size.y;
            body.transform.localScale = new Vector3(size.x, h, size.z);
            body.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            body.layer = layer;
            body.isStatic = true;
            var r = body.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // LOD: culled when smaller than 0.6% of screen height.
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.006f, new[] { r }) });
            lod.RecalculateBounds();

            string path = $"{PrefabDir}/Prop_{type}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            prefabs.Props.Add(new TrackPrefabs.Entry { Type = type, Prefab = prefab });
        }
    }

    /// <summary>Shared material factory for builders (URP Lit/Unlit assets under Art/Materials, created once, colour refreshed).</summary>
    public static class Materials
    {
        public const string Dir = "Assets/_Project/Art/Materials";

        public static Material Lit(string name, Color color, float smoothness = 0.1f)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Lit with emission, for light panels and signs (no realtime light cost).</summary>
        public static Material Emissive(string name, Color color, float intensity)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material Unlit(string name, Color color, bool transparent)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material LoadOrCreate(string name, string shader)
        {
            System.IO.Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
