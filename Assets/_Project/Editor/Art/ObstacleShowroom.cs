using System.Collections.Generic;
using System.IO;
using GateRush.Race;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Tools > Art > Obstacle Showroom: a scene with every kit obstacle in a row and every prop behind them,
    /// on asphalt under a low sun (named in the hierarchy; screenshots are per obstacle), with the Field's post-processing so the LEDs bloom. Two gates show the race
    /// highlight (next = green pulse, upcoming = orange). <see cref="Capture"/> renders review shots to Docs/screenshots/T2.
    /// </summary>
    public static class ObstacleShowroom
    {
        public const string ScenePath = "Assets/_Project/Scenes/Obstacle_Showroom.unity";
        public const string ShotDir = "Docs/screenshots/T2";
        private const float Spacing = 9f;

        [MenuItem("Tools/Art/Obstacle Showroom/Build")]
        private static void BuildMenu() => Debug.Log(Build());

        [MenuItem("Tools/Art/Obstacle Showroom/Capture Screenshots")]
        private static void CaptureMenu() => Debug.Log(Capture());

        public static string Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.92f, 0.82f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(32f, -40f, 0f);
            RenderSettings.sun = sun;
            const string skyPath = "Assets/_Project/Art/Materials/ShowroomSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural")) { name = "ShowroomSky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunSize", 0.03f);
            sky.SetFloat("_AtmosphereThickness", 1.1f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;

            var ground = new GameObject("Ground");
            var groundSize = new Vector3(150f, 0.1f, 60f);
            ground.transform.localScale = groundSize;
            ground.transform.position = new Vector3(0f, -0.05f, 6f);
            ground.AddComponent<MeshFilter>().sharedMesh = ArtPass.MeshAsset("ShowroomGround", () => ProcMesh.Box(groundSize, Vector3.zero, false));
            ground.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("M_Asphalt_PitLane");
            ground.AddComponent<BoxCollider>();

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Project/Art/Volumes/VP_Field.asset");
            if (profile != null)
            {
                var v = new GameObject("Post").AddComponent<Volume>();
                v.isGlobal = true;
                v.sharedProfile = profile;
            }

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 1000f;
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 9f, -30f), Quaternion.LookRotation(new Vector3(0f, -7.5f, 30f)));

            var row = new List<string>(ObstacleKit.GateTypes);
            float x0 = -(row.Count - 1) * Spacing / 2f;
            for (int i = 0; i < row.Count; i++) Place(row[i], new Vector3(x0 + i * Spacing, 0f, 0f), i);
            var props = ObstacleKit.PropTypes;
            float p0 = -(props.Length - 1) * Spacing / 2f;
            for (int i = 0; i < props.Length; i++) Place(props[i], new Vector3(p0 + i * Spacing, 0f, 14f), -1);

            EditorSceneManager.SaveScene(scene, ScenePath);
            return $"showroom: {row.Count} obstacles, {props.Length} props → {ScenePath}";
        }

        private static void Place(string type, Vector3 pos, int index)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ObstacleKit.PrefabPath(type));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = pos;

        }

        public static string Capture()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(ShotDir);
            var cam = GameObject.Find("Main Camera").GetComponent<Camera>();
            // Race highlight (property blocks are not saved with the scene): Single = next (green), Double = upcoming.
            foreach (var (name, state) in new[] { ("Obstacle_Single", GateVisualState.Next), ("Obstacle_Double", GateVisualState.Upcoming), ("Obstacle_Ladder", GateVisualState.Passed) })
            {
                var go = GameObject.Find(name);
                if (go != null) foreach (var gate in go.GetComponentsInChildren<Gate>()) gate.SetVisualState(state);
            }
            var shots = new List<SceneShots.Bookmark>
            {
                new SceneShots.Bookmark("overview", new Vector3(0f, 12f, -34f), new Vector3(0f, 1f, 4f), 42f),
                new SceneShots.Bookmark("props", new Vector3(0f, 6f, 2f), new Vector3(0f, 0.5f, 14f), 50f),
            };
            float x0 = -(ObstacleKit.GateTypes.Length - 1) * Spacing / 2f;
            for (int i = 0; i < ObstacleKit.GateTypes.Length; i++)
            {
                var c = new Vector3(x0 + i * Spacing, 1.6f, 0f);
                shots.Add(new SceneShots.Bookmark("obstacle_" + ObstacleKit.GateTypes[i], c + new Vector3(-4.2f, 1.2f, -7f), c, 45f));
            }
            int n = 0;
            foreach (var bm in shots) n += Shot(cam, bm);

            // LOD comparison on the cube: LOD0 / LOD1 / LOD2 from the same view.
            var cube = GameObject.Find("Obstacle_Cube(Clone)") ?? GameObject.Find("Obstacle_Cube");
            var group = cube != null ? cube.GetComponent<LODGroup>() : null;
            if (group != null)
            {
                var c = cube.transform.position + Vector3.up * 1.3f;
                for (int lod = 0; lod < 3; lod++)
                {
                    group.ForceLOD(lod);
                    n += Shot(cam, new SceneShots.Bookmark($"lod{lod}_Cube", c + new Vector3(-3f, 1f, -4.5f), c, 50f));
                }
                group.ForceLOD(-1);
            }
            return $"{n} showroom shots → {ShotDir}";
        }

        private static int Shot(Camera cam, SceneShots.Bookmark bm)
        {
            var tex = SceneShots.Render(cam, bm);
            File.WriteAllBytes($"{ShotDir}/{bm.Name}.jpg", tex.EncodeToJPG(90));
            Object.DestroyImmediate(tex);
            return 1;
        }
    }
}
