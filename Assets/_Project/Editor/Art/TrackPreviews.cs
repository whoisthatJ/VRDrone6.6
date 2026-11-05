using System.IO;
using System.Text;
using Ushqyr.EditorTools.Builders;
using Ushqyr.Game;
using Ushqyr.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ushqyr.EditorTools.Art
{
    /// <summary>
    /// Ushqyr > Tracks > Capture Track Previews: a 640x360 aerial of every built-in track (the intro's high-orbit
    /// framing, the venue's menu lighting) into <c>Art/Previews/&lt;id&gt;.png</c>, then the catalogue is rebuilt so
    /// the menu shows them.
    /// </summary>
    public static class TrackPreviews
    {
        public const int Width = 640, Height = 360;

        [MenuItem("Ushqyr/Tracks/Capture Track Previews")]
        public static void Menu() => Debug.Log(CaptureAll());

        public static string CaptureAll(string onlyEnvironment = null)
        {
            ArtJobs.RequireCleanScenes();
            var cat = AssetDatabase.LoadAssetAtPath<TrackCatalog>("Assets/_Project/Data/TrackCatalog.asset");
            var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>("Assets/_Project/Data/TrackPrefabs.asset");
            Directory.CreateDirectory(CatalogBuilder.PreviewDir);
            var sb = new StringBuilder();
            try
            {
                foreach (var venue in cat.Environments)
                {
                    if (!string.IsNullOrEmpty(onlyEnvironment) && venue.Environment != onlyEnvironment) continue;
                    var tracks = cat.TracksFor(venue.Environment);
                    if (tracks.Count == 0) continue;
                    EditorSceneManager.OpenScene(ArtJobs.ScenePath(venue.SceneName), OpenSceneMode.Single);
                    var lighting = Object.FindAnyObjectByType<Ushqyr.Core.StadiumLighting>();
                    // The menu look: day at the Expo, floodlit night in the stadium (the game's defaults).
                    if (lighting != null) lighting.Apply(lighting.Venue == "stadium" ? Ushqyr.Core.TimeOfDay.Night : Ushqyr.Core.TimeOfDay.Day);
                    else
                    {
                        var range = Object.FindAnyObjectByType<Ushqyr.Strike.RangeLighting>();
                        if (range != null) range.Apply(Ushqyr.Strike.RangeWeather.Day);
                        DynamicGI.UpdateEnvironment();
                    }
                    var source = GameObject.Find("Main Camera")?.GetComponent<Camera>();
                    foreach (var entry in tracks)
                    {
                        var data = cat.Load(entry);
                        if (data == null) continue;
                        var track = TrackLoader.Load(data, prefabs);
                        try
                        {
                            if (track.RacingLine != null) track.RacingLine.SetActive(false);
                            TrackIntro.Extent(track, out var centre, out float radius);
                            if (data.FreeFlight)
                            {
                                centre = track.Spawn.position + track.Spawn.forward * 150f;
                                radius = 260f;
                            }
                            TrackIntro.Overview(track.Spawn, centre, radius, 0.5f, out var pos, out var look);
                            Inside(centre, ref pos);
                            var shot = SceneShots.Render(source, new SceneShots.Bookmark(entry.Id, pos, look, 50f));
                            var small = Downscale(shot, Width, Height);
                            File.WriteAllBytes($"{CatalogBuilder.PreviewDir}/{entry.Id}.png", small.EncodeToPNG());
                            Object.DestroyImmediate(shot);
                            Object.DestroyImmediate(small);
                            sb.AppendLine($"{venue.SceneName}/{entry.Id}");
                        }
                        finally { Object.DestroyImmediate(track.Root.gameObject); }
                    }
                }
            }
            finally { ArtJobs.ReturnToBoot(); }

            AssetDatabase.Refresh();
            foreach (var entry in cat.Tracks)
            {
                var importer = AssetImporter.GetAtPath($"{CatalogBuilder.PreviewDir}/{entry.Id}.png") as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            CatalogBuilder.BuildTracks();
            AssetDatabase.SaveAssets();
            return "Track previews: " + sb;
        }

        // Indoor venues: keep the camera under the roof and inside the walls.
        private static void Inside(Vector3 centre, ref Vector3 pos)
        {
            Vector3 from = centre + Vector3.up * 1.5f;
            if (!Physics.Raycast(from, Vector3.up, out var roof, 300f, ~0, QueryTriggerInteraction.Ignore)) return;
            float maxY = roof.point.y - 1.5f;
            if (pos.y > maxY) pos.y = maxY;
            Vector3 d = pos - from;
            if (Physics.Raycast(from, d.normalized, out var wall, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
                pos = from + d.normalized * Mathf.Max(1f, wall.distance - 1f);
        }

        private static Texture2D Downscale(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }
}
