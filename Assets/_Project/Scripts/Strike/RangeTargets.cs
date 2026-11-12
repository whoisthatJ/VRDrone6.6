using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ushqyr.Strike
{
    /// <summary>
    /// What one target does in one life (P5): its class, look and where it is. Made from (round seed, slot, generation)
    /// only, so every PC builds the same plan (P6: targets are computed locally from the shared seed and clock).
    /// </summary>
    [Serializable]
    public struct TargetPlan
    {
        public int Slot;
        public int Generation;
        public TargetClass Class;
        public VehicleKind Kind;
        /// <summary>Round time this life began (s).</summary>
        public float SpawnTime;
        /// <summary>Static: the spot index. Moving / evasive: the road index.</summary>
        public int Place;
        /// <summary>
        /// Road vehicles: where on the lane loop the group is at round time 0 (convoys) or at spawn (evasive), in metres
        /// along the loop, and which way round it drives (+1 / -1).
        /// </summary>
        public float Start;
        public int Direction;
        /// <summary>The group's own stretch of road (m from the road's start, and its length): no other group drives there.</summary>
        public float SectionStart, SectionLength;
        /// <summary>Place in the convoy (0 = leader).</summary>
        public int Member;
        /// <summary>Cruise speed (m/s): 10-40 km/h.</summary>
        public float Speed;
        /// <summary>Seed for the evasive manoeuvres.</summary>
        public uint Seed;
    }

    /// <summary>Pose of a target at a round time: a pure function of the plan, the layout and the time.</summary>
    public static class TargetMotion
    {
        /// <summary>Length of one evasive manoeuvre segment (s).</summary>
        public const float SegmentSeconds = 3.5f;
        /// <summary>Sideways weave of evasive targets (m, either way) and its period (s).</summary>
        public const float ZigZagAmplitude = 1.4f, ZigZagPeriod = 5f;

        /// <summary>Half the distance between the two lanes (m): lane centres 2.4 m either side of the road's centre.</summary>
        public const float LaneOffset = 2.4f;
        /// <summary>Radius of the turn at each end of a section (m): a keyhole turn, swinging out and back round.</summary>
        public const float TurnRadius = 5f;
        /// <summary>Vehicles take the turns at this share of their speed.</summary>
        public const float TurnSlow = 0.6f;
        /// <summary>Over how long an evasive vehicle changes speed (s), and the length of one weave (m).</summary>
        public const float EvasiveRamp = 1.4f, ZigZagLength = 30f;
        // The keyhole: swing out by TurnSwing, round by pi + 2 TurnSwing, back by TurnSwing.
        private static readonly float TurnSwing = Mathf.Acos((LaneOffset + TurnRadius) / (2f * TurnRadius));
        /// <summary>Path length of one turn (m), and how far it reaches beyond the end of the section (m).</summary>
        public static readonly float TurnLength = TurnRadius * (Mathf.PI + 4f * TurnSwing);
        public static readonly float TurnExtent = 2f * TurnRadius * Mathf.Sin(TurnSwing) + TurnRadius;
        /// <summary>How far a hit vehicle veers off the road, beyond its lane (m), and how long that takes (s).</summary>
        public const float WreckOffset = 6.5f, WreckStep = 4.5f, WreckSeconds = 0.8f;
        /// <summary>Along-the-road steps a wreck may roll on (m) when the spot beside it is taken.</summary>
        public const float WreckShiftStep = 6.5f;
        /// <summary>How deep a wreck that has nowhere to go sinks (m): out of sight under the ground.</summary>
        public const float SinkDepth = 6f;

        /// <summary>
        /// Pose of a road or static target. Road vehicles drive a loop inside their own road section: out on the right
        /// lane, a U-turn, back on the other lane, a U-turn. A convoy's members keep their spacing on that loop, so they
        /// never meet; groups never share a section, so they never meet each other.
        /// </summary>
        public static void Pose(RangeLayout layout, in TargetPlan plan, float time, out Vector3 position, out float yaw, out float speed) =>
            PoseAt(layout, plan, time, 0f, 0f, out position, out yaw, out speed);

        /// <summary>
        /// A disabled target: where it was hit, then (road vehicles) rolling off the road to the spot the director
        /// picked for it (<see cref="RangeTarget.WreckOut"/> metres beyond its lane, <see cref="RangeTarget.WreckShift"/>
        /// metres along the road), clear of the traffic behind it and of other wrecks.
        /// </summary>
        public static void WreckPose(RangeLayout layout, RangeTarget t, float time, out Vector3 position, out float yaw)
        {
            float k = t.Plan.Class == TargetClass.Static ? 0f : Mathf.SmoothStep(0f, 1f, (time - t.DisabledAt) / WreckSeconds);
            if (t.WreckSinks)
            {
                // Nowhere free to roll to: it stops where it was hit and sinks out of sight within the roll time.
                PoseAt(layout, t.Plan, t.DisabledAt, 0f, 0f, out position, out yaw, out _);
                position.y -= SinkDepth * Mathf.Clamp01((time - t.DisabledAt) / (WreckSeconds * 0.5f));
                return;
            }
            PoseAt(layout, t.Plan, t.DisabledAt, t.WreckOut * k, t.WreckShift * k, out position, out yaw, out _);
        }

        /// <summary>Where a wreck is <paramref name="k"/> (0-1) of the way to a candidate spot.</summary>
        public static void WreckPath(RangeLayout layout, in TargetPlan plan, float disabledAt, float outward, float shift, float k, out Vector3 position, out float yaw) =>
            PoseAt(layout, plan, disabledAt, outward * k, shift * k, out position, out yaw, out _);

        /// <summary>A road vehicle's place in its section at <paramref name="time"/>: along the road and across it (m).</summary>
        public static Vector2 SectionPoint(in TargetPlan plan, float time, float outward = 0f, float shift = 0f)
        {
            LoopParam(plan, time, out float u, out _);
            Loop(plan.SectionLength, u, out float along, out float lat, out _, out _, out _);
            Offset(plan.SectionLength, ref along, ref lat, outward, shift);
            return new Vector2(along, lat);
        }

        private static void LoopParam(in TargetPlan plan, float time, out float u, out float speed)
        {
            float e = Mathf.Max(0f, time - plan.SpawnTime);
            if (plan.Class == TargetClass.Evasive)
            {
                u = plan.Start + plan.Direction * EvasiveDistance(plan, e, out speed);
                return;
            }
            // Convoys run on the round clock (not the life's): a respawned member rejoins its convoy in its place.
            speed = plan.Speed;
            u = plan.Start + plan.Direction * (plan.Speed * Mathf.Max(0f, time) - plan.Member * RangeDirector.ConvoyGap);
        }

        // Pushes a loop point out to the side of the road and
        // along the road.
        private static void Offset(float length, ref float along, ref float lat, float outward, float shift)
        {
            if (outward > 0f)
            {
                // Off to the side of the road, on the side it is already on: on the legs that is away from the
                // other lane; in a turn it is away from the turn, not further down the road.
                lat += (lat >= 0f ? 1f : -1f) * outward;
            }
            along += shift;
        }

        private static void PoseAt(RangeLayout layout, in TargetPlan plan, float time, float outward, float shift, out Vector3 position, out float yaw, out float speed)
        {
            float e = Mathf.Max(0f, time - plan.SpawnTime);
            if (plan.Class == TargetClass.Static || layout.Roads.Count == 0)
            {
                var spot = layout.Spots.Length > 0 ? layout.Spots[Mathf.Clamp(plan.Place, 0, layout.Spots.Length - 1)] : default;
                position = spot.Position;
                yaw = spot.Yaw;
                speed = 0f;
                return;
            }
            var road = layout.Roads[Mathf.Clamp(plan.Place, 0, layout.Roads.Count - 1)];
            LoopParam(plan, time, out float u, out speed);
            Loop(plan.SectionLength, u, out float along, out float lat, out float dAlong, out float dLat, out float straight);
            Offset(plan.SectionLength, ref along, ref lat, outward, shift);
            if (plan.Class == TargetClass.Evasive)
            {
                // Weave inside the lane on the straight legs (its section is its own, so it can use the road). The
                // weave follows the distance driven, so it stops and backs up with the vehicle and never jumps.
                float phase = 2f * Mathf.PI * u / ZigZagLength + (plan.Seed & 0xFF) * 0.0245f;
                lat += ZigZagAmplitude * straight * Mathf.Sin(phase);
                dLat += ZigZagAmplitude * straight * 2f * Mathf.PI / ZigZagLength * Mathf.Cos(phase);
            }
            float d = plan.SectionStart + along;
            road.Sample(d, out position, out Vector3 fwd);
            // The road is a polyline: take its direction over a few metres, or the heading would snap at every point.
            road.Sample(d - 3f, out Vector3 behind, out _);
            road.Sample(d + 3f, out Vector3 ahead, out _);
            Vector3 smooth = ahead - behind;
            smooth.y = 0f;
            if (smooth.sqrMagnitude > 1e-4f) fwd = smooth;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            position += right * lat;
            // Facing: the way round the loop the group drives (a reversing evasive target backs up, nose first stays).
            Vector3 face = (fwd * dAlong + right * dLat) * plan.Direction;
            position.y = layout.Height(position.x, position.z);
            yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            speed = Mathf.Abs(speed);
        }

        /// <summary>
        /// A point on the lane loop of a section <paramref name="length"/> long, <paramref name="s"/> metres of driving
        /// round it: right lane out, a keyhole turn, the other lane back, a keyhole turn. Turns are driven at
        /// <see cref="TurnSlow"/> of the speed. Gives the distance along the road, the sideways offset, the heading
        /// (per metre of path) and how far the point is into a straight leg (0 in the turns, easing to 1).
        /// </summary>
        public static void Loop(float length, float s, out float along, out float lat, out float dAlong, out float dLat, out float straight)
        {
            float w = LaneOffset, turn = TurnLength / TurnSlow;
            float period = 2f * length + 2f * turn;
            s %= period;
            if (s < 0f) s += period;
            float Ease(float q) => Mathf.SmoothStep(0f, 1f, Mathf.Min(q, length - q) / 12f);
            if (s < length)
            {
                along = s; lat = w; dAlong = 1f; dLat = 0f; straight = Ease(s);
                return;
            }
            s -= length;
            if (s < turn)
            {
                Keyhole(s * TurnSlow, out float x, out float y, out float hx, out float hy);
                along = length + x; lat = y; dAlong = hx; dLat = hy; straight = 0f;
                return;
            }
            s -= turn;
            if (s < length)
            {
                along = length - s; lat = -w; dAlong = -1f; dLat = 0f; straight = Ease(s);
                return;
            }
            s -= length;
            Keyhole(s * TurnSlow, out float x2, out float y2, out float hx2, out float hy2);
            along = -x2; lat = -y2; dAlong = -hx2; dLat = -hy2; straight = 0f;
        }

        // The turn at the far end, from (0, +lane) heading out to (0, -lane) heading back, a metres along it: swing
        // out to the side, come right round on the turn radius, swing back into the other lane.
        private static void Keyhole(float a, out float x, out float y, out float hx, out float hy)
        {
            float r = TurnRadius, w = LaneOffset, phi = TurnSwing, h;
            float a1 = r * phi, a2 = r * (Mathf.PI + 2f * phi);
            if (a < a1)
            {
                h = a / r;
                x = r * Mathf.Sin(h); y = w + r - r * Mathf.Cos(h);
            }
            else if (a < a1 + a2)
            {
                h = phi - (a - a1) / r;
                x = 2f * r * Mathf.Sin(phi) - r * Mathf.Sin(h); y = r * Mathf.Cos(h);
            }
            else
            {
                h = -Mathf.PI - phi + (a - a1 - a2) / r;
                x = r * Mathf.Sin(h); y = -(w + r) - r * Mathf.Cos(h);
            }
            hx = Mathf.Cos(h); hy = Mathf.Sin(h);
        }

        /// <summary>Driving distance round the lane loop of a section (m; the turns count at their slowed pace).</summary>
        public static float LoopLength(float sectionLength) => 2f * sectionLength + 2f * TurnLength / TurnSlow;

        /// <summary>Distance driven by an evasive target: segments of driving, stopping and reversing.</summary>
        public static float EvasiveDistance(in TargetPlan plan, float elapsed, out float speedNow)
        {
            float d = 0f;
            speedNow = 0f;
            int segments = Mathf.FloorToInt(elapsed / SegmentSeconds);
            float from = 0f; // it pulls away from rest
            for (int k = 0; k <= segments; k++)
            {
                // Each segment eases from the last one's speed to its own: no instant stops or starts.
                float v = SegmentSpeed(plan, k);
                float dt = k < segments ? SegmentSeconds : elapsed - segments * SegmentSeconds;
                float ramp = Mathf.Min(dt, EvasiveRamp);
                d += from * ramp + (v - from) * ramp * ramp / (2f * EvasiveRamp) + v * Mathf.Max(0f, dt - EvasiveRamp);
                if (k == segments) speedNow = Mathf.Lerp(from, v, Mathf.Clamp01(dt / EvasiveRamp));
                from = v;
            }
            return d;
        }

        /// <summary>Speed in evasive segment <paramref name="k"/>: drive (60 %), stop (25 %), reverse (15 %).</summary>
        public static float SegmentSpeed(in TargetPlan plan, int k)
        {
            float r = StrikeRandom.Value(plan.Seed, (uint)k, 17u);
            if (r < 0.6f) return plan.Speed;
            if (r < 0.85f) return 0f;
            return -0.6f * plan.Speed;
        }

        /// <summary>Evasive targets pop smoke at the start of some stopped or reversing segments.</summary>
        public static bool SmokeStartsIn(in TargetPlan plan, int k) =>
            plan.Class == TargetClass.Evasive && SegmentSpeed(plan, k) <= 0f && StrikeRandom.Value(plan.Seed, (uint)k, 29u) < 0.5f;

        /// <summary>Folds a distance into [0, length] going back and forth; <paramref name="heading"/> is +1 or -1.</summary>
        public static float PingPong(float d, float length, out int heading)
        {
            heading = 1;
            if (length <= 0f) return 0f;
            float period = 2f * length;
            float u = d % period;
            if (u < 0f) u += period;
            if (u <= length) return u;
            heading = -1;
            return period - u;
        }
    }

    /// <summary>State of one target slot in a round.</summary>
    public sealed class RangeTarget
    {
        public TargetPlan Plan;
        public bool Active;
        public float DisabledAt = -1f;
        public float RespawnAt = -1f;
        /// <summary>Who disabled it last (pilot index), -1 = nobody.</summary>
        public int HitBy = -1;
        /// <summary>Where its wreck rolls to: metres beyond its lane, and along the road (set by the director on a hit).</summary>
        public float WreckOut, WreckShift;
        /// <summary>No free spot nearby (rare, a crowded corner): the wreck burns out and sinks out of sight instead.</summary>
        public bool WreckSinks;
    }

    /// <summary>
    /// The targets of one round (P5): how many of each class (from the difficulty), their plans per life, hits and
    /// respawns. Deterministic: the same seed, difficulty, layout and the same hits in the same order give the same
    /// targets everywhere.
    /// </summary>
    public sealed class RangeDirector
    {
        /// <summary>Gap between vehicles in a convoy (m).</summary>
        public const float ConvoyGap = 18f;
        public const int ConvoySize = 3;

        /// <summary>Gap kept free between two groups' sections, besides the U-turns (m).</summary>
        public const float SectionMargin = 6f;

        private readonly RangeLayout _layout;
        /// <summary>Per road group (convoys first, then evasive targets): road, section start and length.</summary>
        private readonly List<(int road, float start, float length)> _sections = new List<(int, float, float)>();
        public int Seed { get; }
        public RangeDifficulty Difficulty { get; }
        public readonly List<RangeTarget> Targets = new List<RangeTarget>();

        public RangeDirector(RangeLayout layout, int seed, RangeDifficulty difficulty)
        {
            _layout = layout;
            Seed = seed;
            Difficulty = difficulty;
            var (statics, moving, evasive) = StrikeRules.Mix(difficulty);
            if (layout.Roads.Count == 0)
            {
                statics += moving + evasive;
                moving = evasive = 0;
            }
            AllocateSections((moving + ConvoySize - 1) / ConvoySize + evasive);
            int slot = 0;
            for (int i = 0; i < statics; i++) Targets.Add(new RangeTarget { Plan = MakePlan(slot++, 0, TargetClass.Static, 0f), Active = true });
            for (int i = 0; i < moving; i++) Targets.Add(new RangeTarget { Plan = MakePlan(slot++, 0, TargetClass.Moving, 0f), Active = true });
            for (int i = 0; i < evasive; i++) Targets.Add(new RangeTarget { Plan = MakePlan(slot++, 0, TargetClass.Evasive, 0f), Active = true });
        }

        public RangeLayout Layout => _layout;

        /// <summary>Brings back targets whose respawn time has come (call once per frame with the round time).</summary>
        public void Update(float time)
        {
            foreach (var t in Targets)
                if (!t.Active && t.RespawnAt >= 0f && time >= t.RespawnAt)
                {
                    t.Plan = MakePlan(t.Plan.Slot, t.Plan.Generation + 1, t.Plan.Class, t.RespawnAt);
                    t.Active = true;
                    t.RespawnAt = -1f;
                }
        }

        /// <summary>A hit on <paramref name="slot"/> at <paramref name="time"/>. False if it was already disabled.</summary>
        public bool Hit(int slot, float time, int pilot = -1)
        {
            if (slot < 0 || slot >= Targets.Count) return false;
            var t = Targets[slot];
            if (!t.Active) return false;
            t.Active = false;
            t.DisabledAt = time;
            t.HitBy = pilot;
            ChooseWreckSpot(t);
            t.RespawnAt = time + StrikeRandom.Range(StrikeRules.RespawnMin, StrikeRules.RespawnMax, (uint)Seed, (uint)slot, (uint)t.Plan.Generation, 7u);
            return true;
        }

        public void Pose(int slot, float time, out Vector3 position, out float yaw, out float speed) =>
            TargetMotion.Pose(_layout, Targets[slot].Plan, time, out position, out yaw, out speed);

        /// <summary>Where the target is drawn: its pose while active, its wreck (off the road) while disabled.</summary>
        /// <summary>The slope of the ground under a vehicle at <paramref name="p"/> (heights sampled 2 m either way).</summary>
        public Vector3 GroundNormal(Vector3 p)
        {
            const float e = 2f;
            float dx = _layout.Height(p.x + e, p.z) - _layout.Height(p.x - e, p.z), dz = _layout.Height(p.x, p.z + e) - _layout.Height(p.x, p.z - e);
            return new Vector3(-dx, 2f * e, -dz).normalized;
        }

        public void ViewPose(int slot, float time, out Vector3 position, out float yaw)
        {
            var t = Targets[slot];
            if (t.Active) TargetMotion.Pose(_layout, t.Plan, time, out position, out yaw, out _);
            else TargetMotion.WreckPose(_layout, t, time, out position, out yaw);
        }

        /// <summary>
        /// Picks where a road vehicle's wreck goes: beside its lane if that is free, else further out or a little along
        /// the road; the spot and the way there keep clear of the wrecks already in its section. Deterministic (hits
        /// arrive in the same order on every PC).
        /// </summary>
        private void ChooseWreckSpot(RangeTarget t)
        {
            t.WreckOut = t.WreckShift = 0f;
            t.WreckSinks = false;
            if (t.Plan.Class == TargetClass.Static || _layout.Roads.Count == 0) return;
            // Everything that stands still near the road: other wrecks where they ended up, and parked targets.
            var others = new List<(Vector3 p, float yaw, Vector2 ext)>();
            foreach (var o in Targets)
            {
                if (o == t || o.Active && o.Plan.Class != TargetClass.Static) continue;
                Vector3 op;
                float oy;
                if (o.Active) TargetMotion.Pose(_layout, o.Plan, t.DisabledAt, out op, out oy, out _);
                else TargetMotion.WreckPose(_layout, o, float.MaxValue, out op, out oy);
                others.Add((op, oy, RangeVehicles.Footprint(o.Plan.Kind)));
                // A wreck hit a moment ago is still rolling to its spot: keep clear of the way it takes too.
                if (!o.Active && !o.WreckSinks && o.Plan.Class != TargetClass.Static && t.DisabledAt - o.DisabledAt < TargetMotion.WreckSeconds)
                    for (int i = 0; i < 8; i++)
                    {
                        TargetMotion.WreckPath(_layout, o.Plan, o.DisabledAt, o.WreckOut, o.WreckShift, i / 8f, out op, out oy);
                        others.Add((op, oy, RangeVehicles.Footprint(o.Plan.Kind)));
                    }
            }
            Vector2 ext = RangeVehicles.Footprint(t.Plan.Kind);
            float ahead = Mathf.Sign(TargetMotion.SectionPoint(t.Plan, t.DisabledAt + 0.1f).x - TargetMotion.SectionPoint(t.Plan, t.DisabledAt).x);
            if (ahead == 0f) ahead = 1f;
            // Live traffic in the same section, which the wreck must not touch while it rolls off.
            var live = new List<RangeTarget>();
            foreach (var o in Targets)
                if (o != t && o.Plan.Class != TargetClass.Static && o.Plan.Place == t.Plan.Place && Mathf.Abs(o.Plan.SectionStart - t.Plan.SectionStart) < 0.5f)
                    live.Add(o);
            int bestHits = int.MaxValue;
            for (int level = 0; level < 5; level++)
                foreach (float steps in new[] { 0f, 1f, -1f, 2f, -2f, 3f, -3f }) // no further: a wreck does not dash down the road
                {
                    float outward = TargetMotion.WreckOffset + level * TargetMotion.WreckStep, shift = steps * TargetMotion.WreckShiftStep * ahead;
                    Vector2 to = TargetMotion.SectionPoint(t.Plan, t.DisabledAt, outward, shift);
                    float spare = SectionMargin + TargetMotion.TurnExtent - 4f;
                    if (to.x < -spare || to.x > t.Plan.SectionLength + spare) continue;
                    // Never back onto the band the loop drives on (lanes and U-turns): later traffic would meet it there.
                    bool inTurn = to.x < 0f || to.x > t.Plan.SectionLength;
                    if (Mathf.Abs(to.y) < (inTurn ? TargetMotion.TurnRadius : TargetMotion.LaneOffset) + 4.5f
                        && to.x > -TargetMotion.TurnExtent - 4.5f && to.x < t.Plan.SectionLength + TargetMotion.TurnExtent + 4.5f) continue;
                    // Roads cross: never roll onto another road, where its traffic would drive into the wreck.
                    TargetMotion.WreckPath(_layout, t.Plan, t.DisabledAt, outward, shift, 1f, out Vector3 rest, out _);
                    if (NearOtherRoad(t.Plan.Place, rest, 13f)) continue;
                    int hits = PathHits(t, outward, shift, ext, others, live);
                    if (hits == 0)
                    {
                        t.WreckOut = outward;
                        t.WreckShift = shift;
                        return;
                    }
                    if (hits < bestHits)
                    {
                        bestHits = hits;
                        t.WreckOut = outward;
                        t.WreckShift = shift;
                    }
                }
            t.WreckSinks = true;
        }

        // How often the wreck's roll from its lane to the spot touches what stands still there, or the traffic driving
        // past while it rolls (0 = clear).
        private int PathHits(RangeTarget t, float outward, float shift, Vector2 ext, List<(Vector3 p, float yaw, Vector2 ext)> others, List<RangeTarget> live)
        {
            int hits = 0;
            const int samples = 40; // under a metre apart on the longest roll
            for (int i = 0; i <= samples; i++)
            {
                float f = i / (float)samples;
                TargetMotion.WreckPath(_layout, t.Plan, t.DisabledAt, outward, shift, Mathf.SmoothStep(0f, 1f, f), out var p, out float y);
                foreach (var o in others)
                    if (Footprints.Overlap(p, y, ext, o.p, o.yaw, o.ext, 0.3f)) hits++;
                float when = t.DisabledAt + f * TargetMotion.WreckSeconds;
                foreach (var o in live)
                {
                    if (!o.Active) continue; // not on the road right now (its line is checked below, for when it is back)
                    TargetMotion.Pose(_layout, o.Plan, when, out var lp, out float ly, out _);
                    if (Footprints.Overlap(p, y, ext, lp, ly, RangeVehicles.Footprint(o.Plan.Kind), 0.3f)) hits++;
                }
            }
            // Where it ends up, the traffic keeps passing for as long as the wreck lies there.
            TargetMotion.WreckPath(_layout, t.Plan, t.DisabledAt, outward, shift, 1f, out var end, out float endYaw);
            foreach (var o in live)
                for (float w = TargetMotion.WreckSeconds; w <= StrikeRules.RespawnMax; w += 0.2f)
                {
                    TargetMotion.Pose(_layout, o.Plan, t.DisabledAt + w, out var lp, out float ly, out _);
                    if (Footprints.Overlap(end, endYaw, ext, lp, ly, RangeVehicles.Footprint(o.Plan.Kind), 0.3f)) hits++;
                }
            return hits;
        }

        /// <summary>The road sections, one per group: road index, start (m) and length (m).</summary>
        public IReadOnlyList<(int road, float start, float length)> Sections => _sections;

        // Splits the roads into one section per group, longest share first, so no two groups ever use the same stretch.
        private void AllocateSections(int groups)
        {
            _sections.Clear();
            int roads = _layout.Roads.Count;
            if (roads == 0 || groups == 0) return;
            var count = new int[roads];
            var order = new List<(int road, int index)>();
            for (int g = 0; g < groups; g++)
            {
                int best = 0;
                float bestShare = -1f;
                for (int r = 0; r < roads; r++)
                {
                    float share = _layout.Roads[r].Length / (count[r] + 1);
                    if (share > bestShare + 1e-3f) { bestShare = share; best = r; }
                }
                order.Add((best, count[best]++));
            }
            float margin = TargetMotion.TurnExtent + SectionMargin;
            var crossings = Crossings();
            foreach (var (road, index) in order)
            {
                float slice = _layout.Roads[road].Length / count[road];
                float start = index * slice + margin, length = Mathf.Max(1f, slice - 2f * margin);
                // Roads cross each other: a section (with its turns) stays off every crossing, keeping its longer side.
                float reach = TargetMotion.TurnExtent + CrossingClearance;
                foreach (var (r, at) in crossings)
                {
                    if (r != road || at < start - reach || at > start + length + reach) continue;
                    float before = at - reach - start, after = start + length - (at + reach);
                    if (before >= after) length = Mathf.Max(1f, before);
                    else { start = at + reach; length = Mathf.Max(1f, after); }
                }
                _sections.Add((road, start, length));
            }
        }

        /// <summary>Clear distance kept between a section's loop and a road crossing (m): the other road's width and a vehicle.</summary>
        public const float CrossingClearance = 14f;

        private bool NearOtherRoad(int road, Vector3 p, float clearance)
        {
            for (int r = 0; r < _layout.Roads.Count; r++)
            {
                if (r == road) continue;
                foreach (var q in _layout.Roads[r].Points)
                    if ((q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z) < clearance * clearance) return true;
            }
            return false;
        }

        // Where the roads cross each other: (road, distance along it), for both roads of each crossing.
        private List<(int road, float at)> Crossings()
        {
            var found = new List<(int, float)>();
            for (int i = 0; i < _layout.Roads.Count; i++)
                for (int j = i + 1; j < _layout.Roads.Count; j++)
                {
                    var a = _layout.Roads[i].Points;
                    var b = _layout.Roads[j].Points;
                    float da = 0f;
                    for (int k = 0; k + 1 < a.Length; k++)
                    {
                        Vector2 a0 = new Vector2(a[k].x, a[k].z), a1 = new Vector2(a[k + 1].x, a[k + 1].z);
                        float la = Vector2.Distance(a0, a1), db = 0f;
                        for (int m = 0; m + 1 < b.Length; m++)
                        {
                            Vector2 b0 = new Vector2(b[m].x, b[m].z), b1 = new Vector2(b[m + 1].x, b[m + 1].z);
                            float lb = Vector2.Distance(b0, b1);
                            Vector2 r = a1 - a0, s = b1 - b0;
                            float den = r.x * s.y - r.y * s.x;
                            if (Mathf.Abs(den) > 1e-6f)
                            {
                                Vector2 q = b0 - a0;
                                float t = (q.x * s.y - q.y * s.x) / den, u = (q.x * r.y - q.y * r.x) / den;
                                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
                                {
                                    found.Add((i, da + t * la));
                                    found.Add((j, db + u * lb));
                                }
                            }
                            db += lb;
                        }
                        da += la;
                    }
                }
            return found;
        }

        private TargetPlan MakePlan(int slot, int generation, TargetClass cls, float spawnTime)
        {
            uint s = (uint)Seed;
            var p = new TargetPlan
            {
                Slot = slot, Generation = generation, Class = cls, SpawnTime = spawnTime,
                Seed = StrikeRandom.Hash(s, (uint)slot, (uint)generation, 3u),
            };
            if (cls == TargetClass.Static)
            {
                p.Kind = (VehicleKind)StrikeRandom.Index(5, s, (uint)slot, (uint)generation, 1u);
                p.Place = FreeSpot(StrikeRandom.Index(Math.Max(1, _layout.Spots.Length), s, (uint)slot, (uint)generation, 2u), slot);
                return p;
            }
            // Moving targets drive in convoys of three (slots in a row share a section); evasive ones alone.
            int movingIndex = slot - FirstOf(TargetClass.Moving);
            int convoys = (StrikeRules.Mix(Difficulty).moving + ConvoySize - 1) / ConvoySize;
            int group = cls == TargetClass.Moving ? movingIndex / ConvoySize : convoys + (slot - FirstOf(TargetClass.Evasive));
            p.Member = cls == TargetClass.Moving ? movingIndex % ConvoySize : 0;
            uint g = (uint)group;
            p.Kind = cls == TargetClass.Evasive
                ? (StrikeRandom.Value(s, (uint)slot, (uint)generation, 4u) < 0.5f ? VehicleKind.Wheeled : VehicleKind.Truck)
                : (VehicleKind)StrikeRandom.Index(3, s, (uint)slot, (uint)generation, 4u);
            var sec = group < _sections.Count ? _sections[group] : (0, 0f, _layout.Roads.Count > 0 ? _layout.Roads[0].Length : 0f);
            p.Place = sec.Item1;
            p.SectionStart = sec.Item2;
            p.SectionLength = sec.Item3;
            float loop = TargetMotion.LoopLength(p.SectionLength);
            if (cls == TargetClass.Moving)
            {
                // A convoy keeps its start, way round and speed for the whole round: every life of every member follows it.
                p.Direction = StrikeRandom.Value(s, g, 6u) < 0.5f ? 1 : -1;
                p.Start = StrikeRandom.Range(0f, loop, s, g, 8u);
                p.Speed = StrikeRandom.Range(10f, 40f, s, g, 9u) / 3.6f;
            }
            else
            {
                p.Direction = StrikeRandom.Value(s, (uint)slot, (uint)generation, 6u) < 0.5f ? 1 : -1;
                p.Start = StrikeRandom.Range(0f, loop, s, (uint)slot, (uint)generation, 8u);
                p.Speed = StrikeRandom.Range(10f, 40f, s, (uint)slot, (uint)generation, 9u) / 3.6f;
            }
            return p;
        }

        private int FirstOf(TargetClass cls)
        {
            var (statics, moving, _) = StrikeRules.Mix(Difficulty);
            if (_layout.Roads.Count == 0) return 0;
            return cls == TargetClass.Moving ? statics : statics + moving;
        }

        // The first spot from 'start' that no other active static target is using.
        private int FreeSpot(int start, int slot)
        {
            int n = _layout.Spots.Length;
            if (n == 0) return 0;
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % n;
                bool used = false;
                foreach (var t in Targets)
                    if (t.Plan.Slot != slot && t.Active && t.Plan.Class == TargetClass.Static && t.Plan.Place == i) used = true;
                if (!used) return i;
            }
            return start % n;
        }
    }

    /// <summary>Ground footprints of vehicles: oriented rectangles (centre, yaw, half width / half length).</summary>
    public static class Footprints
    {
        public static bool Overlap(Vector3 pa, float ya, Vector2 ea, Vector3 pb, float yb, Vector2 eb, float margin)
        {
            Vector2 ca = new Vector2(pa.x, pa.z), cb = new Vector2(pb.x, pb.z);
            if ((ca - cb).sqrMagnitude > 144f) return false;
            float ra = ya * Mathf.Deg2Rad, rb = yb * Mathf.Deg2Rad;
            Vector2 fa = new Vector2(Mathf.Sin(ra), Mathf.Cos(ra)), sa = new Vector2(fa.y, -fa.x);
            Vector2 fb = new Vector2(Mathf.Sin(rb), Mathf.Cos(rb)), sb = new Vector2(fb.y, -fb.x);
            ea += Vector2.one * margin;
            eb += Vector2.one * margin;
            Vector2 d = cb - ca;
            if (Separated(d, fa, ea, fa, sa, eb, fb, sb)) return false;
            if (Separated(d, sa, ea, fa, sa, eb, fb, sb)) return false;
            if (Separated(d, fb, ea, fa, sa, eb, fb, sb)) return false;
            if (Separated(d, sb, ea, fa, sa, eb, fb, sb)) return false;
            return true;
        }

        private static bool Separated(Vector2 d, Vector2 axis, Vector2 ea, Vector2 fa, Vector2 sa, Vector2 eb, Vector2 fb, Vector2 sb)
        {
            float projA = ea.x * Mathf.Abs(Vector2.Dot(sa, axis)) + ea.y * Mathf.Abs(Vector2.Dot(fa, axis));
            float projB = eb.x * Mathf.Abs(Vector2.Dot(sb, axis)) + eb.y * Mathf.Abs(Vector2.Dot(fb, axis));
            return Mathf.Abs(Vector2.Dot(d, axis)) > projA + projB;
        }
    }
}
