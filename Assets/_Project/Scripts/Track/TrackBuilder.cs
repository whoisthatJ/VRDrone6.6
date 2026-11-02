using System.Collections.Generic;
using GateRush.Race;
using UnityEngine;
using UnityEngine.Rendering;

namespace GateRush.Track
{
    /// <summary>
    /// Generates a spline track from its <see cref="RouteData"/> (editor and runtime, deterministic):
    /// <list type="number">
    /// <item>lane lights: inner warm-white and outer red LED tubes on posts every 2 m, with clamps and a hanging cable,
    /// combined into one mesh per 20 m segment (+ small box colliders on the posts);</item>
    /// <item>black-and-white chevron boards on the centre line every 3.5 m (in the same segment meshes);</item>
    /// <item>supports and a truss bridge (side chords, cross beams, legs, diagonal braces, centre walkway) wherever the
    /// lane deck is raised, skipping legs that would stand in another part of the route;</item>
    /// <item>obstacles placed by distance along the route and aligned with it;</item>
    /// <item>directional checkpoints in race order (gate 0 = start/finish, see <see cref="TrackCheckpoints"/>);</item>
    /// <item>ground wear marks under obstacles, the start pad and the spawn point.</item>
    /// </list>
    /// The lane deck is the ground, or the flight line minus <see cref="RouteData.FlightHeight"/> where the route climbs.
    /// </summary>
    public static class TrackBuilder
    {
        public const float SegmentLength = 20f;
        public const float PostSpacing = 2f;
        public const float ChevronSpacing = 3.5f;
        public const float LaneHeight = 0.3f;
        public const float TubeRadius = 0.025f;
        public const float SupportSpacing = 4f;
        /// <summary>Deck heights above ground below this stay on the ground (no supports).</summary>
        public const float ElevatedThreshold = 0.25f;
        /// <summary>Kit start grid offset from the spawn (the spawn is slot 2 of four 1.5 m slots).</summary>
        public const float StartSlotOffset = 0.75f;

        private const int SubWarm = 0, SubRed = 1, SubHardware = 2, SubChevron = 3, SubSupport = 4, SubCount = 5;

        private static Material _fallback;

        public static LoadedTrack Build(TrackData data, TrackPrefabs prefabs, Transform parent, int layer)
        {
            var route = new TrackRoute(data.Route);
            var r = data.Route;
            var root = new GameObject("Track: " + data.Name).transform;
            root.SetParent(parent, false);

            var generated = new GameObject("Route").transform;
            generated.SetParent(root, false);
            var kit = prefabs != null ? prefabs.RouteKit : null;
            var materials = new[]
            {
                Or(kit?.LaneWarm), Or(kit?.LaneRed), Or(kit?.Hardware), Or(kit?.Chevron), Or(kit?.Support),
            };

            var laneColliders = NewChild(generated, "Lane Colliders", layer);
            var supportColliders = NewChild(generated, "Support Colliders", layer);
            float startDistance = route.ClosestDistance(data.Spawn.Position);

            for (float s = 0f; s < route.Length - 0.01f; s += SegmentLength)
            {
                float e = Mathf.Min(s + SegmentLength, route.Length);
                var mesh = new TrackMesh(SubCount);
                AddLanes(mesh, route, s, e, laneColliders);
                AddChevrons(mesh, route, s, e, startDistance);
                AddSupports(mesh, route, s, e, supportColliders);
                if (mesh.IsEmpty) continue;
                var seg = NewChild(generated, $"Segment {s:000}-{e:000}m", layer);
                seg.AddComponent<MeshFilter>().sharedMesh = mesh.ToMesh(seg.name);
                var mr = seg.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = ShadowCastingMode.On;
            }

            var gates = BuildObstacles(data, route, prefabs, root, startDistance, out var wear);
            if (!wear.IsEmpty)
            {
                var decals = NewChild(generated, "Wear Marks", layer);
                decals.AddComponent<MeshFilter>().sharedMesh = wear.ToMesh("Wear Marks");
                var mr = decals.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Or(kit?.Wear);
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }

            BuildStartPad(data, root, Or(kit?.StartPad), layer, prefabs);
            if (prefabs != null) TrackLoader.AddProps(data, prefabs, root);

            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(root, false);
            spawn.SetPositionAndRotation(data.Spawn.Position, Quaternion.Euler(0f, data.Spawn.Yaw, 0f));

            return new LoadedTrack { Data = data, Root = root, Gates = gates, Spawn = spawn, Route = route };
        }

        /// <summary>Height of the lane deck under the route at <paramref name="p"/> (ground, or raised where the route climbs).</summary>
        public static float DeckHeight(RouteData r, in RoutePoint p) => Mathf.Max(r.GroundHeight, p.Position.y - r.FlightHeight);

        /// <summary>Lateral offsets of the four lane lines (inner warm ±w/2, outer red ±(w/2 + gap)).</summary>
        public static float[] LineOffsets(RouteData r)
        {
            float w = r.CorridorWidth * 0.5f;
            return new[] { -w, w, -w - r.OuterLineGap, w + r.OuterLineGap };
        }

        // ---------- lanes ----------

        private static void AddLanes(TrackMesh mesh, TrackRoute route, float s, float e, GameObject colliders)
        {
            var r = route.Data;
            var offsets = LineOffsets(r);
            var path = new List<Vector3>();
            var cable = new List<Vector3>();
            for (int line = 0; line < offsets.Length; line++)
            {
                float off = offsets[line];
                path.Clear();
                cable.Clear();
                for (float d = s; ; d += 0.5f)
                {
                    float dd = Mathf.Min(d, e);
                    var p = route.Sample(dd);
                    Vector3 basePos = Deck(r, p) + p.Right * off;
                    path.Add(basePos + Vector3.up * LaneHeight);
                    // The cable hangs between posts: highest at a post, lowest half-way.
                    float phase = Mathf.Repeat(dd, PostSpacing) / PostSpacing;
                    cable.Add(basePos + Vector3.up * (0.16f - 0.1f * Mathf.Sin(phase * Mathf.PI)) + p.Right * (off > 0f ? 0.03f : -0.03f));
                    if (dd >= e) break;
                }
                mesh.AddTube(line < 2 ? SubWarm : SubRed, path, TubeRadius, 6);
                mesh.AddTube(SubHardware, cable, 0.006f, 4);

                for (float d = Mathf.Ceil(s / PostSpacing) * PostSpacing; d < e; d += PostSpacing)
                {
                    var p = route.Sample(d);
                    Vector3 basePos = Deck(r, p) + p.Right * off;
                    var rot = Quaternion.LookRotation(p.FlatForward);
                    mesh.AddBox(SubHardware, basePos + Vector3.up * (LaneHeight * 0.5f), rot, new Vector3(0.035f, LaneHeight, 0.035f));
                    mesh.AddBox(SubHardware, basePos + Vector3.up * LaneHeight, rot, new Vector3(0.07f, 0.055f, 0.05f));
                    mesh.AddBox(SubHardware, basePos + Vector3.up * 0.01f, rot, new Vector3(0.12f, 0.02f, 0.12f));
                    var box = colliders.AddComponent<BoxCollider>();
                    box.center = basePos + Vector3.up * (LaneHeight * 0.5f);
                    box.size = new Vector3(0.06f, LaneHeight, 0.06f);
                }
            }
        }

        // ---------- chevrons ----------

        private static void AddChevrons(TrackMesh mesh, TrackRoute route, float s, float e, float startDistance)
        {
            var r = route.Data;
            for (float d = Mathf.Ceil(s / ChevronSpacing) * ChevronSpacing; d < e; d += ChevronSpacing)
            {
                if (route.AlongGap(d, startDistance) < 3f) continue; // keep the start pad clear
                var p = route.Sample(d);
                // Boards follow the slope and point in the flying direction.
                var rot = Quaternion.LookRotation(p.Forward, p.Up);
                mesh.AddQuad(SubChevron, Deck(r, p) + p.Up * 0.015f, rot, new Vector2(0.9f, 0.7f));
            }
        }

        // ---------- supports ----------

        private static void AddSupports(TrackMesh mesh, TrackRoute route, float s, float e, GameObject colliders)
        {
            var r = route.Data;
            float half = r.CorridorWidth * 0.5f + r.OuterLineGap * 0.5f;
            const float chord = 0.08f, member = 0.05f;

            // Side chords, cross beams and the centre walkway follow the deck in 1 m steps while it is raised.
            for (float d = s; d < e - 0.01f; d += 1f)
            {
                float d2 = Mathf.Min(d + 1f, e);
                var a = route.Sample(d);
                var b = route.Sample(d2);
                if (!Elevated(r, a) && !Elevated(r, b)) continue;
                Vector3 da = Deck(r, a), db = Deck(r, b);
                foreach (float side in new[] { -half, half })
                    mesh.AddBeam(SubSupport, da + a.Right * side - Vector3.up * 0.06f, db + b.Right * side - Vector3.up * 0.06f, chord);
                mesh.AddBox(SubSupport, (da + db) * 0.5f - Vector3.up * 0.03f, Quaternion.LookRotation(db - da), new Vector3(0.9f, 0.03f, Vector3.Distance(da, db) + 0.02f));
                if (Mathf.Repeat(d, 2f) < 0.5f)
                    mesh.AddBeam(SubSupport, da + a.Right * -half - Vector3.up * 0.1f, da + a.Right * half - Vector3.up * 0.1f, member);
            }

            // Legs + diagonal braces (a zig-zag truss on each side) every SupportSpacing metres.
            Vector3[] prevTop = null, prevBottom = null;
            for (float d = Mathf.Ceil(s / SupportSpacing) * SupportSpacing; d <= e; d += SupportSpacing)
            {
                var p = route.Sample(d);
                if (!Elevated(r, p))
                {
                    prevTop = prevBottom = null;
                    continue;
                }
                Vector3 deck = Deck(r, p);
                var tops = new Vector3[2];
                var bottoms = new Vector3[2];
                bool[] ok = new bool[2];
                for (int k = 0; k < 2; k++)
                {
                    float side = k == 0 ? -half : half;
                    tops[k] = deck + p.Right * side - Vector3.up * 0.06f;
                    bottoms[k] = new Vector3(tops[k].x, r.GroundHeight, tops[k].z);
                    ok[k] = !InAnotherCorridor(route, bottoms[k], p.Distance, deck.y);
                    if (!ok[k]) continue;
                    mesh.AddBeam(SubSupport, bottoms[k], tops[k], 0.1f);
                    mesh.AddBox(SubSupport, bottoms[k] + Vector3.up * 0.01f, Quaternion.LookRotation(p.FlatForward), new Vector3(0.4f, 0.02f, 0.4f));
                    var box = colliders.AddComponent<BoxCollider>();
                    box.center = (bottoms[k] + tops[k]) * 0.5f;
                    box.size = new Vector3(0.12f, tops[k].y - bottoms[k].y, 0.12f);
                    if (prevTop != null && prevBottom != null && prevBottom[k] != Vector3.zero)
                        mesh.AddBeam(SubSupport, prevBottom[k] + Vector3.up * 0.25f, tops[k] - Vector3.up * 0.1f, member);
                }
                prevTop = tops;
                prevBottom = new[] { ok[0] ? bottoms[0] : Vector3.zero, ok[1] ? bottoms[1] : Vector3.zero };
            }
        }

        /// <summary>True if a support leg at <paramref name="ground"/> would stand in a lower part of the route (a crossing).</summary>
        private static bool InAnotherCorridor(TrackRoute route, Vector3 ground, float distance, float deckY)
        {
            var r = route.Data;
            float clearance = r.CorridorWidth * 0.5f + r.OuterLineGap + 0.6f;
            route.ClosestDistance(ground, distance, 15f, horizontal: true);
            if (route.LastClosestSqr > clearance * clearance) return false;
            return true;
        }

        private static bool Elevated(RouteData r, in RoutePoint p) => DeckHeight(r, p) - r.GroundHeight > ElevatedThreshold;

        private static Vector3 Deck(RouteData r, in RoutePoint p) => new Vector3(p.Position.x, DeckHeight(r, p), p.Position.z);

        // ---------- obstacles + checkpoints ----------

        private static Gate[] BuildObstacles(TrackData data, TrackRoute route, TrackPrefabs prefabs, Transform root, float startDistance, out TrackMesh wear)
        {
            var r = data.Route;
            wear = new TrackMesh(1);
            int n = r.Obstacles.Count;
            // One entry per checkpoint gate; an obstacle can have several (e.g. a slalom), each at its own distance.
            var entries = new List<(int obstacle, Gate gate, float distance)>();
            var objects = new GameObject[n];
            int finishEntry = -1;

            for (int i = 0; i < n; i++)
            {
                var o = r.Obstacles[i];
                var p = route.Sample(o.Distance);
                Vector3 pos = Deck(r, p) + p.Right * o.Lateral + Vector3.up * o.Height;
                var rot = Quaternion.LookRotation(p.FlatForward) * Quaternion.Euler(0f, o.Yaw, 0f);

                GameObject prefab = prefabs != null ? prefabs.Gate(o.Type) ?? prefabs.Prop(o.Type) : null;
                if (prefab == null && prefabs != null)
                {
                    Debug.LogWarning($"[GateRush] Track '{data.Id}': unknown obstacle type '{o.Type}', using Square");
                    prefab = prefabs.Gate("Square");
                }
                var go = prefab != null ? Object.Instantiate(prefab, pos, rot, root) : new GameObject();
                if (prefab == null) go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = Vector3.one * o.Scale;
                objects[i] = go;
                go.name = $"Obstacle {i} ({o.Type})";

                var gates = go.GetComponentsInChildren<Gate>(true);
                if (!o.IsCheckpoint)
                {
                    foreach (var gate in gates)
                    {
                        if (Application.isPlaying) Object.Destroy(gate);
                        else Object.DestroyImmediate(gate);
                    }
                }
                else
                {
                    if (gates.Length == 0) gates = new[] { go.AddComponent<Gate>() };
                    var tinted = TryParseColor(o.Color, out var tint);
                    int first = entries.Count;
                    foreach (var gate in gates)
                    {
                        if (tinted) gate.IdleColor = tint;
                        float along = Vector3.Dot(gate.OpeningCenterWorld - pos, p.FlatForward);
                        entries.Add((i, gate, p.Distance + along));
                    }
                    // The finish is the obstacle's first opening along the route.
                    if (i == r.FinishObstacle)
                    {
                        finishEntry = first;
                        for (int k = first + 1; k < entries.Count; k++)
                            if (entries[k].distance < entries[finishEntry].distance) finishEntry = k;
                    }
                }

                // A worn patch under it, turned a little differently each time (seeded by the index).
                float yaw = Hash(i) * 40f - 20f;
                wear.AddQuad(0, new Vector3(pos.x, Deck(r, p).y + 0.006f, pos.z), rot * Quaternion.Euler(0f, yaw, 0f), new Vector2(3.4f * o.Scale, 2.6f * o.Scale));
            }

            var distances = new float[entries.Count];
            var isCheckpoint = new bool[entries.Count];
            for (int k = 0; k < entries.Count; k++)
            {
                distances[k] = route.Wrap(entries[k].distance);
                isCheckpoint[k] = true;
            }
            var order = TrackCheckpoints.Order(distances, isCheckpoint, finishEntry, startDistance, route.Length);
            var result = new Gate[order.Length];
            for (int k = 0; k < order.Length; k++)
            {
                var e = entries[order[k]];
                result[k] = e.gate;
                e.gate.Order = k;
                // "Gate {k} (type)" on whatever object carries the gate (tools sort gates by this name).
                e.gate.gameObject.name = $"Gate {k} ({r.Obstacles[e.obstacle].Type})";
            }
            return result;
        }

        private static void BuildStartPad(TrackData data, Transform root, Material material, int layer, TrackPrefabs prefabs)
        {
            var kitPad = prefabs != null ? prefabs.Prop("StartPad") : null;
            if (kitPad != null)
            {
                // The kit's start grid: four numbered slots; the spawn sits in slot 2.
                var yawRot = Quaternion.Euler(0f, data.Spawn.Yaw, 0f);
                Vector3 ground = data.Spawn.Position - Vector3.up * 0.15f;
                var grid = Object.Instantiate(kitPad, ground + yawRot * new Vector3(StartSlotOffset, 0f, 0f), yawRot, root);
                grid.name = "Start Pad";
                return;
            }
            var pad = NewChild(root, "Start Pad", layer);
            var rot = Quaternion.Euler(0f, data.Spawn.Yaw, 0f);
            // The spawn point is 0.15 m above the pad surface.
            Vector3 top = data.Spawn.Position - Vector3.up * 0.15f;
            pad.transform.SetPositionAndRotation(top - Vector3.up * 0.02f, rot);
            var mesh = new TrackMesh(1);
            mesh.AddBox(0, Vector3.zero, Quaternion.identity, new Vector3(2.4f, 0.04f, 2.4f));
            pad.AddComponent<MeshFilter>().sharedMesh = mesh.ToMesh("Start Pad");
            pad.AddComponent<MeshRenderer>().sharedMaterial = material;
            var box = pad.AddComponent<BoxCollider>();
            box.size = new Vector3(2.4f, 0.04f, 2.4f);
        }

        // ---------- helpers ----------

        private static GameObject NewChild(Transform parent, string name, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Material Or(Material m)
        {
            if (m != null) return m;
            if (_fallback == null) _fallback = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Route Fallback" };
            return _fallback;
        }

        /// <summary>0..1 from an integer, stable across runs and platforms.</summary>
        private static float Hash(int i)
        {
            uint h = (uint)i * 2654435761u;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        public static bool TryParseColor(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "red": color = new Color(0.85f, 0.12f, 0.1f); return true;
                case "green": color = new Color(0.15f, 0.75f, 0.25f); return true;
                case "blue": color = new Color(0.15f, 0.35f, 0.9f); return true;
                case "orange": color = new Color(1f, 0.5f, 0.1f); return true;
                case "white": color = Color.white; return true;
            }
            return ColorUtility.TryParseHtmlString(value, out color);
        }
    }
}
