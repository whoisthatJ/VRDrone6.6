using System.IO;
using System.Linq;
using GateRush.Game;
using GateRush.Track;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// Tools > Track: rebuilds a spline track's generated geometry as a preview in the open scene (not saved with it)
    /// and exports its JSON. Generation is deterministic, so Rebuild can be run any number of times.
    /// </summary>
    public static class TrackTools
    {
        public const string PreviewPrefix = "Track Preview: ";

        [MenuItem("Tools/Track/Rebuild")]
        private static void RebuildMenu() => Debug.Log("[GateRush] " + Rebuild());

        [MenuItem("Tools/Track/Clear Preview")]
        public static void ClearPreview()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith(PreviewPrefix)) Object.DestroyImmediate(go);
        }

        [MenuItem("Tools/Track/Write Built-in Spline Tracks")]
        public static string WriteBuiltIns()
        {
            var written = RouteLibrary.WriteAll();
            CatalogBuilder.BuildTracks();
            AssetDatabase.SaveAssets();
            return string.Join(", ", written);
        }

        /// <summary>Rebuilds the selected track definition, or the first one made for the open scene.</summary>
        public static string Rebuild()
        {
            var def = Selection.activeObject as TrackDefinition ?? ForOpenScene() ?? AllDefinitions().FirstOrDefault();
            return def != null ? Rebuild(def) : "no TrackDefinition found (Tools > Track > Write Built-in Spline Tracks)";
        }

        public static string Rebuild(TrackDefinition def)
        {
            ExportJson(def);
            ClearPreview();

            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(PropBuilder.TrackPrefabsPath);
            int layer = LayerMask.NameToLayer(TrackSceneBuilder.TrackLayerName);
            var loaded = TrackBuilder.Build(def.Track, prefabs, null, layer < 0 ? 0 : layer);
            loaded.Root.name = PreviewPrefix + def.Track.Name;
            // A preview only (the game builds the track from JSON at runtime): never saved, and unlike HideFlags.DontSave it
            // stays in this scene, so it does not linger across scene loads.
            foreach (var t in loaded.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSaveInEditor;

            var errors = def.Track.Validate(prefabs != null ? prefabs.GateTypes() : null, prefabs != null ? prefabs.PropTypes() : null);
            int tris = 0, renderers = 0;
            foreach (var mf in loaded.Root.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) tris += (int)(mf.sharedMesh.GetIndexCount(0) + SubIndices(mf.sharedMesh)) / 3;
            foreach (var r in loaded.Root.GetComponentsInChildren<Renderer>()) renderers++;
            int colliders = loaded.Root.GetComponentsInChildren<Collider>().Length;
            return $"{def.Track.Name}: {loaded.Route.Length:0} m, {loaded.Gates.Length} checkpoints, {def.Track.Route.Obstacles.Count} obstacles, " +
                   $"{renderers} renderers, {tris} triangles, {colliders} colliders" +
                   (errors.Count > 0 ? "; problems: " + string.Join("; ", errors) : "");
        }

        /// <summary>Writes the definition's JSON next to the other tracks and links it.</summary>
        public static string ExportJson(TrackDefinition def)
        {
            Directory.CreateDirectory(CourseLibrary.TrackDir);
            string path = $"{CourseLibrary.TrackDir}/{def.Track.Id}.json";
            File.WriteAllText(path, def.ToJson());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            def.Json = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            return path;
        }

        private static long SubIndices(Mesh mesh)
        {
            long n = 0;
            for (int i = 1; i < mesh.subMeshCount; i++) n += mesh.GetIndexCount(i);
            return n;
        }

        private static TrackDefinition ForOpenScene()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogBuilder.TrackCatalogPath);
            string scene = SceneManager.GetActiveScene().name;
            return AllDefinitions().FirstOrDefault(d => catalog != null && catalog.SceneFor(d.Track.Environment) == scene);
        }

        private static System.Collections.Generic.IEnumerable<TrackDefinition> AllDefinitions() =>
            AssetDatabase.FindAssets("t:TrackDefinition").Select(g => AssetDatabase.LoadAssetAtPath<TrackDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null && d.Track != null && d.Track.IsSplineTrack);
    }
}
