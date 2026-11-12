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
    /// The launch camp round the pads: a ridge tent with poles and guy ropes, a braced timber watchtower with a ladder
    /// and a tin roof, sandbag walls, crates, drums, a generator, a radio mast, a windsock and two parked vehicles.
    /// One mesh per material; simple invisible colliders.
    /// </summary>
    public static class RangeSteppeCamp
    {
        private const string Dir = "Assets/_Project/Art/Strike";
        private const VehicleKit.Slot Wood = VehicleKit.Slot.Paint, Bags = VehicleKit.Slot.Rubber, Tin = VehicleKit.Slot.Metal,
            Black = VehicleKit.Slot.Dark, Green = VehicleKit.Slot.Glass, Canvas = VehicleKit.Slot.Canvas, Sock = VehicleKit.Slot.Light;

        private static float Y(float x, float z) => RangeSteppeBuilder.Height(x, z);

        public static void Build(Transform root, int layer)
        {
            var kit = new VehicleKit { Tile = 2f };
            var colliders = new GameObject("Camp Colliders").transform;
            colliders.SetParent(root, false);
            float pz = RangeSteppeBuilder.PadZ;
            var rng = new System.Random(2718);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            void Solid(Vector3 centre, Vector3 size, float yaw = 0f)
            {
                var go = new GameObject("Collider") { layer = layer, isStatic = true };
                go.transform.SetParent(colliders, false);
                go.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
                go.AddComponent<BoxCollider>().size = size;
            }

            Tent(kit, new Vector3(-36f, Y(-36f, pz - 24f), pz - 24f), 20f, 9f, 5.5f);
            Solid(new Vector3(-36f, Y(-36f, pz - 24f) + 1.5f, pz - 24f), new Vector3(9f, 3f, 5.5f), 20f);
            Tent(kit, new Vector3(-52f, Y(-52f, pz - 12f), pz - 12f), -35f, 5f, 3.6f);
            Solid(new Vector3(-52f, Y(-52f, pz - 12f) + 1.2f, pz - 12f), new Vector3(5f, 2.4f, 3.6f), -35f);

            var tower = new Vector3(46f, Y(46f, pz - 6f), pz - 6f);
            Tower(kit, tower);
            Solid(tower + Vector3.up * 5f, new Vector3(3.6f, 10f, 3.6f));

            // Sandbag walls: beside each pad (blast walls) and round the camp.
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 24f;
                Sandbags(kit, rng, new Vector3(x - 5.2f, Y(x - 5.2f, pz - 1f), pz - 1f), 90f, 5.4f, 4);
                Solid(new Vector3(x - 5.2f, Y(x - 5.2f, pz - 1f) + 0.5f, pz - 1f), new Vector3(0.7f, 1f, 5.6f));
            }
            Sandbags(kit, rng, new Vector3(-26f, Y(-26f, pz - 15f), pz - 15f), 20f, 8f, 5);
            Solid(new Vector3(-26f, Y(-26f, pz - 15f) + 0.6f, pz - 15f), new Vector3(8.2f, 1.2f, 0.7f), 20f);
            Sandbags(kit, rng, new Vector3(41f, Y(41f, pz - 1f), pz - 1f), 0f, 6f, 4);
            Solid(new Vector3(41f, Y(41f, pz - 1f) + 0.5f, pz - 1f), new Vector3(6.2f, 1f, 0.7f));

            // Supplies: crates on pallets, drums, jerrycans, a generator with its cable.
            Supplies(kit, rng, new Vector3(-24f, Y(-24f, pz - 22f), pz - 22f), 20f);
            Solid(new Vector3(-24f, Y(-24f, pz - 22f) + 0.8f, pz - 22f), new Vector3(4.5f, 1.6f, 3f), 20f);
            Supplies(kit, rng, new Vector3(30f, Y(30f, pz - 14f), pz - 14f), -70f);
            Solid(new Vector3(30f, Y(30f, pz - 14f) + 0.8f, pz - 14f), new Vector3(4.5f, 1.6f, 3f), -70f);
            Generator(kit, new Vector3(-44f, Y(-44f, pz - 31f), pz - 31f), 110f);

            Mast(kit, new Vector3(-58f, Y(-58f, pz - 30f), pz - 30f));
            Windsock(kit, new Vector3(13f, Y(13f, pz + 9f), pz + 9f), 35f);

            // A camp table with benches under a camouflage awning.
            Awning(kit, new Vector3(16f, Y(16f, pz - 18f), pz - 18f), -15f);

            // Fence posts with wire along the back of the camp.
            for (float x = -70f; x <= 66f; x += 4f)
            {
                float z = pz - 44f + Mathf.Sin(x * 0.05f) * 2f;
                var p = new Vector3(x, Y(x, z), z);
                kit.Cylinder(Wood, p + Vector3.down * 0.3f, p + Vector3.up * 1.5f + new Vector3(R(-0.05f, 0.05f), 0f, R(-0.05f, 0.05f)), 0.05f, 6);
                if (x + 4f <= 66f)
                {
                    float z2 = pz - 44f + Mathf.Sin((x + 4f) * 0.05f) * 2f;
                    var q = new Vector3(x + 4f, Y(x + 4f, z2), z2);
                    foreach (float h in new[] { 0.5f, 0.95f, 1.4f }) kit.Cylinder(Black, p + Vector3.up * h, q + Vector3.up * h, 0.008f, 4, false);
                }
            }

            var meshes = kit.Build("Steppe Camp");
            var mats = Materials();
            var group = new GameObject("Camp").transform;
            group.SetParent(root, false);
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null) continue;
                var go = new GameObject("Camp " + (VehicleKit.Slot)i) { layer = layer, isStatic = true };
                go.transform.SetParent(group, false);
                go.AddComponent<MeshFilter>().sharedMesh = Save(meshes[i], "Steppe_Camp_" + i);
                go.AddComponent<MeshRenderer>().sharedMaterial = mats[i];
            }

            Parked(root, layer, VehicleKind.Truck, new Vector3(-14f, 0f, pz - 34f), 75f);
            Parked(root, layer, VehicleKind.Wheeled, new Vector3(4f, 0f, pz - 36f), 100f);
        }

        // ---------- pieces ----------

        // A ridge tent: side walls, gable ends stepped up to the ridge, two roof slopes with an overhang, poles, a
        // dark doorway and guy ropes to pegs.
        private static void Tent(VehicleKit k, Vector3 at, float yaw, float length, float width)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);
            float eave = 1.7f, ridge = eave + width * 0.33f, hw = width * 0.5f, hl = length * 0.5f;
            foreach (float s in new[] { -1f, 1f })
            {
                k.Box(Canvas, P(0f, eave * 0.5f, s * hw), new Vector3(length, eave, 0.05f), 0.01f, rot);
                // Roof slope.
                float run = Mathf.Sqrt(hw * hw + (ridge - eave) * (ridge - eave)) + 0.35f;
                float pitch = Mathf.Atan2(ridge - eave, hw) * Mathf.Rad2Deg;
                k.Box(Canvas, P(0f, (ridge + eave) * 0.5f - 0.04f, s * (hw * 0.5f + 0.1f)), new Vector3(length + 0.5f, 0.05f, run), 0.01f, rot * Quaternion.Euler(s * pitch, 0f, 0f));
                // Gable end: the wall, then courses narrowing to the ridge.
                k.Box(Canvas, P(s * hl, eave * 0.5f, 0f), new Vector3(0.05f, eave, width), 0.01f, rot);
                const int courses = 6;
                for (int c = 0; c < courses; c++)
                {
                    float f = (c + 0.5f) / courses;
                    k.Box(Canvas, P(s * hl, eave + (ridge - eave) * f, 0f), new Vector3(0.05f, (ridge - eave) / courses + 0.01f, width * (1f - f)), 0f, rot);
                }
                k.Cylinder(Wood, P(s * (hl + 0.08f), 0f, 0f), P(s * (hl + 0.08f), ridge + 0.25f, 0f), 0.05f, 6);
                // Guy ropes from the eaves.
                for (int g = -1; g <= 1; g++)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        if (s < 0f) continue;
                        Vector3 a = P(g * hl * 0.9f, eave, side * hw), b = P(g * hl * 0.9f, 0.05f, side * (hw + 1.6f));
                        b.y = Y(b.x, b.z) + 0.05f;
                        k.Cylinder(Black, a, b, 0.012f, 4, false);
                        k.Cylinder(Wood, b + Vector3.down * 0.1f, b + Vector3.up * 0.25f, 0.025f, 5);
                    }
            }
            // Doorway on one gable and a rolled flap above it.
            k.Box(Black, P(hl + 0.03f, 0.95f, 0f), new Vector3(0.04f, 1.9f, 1.3f), 0f, rot);
            k.Cylinder(Canvas, P(hl + 0.1f, 1.95f, -0.7f), P(hl + 0.1f, 1.95f, 0.7f), 0.09f, 8);
        }

        // A timber watchtower: splayed legs, cross bracing on every side, a plank deck with boarded sides, a tin
        // roof on posts and a ladder.
        private static void Tower(VehicleKit k, Vector3 at)
        {
            const float deck = 7.5f, baseHalf = 2.1f, topHalf = 1.5f;
            Vector3 Leg(float sx, float sz, float h) => at + new Vector3(sx * Mathf.Lerp(baseHalf, topHalf, h / deck), h, sz * Mathf.Lerp(baseHalf, topHalf, h / deck));
            float[] s = { -1f, 1f };
            foreach (float sx in s)
                foreach (float sz in s)
                    k.Cylinder(Wood, Leg(sx, sz, -0.4f), Leg(sx, sz, deck), 0.11f, 8);
            // Bracing: a horizontal and an X on each side, on three levels.
            for (int level = 0; level < 3; level++)
            {
                float h0 = 0.4f + level * 2.4f, h1 = h0 + 2.2f;
                foreach (float a in s)
                {
                    k.Cylinder(Wood, Leg(a, -1f, h1), Leg(a, 1f, h1), 0.05f, 6);
                    k.Cylinder(Wood, Leg(-1f, a, h1), Leg(1f, a, h1), 0.05f, 6);
                    k.Cylinder(Wood, Leg(a, -1f, h0), Leg(a, 1f, h1), 0.04f, 6);
                    k.Cylinder(Wood, Leg(a, 1f, h0), Leg(a, -1f, h1), 0.04f, 6);
                    k.Cylinder(Wood, Leg(-1f, a, h0), Leg(1f, a, h1), 0.04f, 6);
                    k.Cylinder(Wood, Leg(1f, a, h0), Leg(-1f, a, h1), 0.04f, 6);
                }
            }
            // Deck planks.
            for (int i = 0; i < 9; i++)
                k.Box(Wood, at + new Vector3(-1.6f + i * 0.4f, deck + 0.05f, 0f), new Vector3(0.37f, 0.06f, 3.6f), 0.01f);
            // Boarded sides (three boards, gaps between) and corner posts up to the roof.
            foreach (float a in s)
            {
                for (int b = 0; b < 3; b++)
                {
                    float y = deck + 0.3f + b * 0.34f;
                    k.Box(Wood, at + new Vector3(a * 1.72f, y, 0f), new Vector3(0.04f, 0.26f, 3.5f), 0.008f);
                    if (a < 0f || b < 2) k.Box(Wood, at + new Vector3(0f, y, a * 1.72f), new Vector3(3.5f, 0.26f, 0.04f), 0.008f);
                }
                foreach (float c in s)
                    k.Cylinder(Wood, at + new Vector3(a * 1.7f, deck, c * 1.7f), at + new Vector3(a * 1.7f, deck + 2.45f, c * 1.7f), 0.07f, 6);
                // Tin roof, two slopes.
                k.Box(Tin, at + new Vector3(a * 1.05f, deck + 2.75f, 0f), new Vector3(2.35f, 0.04f, 4.4f), 0.005f, Quaternion.Euler(0f, 0f, -a * 16f));
            }
            k.Box(Wood, at + new Vector3(0f, deck + 3.06f, 0f), new Vector3(0.12f, 0.08f, 4.4f), 0.01f);
            // Ladder up the back.
            Vector3 foot = at + new Vector3(0f, 0f, -2.9f), top = at + new Vector3(0f, deck + 0.9f, -1.85f);
            foreach (float a in s) k.Cylinder(Wood, foot + Vector3.right * a * 0.28f, top + Vector3.right * a * 0.28f, 0.035f, 6);
            for (int r = 1; r < 22; r++)
            {
                Vector3 p = Vector3.Lerp(foot, top, r / 22f);
                k.Cylinder(Wood, p + Vector3.left * 0.3f, p + Vector3.right * 0.3f, 0.022f, 5);
            }
            // A searchlight on the rail.
            k.Cylinder(Black, at + new Vector3(1.5f, deck + 1.35f, 1.5f), at + new Vector3(1.75f, deck + 1.4f, 1.85f), 0.16f, 10);
        }

        // A wall of sandbags: staggered courses of fat rounded bags, each a little out of line.
        private static void Sandbags(VehicleKit k, System.Random rng, Vector3 at, float yaw, float length, int courses)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            const float bagL = 0.62f, bagH = 0.2f, bagW = 0.34f;
            for (int c = 0; c < courses; c++)
            {
                float start = -length * 0.5f + (c % 2) * bagL * 0.5f + c * 0.08f;
                for (float x = start; x < length * 0.5f - c * 0.08f; x += bagL)
                {
                    Vector3 p = at + rot * new Vector3(x + R(-0.03f, 0.03f), bagH * (c + 0.5f) * 0.95f, R(-0.04f, 0.04f));
                    k.Box(Bags, p, new Vector3(bagL * R(0.92f, 1.02f), bagH, bagW * R(0.9f, 1.05f)), 0.07f, rot * Quaternion.Euler(R(-3f, 3f), R(-6f, 6f), R(-3f, 3f)));
                }
            }
        }

        private static void Supplies(VehicleKit k, System.Random rng, Vector3 at, float yaw)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);
            // Two pallets with crates, one stacked two high.
            for (int p = 0; p < 2; p++)
            {
                float px = -1.2f + p * 2.3f;
                for (int b = 0; b < 3; b++) k.Box(Wood, P(px, 0.05f, -0.45f + b * 0.45f), new Vector3(1.2f, 0.1f, 0.1f), 0.01f, rot);
                for (int b = 0; b < 5; b++) k.Box(Wood, P(px - 0.5f + b * 0.25f, 0.125f, 0f), new Vector3(0.18f, 0.03f, 1.1f), 0.005f, rot);
                int high = p == 0 ? 2 : 1;
                for (int h = 0; h < high; h++)
                {
                    var r2 = rot * Quaternion.Euler(0f, R(-8f, 8f), 0f);
                    Vector3 c = P(px + R(-0.05f, 0.05f), 0.14f + 0.36f + h * 0.72f, R(-0.05f, 0.05f));
                    k.Box(Green, c, new Vector3(1.05f, 0.7f, 0.8f), 0.02f, r2);
                    // Battens and rope handles.
                    foreach (float sx in new[] { -0.36f, 0.36f })
                        k.Box(Wood, c + r2 * new Vector3(sx, 0f, 0f), new Vector3(0.08f, 0.74f, 0.84f), 0.008f, r2);
                    k.Box(Black, c + r2 * new Vector3(0f, 0.1f, 0.41f), new Vector3(0.22f, 0.04f, 0.03f), 0.01f, r2);
                }
            }
            // Drums and jerrycans.
            for (int d = 0; d < 4; d++)
            {
                Vector3 b = P(1.2f + (d % 2) * 0.68f + R(-0.04f, 0.04f), 0f, 1.25f + (d / 2) * 0.68f);
                k.Cylinder(d == 1 ? Tin : Green, b, b + Vector3.up * 0.9f, 0.3f, 14);
                foreach (float h in new[] { 0.3f, 0.6f }) k.Cylinder(Black, b + Vector3.up * (h - 0.012f), b + Vector3.up * (h + 0.012f), 0.31f, 14);
            }
            for (int j = 0; j < 3; j++)
            {
                Vector3 c = P(-1.9f + j * 0.26f, 0.24f, 1.2f);
                k.Box(Green, c, new Vector3(0.18f, 0.48f, 0.36f), 0.04f, rot);
                k.Box(Black, c + Vector3.up * 0.27f, new Vector3(0.05f, 0.05f, 0.2f), 0.015f, rot);
            }
        }

        private static void Generator(VehicleKit k, Vector3 at, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);
            k.Box(Green, P(0f, 0.62f, 0f), new Vector3(1.6f, 0.9f, 0.85f), 0.05f, rot);
            foreach (float sx in new[] { -0.6f, 0.6f }) k.Box(Black, P(sx, 0.09f, 0f), new Vector3(0.12f, 0.18f, 0.95f), 0.02f, rot);
            for (int l = 0; l < 6; l++) k.Box(Black, P(-0.45f + l * 0.09f, 0.62f, 0.43f), new Vector3(0.04f, 0.5f, 0.02f), 0.004f, rot);
            k.Box(Black, P(0.45f, 0.72f, 0.435f), new Vector3(0.4f, 0.3f, 0.02f), 0.005f, rot);
            k.Cylinder(Tin, P(-0.5f, 1.07f, 0f), P(-0.5f, 1.45f, 0f), 0.04f, 8);
            k.Cylinder(Tin, P(0.3f, 1.07f, 0f), P(0.3f, 1.12f, 0f), 0.1f, 10);
            // The cable snaking off towards the tents.
            Vector3 prev = P(0.8f, 0.03f, 0f);
            for (int i = 1; i <= 12; i++)
            {
                Vector3 next = P(0.8f + i * 0.9f, 0f, Mathf.Sin(i * 0.9f) * 0.5f);
                next.y = Y(next.x, next.z) + 0.03f;
                k.Cylinder(Black, prev, next, 0.02f, 5, false);
                prev = next;
            }
        }

        // A guyed lattice radio mast with a whip and two dipoles.
        private static void Mast(VehicleKit k, Vector3 at)
        {
            const float h = 14f, w = 0.22f;
            Vector3[] corner = { new Vector3(w, 0f, 0f), new Vector3(-w * 0.5f, 0f, w * 0.87f), new Vector3(-w * 0.5f, 0f, -w * 0.87f) };
            foreach (var c in corner) k.Cylinder(Tin, at + c, at + c + Vector3.up * h, 0.022f, 5);
            for (float y = 0.4f; y < h; y += 0.8f)
                for (int i = 0; i < 3; i++)
                {
                    k.Cylinder(Tin, at + corner[i] + Vector3.up * y, at + corner[(i + 1) % 3] + Vector3.up * y, 0.01f, 4, false);
                    k.Cylinder(Tin, at + corner[i] + Vector3.up * y, at + corner[(i + 1) % 3] + Vector3.up * (y + 0.8f), 0.008f, 4, false);
                }
            k.Cylinder(Black, at + Vector3.up * h, at + Vector3.up * (h + 2.6f), 0.012f, 5);
            foreach (float y in new[] { h - 1.2f, h - 3.4f })
                k.Cylinder(Black, at + new Vector3(-1.1f, y, 0f), at + new Vector3(1.1f, y, 0f), 0.015f, 5);
            for (int g = 0; g < 3; g++)
            {
                float a = g * Mathf.PI * 2f / 3f + 0.4f;
                foreach (float top in new[] { h * 0.55f, h * 0.95f })
                {
                    Vector3 anchor = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 8.5f;
                    anchor.y = Y(anchor.x, anchor.z) + 0.05f;
                    k.Cylinder(Black, at + Vector3.up * top, anchor, 0.008f, 4, false);
                }
            }
            k.Box(Green, at + new Vector3(0.9f, 0.45f, 0.4f), new Vector3(0.7f, 0.9f, 0.5f), 0.03f);
        }

        // A windsock: pole, hoop and a striped tapering sock streaming downwind.
        private static void Windsock(VehicleKit k, Vector3 at, float yaw)
        {
            k.Cylinder(Tin, at + Vector3.down * 0.3f, at + Vector3.up * 6f, 0.05f, 8);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 mouth = at + Vector3.up * 5.8f + rot * new Vector3(0f, 0f, 0.25f);
            Vector3 prev = mouth;
            for (int i = 0; i < 5; i++)
            {
                float f0 = i / 5f, f1 = (i + 1) / 5f;
                Vector3 next = mouth + rot * new Vector3(0f, -0.9f * f1 * f1, 2.6f * f1);
                // Drawn as tapering segments, alternating orange and white.
                int rings = 3;
                for (int r = 0; r < rings; r++)
                {
                    Vector3 a = Vector3.Lerp(prev, next, r / (float)rings), b = Vector3.Lerp(prev, next, (r + 1) / (float)rings);
                    float rad = Mathf.Lerp(0.36f, 0.14f, f0 + (f1 - f0) * (r + 0.5f) / rings);
                    k.Cylinder(i % 2 == 0 ? Sock : Canvas, a, b, rad, 10, false);
                }
                prev = next;
            }
        }

        // A table and benches under a net awning on four poles.
        private static void Awning(VehicleKit k, Vector3 at, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);
            foreach (float sx in new[] { -2.4f, 2.4f })
                foreach (float sz in new[] { -1.8f, 1.8f })
                    k.Cylinder(Wood, P(sx, -0.2f, sz), P(sx, sz > 0f ? 2.7f : 2.3f, sz), 0.05f, 6);
            k.Box(Canvas, P(0f, 2.55f, 0f), new Vector3(5.4f, 0.03f, 4.2f), 0.005f, rot * Quaternion.Euler(-6.3f, 0f, 0f));
            k.Box(Wood, P(0f, 0.76f, 0f), new Vector3(2.4f, 0.05f, 0.9f), 0.01f, rot);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -0.35f, 0.35f })
                    k.Cylinder(Wood, P(sx, 0f, sz), P(sx, 0.74f, sz), 0.035f, 6);
            foreach (float sz in new[] { -0.85f, 0.85f })
            {
                k.Box(Wood, P(0f, 0.44f, sz), new Vector3(2.3f, 0.04f, 0.3f), 0.01f, rot);
                foreach (float sx in new[] { -1f, 1f }) k.Cylinder(Wood, P(sx, 0f, sz), P(sx, 0.42f, sz), 0.03f, 6);
            }
            // A radio set and a map case on the table.
            k.Box(Green, P(-0.6f, 0.93f, 0.1f), new Vector3(0.45f, 0.3f, 0.3f), 0.02f, rot);
            k.Cylinder(Black, P(-0.75f, 1.08f, 0.1f), P(-0.75f, 1.9f, 0.1f), 0.006f, 4, false);
            k.Box(Black, P(0.4f, 0.8f, -0.05f), new Vector3(0.6f, 0.02f, 0.42f), 0.004f, rot * Quaternion.Euler(0f, 12f, 0f));
        }

        private static void Parked(Transform root, int layer, VehicleKind kind, Vector3 at, float yaw)
        {
            RangeVehicles.MaterialsOverride = RangeSteppeBuilder.VehicleMaterials();
            var go = RangeVehicles.Build(kind, root, layer, Color.white);
            go.name = "Parked " + kind;
            foreach (var hb in go.GetComponentsInChildren<StrikeHitbox>()) Object.DestroyImmediate(hb);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (!AssetDatabase.Contains(mf.sharedMesh)) mf.sharedMesh = Save(Object.Instantiate(mf.sharedMesh), $"Parked_{kind}_{mf.gameObject.name}");
            var paint = ParkedPaint();
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
            {
                mr.SetPropertyBlock(null);
                if (mr.gameObject.name == nameof(VehicleKit.Slot.Paint)) mr.sharedMaterial = paint;
            }
            at.y = Y(at.x, at.z);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        }

        // ---------- materials ----------

        private static Material ParkedPaint()
        {
            string path = Dir + "/M_Strike_ParkedPaint.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var src = RangeSteppeBuilder.VehicleMaterials()[(int)VehicleKit.Slot.Paint];
            if (m == null)
            {
                m = new Material(src) { name = "M_Strike_ParkedPaint" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(src);
            m.SetColor("_BaseColor", RangeVehicles.Olive);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Tinted(string name, string from, Color colour)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var src = MaterialLibrary.Get(from);
            if (m == null)
            {
                m = new Material(src) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(src);
            m.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material[] Materials()
        {
            var m = new Material[VehicleKit.Slots];
            m[(int)Wood] = Tinted("M_Strike_CampWood", "M_Wood_Planks", new Color(0.78f, 0.7f, 0.58f));
            m[(int)Bags] = Tinted("M_Strike_Sandbag", "M_Fabric_Sandbag", new Color(0.9f, 0.86f, 0.74f));
            m[(int)Tin] = Tinted("M_Strike_Tin", "M_Iron_CorrugatedRusty", new Color(0.8f, 0.78f, 0.74f));
            m[(int)Black] = MaterialLibrary.Get("M_Plastic_Black");
            m[(int)Green] = Tinted("M_Strike_CampGreen", "M_Metal_ShutterPainted", new Color(0.36f, 0.4f, 0.27f));
            m[(int)Canvas] = Tinted("M_Strike_Canvas", "M_Fabric_Canvas", new Color(0.56f, 0.56f, 0.4f));
            m[(int)Sock] = Tinted("M_Strike_Sock", "M_Fabric_Canvas", new Color(1f, 0.42f, 0.1f));
            return m;
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
