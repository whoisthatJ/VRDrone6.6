using System.IO;
using System.Text;
using Ushqyr.AI;
using Ushqyr.Garage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ushqyr.EditorTools.Bots
{
    /// <summary>
    /// Ushqyr > Bots > Bake Racing Lines (P4): bakes the racing line of every race course for the 5" class into
    /// <c>Resources/RacingLines/</c>. Each course is baked in both line styles (smooth, then tight); the first one an
    /// Elite bot flies cleanly on two seeds headless is kept (else the one with the fewest crashes).
    /// </summary>
    public static class RacingLineBakeMenu
    {
        public const string Folder = "Assets/_Project/Resources/RacingLines";

        [MenuItem("Ushqyr/Bots/Bake Racing Lines")]
        public static void BakeMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[Ushqyr][Bots] " + BakeAll());
        }

        public static string BakeAll(SizeClass size = SizeClass.Five, int laps = 2)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(Folder);
            var sb = new StringBuilder();
            var elite = BotDifficultyProfile.Default(BotLevel.Elite);
            foreach (var id in BotHarness.RaceCourses())
            {
                BotLine best = null;
                string bestNote = "";
                int bestScore = int.MaxValue;
                foreach (bool tight in new[] { false, true })
                {
                    var course = BotHarness.Load(id, size, tight);
                    try
                    {
                        int score = 0;
                        string note = "";
                        for (int seed = 1; seed <= 2; seed++)
                        {
                            var r = BotHarness.Fly(course, elite, seed, laps);
                            score += r.Crashes + r.Missed + (r.Finished ? 0 : 100);
                            note += $" s{seed}: {r}";
                        }
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = course.Line;
                            bestNote = (tight ? "tight" : "smooth") + note;
                        }
                    }
                    finally { course.Dispose(); }
                    if (bestScore == 0) break;
                }
                Save(id, size, best);
                sb.AppendLine($"{id}: {bestNote}");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        private static void Save(string id, SizeClass size, BotLine line)
        {
            string path = $"{Folder}/{RacingLineAsset.NameFor(id, (int)size)}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<RacingLineAsset>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<RacingLineAsset>();
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.TrackId = id;
            asset.SizeClass = (int)size;
            asset.Line = line;
            EditorUtility.SetDirty(asset);
        }
    }
}
