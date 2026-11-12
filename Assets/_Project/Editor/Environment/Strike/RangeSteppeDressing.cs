using System.Collections.Generic;
using System.IO;
using Ushqyr.EditorTools.Art;
using Ushqyr.Strike;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.EditorTools.Range
{
    /// <summary>
    /// What makes the Strike Range read as a place: photographic skies with matching sun and haze, shelter belts of
    /// trees along the field edges and a few inside the range, lone trees and scrub, and tree lines far out on the
    /// plain so the horizon is not a bare line.
    /// </summary>
    public static class RangeSteppeDressing
    {
        private const string Dir = "Assets/_Project/Art/Strike";

        // ---------- skies ----------

        /// <summary>Day, overcast and dusk skies (order of <see cref="RangeWeather"/>), each with its sun and haze.</summary>
        public static RangeLighting.Sky[] Skies()
        {
            return new[]
            {
                Sky("Day", "hilly_terrain_01_puresky", 0.6f, 0.45f),
                Sky("Overcast", "kloofendal_overcast_puresky", 0.6f, 0.55f),
                Sky("Dusk", "qwantani_dusk_2_puresky", 0.6f, 0.45f),
            };
        }

        private static RangeLighting.Sky Sky(string name, string hdri, float exposure, float hazeBrightness)
        {
            var shared = EnvironmentAssets.Sky(hdri);
            var src = ArtCatalog.Find(hdri);
            if (shared == null || src == null) return default;
            string path = $"{Dir}/Sky_Steppe_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shared);
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(shared);
            m.shader = shared.shader;
            m.SetFloat("_Exposure", exposure);
            m.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(m);
            var hdr = HdriSun.Analyze($"{EnvironmentAssets.SkyDir}/{src.Id}_{src.Resolution}.hdr");
            Color haze = hdr.HorizonColor * exposure * hazeBrightness;
            float max = haze.maxColorComponent;
            if (max > 1f) haze /= max;
            haze.a = 1f;
            return new RangeLighting.Sky
            {
                Material = m,
                SunRotation = Quaternion.LookRotation(-hdr.SunDirection, Vector3.up),
                SunColor = Color.Lerp(Color.white, hdr.SunColor, 0.6f),
                Haze = haze.gamma,
            };
        }

        // ---------- leaves, shrubs ----------

        /// <summary>Dusty steppe foliage: the leaf-cluster cards of the Expo trees, toned down.</summary>
        public static Material LeafMaterial()
        {
            string path = Dir + "/M_Strike_Leaves.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(MaterialLibrary.Get("M_Expo_Leaves")) { name = "M_Strike_Leaves" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", new Color(0.92f, 0.82f, 0.5f)); // olive, not park green
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A low shrub about 1 m across: leaf cards scattered through a squashed dome, shaded as one volume.</summary>
        public static Mesh ShrubMesh(int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            var centre = new Vector3(0f, 0.35f, 0f);
            for (int i = 0; i < 18; i++)
            {
                var dir = new Vector3(R(-1f, 1f), R(-0.2f, 1f), R(-1f, 1f)).normalized;
                var c = centre + Vector3.Scale(dir * Mathf.Pow(R(0.2f, 1f), 0.6f), new Vector3(0.5f, 0.45f, 0.5f));
                float size = R(0.42f, 0.62f);
                var rot = Quaternion.Euler(R(-50f, 50f), R(0f, 360f), R(-30f, 30f));
                Vector3 right = rot * Vector3.right * size * 0.5f, up = rot * Vector3.up * size * 0.5f;
                int s = v.Count;
                foreach (var (p, q) in new[] { (c - right - up, new Vector2(0f, 0f)), (c + right - up, new Vector2(1f, 0f)), (c + right + up, new Vector2(1f, 1f)), (c - right + up, new Vector2(0f, 1f)) })
                {
                    var pp = p;
                    pp.y = Mathf.Max(pp.y, 0f);
                    v.Add(pp);
                    n.Add(((pp - centre).normalized + Vector3.up * 0.5f).normalized);
                    uv.Add(q);
                }
                t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            var m = new Mesh { name = "Steppe Shrub" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        // ---------- trees ----------

        private static readonly (Vector2 a, Vector2 b)[] Belts =
        {
            (new Vector2(-615f, -300f), new Vector2(-600f, 420f)),  // west field edge
            (new Vector2(-420f, 625f), new Vector2(380f, 615f)),    // north
            (new Vector2(615f, -200f), new Vector2(625f, 480f)),    // east
            (new Vector2(-580f, -610f), new Vector2(-170f, -632f)), // behind the pads, west
            (new Vector2(170f, -632f), new Vector2(580f, -605f)),   // behind the pads, east
            (new Vector2(-190f, 140f), new Vector2(70f, 255f)),     // inside the range
            (new Vector2(110f, -340f), new Vector2(235f, -140f)),
            (new Vector2(-270f, -340f), new Vector2(-110f, -190f)),
            (new Vector2(140f, 200f), new Vector2(250f, 330f)),
        };

        /// <summary>
        /// Shelter belts (two staggered rows with scrub underneath) and scattered lone trees, kept well clear of the
        /// roads, the target spots, the pads and the earth banks so they never hide a target or catch a diving bot.
        /// </summary>
        public static int Trees(Transform root, int layer, RangeLayout layout, IList<RangeSteppeTerrain.Mound> mounds)
        {
            float[] heights = { 8f, 9.5f, 11f, 7f, 10f };
            var meshes = new Mesh[heights.Length];
            for (int i = 0; i < heights.Length; i++)
            {
                int seed = 300 + i * 17;
                float h = heights[i];
                meshes[i] = ArtPass.MeshAsset("SteppeTree_" + i, () => ExpoArt.TreeMesh(seed, h));
            }
            var shrub = ArtPass.MeshAsset("SteppeShrub", () => ShrubMesh(29));
            var mats = new[] { MaterialLibrary.Get("M_Expo_Bark"), LeafMaterial() };

            bool Blocked(float x, float z, float road)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > 672f) return true;
                if (new Vector2(x, z - RangeSteppeBuilder.PadZ).magnitude < 100f) return true;
                foreach (var r in layout.Roads)
                    foreach (var p in r.Points)
                        if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < road * road) return true;
                foreach (var s in layout.Spots)
                    if ((s.Position.x - x) * (s.Position.x - x) + (s.Position.z - z) * (s.Position.z - z) < 38f * 38f) return true;
                foreach (var m in mounds)
                    if (Vector2.Distance(m.Centre, new Vector2(x, z)) < m.HalfLength + 8f) return true;
                return false;
            }

            var rng = new System.Random(6161);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var colliders = new GameObject("Tree Colliders").transform;
            colliders.SetParent(root, false);
            var group = new GameObject("Trees").transform;
            group.SetParent(root, false);
            int count = 0, chunk = 0;

            void Emit(List<CombineInstance> bark, List<CombineInstance> leaves)
            {
                if (bark.Count == 0) return;
                var b = new Mesh { indexFormat = IndexFormat.UInt32 };
                b.CombineMeshes(bark.ToArray(), true, true);
                var l = new Mesh { indexFormat = IndexFormat.UInt32 };
                l.CombineMeshes(leaves.ToArray(), true, true);
                var mesh = new Mesh { name = "Steppe Trees " + chunk, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(new[] { new CombineInstance { mesh = b, transform = Matrix4x4.identity }, new CombineInstance { mesh = l, transform = Matrix4x4.identity } }, false, false);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(l);
                mesh.RecalculateBounds();
                var go = new GameObject("Trees " + chunk) { layer = layer, isStatic = true };
                go.transform.SetParent(group, false);
                go.AddComponent<MeshFilter>().sharedMesh = Save(mesh, "Steppe_Trees_" + chunk);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = ShadowCastingMode.On;
                chunk++;
                bark.Clear();
                leaves.Clear();
            }

            void Tree(List<CombineInstance> bark, List<CombineInstance> leaves, float x, float z)
            {
                int k = rng.Next(meshes.Length);
                float s = R(0.8f, 1.25f), h = heights[k] * s;
                var pos = new Vector3(x, RangeSteppeBuilder.Height(x, z) - 0.15f, z);
                var m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(s, s * R(0.92f, 1.08f), s));
                bark.Add(new CombineInstance { mesh = meshes[k], subMeshIndex = 0, transform = m });
                leaves.Add(new CombineInstance { mesh = meshes[k], subMeshIndex = 1, transform = m });
                var col = new GameObject("Tree") { layer = layer, isStatic = true };
                col.transform.SetParent(colliders, false);
                col.transform.position = pos;
                var trunk = col.AddComponent<CapsuleCollider>();
                trunk.radius = 0.3f * s;
                trunk.height = h * 0.55f;
                trunk.center = Vector3.up * h * 0.27f;
                var crown = col.AddComponent<SphereCollider>();
                crown.radius = h * 0.27f;
                crown.center = Vector3.up * h * 0.68f;
                count++;
            }

            void Scrub(List<CombineInstance> leaves, float x, float z)
            {
                float s = R(1.3f, 2.6f);
                var m = Matrix4x4.TRS(new Vector3(x, RangeSteppeBuilder.Height(x, z) - 0.05f, z), Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(s, s * R(0.8f, 1.2f), s));
                leaves.Add(new CombineInstance { mesh = shrub, subMeshIndex = 0, transform = m });
            }

            foreach (var (a, b) in Belts)
            {
                var bark = new List<CombineInstance>();
                var leaves = new List<CombineInstance>();
                Vector2 dir = (b - a).normalized, side = new Vector2(dir.y, -dir.x);
                float len = Vector2.Distance(a, b);
                for (int row = 0; row < 2; row++)
                    for (float d = row * 2.8f; d < len; d += R(4.5f, 7.5f))
                    {
                        if (rng.NextDouble() < 0.1) { d += R(4f, 14f); continue; } // gaps
                        float wander = (Mathf.PerlinNoise(d / 40f + a.x, a.y) - 0.5f) * 10f;
                        Vector2 p = a + dir * d + side * (wander + (row == 0 ? -3.2f : 3.2f) + R(-1.2f, 1.2f));
                        if (Blocked(p.x, p.y, 42f)) continue;
                        Tree(bark, leaves, p.x, p.y);
                        if (rng.NextDouble() < 0.7)
                        {
                            Vector2 q = p + new Vector2(R(-3.5f, 3.5f), R(-3.5f, 3.5f));
                            if (!Blocked(q.x, q.y, 30f)) Scrub(leaves, q.x, q.y);
                        }
                    }
                Emit(bark, leaves);
            }

            // Lone trees and small clumps out in the grass.
            {
                var bark = new List<CombineInstance>();
                var leaves = new List<CombineInstance>();
                int made = 0, guard = 0;
                while (made < 26 && guard++ < 3000)
                {
                    float x = R(-640f, 640f), z = R(-600f, 640f);
                    if (Blocked(x, z, 48f)) continue;
                    int n = 1 + rng.Next(3);
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 p = new Vector2(x, z) + (i == 0 ? Vector2.zero : new Vector2(R(-7f, 7f), R(-7f, 7f)));
                        if (!Blocked(p.x, p.y, 44f)) Tree(bark, leaves, p.x, p.y);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 q = new Vector2(x + R(-8f, 8f), z + R(-8f, 8f));
                        if (!Blocked(q.x, q.y, 30f)) Scrub(leaves, q.x, q.y);
                    }
                    made++;
                }
                Emit(bark, leaves);
            }
            return count;
        }

        // ---------- the far plain ----------

        /// <summary>
        /// Tree lines and copses out on the plain beyond the range (cards of a scanned tree; never reached, so no
        /// colliders or shadows): they give the horizon depth instead of a bare line.
        /// </summary>
        public static int Horizon(Transform root)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ImpostorBaker.MaterialPath);
            if (mat == null) return 0;
            var card = new Vector2(13.86f, 11.22f);
            var mesh = ArtPass.MeshAsset("TreeImpostorCard", () => ProcMesh.CrossedQuads(ImpostorBaker.Views, card.x, card.y));
            var rng = new System.Random(8118);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var group = new GameObject("Far Tree Lines").transform;
            group.SetParent(root, false);
            int count = 0;
            for (int line = 0; line < 26; line++)
            {
                float bearing = R(0f, Mathf.PI * 2f), radius = R(780f, 1400f);
                var centre = new Vector2(Mathf.Cos(bearing), Mathf.Sin(bearing)) * radius;
                float tangent = bearing + Mathf.PI * 0.5f + R(-0.5f, 0.5f);
                var dir = new Vector2(Mathf.Cos(tangent), Mathf.Sin(tangent));
                float len = R(220f, 760f);
                var parts = new List<(Mesh, Matrix4x4, int)>();
                for (float d = -len * 0.5f; d < len * 0.5f; d += R(6f, 11f))
                {
                    if (rng.NextDouble() < 0.08) { d += R(10f, 40f); continue; }
                    Vector2 p = centre + dir * d + new Vector2(R(-3f, 3f), R(-3f, 3f));
                    if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) < 712f) continue;
                    float s = R(0.85f, 1.5f);
                    parts.Add((mesh, Matrix4x4.TRS(new Vector3(p.x, RangeSteppeBuilder.SkirtLevel - 0.4f, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f), Vector3.one * s), 0));
                    count++;
                }
                if (parts.Count == 0) continue;
                var combined = ProcMesh.Combine("Far Trees " + line, parts);
                var go = new GameObject("Far Trees " + line) { isStatic = true };
                go.transform.SetParent(group, false);
                go.AddComponent<MeshFilter>().sharedMesh = Save(combined, "Steppe_FarTrees_" + line);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            return count;
        }

        private static Mesh Save(Mesh mesh, string name)
        {
            Directory.CreateDirectory(Dir + "/Meshes");
            string path = $"{Dir}/Meshes/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
