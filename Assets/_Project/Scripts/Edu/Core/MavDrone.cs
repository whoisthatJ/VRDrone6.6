using System;
using System.Collections.Generic;
using Ushqyr.Flight;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Joins the MAVLink endpoint to the simulator's own autopilot: what QGroundControl or a pymavlink program asks for
    /// becomes autopilot commands, and the drone's state becomes telemetry. The world's +Z is north and +X is east; a
    /// location's geographic origin places it on the map.
    /// </summary>
    public sealed class MavDrone : IMavVehicle
    {
        private const double MetresPerDegree = 111319.5;

        private readonly Autopilot _ap;
        private readonly double _lat0, _lon0, _cosLat;
        private readonly float _alt0;
        private QuadState _s;
        private AutopilotSensors _sensors;
        private float _volts = 16f;
        private readonly List<Waypoint> _waypoints = new List<Waypoint>();
        private readonly List<int> _itemOfWaypoint = new List<int>();
        private bool _missionEndsWithReturn, _missionRunning, _guided;
        private int _items;

        /// <summary>True while a ground station or program wants the motors on. The simulator adds it to the pilot's own arm switch.</summary>
        public bool ArmRequested { get; private set; }

        /// <summary>The assistance a person gets while the autopilot is in Manual; set by the mode a ground station picks.</summary>
        public AssistMode PilotAssist { get; private set; } = AssistMode.Position;

        public MavDrone(Autopilot autopilot, double originLat, double originLon, float originAltMsl)
        {
            _ap = autopilot ?? throw new ArgumentNullException(nameof(autopilot));
            _lat0 = originLat;
            _lon0 = originLon;
            _alt0 = originAltMsl;
            _cosLat = Math.Max(Math.Cos(originLat * Math.PI / 180.0), 1e-6);
        }

        /// <summary>Call every step before the endpoint is polled, with the drone's present state.</summary>
        public void Sync(in QuadState s, in AutopilotSensors sensors, float batteryVolts)
        {
            _s = s;
            _sensors = sensors;
            _volts = batteryVolts;
            if (_ap.Landed && ArmRequested && !s.Armed) ArmRequested = false; // it landed by itself: the request is spent
            // Guided was chosen while the motors were off (the autopilot falls back to Manual then): take it up once armed.
            if (_guided && s.Armed && _ap.Mode == GuidedMode.Manual && !_ap.Landed) _ap.Hold(s);
            if (_missionRunning && _ap.Mode != GuidedMode.Mission)
            {
                // The waypoint list is done. A mission that ends with "return to launch" goes home now.
                _missionRunning = false;
                if (_missionEndsWithReturn && _ap.Mode == GuidedMode.Hold) _ap.Return();
            }
        }

        public Vec3 ToLocal(double lat, double lon, float altRel) =>
            new Vec3((float)((lon - _lon0) * MetresPerDegree * _cosLat), _ap.Home.y + altRel, (float)((lat - _lat0) * MetresPerDegree));

        public double Lat => _lat0 + _s.Position.z / MetresPerDegree;
        public double Lon => _lon0 + _s.Position.x / (MetresPerDegree * _cosLat);
        public float AltMsl => _alt0 + _s.Position.y;
        public float AltRel => _s.Position.y - (_ap.HasHome ? _ap.Home.y : _sensors.GroundY);
        public float VelNorth => _s.Velocity.z;
        public float VelEast => _s.Velocity.x;
        public float VelDown => -_s.Velocity.y;
        public float Yaw => FlightAssist.HeadingDeg(_s) * (float)Math.PI / 180f;
        public float Pitch => (float)Math.Asin(Math.Max(-1f, Math.Min(1f, _s.Forward.y)));
        public float Roll => -(float)Math.Asin(Math.Max(-1f, Math.Min(1f, _s.Right.y)));
        /// <summary>Armed as the ground station should see it: the request counts at once, before the next simulation step applies it.</summary>
        public bool Armed => _s.Armed || ArmRequested;
        public bool Landed => _s.Position.y - _sensors.GroundY < 0.15f;
        public float BatteryVolts => _volts;
        public int BatteryPercent => _sensors.Battery < 0f ? 100 : (int)Math.Round(_sensors.Battery * 100f);
        public double HomeLat => _lat0 + (_ap.HasHome ? _ap.Home.z : 0f) / MetresPerDegree;
        public double HomeLon => _lon0 + (_ap.HasHome ? _ap.Home.x : 0f) / (MetresPerDegree * _cosLat);
        public float HomeAltMsl => _alt0 + (_ap.HasHome ? _ap.Home.y : 0f);

        /// <summary>The mission item being flown, counted as the ground station counts (item 0 is the home position).</summary>
        public int MissionSeq
        {
            get
            {
                if (_ap.Mode != GuidedMode.Mission && !_missionRunning) return 0;
                int w = _ap.MissionIndex;
                return w < _itemOfWaypoint.Count ? _itemOfWaypoint[w] : Math.Max(0, _items - 1);
            }
        }

        public int Mode
        {
            get
            {
                switch (_ap.Mode)
                {
                    case GuidedMode.Manual: return _guided ? 4 : PilotAssist == AssistMode.Position ? 5 : PilotAssist == AssistMode.AltitudeHold ? 2 : 0;
                    case GuidedMode.Mission: return 3;
                    case GuidedMode.Return: return 6;
                    case GuidedMode.Land: return 9;
                    default: return 4;
                }
            }
        }

        public bool Arm(bool on)
        {
            if (!on && !Landed) return false; // no switching the motors off in the air from the ground station
            ArmRequested = on;
            return true;
        }

        public bool SetMode(int mode)
        {
            bool known = mode == 0 || mode == 2 || mode == 3 || mode == 4 || mode == 5 || mode == 6 || mode == 9 || mode == 17;
            if (!known) return false;
            _guided = mode == 4 || mode == 17;
            switch (mode)
            {
                case 0: PilotAssist = AssistMode.Off; _ap.Manual(); return true;
                case 2: PilotAssist = AssistMode.AltitudeHold; _ap.Manual(); return true;
                case 5: PilotAssist = AssistMode.Position; _ap.Manual(); return true;
                case 4:
                case 17:
                    if (_s.Armed) _ap.Hold(_s);
                    return true;
                case 6: _ap.Return(); return true;
                case 9: _ap.Land(); return true;
                case 3:
                    if (_waypoints.Count == 0 || !_ap.StartMission(_waypoints)) return false;
                    _missionRunning = true;
                    return true;
                default: return false;
            }
        }

        public bool Takeoff(float altitude)
        {
            if (!_s.Armed || Mode != 4 || altitude <= 0f) return false;
            _ap.Takeoff(_s, altitude, _sensors.GroundY);
            return true;
        }

        public bool GoTo(double lat, double lon, float altRel)
        {
            if (!_s.Armed || Mode != 4 || Landed) return false;
            _ap.GoTo(ToLocal(lat, lon, altRel));
            return true;
        }

        public void SetMission(List<MavItem> items)
        {
            _waypoints.Clear();
            _itemOfWaypoint.Clear();
            _missionEndsWithReturn = false;
            _items = items == null ? 0 : items.Count;
            Vec3 last = new Vec3(_s.Position.x, _ap.Home.y, _s.Position.z);
            for (int i = 1; i < _items; i++) // item 0 is the home position
            {
                MavItem m = items[i];
                if (m.Command == 22) last = new Vec3(last.x, _ap.Home.y + m.Z, last.z); // take off: straight up
                else if (m.Command == 16) last = ToLocal(m.X / 1e7, m.Y / 1e7, m.Z);
                else if (m.Command == 21)
                {
                    if (m.X != 0 || m.Y != 0) last = ToLocal(m.X / 1e7, m.Y / 1e7, Math.Max(m.Z, 2f));
                    _waypoints.Add(new Waypoint { Position = last, HeadingDeg = float.NaN, Action = WaypointAction.Land });
                    _itemOfWaypoint.Add(i);
                    continue;
                }
                else if (m.Command == 20)
                {
                    _missionEndsWithReturn = true;
                    break;
                }
                else continue; // commands the simulator does not fly are skipped, as a real autopilot skips unknown ones
                _waypoints.Add(new Waypoint { Position = last, HeadingDeg = float.NaN, HoldSeconds = m.Command == 16 ? m.P1 : 0f });
                _itemOfWaypoint.Add(i);
            }
        }

        public void ParamChanged(string name, float value)
        {
            // ArduCopter keeps these in centimetres.
            if (name == "RTL_ALT") _ap.ReturnHeight = Math.Max(2f, value / 100f);
            else if (name == "WPNAV_SPEED") _ap.CruiseSpeed = Math.Max(0.5f, value / 100f);
            else if (name == "LAND_SPEED") _ap.LandSpeed = Math.Max(0.2f, value / 100f);
            else if (name == "FENCE_RADIUS") _fenceRadius = value;
            else if (name == "FENCE_ALT_MAX") _fenceHeight = value;
            else if (name == "FENCE_ENABLE") _fenceOn = value > 0.5f;
            _ap.FenceRadius = _fenceOn ? _fenceRadius : 0f;
            _ap.FenceHeight = _fenceOn ? _fenceHeight : 0f;
        }

        private float _fenceRadius = 300f, _fenceHeight = 100f;
        private bool _fenceOn;
    }
}
