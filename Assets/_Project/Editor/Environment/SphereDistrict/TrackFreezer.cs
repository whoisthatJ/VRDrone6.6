using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GateRush.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GateRush.EditorTools.SphereDistrict
{
    /// <summary>
    /// Phase 3 P1a: freezes the first Sphere District as its own scene, <c>Track_Expo_v1</c>, before the environment and
    /// route upgrades. The copied scene points at its own copies of every generated asset the upgrades may rewrite
    /// (meshes, materials, procedural textures, skies, volume profiles) under <see cref="Folder"/>; downloaded CC0
    /// textures, HDR skies, models, shaders, scripts and the obstacle kit stay shared. Its three tracks keep their ids
    /// (so their records, ghosts and online boards stay with them) and move to the environment <c>expo_v1</c> as frozen
    /// JSON. Refuses to run twice: v1 is never modified after this.
    /// </summary>
    public static class TrackFreezer
    {
        public const string SourceScene = "Assets/_Project/Scenes/Track_Expo.unity";
        public const string FrozenScene = "Assets/_Project/Scenes/Track_Expo_v1.unity";
        public const string Folder = "Assets/_Project/Tracks/Track_expo_v1";
        public const string Environment = "expo_v1";
        public static readonly (string id, string name)[] Tracks =
        {
            ("expo-astana", "Expo Classic Sprint"), ("expo-pro", "Expo Classic Pro"), ("expo-free", "Expo Classic Free Flight"),
        };

        private static readonly string[] CopiedExtensions = { ".asset", ".mat", ".png", ".exr" };

        public static string TrackJson(string id) => $"{Folder}/Data/{id}.json";

        /// <summary>Assets the upgrades may regenerate: generated art (meshes, materials, procedural textures, volumes, skies).</summary>
        public static bool ShouldCopy(string path) =>
            path.StartsWith("Assets/_Project/Art/") && CopiedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        public static string Freeze()
        {
            if (File.Exists(FrozenScene)) return "Track_Expo_v1 already exists: it is frozen and is never rebuilt.";
            var log = new StringBuilder();

            // 1. Copies of the generated assets, keeping their sub-folders (no name clashes).
            var map = new Dictionary<string, string>();
            foreach (var path in AssetDatabase.GetDependencies(SourceScene, true))
            {
                if (!ShouldCopy(path)) continue;
                string dst = Folder + "/" + path.Substring("Assets/_Project/".Length);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (!AssetDatabase.CopyAsset(path, dst)) throw new IOException("copy failed: " + path);
                map[path] = dst;
            }
            AssetDatabase.Refresh();
            log.Append($"copied {map.Count} assets; ");

            // 2. Copies point at copies (materials at their textures, volume profiles at their components).
            var guidMap = map.ToDictionary(kv => AssetDatabase.AssetPathToGUID(kv.Key), kv => kv.Value);
            int remapped = 0;
            foreach (var dst in map.Values)
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(dst))
                    if (o != null) remapped += Remap(o, guidMap);
            AssetDatabase.SaveAssets();

            // 3. The scene itself.
            if (!AssetDatabase.CopyAsset(SourceScene, FrozenScene)) throw new IOException("scene copy failed");
            var scene = EditorSceneManager.OpenScene(FrozenScene, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                    if (c != null) remapped += Remap(c, guidMap);
            if (RenderSettings.skybox != null && guidMap.TryGetValue(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(RenderSettings.skybox)), out var sky))
            {
                RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(sky);
                remapped++;
            }
            var controller = Object.FindAnyObjectByType<GateRush.Game.TrackSceneController>(FindObjectsInactive.Include);
            var so = new SerializedObject(controller);
            so.FindProperty("environment").stringValue = Environment;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Append($"remapped {remapped} references; ");
            var build = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!build.Exists(b => b.path == FrozenScene)) build.Add(new EditorBuildSettingsScene(FrozenScene, true));
            EditorBuildSettings.scenes = build.ToArray();

            // 4. The tracks: frozen JSON under the v1 folder, environment expo_v1, "Classic" names.
            Directory.CreateDirectory(Folder + "/Data");
            foreach (var (id, name) in Tracks)
            {
                var data = TrackData.FromJson(File.ReadAllText($"{Builders.CourseLibrary.TrackDir}/{id}.json"));
                data.Environment = Environment;
                data.Name = name;
                File.WriteAllText(TrackJson(id), data.ToJson());
                // The generated originals go: RouteLibrary now writes the upgraded routes under new ids.
                AssetDatabase.DeleteAsset($"{Builders.CourseLibrary.TrackDir}/{id}.json");
                AssetDatabase.DeleteAsset($"{Builders.RouteLibrary.DefinitionDir}/{id}.asset");
            }
            AssetDatabase.Refresh();

            // 5. What the frozen scene still shares (must not be anything the upgrades regenerate).
            var leaks = AssetDatabase.GetDependencies(FrozenScene, true).Where(ShouldCopy).Where(p => !p.StartsWith(Folder)).ToList();
            log.Append(leaks.Count == 0 ? "no shared generated assets left." : $"{leaks.Count} still shared: {string.Join(", ", leaks.Take(10))}");
            return log.ToString();
        }

        /// <summary>Points every object reference of <paramref name="o"/> that has a copy at the copy.</summary>
        private static int Remap(Object o, Dictionary<string, string> guidMap)
        {
            int n = 0;
            var so = new SerializedObject(o);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                var target = it.objectReferenceValue;
                string path = AssetDatabase.GetAssetPath(target);
                if (string.IsNullOrEmpty(path) || !guidMap.TryGetValue(AssetDatabase.AssetPathToGUID(path), out var copy)) continue;
                // Same object inside the copy: the main asset, or the sub-asset with the same name and type.
                Object replacement = AssetDatabase.IsMainAsset(target)
                    ? AssetDatabase.LoadMainAssetAtPath(copy)
                    : AssetDatabase.LoadAllAssetsAtPath(copy).FirstOrDefault(a => a != null && a.GetType() == target.GetType() && a.name == target.name);
                if (replacement == null) continue;
                it.objectReferenceValue = replacement;
                n++;
            }
            if (n > 0) so.ApplyModifiedPropertiesWithoutUndo();
            return n;
        }
    }
}
