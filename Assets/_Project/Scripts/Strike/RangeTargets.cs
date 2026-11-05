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
        /// <summary>Start distance along the road (m) and the direction (+1 / -1).</summary>
        public float Start;
        public int Direction;
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
        public const float ZigZagAmplitude = 3f, ZigZagPeriod = 5f;

        public static void Pose(RangeLayout layout, in TargetPlan plan, float time, out Vector3 position, out float yaw, out float speed)
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
            speed = plan.Speed;
            float travelled = plan.Class == TargetClass.Evasive ? EvasiveDistance(plan, e, out speed) : plan.Speed * e;
            float d = PingPong(plan.Start + plan.Direction * travelled, road.Length, out int heading);
            road.Sample(d, out position, out Vector3 fwd);
            int dir = plan.Direction * heading * (speed < 0f ? -1 : 1);
            if (dir < 0) fwd = -fwd;
            if (plan.Class == TargetClass.Evasive && Mathf.Abs(speed) > 0.5f)
            {
                // Zig-zag across the road while driving.
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                float phase = 2f * Mathf.PI * e / ZigZagPeriod + (plan.Seed & 0xFF) * 0.0245f;
                position += right * (ZigZagAmplitude * Mathf.Sin(phase));
                fwd = (fwd + right * (ZigZagAmplitude * 2f * Mathf.PI / ZigZagPeriod / Mathf.Max(1f, Mathf.Abs(speed)) * Mathf.Cos(phase))).normalized;
            }
            position.y = layout.Height(position.x, position.z);
            yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            speed = Mathf.Abs(speed);
        }

        /// <summary>Distance driven by an evasive target: segments of driving, stopping and reversing.</summary>
        public static float EvasiveDistance(in TargetPlan plan, float elapsed, out float speedNow)
        {
            float d = 0f;
            speedNow = 0f;
            int segments = Mathf.FloorToInt(elapsed / SegmentSeconds);
            for (int k = 0; k <= segments; k++)
            {
                float v = SegmentSpeed(plan, k);
                float dt = k < segments ? SegmentSeconds : elapsed - segments * SegmentSeconds;
                d += v * dt;
                if (k == segments) speedNow = v;
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

        private readonly RangeLayout _layout;
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
            t.RespawnAt = time + StrikeRandom.Range(StrikeRules.RespawnMin, StrikeRules.RespawnMax, (uint)Seed, (uint)slot, (uint)t.Plan.Generation, 7u);
            return true;
        }

        public void Pose(int slot, float time, out Vector3 position, out float yaw, out float speed) =>
            TargetMotion.Pose(_layout, Targets[slot].Plan, time, out position, out yaw, out speed);

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
            // Moving targets drive in convoys of three (slots in a row share the road); evasive ones alone.
            int movingIndex = slot - FirstOf(TargetClass.Moving);
            int group = cls == TargetClass.Moving ? movingIndex / ConvoySize : slot;
            int member = cls == TargetClass.Moving ? movingIndex % ConvoySize : 0;
            uint g = (uint)group;
            p.Kind = cls == TargetClass.Evasive
                ? (StrikeRandom.Value(s, (uint)slot, (uint)generation, 4u) < 0.5f ? VehicleKind.Wheeled : VehicleKind.Truck)
                : (VehicleKind)StrikeRandom.Index(3, s, g, (uint)generation, 4u);
            p.Place = StrikeRandom.Index(_layout.Roads.Count, s, g, (uint)generation, 5u);
            float length = _layout.Roads.Count > 0 ? _layout.Roads[p.Place].Length : 0f;
            p.Direction = StrikeRandom.Value(s, g, (uint)generation, 6u) < 0.5f ? 1 : -1;
            p.Start = StrikeRandom.Range(0f, length, s, g, (uint)generation, 8u) - p.Direction * member * ConvoyGap;
            // 10-40 km/h; a convoy shares one speed.
            p.Speed = StrikeRandom.Range(10f, 40f, s, g, (uint)generation, 9u) / 3.6f;
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
}
