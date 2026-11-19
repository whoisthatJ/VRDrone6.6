#if !USHQYR_BENCH_SERVER
using System;
using System.Collections.Generic;
using Ushqyr.Flight;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Bench
{
    public sealed class OracleResult
    {
        public int Points;
        public double ScoreTime, SimTime;
        public int Collisions;
        /// <summary>Simulator time at which every gate of the track had scored once; 0 if it never happened.</summary>
        public double LapTime;
        /// <summary>The flown path, 20 points per second.</summary>
        public readonly List<Vector3> Path = new List<Vector3>();
        public readonly List<Quaternion> Attitude = new List<Quaternion>();
        public bool Lap => LapTime > 0;
    }

    /// <summary>
    /// The oracle pilot: an autopilot that knows the true position of the drone and of every gate and flies the gates in
    /// the order of the track file. It exists to prove a track can be flown within its time limit, to test-fly in the
    /// track editor and to supply camera paths for datasets. It is authoring-only: it is compiled out of the Bench
    /// Server build and has no connection to the API.
    ///
    /// It flies the same flight controller and flight model as everything else (Angle mode through pilot sticks), with
    /// the same collision sweep against the scene.
    /// </summary>
    public static class BenchOracle
    {
        public const float Cruise = 3.2f;

        public static List<RefereeGate> Gates(BenchTrack track)
        {
            var list = new List<RefereeGate>();
            foreach (TrackGate g in track.gates)
            {
                Quaternion rot = Quaternion.Euler(0f, g.yaw, 0f);
                Vector3 centre = BenchTrack.V(g.position) + Vector3.up * GateGeometry.CentreHeight;
                list.Add(RefereeGate.At(g.id, CollisionHook.FromUnity(centre), CollisionHook.FromUnity(rot), g.direction == "front"));
            }
            return list;
        }

        /// <summary>
        /// Flies <paramref name="track"/> for up to <paramref name="seconds"/> of simulator time, or until
        /// <paramref name="laps"/> laps are done. <paramref name="solid"/> is the layer mask of the venue and the gates
        /// (0 = fly without collisions, for tests of the logic alone).
        /// </summary>
        public static OracleResult Fly(BenchTrack track, LayerMask solid, float seconds, int laps = 1)
        {
            QuadProfile profile = QuadProfile.Whoop65;
            PhysicsSettings env = PhysicsSettings.Default;
            env.BatterySag = true;
            var hook = new CollisionHook { Settings = CollisionSettings.Default };
            hook.Settings.Radius = 0.045f;
            hook.Settings.CrashSpeed = 1e6f;
            hook.Settings.TrackGeometry = solid;

            List<RefereeGate> gates = Gates(track);
            int window = track.rules != null ? track.rules.repeat_window : 4;
            var referee = new Referee(gates, window);
            Vector3 start = BenchTrack.V(track.start.position) + Vector3.up * (hook.Settings.Radius + 0.002f);
            QuadState s = QuadState.AtRest(CollisionHook.FromUnity(start), CollisionHook.FromUnity(Quaternion.Euler(0f, track.start.yaw, 0f)));
            hook.PrevPosition = s.Position;
            hook.PrevOrientation = s.Orientation;
            referee.Reset(s.Position);

            var result = new OracleResult();
            float hover = MotorModel.HoverCommand(profile.Motors, profile.Mass * env.Gravity) + 0.04f;
            float maxAngle = profile.AngleMode.MaxAngleDeg * Mathf.Deg2Rad;
            float dt = env.FixedStep;
            int perControl = Mathf.RoundToInt(0.01f / dt);
            long ticks = (long)(seconds / dt);
            int gate = 0, phase = 0, side = 1, lapsDone = 0;
            float heightIntegral = 0f;
            var scoredThisLap = new HashSet<int>();
            int impacts = 0, eventsSeen = 0;
            PilotInput input = default;
            bool planned = false;

            for (long tick = 0; tick < ticks; tick++)
            {
                if (tick % perControl == 0)
                {
                    Vector3 pos = CollisionHook.ToUnity(s.Position), vel = CollisionHook.ToUnity(s.Velocity);
                    double t = tick * (double)dt;
                    if (t < 0.25) input = new PilotInput { Arm = t > 0.1, Mode = FlightMode.Angle };
                    else
                    {
                        RefereeGate g = gates[gate];
                        Vector3 centre = CollisionHook.ToUnity(g.Centre), normal = CollisionHook.ToUnity(g.Normal);
                        if (!planned)
                        {
                            // Through from the side we are on, unless the gate allows one direction only.
                            side = g.FrontOnly || Vector3.Dot(pos - centre, normal) < 0f ? 1 : -1;
                            planned = true;
                            phase = 0;
                        }
                        Vector3 n = normal * side;
                        float along = Vector3.Dot(pos - centre, n);
                        Vector3 before = centre - n * 1.4f, after = centre + n * 1.3f;
                        Vector3 target;
                        float speed = Cruise;
                        if (phase == 0)
                        {
                            target = before;
                            Vector3 to = before - pos;
                            // Slow down for the turn into the gate when arriving from the side.
                            float turn = Vector3.Angle(new Vector3(to.x, 0f, to.z), new Vector3(n.x, 0f, n.z));
                            speed = Mathf.Min(Cruise, 0.9f + to.magnitude * (turn > 50f ? 0.7f : 1.4f));
                            if (to.magnitude < 0.5f || (along > -1.4f && along < 0f && LateralError(pos, centre, n) < 0.45f)) phase = 1;
                        }
                        else
                        {
                            // On the gate's axis: aim at a point ahead on the axis, which pulls the drone onto the line.
                            target = centre + n * Mathf.Max(along + 1.2f, 0.4f);
                            speed = Cruise * 0.85f;
                            if (along > 1.0f)
                            {
                                gate = (gate + 1) % gates.Count;
                                planned = false;
                            }
                        }

                        Vector3 want = target - pos;
                        Vector3 flat = new Vector3(want.x, 0f, want.z);
                        Vector3 vWant = flat.sqrMagnitude > 1e-4f ? flat.normalized * speed : Vector3.zero;
                        Vector3 accel = Vector3.ClampMagnitude((vWant - new Vector3(vel.x, 0f, vel.z)) * 2.6f, 6.5f);

                        Quaternion q = CollisionHook.ToUnity(s.Orientation);
                        float yaw = q.eulerAngles.y;
                        Quaternion level = Quaternion.Euler(0f, yaw, 0f);
                        Vector3 local = Quaternion.Inverse(level) * accel;
                        float pitch = Mathf.Clamp(Mathf.Atan2(local.z, env.Gravity) / maxAngle, -1f, 1f);
                        float roll = Mathf.Clamp(Mathf.Atan2(local.x, env.Gravity) / maxAngle, -1f, 1f);
                        float heading = flat.sqrMagnitude > 0.04f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : yaw;
                        float yawStick = Mathf.Clamp(Mathf.DeltaAngle(yaw, heading) * Mathf.Deg2Rad * 1.3f, -0.85f, 0.85f);

                        float heightError = target.y - pos.y;
                        heightIntegral = Mathf.Clamp(heightIntegral + heightError * 0.01f, -0.3f, 0.3f);
                        float tilt = Mathf.Max(0.75f, Mathf.Cos(pitch * maxAngle) * Mathf.Cos(roll * maxAngle));
                        float throttle = Mathf.Clamp01((hover + heightError * 0.9f - vel.y * 0.4f + heightIntegral * 0.6f) / tilt);
                        input = new PilotInput { Throttle = throttle, Pitch = pitch, Roll = roll, Yaw = yawStick, Arm = true, Mode = FlightMode.Angle };
                    }
                    if (tick % (perControl * 5) == 0)
                    {
                        result.Path.Add(pos);
                        result.Attitude.Add(CollisionHook.ToUnity(s.Orientation));
                    }
                }

                QuadState prev = s;
                QuadSimulator.Step(ref s, input, profile, env, dt);
                hook.AfterStep(ref s, prev, dt);
                hook.Crashed = false;
                referee.Step(prev.Position, s.Position, tick * (double)dt, (tick + 1) * (double)dt);
                if (hook.ImpactCount != impacts) impacts = hook.ImpactCount;

                for (; eventsSeen < referee.Events.Count; eventsSeen++)
                {
                    GateEvent e = referee.Events[eventsSeen];
                    if (e.Kind != GateEventKind.Scored) continue;
                    scoredThisLap.Add(e.GateIndex);
                    if (scoredThisLap.Count < gates.Count) continue;
                    scoredThisLap.Clear();
                    lapsDone++;
                    if (result.LapTime <= 0) result.LapTime = e.Time;
                }
                if (lapsDone >= laps)
                {
                    ticks = tick + 1;
                    break;
                }
            }
            result.Points = referee.Points;
            result.ScoreTime = referee.LastScoreTime;
            result.SimTime = ticks * (double)dt;
            result.Collisions = impacts;
            return result;
        }

        private static float LateralError(Vector3 pos, Vector3 centre, Vector3 n)
        {
            Vector3 d = pos - centre;
            return (d - n * Vector3.Dot(d, n)).magnitude;
        }
    }
}
#endif
