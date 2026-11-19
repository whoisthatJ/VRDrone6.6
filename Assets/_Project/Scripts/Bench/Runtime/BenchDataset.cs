#if !USHQYR_BENCH_SERVER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>
    /// Makes a labelled image dataset of gates from a track: training mode only, never part of the server build.
    ///
    /// Camera poses come from the oracle pilot's flights (with random offsets) and from random places in the venue.
    /// Every image goes through the same camera as a real run (lens, exposure, analog link). Each visible gate gets a
    /// box, the four inner corners of its opening as keypoints with visibility, and "occluded" / "truncated" flags;
    /// optionally a mask of which pixels belong to which gate. Output: YOLO (boxes with keypoints) and COCO, split into
    /// train / val / test. The scene is varied from the seed: light, LED brightness and colour, surfaces, clutter,
    /// video noise, exposure, motion blur, small gate shifts.
    ///
    /// The labels are checked against a second render in which every gate is painted in its own flat colour: the box
    /// of a gate that is wholly in view must match the outline of its painted pixels within two pixels on every side,
    /// and every visible corner must lie on its gate. The result is in dataset.json.
    /// </summary>
    public sealed class BenchDataset : MonoBehaviour
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] KeypointNames = { "top_left", "top_right", "bottom_right", "bottom_left" };

        public struct Options
        {
            public string Folder;
            public int Count;
            public long Seed;
            public string Profile;
            public bool Masks;
        }

        private struct Label
        {
            public string Gate;
            public Rect Box;
            public Vector2[] Corners;
            public int[] Visible;
            public bool Occluded, Truncated, Whole;
            public float Distance;
            public int Index;
        }

        public int Done { get; private set; }
        public int Total { get; private set; }
        public bool Finished { get; private set; }
        public string Error { get; private set; }
        public string Summary { get; private set; } = "";

        public void Run(BenchSession session, Options options) => StartCoroutine(Make(session, options));

        private IEnumerator Make(BenchSession session, Options o)
        {
            Total = o.Count;
            int w = session.CameraWidth, h = session.CameraHeight;
            var frame = new byte[w * h * 3];
            var mask = new byte[w * h];
            var flipped = new byte[w * h * 3];
            var image = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            var maskTexture = new Texture2D(w, h, TextureFormat.R8, false, true);
            var splits = new[] { "train", "val", "test" };
            var coco = new Dictionary<string, (StringBuilder images, StringBuilder annotations)>();
            var counts = new Dictionary<string, int>();
            try
            {
                foreach (string s in splits)
                {
                    Directory.CreateDirectory(Path.Combine(o.Folder, "images", s));
                    Directory.CreateDirectory(Path.Combine(o.Folder, "labels", s));
                    if (o.Masks) Directory.CreateDirectory(Path.Combine(o.Folder, "masks", s));
                    coco[s] = (new StringBuilder(), new StringBuilder());
                    counts[s] = 0;
                }
                Directory.CreateDirectory(Path.Combine(o.Folder, "annotations"));
            }
            catch (Exception e)
            {
                Error = e.Message;
                Finished = true;
                yield break;
            }

            Venue venue = Venue.Get(session.Track.venue);
            OracleResult path = null;
            int annotationId = 1, labelled = 0, empty = 0, checkedCorners = 0, goodCorners = 0, checkedBoxes = 0, goodBoxes = 0;
            var labels = new List<Label>();
            const int block = 25;

            for (int i = 0; i < o.Count; i++)
            {
                var rng = BenchRandom.For(o.Seed, "sample-" + i);
                if (i % block == 0)
                {
                    // A new variation of the scene, and the oracle's flight through it for camera poses.
                    long variationSeed = o.Seed * 100003L + i / block;
                    SceneVariation v = session.DrawVariation(o.Profile, variationSeed);
                    if (o.Profile == "strong")
                    {
                        var colours = BenchRandom.For(variationSeed, "led-colours");
                        bool mixed = colours.NextFloat() < 0.5f;
                        string one = TrackGenerator.Colours[(int)(colours.NextFloat() * TrackGenerator.Colours.Length) % TrackGenerator.Colours.Length];
                        foreach (TrackGate g in v.Track.gates)
                            g.led.color = mixed ? TrackGenerator.Colours[(int)(colours.NextFloat() * TrackGenerator.Colours.Length) % TrackGenerator.Colours.Length] : one;
                    }
                    session.ApplyVariation(v);
                    path = BenchOracle.Fly(v.Track, session.SolidLayers, 45f);
                }

                // Camera pose.
                Vector3 position;
                Quaternion rotation;
                if (path != null && path.Path.Count > 40 && rng.NextFloat() < 0.7f)
                {
                    int k = 20 + (int)(rng.NextFloat() * (path.Path.Count - 21));
                    position = path.Path[k] + new Vector3((rng.NextFloat() - 0.5f) * 1.0f, (rng.NextFloat() - 0.5f) * 0.6f, (rng.NextFloat() - 0.5f) * 1.0f);
                    rotation = path.Attitude[k] * Quaternion.Euler((rng.NextFloat() - 0.5f) * 20f, (rng.NextFloat() - 0.5f) * 60f, (rng.NextFloat() - 0.5f) * 20f);
                }
                else
                {
                    position = new Vector3((rng.NextFloat() - 0.5f) * (venue.Length - 2f), 0f, (rng.NextFloat() - 0.5f) * (venue.Width - 2f));
                    rotation = Quaternion.Euler((rng.NextFloat() - 0.5f) * 30f, rng.NextFloat() * 360f, (rng.NextFloat() - 0.5f) * 30f);
                    position.y = 0.2f + rng.NextFloat() * (Mathf.Min(2.8f, venue.Height - 0.5f) - 0.2f);
                }
                if (!venue.Free(position.x, position.z, 0.3f)) position = new Vector3(0f, position.y, 0f);
                position.y = Mathf.Clamp(position.y, 0.15f, venue.Height - 0.3f);
                session.Quad.Teleport(position, rotation);

                float signal = 0.35f + 0.65f * rng.NextFloat(), gain = 0.7f + 0.8f * rng.NextFloat();
                int blur = rng.NextFloat() < 0.3f ? 2 + (int)(rng.NextFloat() * 5f) : 0;
                session.RenderDatasetFrame(i, signal, gain, frame, mask);
                if (blur > 0) MotionBlur(frame, flipped, w, h, blur);

                labels.Clear();
                MakeLabels(session, w, h, labels);
                // Label check against the flat-colour render.
                foreach (Label l in labels)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        if (l.Visible[c] != 2) continue;
                        checkedCorners++;
                        if (OnGate(mask, w, h, l.Corners[c], l.Index + 1)) goodCorners++;
                    }
                    if (!l.Whole) continue;
                    checkedBoxes++;
                    if (BoxMatches(mask, w, h, l.Box, l.Index + 1)) goodBoxes++;
                }

                // Files.
                float u = rng.NextFloat();
                string split = u < 0.8f ? "train" : u < 0.9f ? "val" : "test";
                string name = i.ToString("000000", Inv);
                for (int row = 0; row < h; row++) Buffer.BlockCopy(frame, row * w * 3, flipped, (h - 1 - row) * w * 3, w * 3);
                image.LoadRawTextureData(flipped);
                image.Apply(false);
                File.WriteAllBytes(Path.Combine(o.Folder, "images", split, name + ".jpg"), image.EncodeToJPG(92));
                if (o.Masks)
                {
                    for (int row = 0; row < h; row++) Buffer.BlockCopy(mask, row * w, flipped, (h - 1 - row) * w, w);
                    maskTexture.LoadRawTextureData(new ArraySegment<byte>(flipped, 0, w * h).ToArray());
                    maskTexture.Apply(false);
                    File.WriteAllBytes(Path.Combine(o.Folder, "masks", split, name + ".png"), maskTexture.EncodeToPNG());
                }

                var yolo = new StringBuilder();
                var (cocoImages, cocoAnnotations) = coco[split];
                if (cocoImages.Length > 0) cocoImages.Append(",\n");
                cocoImages.Append($"    {{\"id\": {i + 1}, \"file_name\": \"images/{split}/{name}.jpg\", \"width\": {w}, \"height\": {h}}}");
                foreach (Label l in labels)
                {
                    yolo.Append("0 ").Append(F(l.Box.center.x / w)).Append(' ').Append(F(l.Box.center.y / h)).Append(' ')
                        .Append(F(l.Box.width / w)).Append(' ').Append(F(l.Box.height / h));
                    var keypoints = new StringBuilder();
                    int visible = 0;
                    for (int c = 0; c < 4; c++)
                    {
                        bool inside = l.Visible[c] > 0;
                        yolo.Append(' ').Append(F(inside ? l.Corners[c].x / w : 0f)).Append(' ').Append(F(inside ? l.Corners[c].y / h : 0f)).Append(' ').Append(l.Visible[c]);
                        if (c > 0) keypoints.Append(", ");
                        keypoints.Append(inside ? l.Corners[c].x.ToString("F1", Inv) : "0").Append(", ").Append(inside ? l.Corners[c].y.ToString("F1", Inv) : "0").Append(", ").Append(l.Visible[c]);
                        if (inside) visible++;
                    }
                    yolo.Append('\n');
                    if (cocoAnnotations.Length > 0) cocoAnnotations.Append(",\n");
                    cocoAnnotations.Append($"    {{\"id\": {annotationId++}, \"image_id\": {i + 1}, \"category_id\": 1, \"iscrowd\": 0, ")
                        .Append($"\"bbox\": [{l.Box.x.ToString("F1", Inv)}, {l.Box.y.ToString("F1", Inv)}, {l.Box.width.ToString("F1", Inv)}, {l.Box.height.ToString("F1", Inv)}], ")
                        .Append($"\"area\": {(l.Box.width * l.Box.height).ToString("F1", Inv)}, \"keypoints\": [{keypoints}], \"num_keypoints\": {visible}, ")
                        .Append($"\"attributes\": {{\"gate\": \"{l.Gate}\", \"occluded\": {(l.Occluded ? "true" : "false")}, \"truncated\": {(l.Truncated ? "true" : "false")}, ")
                        .Append($"\"distance_m\": {l.Distance.ToString("F2", Inv)}}}}}");
                }
                File.WriteAllText(Path.Combine(o.Folder, "labels", split, name + ".txt"), yolo.ToString());
                counts[split]++;
                labelled += labels.Count;
                if (labels.Count == 0) empty++;
                Done = i + 1;
                if (i % 4 == 3) yield return null;
            }

            // Index files.
            try
            {
                File.WriteAllText(Path.Combine(o.Folder, "data.yaml"),
                    "# Ushqyr AI Bench gate dataset (YOLO pose format: box + 4 keypoints with visibility)\n" +
                    "path: .\ntrain: images/train\nval: images/val\ntest: images/test\n" +
                    "names:\n  0: gate\nkpt_shape: [4, 3]\nflip_idx: [1, 0, 3, 2]\n");
                foreach (string s in splits)
                    File.WriteAllText(Path.Combine(o.Folder, "annotations", $"instances_{s}.json"),
                        "{\n  \"info\": {\"description\": \"Ushqyr AI Bench gate dataset\", \"version\": \"1\"},\n" +
                        "  \"categories\": [{\"id\": 1, \"name\": \"gate\", \"keypoints\": [\"top_left\", \"top_right\", \"bottom_right\", \"bottom_left\"], " +
                        "\"skeleton\": [[1, 2], [2, 3], [3, 4], [4, 1]]}],\n" +
                        "  \"images\": [\n" + coco[s].images + "\n  ],\n  \"annotations\": [\n" + coco[s].annotations + "\n  ]\n}\n");
                float share = checkedBoxes > 0 ? (float)goodBoxes / checkedBoxes : 1f;
                Summary = FormattableString.Invariant(
                    $"{o.Count} images ({counts["train"]} / {counts["val"]} / {counts["test"]}), {labelled} gates labelled, {empty} images without a gate; ") +
                    FormattableString.Invariant($"label check: {goodBoxes} of {checkedBoxes} boxes within 2 px ({share * 100f:F1}%), {goodCorners} of {checkedCorners} corners on their gate");
                File.WriteAllText(Path.Combine(o.Folder, "dataset.json"),
                    "{\n" +
                    $"  \"track\": \"{session.Track.id}\",\n  \"track_hash\": \"{session.Track.Hash}\",\n  \"seed\": {o.Seed},\n  \"profile\": \"{o.Profile}\",\n" +
                    $"  \"images\": {o.Count},\n  \"train\": {counts["train"]},\n  \"val\": {counts["val"]},\n  \"test\": {counts["test"]},\n" +
                    $"  \"gates_labelled\": {labelled},\n  \"images_without_gate\": {empty},\n  \"masks\": {(o.Masks ? "true" : "false")},\n" +
                    $"  \"width\": {w},\n  \"height\": {h},\n  \"fx\": {session.CameraFocal.ToString("F3", Inv)},\n  \"k1\": {session.CameraDistortion.ToString("F4", Inv)},\n" +
                    "  \"keypoints\": [\"top_left\", \"top_right\", \"bottom_right\", \"bottom_left\"],\n" +
                    "  \"keypoint_visibility\": \"0 = outside the picture, 1 = in the picture but hidden, 2 = visible\",\n" +
                    $"  \"label_check\": {{\"boxes_checked\": {checkedBoxes}, \"boxes_within_2px\": {goodBoxes}, \"share\": {share.ToString("F4", Inv)}, " +
                    $"\"corners_checked\": {checkedCorners}, \"corners_on_gate\": {goodCorners}}}\n" +
                    "}\n");
            }
            catch (Exception e) { Error = e.Message; }
            Destroy(image);
            Destroy(maskTexture);
            Debug.Log("[Bench] dataset: " + (Error ?? Summary) + " -> " + o.Folder);
            Finished = true;
        }

        private static string F(float v) => v.ToString("F6", Inv);

        /// <summary>Boxes and corner keypoints of every gate the camera sees.</summary>
        private static void MakeLabels(BenchSession session, int w, int h, List<Label> into)
        {
            Transform cam = session.CameraTransform;
            float f = session.CameraFocal, k1 = session.CameraDistortion, cx = w * 0.5f, cy = h * 0.5f;
            const float half = GateGeometry.Opening * 0.5f, outer = GateGeometry.Outer * 0.5f, depth = GateGeometry.Depth * 0.5f, centre = GateGeometry.CentreHeight;
            for (int g = 0; g < session.Gates.Count; g++)
            {
                Transform gate = session.Gates[g].transform;
                Vector3 local = gate.InverseTransformPoint(cam.position);
                float distance = Vector3.Distance(cam.position, gate.TransformPoint(new Vector3(0f, centre, 0f)));
                if (distance > 30f) continue;
                // The face the camera looks at, and which way "left" is from there.
                float face = local.z < 0f ? -depth : depth, mirror = local.z < 0f ? 1f : -1f;

                // Box: the outline of the frame on both faces, sampled so the lens's bending is followed.
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                int inFront = 0, inPicture = 0, seen = 0, samples = 0;
                foreach (float z in new[] { -depth, depth })
                    for (int edge = 0; edge < 4; edge++)
                        for (int k = 0; k < 8; k++)
                        {
                            float t = -outer + 2f * outer * k / 8f;
                            Vector3 p = edge == 0 ? new Vector3(t, centre + outer, z) : edge == 1 ? new Vector3(outer, centre - t, z)
                                : edge == 2 ? new Vector3(-t, centre - outer, z) : new Vector3(-outer, centre + t, z);
                            Vector3 world = gate.TransformPoint(p);
                            samples++;
                            Vector3 inCamera = cam.InverseTransformPoint(world);
                            // Points beside or behind the lens have no place in the picture.
                            if (inCamera.z < 0.25f || !LensProjection.Project(inCamera, f, cx, cy, k1, out Vector2 px)) continue;
                            inFront++;
                            minX = Mathf.Min(minX, px.x); maxX = Mathf.Max(maxX, px.x);
                            minY = Mathf.Min(minY, px.y); maxY = Mathf.Max(maxY, px.y);
                            if (px.x < 0f || px.y < 0f || px.x >= w || px.y >= h) continue;
                            inPicture++;
                            if (Mathf.Sign(z) == Mathf.Sign(face) && Clear(cam.position, world, session.SolidLayers)) seen++;
                        }
                // A gate the camera is inside of, or mostly beside, is not an object in the picture.
                if (inFront < samples * 3 / 4 || inPicture < 3 || distance < 1.0f) continue;
                bool truncated = minX < 0f || minY < 0f || maxX > w || maxY > h || inFront < samples;
                var box = Rect.MinMaxRect(Mathf.Max(0f, minX), Mathf.Max(0f, minY), Mathf.Min(w, maxX), Mathf.Min(h, maxY));
                if (box.width < 10f || box.height < 10f || seen == 0) continue;

                var label = new Label
                {
                    Gate = session.Gates[g].Id, Box = box, Corners = new Vector2[4], Visible = new int[4], Truncated = truncated,
                    // Half of the samples face the camera; fewer than 60% of those in the picture being visible = occluded.
                    Occluded = seen < inPicture * 0.5f * 0.6f, Distance = distance, Index = g,
                    // Wholly in view: nothing cut off by the picture's edge, and every sampled point of the near face seen.
                    Whole = !truncated && inPicture == samples && seen == samples / 2,
                };
                Vector3[] corners =
                {
                    new Vector3(-half * mirror, centre + half, face), new Vector3(half * mirror, centre + half, face),
                    new Vector3(half * mirror, centre - half, face), new Vector3(-half * mirror, centre - half, face),
                };
                for (int c = 0; c < 4; c++)
                {
                    Vector3 world = gate.TransformPoint(corners[c]);
                    if (!LensProjection.Project(cam.InverseTransformPoint(world), f, cx, cy, k1, out Vector2 px) || px.x < 0f || px.y < 0f || px.x >= w || px.y >= h)
                        continue;
                    label.Corners[c] = px;
                    label.Visible[c] = Clear(cam.position, world, session.SolidLayers) ? 2 : 1;
                }
                into.Add(label);
            }
        }

        private static bool Clear(Vector3 from, Vector3 to, LayerMask solid)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            return length < 0.08f || !Physics.Raycast(from, d / length, length - 0.06f, solid, QueryTriggerInteraction.Ignore);
        }

        /// <summary>True when the mask shows this gate within two pixels of <paramref name="p"/>.</summary>
        private static bool OnGate(byte[] mask, int w, int h, Vector2 p, int id)
        {
            int px = Mathf.RoundToInt(p.x), py = Mathf.RoundToInt(p.y);
            for (int y = Mathf.Max(0, py - 2); y <= Mathf.Min(h - 1, py + 2); y++)
                for (int x = Mathf.Max(0, px - 2); x <= Mathf.Min(w - 1, px + 2); x++)
                    if (mask[y * w + x] == id) return true;
            return false;
        }

        /// <summary>True when the label's box and the outline of this gate's painted pixels agree within two pixels on every side.</summary>
        private static bool BoxMatches(byte[] mask, int w, int h, Rect box, int id)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (mask[row + x] != id) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return false;
            // Pixel k covers [k, k + 1): the painted area's right and bottom edges are one past the last pixel.
            return Mathf.Abs(box.xMin - minX) <= 2f && Mathf.Abs(box.xMax - (maxX + 1)) <= 2f
                   && Mathf.Abs(box.yMin - minY) <= 2f && Mathf.Abs(box.yMax - (maxY + 1)) <= 2f;
        }

        /// <summary>A sideways smear of <paramref name="length"/> pixels, as from a turning camera with a slow shutter.</summary>
        private static void MotionBlur(byte[] frame, byte[] scratch, int w, int h, int length)
        {
            Buffer.BlockCopy(frame, 0, scratch, 0, frame.Length);
            for (int y = 0; y < h; y++)
            {
                int row = y * w * 3;
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        int sum = 0, n = 0;
                        for (int k = 0; k < length; k++)
                        {
                            int sx = x + k - length / 2;
                            if (sx < 0 || sx >= w) continue;
                            sum += scratch[row + sx * 3 + c];
                            n++;
                        }
                        frame[row + x * 3 + c] = (byte)(sum / n);
                    }
            }
        }
    }
}
#endif
