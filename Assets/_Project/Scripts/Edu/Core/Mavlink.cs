using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>One waypoint or command of a mission, kept exactly as the ground station sent it.</summary>
    public struct MavItem
    {
        public float P1, P2, P3, P4, Z;
        public int X, Y;
        public ushort Command;
        public byte Frame, Autocontinue;
    }

    /// <summary>What the endpoint needs from the drone it speaks for. The simulator's autopilot will implement it.</summary>
    public interface IMavVehicle
    {
        double Lat { get; }
        double Lon { get; }
        float AltMsl { get; }
        float AltRel { get; }
        float VelNorth { get; }
        float VelEast { get; }
        float VelDown { get; }
        float Roll { get; }
        float Pitch { get; }
        float Yaw { get; }
        bool Armed { get; }
        bool Landed { get; }
        /// <summary>Flight mode number as ArduCopter counts them (0 Stabilize, 3 Auto, 4 Guided, 5 Loiter, 6 RTL, 9 Land).</summary>
        int Mode { get; }
        float BatteryVolts { get; }
        int BatteryPercent { get; }
        double HomeLat { get; }
        double HomeLon { get; }
        float HomeAltMsl { get; }
        int MissionSeq { get; }
        bool Arm(bool on);
        bool SetMode(int mode);
        bool Takeoff(float altitude);
        bool GoTo(double lat, double lon, float altRel);
        void SetMission(List<MavItem> items);
        void ParamChanged(string name, float value);
    }

    /// <summary>
    /// A MAVLink 2 endpoint that presents the drone as an ArduPilot multirotor, so that QGroundControl shows its
    /// flight modes and offers guided actions. It sends to the ground station's UDP port (14550) and answers
    /// whoever talks to it. No thread of its own: call <see cref="Poll"/> from the simulation loop.
    /// </summary>
    public sealed class MavlinkEndpoint : IDisposable
    {
        public const int Quadrotor = 2, ArduPilot = 3;
        private const byte Component = 1;

        private readonly UdpClient _udp;
        private readonly IMavVehicle _v;
        private readonly byte _system;
        private readonly List<IPEndPoint> _peers = new List<IPEndPoint>();
        private readonly List<string> _paramNames = new List<string>();
        private readonly Dictionary<string, float> _params = new Dictionary<string, float>();
        private List<MavItem> _mission = new List<MavItem>();
        private List<MavItem> _incoming;
        private int _incomingCount, _paramCursor = -1;
        private IPEndPoint _uploader;
        private byte _seq;
        private double _t1, _t4, _t10, _tUpload;
        private readonly Queue<string> _texts = new Queue<string>();

        public int Received { get; private set; }
        public int BadFrames { get; private set; }
        public IReadOnlyList<MavItem> Mission => _mission;

        public MavlinkEndpoint(IMavVehicle vehicle, byte systemId = 1, int groundStationPort = 14550, string groundStationHost = "127.0.0.1")
        {
            _v = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
            _system = systemId;
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            _udp.Client.Blocking = false;
            _peers.Add(new IPEndPoint(IPAddress.Parse(groundStationHost), groundStationPort));
            // A small, honest parameter set under the names ArduCopter uses, so the ground station's pages find them.
            Param("SYSID_THISMAV", systemId);
            Param("FRAME_CLASS", 1);
            Param("FRAME_TYPE", 1);
            Param("RTL_ALT", 1500);
            Param("WPNAV_SPEED", 500);
            Param("WPNAV_SPEED_UP", 250);
            Param("WPNAV_SPEED_DN", 150);
            Param("LAND_SPEED", 50);
            Param("PILOT_TKOFF_ALT", 200);
            Param("FENCE_ENABLE", 0);
            Param("FENCE_ALT_MAX", 100);
            Param("FENCE_RADIUS", 300);
            Param("FS_THR_ENABLE", 1);
            Param("BATT_MONITOR", 4);
            Param("BATT_LOW_VOLT", 14);
            Param("ARMING_CHECK", 1);
            for (int i = 1; i <= 6; i++) Param("FLTMODE" + i, i == 1 ? 0 : i == 2 ? 5 : 4);
            Param("FLTMODE_CH", 5);
        }

        private void Param(string name, float value)
        {
            _paramNames.Add(name);
            _params[name] = value;
        }

        public float GetParam(string name) => _params.TryGetValue(name, out float v) ? v : float.NaN;

        /// <summary>Shows a line of text in the ground station.</summary>
        public void Say(string text) => _texts.Enqueue(text);

        /// <summary>Reads what arrived and sends the periodic messages. <paramref name="now"/> is in seconds.</summary>
        public void Poll(double now)
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            for (int guard = 0; guard < 200 && _udp.Available > 0; guard++)
            {
                byte[] data;
                try { data = _udp.Receive(ref from); }
                catch (SocketException) { break; }
                var sender = new IPEndPoint(from.Address, from.Port);
                if (!_peers.Contains(sender) && _peers.Count < 8) _peers.Add(sender);
                Parse(data, sender, now);
            }
            uint ms = (uint)(now * 1000);
            if (now - _t1 >= 1.0)
            {
                _t1 = now;
                Heartbeat();
                var s = new W(31);
                s.U32(0x0320802F).U32(0x0320802F).U32(0x0320802F).U16(150).U16((ushort)(_v.BatteryVolts * 1000)).I16(-1)
                 .U16(0).U16(0).U16(0).U16(0).U16(0).U16(0).I8((sbyte)_v.BatteryPercent);
                Send(1, 124, s);
                var b = new W(36);
                b.I32(-1).I32(-1).I16(short.MaxValue).U16((ushort)(_v.BatteryVolts * 1000));
                for (int i = 1; i < 10; i++) b.U16(ushort.MaxValue);
                b.I16(-1).U8(0).U8(1).U8(1).I8((sbyte)_v.BatteryPercent);
                Send(147, 154, b);
                Send(245, 130, new W(2).U8(0).U8((byte)(_v.Landed ? 1 : 2)));
                Send(42, 28, new W(2).U16((ushort)Math.Max(0, _v.MissionSeq)));
                Home();
                if (_texts.Count > 0)
                {
                    var t = new W(51).U8(6);
                    t.Text(_texts.Dequeue(), 50);
                    Send(253, 83, t);
                }
            }
            if (now - _t4 >= 0.25)
            {
                _t4 = now;
                var g = new W(30);
                g.U64((ulong)(now * 1e6)).I32(Deg7(_v.Lat)).I32(Deg7(_v.Lon)).I32((int)(_v.AltMsl * 1000)).U16(80).U16(120)
                 .U16((ushort)(Speed() * 100)).U16(ushort.MaxValue).U8(3).U8(14);
                Send(24, 24, g);
                var p = new W(28);
                p.U32(ms).I32(Deg7(_v.Lat)).I32(Deg7(_v.Lon)).I32((int)(_v.AltMsl * 1000)).I32((int)(_v.AltRel * 1000))
                 .I16((short)(_v.VelNorth * 100)).I16((short)(_v.VelEast * 100)).I16((short)(_v.VelDown * 100)).U16((ushort)(Heading() * 100));
                Send(33, 104, p);
                Send(74, 20, new W(20).F(Speed()).F(Speed()).F(_v.AltMsl).F(-_v.VelDown).I16((short)Heading()).U16((ushort)(_v.Armed ? 50 : 0)));
            }
            if (now - _t10 >= 0.1)
            {
                _t10 = now;
                Send(30, 39, new W(28).U32(ms).F(_v.Roll).F(_v.Pitch).F(_v.Yaw).F(0).F(0).F(0));
                // The parameter list goes out a few at a time, as a real autopilot sends it.
                for (int n = 0; n < 10 && _paramCursor >= 0; n++)
                {
                    SendParam(_paramCursor);
                    if (++_paramCursor >= _paramNames.Count) _paramCursor = -1;
                }
            }
            if (_incoming != null && now - _tUpload > 1.0)
            {
                // The ground station went quiet in the middle of an upload: ask again, then give up.
                if (now - _tUpload > 5.0) _incoming = null;
                else RequestItem(_incoming.Count);
            }
        }

        private float Speed() => (float)Math.Sqrt(_v.VelNorth * _v.VelNorth + _v.VelEast * _v.VelEast);

        private float Heading()
        {
            double d = _v.Yaw * 180.0 / Math.PI % 360.0;
            return (float)(d < 0 ? d + 360.0 : d);
        }

        private static int Deg7(double deg) => (int)Math.Round(deg * 1e7);

        private void Heartbeat()
        {
            byte baseMode = (byte)(1 | 16 | 8 | (_v.Armed ? 128 : 0)); // custom mode, stabilised, guided, armed
            Send(0, 50, new W(9).U32((uint)_v.Mode).U8(Quadrotor).U8(ArduPilot).U8(baseMode).U8((byte)(_v.Armed ? 4 : 3)).U8(3));
        }

        private void Home()
        {
            var h = new W(52);
            h.I32(Deg7(_v.HomeLat)).I32(Deg7(_v.HomeLon)).I32((int)(_v.HomeAltMsl * 1000)).F(0).F(0).F(0).F(1).F(0).F(0).F(0).F(0).F(0).F(0);
            Send(242, 104, h);
        }

        private void SendParam(int index)
        {
            if (index < 0 || index >= _paramNames.Count) return;
            var w = new W(25).F(_params[_paramNames[index]]).U16((ushort)_paramNames.Count).U16((ushort)index);
            w.Text(_paramNames[index], 16);
            Send(22, 220, w.U8(9)); // 9 = 32-bit float
        }

        private void Ack(int command, bool ok, byte failure = 4)
        {
            Send(77, 143, new W(10).U16((ushort)command).U8(ok ? (byte)0 : failure).U8(0).I32(0).U8(255).U8(190));
        }

        private void MissionAck(byte type, byte missionType)
        {
            Send(47, 153, new W(4).U8(255).U8(190).U8(type).U8(missionType));
        }

        private void RequestItem(int seq)
        {
            Send(51, 196, new W(5).U16((ushort)seq).U8(255).U8(190).U8(0));
        }

        private void Version()
        {
            var w = new W(60);
            w.U64(1 | 2 | 4 | 8 | 64 | 128 | 8192).U64(_system).U32((4u << 24) | (5u << 16) | 255u).U32(0).U32(0).U32(0).U16(0).U16(0);
            Send(148, 178, w);
        }

        private void Parse(byte[] d, IPEndPoint from, double now)
        {
            int at = 0;
            while (at < d.Length)
            {
                if (d[at] != 0xFD) { at++; continue; } // only MAVLink 2
                if (at + 12 > d.Length) break;
                int len = d[at + 1];
                bool signed = (d[at + 2] & 1) != 0;
                int total = 10 + len + 2 + (signed ? 13 : 0);
                if (at + total > d.Length) break;
                int id = d[at + 7] | (d[at + 8] << 8) | (d[at + 9] << 16);
                int extra = Extra(id);
                if (extra >= 0)
                {
                    ushort crc = Crc(d, at + 1, 9 + len, (byte)extra);
                    if (crc == (d[at + 10 + len] | (d[at + 11 + len] << 8)))
                    {
                        Received++;
                        var payload = new byte[64]; // zero padded: MAVLink 2 cuts trailing zeros off
                        Buffer.BlockCopy(d, at + 10, payload, 0, Math.Min(len, 64));
                        Handle(id, new R(payload), from, now);
                    }
                    else BadFrames++;
                }
                at += total;
            }
        }

        private static int Extra(int id)
        {
            switch (id)
            {
                case 0: return 50; case 11: return 89; case 20: return 214; case 21: return 159; case 23: return 168;
                case 40: return 230; case 43: return 132; case 44: return 221; case 45: return 232; case 47: return 153;
                case 51: return 196; case 73: return 38; case 75: return 158; case 76: return 152; case 86: return 5;
                default: return -1;
            }
        }

        private void Handle(int id, R r, IPEndPoint from, double now)
        {
            switch (id)
            {
                case 21: // PARAM_REQUEST_LIST
                    _paramCursor = 0;
                    break;
                case 20: // PARAM_REQUEST_READ
                {
                    int index = r.I16();
                    r.Skip(2);
                    string name = r.Text(16);
                    SendParam(index >= 0 ? index : _paramNames.IndexOf(name));
                    break;
                }
                case 23: // PARAM_SET
                {
                    float value = r.F();
                    r.Skip(2);
                    string name = r.Text(16);
                    int index = _paramNames.IndexOf(name);
                    if (index < 0 || float.IsNaN(value) || float.IsInfinity(value)) break;
                    _params[name] = value;
                    _v.ParamChanged(name, value);
                    SendParam(index);
                    break;
                }
                case 11: // SET_MODE
                {
                    int mode = (int)r.U32();
                    Ack(11, _v.SetMode(mode));
                    Heartbeat();
                    break;
                }
                case 43: // MISSION_REQUEST_LIST
                {
                    r.Skip(2);
                    byte type = r.U8();
                    Send(44, 221, new W(5).U16((ushort)(type == 0 ? _mission.Count : 0)).U8(255).U8(190).U8(type));
                    break;
                }
                case 40: // MISSION_REQUEST
                case 51: // MISSION_REQUEST_INT
                {
                    int seq = r.U16();
                    if (seq >= _mission.Count) { MissionAck(13, 0); break; }
                    MavItem m = _mission[seq];
                    var w = new W(38);
                    w.F(m.P1).F(m.P2).F(m.P3).F(m.P4).I32(m.X).I32(m.Y).F(m.Z).U16((ushort)seq).U16(m.Command)
                     .U8(255).U8(190).U8(m.Frame).U8((byte)(seq == _v.MissionSeq ? 1 : 0)).U8(m.Autocontinue).U8(0);
                    Send(73, 38, w);
                    break;
                }
                case 44: // MISSION_COUNT: an upload starts
                {
                    int count = r.U16();
                    r.Skip(2);
                    byte type = r.U8();
                    if (type != 0 || count == 0)
                    {
                        if (type == 0) { _mission = new List<MavItem>(); _v.SetMission(_mission); }
                        MissionAck(0, type);
                        break;
                    }
                    if (count > 500) { MissionAck(4, 0); break; }
                    _incoming = new List<MavItem>();
                    _incomingCount = count;
                    _uploader = from;
                    _tUpload = now;
                    RequestItem(0);
                    break;
                }
                case 73: // MISSION_ITEM_INT
                {
                    var m = new MavItem { P1 = r.F(), P2 = r.F(), P3 = r.F(), P4 = r.F(), X = r.I32(), Y = r.I32(), Z = r.F() };
                    int seq = r.U16();
                    m.Command = r.U16();
                    r.Skip(2);
                    m.Frame = r.U8();
                    byte current = r.U8();
                    m.Autocontinue = r.U8();
                    if (current == 2) // "fly here now" in guided mode
                    {
                        _v.GoTo(m.X / 1e7, m.Y / 1e7, m.Z);
                        MissionAck(0, 0);
                        break;
                    }
                    if (_incoming == null || !from.Equals(_uploader)) break;
                    if (seq != _incoming.Count) { RequestItem(_incoming.Count); break; }
                    _incoming.Add(m);
                    _tUpload = now;
                    if (_incoming.Count < _incomingCount) RequestItem(_incoming.Count);
                    else
                    {
                        _mission = _incoming;
                        _incoming = null;
                        _v.SetMission(_mission);
                        MissionAck(0, 0);
                    }
                    break;
                }
                case 45: // MISSION_CLEAR_ALL
                    _mission = new List<MavItem>();
                    _v.SetMission(_mission);
                    MissionAck(0, 0);
                    break;
                case 86: // SET_POSITION_TARGET_GLOBAL_INT
                {
                    r.Skip(4);
                    int lat = r.I32(), lon = r.I32();
                    float alt = r.F();
                    _v.GoTo(lat / 1e7, lon / 1e7, alt);
                    break;
                }
                case 75: // COMMAND_INT
                {
                    float p1 = r.F(), p2 = r.F();
                    r.Skip(8);
                    int x = r.I32(), y = r.I32();
                    float z = r.F();
                    Command(r.U16(), p1, p2, x / 1e7, y / 1e7, z);
                    break;
                }
                case 76: // COMMAND_LONG
                {
                    float p1 = r.F(), p2 = r.F();
                    r.Skip(8);
                    float p5 = r.F(), p6 = r.F(), p7 = r.F();
                    Command(r.U16(), p1, p2, p5, p6, p7);
                    break;
                }
            }
        }

        private void Command(int command, float p1, float p2, double lat, double lon, float alt)
        {
            switch (command)
            {
                case 400: Ack(command, _v.Arm(p1 > 0.5f)); Heartbeat(); break;
                case 22: Ack(command, _v.Takeoff(alt)); break;
                case 21: Ack(command, _v.SetMode(9)); break;
                case 20: Ack(command, _v.SetMode(6)); break;
                case 176: Ack(command, _v.SetMode((int)p2)); Heartbeat(); break;
                case 192: Ack(command, _v.GoTo(lat, lon, float.IsNaN(alt) ? _v.AltRel : alt - _v.HomeAltMsl)); break;
                case 300: Ack(command, _v.SetMode(3)); break;
                case 410: Ack(command, true); Home(); break;
                case 511: Ack(command, true); break;
                case 520: Ack(command, true); Version(); break;
                case 512:
                    if ((int)p1 == 148) { Ack(command, true); Version(); }
                    else if ((int)p1 == 242) { Ack(command, true); Home(); }
                    else Ack(command, false, 3);
                    break;
                default: Ack(command, false, 3); break; // 3 = unsupported
            }
        }

        private void Send(int id, byte extra, W w)
        {
            int len = w.Length;
            while (len > 1 && w.Data[len - 1] == 0) len--; // MAVLink 2: trailing zeros are not sent
            var f = new byte[12 + len];
            f[0] = 0xFD;
            f[1] = (byte)len;
            f[4] = _seq++;
            f[5] = _system;
            f[6] = Component;
            f[7] = (byte)id;
            f[8] = (byte)(id >> 8);
            f[9] = (byte)(id >> 16);
            Buffer.BlockCopy(w.Data, 0, f, 10, len);
            ushort crc = Crc(f, 1, 9 + len, extra);
            f[10 + len] = (byte)crc;
            f[11 + len] = (byte)(crc >> 8);
            foreach (IPEndPoint peer in _peers)
            {
                try { _udp.Send(f, f.Length, peer); }
                catch (SocketException) { }
            }
        }

        /// <summary>The checksum of MAVLink (X.25), with the message's own extra byte at the end.</summary>
        public static ushort Crc(byte[] data, int start, int count, byte extra)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i <= count; i++)
            {
                byte b = i < count ? data[start + i] : extra;
                byte t = (byte)(b ^ (byte)(crc & 0xFF));
                t ^= (byte)(t << 4);
                crc = (ushort)((crc >> 8) ^ (t << 8) ^ (t << 3) ^ (t >> 4));
            }
            return crc;
        }

        public void Dispose() => _udp.Close();

        private sealed class W
        {
            public readonly byte[] Data;
            public int Length => Data.Length;
            private int _at;
            public W(int size) { Data = new byte[size]; }
            public W U8(byte v) { Data[_at++] = v; return this; }
            public W I8(sbyte v) => U8((byte)v);
            public W U16(ushort v) { Data[_at++] = (byte)v; Data[_at++] = (byte)(v >> 8); return this; }
            public W I16(short v) => U16((ushort)v);
            public W U32(uint v) { U16((ushort)v); return U16((ushort)(v >> 16)); }
            public W I32(int v) => U32((uint)v);
            public W U64(ulong v) { U32((uint)v); return U32((uint)(v >> 32)); }
            public W F(float v) => I32(BitConverter.SingleToInt32Bits(v));
            public W Text(string s, int size)
            {
                byte[] b = Encoding.ASCII.GetBytes(s ?? "");
                Buffer.BlockCopy(b, 0, Data, _at, Math.Min(b.Length, size));
                _at += size;
                return this;
            }
        }

        private sealed class R
        {
            private readonly byte[] _d;
            private int _at;
            public R(byte[] d) { _d = d; }
            public void Skip(int n) => _at += n;
            public byte U8() => _d[_at++];
            public ushort U16() { ushort v = (ushort)(_d[_at] | (_d[_at + 1] << 8)); _at += 2; return v; }
            public short I16() => (short)U16();
            public uint U32() { uint v = (uint)(_d[_at] | (_d[_at + 1] << 8) | (_d[_at + 2] << 16) | (_d[_at + 3] << 24)); _at += 4; return v; }
            public int I32() => (int)U32();
            public float F() => BitConverter.Int32BitsToSingle(I32());
            public string Text(int size)
            {
                int n = 0;
                while (n < size && _d[_at + n] != 0) n++;
                string s = Encoding.ASCII.GetString(_d, _at, n);
                _at += size;
                return s;
            }
        }
    }

    /// <summary>
    /// A stand-in drone for trying the endpoint before the real autopilot exists: it moves in straight lines at fixed
    /// speeds. It is not flight physics and is never used for a grade.
    /// </summary>
    public sealed class MavTestVehicle : IMavVehicle
    {
        private const double MetresPerDegree = 111319.5;
        private double _north, _east, _up, _tn, _te, _tu;
        private List<MavItem> _mission = new List<MavItem>();
        private bool _hasTarget;

        public double HomeLat { get; }
        public double HomeLon { get; }
        public float HomeAltMsl { get; }
        public bool Armed { get; private set; }
        public int Mode { get; private set; }
        public int MissionSeq { get; private set; }
        public float VelNorth { get; private set; }
        public float VelEast { get; private set; }
        public float VelDown { get; private set; }
        public float Yaw { get; private set; }
        public float Roll => 0;
        public float Pitch => 0;
        public float BatteryVolts => 16.2f;
        public int BatteryPercent => 87;
        public bool Landed => _up < 0.05;
        public float AltRel => (float)_up;
        public float AltMsl => HomeAltMsl + (float)_up;
        public double Lat => HomeLat + _north / MetresPerDegree;
        public double Lon => HomeLon + _east / (MetresPerDegree * Math.Cos(HomeLat * Math.PI / 180));
        public float RtlAltitude = 15;

        public MavTestVehicle(double homeLat, double homeLon, float homeAltMsl)
        {
            HomeLat = homeLat;
            HomeLon = homeLon;
            HomeAltMsl = homeAltMsl;
        }

        public bool Arm(bool on)
        {
            if (!on && !Landed) return false;
            Armed = on;
            return true;
        }

        public bool SetMode(int mode)
        {
            if (mode != 0 && mode != 2 && mode != 3 && mode != 4 && mode != 5 && mode != 6 && mode != 9 && mode != 17) return false;
            if (mode == 3 && _mission.Count < 2) return false;
            Mode = mode;
            _hasTarget = false;
            if (mode == 3) MissionSeq = 1; // item 0 is the home position
            return true;
        }

        public bool Takeoff(float altitude)
        {
            if (!Armed || Mode != 4 || altitude <= 0) return false;
            Target(_north, _east, altitude);
            return true;
        }

        public bool GoTo(double lat, double lon, float altRel)
        {
            if (!Armed || Mode != 4 || Landed) return false;
            Target((lat - HomeLat) * MetresPerDegree, (lon - HomeLon) * MetresPerDegree * Math.Cos(HomeLat * Math.PI / 180), altRel);
            return true;
        }

        public void SetMission(List<MavItem> items)
        {
            _mission = items ?? new List<MavItem>();
            MissionSeq = 0;
        }

        public void ParamChanged(string name, float value)
        {
            if (name == "RTL_ALT") RtlAltitude = value / 100f;
        }

        private void Target(double n, double e, double u)
        {
            _tn = n;
            _te = e;
            _tu = u;
            _hasTarget = true;
        }

        public void Step(double dt)
        {
            VelNorth = VelEast = VelDown = 0;
            if (!Armed) return;
            if (Mode == 9) Target(_north, _east, 0);
            else if (Mode == 6)
            {
                bool far = Math.Abs(_north) + Math.Abs(_east) > 0.5;
                if (far && _up < RtlAltitude - 0.3) Target(_north, _east, RtlAltitude);
                else if (far) Target(0, 0, RtlAltitude);
                else Target(0, 0, 0);
            }
            else if (Mode == 3 && !_hasTarget && MissionSeq < _mission.Count)
            {
                MavItem m = _mission[MissionSeq];
                if (m.Command == 22) Target(_north, _east, m.Z);
                else if (m.Command == 16)
                    Target((m.X / 1e7 - HomeLat) * MetresPerDegree,
                           (m.Y / 1e7 - HomeLon) * MetresPerDegree * Math.Cos(HomeLat * Math.PI / 180), m.Z);
                else if (m.Command == 21) Target(_north, _east, 0);
                else if (m.Command == 20) { Mode = 6; return; }
                else MissionSeq++;
            }
            if (!_hasTarget) return;
            double dn = _tn - _north, de = _te - _east, du = _tu - _up;
            double flat = Math.Sqrt(dn * dn + de * de);
            double hs = Math.Min(5.0, flat / Math.Max(dt, 1e-3)), vs = Math.Min(2.0, Math.Abs(du) / Math.Max(dt, 1e-3));
            if (flat > 1e-3)
            {
                VelNorth = (float)(dn / flat * hs);
                VelEast = (float)(de / flat * hs);
                Yaw = (float)Math.Atan2(de, dn);
                _north += VelNorth * dt;
                _east += VelEast * dt;
            }
            _up += Math.Sign(du) * vs * dt;
            VelDown = (float)(-Math.Sign(du) * vs);
            if (flat < 0.2 && Math.Abs(du) < 0.1)
            {
                _hasTarget = false;
                if (Mode == 3) MissionSeq++;
                if (_up < 0.05 && (Mode == 9 || Mode == 6 || Mode == 3 && MissionSeq >= _mission.Count)) Armed = false;
            }
        }
    }
}
