using System.Collections.Generic;
using System.Linq;
using System.Text;
using GateRush.Race;
using GateRush.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Tools > Art > Verify: checks that the art pass left gameplay alone. For every track it sphere-casts the quad's
    /// collision radius (0.12 m) along the straight lines spawn → gate 1 → … → gate 1 against TrackGeometry and
    /// reports any hit on a collider the art pass added (under "Art Dressing"); it also checks that every gate still
    /// has its colliders and state renderers.
    /// </summary>
    public static class ArtVerify
    {
        private const float QuadRadius = 0.12f;
        private const int TrackGeometryMask = 1 << ArtPass.TrackGeometryLayer;

        [MenuItem("Tools/Art/Verify Racing Lines and Gates")]
        private static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            ArtJobs.RequireCleanScenes();
            var sb = new StringBuilder();
            try
            {
                foreach (var env in SceneShots.Environments)
                {
                    var scene = EditorSceneManager.OpenScene(ArtJobs.ScenePath("Track_" + env), OpenSceneMode.Single);
                    var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(SceneShots.PrefabsPath);
                    var track = TrackLoader.Load(SceneShots.CourseFor(env), prefabs);
                    Physics.SyncTransforms();
                    var dressing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == ArtPass.DressingRoot);

                    var points = new List<Vector3> { track.Spawn.position + Vector3.up * 0.4f };
                    points.AddRange(track.Gates.Select(g => g.OpeningCenterWorld));
                    points.Add(track.Gates[0].OpeningCenterWorld);
                    var artHits = new HashSet<string>();
                    var otherHits = new HashSet<string>();
                    for (int i = 1; i < points.Count; i++)
                    {
                        Vector3 a = points[i - 1], b = points[i];
                        Vector3 d = b - a;
                        float len = d.magnitude;
                        foreach (var h in Physics.SphereCastAll(a, QuadRadius, d / len, len, TrackGeometryMask, QueryTriggerInteraction.Ignore))
                        {
                            if (h.collider.GetComponentInParent<Gate>() != null) continue;
                            if (dressing != null && h.collider.transform.IsChildOf(dressing.transform)) artHits.Add(h.collider.name);
                            else otherHits.Add(h.collider.transform.IsChildOf(track.Root) ? "course prop " + h.collider.name : h.collider.name);
                        }
                    }

                    int gateProblems = 0;
                    foreach (var g in track.Gates)
                    {
                        var so = new SerializedObject(g);
                        var fr = so.FindProperty("frameRenderers");
                        bool renderersOk = fr.arraySize > 0 && Enumerable.Range(0, fr.arraySize).All(k => fr.GetArrayElementAtIndex(k).objectReferenceValue != null);
                        bool collidersOk = g.GetComponentsInChildren<BoxCollider>().Count(c => c.gameObject.layer == ArtPass.TrackGeometryLayer && !c.isTrigger) >= 3;
                        if (!renderersOk || !collidersOk) gateProblems++;
                    }

                    sb.AppendLine($"{env}: {track.Gates.Length} gates ({gateProblems} with problems); racing line blocked by art: " +
                                  (artHits.Count == 0 ? "none" : string.Join(", ", artHits)) +
                                  (otherHits.Count > 0 ? $"; pre-existing obstacles on straight lines (course design, unchanged): {string.Join(", ", otherHits.Take(6))}" : ""));
                    Object.DestroyImmediate(track.Root.gameObject);
                }
            }
            finally
            {
                ArtJobs.ReturnToBoot();
            }
            return sb.ToString();
        }
    }
}
