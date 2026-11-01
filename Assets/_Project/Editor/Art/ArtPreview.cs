using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Tools > Art > Render Material Sheet: every library material on a sphere (10 columns, catalogue order) under
    /// the Field HDRI, saved to Docs/screenshots/material_library.jpg. Uses a throw-away scene.
    /// </summary>
    public static class ArtPreview
    {
        public const string SheetPath = "Docs/screenshots/material_library.jpg";
        private const int Columns = 10;

        [MenuItem("Tools/Art/Render Material Sheet")]
        private static void Menu() => Debug.Log(RenderSheet());

        public static string RenderSheet(string hdriId = "hilly_terrain_01_puresky")
        {
            ArtJobs.RequireCleanScenes();
            var mats = MaterialLibrary.Defs.Where(d => d.Shader == MatDef.EnvLit).Select(d => MaterialLibrary.Get(d.Name)).Where(m => m != null).ToList();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var sky = EnvironmentAssets.Sky(hdriId);
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = EnvironmentAssets.SkyCube(hdriId);
                DynamicGI.UpdateEnvironment();

                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.6f;
                sun.color = new Color(1f, 0.9f, 0.78f);
                sun.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

                int rows = Mathf.CeilToInt(mats.Count / (float)Columns);
                const float spacing = 1.2f;
                for (int i = 0; i < mats.Count; i++)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(s.GetComponent<Collider>());
                    s.transform.position = new Vector3((i % Columns - (Columns - 1) * 0.5f) * spacing, ((rows - 1) * 0.5f - i / Columns) * spacing, 0f);
                    s.GetComponent<Renderer>().sharedMaterial = mats[i];
                }

                float halfHeight = rows * spacing * 0.5f + 0.3f;
                float fov = 30f;
                float dist = halfHeight / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                var shot = new SceneShots.Bookmark("sheet", new Vector3(0f, 0f, -dist), Vector3.zero, fov);
                var tex = SceneShots.Render(null, shot);
                Directory.CreateDirectory(Path.GetDirectoryName(SheetPath));
                File.WriteAllBytes(SheetPath, tex.EncodeToJPG(90));
                Object.DestroyImmediate(tex);
                return $"{SheetPath}: {mats.Count} materials, order: " + string.Join(", ", mats.Select(m => m.name));
            }
            finally
            {
                ArtJobs.ReturnToBoot();
            }
        }
    }
}
