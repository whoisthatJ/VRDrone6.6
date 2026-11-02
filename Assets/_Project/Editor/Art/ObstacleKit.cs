using System.Collections.Generic;
using System.IO;
using GateRush.Race;
using GateRush.Track;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// T2 obstacle kit: procedural, LOD'd race obstacles built from a shared set of parts —
    /// <list type="bullet">
    /// <item>290 mm box truss (4 round chords, zig-zag diagonals on every face, end plates and bolts at the couplers);</item>
    /// <item>padded foam covers (stitched-seam fabric) with black Velcro straps;</item>
    /// <item>aluminium LED channels with diffusers (one LED renderer per obstacle, driven by <see cref="NextGateHighlight"/>);</item>
    /// <item>steel base plates with sandbags or water barrels and striped cable ramps;</item>
    /// <item>alpha-tested safety nets, printed chevron boards, GATERUSH feather flags, landing pads, the start grid.</item>
    /// </list>
    /// Obstacles (local +Z = flying direction, base on y = 0): Single, Double (stacked), Slalom (3 offset gates, 3
    /// checkpoints), Cube, Ladder (H hurdle, fly under or between the rungs), Dive (frame tilted 45° down), Ramp
    /// (gate on a raised platform at the top of a ramp), Arch. Every obstacle has LOD0/1/2 + cull, box colliders only,
    /// and one or more directional <see cref="Gate"/> checkpoints. Prefabs go to Prefabs/Obstacles and are registered
    /// in <see cref="TrackPrefabs"/> by type name.
    /// </summary>
    public static class ObstacleKit
    {
        public const string PrefabDir = "Assets/_Project/Prefabs/Obstacles";
        public static readonly string[] GateTypes = { "Single", "Double", "Slalom", "Cube", "Ladder", "Dive", "Ramp", "Arch" };
        public static readonly string[] PropTypes = { "ChevronBoard", "ChevronBoardLeft", "Flag", "LandingPad", "StartPad" };

        public const float TrussSize = 0.29f;
        public const float PadSize = 0.34f;
        private const float Chord = 0.024f;

        private enum Slot { Frame = 0, Pad = 1, Metal = 2, Bag = 3, Black = 4, Net = 5, Barrel = 6, Stripes = 7, Wood = 8, Print = 9 }
        private const int SlotCount = 10;

        /// <summary>Screen-height fractions where LOD1, LOD2 and culling start.</summary>
        private static readonly float[] LodHeights = { 0.12f, 0.035f, 0.005f };

        public static string BuildAll(TrackPrefabs prefabs)
        {
            KitTextures.GenerateAll();
            MaterialLibrary.BuildAll();
            Directory.CreateDirectory(PrefabDir);
            var log = new System.Text.StringBuilder("obstacle kit:");
            foreach (var type in GateTypes) log.Append(" ").Append(Save(type, BuildObstacle(type)));
            foreach (var type in PropTypes) log.Append(" ").Append(Save(type, BuildProp(type)));
            AssetDatabase.SaveAssets();
            if (prefabs != null) Register(prefabs);
            return log.ToString();
        }

        public static string PrefabPath(string type) => $"{PrefabDir}/Obstacle_{type}.prefab";

        private static void Register(TrackPrefabs prefabs)
        {
            prefabs.Gates.RemoveAll(e => System.Array.IndexOf(GateTypes, e.Type) >= 0);
            prefabs.Props.RemoveAll(e => System.Array.IndexOf(PropTypes, e.Type) >= 0);
            foreach (var t in GateTypes) prefabs.Gates.Add(new TrackPrefabs.Entry { Type = t, Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(t)) });
            foreach (var t in PropTypes) prefabs.Props.Add(new TrackPrefabs.Entry { Type = t, Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(t)) });
            EditorUtility.SetDirty(prefabs);
        }

        private static string Save(string type, GameObject root)
        {
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(type));
            Object.DestroyImmediate(root);
            return type;
        }

        // =====================================================================
        // Geometry accumulator: per LOD, per material slot; plus the LED strips and colliders.
        // =====================================================================

        private sealed class Geo
        {
            private readonly TrackMesh[,] _meshes = new TrackMesh[3, SlotCount];
            private readonly List<(Mesh, Matrix4x4, int)>[] _parts = { new List<(Mesh, Matrix4x4, int)>(), new List<(Mesh, Matrix4x4, int)>(), new List<(Mesh, Matrix4x4, int)>() };
            public readonly TrackMesh Leds = new TrackMesh(1);
            public readonly List<(Vector3 center, Vector3 size, Quaternion rotation)> Colliders = new List<(Vector3, Vector3, Quaternion)>();
            /// <summary>Applied to everything added (lets a sub-assembly, e.g. the tilted dive frame, use its own axes).</summary>
            public Matrix4x4 Frame = Matrix4x4.identity;

            public TrackMesh M(int lod, Slot s) => _meshes[lod, (int)s] ??= new TrackMesh(1);
            public void Part(int lod, Mesh mesh, Matrix4x4 local, Slot s) => _parts[lod].Add((mesh, Frame * local, (int)s));
            public Vector3 P(Vector3 local) => Frame.MultiplyPoint3x4(local);
            public Quaternion R(Quaternion local) => Frame.rotation * local;

            public void Collider(Vector3 center, Vector3 size, Quaternion rotation) => Colliders.Add((P(center), size, R(rotation)));

            public Mesh Build(int lod, string name, out int slots)
            {
                var parts = new List<(Mesh, Matrix4x4, int)>(_parts[lod]);
                var temp = new List<Mesh>();
                for (int s = 0; s < SlotCount; s++)
                {
                    var m = _meshes[lod, s];
                    if (m == null || m.IsEmpty) continue;
                    var mesh = m.ToMesh("tmp");
                    temp.Add(mesh);
                    parts.Add((mesh, Matrix4x4.identity, s));
                }
                slots = 0;
                foreach (var p in parts) slots = Mathf.Max(slots, p.Item3 + 1);
                // Combine drops empty sub-meshes, which would shift every later material: give each unused slot a
                // degenerate (invisible) triangle so sub-mesh i always uses material slot i.
                var used = new bool[slots];
                foreach (var p in parts) used[p.Item3] = true;
                for (int s = 0; s < slots; s++)
                {
                    if (used[s]) continue;
                    var stub = new Mesh { name = "stub" };
                    stub.SetVertices(new[] { Vector3.zero, Vector3.zero, Vector3.zero });
                    stub.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up });
                    stub.SetUVs(0, new[] { Vector2.zero, Vector2.zero, Vector2.zero });
                    stub.SetTriangles(new[] { 0, 1, 2 }, 0);
                    temp.Add(stub);
                    parts.Add((stub, Matrix4x4.identity, s));
                }
                var combined = ProcMesh.Combine(name, parts);
                foreach (var t in temp) Object.DestroyImmediate(t);
                return combined;
            }
        }

        // ---------- parts ----------

        /// <summary>Box truss from a to b (local coords of the current frame).</summary>
        private static void Truss(Geo g, Vector3 a, Vector3 b, Slot slot = Slot.Frame, bool collider = true)
        {
            Vector3 wa = g.P(a), wb = g.P(b);
            Vector3 d = wb - wa;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Vector3 dir = d / len;
            Vector3 refUp = Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 u = Vector3.Cross(refUp, dir).normalized, w = Vector3.Cross(dir, u);
            float h = TrussSize * 0.5f - Chord;
            var corners = new[] { u * h + w * h, -u * h + w * h, -u * h - w * h, u * h - w * h };
            var rot = Quaternion.LookRotation(dir, w);

            for (int lod = 0; lod < 2; lod++)
            {
                var m = g.M(lod, slot);
                foreach (var c in corners) m.AddTube((int)0, new[] { wa + c, wb + c }, Chord, lod == 0 ? 8 : 4);
                int panels = Mathf.Max(1, Mathf.RoundToInt(len / 0.45f));
                int faces = lod == 0 ? 4 : 2;
                for (int f = 0; f < faces; f++)
                {
                    Vector3 c0 = corners[f], c1 = corners[(f + 1) % 4];
                    for (int i = 0; i < panels; i++)
                    {
                        float t0 = (float)i / panels, t1 = (float)(i + 1) / panels;
                        Vector3 p0 = wa + d * t0 + (i % 2 == 0 ? c0 : c1), p1 = wa + d * t1 + (i % 2 == 0 ? c1 : c0);
                        m.AddTube(0, new[] { p0, p1 }, 0.008f, lod == 0 ? 5 : 3);
                    }
                }
            }
            // End plates (couplers) with four bolts each on LOD0.
            foreach (var end in new[] { wa, wb })
            {
                Vector3 inward = end == wa ? dir : -dir;
                g.M(0, slot).AddBox(0, end + inward * 0.006f, rot, new Vector3(TrussSize + 0.03f, TrussSize + 0.03f, 0.012f));
                foreach (var c in corners)
                    g.M(0, Slot.Metal).AddBox(0, end + inward * 0.02f + c * 1.12f, rot, new Vector3(0.022f, 0.022f, 0.03f));
            }
            g.M(2, slot).AddBeam(0, wa, wb, TrussSize);
            if (collider) g.Colliders.Add(((wa + wb) * 0.5f, new Vector3(TrussSize, TrussSize, len), rot));
        }

        /// <summary>Padded foam cover (box-ish bar with rounded edges) with Velcro straps every 0.6 m.</summary>
        private static void Pad(Geo g, Vector3 center, Vector3 size, Quaternion rotation = default, bool collider = true)
        {
            if (rotation == default) rotation = Quaternion.identity;
            // PaddedBar is a unit mesh made for a transform scaled to the bar size.
            var local = Matrix4x4.TRS(center, rotation, size);
            g.Part(0, PaddedMesh(size, 4), local, Slot.Pad);
            g.Part(1, PaddedMesh(size, 1), local, Slot.Pad);
            g.M(2, Slot.Pad).AddBox(0, g.P(center), g.R(rotation), size);

            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            float len = size[axis];
            int straps = Mathf.Max(1, Mathf.FloorToInt(len / 0.6f));
            for (int i = 0; i < straps; i++)
            {
                float t = -len / 2 + (i + 0.5f) * len / straps;
                var offset = Vector3.zero;
                offset[axis] = t;
                var strap = size + Vector3.one * 0.012f;
                strap[axis] = 0.035f;
                g.M(0, Slot.Black).AddBox(0, g.P(center + rotation * offset), g.R(rotation), strap);
            }
            if (collider) g.Collider(center, size, rotation);
        }

        private static Mesh PaddedMesh(Vector3 size, int segments)
        {
            string key = $"KitPad_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_{segments}".Replace(',', '.');
            return ArtPass.MeshAsset(key, () => ProcMesh.PaddedBar(size, 0.42f, segments));
        }

        /// <summary>LED channel: aluminium housing (LOD0) + diffuser (the LED renderer), both faces of a gate edge.</summary>
        private static void Led(Geo g, Vector3 a, Vector3 b, Vector3 normal)
        {
            Vector3 wa = g.P(a), wb = g.P(b), n = g.R(Quaternion.identity) * normal;
            Vector3 d = wb - wa;
            float len = d.magnitude;
            if (len < 0.01f) return;
            var rot = Quaternion.LookRotation(d / len, n);
            Vector3 mid = (wa + wb) * 0.5f;
            g.M(0, Slot.Metal).AddBox(0, mid + n * 0.006f, rot, new Vector3(0.05f, 0.012f, len));
            g.Leds.AddBox(0, mid + n * 0.014f, rot, new Vector3(0.032f, 0.008f, len - 0.02f));
        }

        /// <summary>LED strips around a rectangular opening on both faces (front at +Z/2, back at −Z/2).</summary>
        private static void LedFrame(Geo g, float x0, float x1, float y0, float y1, float halfDepth)
        {
            foreach (float face in new[] { 1f, -1f })
            {
                float z = face * halfDepth;
                var n = new Vector3(0f, 0f, face);
                Led(g, new Vector3(x0, y0, z), new Vector3(x0, y1, z), n);
                Led(g, new Vector3(x1, y0, z), new Vector3(x1, y1, z), n);
                Led(g, new Vector3(x0, y1, z), new Vector3(x1, y1, z), n);
                if (y0 > 0.05f) Led(g, new Vector3(x0, y0, z), new Vector3(x1, y0, z), n);
            }
        }

        /// <summary>Steel base plate with two sandbags (or a water barrel) and a striped cable ramp.</summary>
        private static void Base(Geo g, Vector3 pos, float yaw, bool barrel = false)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            g.M(0, Slot.Metal).AddBox(0, g.P(pos + Vector3.up * 0.01f), g.R(rot), new Vector3(0.7f, 0.02f, 0.7f));
            g.M(1, Slot.Metal).AddBox(0, g.P(pos + Vector3.up * 0.01f), g.R(rot), new Vector3(0.7f, 0.02f, 0.7f));
            if (barrel)
            {
                var m = ArtPass.MeshAsset("KitBarrel", () => ProcMesh.Cone(0.85f, 0.28f, 0.26f, 18));
                g.Part(0, m, Matrix4x4.TRS(pos + rot * new Vector3(0.48f, 0f, 0f), rot, Vector3.one), Slot.Barrel);
                g.Part(1, m, Matrix4x4.TRS(pos + rot * new Vector3(0.48f, 0f, 0f), rot, Vector3.one), Slot.Barrel);
            }
            else
            {
                var bag = PaddedMesh(new Vector3(0.42f, 0.13f, 0.28f), 2);
                foreach (float side in new[] { -0.2f, 0.2f })
                    g.Part(0, bag, Matrix4x4.TRS(pos + rot * new Vector3(side, 0.085f, 0.18f), rot * Quaternion.Euler(0f, 90f, 0f), new Vector3(0.42f, 0.13f, 0.28f)), Slot.Bag);
                g.M(1, Slot.Bag).AddBox(0, g.P(pos + rot * new Vector3(0f, 0.085f, 0.18f)), g.R(rot), new Vector3(0.7f, 0.13f, 0.28f));
            }
            g.M(0, Slot.Stripes).AddBox(0, g.P(pos + rot * new Vector3(0f, 0.015f, -0.5f)), g.R(rot), new Vector3(0.5f, 0.03f, 0.22f));
        }

        /// <summary>Safety net panel facing ±<paramref name="normal"/>, metric UVs, thin box collider ("bounces").</summary>
        private static void Net(Geo g, Vector3 center, Vector2 size, Vector3 normal, Vector3 up)
        {
            var rot = Quaternion.LookRotation(up, normal); // quad +Y = normal, V along "up"
            for (int lod = 0; lod < 2; lod++) g.M(lod, Slot.Net).AddQuad(0, g.P(center), g.R(rot), size, metricUv: true);
            g.Collider(center, new Vector3(size.x, size.y, 0.03f), Quaternion.LookRotation(normal, up));
        }

        // =====================================================================
        // Obstacles
        // =====================================================================

        private static GameObject BuildObstacle(string type)
        {
            var g = new Geo();
            var gates = new List<(Vector3 center, Vector2 size, Quaternion rotation)>();
            Vector3? aim = null; // obstacle-space aim point when the opening centre is blocked (stacked gate)
            string pad = "M_Kit_Pad_Red", frame = "M_Kit_Truss_Black";
            Color idle = new Color(0.9f, 0.9f, 0.9f);
            float W, H, b;

            switch (type)
            {
                case "Single":
                    W = 1.8f; H = 1.8f; b = 0.5f;
                    PaddedGate(g, W, H, b);
                    gates.Add((new Vector3(0f, b + H / 2, 0f), new Vector2(W, H), Quaternion.identity));
                    break;

                case "Double":
                {
                    W = 1.8f; float h1 = 1.5f, h2 = 1.5f; b = 0.45f;
                    float top = b + h1 + PadSize + h2;
                    float x = W / 2 + PadSize / 2;
                    foreach (float s in new[] { -1f, 1f }) Pad(g, new Vector3(s * x, (top + PadSize) / 2, 0f), new Vector3(PadSize, top + PadSize, PadSize));
                    Pad(g, new Vector3(0f, top + PadSize / 2, 0f), new Vector3(W, PadSize, PadSize));
                    Pad(g, new Vector3(0f, b + h1 + PadSize / 2, 0f), new Vector3(W, PadSize, PadSize));
                    Pad(g, new Vector3(0f, b - PadSize / 2, 0f), new Vector3(W, PadSize, PadSize));
                    LedFrame(g, -W / 2, W / 2, b, b + h1, PadSize / 2 + 0.002f);
                    LedFrame(g, -W / 2, W / 2, b + h1 + PadSize, top, PadSize / 2 + 0.002f);
                    foreach (float s in new[] { -1f, 1f }) Base(g, new Vector3(s * x, 0f, 0f), 0f);
                    gates.Add((new Vector3(0f, (b + top) / 2, 0f), new Vector2(W, top - b), Quaternion.identity));
                    // Both windows count, but aim (autopilot, HUD, respawn) at the lower one: the centre is the middle bar.
                    aim = new Vector3(0f, b + h1 / 2, 0f);
                    break;
                }

                case "Slalom":
                    // Three smaller gates weaving left-right-left, 5 m apart; each one is a checkpoint.
                    W = 1.4f; H = 1.4f; b = 0.8f;
                    for (int i = 0; i < 3; i++)
                    {
                        var offset = new Vector3(i == 1 ? 1.2f : -1.2f, 0f, (i - 1) * 5f);
                        g.Frame = Matrix4x4.Translate(offset);
                        PaddedGate(g, W, H, b);
                        g.Frame = Matrix4x4.identity;
                        gates.Add((offset + new Vector3(0f, b + H / 2, 0f), new Vector2(W, H), Quaternion.identity));
                    }
                    pad = "M_Kit_Pad_White";
                    break;

                case "Cube":
                {
                    float C = 2.6f, e = C / 2 - TrussSize / 2;
                    frame = "M_Kit_Truss_Green";
                    pad = "M_Kit_Pad_Green";
                    foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        Truss(g, new Vector3(sx * e, 0f, sz * e), new Vector3(sx * e, C, sz * e));
                        // Barrels on the outside of the back corners (Base puts the barrel on its local +X).
                        Base(g, new Vector3(sx * e, 0f, sz * e), sx > 0f ? 0f : 180f, barrel: sz > 0f);
                    }
                    foreach (float y in new[] { TrussSize / 2, C - TrussSize / 2 })
                    {
                        foreach (float s in new[] { -1f, 1f })
                        {
                            Truss(g, new Vector3(-e + TrussSize / 2, y, s * e), new Vector3(e - TrussSize / 2, y, s * e));
                            Truss(g, new Vector3(s * e, y, -e + TrussSize / 2), new Vector3(s * e, y, e - TrussSize / 2));
                        }
                    }
                    float inner = C - 2f * TrussSize;
                    foreach (float s in new[] { -1f, 1f })
                        Net(g, new Vector3(s * e, C / 2, 0f), new Vector2(inner, inner), new Vector3(s, 0f, 0f), Vector3.up);
                    // Green pads along the bottom front edge.
                    Pad(g, new Vector3(0f, TrussSize + 0.12f, e + 0.02f), new Vector3(inner, 0.24f, 0.24f));
                    LedFrame(g, -inner / 2, inner / 2, TrussSize + 0.24f, C - TrussSize, e + TrussSize / 2 + 0.002f);
                    gates.Add((new Vector3(0f, C / 2 + 0.12f, 0f), new Vector2(inner, C - 2f * TrussSize - 0.24f), Quaternion.identity));
                    break;
                }

                case "Ladder":
                {
                    // H hurdle with a second rung on top: fly over the low rung (the 1.5 m flight line clears it) or dive under it.
                    float x = 1.8f, top = 3.4f, rung = 1.0f;
                    frame = "M_Kit_Truss_Green";
                    pad = "M_Kit_Pad_Green";
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Truss(g, new Vector3(s * x, 0f, 0f), new Vector3(s * x, top, 0f));
                        Base(g, new Vector3(s * x, 0f, 0f), 0f);
                    }
                    Truss(g, new Vector3(-x + TrussSize / 2, top - TrussSize / 2, 0f), new Vector3(x - TrussSize / 2, top - TrussSize / 2, 0f));
                    Pad(g, new Vector3(0f, rung, 0f), new Vector3(2f * x - TrussSize, PadSize, PadSize));
                    foreach (float face in new[] { 1f, -1f })
                    {
                        var n = new Vector3(0f, 0f, face);
                        Led(g, new Vector3(-x + 0.2f, rung, face * (PadSize / 2 + 0.002f)), new Vector3(x - 0.2f, rung, face * (PadSize / 2 + 0.002f)), n);
                        Led(g, new Vector3(-x + 0.2f, top - TrussSize, face * (TrussSize / 2 + 0.002f)), new Vector3(x - 0.2f, top - TrussSize, face * (TrussSize / 2 + 0.002f)), n);
                    }
                    gates.Add((new Vector3(0f, (top - TrussSize) / 2, 0f), new Vector2(2f * x - TrussSize, top - TrussSize), Quaternion.identity));
                    break;
                }

                case "Dive":
                {
                    // A padded frame tilted 45° (top away from the pilot) on axles between two truss posts: the opening
                    // faces up-and-back, so you fly over it and dive down through it.
                    W = 1.8f; H = 1.8f; b = PadSize;
                    float pivot = 1.4f, x = W / 2 + PadSize + TrussSize / 2 + 0.05f;
                    pad = "M_Kit_Pad_Blue";
                    var tilt = Quaternion.Euler(45f, 0f, 0f);
                    Vector3 mid = new Vector3(0f, pivot, 0f) + tilt * new Vector3(0f, b + H / 2, 0f);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Truss(g, new Vector3(s * x, 0f, mid.z), new Vector3(s * x, mid.y + 0.45f, mid.z));
                        Base(g, new Vector3(s * x, 0f, mid.z), 0f);
                        for (int lod = 0; lod < 2; lod++)
                            g.M(lod, Slot.Metal).AddBeam(0, new Vector3(s * (W / 2 + PadSize * 0.9f), mid.y, mid.z), new Vector3(s * (x - TrussSize / 2), mid.y, mid.z), 0.07f);
                    }
                    g.Frame = Matrix4x4.TRS(new Vector3(0f, pivot, 0f), tilt, Vector3.one);
                    PaddedRect(g, W, H, b);
                    g.Frame = Matrix4x4.identity;
                    gates.Add((mid, new Vector2(W, H), tilt));
                    break;
                }

                case "Ramp":
                {
                    // Platform 2 m up on four truss legs, a plywood ramp up to it, a padded gate on the platform.
                    float deck = 2f, x = 1.45f;
                    frame = "M_Kit_Truss_Black";
                    foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        Truss(g, new Vector3(sx * x, 0f, sz * 0.9f), new Vector3(sx * x, deck - 0.05f, sz * 0.9f));
                        Base(g, new Vector3(sx * x, 0f, sz * 0.9f), 0f);
                    }
                    g.M(0, Slot.Wood).AddBox(0, new Vector3(0f, deck, 0f), Quaternion.identity, new Vector3(3.2f, 0.06f, 2.2f));
                    g.M(1, Slot.Wood).AddBox(0, new Vector3(0f, deck, 0f), Quaternion.identity, new Vector3(3.2f, 0.06f, 2.2f));
                    g.M(2, Slot.Wood).AddBox(0, new Vector3(0f, deck / 2, 0f), Quaternion.identity, new Vector3(3.2f, deck, 2.2f));
                    g.Collider(new Vector3(0f, deck, 0f), new Vector3(3.2f, 0.06f, 2.2f), Quaternion.identity);
                    var a = new Vector3(0f, 0f, -5.2f);
                    var e = new Vector3(0f, deck, -1.1f);
                    var rampRot = Quaternion.LookRotation(e - a);
                    float rl = Vector3.Distance(a, e);
                    for (int lod = 0; lod < 2; lod++)
                    {
                        g.M(lod, Slot.Wood).AddBox(0, (a + e) / 2, rampRot, new Vector3(2.4f, 0.05f, rl));
                        g.M(lod, Slot.Print).AddQuad(0, (a + e) / 2 + rampRot * Vector3.up * 0.03f, rampRot * Quaternion.Euler(0f, -90f, 0f), new Vector2(rl * 0.6f, 1.6f));
                    }
                    g.Collider((a + e) / 2, new Vector3(2.4f, 0.05f, rl), rampRot);
                    g.Frame = Matrix4x4.Translate(new Vector3(0f, deck, 0f));
                    PaddedGate(g, 1.8f, 1.6f, 0.3f, bases: false);
                    g.Frame = Matrix4x4.identity;
                    gates.Add((new Vector3(0f, deck + 0.3f + 0.8f, 0f), new Vector2(1.8f, 1.6f), Quaternion.identity));
                    break;
                }

                case "Arch":
                {
                    // Semicircular padded arch on short legs, white.
                    float R = 1.9f, legs = 1.1f;
                    pad = "M_Kit_Pad_White";
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Pad(g, new Vector3(s * R, legs / 2, 0f), new Vector3(PadSize, legs, PadSize));
                        Base(g, new Vector3(s * R, 0f, 0f), 0f);
                    }
                    const int seg = 10;
                    for (int i = 0; i < seg; i++)
                    {
                        float a0 = Mathf.PI * i / seg, a1 = Mathf.PI * (i + 1) / seg;
                        var p0 = new Vector3(Mathf.Cos(a0) * R, legs + Mathf.Sin(a0) * R, 0f);
                        var p1 = new Vector3(Mathf.Cos(a1) * R, legs + Mathf.Sin(a1) * R, 0f);
                        var rot = Quaternion.FromToRotation(Vector3.right, (p1 - p0).normalized);
                        Pad(g, (p0 + p1) / 2, new Vector3(Vector3.Distance(p0, p1) + 0.06f, PadSize, PadSize), rot);
                        // LED on the inner edge, both faces.
                        Vector3 inward = -new Vector3(Mathf.Cos((a0 + a1) / 2), Mathf.Sin((a0 + a1) / 2), 0f) * (PadSize / 2);
                        foreach (float face in new[] { 1f, -1f })
                            Led(g, p0 + inward * 0.6f + Vector3.forward * face * (PadSize / 2 + 0.002f), p1 + inward * 0.6f + Vector3.forward * face * (PadSize / 2 + 0.002f), new Vector3(0f, 0f, face));
                    }
                    gates.Add((new Vector3(0f, (legs + R * 0.8f) / 2 + 0.2f, 0f), new Vector2(2f * R - PadSize, legs + R * 0.8f - 0.2f), Quaternion.identity));
                    break;
                }

                default: throw new System.ArgumentException("unknown obstacle " + type);
            }

            var materials = Materials(frame, pad);
            var root = Assemble("Obstacle_" + type, g, materials, out var ledRenderer);
            for (int i = 0; i < gates.Count; i++)
            {
                var gt = gates.Count == 1 ? root : new GameObject($"Checkpoint {i}");
                if (gt != root) gt.transform.SetParent(root.transform, false);
                // The Gate's opening is defined in its own space: put the child at the opening's base.
                Vector3 baseLocal = gates[i].center - gates[i].rotation * new Vector3(0f, gates[i].size.y / 2, 0f);
                Vector3 openingCenter = new Vector3(0f, gates[i].size.y / 2, 0f);
                if (gt == root)
                {
                    openingCenter = Quaternion.Inverse(gates[i].rotation) * gates[i].center;
                    if (gates[i].rotation != Quaternion.identity)
                    {
                        gt = new GameObject("Checkpoint");
                        gt.transform.SetParent(root.transform, false);
                        gt.transform.SetLocalPositionAndRotation(baseLocal, gates[i].rotation);
                        openingCenter = new Vector3(0f, gates[i].size.y / 2, 0f);
                    }
                }
                else gt.transform.SetLocalPositionAndRotation(baseLocal, gates[i].rotation);
                var gate = gt.AddComponent<Gate>();
                var so = new SerializedObject(gate);
                so.FindProperty("openingSize").vector2Value = gates[i].size;
                so.FindProperty("openingCenter").vector3Value = openingCenter;
                so.FindProperty("idleColor").colorValue = idle;
                if (aim.HasValue)
                {
                    so.FindProperty("useAimPoint").boolValue = true;
                    so.FindProperty("aimPoint").vector3Value = gt.transform.InverseTransformPoint(root.transform.TransformPoint(aim.Value));
                }
                var fr = so.FindProperty("frameRenderers");
                fr.arraySize = 1;
                fr.GetArrayElementAtIndex(0).objectReferenceValue = ledRenderer;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var hl = root.AddComponent<NextGateHighlight>();
            hl.Leds = new[] { ledRenderer };
            hl.Show(GateVisualState.Idle, idle);
            return root;
        }

        /// <summary>A padded rectangular gate (posts, top bar, bottom bar), its LEDs and bases, in the current frame.</summary>
        private static void PaddedGate(Geo g, float W, float H, float b, bool bases = true)
        {
            PaddedRect(g, W, H, b);
            if (!bases) return;
            float x = W / 2 + PadSize / 2;
            foreach (float s in new[] { -1f, 1f }) Base(g, new Vector3(s * x, 0f, 0f), 0f);
        }

        private static void PaddedRect(Geo g, float W, float H, float b)
        {
            float x = W / 2 + PadSize / 2, top = b + H;
            foreach (float s in new[] { -1f, 1f }) Pad(g, new Vector3(s * x, (top + PadSize) / 2, 0f), new Vector3(PadSize, top + PadSize, PadSize));
            Pad(g, new Vector3(0f, top + PadSize / 2, 0f), new Vector3(W, PadSize, PadSize));
            if (b > PadSize * 0.5f) Pad(g, new Vector3(0f, b - PadSize / 2, 0f), new Vector3(W, PadSize, PadSize));
            LedFrame(g, -W / 2, W / 2, b, top, PadSize / 2 + 0.002f);
        }

        // =====================================================================
        // Props
        // =====================================================================

        private static GameObject BuildProp(string type)
        {
            var g = new Geo();
            GameObject root;
            switch (type)
            {
                case "ChevronBoard":
                case "ChevronBoardLeft":
                {
                    // Printed board facing the approaching pilot (−Z), arrows pointing into the turn.
                    bool left = type == "ChevronBoardLeft";
                    float bw = 1.4f, bh = 0.7f, y = 0.85f;
                    var face = left ? Quaternion.LookRotation(Vector3.up, Vector3.back) * Quaternion.Euler(0f, 180f, 0f) : Quaternion.LookRotation(Vector3.up, Vector3.back);
                    for (int lod = 0; lod < 3; lod++)
                    {
                        g.M(lod, Slot.Print).AddQuad(0, new Vector3(0f, y, -0.012f), face, new Vector2(bw, bh));
                        g.M(lod, Slot.Metal).AddBox(0, new Vector3(0f, y, 0f), Quaternion.identity, new Vector3(bw + 0.02f, bh + 0.02f, 0.02f));
                    }
                    foreach (float s in new[] { -0.55f, 0.55f })
                    {
                        g.M(0, Slot.Metal).AddTube(0, new[] { new Vector3(s, 0f, 0.03f), new Vector3(s, y + bh / 2, 0.03f) }, 0.025f, 8);
                        g.M(1, Slot.Metal).AddBox(0, new Vector3(s, (y + bh / 2) / 2, 0.03f), Quaternion.identity, new Vector3(0.05f, y + bh / 2, 0.05f));
                        g.M(0, Slot.Metal).AddBox(0, new Vector3(s, 0.01f, 0.03f), Quaternion.identity, new Vector3(0.35f, 0.02f, 0.35f));
                    }
                    g.Collider(new Vector3(0f, y, 0f), new Vector3(bw, bh, 0.04f), Quaternion.identity);
                    root = Assemble("Obstacle_" + type, g, Materials("M_Kit_Truss_Black", "M_Kit_Pad_White", print: "M_Kit_ChevronSign"), out _);
                    break;
                }

                case "Flag":
                {
                    // Feather flag: pole on a cross foot + the cloth on its own child so FlagWind can swing it.
                    float h = 3.3f;
                    g.M(0, Slot.Metal).AddTube(0, new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, h, 0f) }, 0.016f, 8);
                    g.M(1, Slot.Metal).AddBox(0, new Vector3(0f, h / 2, 0f), Quaternion.identity, new Vector3(0.03f, h, 0.03f));
                    foreach (float yaw in new[] { 0f, 90f })
                        g.M(0, Slot.Black).AddBox(0, new Vector3(0f, 0.02f, 0f), Quaternion.Euler(0f, yaw, 0f), new Vector3(0.9f, 0.04f, 0.06f));
                    g.Collider(new Vector3(0f, h / 2, 0f), new Vector3(0.05f, h, 0.05f), Quaternion.identity);
                    root = Assemble("Obstacle_Flag", g, Materials("M_Kit_Truss_Black", "M_Kit_Pad_White"), out _);
                    var cloth = new GameObject("Cloth") { layer = root.layer };
                    cloth.transform.SetParent(root.transform, false);
                    cloth.AddComponent<MeshFilter>().sharedMesh = ArtPass.MeshAsset("KitFlagCloth", () => FlagCloth(h));
                    var mr = cloth.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = MaterialLibrary.Get("M_Kit_Flag");
                    var wind = root.AddComponent<FlagWind>();
                    wind.Cloth = cloth.transform;
                    AddToLods(root, mr, 2);
                    break;
                }

                case "LandingPad":
                {
                    const float r = 1.5f;
                    for (int lod = 0; lod < 3; lod++)
                    {
                        g.M(lod, Slot.Print).AddDisc(0, new Vector3(0f, 0.03f, 0f), r, lod == 0 ? 48 : lod == 1 ? 24 : 12);
                    }
                    var ring = new List<Vector3>();
                    for (int k = 0; k <= 48; k++)
                    {
                        float a = k * Mathf.PI * 2f / 48;
                        ring.Add(new Vector3(Mathf.Cos(a) * r, 0.022f, Mathf.Sin(a) * r));
                    }
                    g.M(0, Slot.Black).AddTube(0, ring, 0.025f, 6);
                    g.Collider(new Vector3(0f, 0.02f, 0f), new Vector3(2f * r * 0.9f, 0.04f, 2f * r * 0.9f), Quaternion.identity);
                    root = Assemble("Obstacle_LandingPad", g, Materials("M_Kit_Truss_Black", "M_Kit_Pad_White", print: "M_Kit_LandingPad"), out _);
                    break;
                }

                case "StartPad":
                {
                    // Four numbered slots side by side (6 m wide, 2.5 m deep), chevrons pointing along +Z.
                    var size = new Vector3(6f, 0.04f, 2.5f);
                    for (int lod = 0; lod < 3; lod++)
                    {
                        g.M(lod, Slot.Print).AddQuad(0, new Vector3(0f, size.y + 0.001f, 0f), Quaternion.identity, new Vector2(size.x, size.z));
                        g.M(lod, Slot.Black).AddBox(0, new Vector3(0f, size.y / 2, 0f), Quaternion.identity, size);
                    }
                    g.Collider(new Vector3(0f, size.y / 2, 0f), size, Quaternion.identity);
                    root = Assemble("Obstacle_StartPad", g, Materials("M_Kit_Truss_Black", "M_Kit_Pad_White", print: "M_Kit_StartGrid"), out _);
                    break;
                }

                default: throw new System.ArgumentException("unknown prop " + type);
            }
            return root;
        }

        /// <summary>Feather-flag cloth with UV 0..1 over the print (pole edge at U = 0, top at V = 1).</summary>
        private static Mesh FlagCloth(float height)
        {
            var tm = new TrackMesh(1);
            var verts = new List<Vector3>();
            // Build as a strip of quads: easiest with the TrackMesh quad helper on a curved outline.
            const int rows = 12;
            const float width = 0.75f, bottom = 0.6f;
            for (int i = 0; i < rows; i++)
            {
                float t0 = (float)i / rows, t1 = (float)(i + 1) / rows;
                float y0 = bottom + t0 * (height - bottom - 0.05f), y1 = bottom + t1 * (height - bottom - 0.05f);
                float w0 = width * (t0 < 0.8f ? 1f : Mathf.Lerp(1f, 0.3f, (t0 - 0.8f) / 0.2f));
                float w1 = width * (t1 < 0.8f ? 1f : Mathf.Lerp(1f, 0.3f, (t1 - 0.8f) / 0.2f));
                verts.Add(new Vector3(0.02f, y0, 0f));
                verts.Add(new Vector3(0.02f + w0, y0 + (t0 > 0.8f ? (t0 - 0.8f) * 0.8f : 0f), Mathf.Sin(t0 * Mathf.PI) * 0.1f));
                if (i == rows - 1)
                {
                    verts.Add(new Vector3(0.02f, y1, 0f));
                    verts.Add(new Vector3(0.02f + w1, y1 + (t1 - 0.8f) * 0.8f, Mathf.Sin(t1 * Mathf.PI) * 0.1f));
                }
            }
            var mesh = new Mesh { name = "KitFlagCloth" };
            var uv = new List<Vector2>();
            for (int i = 0; i < verts.Count; i += 2)
            {
                float v = (float)(i / 2) / rows;
                uv.Add(new Vector2(0f, v));
                uv.Add(new Vector2(1f, v));
            }
            var tris = new List<int>();
            for (int i = 0; i < rows; i++)
            {
                int a = i * 2;
                tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // =====================================================================
        // Assembly: LOD renderers, LED renderer, colliders, LODGroup
        // =====================================================================

        private static Material[] Materials(string frame, string pad, string print = "M_Kit_ChevronSign") => new[]
        {
            MaterialLibrary.Get(frame), MaterialLibrary.Get(pad), MaterialLibrary.Get("M_Aluminium_Brushed"), MaterialLibrary.Get("M_Fabric_Sandbag"),
            MaterialLibrary.Get("M_Plastic_Black"), MaterialLibrary.Get("M_Kit_Net"), MaterialLibrary.Get("M_Plastic_ConeOrange"),
            MaterialLibrary.Get("M_SafetyStripes"), MaterialLibrary.Get("M_Wood_Plywood"), MaterialLibrary.Get(print),
        };

        private static GameObject Assemble(string name, Geo g, Material[] materials, out Renderer ledRenderer)
        {
            int layer = LayerMask.NameToLayer("TrackGeometry");
            if (layer < 0) layer = 0;
            var root = new GameObject(name) { layer = layer };
            var lodRenderers = new List<Renderer>[3];
            for (int lod = 0; lod < 3; lod++)
            {
                lodRenderers[lod] = new List<Renderer>();
                var mesh = g.Build(lod, $"{name}_LOD{lod}", out int slots);
                if (mesh.vertexCount == 0)
                {
                    Object.DestroyImmediate(mesh);
                    continue;
                }
                var go = new GameObject($"LOD{lod}") { layer = layer };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = ArtPass.MeshAsset($"{name}_LOD{lod}", () => mesh);
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[slots];
                System.Array.Copy(materials, mats, slots);
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = lod == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                lodRenderers[lod].Add(mr);
            }

            ledRenderer = null;
            if (!g.Leds.IsEmpty)
            {
                var go = new GameObject("LEDs") { layer = layer };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = ArtPass.MeshAsset($"{name}_LEDs", () => g.Leds.ToMesh($"{name}_LEDs"));
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialLibrary.Get("M_LED_Gate");
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                ledRenderer = mr;
                lodRenderers[0].Add(mr);
                lodRenderers[1].Add(mr);
            }

            var colliders = new GameObject("Colliders") { layer = layer };
            colliders.transform.SetParent(root.transform, false);
            int n = 0;
            foreach (var (center, size, rotation) in g.Colliders)
            {
                // BoxCollider sizes are local: axis-aligned members go on one object, rotated ones get their own.
                if (Quaternion.Angle(rotation, Quaternion.identity) < 0.01f || IsAxisSwap(rotation, out _))
                {
                    var box = colliders.AddComponent<BoxCollider>();
                    box.center = center;
                    box.size = IsAxisSwap(rotation, out var swapped) ? Swizzle(size, swapped) : size;
                }
                else
                {
                    var child = new GameObject($"Collider {n++}") { layer = layer };
                    child.transform.SetParent(colliders.transform, false);
                    child.transform.SetLocalPositionAndRotation(center, rotation);
                    child.AddComponent<BoxCollider>().size = size;
                }
            }

            var group = root.AddComponent<LODGroup>();
            var lods = new List<LOD>();
            for (int lod = 0; lod < 3; lod++)
                if (lodRenderers[lod].Count > 0) lods.Add(new LOD(LodHeights[lod], lodRenderers[lod].ToArray()));
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
            return root;
        }

        private static void AddToLods(GameObject root, Renderer r, int upTo)
        {
            var group = root.GetComponent<LODGroup>();
            var lods = group.GetLODs();
            for (int i = 0; i < lods.Length && i < upTo; i++)
            {
                var list = new List<Renderer>(lods[i].renderers) { r };
                lods[i].renderers = list.ToArray();
            }
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        /// <summary>True if <paramref name="q"/> only permutes axes (90° multiples); <paramref name="map"/> maps local size axes to root axes.</summary>
        private static bool IsAxisSwap(Quaternion q, out Vector3Int map)
        {
            map = new Vector3Int(0, 1, 2);
            Vector3[] axes = { q * Vector3.right, q * Vector3.up, q * Vector3.forward };
            for (int i = 0; i < 3; i++)
            {
                Vector3 a = axes[i];
                int k = Mathf.Abs(a.x) > 0.999f ? 0 : Mathf.Abs(a.y) > 0.999f ? 1 : Mathf.Abs(a.z) > 0.999f ? 2 : -1;
                if (k < 0) return false;
                map[i] = k;
            }
            return true;
        }

        private static Vector3 Swizzle(Vector3 size, Vector3Int map)
        {
            var s = Vector3.zero;
            for (int i = 0; i < 3; i++) s[map[i]] = size[i];
            return s;
        }
    }
}
