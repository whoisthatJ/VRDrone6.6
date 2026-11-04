using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.Garage
{
    /// <summary>Materials the procedural quad uses (assigned by the scene builder).</summary>
    [Serializable]
    public sealed class QuadModelMaterials
    {
        public Material Carbon;
        public Material Metal;
        public Material Bell;
        public Material Copper;
        public Material Prop;
        public Material Lipo;
        public Material Plastic;
        public Material Led;
        public Material Lens;
        public Material Gold;
        public Material Pad;
    }

    /// <summary>
    /// Builds the quad for a <see cref="QuadBuild"/> at runtime, in metres, so it always matches the parts. With the
    /// P2 part library (Blender-generated meshes) it assembles the detailed parts; otherwise it builds the frame
    /// plates and arms (true-X, stretch-X, deadcat) or ducts, motors sized from the stator code, props by diameter and
    /// blade count, the LiPo with its strap, FPV camera, antenna and extras, in the build's style and accent colour.
    /// Origin at the centre of the bottom plate; +Z forward. Props are separate children ("Prop 0..3" in mixer order
    /// RR, FR, RL, FL) so they can spin.
    /// </summary>
    public static class QuadModelBuilder
    {
        private static Mesh _cube, _cylinder;

        public sealed class Result
        {
            public GameObject Root;
            public Transform[] Props = new Transform[4];
            public Renderer[] Leds = new Renderer[0];
            /// <summary>Prop blur discs (detailed models), one per prop, for <c>PropVisuals</c>.</summary>
            public Renderer[] BlurDiscs = new Renderer[0];
            /// <summary>Child objects by part slot, for the exploded view.</summary>
            public readonly Dictionary<string, Transform> Parts = new Dictionary<string, Transform>();
        }

        private sealed class Batch
        {
            public readonly Dictionary<Material, List<CombineInstance>> Items = new Dictionary<Material, List<CombineInstance>>();

            public void Add(Material m, Mesh mesh, Matrix4x4 mx)
            {
                if (m == null) return;
                if (!Items.TryGetValue(m, out var list)) Items[m] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, transform = mx });
            }

            public void Box(Material m, Vector3 c, Vector3 size, Quaternion r) => Add(m, Cube, Matrix4x4.TRS(c, r, size));
            public void Box(Material m, Vector3 c, Vector3 size) => Box(m, c, size, Quaternion.identity);
            public void Cyl(Material m, Vector3 c, float radius, float height) => Add(m, Cylinder, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(radius * 2f, height, radius * 2f)));

            public void Rod(Material m, Vector3 a, Vector3 b, float thickness)
            {
                Vector3 d = b - a;
                if (d.sqrMagnitude < 1e-8f) return;
                Box(m, (a + b) * 0.5f, new Vector3(thickness, thickness, d.magnitude), Quaternion.LookRotation(d));
            }

            public GameObject Build(string name, Transform parent, int layer)
            {
                var go = new GameObject(name) { layer = layer };
                go.transform.SetParent(parent, false);
                var mats = new List<Material>();
                var subs = new List<CombineInstance>();
                foreach (var kv in Items)
                {
                    var sub = new Mesh { indexFormat = IndexFormat.UInt32 };
                    sub.CombineMeshes(kv.Value.ToArray(), true, true);
                    subs.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
                    mats.Add(kv.Key);
                }
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(subs.ToArray(), false, false);
                foreach (var s in subs)
                    if (Application.isPlaying) UnityEngine.Object.Destroy(s.mesh);
                    else UnityEngine.Object.DestroyImmediate(s.mesh);
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats.ToArray();
                return go;
            }
        }

        public static Result Build(QuadBuild b, QuadModelMaterials mats, Transform parent, int layer = 0)
        {
            var res = new Result();
            var f = b.Frame;
            var root = new GameObject("Quad: " + b.Name) { layer = layer };
            root.transform.SetParent(parent, false);
            res.Root = root;
            if (f == null || b.Motor == null || b.Prop == null || b.Battery == null) return res;
            // P2: the detailed Blender-generated parts when they exist for this build.
            var lib = QuadPartLibrary.Instance;
            if (lib != null && lib.Find(f.Id) != null && lib.Find(b.Motor.Id) != null && lib.Find(b.Prop.Id) != null && lib.Find(b.Battery.Id) != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                return BuildDetailed(b, mats, parent, layer, lib);
            }

            var accent = b.Color.Accent;
            var finish = b.Style.Finish;
            Material carbon = Tint(mats.Carbon, finish == FrameFinish.Stealth ? new Color(0.25f, 0.25f, 0.27f) : finish == FrameFinish.Tiger ? new Color(1.0f, 0.82f, 0.35f) : Color.white,
                finish == FrameFinish.Tiger ? TigerTexture() : null);
            Material edge = finish == FrameFinish.GoldEdge ? mats.Gold : finish == FrameFinish.Stealth ? mats.Plastic : mats.Metal;
            Material bell = Tint(mats.Bell, accent);
            Material prop = Tint(mats.Prop, new Color(accent.r, accent.g, accent.b, mats.Prop != null ? mats.Prop.color.a : 0.7f));
            Material led = TintLed(mats.Led, accent);

            BuildComposer.Arms(f, out float ax, out float az);
            float plate = f.PlateMm * 0.001f;
            float armW = f.ArmWidthMm * 0.001f;
            float bodyL = f.BodyLengthMm * 0.001f, bodyW = f.BodyWidthMm * 0.001f, stack = f.StackMm * 0.001f;
            float statorR = b.Motor.StatorD * 0.0005f, statorH = b.Motor.StatorH * 0.001f;
            float bellR = statorR + 0.0025f + statorR * 0.12f, bellH = statorH + 0.004f + statorR * 0.3f;
            float propR = b.Prop.DiameterIn * 0.0254f * 0.5f;
            var motors = new[] { new Vector3(ax, 0f, -az), new Vector3(ax, 0f, az), new Vector3(-ax, 0f, -az), new Vector3(-ax, 0f, az) };

            // ---------- frame ----------
            var frame = new Batch();
            bool ducted = f.Geometry == FrameGeometry.Ducted;
            if (ducted)
            {
                frame.Box(carbon, new Vector3(0f, plate * 0.5f, 0f), new Vector3(ax * 2f + propR, plate, az * 2f + propR));
                foreach (var m in motors)
                    Ring(frame, mats.Plastic, m + Vector3.up * (plate + bellH * 0.6f), propR + 0.004f, Mathf.Max(0.004f, propR * 0.12f), propR * 0.55f, 28);
            }
            else
            {
                // Deadcat: front arms swept out to the sides; X/stretch-X: straight arms from the body.
                foreach (var m in motors)
                {
                    Vector3 root2 = new Vector3(Mathf.Sign(m.x) * bodyW * 0.3f, plate * 0.5f, Mathf.Sign(m.z) * bodyL * 0.3f);
                    if (f.Geometry == FrameGeometry.Deadcat && m.z > 0f) root2.x = Mathf.Sign(m.x) * bodyW * 0.5f;
                    Vector3 tip = new Vector3(m.x, plate * 0.5f, m.z);
                    Vector3 dir = tip - root2;
                    frame.Box(carbon, (root2 + tip) * 0.5f, new Vector3(armW, plate, dir.magnitude + armW), Quaternion.LookRotation(dir));
                    frame.Cyl(carbon, tip, armW * 0.9f, plate);
                }
                frame.Box(carbon, new Vector3(0f, plate * 0.5f, 0f), new Vector3(bodyW, plate, bodyL));
            }
            float topY = plate + stack;
            frame.Box(carbon, new Vector3(0f, topY + plate * 0.4f, 0f), new Vector3(bodyW * 0.92f, plate * 0.8f, bodyL * 0.95f));
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    frame.Cyl(edge, new Vector3(sx * bodyW * 0.38f, plate + stack * 0.5f, sz * bodyL * 0.42f), Mathf.Max(0.0025f, bodyW * 0.03f), stack);
            if (finish == FrameFinish.GoldEdge && !ducted)
                foreach (var m in motors)
                    frame.Rod(mats.Gold, new Vector3(0f, plate + 0.0005f, 0f) + Vector3.up * plate * 0.01f, new Vector3(m.x, plate + 0.0005f, m.z), Mathf.Max(0.0015f, armW * 0.12f));
            // Flight-controller stack (board edges between the plates).
            frame.Box(mats.Plastic, new Vector3(0f, plate + stack * 0.45f, 0f), new Vector3(bodyW * 0.62f, stack * 0.55f, bodyW * 0.62f));
            var frameGo = frame.Build("Frame", root.transform, layer);
            res.Parts["Frame"] = frameGo.transform;

            // ---------- motors ----------
            var motorBatch = new Batch();
            foreach (var m in motors)
            {
                Vector3 baseC = m + Vector3.up * (plate + 0.0015f);
                motorBatch.Cyl(mats.Metal, baseC, bellR * 0.95f, 0.003f);
                motorBatch.Cyl(mats.Copper, baseC + Vector3.up * (0.0015f + statorH * 0.5f), statorR, statorH);
                motorBatch.Cyl(bell, baseC + Vector3.up * (0.003f + bellH * 0.5f), bellR, bellH);
                motorBatch.Cyl(mats.Metal, baseC + Vector3.up * (0.003f + bellH + 0.004f), Mathf.Max(0.0012f, statorR * 0.12f), 0.008f);
            }
            var motorGo = motorBatch.Build("Motors", root.transform, layer);
            res.Parts["Motors"] = motorGo.transform;

            // ---------- props ----------
            var propRoot = new GameObject("Props") { layer = layer }.transform;
            propRoot.SetParent(root.transform, false);
            float propY = plate + 0.003f + bellH + 0.003f;
            for (int i = 0; i < 4; i++)
            {
                var pb = new Batch();
                int blades = Mathf.Max(2, b.Prop.Blades);
                float chord = Mathf.Max(0.004f, propR * (blades >= 4 ? 0.22f : 0.18f));
                for (int k = 0; k < blades; k++)
                {
                    float yaw = k * 360f / blades;
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    // Two twisted panels per blade (steeper at the root).
                    for (int s = 0; s < 2; s++)
                    {
                        float r0 = propR * (0.12f + s * 0.44f), r1 = propR * (0.56f + s * 0.44f);
                        float twist = s == 0 ? 22f : 12f;
                        float w = chord * (s == 0 ? 1f : 0.72f);
                        pb.Box(prop, rot * new Vector3(0f, 0f, (r0 + r1) * 0.5f), new Vector3(w, 0.0012f + propR * 0.004f, r1 - r0), rot * Quaternion.Euler(0f, 0f, twist * (i % 3 == 0 ? -1f : 1f)));
                    }
                }
                pb.Cyl(prop, Vector3.zero, Mathf.Max(0.003f, propR * 0.11f), 0.006f);
                var go = pb.Build($"Prop {i}", propRoot, layer);
                go.transform.localPosition = motors[i] + Vector3.up * propY;
                go.transform.localRotation = Quaternion.Euler(0f, i * 37f, 0f);
                res.Props[i] = go.transform;
            }
            res.Parts["Props"] = propRoot;

            // ---------- battery ----------
            var lipo = new Batch();
            float batVol = b.Battery.MassG / 2.1f * 1e-6f; // ~2.1 g/cm³
            float batL = Mathf.Min(bodyL * 1.05f, Mathf.Pow(batVol * 3.2f, 1f / 3f) * 1.25f);
            float batW = Mathf.Min(bodyW * 0.95f, Mathf.Sqrt(batVol / batL) * 1.3f);
            float batH = batVol / (batL * batW);
            Vector3 batC = new Vector3(0f, topY + plate + batH * 0.5f + 0.001f, -bodyL * 0.06f);
            lipo.Box(mats.Lipo, batC, new Vector3(batW, batH, batL));
            lipo.Box(mats.Pad, batC + Vector3.down * (batH * 0.5f + 0.0005f), new Vector3(batW * 0.9f, 0.001f, batL * 0.9f));
            lipo.Box(mats.Plastic, batC + Vector3.up * 0.0006f, new Vector3(batW + 0.002f, batH + 0.002f, Mathf.Max(0.008f, batL * 0.18f)));
            var batGo = lipo.Build("Battery", root.transform, layer);
            res.Parts["Battery"] = batGo.transform;

            // ---------- camera, antenna, extras ----------
            var extras = new Batch();
            float camS = Mathf.Clamp(stack * 0.75f, 0.008f, 0.019f);
            Vector3 camC = new Vector3(0f, plate + stack * 0.5f, bodyL * 0.5f - camS * 0.3f);
            var tilt = Quaternion.Euler(-25f, 0f, 0f);
            extras.Box(mats.Plastic, camC, Vector3.one * camS, tilt);
            extras.Add(mats.Lens, Cylinder, Matrix4x4.TRS(camC + tilt * new Vector3(0f, 0f, camS * 0.55f), tilt * Quaternion.Euler(90f, 0f, 0f), new Vector3(camS * 0.7f, camS * 0.4f, camS * 0.7f)));
            bool longAntenna = false;
            var ledBatch = new Batch();
            foreach (var e in b.Extras)
            {
                switch (e.Kind)
                {
                    case ExtraKind.ActionCam:
                        Vector3 gc = new Vector3(0f, batC.y + batH * 0.5f + 0.022f, bodyL * 0.28f);
                        extras.Box(mats.Plastic, gc, new Vector3(0.062f, 0.044f, 0.033f), Quaternion.Euler(-15f, 0f, 0f));
                        extras.Add(mats.Lens, Cylinder, Matrix4x4.TRS(gc + new Vector3(-0.012f, 0.004f, 0.018f), Quaternion.Euler(75f, 0f, 0f), new Vector3(0.018f, 0.006f, 0.018f)));
                        break;
                    case ExtraKind.HdCamera:
                        extras.Box(mats.Plastic, new Vector3(0f, topY + plate + 0.012f, bodyL * 0.38f), new Vector3(0.024f, 0.02f, 0.02f));
                        break;
                    case ExtraKind.LongAntenna:
                        longAntenna = true;
                        break;
                    case ExtraKind.LedKit:
                        // Two rows of LED chips down both sides of the stack, and a row along the top of every arm.
                        float y0 = plate + stack * 0.3f, y1 = plate + stack * 0.68f;
                        float chip = Mathf.Max(0.003f, stack * 0.2f);
                        foreach (float sx in new[] { -1f, 1f })
                            foreach (float y in new[] { y0, y1 })
                                for (int k = 0; k < 8; k++)
                                {
                                    float z = -bodyL * 0.42f + k * bodyL * 0.84f / 7f;
                                    ledBatch.Box(led, new Vector3(sx * (bodyW * 0.5f + 0.0015f), y, z), new Vector3(0.002f, chip, bodyL * 0.075f));
                                }
                        if (!ducted)
                            foreach (var m in motors)
                                for (int k = 1; k <= 5; k++)
                                {
                                    Vector3 p = Vector3.Lerp(new Vector3(0f, plate, 0f), new Vector3(m.x, plate, m.z), 0.2f + k * 0.12f) + Vector3.up * 0.0012f;
                                    ledBatch.Box(led, p, new Vector3(armW * 0.6f, 0.0025f, armW * 0.6f), Quaternion.LookRotation(new Vector3(m.x, 0f, m.z)));
                                }
                        break;
                }
            }
            float antH = longAntenna ? 0.09f : Mathf.Max(0.02f, stack * 1.6f);
            extras.Rod(mats.Plastic, new Vector3(0f, topY, -bodyL * 0.48f), new Vector3(0f, topY + antH, -bodyL * 0.62f), longAntenna ? 0.004f : 0.003f);
            extras.Cyl(mats.Plastic, new Vector3(0f, topY + antH + 0.004f, -bodyL * 0.62f - antH * 0.12f), longAntenna ? 0.007f : 0.004f, 0.012f);
            var extrasGo = extras.Build("Camera and extras", root.transform, layer);
            res.Parts["Extras"] = extrasGo.transform;
            if (ledBatch.Items.Count > 0)
            {
                var ledGo = ledBatch.Build("LEDs", root.transform, layer);
                ledGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                res.Leds = new Renderer[] { ledGo.GetComponent<Renderer>() };
                res.Parts["LEDs"] = ledGo.transform;
            }
            return res;
        }

        /// <summary>The pack's natural size (m) from its mass (~2.1 g/cm³): width, length, height.</summary>
        public static (float w, float l, float h) BatterySize(BatteryDef b)
        {
            float vol = b.MassG / 2.1f * 1e-6f;
            float l = Mathf.Pow(vol * 3.2f, 1f / 3f) * 1.25f;
            float w = Mathf.Sqrt(vol / l) * 1.3f;
            return (w, l, vol / (l * w));
        }

        /// <summary>The stack (mounting pattern) a frame carries.</summary>
        public static string StackFor(FrameDef f) =>
            f.Size >= SizeClass.Five ? "stack-30" : f.Size == SizeClass.Three && f.Geometry == FrameGeometry.Ducted ? "stack-25" : "stack-20";

        /// <summary>
        /// P2: the build from the Blender-generated parts (<see cref="QuadPartLibrary"/>): frame, motors, props with blur
        /// discs, battery with leads, plug, strap and pad, the FC/ESC stack, FPV camera, analog VTX or HD air unit,
        /// receiver, capacitor and extras, placed on the frame's sockets. Same origin and conventions as the procedural
        /// model.
        /// </summary>
        private static Result BuildDetailed(QuadBuild b, QuadModelMaterials mats, Transform parent, int layer, QuadPartLibrary lib)
        {
            var res = new Result();
            var f = b.Frame;
            var root = new GameObject("Quad: " + b.Name) { layer = layer };
            root.transform.SetParent(parent, false);
            res.Root = root;

            var accent = b.Color.Accent;
            var finish = b.Style.Finish;
            Material carbon = Tint(mats.Carbon, finish == FrameFinish.Stealth ? new Color(0.25f, 0.25f, 0.27f) : finish == FrameFinish.Tiger ? new Color(1.0f, 0.82f, 0.35f) : Color.white,
                finish == FrameFinish.Tiger ? TigerTexture() : null);
            Material edge = finish == FrameFinish.GoldEdge ? mats.Gold : finish == FrameFinish.Stealth ? mats.Plastic : mats.Metal;
            Material Slot(string slot) => slot switch
            {
                "Carbon" => carbon,
                "Edge" => edge,
                "Metal" => mats.Metal,
                "Bell" => Tint(mats.Bell, accent),
                "Copper" => mats.Copper,
                "Prop" or "PropBlur" => Tint(mats.Prop, new Color(accent.r, accent.g, accent.b, mats.Prop != null ? mats.Prop.color.a : 0.7f)),
                "Lipo" => mats.Lipo,
                "Led" => TintLed(mats.Led, accent),
                "Lens" => mats.Lens,
                "Pad" => mats.Pad,
                "TPU" => Tint(mats.Plastic, Color.Lerp(accent, Color.black, 0.2f)),
                "PCB" => Tint(mats.Plastic, new Color(0.05f, 0.2f, 0.11f)),
                "Wire" => Tint(mats.Plastic, new Color(0.05f, 0.05f, 0.05f)),
                "WireRed" => Tint(mats.Plastic, new Color(0.7f, 0.05f, 0.04f)),
                "Rubber" => Tint(mats.Plastic, new Color(0.09f, 0.09f, 0.09f)),
                "Plug" => Tint(mats.Plastic, new Color(0.95f, 0.72f, 0.08f)),
                "Cap" => Tint(mats.Plastic, new Color(0.1f, 0.1f, 0.12f)),
                _ => mats.Plastic,
            };
            GameObject Part(string id, string name, Transform par, Vector3 pos, Quaternion rot, Vector3 scale)
            {
                var e = lib.Find(id);
                if (e == null) return null;
                var go = new GameObject(name) { layer = layer };
                go.transform.SetParent(par, false);
                go.transform.SetLocalPositionAndRotation(pos, rot);
                go.transform.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = e.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var list = new Material[e.Slots.Length];
                for (int i = 0; i < list.Length; i++) list[i] = Slot(e.Slots[i]);
                mr.sharedMaterials = list;
                return go;
            }
            Transform Group(string name)
            {
                var t = new GameObject(name) { layer = layer }.transform;
                t.SetParent(root.transform, false);
                return t;
            }

            var frameEntry = lib.Find(f.Id);
            float plate = f.PlateMm * 0.001f, stack = f.StackMm * 0.001f, bodyL = f.BodyLengthMm * 0.001f, bodyW = f.BodyWidthMm * 0.001f;
            float topY = plate + stack + plate * 0.45f;
            Vector3 Socket(string name, Vector3 fallback) => frameEntry.TryGetSocket(name, out var p) ? p : fallback;

            // Frame and the electronics between the plates.
            var frameGo = Part(f.Id, "Frame", root.transform, Vector3.zero, Quaternion.identity, Vector3.one);
            res.Parts["Frame"] = frameGo.transform;
            Part(StackFor(f), "Stack", frameGo.transform, Socket("stack", new Vector3(0f, plate, 0f)), Quaternion.identity, Vector3.one);
            Part("receiver", "Receiver", frameGo.transform, Socket("rx", new Vector3(0f, plate, -bodyL * 0.35f)), Quaternion.identity, Vector3.one * (f.Size <= SizeClass.TwoFive ? 0.7f : 1f));

            // Motors on the mounts, props on the motor shafts (mirrored blades on the counter-clockwise pair).
            BuildComposer.Arms(f, out float ax, out float az);
            var mounts = new[] { new Vector3(ax, plate, -az), new Vector3(ax, plate, az), new Vector3(-ax, plate, -az), new Vector3(-ax, plate, az) };
            for (int i = 0; i < 4; i++) mounts[i] = Socket("motor" + i, mounts[i]);
            var motorEntry = lib.Find(b.Motor.Id);
            float propLift = motorEntry != null && motorEntry.TryGetSocket("prop", out var ps) ? ps.y : 0.012f;
            var motorRoot = Group("Motors");
            for (int i = 0; i < 4; i++) Part(b.Motor.Id, $"Motor {i}", motorRoot, mounts[i], Quaternion.Euler(0f, i * 90f + 45f, 0f), Vector3.one);
            res.Parts["Motors"] = motorRoot;
            var propRoot = Group("Props");
            var discs = new List<Renderer>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 at = mounts[i] + Vector3.up * propLift;
                var mirror = i == 1 || i == 2 ? new Vector3(-1f, 1f, 1f) : Vector3.one;
                var prop = Part(b.Prop.Id, $"Prop {i}", propRoot, at, Quaternion.Euler(0f, i * 37f, 0f), mirror);
                res.Props[i] = prop != null ? prop.transform : null;
                var disc = Part(b.Prop.Id + "-blur", $"Blur {i}", propRoot, at, Quaternion.identity, Vector3.one);
                if (disc == null) continue;
                var dr = disc.GetComponent<MeshRenderer>();
                dr.shadowCastingMode = ShadowCastingMode.Off;
                dr.enabled = false;
                discs.Add(dr);
            }
            res.BlurDiscs = discs.ToArray();
            res.Parts["Props"] = propRoot;

            // Battery on the top plate (scaled down if the pack is longer or wider than the frame allows).
            var (bw, bl, bh) = BatterySize(b.Battery);
            float s = Mathf.Min(1f, bodyL * 1.05f / bl, bodyW * 0.95f / bw);
            Vector3 batC = new Vector3(0f, topY + bh * s * 0.5f + 0.001f, -bodyL * 0.06f);
            var bat = Part(b.Battery.Id, "Battery", root.transform, batC, Quaternion.identity, Vector3.one * s);
            res.Parts["Battery"] = bat != null ? bat.transform : root.transform;

            // Camera, video and extras.
            var extras = Group("Camera and extras");
            bool hd = false, actionCam = false, longAntenna = false, leds = false;
            foreach (var e in b.Extras)
            {
                hd |= e.Kind == ExtraKind.HdCamera || e.Kind == ExtraKind.DigitalHd;
                actionCam |= e.Kind == ExtraKind.ActionCam;
                longAntenna |= e.Kind == ExtraKind.LongAntenna;
                leds |= e.Kind == ExtraKind.LedKit;
            }
            string cam = hd ? "camera-hd" : f.Size >= SizeClass.Five ? "camera-micro" : "camera-nano";
            Part(cam, "FPV camera", extras, Socket("camera", new Vector3(0f, plate + stack * 0.5f, bodyL * 0.4f)), Quaternion.Euler(-25f, 0f, 0f), Vector3.one * (f.Size == SizeClass.Whoop ? 0.85f : 1f));
            bool digital = false;
            foreach (var e in b.Extras) digital |= e.Kind == ExtraKind.DigitalHd;
            Vector3 antenna = Socket("antenna", new Vector3(0f, topY, -bodyL * 0.47f));
            Part(digital ? "vtx-hd" : "vtx-analog", digital ? "HD air unit" : "VTX antenna", extras, antenna, Quaternion.identity, longAntenna ? new Vector3(1f, 2.2f, 1f) : Vector3.one);
            if (f.Size > SizeClass.Whoop)
                Part("capacitor", "Capacitor", extras, new Vector3(bodyW * 0.3f, topY + 0.004f, -bodyL * 0.42f), Quaternion.Euler(0f, 20f, 0f), Vector3.one);
            if (actionCam)
                Part("actioncam", "Action camera", extras, new Vector3(0f, batC.y + bh * s * 0.5f + 0.026f, bodyL * 0.28f), Quaternion.Euler(-15f, 0f, 0f), Vector3.one);
            res.Parts["Extras"] = extras;

            if (leds)
            {
                var ledBatch = new Batch();
                Material led = TintLed(mats.Led, accent);
                float chip = Mathf.Max(0.003f, stack * 0.2f);
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float y in new[] { plate + stack * 0.3f, plate + stack * 0.68f })
                        for (int k = 0; k < 8; k++)
                            ledBatch.Box(led, new Vector3(sx * (bodyW * 0.5f + 0.0015f), y, -bodyL * 0.42f + k * bodyL * 0.84f / 7f), new Vector3(0.002f, chip, bodyL * 0.075f));
                if (ledBatch.Items.Count > 0)
                {
                    var ledGo = ledBatch.Build("LEDs", root.transform, layer);
                    ledGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    res.Leds = new Renderer[] { ledGo.GetComponent<Renderer>() };
                    res.Parts["LEDs"] = ledGo.transform;
                }
            }
            return res;
        }

        /// <summary>Size of the model (bounds of every renderer), for framing cameras.</summary>
        public static Bounds Bounds(GameObject root)
        {
            Bounds? b = null;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (b == null) b = r.bounds;
                else
                {
                    var bb = b.Value;
                    bb.Encapsulate(r.bounds);
                    b = bb;
                }
            }
            return b ?? new Bounds(root.transform.position, Vector3.one * 0.1f);
        }

        private static void Ring(Batch b, Material m, Vector3 c, float radius, float thickness, float height, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 p1 = c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                Vector3 d = p1 - p0;
                b.Box(m, (p0 + p1) * 0.5f, new Vector3(thickness, height, d.magnitude * 1.05f), Quaternion.LookRotation(d));
            }
        }

        // ---------- materials ----------

        private static readonly Dictionary<(Material, Color, Texture), Material> TintCache = new Dictionary<(Material, Color, Texture), Material>();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        private static Material Tint(Material m, Color c, Texture tex = null)
        {
            if (m == null) return null;
            if (c == Color.white && tex == null) return m;
            var key = (m, c, tex);
            if (TintCache.TryGetValue(key, out var t) && t != null) return t;
            t = new Material(m) { name = m.name + " (tint)" };
            if (t.HasProperty(BaseColor)) t.SetColor(BaseColor, c);
            if (tex != null && t.HasProperty(BaseMap))
            {
                t.SetTexture(BaseMap, tex);
                t.SetTextureScale(BaseMap, Vector2.one * 8f);
            }
            TintCache[key] = t;
            return t;
        }

        private static Material TintLed(Material m, Color c) => Tint(m, c);

        private static Texture2D _tiger;

        /// <summary>Procedural tiger-stripe print (wrapped plates).</summary>
        private static Texture2D TigerTexture()
        {
            if (_tiger != null) return _tiger;
            const int n = 256;
            _tiger = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Tiger Wrap", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float stripe = Mathf.Sin((u * 6f + Mathf.PerlinNoise(u * 3f, v * 3f) * 1.6f) * Mathf.PI * 2f);
                    bool dark = stripe > 0.55f;
                    px[y * n + x] = dark ? new Color32(18, 14, 10, 255) : new Color32(235, 170, 40, 255);
                }
            _tiger.SetPixels32(px);
            _tiger.Apply(true, true);
            return _tiger;
        }

        // ---------- primitive meshes ----------

        public static Mesh Cube
        {
            get
            {
                if (_cube != null) return _cube;
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var uv = new List<Vector2>();
                var t = new List<int>();
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var d in dirs)
                {
                    Vector3 a = d.y != 0f ? Vector3.right : Vector3.up;
                    Vector3 u = Vector3.Cross(d, a), w = Vector3.Cross(d, u);
                    int b0 = v.Count;
                    foreach (var (su, sw) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                    {
                        v.Add((d + u * su + w * sw) * 0.5f);
                        n.Add(d);
                        uv.Add(new Vector2((su + 1f) * 0.5f, (sw + 1f) * 0.5f));
                    }
                    t.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
                }
                _cube = new Mesh { name = "Garage Cube" };
                _cube.SetVertices(v);
                _cube.SetNormals(n);
                _cube.SetUVs(0, uv);
                _cube.SetTriangles(t, 0);
                FixWinding(_cube);
                _cube.RecalculateTangents();
                return _cube;
            }
        }

        /// <summary>Unit cylinder (diameter 1, height 1, axis Y, centred).</summary>
        public static Mesh Cylinder
        {
            get
            {
                if (_cylinder != null) return _cylinder;
                const int seg = 20;
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var uv = new List<Vector2>();
                var t = new List<int>();
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * 0.5f + Vector3.down * 0.5f);
                    v.Add(d * 0.5f + Vector3.up * 0.5f);
                    n.Add(d);
                    n.Add(d);
                    uv.Add(new Vector2(i / (float)seg, 0f));
                    uv.Add(new Vector2(i / (float)seg, 1f));
                    if (i < seg)
                    {
                        int b = i * 2;
                        t.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
                    }
                }
                foreach (float y in new[] { 0.5f, -0.5f })
                {
                    int c = v.Count;
                    v.Add(new Vector3(0f, y, 0f));
                    n.Add(new Vector3(0f, Mathf.Sign(y), 0f));
                    uv.Add(new Vector2(0.5f, 0.5f));
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = i * Mathf.PI * 2f / seg;
                        v.Add(new Vector3(Mathf.Cos(a) * 0.5f, y, Mathf.Sin(a) * 0.5f));
                        n.Add(new Vector3(0f, Mathf.Sign(y), 0f));
                        uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                        if (i < seg) t.AddRange(new[] { c, c + 1 + i, c + 2 + i });
                    }
                }
                _cylinder = new Mesh { name = "Garage Cylinder" };
                _cylinder.SetVertices(v);
                _cylinder.SetNormals(n);
                _cylinder.SetUVs(0, uv);
                _cylinder.SetTriangles(t, 0);
                FixWinding(_cylinder);
                _cylinder.RecalculateTangents();
                return _cylinder;
            }
        }

        /// <summary>Makes every triangle face along its vertex normals.</summary>
        private static void FixWinding(Mesh m)
        {
            var v = m.vertices;
            var n = m.normals;
            var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) < 0f) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }
            m.triangles = t;
        }
    }
}
