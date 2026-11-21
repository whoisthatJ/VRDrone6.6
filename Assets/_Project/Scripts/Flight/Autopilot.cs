using System;
using System.Collections.Generic;

namespace Ushqyr.Flight
{
    /// <summary>What the autopilot is doing. Manual means a person flies (with or without assistance).</summary>
    public enum GuidedMode
    {
        Manual = 0,
        Takeoff = 1,
        Hold = 2,
        GoTo = 3,
        Velocity = 4,
        Orbit = 5,
        Mission = 6,
        Return = 7,
        Land = 8,
    }

    public enum WaypointAction
    {
        None = 0,
        Photo = 1,
        Drop = 2,
        Land = 3,
    }

    [Serializable]
    public struct Waypoint
    {
        public Vec3 Position;
        /// <summary>Speed towards this point (m/s); 0 = the autopilot's cruise speed.</summary>
        public float Speed;
        /// <summary>How long to stay at the point (s).</summary>
        public float HoldSeconds;
        /// <summary>Heading on the way (degrees, 0 = +Z, clockwise from above); NaN = face the direction of travel.</summary>
        public float HeadingDeg;
        public WaypointAction Action;
    }

    /// <summary>Why the autopilot took over or changed its plan, for the pilot's screen and the attempt record.</summary>
    public enum FailsafeReason
    {
        None = 0,
        RadioLost = 1,
        BatteryLow = 2,
        BatteryCritical = 3,
        Geofence = 4,
        PositionLost = 5,
    }

    /// <summary>What the autopilot is told about the drone's health each step.</summary>
    public struct AutopilotSensors
    {
        /// <summary>Height of the ground under the drone (world Y).</summary>
        public float GroundY;
        /// <summary>Battery left, 0..1; a negative value means "unknown" and turns the battery failsafe off.</summary>
        public float Battery;
        public bool RadioOk;
        /// <summary>False when positioning (GPS outdoors, optical flow indoors) is lost.</summary>
        public bool PositionOk;

        public static AutopilotSensors Healthy(float groundY) => new AutopilotSensors { GroundY = groundY, Battery = -1f, RadioOk = true, PositionOk = true };
    }

    /// <summary>
    /// The built-in autopilot (EDU_PLAN F3): take off, land, hold, go to a point, fly a velocity, orbit, mission, return
    /// to launch, and the failsafes. It only ever produces stick commands for the flight controller through
    /// <see cref="FlightAssist"/>, so missions and students' programs fly the same physics as a person.
    /// </summary>
    public sealed class Autopilot
    {
        public AssistSettings Settings = AssistSettings.Default;
        /// <summary>Speed between waypoints when a waypoint names none (m/s).</summary>
        public float CruiseSpeed = 5f;
        public float ReturnHeight = 15f;
        /// <summary>Descent rate close to the ground (m/s).</summary>
        public float LandSpeed = 0.6f;
        /// <summary>A waypoint counts as reached inside this distance (m).</summary>
        public float AcceptRadius = 0.6f;
        /// <summary>A velocity command older than this is dropped and the drone holds (s): a crashed program must not fly on.</summary>
        public float VelocityTimeout = 1f;
        public float RadioLossSeconds = 1.5f;
        public float BatteryWarn = 0.25f, BatteryReturn = 0.15f, BatteryLand = 0.08f;
        /// <summary>Geofence around home: 0 = no limit.</summary>
        public float FenceRadius, FenceHeight;

        public GuidedMode Mode { get; private set; }
        public FailsafeReason Failsafe { get; private set; }
        public bool BatteryWarning { get; private set; }
        public Vec3 Home { get; private set; }
        public bool HasHome { get; private set; }
        public int MissionIndex { get; private set; }
        /// <summary>True from touchdown after an automatic landing until the next take-off.</summary>
        public bool Landed { get; private set; }

        private AssistState _assist;
        private Vec3 _target, _velocity, _orbitCentre;
        private float _heading = float.NaN, _speed, _yawRate, _velocityAge, _orbitRadius, _orbitSpeed, _orbitAngle;
        private float _holdLeft, _groundTime, _stuckTime, _radioLost;
        private int _returnStage;
        private bool _actionDone, _landSpotSet, _stayDisarmed;
        private Vec3 _landSpot;
        private readonly List<Waypoint> _mission = new List<Waypoint>();
        private readonly Queue<KeyValuePair<int, WaypointAction>> _actions = new Queue<KeyValuePair<int, WaypointAction>>();

        public IReadOnlyList<Waypoint> Mission => _mission;

        /// <summary>The next waypoint action that became due (photo, drop), with its waypoint number; false when none waits.</summary>
        public bool TryTakeAction(out int waypoint, out WaypointAction action)
        {
            if (_actions.Count == 0)
            {
                waypoint = -1;
                action = WaypointAction.None;
                return false;
            }
            KeyValuePair<int, WaypointAction> a = _actions.Dequeue();
            waypoint = a.Key;
            action = a.Value;
            return true;
        }

        /// <summary>Gives control back to the pilot.</summary>
        public void Manual()
        {
            Mode = GuidedMode.Manual;
            Failsafe = FailsafeReason.None;
        }

        public void Takeoff(in QuadState s, float height, float groundY)
        {
            if (!HasHome || Landed || s.Position.y - groundY < 0.3f)
            {
                Home = new Vec3(s.Position.x, groundY, s.Position.z);
                HasHome = true;
            }
            Landed = false;
            _stayDisarmed = false;
            _target = new Vec3(s.Position.x, groundY + MathF.Max(height, 0.5f), s.Position.z);
            _heading = float.NaN;
            _speed = CruiseSpeed;
            Mode = GuidedMode.Takeoff;
        }

        public void Hold(in QuadState s)
        {
            _target = s.Position + StoppingOffset(s);
            _heading = float.NaN;
            _speed = CruiseSpeed;
            Mode = GuidedMode.Hold;
        }

        public void GoTo(Vec3 position, float headingDeg = float.NaN, float speed = 0f)
        {
            _target = position;
            _heading = headingDeg;
            _speed = speed > 0f ? speed : CruiseSpeed;
            Mode = GuidedMode.GoTo;
        }

        /// <summary>Fly this velocity (world axes) and yaw rate until the next command; it must be repeated at least once a second.</summary>
        public void SetVelocity(Vec3 velocity, float yawRateDeg = 0f)
        {
            _velocity = velocity;
            _yawRate = yawRateDeg;
            _velocityAge = 0f;
            Mode = GuidedMode.Velocity;
        }

        /// <summary>Circle a point at the drone's present height, nose towards the centre. A negative speed circles the other way.</summary>
        public void Orbit(in QuadState s, Vec3 centre, float radius, float speed)
        {
            _orbitCentre = new Vec3(centre.x, s.Position.y, centre.z);
            _orbitRadius = MathF.Max(radius, 1f);
            _orbitSpeed = speed == 0f ? 2f : speed;
            Vec3 d = s.Position - _orbitCentre;
            _orbitAngle = MathF.Atan2(d.x, d.z);
            Mode = GuidedMode.Orbit;
        }

        public bool StartMission(IList<Waypoint> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0) return false;
            _mission.Clear();
            _mission.AddRange(waypoints);
            MissionIndex = 0;
            _holdLeft = -1f;
            _actionDone = false;
            Mode = GuidedMode.Mission;
            return true;
        }

        public void Return()
        {
            _returnStage = 0;
            Mode = GuidedMode.Return;
        }

        public void Land()
        {
            _groundTime = 0f;
            _stuckTime = 0f;
            _landSpotSet = false; // it comes down where it is when the landing starts
            Mode = GuidedMode.Land;
        }

        /// <summary>
        /// One step. <paramref name="sticks"/> and <paramref name="pilotMode"/> are used while a person flies; in every
        /// guided mode the sticks are ignored except that the pilot's disarm is always obeyed.
        /// </summary>
        public PilotInput Update(in QuadState s, in PilotInput sticks, AssistMode pilotMode, in AutopilotSensors sensors,
            in QuadProfile p, in PhysicsSettings env, float dt)
        {
            if (!HasHome && sticks.Arm)
            {
                Home = new Vec3(s.Position.x, sensors.GroundY, s.Position.z);
                HasHome = true;
            }
            if (!sticks.Arm)
            {
                // Disarmed by the pilot: nothing overrides that.
                if (Mode != GuidedMode.Manual) Mode = GuidedMode.Manual;
                Failsafe = FailsafeReason.None;
                _assist = default;
                _radioLost = 0f;
                _stayDisarmed = false;
                return sticks;
            }
            if (_stayDisarmed)
            {
                // It landed by itself with the pilot's arm switch still on. The motors stay off until the pilot
                // switches off and on again, or a new take-off is commanded.
                PilotInput off = sticks;
                off.Arm = false;
                return off;
            }
            CheckFailsafes(s, sensors, dt);

            if (!sensors.PositionOk && Mode != GuidedMode.Land)
            {
                // No position: nothing that needs one can run. Keep the height and drift with the wind until it returns.
                Failsafe = FailsafeReason.PositionLost;
                var level = new PilotInput { Arm = true, Throttle = Mode == GuidedMode.Manual ? sticks.Throttle : 0.5f, Mode = FlightMode.Angle };
                if (Mode == GuidedMode.Manual) { level.Pitch = sticks.Pitch; level.Roll = sticks.Roll; level.Yaw = sticks.Yaw; }
                return FlightAssist.Pilot(ref _assist, s, level, AssistMode.AltitudeHold, p, Settings, env, dt);
            }
            if (Failsafe == FailsafeReason.PositionLost) Failsafe = FailsafeReason.None;

            switch (Mode)
            {
                case GuidedMode.Manual:
                    return FlightAssist.Pilot(ref _assist, s, sticks, pilotMode, p, Settings, env, dt);

                case GuidedMode.Takeoff:
                    if ((s.Position - _target).Magnitude < AcceptRadius && MathF.Abs(s.Velocity.y) < 0.5f) Mode = GuidedMode.Hold;
                    return Go(s, _target, float.NaN, _speed, p, env, dt);

                case GuidedMode.Hold:
                case GuidedMode.GoTo:
                    return Go(s, _target, _heading, _speed, p, env, dt);

                case GuidedMode.Velocity:
                    _velocityAge += dt;
                    if (_velocityAge > VelocityTimeout)
                    {
                        Hold(s);
                        return Go(s, _target, float.NaN, _speed, p, env, dt);
                    }
                    return FlightAssist.Velocity(ref _assist, s, Fenced(s, _velocity), _yawRate, p, Settings, env, dt);

                case GuidedMode.Orbit:
                {
                    _orbitAngle += _orbitSpeed / _orbitRadius * dt;
                    Vec3 on = _orbitCentre + new Vec3(MathF.Sin(_orbitAngle), 0f, MathF.Cos(_orbitAngle)) * _orbitRadius;
                    Vec3 toCentre = _orbitCentre - s.Position;
                    float heading = MathF.Atan2(toCentre.x, toCentre.z) * 180f / MathF.PI;
                    // The point runs ahead along the circle; the drone follows it with the speed of the circle fed forward.
                    Vec3 tangent = new Vec3(MathF.Cos(_orbitAngle), 0f, -MathF.Sin(_orbitAngle)) * _orbitSpeed;
                    Vec3 e = on - s.Position;
                    Vec3 v = tangent + new Vec3(e.x, e.y, e.z) * Settings.PositionGain;
                    float err = (heading - FlightAssist.HeadingDeg(s)) % 360f;
                    if (err > 180f) err -= 360f;
                    if (err < -180f) err += 360f;
                    return FlightAssist.Velocity(ref _assist, s, v, err * Settings.HeadingGain, p, Settings, env, dt);
                }

                case GuidedMode.Mission:
                    return FlyMission(s, p, env, dt);

                case GuidedMode.Return:
                    return FlyReturn(s, sensors, p, env, dt);

                default:
                    return FlyLand(s, sensors, p, env, dt);
            }
        }

        private void CheckFailsafes(in QuadState s, in AutopilotSensors sensors, float dt)
        {
            bool flying = s.Position.y - sensors.GroundY > 0.3f;
            _radioLost = sensors.RadioOk ? 0f : _radioLost + dt;
            BatteryWarning = sensors.Battery >= 0f && sensors.Battery < BatteryWarn;
            if (!flying || Mode == GuidedMode.Land) return;

            if (sensors.Battery >= 0f && sensors.Battery < BatteryLand)
            {
                Failsafe = FailsafeReason.BatteryCritical;
                Land();
                return;
            }
            if (Mode == GuidedMode.Return) return;
            if (sensors.Battery >= 0f && sensors.Battery < BatteryReturn)
            {
                Failsafe = FailsafeReason.BatteryLow;
                Return();
            }
            else if (_radioLost > RadioLossSeconds)
            {
                Failsafe = FailsafeReason.RadioLost;
                Return();
            }
            else if (HasHome && Mode == GuidedMode.Manual && OutsideFence(s.Position + s.Velocity * 0.9f))
            {
                // The check looks 0.9 s ahead, about the time the drone needs to stop, so it stops near the edge and not past it.
                // A person flew through the fence: stop at the edge and wait. The pilot gets the sticks back by flying inwards.
                Failsafe = FailsafeReason.Geofence;
                Hold(s);
                _target = Inside(_target);
            }
        }

        private bool OutsideFence(Vec3 at)
        {
            if (FenceHeight > 0f && at.y - Home.y > FenceHeight) return true;
            if (FenceRadius <= 0f) return false;
            float dx = at.x - Home.x, dz = at.z - Home.z;
            return dx * dx + dz * dz > FenceRadius * FenceRadius;
        }

        /// <summary>The nearest point inside the fence (half a metre in from the edge).</summary>
        private Vec3 Inside(Vec3 at)
        {
            if (!HasHome) return at;
            if (FenceHeight > 0f && at.y - Home.y > FenceHeight - 0.5f) at.y = Home.y + FenceHeight - 0.5f;
            if (FenceRadius > 0f)
            {
                float dx = at.x - Home.x, dz = at.z - Home.z, d = MathF.Sqrt(dx * dx + dz * dz), max = MathF.Max(FenceRadius - 0.5f, 0f);
                if (d > max && d > 0f)
                {
                    at.x = Home.x + dx / d * max;
                    at.z = Home.z + dz / d * max;
                }
            }
            return at;
        }

        /// <summary>A velocity command may not carry the drone out through the fence.</summary>
        private Vec3 Fenced(in QuadState s, Vec3 velocity)
        {
            if (!HasHome || !OutsideFence(s.Position + velocity * 1f)) return velocity;
            Vec3 back = Inside(s.Position) - s.Position;
            return back * Settings.PositionGain;
        }

        private PilotInput Go(in QuadState s, Vec3 target, float heading, float speed, in QuadProfile p, in PhysicsSettings env, float dt) =>
            FlightAssist.GoTo(ref _assist, s, Inside(target), heading, speed, p, Settings, env, dt);

        private static Vec3 StoppingOffset(in QuadState s) => new Vec3(s.Velocity.x, 0f, s.Velocity.z) * 0.4f;

        private PilotInput FlyMission(in QuadState s, in QuadProfile p, in PhysicsSettings env, float dt)
        {
            if (MissionIndex >= _mission.Count)
            {
                Hold(s);
                return Go(s, _target, float.NaN, _speed, p, env, dt);
            }
            Waypoint w = _mission[MissionIndex];
            Vec3 target = Inside(w.Position);
            float heading = w.HeadingDeg;
            Vec3 to = target - s.Position;
            float flat = MathF.Sqrt(to.x * to.x + to.z * to.z);
            if (float.IsNaN(heading) && flat > 2f) heading = MathF.Atan2(to.x, to.z) * 180f / MathF.PI;
            if (to.Magnitude < AcceptRadius)
            {
                if (_holdLeft < 0f) _holdLeft = w.HoldSeconds;
                if (!_actionDone && w.Action != WaypointAction.None && w.Action != WaypointAction.Land && s.Velocity.Magnitude < 0.8f)
                {
                    _actions.Enqueue(new KeyValuePair<int, WaypointAction>(MissionIndex, w.Action));
                    _actionDone = true;
                }
                _holdLeft -= dt;
                bool actionPending = w.Action == WaypointAction.Photo || w.Action == WaypointAction.Drop ? !_actionDone : false;
                if (_holdLeft <= 0f && !actionPending)
                {
                    if (w.Action == WaypointAction.Land)
                    {
                        MissionIndex++;
                        Land();
                        return Go(s, target, heading, CruiseSpeed, p, env, dt);
                    }
                    MissionIndex++;
                    _holdLeft = -1f;
                    _actionDone = false;
                }
            }
            return Go(s, target, heading, w.Speed > 0f ? w.Speed : CruiseSpeed, p, env, dt);
        }

        private PilotInput FlyReturn(in QuadState s, in AutopilotSensors sensors, in QuadProfile p, in PhysicsSettings env, float dt)
        {
            float height = MathF.Max(Home.y + ReturnHeight, _returnStage == 0 ? s.Position.y : Home.y + ReturnHeight);
            if (FenceHeight > 0f) height = MathF.Min(height, Home.y + FenceHeight - 0.5f);
            float dx = s.Position.x - Home.x, dz = s.Position.z - Home.z;
            bool overHome = dx * dx + dz * dz < AcceptRadius * AcceptRadius;
            if (_returnStage == 0)
            {
                // Climb first, where it is, so the way home clears what stands in between. Close to home it lands at once.
                if (overHome) _returnStage = 2;
                else if (s.Position.y >= Home.y + ReturnHeight - 0.5f || (FenceHeight > 0f && s.Position.y >= height - 0.5f)) _returnStage = 1;
                else return FlightAssist.GoTo(ref _assist, s, new Vec3(s.Position.x, Home.y + ReturnHeight, s.Position.z), float.NaN, CruiseSpeed, p, Settings, env, dt);
            }
            if (_returnStage == 1)
            {
                if (overHome && s.Velocity.Magnitude < 1f) _returnStage = 2;
                else return FlightAssist.GoTo(ref _assist, s, new Vec3(Home.x, MathF.Max(s.Position.y, Home.y + 2f), Home.z), float.NaN, CruiseSpeed, p, Settings, env, dt);
            }
            Land();
            return FlyLand(s, sensors, p, env, dt);
        }

        private PilotInput FlyLand(in QuadState s, in AutopilotSensors sensors, in QuadProfile p, in PhysicsSettings env, float dt)
        {
            float above = s.Position.y - sensors.GroundY;
            // Fast while high, gentle for the last two metres.
            float sink = above > 2f ? Settings.MaxDescent : LandSpeed;
            if (above < 0.12f && MathF.Abs(s.Velocity.y) < 0.3f) _groundTime += dt;
            else _groundTime = 0f;
            // The height the drone believes can be off by a few tens of centimetres (barometer). So it also counts as
            // down when it is low, has been asking to descend, and is not moving down any more.
            if (above < 1f && MathF.Abs(s.Velocity.y) < 0.15f) _stuckTime += dt;
            else _stuckTime = 0f;
            if (_groundTime > 0.4f || _stuckTime > 1f)
            {
                Landed = true;
                _stayDisarmed = true;
                Mode = GuidedMode.Manual;
                _assist = default;
                return new PilotInput { Arm = false, Mode = FlightMode.Angle };
            }
            if (!sensors.PositionOk)
            {
                // Landing without a position: straight down, level, wherever the wind takes it.
                var level = new PilotInput { Arm = true, Throttle = 0.5f - 0.5f * sink / MathF.Max(Settings.MaxDescent, 0.1f), Mode = FlightMode.Angle };
                return FlightAssist.Pilot(ref _assist, s, level, AssistMode.AltitudeHold, p, Settings, env, dt);
            }
            if (!_landSpotSet)
            {
                _landSpot = s.Position + StoppingOffset(s);
                _landSpotSet = true;
            }
            Vec3 e = new Vec3(_landSpot.x - s.Position.x, 0f, _landSpot.z - s.Position.z) * Settings.PositionGain;
            return FlightAssist.Velocity(ref _assist, s, new Vec3(e.x, -sink, e.z), 0f, p, Settings, env, dt);
        }
    }
}
