using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GateRush.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Tools > Art > Audit Project: per scene (track scenes with their built-in course spawned) the renderers,
    /// estimated draw calls, triangles, materials, textures (size, format, memory), lightmaps and lights, plus every
    /// material under Assets/_Project. Writes Markdown to <see cref="OutPath"/>.
    /// </summary>
    public static class ArtAudit
    {
        public const string OutPath = "Docs/art_audit.md";

        [MenuItem("Tools/Art/Audit Project")]
        private static void MenuAudit() => Debug.Log(Run(OutPath));

        public static string RunDeferred(string outPath = OutPath) => ArtJobs.RunDeferred("audit", () => Run(outPath));

        private sealed class Stats
        {
            public string Scene;
            public int Renderers, DrawCalls, ShadowCasters, StaticBatched, Terrains, ReflectionProbes;
            public long Triangles;
            public readonly HashSet<Material> Materials = new HashSet<Material>();
            public readonly HashSet<Texture> Textures = new HashSet<Texture>();
            public readonly HashSet<Mesh> Meshes = new HashSet<Mesh>();
            public readonly List<Light> Lights = new List<Light>();
            public readonly List<Texture> Lightmaps = new List<Texture>();
            public long TextureBytes => Textures.Sum(Bytes);
            public long MeshBytes => Meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
            public long LightmapBytes => Lightmaps.Sum(Bytes);
        }

        public static string Run(string outPath)
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try { return RunInvariant(outPath); }
            finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        }

        private static string RunInvariant(string outPath)
        {
            ArtJobs.RequireCleanScenes();
            // Scene objects die when the next scene opens, so each scene is written out while it is still loaded.
            var rows = new List<string>();
            var sections = new StringBuilder();
            var summary = new List<string>();
            try
            {
                foreach (var s in EditorBuildSettings.scenes)
                {
                    if (!s.enabled) continue;
                    var st = AuditScene(s.path, out LoadedTrack track);
                    try
                    {
                        rows.Add(SummaryRow(st));
                        AppendScene(sections, st);
                        summary.Add($"{st.Scene} {st.DrawCalls} dc/{st.Triangles:N0} tris");
                    }
                    finally
                    {
                        if (track != null) Object.DestroyImmediate(track.Root.gameObject);
                    }
                }
            }
            finally
            {
                ArtJobs.ReturnToBoot();
            }

            var md = new StringBuilder();
            var rp = GraphicsSettings.currentRenderPipeline;
            md.AppendLine("# Art audit").AppendLine();
            md.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm} by *Tools > Art > Audit Project*. " +
                          $"Quality level **{QualitySettings.names[QualitySettings.GetQualityLevel()]}**, pipeline `{(rp != null ? AssetDatabase.GetAssetPath(rp) : "built-in")}`, " +
                          $"global texture mip limit {QualitySettings.globalTextureMipmapLimit}.");
            md.AppendLine("Track scenes include their built-in course (gates and props spawned from the prefabs). " +
                          "Draw calls = renderer submeshes before batching (SRP Batcher/static batching make them cheaper, not fewer). " +
                          "Memory is the editor's runtime estimate.").AppendLine();

            md.AppendLine("| Scene | Renderers | Draw calls (est.) | Shadow casters | Triangles | Materials | Textures | Texture MB | Mesh MB | Lightmaps | Lights (realtime/mixed/baked) | Terrains | Refl. probes |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var row in rows) md.AppendLine(row);
            md.AppendLine();

            md.Append(sections);
            AppendProjectMaterials(md);

            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, md.ToString());
            return $"{outPath}: {rows.Count} scenes, " + string.Join(", ", summary);
        }

        private static string SummaryRow(Stats st)
        {
            int rt = st.Lights.Count(l => l.lightmapBakeType == LightmapBakeType.Realtime);
            int mx = st.Lights.Count(l => l.lightmapBakeType == LightmapBakeType.Mixed);
            int bk = st.Lights.Count(l => l.lightmapBakeType == LightmapBakeType.Baked);
            return $"| {st.Scene} | {st.Renderers} | {st.DrawCalls} | {st.ShadowCasters} | {st.Triangles:N0} | {st.Materials.Count} | {st.Textures.Count} | " +
                   $"{Mb(st.TextureBytes)} | {Mb(st.MeshBytes)} | {st.Lightmaps.Count} ({Mb(st.LightmapBytes)} MB) | {rt}/{mx}/{bk} | {st.Terrains} | {st.ReflectionProbes} |";
        }

        private static Stats AuditScene(string path, out LoadedTrack track)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var st = new Stats { Scene = scene.name };
            track = null;
            if (scene.name.StartsWith("Track_"))
            {
                var prefabs = AssetDatabase.LoadAssetAtPath<TrackPrefabs>(SceneShots.PrefabsPath);
                track = TrackLoader.Load(SceneShots.CourseFor(scene.name.Substring("Track_".Length)), prefabs);
            }
            {
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                {
                    if (!r.enabled) continue;
                    st.Renderers++;
                    var mats = r.sharedMaterials;
                    st.DrawCalls += Mathf.Max(1, mats.Length);
                    if (r.shadowCastingMode != ShadowCastingMode.Off) st.ShadowCasters += Mathf.Max(1, mats.Length);
                    if (r.isPartOfStaticBatch || GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic)) st.StaticBatched++;
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
                    if (mesh != null)
                    {
                        st.Meshes.Add(mesh);
                        for (int i = 0; i < mesh.subMeshCount; i++)
                            if (mesh.GetTopology(i) == MeshTopology.Triangles) st.Triangles += mesh.GetIndexCount(i) / 3;
                    }
                    foreach (var m in mats) AddMaterial(st, m);
                }
                foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
                {
                    st.Terrains++;
                    AddMaterial(st, t.materialTemplate);
                    var td = t.terrainData;
                    if (td == null) continue;
                    st.Triangles += (long)(td.heightmapResolution - 1) * (td.heightmapResolution - 1) * 2 / Mathf.Max(1, t.heightmapPixelError > 1f ? 16 : 4);
                    foreach (var layer in td.terrainLayers)
                    {
                        if (layer == null) continue;
                        AddTexture(st, layer.diffuseTexture);
                        AddTexture(st, layer.normalMapTexture);
                        AddTexture(st, layer.maskMapTexture);
                    }
                    foreach (var dp in td.detailPrototypes) AddTexture(st, dp.prototypeTexture);
                }
                st.Lights.AddRange(Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude));
                st.ReflectionProbes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude).Length;
                AddMaterial(st, RenderSettings.skybox);
                foreach (var lm in LightmapSettings.lightmaps)
                {
                    if (lm.lightmapColor != null) st.Lightmaps.Add(lm.lightmapColor);
                    if (lm.lightmapDir != null) st.Lightmaps.Add(lm.lightmapDir);
                    if (lm.shadowMask != null) st.Lightmaps.Add(lm.shadowMask);
                }
            }
            return st;
        }

        private static void AddMaterial(Stats st, Material m)
        {
            if (m == null || !st.Materials.Add(m)) return;
            foreach (int id in m.GetTexturePropertyNameIDs()) AddTexture(st, m.GetTexture(id));
        }

        private static void AddTexture(Stats st, Texture t)
        {
            if (t != null) st.Textures.Add(t);
        }

        private static long Bytes(Texture t) => t == null ? 0 : Profiler.GetRuntimeMemorySizeLong(t);
        private static string Mb(long bytes) => (bytes / (1024f * 1024f)).ToString("F1");

        private static void AppendScene(StringBuilder md, Stats st)
        {
            md.AppendLine($"## {st.Scene}").AppendLine();
            md.AppendLine($"{st.Renderers} renderers ({st.StaticBatched} batching-static), ~{st.DrawCalls} draw calls, {st.ShadowCasters} shadow-caster draws, " +
                          $"{st.Triangles:N0} triangles, {st.Meshes.Count} unique meshes ({Mb(st.MeshBytes)} MB).").AppendLine();

            md.AppendLine("**Materials by shader**").AppendLine();
            foreach (var g in st.Materials.GroupBy(m => m.shader != null ? m.shader.name : "(missing)").OrderByDescending(g => g.Count()))
                md.AppendLine($"- `{g.Key}`: {g.Count()} ({string.Join(", ", g.Select(m => m.name).OrderBy(n => n).Take(14))}{(g.Count() > 14 ? ", …" : "")})");
            md.AppendLine();

            md.AppendLine("**Textures**").AppendLine();
            md.AppendLine("| Texture | Size | Format | Mips | MB |").AppendLine("|---|---|---|---|---|");
            foreach (var t in st.Textures.Concat(st.Lightmaps).Distinct().OrderByDescending(Bytes))
                md.AppendLine($"| {Label(t)} | {t.width}×{t.height} | {Format(t)} | {t.mipmapCount} | {Mb(Bytes(t))} |");
            md.AppendLine();

            md.AppendLine("**Lights**").AppendLine();
            if (st.Lights.Count == 0) md.AppendLine("- none");
            foreach (var l in st.Lights)
                md.AppendLine($"- {l.name}: {l.type}, {l.lightmapBakeType}, intensity {l.intensity:F2}, colour #{ColorUtility.ToHtmlStringRGB(l.color)}, shadows {l.shadows}");
            md.AppendLine();
        }

        private static void AppendProjectMaterials(StringBuilder md)
        {
            md.AppendLine("## Materials under Assets/_Project").AppendLine();
            md.AppendLine("| Material | Shader | Textures |").AppendLine("|---|---|---|");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }).OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null) continue;
                var texs = m.GetTexturePropertyNames().Select(p => (p, t: m.GetTexture(p))).Where(x => x.t != null).Select(x => $"{x.p}={x.t.name}");
                md.AppendLine($"| {AssetDatabase.GUIDToAssetPath(guid).Replace("Assets/_Project/", "")} | `{(m.shader != null ? m.shader.name : "(missing)")}` | {string.Join(", ", texs)} |");
            }
        }

        private static string Label(Texture t)
        {
            string p = AssetDatabase.GetAssetPath(t);
            return string.IsNullOrEmpty(p) ? t.name : p.Replace("Assets/_Project/", "");
        }

        private static string Format(Texture t) => t switch
        {
            Texture2D t2 => t2.format.ToString(),
            Cubemap c => "Cube " + c.format,
            _ => t.graphicsFormat.ToString(),
        };
    }
}
