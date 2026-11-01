using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Downloads CC0 assets from Poly Haven (api.polyhaven.com) and ambientCG (ambientcg.com) into the project.
    /// Files are staged in Temp/ArtDownloads (Unity never sees half-written files), then moved into Assets on the
    /// main thread. Texture sets are normalised to &lt;Name&gt;_Albedo (sRGB), _Normal (OpenGL), _ORM (R = AO,
    /// G = roughness, B = metallic, Poly Haven's "arm" layout) and optionally _Height. Requests run one at a time.
    /// Existing files are kept, so re-running only fetches what is missing.
    /// </summary>
    public static class ArtDownloader
    {
        private const string UserAgent = "GateRushArtPipeline/1.0 (Unity editor tool; github.com/whoisthatJ/VRDrone6.6)";
        private static readonly HttpClient Http = CreateClient();
        private static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();
        private static bool _pumping;

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

        [MenuItem("Tools/Art/Download All Catalog Assets")]
        private static void MenuAll() => FetchAllDeferred();

        [MenuItem("Tools/Art/Download Material...")]
        private static void MenuOne() => DownloadMaterialWindow.Open();

        public static string FetchAllDeferred() => FetchDeferred(ArtCatalog.All().ToList());

        /// <summary>Comma-separated catalog names or ids.</summary>
        public static string FetchDeferred(string names) =>
            FetchDeferred(names.Split(',').Select(n => ArtCatalog.Find(n.Trim())).Where(s => s != null).ToList());

        public static string FetchDeferred(List<ArtSource> sources)
        {
            string root = Directory.GetCurrentDirectory();
            ArtJobs.SetStatus($"running download 0/{sources.Count}");
            StartPump();
            Task.Run(async () =>
            {
                var log = new StringBuilder();
                for (int i = 0; i < sources.Count; i++)
                {
                    var s = sources[i];
                    ArtJobs.SetStatus($"running download {i + 1}/{sources.Count} {s.Id}");
                    try { log.AppendLine(await FetchAsync(s, root)); }
                    catch (Exception e) { log.AppendLine($"FAIL {s.Name} ({s.Id}): {e.GetType().Name}: {e.Message}"); }
                    await Task.Delay(300);
                }
                MainThread.Enqueue(() => Finish(sources, log));
            });
            return ArtJobs.StatusPath;
        }

        private static void StartPump()
        {
            if (_pumping) return;
            _pumping = true;
            EditorApplication.update += () =>
            {
                while (MainThread.TryDequeue(out var action)) action();
            };
        }

        private static string Stage(string root, ArtSource s) => Path.Combine(root, "Temp", "ArtDownloads", s.Kind.ToString(), s.Name);

        private static bool Installed(ArtSource s) => s.Kind switch
        {
            ArtKind.Hdri => File.Exists(Path.Combine(s.Folder, $"{s.Id}_{s.Resolution}.hdr")),
            ArtKind.Model => File.Exists(Path.Combine(s.Folder, $"{s.Id}_{s.Resolution}.fbx")),
            _ => Directory.Exists(s.Folder) && Directory.GetFiles(s.Folder, s.Name + "_Albedo.*").Length > 0,
        };

        // ---------- background thread: HTTP only ----------

        private static async Task<string> FetchAsync(ArtSource s, string root)
        {
            if (Installed(s)) return $"cached {s.Name}";
            string stage = Stage(root, s);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);
            switch (s.Kind)
            {
                case ArtKind.Hdri:
                {
                    var files = await Files(s.Id);
                    string url = files["hdri"]?[s.Resolution]?["hdr"]?["url"]?.ToString() ?? throw new Exception("no .hdr at " + s.Resolution);
                    await Download(url, Path.Combine(stage, $"{s.Id}_{s.Resolution}.hdr"));
                    break;
                }
                case ArtKind.Model:
                {
                    var files = await Files(s.Id);
                    var fbx = files["fbx"]?[s.Resolution]?["fbx"] ?? throw new Exception("no fbx at " + s.Resolution);
                    await Download(fbx["url"].ToString(), Path.Combine(stage, $"{s.Id}_{s.Resolution}.fbx"));
                    if (fbx["include"] is JObject include)
                        foreach (var p in include.Properties())
                        {
                            string lower = p.Name.ToLowerInvariant();
                            bool wanted = s.CacheOnly || lower.Contains("_diff") || lower.Contains("_nor_gl") || lower.Contains("_arm") || lower.Contains("_alpha");
                            if (!wanted) continue;
                            await Download(p.Value["url"].ToString(), Path.Combine(stage, p.Name.Replace('/', Path.DirectorySeparatorChar)));
                        }
                    break;
                }
                default:
                    if (s.Provider == ArtProvider.PolyHaven) await PolyHavenTexture(s, stage);
                    else await AmbientCgTexture(s, stage);
                    break;
            }
            return $"downloaded {s.Name} ({s.Id})";
        }

        private static async Task<JObject> Files(string id) => JObject.Parse(await Http.GetStringAsync($"https://api.polyhaven.com/files/{id}"));

        private static async Task PolyHavenTexture(ArtSource s, string stage)
        {
            var files = await Files(s.Id);
            await Map(files, s.Resolution, stage, "albedo", true, "Diffuse", "diff");
            await Map(files, s.Resolution, stage, "normal", true, "nor_gl");
            if (!await Map(files, s.Resolution, stage, "arm", false, "arm"))
            {
                await Map(files, s.Resolution, stage, "roughness", false, "Rough", "rough");
                await Map(files, s.Resolution, stage, "ao", false, "AO", "ao");
                await Map(files, s.Resolution, stage, "metal", false, "Metal", "metal");
            }
            if (s.Height) await Map(files, s.Resolution, stage, "height", false, "Displacement", "disp");
        }

        private static async Task<bool> Map(JObject files, string res, string stage, string role, bool required, params string[] keys)
        {
            foreach (var key in keys)
            {
                var node = files.Properties().FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))?.Value;
                string url = node?[res]?["jpg"]?["url"]?.ToString() ?? node?[res]?["png"]?["url"]?.ToString();
                if (url == null) continue;
                await Download(url, Path.Combine(stage, role + Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant()));
                return true;
            }
            if (required) throw new Exception($"map {string.Join("/", keys)} not available at {res}");
            return false;
        }

        private static async Task AmbientCgTexture(ArtSource s, string stage)
        {
            string zip = Path.Combine(stage, "src.zip");
            await Download($"https://ambientcg.com/get?file={s.Id}_{s.Resolution}-JPG.zip", zip);
            using (var stream = File.OpenRead(zip))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var e in archive.Entries)
                {
                    string n = e.Name.ToLowerInvariant();
                    string role = n.Contains("_color") ? "albedo"
                        : n.Contains("normalgl") ? "normal"
                        : n.Contains("roughness") ? "roughness"
                        : n.Contains("ambientocclusion") ? "ao"
                        : n.Contains("metalness") ? "metal"
                        : n.Contains("displacement") ? "height"
                        : n.Contains("opacity") ? "opacity"
                        : null;
                    if (role == null) continue;
                    using var src = e.Open();
                    using var dst = File.Create(Path.Combine(stage, role + Path.GetExtension(n)));
                    await src.CopyToAsync(dst);
                }
            }
            File.Delete(zip);
        }

        private static async Task Download(string url, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    string part = path + ".part";
                    using (var fs = File.Create(part)) await response.Content.CopyToAsync(fs);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(part, path);
                    return;
                }
                catch (Exception) when (attempt < 3)
                {
                    await Task.Delay(2000 * attempt);
                }
            }
        }

        // ---------- main thread: pack, move into Assets, import ----------

        private static void Finish(List<ArtSource> sources, StringBuilder log)
        {
            try
            {
                string root = Directory.GetCurrentDirectory();
                foreach (var s in sources)
                {
                    string stage = Stage(root, s);
                    if (!Directory.Exists(stage)) continue;
                    try { log.AppendLine(Install(s, stage)); }
                    catch (Exception e) { log.AppendLine($"FAIL install {s.Name}: {e.Message}"); }
                    Directory.Delete(stage, true);
                }
                ArtLicenses.Write();
                AssetDatabase.Refresh();
                ArtJobs.SetStatus("done download\n" + log);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ArtJobs.SetStatus("error download\n" + log + e);
            }
        }

        private static string Install(ArtSource s, string stage)
        {
            Directory.CreateDirectory(s.Folder);
            if (s.Kind != ArtKind.Texture)
            {
                foreach (var file in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(s.Folder, file.Substring(stage.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target, true);
                }
                return $"installed {s.Kind} {s.Id}";
            }

            string albedo = Pick(stage, "albedo") ?? throw new Exception("no albedo");
            string opacity = Pick(stage, "opacity");
            if (opacity != null)
            {
                var a = Load(albedo, false);
                var o = Load(opacity, true);
                var px = a.GetPixels32();
                var op = Resample(o, a.width, a.height);
                for (int i = 0; i < px.Length; i++) px[i].a = op[i];
                a.SetPixels32(px);
                File.WriteAllBytes(Path.Combine(s.Folder, s.Name + "_Albedo.png"), a.EncodeToPNG());
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(o);
            }
            else File.Copy(albedo, Path.Combine(s.Folder, s.Name + "_Albedo" + Path.GetExtension(albedo)), true);

            File.Copy(Pick(stage, "normal") ?? throw new Exception("no normal"), Path.Combine(s.Folder, s.Name + "_Normal.jpg"), true);

            string arm = Pick(stage, "arm");
            if (arm != null) File.Copy(arm, Path.Combine(s.Folder, s.Name + "_ORM.jpg"), true);
            else PackOrm(stage, Path.Combine(s.Folder, s.Name + "_ORM.jpg"));

            string height = Pick(stage, "height");
            if (s.Height && height != null) File.Copy(height, Path.Combine(s.Folder, s.Name + "_Height" + Path.GetExtension(height)), true);
            return $"installed {s.Name}";
        }

        /// <summary>R = AO (1 if missing), G = roughness (0.6 if missing), B = metallic (0 if missing).</summary>
        private static void PackOrm(string stage, string target)
        {
            var rough = LoadOrNull(Pick(stage, "roughness"));
            var ao = LoadOrNull(Pick(stage, "ao"));
            var metal = LoadOrNull(Pick(stage, "metal"));
            var reference = rough ?? ao ?? metal;
            int w = reference != null ? reference.width : 256, h = reference != null ? reference.height : 256;
            byte[] r = ao != null ? Resample(ao, w, h) : Fill(w * h, 255);
            byte[] g = rough != null ? Resample(rough, w, h) : Fill(w * h, 153);
            byte[] b = metal != null ? Resample(metal, w, h) : Fill(w * h, 0);
            var orm = new Texture2D(w, h, TextureFormat.RGB24, false, true);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(r[i], g[i], b[i], 255);
            orm.SetPixels32(px);
            File.WriteAllBytes(target, orm.EncodeToJPG(95));
            Object.DestroyImmediate(orm);
            if (rough != null) Object.DestroyImmediate(rough);
            if (ao != null) Object.DestroyImmediate(ao);
            if (metal != null) Object.DestroyImmediate(metal);
        }

        private static string Pick(string stage, string role) =>
            Directory.GetFiles(stage, role + ".*").FirstOrDefault(f => !f.EndsWith(".part"));

        private static Texture2D Load(string path, bool linear)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        private static Texture2D LoadOrNull(string path) => path != null ? Load(path, true) : null;

        private static byte[] Fill(int n, byte v)
        {
            var a = new byte[n];
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }

        /// <summary>Red channel of <paramref name="t"/> at w×h (nearest sample when the sizes differ).</summary>
        private static byte[] Resample(Texture2D t, int w, int h)
        {
            var src = t.GetPixels32();
            var dst = new byte[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x * t.width / w, sy = y * t.height / h;
                dst[y * w + x] = src[sy * t.width + sx].r;
            }
            return dst;
        }
    }

    /// <summary>Writes Assets/_Project/Art/ASSET_LICENSES.md from the catalog and what is installed.</summary>
    public static class ArtLicenses
    {
        public const string FilePath = "Assets/_Project/Art/ASSET_LICENSES.md";

        public static void Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Asset licenses").AppendLine();
            sb.AppendLine("Every external art asset in this project is **CC0 1.0** (public domain dedication, no attribution required). " +
                          "Sources: [Poly Haven](https://polyhaven.com/license) and [ambientCG](https://docs.ambientcg.com/license/). " +
                          "Generated by *Tools > Art > Download* (ArtDownloader); do not add assets from \"personal use only\" sites.").AppendLine();
            sb.AppendLine("| Asset | Type | Source | URL | License | Downloaded | Used for |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var s in ArtCatalog.All())
            {
                string probe = s.Kind == ArtKind.Hdri ? Path.Combine(s.Folder, $"{s.Id}_{s.Resolution}.hdr") : s.Folder;
                bool present = File.Exists(probe) || Directory.Exists(probe);
                if (!present) continue;
                var date = File.Exists(probe) ? File.GetLastWriteTime(probe) : Directory.GetLastWriteTime(probe);
                string provider = s.Provider == ArtProvider.PolyHaven ? "Poly Haven" : "ambientCG";
                string use = s.CacheOnly ? "impostor source (not committed)" : s.Kind == ArtKind.Texture ? $"{s.Category} material, {s.Resolution}, tile {s.TileMeters:0.##} m" : $"{s.Kind}, {s.Resolution}";
                sb.AppendLine($"| {s.Name} | {s.Kind} | {provider} `{s.Id}` | {s.Url} | CC0 1.0 | {date:yyyy-MM-dd} | {use} |");
            }
            sb.AppendLine().AppendLine("## Generated in the editor (no external source)").AppendLine();
            sb.AppendLine("Macro-variation noise, carbon-fibre weave, chalk, safety stripes, grass blades, water ripples, LiPo label, " +
                          "and the procedural meshes (box UV meshes, trusses, tents, fences, cones, quad parts) are generated by the scripts in " +
                          "`Assets/_Project/Editor/Art/` and are original to this project.");
            File.WriteAllText(FilePath, sb.ToString());
        }
    }

    /// <summary>Tools > Art > Download Material…: fetch one Poly Haven or ambientCG texture set by id.</summary>
    public sealed class DownloadMaterialWindow : EditorWindow
    {
        private string _id = "concrete_floor_02";
        private string _name = "Concrete_Floor";
        private string _category = "Concrete";
        private ArtProvider _provider = ArtProvider.PolyHaven;
        private string _resolution = "2k";
        private float _tile = 2f;

        public static void Open() => GetWindow<DownloadMaterialWindow>(true, "Download Material");

        private void OnGUI()
        {
            _provider = (ArtProvider)EditorGUILayout.EnumPopup("Provider", _provider);
            _id = EditorGUILayout.TextField(_provider == ArtProvider.PolyHaven ? "Poly Haven id" : "ambientCG id", _id);
            _name = EditorGUILayout.TextField("Material name", _name);
            _category = EditorGUILayout.TextField("Category", _category);
            _resolution = EditorGUILayout.TextField("Resolution (2k/4k, 1K/2K)", _resolution);
            _tile = EditorGUILayout.FloatField("Tile size (m)", _tile);
            EditorGUILayout.HelpBox("CC0 sources only. Add the asset to ArtCatalog.cs to keep it in the licence log and rebuilds.", MessageType.Info);
            if (GUILayout.Button("Download"))
            {
                var s = new ArtSource { Id = _id, Name = _name, Category = _category, Provider = _provider, Kind = ArtKind.Texture, Resolution = _resolution, TileMeters = _tile };
                ArtDownloader.FetchDeferred(new List<ArtSource> { s });
                Close();
            }
        }
    }
}
