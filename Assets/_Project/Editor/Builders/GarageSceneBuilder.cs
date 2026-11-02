using System.Collections.Generic;
using System.IO;
using GateRush.EditorTools.Art;
using GateRush.Game;
using GateRush.Garage;
using GateRush.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/Garage.unity (T7): a workshop corner — workbench with a blue cutting mat, a
    /// pegboard with tools, shelves with LiPo bags and spare props, a soldering station, a radio and a desk lamp, warm
    /// practical lights — the quad on a turntable anchor on the mat, an orbit camera with depth of field, and the
    /// screen-space garage UI (<see cref="GarageController"/>). Realtime lights only (nothing to bake).
    /// </summary>
    public static class GarageSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Garage.unity";
        public const string VolumePath = ArtPass.VolumeDir + "/VP_Garage.asset";
        private static readonly Vector3 MatCentre = new Vector3(0f, 0.9f, 0.42f);

        /// <summary>Materials for the procedural quad (garage and track scenes).</summary>
        public static void AssignModelMaterials(SerializedProperty p)
        {
            void Set(string field, string mat) => p.FindPropertyRelative(field).objectReferenceValue = MaterialLibrary.Get(mat);
            Set("Carbon", "M_CarbonFiber");
            Set("Metal", "M_Aluminium_Brushed");
            Set("Bell", "M_Metal_Anodized");
            Set("Copper", "M_Copper");
            Set("Prop", "M_Prop_Front");
            Set("Lipo", "M_Lipo");
            Set("Plastic", "M_Plastic_Black");
            Set("Led", "M_LED_Gate");
            Set("Lens", "M_Glass_Night");
            Set("Gold", "M_Garage_Gold");
            Set("Pad", "M_Rubber_Black");
        }

        [MenuItem("GateRush/Builders/Build Garage Scene")]
        public static string Build()
        {
            KitTextures.CuttingMat();
            KitTextures.Pegboard();
            AssetDatabase.Refresh();
            MaterialLibrary.BuildAll();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.3f, 0.28f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.2f, 0.18f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.09f, 0.08f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            var env = new GameObject("Workshop").transform;
            BuildRoom(env);
            BuildBench(env);
            BuildPegboard(env);
            BuildShelves(env);
            BuildBenchProps(env);
            BuildLights(env);

            var turntable = new GameObject("Turntable").transform;
            turntable.position = MatCentre + Vector3.up * 0.0035f;

            var cam = BootSceneBuilder.CreateCamera(Vector3.zero, 32f, out var rig);
            cam.transform.SetParent(null, true);
            Object.DestroyImmediate(rig.gameObject);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.045f, 0.04f);
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 30f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            cam.allowHDR = true;
            var orbit = cam.gameObject.AddComponent<GarageCamera>();
            var oso = new SerializedObject(orbit);
            oso.FindProperty("target").objectReferenceValue = turntable;
            oso.ApplyModifiedPropertiesWithoutUndo();

            var volume = BuildVolume();

            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();

            var canvasGo = new GameObject("Garage UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var controller = canvasGo.AddComponent<GarageController>();
            var so = new SerializedObject(controller);
            so.FindProperty("canvas").objectReferenceValue = canvasGo.transform;
            so.FindProperty("turntable").objectReferenceValue = turntable;
            so.FindProperty("orbit").objectReferenceValue = orbit;
            so.FindProperty("postVolume").objectReferenceValue = volume;
            so.FindProperty("moiMaterial").objectReferenceValue = MaterialLibrary.Get("M_Garage_Moi");
            so.FindProperty("tracks").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogBuilder.TrackCatalogPath);
            AssignModelMaterials(so.FindProperty("materials"));
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            return ScenePath;
        }

        // ---------- room ----------

        private static GameObject Box(Transform parent, string name, Vector3 centre, Vector3 size, string material, Quaternion? rot = null, bool shadows = true)
        {
            var mesh = ProcMesh.Box(size, centre, false, "Garage " + name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, rot ?? Quaternion.identity);
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.Get(material);
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        private static GameObject Cyl(Transform parent, string name, Vector3 centre, float radius, float height, string material, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, rot ?? Quaternion.identity);
            go.transform.localScale = new Vector3(radius * 2f, height, radius * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = MeshAsset("Garage_Cylinder", () => Object.Instantiate(QuadModelBuilder.Cylinder));
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get(material);
            return go;
        }

        private static Mesh MeshAsset(string name, System.Func<Mesh> build) => ArtPass.MeshAsset(name, build);

        private static void BuildRoom(Transform env)
        {
            Box(env, "Floor", new Vector3(0f, -0.01f, 0f), new Vector3(8f, 0.02f, 8f), "M_Concrete_PaintedFloor");
            Box(env, "Back Wall", new Vector3(0f, 1.5f, 1.08f), new Vector3(8f, 3f, 0.1f), "M_Brick_Factory");
            Box(env, "Side Wall", new Vector3(-2.2f, 1.5f, 0f), new Vector3(0.1f, 3f, 8f), "M_Concrete_Wall");
            Box(env, "Ceiling", new Vector3(0f, 3f, 0f), new Vector3(8f, 0.05f, 8f), "M_Concrete_Wall");
            // A window-ish bright panel far right to give the background some light shapes for the bokeh.
            Box(env, "Window", new Vector3(1.9f, 1.75f, 1.025f), new Vector3(1.1f, 0.8f, 0.02f), "M_Lamp_Emitter", null, false);
        }

        private static void BuildBench(Transform env)
        {
            Box(env, "Bench Top", new Vector3(0f, 0.875f, 0.55f), new Vector3(2.8f, 0.05f, 0.9f), "M_Wood_Tabletop");
            foreach (float x in new[] { -1.3f, 1.3f })
                foreach (float z in new[] { 0.16f, 0.94f })
                    Box(env, "Bench Leg", new Vector3(x, 0.425f, z), new Vector3(0.06f, 0.85f, 0.06f), "M_Steel_Truss");
            Box(env, "Bench Shelf", new Vector3(0f, 0.25f, 0.55f), new Vector3(2.7f, 0.03f, 0.8f), "M_Wood_Plywood");
            Box(env, "Cutting Mat", MatCentre + Vector3.up * 0.0015f, new Vector3(0.9f, 0.003f, 0.6f), "M_Garage_CuttingMat");
        }

        private static void BuildPegboard(Transform env)
        {
            var board = Box(env, "Pegboard", new Vector3(-0.25f, 1.45f, 1.02f), new Vector3(1.6f, 0.9f, 0.012f), "M_Garage_Pegboard");
            board.name = "Pegboard";
            var tools = new GameObject("Tools").transform;
            tools.SetParent(env, false);
            float z = 1.0f;
            // Screwdrivers: handle + shaft.
            for (int i = 0; i < 6; i++)
            {
                float x = -0.9f + i * 0.07f;
                Cyl(tools, "Screwdriver", new Vector3(x, 1.72f, z - 0.02f), 0.012f, 0.09f, i % 2 == 0 ? "M_Garage_ToolRed" : "M_Garage_ToolYellow");
                Cyl(tools, "Shaft", new Vector3(x, 1.6f, z - 0.02f), 0.0025f, 0.15f, "M_Steel_Brushed");
            }
            // Pliers and cutters (two angled bars each).
            for (int i = 0; i < 3; i++)
            {
                float x = -0.4f + i * 0.13f;
                foreach (float s in new[] { -1f, 1f })
                    Box(tools, "Plier", new Vector3(x + s * 0.012f, 1.6f, z - 0.015f), new Vector3(0.012f, 0.17f, 0.008f), i == 1 ? "M_Garage_ToolYellow" : "M_Garage_ToolRed", Quaternion.Euler(0f, 0f, s * 8f));
            }
            // Hex keys, a tape roll and a hanging spare frame plate.
            for (int i = 0; i < 4; i++) Box(tools, "Hex Key", new Vector3(0.05f + i * 0.04f, 1.62f, z - 0.01f), new Vector3(0.006f, 0.12f - i * 0.015f, 0.006f), "M_Steel_Brushed");
            Cyl(tools, "Tape", new Vector3(0.35f, 1.28f, z - 0.03f), 0.05f, 0.05f, "M_Fabric_Black", Quaternion.Euler(90f, 0f, 0f));
            Box(tools, "Spare Plate", new Vector3(0.3f, 1.62f, z - 0.01f), new Vector3(0.22f, 0.15f, 0.004f), "M_CarbonFiber", Quaternion.Euler(0f, 0f, 12f));
            Box(tools, "Zip Ties", new Vector3(-0.55f, 1.25f, z - 0.02f), new Vector3(0.18f, 0.03f, 0.02f), "M_Plastic_White");
        }

        private static void BuildShelves(Transform env)
        {
            var shelf = new GameObject("Shelves").transform;
            shelf.SetParent(env, false);
            for (int i = 0; i < 3; i++)
            {
                float y = 1.15f + i * 0.33f;
                Box(shelf, "Shelf", new Vector3(1.05f, y, 0.9f), new Vector3(0.7f, 0.02f, 0.28f), "M_Wood_Plywood");
                for (int k = 0; k < 4; k++)
                {
                    float x = 0.8f + k * 0.16f;
                    if (i == 0) Box(shelf, "LiPo Bag", new Vector3(x, y + 0.05f, 0.9f), new Vector3(0.12f, 0.08f, 0.18f), k % 2 == 0 ? "M_Fabric_CanvasRed" : "M_Fabric_Black");
                    else if (i == 1) Box(shelf, "LiPo", new Vector3(x, y + 0.025f, 0.88f), new Vector3(0.035f, 0.035f, 0.075f), "M_Lipo", Quaternion.Euler(0f, k * 11f, 0f));
                    else Cyl(shelf, "Prop Pack", new Vector3(x, y + 0.015f, 0.9f), 0.06f, 0.025f, k % 2 == 0 ? "M_Prop_Front" : "M_Plastic_ConeOrange");
                }
            }
            foreach (float x in new[] { 0.71f, 1.39f })
                Box(shelf, "Shelf Bracket", new Vector3(x, 1.48f, 0.98f), new Vector3(0.02f, 0.75f, 0.12f), "M_Steel_Truss");
        }

        private static void BuildBenchProps(Transform env)
        {
            var props = new GameObject("Bench Props").transform;
            props.SetParent(env, false);
            float top = 0.9f;
            // Soldering station: unit, iron holder, sponge.
            Box(props, "Soldering Station", new Vector3(-0.85f, top + 0.045f, 0.75f), new Vector3(0.18f, 0.09f, 0.15f), "M_Plastic_Grey");
            Box(props, "Station Display", new Vector3(-0.85f, top + 0.06f, 0.674f), new Vector3(0.08f, 0.03f, 0.004f), "M_LED_Red", null, false);
            Cyl(props, "Iron Holder", new Vector3(-0.6f, top + 0.06f, 0.78f), 0.022f, 0.12f, "M_Steel_Brushed", Quaternion.Euler(35f, 0f, 0f));
            Box(props, "Sponge", new Vector3(-0.6f, top + 0.008f, 0.68f), new Vector3(0.06f, 0.016f, 0.05f), "M_Plastic_ConeOrange");
            // Radio transmitter: body, gimbals, antenna.
            var radio = new GameObject("Radio").transform;
            radio.SetParent(props, false);
            radio.SetPositionAndRotation(new Vector3(0.78f, top + 0.03f, 0.32f), Quaternion.Euler(0f, -25f, 0f));
            Box(radio, "Body", radio.position, new Vector3(0.17f, 0.05f, 0.11f), "M_Plastic_Black", radio.rotation);
            foreach (float s in new[] { -1f, 1f })
                Cyl(radio, "Stick", radio.position + radio.rotation * new Vector3(s * 0.05f, 0.04f, 0f), 0.004f, 0.035f, "M_Steel_Brushed");
            Cyl(radio, "Antenna", radio.position + radio.rotation * new Vector3(0.07f, 0.06f, 0.05f), 0.005f, 0.09f, "M_Plastic_Black", radio.rotation * Quaternion.Euler(-30f, 0f, 0f));
            // Spare batteries and props on the bench.
            Box(props, "Spare LiPo", new Vector3(-0.32f, top + 0.018f, 0.25f), new Vector3(0.035f, 0.036f, 0.075f), "M_Lipo", Quaternion.Euler(0f, 20f, 0f));
            Box(props, "Spare LiPo", new Vector3(-0.27f, top + 0.018f, 0.27f), new Vector3(0.035f, 0.036f, 0.075f), "M_Lipo", Quaternion.Euler(0f, -8f, 0f));
            for (int i = 0; i < 3; i++)
                Cyl(props, "Spare Prop", new Vector3(0.35f + i * 0.06f, top + 0.002f, 0.66f), 0.055f, 0.003f, "M_Prop_Front");
            // Desk lamp: base, arm, head (its spot light is added with the lights).
            Cyl(props, "Lamp Base", new Vector3(0.95f, top + 0.01f, 0.8f), 0.07f, 0.02f, "M_Plastic_Black");
            Box(props, "Lamp Arm", new Vector3(0.88f, top + 0.25f, 0.72f), new Vector3(0.015f, 0.5f, 0.015f), "M_Plastic_Black", Quaternion.Euler(-20f, 0f, 25f));
            Box(props, "Lamp Head", new Vector3(0.7f, top + 0.47f, 0.62f), new Vector3(0.12f, 0.06f, 0.08f), "M_Plastic_Black", Quaternion.Euler(30f, -35f, 0f));
        }

        private static void BuildLights(Transform env)
        {
            var lights = new GameObject("Lights").transform;
            lights.SetParent(env, false);
            Light L(string name, LightType type, Vector3 pos, Color c, float intensity, float range, bool shadows, Quaternion? rot = null, float angle = 60f)
            {
                var go = new GameObject(name);
                go.transform.SetParent(lights, false);
                go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
                var l = go.AddComponent<Light>();
                l.type = type;
                l.color = c;
                l.intensity = intensity;
                l.range = range;
                l.spotAngle = angle;
                l.innerSpotAngle = angle * 0.6f;
                l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                l.shadowNearPlane = 0.05f;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                return l;
            }
            // Desk lamp key light on the mat (shadows), warm ceiling fill, cool rim from the window, bench strip.
            L("Desk Lamp", LightType.Spot, new Vector3(0.68f, 1.36f, 0.6f), new Color(1f, 0.86f, 0.68f), 3.2f, 3f, true,
                Quaternion.LookRotation(MatCentre - new Vector3(0.68f, 1.36f, 0.6f)), 75f);
            L("Ceiling", LightType.Point, new Vector3(-0.2f, 2.6f, -0.2f), new Color(1f, 0.78f, 0.55f), 2.2f, 5f, false);
            L("Window Rim", LightType.Point, new Vector3(1.7f, 1.7f, 0.85f), new Color(0.6f, 0.75f, 1f), 1.6f, 3f, false);
            L("Pegboard Strip", LightType.Point, new Vector3(-0.25f, 2.05f, 0.85f), new Color(1f, 0.8f, 0.6f), 1.2f, 1.6f, false);
            var sun = L("Fill", LightType.Directional, Vector3.zero, new Color(1f, 0.9f, 0.8f), 0.25f, 0f, false, Quaternion.Euler(55f, 150f, 0f));
            sun.shadows = LightShadows.None;
        }

        private static Volume BuildVolume()
        {
            Directory.CreateDirectory(ArtPass.VolumeDir);
            AssetDatabase.DeleteAsset(VolumePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var adj = Add<ColorAdjustments>();
            adj.postExposure.Override(0.35f);
            adj.contrast.Override(10f);
            adj.saturation.Override(8f);
            var bloom = Add<Bloom>();
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(1f);
            bloom.scatter.Override(0.6f);
            var dof = Add<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(0.6f);
            dof.focalLength.Override(60f);
            dof.aperture.Override(2.2f);
            var v = Add<Vignette>();
            v.intensity.Override(0.25f);
            v.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Post Volume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
            return vol;
        }
    }
}
