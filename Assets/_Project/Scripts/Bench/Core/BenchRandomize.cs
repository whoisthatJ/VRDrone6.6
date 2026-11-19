using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>How much a run or a dataset varies the scene: "none", "mild" or "strong".</summary>
    public struct RandomizationProfile
    {
        public string Name;
        /// <summary>Largest shift of a gate (m) and largest turn (degrees).</summary>
        public float GateShift, GateTurn;
        public float StartShift, StartTurn;
        /// <summary>Brightness of the venue's lights: between these factors.</summary>
        public float LightMin, LightMax;
        /// <summary>0..1: how far the light's colour may move towards warm or cool.</summary>
        public float LightTint;
        public float LedMin;
        /// <summary>Strength of the analog video effects: between these factors.</summary>
        public float NoiseMin, NoiseMax;
        /// <summary>Other floor and wall materials may be used.</summary>
        public bool Surfaces;
        /// <summary>Coloured boxes and panels placed along the walls.</summary>
        public int Clutter;

        public bool Any => Name != "none";

        public static RandomizationProfile Get(string name)
        {
            switch (name)
            {
                case "mild":
                    return new RandomizationProfile
                    {
                        Name = "mild", GateShift = 0.10f, GateTurn = 3f, StartShift = 0.15f, StartTurn = 5f, LightMin = 0.8f, LightMax = 1.2f,
                        LightTint = 0.25f, LedMin = 0.75f, NoiseMin = 0.8f, NoiseMax = 1.3f,
                    };
                case "strong":
                    return new RandomizationProfile
                    {
                        Name = "strong", GateShift = 0.25f, GateTurn = 8f, StartShift = 0.3f, StartTurn = 10f, LightMin = 0.45f, LightMax = 1.6f,
                        LightTint = 0.7f, LedMin = 0.45f, NoiseMin = 0.5f, NoiseMax = 2.5f, Surfaces = true, Clutter = 8,
                    };
                default:
                    return new RandomizationProfile { Name = "none", LightMin = 1f, LightMax = 1f, LedMin = 1f, NoiseMin = 1f, NoiseMax = 1f };
            }
        }
    }

    /// <summary>One drawn variation of a scene: everything that was randomised, as plain numbers.</summary>
    public sealed class SceneVariation
    {
        public BenchTrack Track;
        public float Light = 1f;
        public Color LightColour = Color.white;
        public float LightYaw;
        public float Noise = 1f;
        public int FloorMaterial, WallMaterial;
        public readonly List<TrackProp> Clutter = new List<TrackProp>();
    }

    /// <summary>
    /// Draws a variation of a track from a seed: gates and start pad nudged, LED brightness, light level and colour,
    /// video noise level, surface materials and clutter along the walls. The same seed gives the same variation for
    /// every team; the track file itself is never changed.
    /// </summary>
    public static class TrackRandomizer
    {
        private static readonly string[] ClutterColours = { "#FF00C8", "#00E5FF", "#FFD400", "#3CFF4E", "#FF5A1F", "#D0D0D0", "#2B5BD7" };

        public static SceneVariation Draw(BenchTrack track, RandomizationProfile p, long seed, int floorMaterials = 1, int wallMaterials = 1)
        {
            var v = new SceneVariation { Track = JsonUtility.FromJson<BenchTrack>(JsonUtility.ToJson(track)) };
            v.Track.Hash = track.Hash;
            v.Track.Json = track.Json;
            if (!p.Any) return v;
            var rng = BenchRandom.For(seed, "randomize-" + track.id);
            Venue venue = Venue.Get(track.venue);
            float Signed() => rng.NextFloat() * 2f - 1f;

            for (int i = 0; i < v.Track.gates.Count; i++)
            {
                TrackGate g = v.Track.gates[i], original = track.gates[i];
                // A nudge that would break the track's checks (a wall, a column) is simply not applied to that gate.
                float dx = Signed() * p.GateShift, dz = Signed() * p.GateShift, turn = Signed() * p.GateTurn;
                float brightness = Mathf.Lerp(p.LedMin, 1f, rng.NextFloat());
                g.position = new[] { original.position[0] + dx, original.position[1], original.position[2] + dz };
                g.yaw = original.yaw + turn;
                g.led.brightness = original.led.brightness * brightness;
                if (GateBroken(v.Track, i, venue))
                {
                    g.position = (float[])original.position.Clone();
                    g.yaw = original.yaw;
                }
            }
            float sx = Signed() * p.StartShift, sz = Signed() * p.StartShift, sturn = Signed() * p.StartTurn;
            float nx = track.start.position[0] + sx, nz = track.start.position[2] + sz;
            if (venue.Free(nx, nz, 0.6f)) v.Track.start.position = new[] { nx, track.start.position[1], nz };
            v.Track.start.yaw = track.start.yaw + sturn;

            v.Light = Mathf.Lerp(p.LightMin, p.LightMax, rng.NextFloat());
            float tint = Signed() * p.LightTint;
            v.LightColour = tint >= 0f ? Color.Lerp(Color.white, new Color(1f, 0.8f, 0.58f), tint) : Color.Lerp(Color.white, new Color(0.72f, 0.85f, 1f), -tint);
            v.LightYaw = rng.NextFloat() * 360f;
            v.Noise = Mathf.Lerp(p.NoiseMin, p.NoiseMax, rng.NextFloat());
            float floor = rng.NextFloat(), wall = rng.NextFloat();
            if (p.Surfaces)
            {
                v.FloorMaterial = Mathf.Min(floorMaterials - 1, (int)(floor * floorMaterials));
                v.WallMaterial = Mathf.Min(wallMaterials - 1, (int)(wall * wallMaterials));
            }

            // Clutter: along the walls, clear of the gates and the pad.
            for (int i = 0; i < p.Clutter; i++)
            {
                bool longWall = rng.NextFloat() < 0.6f;
                float side = rng.NextFloat() < 0.5f ? -1f : 1f, along = Signed(), inset = 0.5f + rng.NextFloat() * 1.3f;
                float x = longWall ? along * (venue.Length * 0.5f - 1.5f) : side * (venue.Length * 0.5f - inset);
                float z = longWall ? side * (venue.Width * 0.5f - inset) : along * (venue.Width * 0.5f - 1.5f);
                float w = 0.4f + rng.NextFloat() * 1.2f, h = 0.3f + rng.NextFloat() * 1.4f, d = 0.3f + rng.NextFloat() * 0.8f;
                string colour = ClutterColours[(int)(rng.NextFloat() * ClutterColours.Length) % ClutterColours.Length];
                bool glow = rng.NextFloat() < 0.25f;
                float yaw = rng.NextFloat() * 360f;
                if (!venue.Free(x, z, 0.9f) || NearTrack(v.Track, x, z, 3f)) continue;
                v.Clutter.Add(new TrackProp
                {
                    id = "C" + i, position = new[] { x, 0f, z }, yaw = yaw, size = new[] { w, Mathf.Min(h, venue.Height - 0.2f), d }, color = colour, glow = glow,
                });
            }
            return v;
        }

        private static bool GateBroken(BenchTrack t, int index, Venue venue)
        {
            TrackGate g = t.gates[index];
            Vector2 c = TrackValidator.Flat(g.position), n = TrackValidator.Forward(g.yaw), side = new Vector2(n.y, -n.x);
            for (float s = -GateGeometry.Outer * 0.5f; s <= GateGeometry.Outer * 0.5f + 1e-3f; s += 0.25f)
            {
                Vector2 p = c + side * s;
                if (!venue.Free(p.x, p.y, TrackValidator.WallClearance)) return true;
            }
            foreach (float along in new[] { -TrackValidator.FlyThrough, TrackValidator.FlyThrough })
                if (!venue.Free(c.x + n.x * along, c.y + n.y * along, 0.3f)) return true;
            return false;
        }

        private static bool NearTrack(BenchTrack t, float x, float z, float distance)
        {
            var p = new Vector2(x, z);
            if (Vector2.Distance(p, TrackValidator.Flat(t.start.position)) < distance) return true;
            foreach (TrackGate g in t.gates)
                if (Vector2.Distance(p, TrackValidator.Flat(g.position)) < distance) return true;
            return false;
        }
    }

    /// <summary>The camera's lens model the other way round: from a point in front of the camera to its pixel.</summary>
    public static class LensProjection
    {
        /// <summary>
        /// Projects a point given in camera axes (x right, y up, z forward, metres) to pixel coordinates (top-left
        /// origin). Returns false for points behind the camera.
        /// </summary>
        public static bool Project(Vector3 p, float focal, float cx, float cy, float k1, out Vector2 pixel)
        {
            pixel = default;
            if (p.z < 0.02f) return false;
            float xu = p.x / p.z, yu = -p.y / p.z;
            float ru = Mathf.Sqrt(xu * xu + yu * yu);
            float scale = 1f;
            if (ru > 1e-6f && k1 > 0f)
            {
                // Solve r_d * (1 + k1 * r_d^2) = r_u for r_d.
                float rd = ru;
                for (int i = 0; i < 25; i++) rd -= (rd * (1f + k1 * rd * rd) - ru) / (1f + 3f * k1 * rd * rd);
                scale = rd / ru;
            }
            pixel = new Vector2(cx + focal * xu * scale, cy + focal * yu * scale);
            return true;
        }
    }
}
