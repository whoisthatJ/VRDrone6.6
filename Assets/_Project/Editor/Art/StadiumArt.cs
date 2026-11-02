using System.Collections.Generic;
using GateRush.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Track_Stadium (T3): a football stadium with an athletics track. Striped 105 × 68 m pitch with full markings and
    /// goals, a red 8-lane 400 m tartan track, two roofed 24-row stands on the long sides (seats drawn with GPU
    /// instancing), dugouts, scrolling perimeter LED boards and four floodlight towers. Nothing is baked: three
    /// time-of-day presets (night under floodlights, dusk, day) switch at runtime through <see cref="StadiumLighting"/>.
    /// </summary>
    public static class StadiumArt
    {
        // Pitch (x along the length) and the 400 m track: inner straight 84.39 m, inner radius 36.5 m, 8 × 1.22 m lanes.
        public const float PitchLength = 105f, PitchWidth = 68f;
        public const float TrackHalfStraight = 42.195f, TrackInnerRadius = 36.5f, LaneWidth = 1.22f;
        public const int Lanes = 8;
        public static float TrackOuterRadius => TrackInnerRadius + Lanes * LaneWidth;
        public const float ApronOuterRadius = 51.5f;
        // Stands: front wall at |z| = StandFront, rows outwards.
        public const float StandFront = 53f, StandHalfLength = 55f, RowDepth = 0.85f, RowRise = 0.42f, FrontWall = 2f;
        public const int Rows = 24;
        public const float BoardZ = 35.4f, BoardEndX = 56f, BoardHeight = 0.9f;
        public static readonly Vector2[] TowerPositions = { new Vector2(72f, 62f), new Vector2(-72f, 62f), new Vector2(72f, -62f), new Vector2(-72f, -62f) };
        public const float TowerHeight = 40f;

        public static void Apply(ArtContext ctx)
        {
            var ground = ArtPass.TileGround(ctx, "M_Asphalt_Old");
            ctx.Log.Append(ground != null ? " ground tiled;" : " no ground;");

            BuildInfieldAndTrack(ctx);
            BuildPitch(ctx);
            BuildGoals(ctx);
            var seats = new List<(Matrix4x4 m, bool blue)>();
            foreach (int side in new[] { 1, -1 }) BuildStand(ctx, side, seats);
            BuildSeats(ctx, seats);
            BuildDugouts(ctx);
            BuildBoards(ctx);
            var (spots, heads) = BuildTowers(ctx);
            var scales = new List<float>();
            foreach (var _ in spots) scales.Add(1f);
            var all = new List<Light>(spots);
            foreach (var l in BuildStandLights(ctx))
            {
                all.Add(l);
                scales.Add(StandLightScale);
            }
            BuildLighting(ctx, all.ToArray(), heads, scales.ToArray());
        }

        // ---------- oval geometry ----------

        /// <summary>
        /// Samples the stadium-shaped track skeleton anticlockwise: (centre on the x axis segment, outward normal).
        /// A point at offset r is centre + normal × r.
        /// </summary>
        public static List<(Vector3 c, Vector3 n)> Skeleton(int perStraight = 12, int perArc = 48)
        {
            var s = new List<(Vector3, Vector3)>();
            float h = TrackHalfStraight;
            for (int i = 0; i < perStraight; i++) s.Add((new Vector3(-h + 2f * h * i / perStraight, 0f, 0f), Vector3.back));
            for (int i = 0; i < perArc; i++)
            {
                float a = -Mathf.PI / 2 + Mathf.PI * i / perArc;
                s.Add((new Vector3(h, 0f, 0f), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))));
            }
            for (int i = 0; i < perStraight; i++) s.Add((new Vector3(h - 2f * h * i / perStraight, 0f, 0f), Vector3.forward));
            for (int i = 0; i < perArc; i++)
            {
                float a = Mathf.PI / 2 + Mathf.PI * i / perArc;
                s.Add((new Vector3(-h, 0f, 0f), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))));
            }
            return s;
        }

        /// <summary>Flat closed band between offsets r0 and r1 at height y; UV in metres (u along the centre line, v across).</summary>
        public static Mesh OvalBand(float r0, float r1, float y, string name, int perArc = 48)
        {
            var sk = Skeleton(12, perArc);
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<Vector4>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            float rMid = (r0 + r1) / 2f, along = 0f;
            for (int i = 0; i <= sk.Count; i++)
            {
                var (c, nn) = sk[i % sk.Count];
                if (i > 0)
                {
                    var (pc, pn) = sk[i - 1];
                    along += Vector3.Distance(pc + pn * rMid, c + nn * rMid);
                }
                var dir = Vector3.Cross(Vector3.up, nn).normalized * -1f; // anticlockwise travel direction
                var tan = new Vector4(dir.x, dir.y, dir.z, Vector3.Dot(Vector3.Cross(Vector3.up, dir), nn) >= 0f ? 1f : -1f);
                foreach (float r in new[] { r0, r1 })
                {
                    v.Add(c + nn * r + Vector3.up * y);
                    n.Add(Vector3.up);
                    t.Add(tan);
                    uv.Add(new Vector2(along, r));
                }
                if (i > 0)
                {
                    int b = v.Count - 4;
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTangents(t);
            m.SetUVs(0, uv);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return ProcMesh.FixWindingMetres(m);
        }

        private static Transform G(ArtContext ctx, string name) => ArtPass.Group(ctx, name);

        private static GameObject Add(ArtContext ctx, string group, string name, Mesh mesh, Material[] mats, bool solid = false, bool castShadows = true) =>
            ArtPass.AddMesh(G(ctx, group), name, ArtPass.MeshAsset("Stadium_" + name.Replace(' ', '_'), () => mesh), mats,
                Vector3.zero, Quaternion.identity, Vector3.one, false, solid, castShadows);

        /// <summary>A box collider on TrackGeometry (for parts whose combined bounds would swallow open space).</summary>
        private static void Collider(Transform parent, string name, Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var go = new GameObject(name) { layer = ArtPass.TrackGeometryLayer };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.AddComponent<BoxCollider>().size = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        // ---------- ground surfaces ----------

        private static void BuildInfieldAndTrack(ArtContext ctx)
        {
            Add(ctx, "Ground", "Infield", OvalBand(0f, TrackInnerRadius, 0.01f, "Infield"), ArtPass.Mats("M_Stadium_Grass_Dark"), castShadows: false);
            Add(ctx, "Ground", "Tartan", OvalBand(TrackInnerRadius, TrackOuterRadius, 0.02f, "Tartan"), ArtPass.Mats("M_Stadium_Tartan"), castShadows: false);
            Add(ctx, "Ground", "Apron", OvalBand(TrackOuterRadius, ApronOuterRadius, 0.015f, "Apron"), ArtPass.Mats("M_Concrete_PaintedFloor"), castShadows: false);

            // Lane lines (9 boundaries) and a kerb on the inside edge.
            var parts = new List<(Mesh, Matrix4x4, int)>();
            for (int i = 0; i <= Lanes; i++)
            {
                float r = TrackInnerRadius + i * LaneWidth;
                parts.Add((OvalBand(r - 0.025f, r + 0.025f, 0.026f, "Lane"), Matrix4x4.identity, 0));
            }
            // 100 m start / finish line across the lanes at the end of the home (south) straight.
            parts.Add(ProcMesh.Part(new Vector3(TrackHalfStraight, 0.028f, -(TrackInnerRadius + TrackOuterRadius) / 2f), new Vector3(0.05f, 0.004f, Lanes * LaneWidth), Quaternion.identity));
            Add(ctx, "Ground", "Lane Lines", ProcMesh.Combine("LaneLines", parts), ArtPass.Mats("M_Stadium_LinePaint"), castShadows: false);
            var kerb = new List<(Mesh, Matrix4x4, int)> { (OvalBand(TrackInnerRadius - 0.06f, TrackInnerRadius, 0.06f, "Kerb"), Matrix4x4.identity, 0) };
            Add(ctx, "Ground", "Kerb", ProcMesh.Combine("Kerb", kerb), ArtPass.Mats("M_Aluminium_Brushed"), castShadows: false);
            ctx.Log.Append(" track;");
        }

        private static void BuildPitch(ArtContext ctx)
        {
            const float y = 0.03f;
            var parts = new List<(Mesh, Matrix4x4, int)>();
            const int stripes = 14;
            float sw = PitchLength / stripes;
            for (int i = 0; i < stripes; i++)
                parts.Add(ProcMesh.Part(new Vector3(-PitchLength / 2 + sw * (i + 0.5f), y / 2, 0f), new Vector3(sw, y, PitchWidth + 4f), Quaternion.identity, i % 2));

            // Markings (submesh 2): 12 cm lines.
            const float w = 0.12f;
            float hx = PitchLength / 2, hz = PitchWidth / 2;
            Vector3 P(float x, float z) => new Vector3(x, y, z);
            void Line(float x0, float z0, float x1, float z1) => ProcMesh.Strip(parts, P(x0, z0), P(x1, z1), w, 0.004f, 2);
            Line(-hx, -hz, hx, -hz);
            Line(-hx, hz, hx, hz);
            Line(-hx, -hz, -hx, hz);
            Line(hx, -hz, hx, hz);
            Line(0f, -hz, 0f, hz);
            int before = parts.Count;
            ProcMesh.Ring(parts, P(0f, 0f), 9.15f, w, 64);
            for (int i = before; i < parts.Count; i++) parts[i] = (parts[i].Item1, parts[i].Item2, 2);
            foreach (int s in new[] { -1, 1 })
            {
                float gx = s * hx;
                // Penalty area 16.5 × 40.32, goal area 5.5 × 18.32, spot at 11 m, arc r 9.15 outside the area.
                Line(gx, -20.16f, gx - s * 16.5f, -20.16f);
                Line(gx, 20.16f, gx - s * 16.5f, 20.16f);
                Line(gx - s * 16.5f, -20.16f, gx - s * 16.5f, 20.16f);
                Line(gx, -9.16f, gx - s * 5.5f, -9.16f);
                Line(gx, 9.16f, gx - s * 5.5f, 9.16f);
                Line(gx - s * 5.5f, -9.16f, gx - s * 5.5f, 9.16f);
                parts.Add(ProcMesh.Part(P(gx - s * 11f, 0f) + Vector3.up * 0.002f, new Vector3(0.22f, 0.004f, 0.22f), Quaternion.identity, 2));
                Vector3 spot = P(gx - s * 11f, 0f);
                float a0 = Mathf.Acos(5.5f / 9.15f);
                const int seg = 16;
                for (int k = 0; k < seg; k++)
                {
                    float t0 = -a0 + 2f * a0 * k / seg, t1 = -a0 + 2f * a0 * (k + 1) / seg;
                    Vector3 d0 = new Vector3(-s * Mathf.Cos(t0), 0f, Mathf.Sin(t0)) * 9.15f, d1 = new Vector3(-s * Mathf.Cos(t1), 0f, Mathf.Sin(t1)) * 9.15f;
                    ProcMesh.Strip(parts, spot + d0, spot + d1, w, 0.004f, 2);
                }
                // Corner arcs (r 1 m).
                foreach (int c in new[] { -1, 1 })
                    for (int k = 0; k < 6; k++)
                    {
                        float t0 = Mathf.PI / 2 * k / 6, t1 = Mathf.PI / 2 * (k + 1) / 6;
                        Vector3 corner = P(gx, c * hz);
                        ProcMesh.Strip(parts, corner + new Vector3(-s * Mathf.Cos(t0), 0f, -c * Mathf.Sin(t0)), corner + new Vector3(-s * Mathf.Cos(t1), 0f, -c * Mathf.Sin(t1)), w, 0.004f, 2);
                    }
            }
            parts.Add(ProcMesh.Part(P(0f, 0f) + Vector3.up * 0.002f, new Vector3(0.3f, 0.004f, 0.3f), Quaternion.identity, 2));
            Add(ctx, "Ground", "Pitch", ProcMesh.Combine("Pitch", parts), ArtPass.Mats("M_Stadium_Grass_Light", "M_Stadium_Grass_Dark", "M_Stadium_LinePaint"), castShadows: false);
            ctx.Log.Append(" pitch;");
        }

        private static void BuildGoals(ArtContext ctx)
        {
            const float halfW = 3.66f, h = 2.44f, post = 0.12f, depth = 2f;
            foreach (int s in new[] { -1, 1 })
            {
                var parts = new List<(Mesh, Matrix4x4, int)>();
                float gx = s * PitchLength / 2;
                float back = gx + s * depth;
                foreach (float z in new[] { -halfW - post / 2, halfW + post / 2 })
                {
                    parts.Add(ProcMesh.Part(new Vector3(gx + s * post / 2, h / 2, z), new Vector3(post, h, post), Quaternion.identity));
                    // Net supports: top bar back to the rear frame, rear post.
                    parts.Add(ProcMesh.Part(new Vector3((gx + back) / 2, h - 0.02f, z), new Vector3(depth, 0.04f, 0.04f), Quaternion.identity));
                    parts.Add(ProcMesh.Part(new Vector3(back, h / 2, z), new Vector3(0.04f, h, 0.04f), Quaternion.identity));
                    parts.Add(ProcMesh.Part(new Vector3((gx + back) / 2, 0.02f, z), new Vector3(depth, 0.04f, 0.04f), Quaternion.identity));
                }
                parts.Add(ProcMesh.Part(new Vector3(gx + s * post / 2, h + post / 2, 0f), new Vector3(post, post, 2 * halfW + 2 * post), Quaternion.identity));
                parts.Add(ProcMesh.Part(new Vector3(back, h - 0.02f, 0f), new Vector3(0.04f, 0.04f, 2 * halfW), Quaternion.identity));
                // Net (submesh 1): back, top and both sides.
                parts.Add(ProcMesh.Part(new Vector3(back, h / 2, 0f), new Vector3(0.01f, h, 2 * halfW), Quaternion.identity, 1));
                parts.Add(ProcMesh.Part(new Vector3((gx + back) / 2, h, 0f), new Vector3(depth, 0.01f, 2 * halfW), Quaternion.identity, 1));
                foreach (float z in new[] { -halfW, halfW })
                    parts.Add(ProcMesh.Part(new Vector3((gx + back) / 2, h / 2, z), new Vector3(depth, h, 0.01f), Quaternion.identity, 1));
                Add(ctx, "Goals", s > 0 ? "Goal East" : "Goal West", ProcMesh.Combine("Goal", parts), ArtPass.Mats("M_Plastic_White", "M_Kit_Net"), solid: true);
            }
        }

        // ---------- stands ----------

        /// <summary>One long-side stand, built in a frame where outwards is +Z, then rotated for the south side.</summary>
        private static void BuildStand(ArtContext ctx, int side, List<(Matrix4x4, bool)> seats)
        {
            var rot = Quaternion.Euler(0f, side > 0 ? 0f : 180f, 0f);
            var frame = Matrix4x4.TRS(Vector3.zero, rot, Vector3.one);
            var parts = new List<(Mesh, Matrix4x4, int)>();
            float L = StandHalfLength * 2f;
            float back = StandFront + Rows * RowDepth;
            float topRow = FrontWall + (Rows - 1) * RowRise;
            void P(Vector3 c, Vector3 size, int sub = 0) => parts.Add(ProcMesh.Part(frame.MultiplyPoint3x4(c), size, rot, sub));

            // Front wall with a white coping, then one stepped tier per row.
            P(new Vector3(0f, FrontWall / 2, StandFront - 0.15f), new Vector3(L, FrontWall, 0.3f));
            P(new Vector3(0f, FrontWall + 0.05f, StandFront - 0.15f), new Vector3(L, 0.1f, 0.34f), 2);
            for (int i = 0; i < Rows; i++)
            {
                float top = FrontWall + i * RowRise;
                P(new Vector3(0f, top / 2, StandFront + (i + 0.5f) * RowDepth), new Vector3(L, top, RowDepth));
                // Gable walls (stepped, a little above each row).
                foreach (int e in new[] { -1, 1 })
                    P(new Vector3(e * (StandHalfLength + 0.25f), (top + 1.1f) / 2, StandFront + (i + 0.5f) * RowDepth), new Vector3(0.5f, top + 1.1f, RowDepth));
            }
            // Back wall up to the roof and a concourse slab behind it.
            const float roofBack = 19f, roofFront = 17.2f, roofOverhang = 3f;
            P(new Vector3(0f, roofBack / 2, back + 0.25f), new Vector3(L + 1f, roofBack, 0.5f));
            Add(ctx, "Stands", side > 0 ? "Stand North" : "Stand South", ProcMesh.Combine("Stand", parts), ArtPass.Mats("M_Concrete_Wall", "M_Concrete_Wall", "M_Paint_White"), solid: true);

            // Roof: sloping corrugated deck, rafters, front fascia and columns at the back.
            var roof = new List<(Mesh, Matrix4x4, int)>();
            void R(Vector3 c, Vector3 size, Quaternion r, int sub = 0) => roof.Add(ProcMesh.Part(frame.MultiplyPoint3x4(c), size, rot * r, sub));
            float z0 = StandFront - roofOverhang, z1 = back + 0.5f;
            float slope = Mathf.Atan2(roofBack - roofFront, z1 - z0) * Mathf.Rad2Deg;
            float deckLen = Vector3.Distance(new Vector3(0f, roofFront, z0), new Vector3(0f, roofBack, z1));
            var deckRot = Quaternion.Euler(-slope, 0f, 0f);
            Vector3 deckMid = new Vector3(0f, (roofFront + roofBack) / 2, (z0 + z1) / 2);
            R(deckMid, new Vector3(L + 2f, 0.25f, deckLen), deckRot);
            for (int k = 0; k <= 8; k++)
            {
                float x = -StandHalfLength + k * (2f * StandHalfLength / 8f);
                R(deckMid + new Vector3(x, -0.55f, 0f), new Vector3(0.35f, 0.9f, deckLen), deckRot, 1);
                R(new Vector3(x, roofBack / 2, back + 0.9f), new Vector3(0.6f, roofBack, 0.6f), Quaternion.identity, 1);
            }
            R(new Vector3(0f, roofFront - 0.5f, z0 - 0.1f), new Vector3(L + 2f, 1.3f, 0.25f), Quaternion.identity, 2);
            Add(ctx, "Stands", side > 0 ? "Roof North" : "Roof South", ProcMesh.Combine("Roof", roof), ArtPass.Mats("M_Steel_CorrugatedDark", "M_Steel_Truss", "M_Paint_White"), solid: true);

            // Seats: 0.5 m apart, an aisle every 26th place; blue front and back rows and blue bands at the aisles.
            for (int i = 0; i < Rows; i++)
            {
                float top = FrontWall + i * RowRise;
                int idx = 0;
                for (float x = -StandHalfLength + 0.4f; x <= StandHalfLength - 0.4f; x += 0.5f, idx++)
                {
                    int place = idx % 26;
                    if (place == 25) continue;
                    bool blue = i < 2 || i >= Rows - 2 || place < 2 || place > 22;
                    var local = new Vector3(x, top, StandFront + i * RowDepth + 0.45f);
                    seats.Add((frame * Matrix4x4.Translate(local), blue));
                }
            }
            ctx.Log.Append($" stand {(side > 0 ? "N" : "S")} top row {topRow:F1} m;");
        }

        /// <summary>Folding stadium seat facing −Z: pedestal, pan and back (pivot on the step, 0.48 m wide).</summary>
        public static Mesh SeatMesh()
        {
            var parts = new List<(Mesh, Matrix4x4, int)>
            {
                ProcMesh.Part(new Vector3(0f, 0.2f, 0.12f), new Vector3(0.06f, 0.4f, 0.06f), Quaternion.identity),
                ProcMesh.Part(new Vector3(0f, 0.42f, 0f), new Vector3(0.46f, 0.05f, 0.4f), Quaternion.Euler(-4f, 0f, 0f)),
                ProcMesh.Part(new Vector3(0f, 0.66f, 0.2f), new Vector3(0.46f, 0.44f, 0.05f), Quaternion.Euler(-10f, 0f, 0f)),
            };
            return ProcMesh.Combine("StadiumSeat", parts);
        }

        private static void BuildSeats(ArtContext ctx, List<(Matrix4x4 m, bool blue)> seats)
        {
            var mesh = ArtPass.MeshAsset("Stadium_Seat", SeatMesh);
            var red = MaterialLibrary.Get("M_Stadium_Seat_Red");
            var blue = MaterialLibrary.Get("M_Stadium_Seat_Blue");
            foreach (var m in new[] { red, blue })
            {
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            var groups = new[]
            {
                new InstancedSeats.Group { Material = red, Matrices = seats.FindAll(s => !s.blue).ConvertAll(s => s.m).ToArray() },
                new InstancedSeats.Group { Material = blue, Matrices = seats.FindAll(s => s.blue).ConvertAll(s => s.m).ToArray() },
            };
            var go = new GameObject("Seats (instanced)");
            go.transform.SetParent(G(ctx, "Stands"), false);
            var b = new Bounds(Vector3.zero, new Vector3(StandHalfLength * 2f + 2f, 30f, (StandFront + Rows * RowDepth) * 2f + 2f));
            go.AddComponent<InstancedSeats>().Configure(mesh, groups, b, 0);
            ctx.Log.Append($" seats {seats.Count};");
        }

        private static void BuildDugouts(ArtContext ctx)
        {
            foreach (int s in new[] { -1, 1 })
            {
                // On the south apron, backs to the stand, facing the pitch (+Z).
                var c = new Vector3(s * 12f, 0f, -49.2f);
                var parts = new List<(Mesh, Matrix4x4, int)>
                {
                    ProcMesh.Part(c + new Vector3(0f, 1.1f, -1f), new Vector3(6f, 2.2f, 0.1f), Quaternion.identity, 0),
                    ProcMesh.Part(c + new Vector3(-3f, 1.1f, 0f), new Vector3(0.1f, 2.2f, 2f), Quaternion.identity, 1),
                    ProcMesh.Part(c + new Vector3(3f, 1.1f, 0f), new Vector3(0.1f, 2.2f, 2f), Quaternion.identity, 1),
                    ProcMesh.Part(c + new Vector3(0f, 2.15f, 0.05f), new Vector3(6.1f, 0.06f, 2.2f), Quaternion.Euler(-8f, 0f, 0f), 1),
                    ProcMesh.Part(c + new Vector3(0f, 0.45f, -0.6f), new Vector3(5.6f, 0.08f, 0.45f), Quaternion.identity, 2),
                    ProcMesh.Part(c + new Vector3(0f, 0.22f, -0.6f), new Vector3(5.6f, 0.44f, 0.05f), Quaternion.identity, 3),
                };
                Add(ctx, "Dugouts", s > 0 ? "Dugout Home" : "Dugout Away", ProcMesh.Combine("Dugout", parts),
                    ArtPass.Mats("M_Steel_Brushed", "M_Prop_Polycarbonate", "M_Stadium_Seat_Blue", "M_Plastic_Grey"), solid: true);
            }
        }

        // ---------- LED boards ----------

        private static void BuildBoards(ArtContext ctx)
        {
            var frames = new List<(Mesh, Matrix4x4, int)>();
            var sv = new List<Vector3>();
            var sn = new List<Vector3>();
            var suv = new List<Vector2>();
            var st = new List<int>();
            var colliders = G(ctx, "LED Boards");
            int count = 0;

            void Board(Vector3 centre, Vector3 facing, float length)
            {
                var rot = Quaternion.LookRotation(-facing); // local +Z away from the pitch
                frames.Add(ProcMesh.Part(centre + Vector3.up * BoardHeight / 2, new Vector3(length, BoardHeight, 0.25f), rot));
                // Struts behind, leaning back.
                frames.Add(ProcMesh.Part(centre - facing * 0.45f + Vector3.up * 0.35f, new Vector3(length * 0.9f, 0.05f, 0.8f), rot * Quaternion.Euler(-40f, 0f, 0f)));
                // Screen quad on the pitch side; u runs left → right as seen from the pitch.
                var right = Vector3.Cross(Vector3.up, -facing).normalized;
                Vector3 f = centre + facing * 0.128f;
                int b = sv.Count;
                float half = length / 2 - 0.08f;
                sv.Add(f - right * half + Vector3.up * 0.08f);
                sv.Add(f + right * half + Vector3.up * 0.08f);
                sv.Add(f + right * half + Vector3.up * (BoardHeight - 0.08f));
                sv.Add(f - right * half + Vector3.up * (BoardHeight - 0.08f));
                for (int k = 0; k < 4; k++) sn.Add(facing);
                suv.AddRange(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                st.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                Collider(colliders, "Board collider", centre + Vector3.up * BoardHeight / 2, new Vector3(length, BoardHeight, 0.3f), rot);
                count++;
            }

            const float seg = 10f;
            foreach (int s in new[] { -1, 1 })
            {
                for (int i = 0; i < 10; i++)
                    Board(new Vector3(-50f + seg * (i + 0.5f), 0f, s * BoardZ), new Vector3(0f, 0f, -s), seg - 0.15f);
                for (int i = 0; i < 5; i++)
                    Board(new Vector3(s * BoardEndX, 0f, -25f + seg * (i + 0.5f)), new Vector3(-s, 0f, 0f), seg - 0.15f);
            }
            Add(ctx, "LED Boards", "Board Frames", ProcMesh.Combine("BoardFrames", frames), ArtPass.Mats("M_Plastic_Black"));

            var screen = new Mesh { name = "BoardScreens" };
            screen.SetVertices(sv);
            screen.SetNormals(sn);
            screen.SetUVs(0, suv);
            screen.SetTriangles(st, 0);
            screen.RecalculateBounds();
            screen.RecalculateTangents();
            var screens = Add(ctx, "LED Boards", "Board Screens", ProcMesh.FixWindingMetres(screen), ArtPass.Mats("M_Stadium_LedBanner"), castShadows: false);
            var scroller = screens.AddComponent<LedBoardScroller>();
            scroller.Configure(new[] { screens.GetComponent<Renderer>() }, 1f);
            ctx.Log.Append($" boards {count};");
        }

        // ---------- floodlights ----------

        private static (Light[] spots, Renderer[] heads) BuildTowers(ArtContext ctx)
        {
            var spots = new List<Light>();
            var heads = new List<Renderer>();
            var group = G(ctx, "Floodlights");
            int n = 0;
            foreach (var p in TowerPositions)
            {
                var basePos = new Vector3(p.x, 0f, p.y);
                var headPos = basePos + Vector3.up * TowerHeight;
                // Aim at a point between the pitch centre and the near touchline, so each tower lights its half.
                var target = new Vector3(p.x * 0.15f, 0f, p.y * 0.1f);
                var aim = (target - headPos).normalized;
                var flatAim = new Vector3(aim.x, 0f, aim.z).normalized;
                var headRot = Quaternion.LookRotation(aim, Vector3.up);

                var mast = new List<(Mesh, Matrix4x4, int)>
                {
                    ProcMesh.Part(basePos + Vector3.up * 0.5f, new Vector3(3f, 1f, 3f), Quaternion.LookRotation(flatAim), 1),
                };
                // Tapered lattice mast: four legs leaning in, with cross bracing every 4 m.
                for (int leg = 0; leg < 4; leg++)
                {
                    float ang = leg * Mathf.PI / 2 + Mathf.PI / 4;
                    Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 a = basePos + off * 1.1f + Vector3.up, b = headPos + off * 0.45f - Vector3.up * 3f;
                    mast.Add(ProcMesh.Part((a + b) / 2, new Vector3(0.18f, 0.18f, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a)));
                }
                for (float y = 3f; y < TowerHeight - 3f; y += 4f)
                {
                    float r = Mathf.Lerp(1.1f, 0.45f, (y - 1f) / (TowerHeight - 4f)) * 1.414f;
                    for (int k = 0; k < 4; k++)
                    {
                        float a0 = k * Mathf.PI / 2 + Mathf.PI / 4, a1 = a0 + Mathf.PI / 2;
                        Vector3 q0 = basePos + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r / 1.414f + Vector3.up * y;
                        Vector3 q1 = basePos + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r / 1.414f + Vector3.up * (y + 2f);
                        mast.Add(ProcMesh.Part((q0 + q1) / 2, new Vector3(0.08f, 0.08f, Vector3.Distance(q0, q1)), Quaternion.LookRotation(q1 - q0)));
                    }
                }
                // Lamp head frame: a tilted grid panel with a service walkway.
                mast.Add(ProcMesh.Part(headPos, new Vector3(9f, 5.2f, 0.5f), headRot));
                mast.Add(ProcMesh.Part(headPos - Vector3.up * 3f, new Vector3(1.2f, 3f, 1.2f), Quaternion.LookRotation(flatAim)));
                mast.Add(ProcMesh.Part(headPos - Vector3.up * 2.9f + flatAim * 0.8f, new Vector3(9.4f, 0.1f, 1.4f), Quaternion.LookRotation(flatAim), 2));
                Add(ctx, "Floodlights", $"Tower {n}", ProcMesh.Combine("Tower", mast), ArtPass.Mats("M_Steel_Truss", "M_Concrete_Blocks", "M_Steel_Plate"));
                Collider(group, "Tower collider", basePos + Vector3.up * TowerHeight / 2, new Vector3(2.6f, TowerHeight, 2.6f), Quaternion.LookRotation(flatAim));
                Collider(group, "Head collider", headPos, new Vector3(9.4f, 5.4f, 1.2f), headRot);

                // 6 × 4 lamps on the pitch side of the frame.
                var lamps = new List<(Mesh, Matrix4x4, int)>();
                var right = headRot * Vector3.right;
                var up = headRot * Vector3.up;
                for (int row = 0; row < 4; row++)
                for (int col = 0; col < 6; col++)
                {
                    Vector3 c = headPos + right * (-3.6f + col * 1.44f) + up * (-1.8f + row * 1.2f) + aim * 0.35f;
                    lamps.Add(ProcMesh.Part(c, new Vector3(1.1f, 0.85f, 0.22f), headRot));
                }
                var head = Add(ctx, "Floodlights", $"Lamps {n}", ProcMesh.Combine("Lamps", lamps), ArtPass.Mats("M_Stadium_Floodlamp"), castShadows: false);
                heads.Add(head.GetComponent<Renderer>());

                var lightGo = new GameObject($"Floodlight {n}");
                lightGo.transform.SetParent(group, false);
                lightGo.transform.SetPositionAndRotation(headPos + aim * 1.5f, headRot);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 180f;
                l.spotAngle = 96f;
                l.innerSpotAngle = 55f;
                l.color = new Color(1f, 0.96f, 0.9f);
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.8f;
                l.shadowBias = 0.05f;
                l.shadowNormalBias = 0.6f;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                spots.Add(l);
                n++;
            }
            ctx.Log.Append($" towers {n};");
            return (spots.ToArray(), heads.ToArray());
        }

        /// <summary>Stand lights relative to the tower floodlights (they hang 15 m from the seats, not 100 m).</summary>
        public const float StandLightScale = 0.035f;

        /// <summary>Two wide spots under each roof's front edge washing the seats (no shadows).</summary>
        private static List<Light> BuildStandLights(ArtContext ctx)
        {
            var list = new List<Light>();
            foreach (int side in new[] { 1, -1 })
            foreach (float x in new[] { -28f, 28f })
            {
                var go = new GameObject("Stand light");
                go.transform.SetParent(G(ctx, "Floodlights"), false);
                var pos = new Vector3(x, 15.5f, side * (StandFront - 1.5f));
                var target = new Vector3(x, 4f, side * (StandFront + Rows * RowDepth * 0.55f));
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 45f;
                l.spotAngle = 120f;
                l.innerSpotAngle = 70f;
                l.color = new Color(1f, 0.94f, 0.85f);
                l.shadows = LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                list.Add(l);
            }
            return list;
        }

        // ---------- time of day ----------

        private sealed class LookDef
        {
            public string Name, Hdri;
            public float Exposure, Ambient, Reflection, SunIntensity, Shadow, FogDensity, FogBrightness, Flood, Lamp;
            public bool Sun = true;
            public Quaternion? SunRotation;
            public Color SunTint = Color.white;
            public Color? Fog;
            public SceneLook Post;
        }

        private static readonly LookDef[] Looks =
        {
            new LookDef
            {
                Name = "Night", Hdri = "qwantani_night_puresky", Exposure = 0.16f, Ambient = 0.35f, Reflection = 0.45f,
                // Moonlight: faint, cool, high in the sky.
                SunIntensity = 0.06f, SunRotation = Quaternion.Euler(52f, 150f, 0f), SunTint = new Color(0.6f, 0.7f, 1f), Shadow = 0.4f,
                Fog = new Color(0.035f, 0.045f, 0.07f), FogDensity = 0.0016f, Flood = 11000f, Lamp = 14f,
                Post = new SceneLook { PostExposure = 0.25f, Contrast = 12f, Saturation = 4f, Temperature = -4f, BloomIntensity = 0.75f, BloomThreshold = 1f, Vignette = 0.24f },
            },
            new LookDef
            {
                Name = "Dusk", Hdri = "qwantani_dusk_2_puresky", Exposure = 0.75f, Ambient = 0.8f, Reflection = 0.8f,
                SunIntensity = 1.6f, SunTint = new Color(1f, 0.8f, 0.62f), Shadow = 0.85f, FogDensity = 0.0012f, FogBrightness = 0.8f,
                Flood = 4500f, Lamp = 9f,
                Post = new SceneLook { PostExposure = 0.1f, Contrast = 10f, Saturation = 6f, Temperature = 6f, BloomIntensity = 0.5f, Vignette = 0.2f },
            },
            new LookDef
            {
                Name = "Day", Hdri = "hilly_terrain_01_puresky", Exposure = 0.85f, Ambient = 1.05f, Reflection = 1f,
                SunIntensity = 3.1f, SunTint = new Color(1f, 0.95f, 0.88f), Shadow = 0.9f, FogDensity = 0.0008f, FogBrightness = 0.85f,
                Flood = 0f, Lamp = 0.5f,
                Post = new SceneLook { PostExposure = 0.1f, Contrast = 10f, Saturation = 6f, BloomIntensity = 0.3f, Vignette = 0.16f },
            },
        };

        /// <summary>Stadium-only copy of a shared sky material (the shared one's exposure belongs to its own scene).</summary>
        private static Material Sky(string name, string hdri, float exposure)
        {
            string path = $"{EnvironmentAssets.SkyDir}/Sky_Stadium_{name}.mat";
            var shared = EnvironmentAssets.Sky(hdri);
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
            return m;
        }

        private static void BuildLighting(ArtContext ctx, Light[] spots, Renderer[] heads, float[] scales)
        {
            var presets = new StadiumLighting.Preset[Looks.Length];
            for (int i = 0; i < Looks.Length; i++)
            {
                var d = Looks[i];
                var src = ArtCatalog.Find(d.Hdri);
                var hdr = HdriSun.Analyze($"{EnvironmentAssets.SkyDir}/{src.Id}_{src.Resolution}.hdr");
                Color fog;
                if (d.Fog.HasValue) fog = d.Fog.Value;
                else
                {
                    Color c = hdr.HorizonColor * d.Exposure * d.FogBrightness;
                    float m = c.maxColorComponent;
                    if (m > 1f) c /= m;
                    c.a = 1f;
                    fog = c.gamma;
                }
                var vol = ArtPass.SetupVolume(ctx, d.Post, "_" + d.Name);
                presets[i] = new StadiumLighting.Preset
                {
                    Name = d.Name,
                    Sky = Sky(d.Name, d.Hdri, d.Exposure),
                    AmbientIntensity = d.Ambient,
                    ReflectionIntensity = d.Reflection,
                    SunEnabled = d.Sun,
                    SunRotation = d.SunRotation ?? Quaternion.LookRotation(-hdr.SunDirection, Vector3.up),
                    SunColor = Color.Lerp(Color.white, d.SunRotation.HasValue ? Color.white : hdr.SunColor, 0.6f) * d.SunTint,
                    SunIntensity = d.SunIntensity,
                    ShadowStrength = d.Shadow,
                    FogColor = fog,
                    FogDensity = d.FogDensity,
                    FloodlightIntensity = d.Flood,
                    LampIntensity = d.Lamp,
                    Volume = vol,
                };
                ctx.Log.Append($" {d.Name} sun elev {hdr.SunElevation:F1}°;");
            }
            AssetDatabase.SaveAssets();

            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 256;
            if (ctx.Sun != null)
            {
                ctx.Sun.shadows = LightShadows.Soft;
                ctx.Sun.shadowBias = 0.05f;
                ctx.Sun.shadowNormalBias = 0.4f;
                ctx.Sun.lightmapBakeType = LightmapBakeType.Realtime;
            }
            var go = new GameObject("Stadium Lighting");
            go.transform.SetParent(ctx.Dressing, false);
            var sl = go.AddComponent<StadiumLighting>();
            sl.Configure(presets, ctx.Sun, spots, heads, scales);
            // The saved scene starts at night (the default setting); the track scene re-applies the player's choice.
            sl.Apply(TimeOfDay.Night);
            ctx.Log.Append(" lighting presets 3;");
        }
    }
}
