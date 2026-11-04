using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GateRush.Garage;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// P2 quad pipeline: garage part data → <c>Tools/Blender/quadgen.py</c> (Blender, headless) → one JSON mesh per part
    /// → mesh assets in <see cref="MeshDir"/> and the <see cref="QuadPartLibrary"/> in Resources. The spec carries the
    /// same layout numbers the runtime builder uses (<see cref="BuildComposer.Arms"/>, plate, stack, body), so the
    /// generated parts sit exactly where the physics puts them.
    /// </summary>
    public static class QuadPartGenerator
    {
        public const string Blender = "C:/Program Files/Blender Foundation/Blender 3.3/blender.exe";
        public const string Script = "Tools/Blender/quadgen.py";
        public const string MeshDir = "Assets/_Project/Art/Quads/Generated";
        public const string LibraryPath = "Assets/_Project/Resources/QuadPartLibrary.asset";
        private const string WorkDir = "Temp/QuadGen";

        /// <summary>The shared electronics every build picks from.</summary>
        public static readonly string[] SharedParts =
        {
            "stack-30", "stack-25", "stack-20", "camera-micro", "camera-nano", "camera-hd", "vtx-analog", "vtx-hd", "receiver", "capacitor", "actioncam",
        };

        [MenuItem("GateRush/Quads/Generate Quad Parts")]
        private static void Menu() => Debug.Log(GenerateAll());

        public static string GenerateAll(string only = null)
        {
            Directory.CreateDirectory(WorkDir);
            string spec = Path.GetFullPath(Path.Combine(WorkDir, "parts.json"));
            File.WriteAllText(spec, SpecJson());
            string outDir = Path.GetFullPath(Path.Combine(WorkDir, "out"));
            if (only == null && Directory.Exists(outDir)) Directory.Delete(outDir, true);
            string args = $"-b --factory-startup -P \"{Path.GetFullPath(Script)}\" -- \"{spec}\" \"{outDir}\"" + (only != null ? $" {only}" : "");
            var psi = new ProcessStartInfo(Blender, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            var log = new StringBuilder();
            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                foreach (var line in stdout.Split('\n'))
                    if (line.StartsWith("QUADGEN") || line.Contains("Error")) log.AppendLine(line.Trim());
                if (p.ExitCode != 0 || !stdout.Contains("QUADGEN DONE")) return "Blender failed:\n" + log + stderr;
            }
            return Import(outDir) + "\n" + log;
        }

        // ---------- spec ----------

        public static string SpecJson()
        {
            var parts = new List<string>();
            string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
            foreach (var f in PartCatalog.Frames)
            {
                BuildComposer.Arms(f, out float ax, out float az);
                bool whoop = f.Size == SizeClass.Whoop;
                float mount = MountFor(f.Size, f.Geometry);
                float ductH = f.Geometry != FrameGeometry.Ducted ? 0f : whoop ? 0.014f : f.MaxPropIn < 3f ? 0.018f : 0.022f;
                parts.Add($"{{\"id\":\"{f.Id}\",\"kind\":\"frame\",\"armX\":{F(ax)},\"armZ\":{F(az)},\"plate\":{F(f.PlateMm * 0.001f)},\"armW\":{F(f.ArmWidthMm * 0.001f)}," +
                          $"\"bodyL\":{F(f.BodyLengthMm * 0.001f)},\"bodyW\":{F(f.BodyWidthMm * 0.001f)},\"stack\":{F(f.StackMm * 0.001f)},\"geometry\":\"{f.Geometry}\"," +
                          $"\"mount\":{F(mount)},\"mountScrew\":{F(mount >= 16f ? 3f : mount >= 9f ? 2f : 1.4f)},\"propR\":{F(f.MaxPropIn * 0.0254f * 0.5f)},\"ductH\":{F(ductH)}," +
                          $"\"canopy\":{(whoop ? "true" : "false")},\"camW\":{(f.Size >= SizeClass.Five ? 19 : 14)}}}");
            }
            foreach (var m in PartCatalog.Motors)
                parts.Add($"{{\"id\":\"{m.Id}\",\"kind\":\"motor\",\"statorD\":{F(m.StatorD)},\"statorH\":{F(m.StatorH)},\"mount\":{F(m.StatorD >= 20f ? 16f : m.StatorD >= 16f ? 12f : m.StatorD >= 10f ? 9f : 6.6f)}}}");
            foreach (var p in PartCatalog.Props)
                parts.Add($"{{\"id\":\"{p.Id}\",\"kind\":\"prop\",\"diameterIn\":{F(p.DiameterIn)},\"pitchIn\":{F(p.PitchIn)},\"blades\":{p.Blades}}}");
            foreach (var b in PartCatalog.Batteries)
            {
                var (w, l, h) = BatterySize(b);
                string plug = b.Cells == 1 ? "BT2" : b.CapacityMah <= 850f ? "XT30" : "XT60";
                parts.Add($"{{\"id\":\"{b.Id}\",\"kind\":\"battery\",\"w\":{F(w)},\"l\":{F(l)},\"h\":{F(h)},\"liion\":{(b.Id.Contains("liion") ? "true" : "false")},\"cells\":{b.Cells},\"plug\":\"{plug}\"}}");
            }
            parts.Add("{\"id\":\"stack-30\",\"kind\":\"stack\",\"pattern\":30.5}");
            parts.Add("{\"id\":\"stack-25\",\"kind\":\"stack\",\"pattern\":25.5}");
            parts.Add("{\"id\":\"stack-20\",\"kind\":\"stack\",\"pattern\":20}");
            parts.Add("{\"id\":\"camera-micro\",\"kind\":\"camera\",\"size\":19,\"hd\":false}");
            parts.Add("{\"id\":\"camera-nano\",\"kind\":\"camera\",\"size\":14,\"hd\":false}");
            parts.Add("{\"id\":\"camera-hd\",\"kind\":\"camera\",\"size\":20,\"hd\":true}");
            parts.Add("{\"id\":\"vtx-analog\",\"kind\":\"vtx\",\"hd\":false}");
            parts.Add("{\"id\":\"vtx-hd\",\"kind\":\"vtx\",\"hd\":true}");
            parts.Add("{\"id\":\"receiver\",\"kind\":\"receiver\"}");
            parts.Add("{\"id\":\"capacitor\",\"kind\":\"capacitor\"}");
            parts.Add("{\"id\":\"actioncam\",\"kind\":\"actioncam\"}");
            return "{\"parts\":[\n" + string.Join(",\n", parts) + "\n]}";
        }

        /// <summary>Motor mounting pattern (mm) by class.</summary>
        public static float MountFor(SizeClass size, FrameGeometry geometry) => size switch
        {
            SizeClass.Five or SizeClass.Seven => 16f,
            SizeClass.Three => geometry == FrameGeometry.Ducted ? 12f : 9f,
            SizeClass.TwoFive => 9f,
            _ => 6.6f,
        };

        /// <summary>The pack's natural size (m) from its mass (~2.1 g/cm³), the same rule as the runtime builder.</summary>
        public static (float w, float l, float h) BatterySize(BatteryDef b) => QuadModelBuilder.BatterySize(b);

        // ---------- import ----------

        [Serializable]
        private sealed class PartJson
        {
            public string id;
            public float[] vertices, normals, uvs;
            public SubJson[] submeshes;
            public SocketJson[] sockets;
        }

        [Serializable] private sealed class SubJson { public string material; public int[] indices; }
        [Serializable] private sealed class SocketJson { public string name; public float[] position; }

        public static string Import(string outDir)
        {
            Directory.CreateDirectory(MeshDir);
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            var lib = AssetDatabase.LoadAssetAtPath<QuadPartLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<QuadPartLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            int parts = 0, verts = 0;
            foreach (var file in Directory.GetFiles(outDir, "*.json").OrderBy(f => f))
            {
                var pj = JsonUtility.FromJson<PartJson>(File.ReadAllText(file));
                var mesh = BuildMesh(pj);
                verts += mesh.vertexCount;
                string path = $"{MeshDir}/{pj.id}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    mesh = existing;
                }
                else AssetDatabase.CreateAsset(mesh, path);
                var entry = lib.Entries.FirstOrDefault(e => e.Id == pj.id);
                if (entry == null) lib.Entries.Add(entry = new QuadPartLibrary.Entry { Id = pj.id });
                entry.Mesh = mesh;
                entry.Slots = pj.submeshes.Select(s => s.material).ToArray();
                entry.Sockets = (pj.sockets ?? new SocketJson[0]).Select(s => new QuadPartLibrary.Socket { Name = s.name, Position = new Vector3(s.position[0], s.position[1], s.position[2]) }).ToList();
                parts++;
            }
            lib.Entries.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            QuadPartLibrary.Reload();
            return $"quad parts: {parts} meshes, {verts} vertices";
        }

        /// <summary>One sub-mesh per material slot; identical corners (position, normal, UV) are welded.</summary>
        private static Mesh BuildMesh(PartJson pj)
        {
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var map = new Dictionary<(Vector3, Vector3, Vector2), int>();
            var subs = new List<List<int>>();
            foreach (var s in pj.submeshes)
            {
                var tri = new List<int>(s.indices.Length);
                // quadgen writes Blender (right-handed) winding; the axis swap to Unity mirrors it, so it stays front-facing.
                foreach (int i in s.indices)
                {
                    var p = new Vector3(pj.vertices[i * 3], pj.vertices[i * 3 + 1], pj.vertices[i * 3 + 2]);
                    var n = new Vector3(pj.normals[i * 3], pj.normals[i * 3 + 1], pj.normals[i * 3 + 2]);
                    var t = new Vector2(pj.uvs[i * 2], pj.uvs[i * 2 + 1]);
                    var key = (p, n, t);
                    if (!map.TryGetValue(key, out int v))
                    {
                        v = pos.Count;
                        map[key] = v;
                        pos.Add(p);
                        nrm.Add(n);
                        uv.Add(t);
                    }
                    tri.Add(v);
                }
                subs.Add(tri);
            }
            var mesh = new Mesh { name = pj.id, indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
