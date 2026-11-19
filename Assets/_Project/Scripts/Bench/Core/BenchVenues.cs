using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>A solid box standing on the floor of a venue (a column, a parked car).</summary>
    public struct VenueBox
    {
        public string Kind;
        /// <summary>Centre on the floor plan (x, z), metres.</summary>
        public float X, Z;
        public float SizeX, SizeZ, Height;
        /// <summary>For cars: index into the builder's colour list.</summary>
        public int Variant;

        public bool Contains(float x, float z, float margin) =>
            Mathf.Abs(x - X) <= SizeX * 0.5f + margin && Mathf.Abs(z - Z) <= SizeZ * 0.5f + margin;

        /// <summary>Does the segment a-b pass through the box grown by <paramref name="margin"/>?</summary>
        public bool Blocks(Vector2 a, Vector2 b, float margin)
        {
            // Slab test on the floor plan.
            float t0 = 0f, t1 = 1f;
            Vector2 d = b - a;
            for (int axis = 0; axis < 2; axis++)
            {
                float lo = (axis == 0 ? X - SizeX * 0.5f : Z - SizeZ * 0.5f) - margin;
                float hi = (axis == 0 ? X + SizeX * 0.5f : Z + SizeZ * 0.5f) + margin;
                float p = axis == 0 ? a.x : a.y, q = axis == 0 ? d.x : d.y;
                if (Mathf.Abs(q) < 1e-6f)
                {
                    if (p < lo || p > hi) return false;
                    continue;
                }
                float ta = (lo - p) / q, tb = (hi - p) / q;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Mathf.Max(t0, ta);
                t1 = Mathf.Min(t1, tb);
                if (t0 > t1) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The size and the fixed obstacles of a venue. One description serves the scene builder (which makes the geometry
    /// from it), the track validator and the generator, so they can never disagree.
    /// </summary>
    public sealed class Venue
    {
        public string Id;
        /// <summary>Inner size: length along x, width along z, clear height (m). The floor plan is centred on the origin.</summary>
        public float Length, Width, Height;
        public readonly List<VenueBox> Boxes = new List<VenueBox>();
        public string[] Lighting = { "default" };

        public static readonly string[] Ids = { "hall", "parking" };

        public static Venue Get(string id) => id == "parking" ? Parking : Hall;

        public static readonly Venue Hall = new Venue { Id = "hall", Length = 40f, Width = 20f, Height = 8f, Lighting = new[] { "default", "dim", "warm" } };

        public static readonly Venue Parking = MakeParking();

        private static Venue MakeParking()
        {
            var v = new Venue { Id = "parking", Length = 60f, Width = 34f, Height = 3.4f, Lighting = new[] { "default", "dim", "bright" } };
            // Column grid: 8 m along the length, rows at z = -12, -4, 4, 12.
            for (int ix = -3; ix <= 3; ix++)
                foreach (float z in new[] { -12f, -4f, 4f, 12f })
                    v.Boxes.Add(new VenueBox { Kind = "column", X = ix * 8f, Z = z, SizeX = 0.6f, SizeZ = 0.6f, Height = v.Height });
            // Cars parked nose-in along both long walls, with gaps.
            int n = 0;
            foreach (float z in new[] { -14.6f, 14.6f })
                for (float x = -27f; x <= 27f; x += 2.7f, n++)
                {
                    if (n % 4 == 2) continue;
                    v.Boxes.Add(new VenueBox { Kind = "car", X = x, Z = z, SizeX = 1.85f, SizeZ = 4.4f, Height = 1.5f, Variant = n });
                }
            return v;
        }

        public bool Inside(float x, float z, float margin) =>
            Mathf.Abs(x) <= Length * 0.5f - margin && Mathf.Abs(z) <= Width * 0.5f - margin;

        public bool Free(float x, float z, float margin)
        {
            if (!Inside(x, z, margin)) return false;
            foreach (VenueBox b in Boxes)
                if (b.Contains(x, z, margin)) return false;
            return true;
        }

        public bool ClearLine(Vector2 a, Vector2 b, float margin, float aboveHeight = 0f)
        {
            foreach (VenueBox box in Boxes)
                if (box.Height > aboveHeight && box.Blocks(a, b, margin)) return false;
            return true;
        }
    }

    public struct TrackIssue
    {
        public bool Error;
        public string Gate;
        public string Text;
        public override string ToString() => (Error ? "error: " : "warning: ") + (string.IsNullOrEmpty(Gate) ? "" : Gate + ": ") + Text;
    }

    /// <summary>The checks a track must pass before it is flown: by the editor, the generator and the tests.</summary>
    public static class TrackValidator
    {
        public const float WallClearance = 0.5f;
        public const float ObstacleClearance = 0.35f;
        public const float FlyThrough = 1.2f;

        public static Vector2 Flat(float[] p) => new Vector2(p[0], p[2]);
        public static Vector2 Forward(float yaw) => new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));

        public static List<TrackIssue> Validate(BenchTrack t)
        {
            var issues = new List<TrackIssue>();
            void Err(string gate, string text) => issues.Add(new TrackIssue { Error = true, Gate = gate, Text = text });
            void Warn(string gate, string text) => issues.Add(new TrackIssue { Gate = gate, Text = text });

            if (Array.IndexOf(Venue.Ids, t.venue) < 0)
            {
                Err(null, $"unknown venue \"{t.venue}\"");
                return issues;
            }
            Venue venue = Venue.Get(t.venue);
            if (Array.IndexOf(venue.Lighting, t.lighting) < 0) Warn(null, $"unknown lighting \"{t.lighting}\" (the default is used)");
            int window = t.rules != null ? t.rules.repeat_window : 4;
            if (t.gates.Count < window + 1)
                Err(null, $"{t.gates.Count} gates, but a gate scores again only after {window} others: at least {window + 1} are needed");
            if (GateGeometry.Outer + 0.3f > venue.Height) Err(null, "the venue is too low for the standard gate");

            var ids = new HashSet<string>();
            for (int i = 0; i < t.gates.Count; i++)
            {
                TrackGate g = t.gates[i];
                if (string.IsNullOrEmpty(g.id) || !ids.Add(g.id)) Err(g.id, "every gate needs its own id");
                if (g.direction != "any" && g.direction != "front") Err(g.id, "direction must be \"any\" or \"front\"");
                if (g.led == null || !ColorUtility.TryParseHtmlString(g.led.color, out _)) Err(g.id, "the LED colour must look like #RRGGBB");
                if (Mathf.Abs(g.position[1]) > 0.01f) Warn(g.id, "the gate doesn't stand on the floor");

                Vector2 c = Flat(g.position), n = Forward(g.yaw), side = new Vector2(n.y, -n.x);
                // The frame's footprint: sample along its width.
                for (float s = -GateGeometry.Outer * 0.5f; s <= GateGeometry.Outer * 0.5f + 1e-3f; s += 0.25f)
                {
                    Vector2 p = c + side * s;
                    if (!venue.Inside(p.x, p.y, WallClearance)) { Err(g.id, "too close to a wall"); break; }
                    bool hit = false;
                    foreach (VenueBox b in venue.Boxes)
                        if (b.Contains(p.x, p.y, ObstacleClearance + GateGeometry.Depth * 0.5f)) { Err(g.id, $"too close to a {b.Kind}"); hit = true; break; }
                    if (hit) break;
                }
                // Room to fly in and out.
                foreach (float along in new[] { -FlyThrough, FlyThrough })
                {
                    Vector2 p = c + n * along;
                    if (!venue.Free(p.x, p.y, 0.3f)) { Err(g.id, "no room to fly through (something stands right before or behind it)"); break; }
                }
                for (int j = 0; j < i; j++)
                {
                    float d = Vector2.Distance(c, Flat(t.gates[j].position));
                    string metres = d.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    if (d < 2.6f) Err(g.id, $"too close to {t.gates[j].id} ({metres} m)");
                    else if (d < 4f) Warn(g.id, $"close to {t.gates[j].id} ({metres} m)");
                }
            }

            Vector2 start = Flat(t.start.position);
            if (!venue.Free(start.x, start.y, 0.6f)) Err(null, "the start pad is outside the venue or inside an obstacle");
            bool seen = false;
            foreach (TrackGate g in t.gates)
            {
                Vector2 to = Flat(g.position) - start;
                if (to.magnitude < 1.5f) Err(g.id, "the start pad is right at this gate");
                if (Vector2.Angle(Forward(t.start.yaw), to) <= 60f && venue.ClearLine(start, Flat(g.position), 0.1f, 1f)) seen = true;
            }
            if (!seen) Err(null, "no gate is in view from the start pad");
            return issues;
        }

        public static bool HasErrors(List<TrackIssue> issues)
        {
            foreach (TrackIssue i in issues)
                if (i.Error) return true;
            return false;
        }
    }

    /// <summary>
    /// Random tracks from a seed and a difficulty (1 easy, 2 medium, 3 hard): a closed loop of gates around the venue
    /// with more gates, sharper turns, less regular spacing and mixed LED colours as the difficulty rises. The same
    /// seed always gives the same track; every track it returns passes <see cref="TrackValidator"/>.
    /// </summary>
    public static class TrackGenerator
    {
        public static readonly string[] Colours = { "#FF00C8", "#00E5FF", "#FFD400", "#3CFF4E", "#FF5A1F" };

        public static BenchTrack Generate(string venueId, long seed, int difficulty)
        {
            difficulty = Mathf.Clamp(difficulty, 1, 3);
            Venue venue = Venue.Get(venueId);
            for (int attempt = 0; attempt < 400; attempt++)
            {
                var rng = BenchRandom.For(seed, $"track-{venue.Id}-{difficulty}-{attempt}");
                BenchTrack t = Try(venue, ref rng, difficulty);
                if (t == null) continue;
                t.id = $"GEN_{venue.Id}_{seed}_d{difficulty}";
                t.name = $"Generated {venue.Id} #{seed}";
                t.author = "generator";
                t.difficulty = difficulty == 1 ? "easy" : difficulty == 2 ? "medium" : "hard";
                if (!TrackValidator.HasErrors(TrackValidator.Validate(t))) return t;
            }
            throw new InvalidOperationException($"no valid track found for venue {venueId}, seed {seed}, difficulty {difficulty}");
        }

        private static BenchTrack Try(Venue venue, ref BenchRandom rng, int difficulty)
        {
            int count = 5 + difficulty + (int)(rng.NextFloat() * (difficulty + 1));
            float a = (venue.Length * 0.5f - 4f) * (0.6f + 0.35f * rng.NextFloat());
            float b = (venue.Width * 0.5f - 3.2f) * (0.7f + 0.3f * rng.NextFloat());
            float phase = rng.NextFloat() * 360f;
            bool clockwise = rng.NextFloat() < 0.5f;
            var t = new BenchTrack { venue = venue.Id, lighting = "default", rules = new TrackRules { repeat_window = 4, time_limit = 120f } };
            string single = Colours[(int)(rng.NextFloat() * Colours.Length) % Colours.Length];

            for (int i = 0; i < count; i++)
            {
                float step = 360f / count;
                float th = (phase + i * step + (rng.NextFloat() - 0.5f) * step * 0.25f * difficulty) * Mathf.Deg2Rad * (clockwise ? -1f : 1f);
                float radial = 1f - rng.NextFloat() * 0.14f * difficulty;
                var pos = new Vector2(a * radial * Mathf.Cos(th), b * radial * Mathf.Sin(th));
                var tangent = new Vector2(-a * Mathf.Sin(th), b * Mathf.Cos(th)) * (clockwise ? -1f : 1f);
                float yaw = Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg + (rng.NextFloat() - 0.5f) * 24f * (difficulty - 1);

                // In a venue with columns: slide to the nearest spot where the gate fits.
                bool placed = false;
                for (int k = 0; k < 24 && !placed; k++)
                {
                    Vector2 p = pos + (k == 0 ? Vector2.zero : new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f) * (0.5f + k * 0.25f));
                    if (!Fits(venue, p, yaw)) continue;
                    pos = p;
                    placed = true;
                }
                if (!placed) return null;
                t.gates.Add(new TrackGate
                {
                    id = "G" + (i + 1), position = new[] { Round(pos.x), 0f, Round(pos.y) }, yaw = Round(yaw),
                    led = new GateLed { color = difficulty == 3 ? Colours[i % Colours.Length] : single, brightness = 1f, mode = "solid" },
                    direction = "any",
                });
            }

            // The pad: a few metres before the first gate, looking at it.
            TrackGate first = t.gates[0];
            Vector2 c = TrackValidator.Flat(first.position), n = TrackValidator.Forward(first.yaw);
            for (float back = 4.5f; back >= 2.5f; back -= 0.5f)
            {
                Vector2 p = c - n * back;
                if (!venue.Free(p.x, p.y, 0.8f)) continue;
                t.start = new TrackStart { position = new[] { Round(p.x), 0f, Round(p.y) }, yaw = first.yaw };
                return t;
            }
            return null;
        }

        private static bool Fits(Venue venue, Vector2 c, float yaw)
        {
            Vector2 n = TrackValidator.Forward(yaw), side = new Vector2(n.y, -n.x);
            for (float s = -GateGeometry.Outer * 0.5f; s <= GateGeometry.Outer * 0.5f + 1e-3f; s += 0.25f)
            {
                Vector2 p = c + side * s;
                if (!venue.Free(p.x, p.y, TrackValidator.WallClearance)) return false;
            }
            return venue.Free(c.x + n.x * 1.3f, c.y + n.y * 1.3f, 0.4f) && venue.Free(c.x - n.x * 1.3f, c.y - n.y * 1.3f, 0.4f);
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;
    }
}
