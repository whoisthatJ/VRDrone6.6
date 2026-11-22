using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The teacher's signing key of a class (EDU_PLAN F6). It is made on the teacher's PC when the class link is
    /// first opened and kept in the class database there. Seats get only the public half, remember its fingerprint,
    /// and obey only commands signed with it: one student's PC cannot send "teacher" commands to another.
    /// </summary>
    public sealed class ClassKey : IDisposable
    {
        private readonly RSA _rsa;
        public bool CanSign { get; }

        private ClassKey(RSA rsa, bool canSign)
        {
            _rsa = rsa;
            CanSign = canSign;
        }

        public static ClassKey Create()
        {
            RSA rsa = RSA.Create();
            rsa.KeySize = 2048;
            return new ClassKey(rsa, true);
        }

        private static string B(byte[] x) => Convert.ToBase64String(x ?? new byte[0]);

        /// <summary>The public half, as text: modulus.exponent</summary>
        public string ExportPublic()
        {
            RSAParameters p = _rsa.ExportParameters(false);
            return B(p.Modulus) + "." + B(p.Exponent);
        }

        /// <summary>The whole key, as text. It stays in the class database on the teacher's PC.</summary>
        public string ExportPrivate()
        {
            RSAParameters p = _rsa.ExportParameters(true);
            return string.Join(".", B(p.Modulus), B(p.Exponent), B(p.D), B(p.P), B(p.Q), B(p.DP), B(p.DQ), B(p.InverseQ));
        }

        /// <summary>Reads a key exported by <see cref="ExportPublic"/> or <see cref="ExportPrivate"/>.</summary>
        public static ClassKey Import(string text)
        {
            string[] parts = (text ?? "").Split('.');
            if (parts.Length != 2 && parts.Length != 8) throw new FormatException("this is not a class key");
            var bytes = new byte[parts.Length][];
            for (int i = 0; i < parts.Length; i++) bytes[i] = Convert.FromBase64String(parts[i]);
            var p = new RSAParameters { Modulus = bytes[0], Exponent = bytes[1] };
            if (parts.Length == 8)
            {
                p.D = bytes[2]; p.P = bytes[3]; p.Q = bytes[4]; p.DP = bytes[5]; p.DQ = bytes[6]; p.InverseQ = bytes[7];
            }
            RSA rsa = RSA.Create();
            rsa.ImportParameters(p);
            return new ClassKey(rsa, parts.Length == 8);
        }

        /// <summary>Sixteen hex digits that name the public key; a seat remembers them for its class.</summary>
        public string Fingerprint => FingerprintOf(ExportPublic());

        public static string FingerprintOf(string publicKey)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.ASCII.GetBytes(publicKey ?? ""));
                var b = new StringBuilder();
                for (int i = 0; i < 8; i++) b.Append(h[i].ToString("x2"));
                return b.ToString();
            }
        }

        public byte[] Sign(byte[] data) => _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        public bool Verify(byte[] data, byte[] signature)
        {
            try { return _rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
            catch (CryptographicException) { return false; }
        }

        public void Dispose() => _rsa.Dispose();
    }

    /// <summary>The frames both ends of the class link speak: 4 bytes of length, 1 byte of kind, the payload.</summary>
    public static class ClassWire
    {
        public const int DefaultPort = 47810, DefaultBeaconPort = 47811;
        public const byte Json = 1, Picture = 2, Attempt = 3, Command = 4;
        /// <summary>The largest frame accepted: a recording of a long flight is a few hundred kilobytes.</summary>
        public const int MaxFrame = 16 * 1024 * 1024;

        public static void Write(Stream s, byte kind, byte[] payload)
        {
            var head = new byte[5];
            int n = payload.Length;
            head[0] = (byte)n; head[1] = (byte)(n >> 8); head[2] = (byte)(n >> 16); head[3] = (byte)(n >> 24);
            head[4] = kind;
            s.Write(head, 0, 5);
            s.Write(payload, 0, n);
            s.Flush();
        }

        public static void WriteJson(Stream s, Dictionary<string, object> message) => Write(s, Json, Encoding.UTF8.GetBytes(Edu.Json.Write(message)));

        private static bool ReadExact(Stream s, byte[] buffer, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buffer, got, count - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        /// <summary>Reads one frame; false when the other end has gone.</summary>
        public static bool Read(Stream s, out byte kind, out byte[] payload)
        {
            kind = 0;
            payload = null;
            var head = new byte[5];
            if (!ReadExact(s, head, 5)) return false;
            int n = head[0] | head[1] << 8 | head[2] << 16 | head[3] << 24;
            if (n < 0 || n > MaxFrame) throw new InvalidDataException("a frame of " + n + " bytes is not accepted");
            kind = head[4];
            payload = new byte[n];
            return ReadExact(s, payload, n);
        }

        public static Dictionary<string, object> ReadJson(Stream s)
        {
            if (!Read(s, out byte kind, out byte[] payload)) throw new IOException("the connection closed");
            if (kind != Json) throw new InvalidDataException("a message was expected");
            return Edu.Json.ParseObject(Encoding.UTF8.GetString(payload));
        }

        /// <summary>What proves that an end knows the class code, without sending the code.</summary>
        public static string Proof(string code, string who, string seatNonce, string hostNonce, string what)
        {
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes("ushqyr-class:" + (code ?? ""))))
                return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(who + "|" + seatNonce + "|" + hostNonce + "|" + what)));
        }

        public static string Nonce()
        {
            var b = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Convert.ToBase64String(b);
        }

        public static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var b = new StringBuilder();
                foreach (byte x in sha.ComputeHash(data)) b.Append(x.ToString("x2"));
                return b.ToString();
            }
        }

        /// <summary>An attempt frame: the record as JSON, then the bytes of the recording.</summary>
        public static byte[] PackAttempt(string json, byte[] replay)
        {
            byte[] j = Encoding.UTF8.GetBytes(json);
            var all = new byte[4 + j.Length + (replay?.Length ?? 0)];
            all[0] = (byte)j.Length; all[1] = (byte)(j.Length >> 8); all[2] = (byte)(j.Length >> 16); all[3] = (byte)(j.Length >> 24);
            Buffer.BlockCopy(j, 0, all, 4, j.Length);
            if (replay != null) Buffer.BlockCopy(replay, 0, all, 4 + j.Length, replay.Length);
            return all;
        }

        public static void UnpackAttempt(byte[] payload, out string json, out byte[] replay)
        {
            if (payload.Length < 4) throw new InvalidDataException("a short attempt frame");
            int n = payload[0] | payload[1] << 8 | payload[2] << 16 | payload[3] << 24;
            if (n < 0 || n > payload.Length - 4) throw new InvalidDataException("a damaged attempt frame");
            json = Encoding.UTF8.GetString(payload, 4, n);
            replay = new byte[payload.Length - 4 - n];
            Buffer.BlockCopy(payload, 4 + n, replay, 0, replay.Length);
        }
    }

    /// <summary>Something that happened on the class link, handed to the teacher's screen.</summary>
    public sealed class ClassEvent
    {
        /// <summary>"joined", "left", "status", "picture", "attempt" or "hand".</summary>
        public string Kind;
        public string Seat;
        public Dictionary<string, object> Data;
        public byte[] Bytes;
    }

    /// <summary>A seat as the teacher's PC knows it.</summary>
    public sealed class ClassSeatInfo
    {
        public string Name = "", User = "", Address = "";
        public bool Connected;
        public Dictionary<string, object> Status = new Dictionary<string, object>();
        public byte[] Picture;
        public DateTime LastHeard, PictureAt;
        public int Pictures;
        internal TcpClient Client;
        internal Stream Stream;
        internal readonly object WriteLock = new object();
    }

    /// <summary>
    /// The teacher's end of the class link: listens on the LAN, lets in seats that know the class code, hears their
    /// status, pictures and finished attempts, and sends them signed commands. All network work is on its own
    /// threads; what happened is read with <see cref="Poll"/> from the game's thread.
    /// </summary>
    public sealed class ClassHost : IDisposable
    {
        private readonly ClassKey _key;
        private readonly string _code;
        private TcpListener _listener;
        private UdpClient _beacon;
        private volatile bool _open;
        private long _counter;
        private readonly ConcurrentQueue<ClassEvent> _events = new ConcurrentQueue<ClassEvent>();
        private readonly Dictionary<string, ClassSeatInfo> _seats = new Dictionary<string, ClassSeatInfo>();

        public string ClassName { get; }
        public string Session { get; } = ClassWire.Nonce();
        public int Port { get; private set; }

        public ClassHost(string className, string code, ClassKey key)
        {
            if (key == null || !key.CanSign) throw new ArgumentException("the class key with its private half is needed");
            if (string.IsNullOrEmpty(code) || code.Length < 4) throw new ArgumentException("the class code has at least 4 characters");
            ClassName = className ?? "";
            _code = code;
            _key = key;
        }

        /// <summary>
        /// Opens the link. <paramref name="port"/> 0 takes any free port (tests). With a beacon port the class
        /// announces itself on the LAN every second so seats can find it without typing an address.
        /// </summary>
        public void Start(int port = ClassWire.DefaultPort, int beaconPort = 0, bool loopbackOnly = false)
        {
            _listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _open = true;
            new Thread(AcceptLoop) { IsBackground = true, Name = "Class link: accept" }.Start();
            if (beaconPort > 0)
            {
                _beacon = new UdpClient { EnableBroadcast = true };
                new Thread(() => BeaconLoop(beaconPort)) { IsBackground = true, Name = "Class link: beacon" }.Start();
            }
        }

        private void BeaconLoop(int beaconPort)
        {
            byte[] hello = Encoding.UTF8.GetBytes(Json.Write(new Dictionary<string, object>
            {
                ["ushqyr_class"] = ClassName, ["port"] = (double)Port, ["key"] = _key.Fingerprint, ["v"] = 1.0,
            }));
            while (_open)
            {
                foreach (IPAddress to in new[] { IPAddress.Broadcast, IPAddress.Loopback })
                {
                    try { _beacon.Send(hello, hello.Length, new IPEndPoint(to, beaconPort)); }
                    catch (SocketException) { /* no network just now: try again next second */ }
                    catch (ObjectDisposedException) { return; }
                }
                Thread.Sleep(1000);
            }
        }

        private void AcceptLoop()
        {
            while (_open)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
                new Thread(() => Serve(client)) { IsBackground = true, Name = "Class link: seat" }.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            ClassSeatInfo seat = null;
            try
            {
                client.NoDelay = true;
                client.SendTimeout = 5000;
                client.ReceiveTimeout = 10000; // for the handshake only
                Stream s = client.GetStream();
                Dictionary<string, object> hello = ClassWire.ReadJson(s);
                string name = Json.Str(hello, "seat", "").Trim(), seatNonce = Json.Str(hello, "nonce", "");
                if (Json.Str(hello, "t", "") != "hello" || name.Length == 0 || name.Length > 80 || seatNonce.Length == 0) throw new InvalidDataException("not a seat");
                string hostNonce = ClassWire.Nonce(), pub = _key.ExportPublic();
                ClassWire.WriteJson(s, new Dictionary<string, object>
                {
                    ["t"] = "welcome", ["class"] = ClassName, ["nonce"] = hostNonce, ["key"] = pub, ["session"] = Session,
                    ["proof"] = ClassWire.Proof(_code, "host", seatNonce, hostNonce, pub + "|" + Session),
                });
                Dictionary<string, object> join = ClassWire.ReadJson(s);
                if (Json.Str(join, "t", "") != "join" || Json.Str(join, "proof", "") != ClassWire.Proof(_code, "seat", seatNonce, hostNonce, name))
                {
                    ClassWire.WriteJson(s, new Dictionary<string, object> { ["t"] = "no", ["why"] = "code" });
                    client.Close();
                    return;
                }
                ClassWire.WriteJson(s, new Dictionary<string, object> { ["t"] = "ok" });
                client.ReceiveTimeout = 0;

                lock (_seats)
                {
                    if (!_seats.TryGetValue(name, out seat)) _seats[name] = seat = new ClassSeatInfo { Name = name };
                    // The same seat joining again takes the place of its old connection.
                    try { seat.Client?.Close(); } catch (SocketException) { }
                    seat.Client = client;
                    seat.Stream = s;
                    seat.Connected = true;
                    seat.User = Json.Str(join, "user", "");
                    seat.Address = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    seat.LastHeard = DateTime.UtcNow;
                }
                _events.Enqueue(new ClassEvent { Kind = "joined", Seat = name });

                while (_open && ClassWire.Read(s, out byte kind, out byte[] payload))
                {
                    seat.LastHeard = DateTime.UtcNow;
                    if (kind == ClassWire.Json)
                    {
                        Dictionary<string, object> m = Json.ParseObject(Encoding.UTF8.GetString(payload));
                        string t = Json.Str(m, "t", "");
                        if (t == "status")
                        {
                            seat.Status = m;
                            seat.User = Json.Str(m, "user", seat.User);
                            _events.Enqueue(new ClassEvent { Kind = "status", Seat = name, Data = m });
                        }
                        else if (t == "hand") _events.Enqueue(new ClassEvent { Kind = "hand", Seat = name, Data = m });
                    }
                    else if (kind == ClassWire.Picture)
                    {
                        seat.Picture = payload;
                        seat.PictureAt = DateTime.UtcNow;
                        seat.Pictures++;
                        _events.Enqueue(new ClassEvent { Kind = "picture", Seat = name, Bytes = payload });
                    }
                    else if (kind == ClassWire.Attempt)
                    {
                        ClassWire.UnpackAttempt(payload, out string json, out byte[] replay);
                        Dictionary<string, object> record = Json.ParseObject(json);
                        _events.Enqueue(new ClassEvent { Kind = "attempt", Seat = name, Data = record, Bytes = replay });
                        // Told at once that it arrived; the seat keeps its copy until it hears this.
                        WriteTo(seat, s, ClassWire.Json, Encoding.UTF8.GetBytes(Json.Write(new Dictionary<string, object> { ["t"] = "got", ["hash"] = ClassWire.Sha256(replay) })));
                    }
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidDataException) { }
            catch (FormatException) { }
            finally
            {
                try { client.Close(); } catch (SocketException) { }
                if (seat != null)
                {
                    bool was;
                    lock (_seats)
                    {
                        was = seat.Client == client;
                        if (was) seat.Connected = false;
                    }
                    if (was) _events.Enqueue(new ClassEvent { Kind = "left", Seat = seat.Name });
                }
            }
        }

        private static bool WriteTo(ClassSeatInfo seat, Stream s, byte kind, byte[] payload)
        {
            try
            {
                lock (seat.WriteLock) ClassWire.Write(s, kind, payload);
                return true;
            }
            catch (IOException) { return false; }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }

        /// <summary>The next thing that happened, if any.</summary>
        public bool Poll(out ClassEvent e) => _events.TryDequeue(out e);

        /// <summary>The seats, in the order of their names.</summary>
        public List<ClassSeatInfo> Seats()
        {
            lock (_seats)
            {
                var list = new List<ClassSeatInfo>(_seats.Values);
                list.Sort((a, b) => EduStore.CompareNames(a.Name, b.Name));
                return list;
            }
        }

        /// <summary>
        /// A signed command frame for one seat, or for all ("*"). It carries this session and a number that only
        /// grows, so a recorded command cannot be sent again later, nor to another seat.
        /// </summary>
        public byte[] BuildCommand(Dictionary<string, object> command, string to = "*")
        {
            var c = new Dictionary<string, object>(command) { ["session"] = Session, ["n"] = (double)Interlocked.Increment(ref _counter), ["to"] = to };
            byte[] json = Encoding.UTF8.GetBytes(Json.Write(c)), sig = _key.Sign(json);
            var frame = new byte[2 + sig.Length + json.Length];
            frame[0] = (byte)sig.Length;
            frame[1] = (byte)(sig.Length >> 8);
            Buffer.BlockCopy(sig, 0, frame, 2, sig.Length);
            Buffer.BlockCopy(json, 0, frame, 2 + sig.Length, json.Length);
            return frame;
        }

        /// <summary>Sends a command to one seat. False when the seat is not connected.</summary>
        public bool Send(string seatName, Dictionary<string, object> command)
        {
            ClassSeatInfo seat;
            Stream s;
            lock (_seats)
            {
                if (!_seats.TryGetValue(seatName, out seat) || !seat.Connected) return false;
                s = seat.Stream;
            }
            return WriteTo(seat, s, ClassWire.Command, BuildCommand(command, seatName));
        }

        /// <summary>Sends a command to every connected seat; returns how many it was written to.</summary>
        public int Broadcast(Dictionary<string, object> command)
        {
            byte[] frame = BuildCommand(command);
            var targets = new List<KeyValuePair<ClassSeatInfo, Stream>>();
            lock (_seats)
                foreach (ClassSeatInfo seat in _seats.Values)
                    if (seat.Connected) targets.Add(new KeyValuePair<ClassSeatInfo, Stream>(seat, seat.Stream));
            int n = 0;
            foreach (KeyValuePair<ClassSeatInfo, Stream> t in targets)
                if (WriteTo(t.Key, t.Value, ClassWire.Command, frame)) n++;
            return n;
        }

        public void Dispose()
        {
            _open = false;
            try { _listener?.Stop(); } catch (SocketException) { }
            try { _beacon?.Close(); } catch (SocketException) { }
            lock (_seats)
                foreach (ClassSeatInfo seat in _seats.Values)
                {
                    try { seat.Client?.Close(); } catch (SocketException) { }
                    seat.Connected = false;
                }
        }
    }

    /// <summary>
    /// A seat's end of the class link. It proves it knows the class code, checks that the teacher's PC knows it too,
    /// and from then on obeys only commands signed with the teacher's key, which it can be told to expect
    /// (<c>pinnedKey</c>: the fingerprint it remembered for this class).
    /// </summary>
    public sealed class ClassSeat : IDisposable
    {
        private TcpClient _client;
        private Stream _stream;
        private ClassKey _hostKey;
        private readonly object _writeLock = new object();
        private volatile bool _open;
        private readonly ConcurrentQueue<Dictionary<string, object>> _commands = new ConcurrentQueue<Dictionary<string, object>>();
        private readonly ConcurrentQueue<string> _acks = new ConcurrentQueue<string>();
        /// <summary>The highest command number obeyed in each of the teacher's sessions, for as long as the program runs.</summary>
        private static readonly Dictionary<string, long> Seen = new Dictionary<string, long>();

        public string SeatName { get; private set; } = "";
        public string ClassName { get; private set; } = "";
        public string Session { get; private set; } = "";
        /// <summary>The fingerprint of the teacher's key, to be remembered for this class.</summary>
        public string KeyFingerprint { get; private set; } = "";
        public bool Connected => _open;
        /// <summary>Commands refused because their signature, session, addressee or number was wrong.</summary>
        public int Refused { get; private set; }

        /// <summary>
        /// Joins a class. Throws <see cref="UnauthorizedAccessException"/> with "code" when the code is wrong (on
        /// either side) and with "key" when the teacher's key is not the one remembered for this class.
        /// </summary>
        public void Connect(string host, int port, string seatName, string code, string user = "", string pinnedKey = null, int timeoutMs = 5000)
        {
            SeatName = seatName;
            _client = new TcpClient { NoDelay = true, SendTimeout = 5000, ReceiveTimeout = timeoutMs };
            IAsyncResult pending = _client.BeginConnect(host, port, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                _client.Close();
                throw new IOException("no answer from " + host + ":" + port);
            }
            _client.EndConnect(pending);
            _stream = _client.GetStream();
            string seatNonce = ClassWire.Nonce();
            ClassWire.WriteJson(_stream, new Dictionary<string, object> { ["t"] = "hello", ["seat"] = seatName, ["nonce"] = seatNonce, ["v"] = 1.0 });
            Dictionary<string, object> welcome = ClassWire.ReadJson(_stream);
            string hostNonce = Json.Str(welcome, "nonce", ""), pub = Json.Str(welcome, "key", "");
            Session = Json.Str(welcome, "session", "");
            if (Json.Str(welcome, "t", "") != "welcome" || Json.Str(welcome, "proof", "") != ClassWire.Proof(code, "host", seatNonce, hostNonce, pub + "|" + Session))
            {
                _client.Close();
                throw new UnauthorizedAccessException("code");
            }
            KeyFingerprint = ClassKey.FingerprintOf(pub);
            if (!string.IsNullOrEmpty(pinnedKey) && pinnedKey != KeyFingerprint)
            {
                _client.Close();
                throw new UnauthorizedAccessException("key");
            }
            _hostKey = ClassKey.Import(pub);
            ClassName = Json.Str(welcome, "class", "");
            ClassWire.WriteJson(_stream, new Dictionary<string, object>
            {
                ["t"] = "join", ["user"] = user ?? "", ["proof"] = ClassWire.Proof(code, "seat", seatNonce, hostNonce, seatName),
            });
            Dictionary<string, object> answer = ClassWire.ReadJson(_stream);
            if (Json.Str(answer, "t", "") != "ok")
            {
                _client.Close();
                throw new UnauthorizedAccessException("code");
            }
            _client.ReceiveTimeout = 0;
            _open = true;
            new Thread(ReadLoop) { IsBackground = true, Name = "Class link: teacher" }.Start();
        }

        private void ReadLoop()
        {
            try
            {
                while (_open && ClassWire.Read(_stream, out byte kind, out byte[] payload)) HandleFrame(kind, payload);
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidDataException) { }
            catch (FormatException) { }
            _open = false;
        }

        /// <summary>
        /// Takes one frame from the teacher's PC (the reader thread calls this). A command is obeyed only when it is
        /// signed with the teacher's key, belongs to this session, is addressed to this seat or to all, and has a
        /// higher number than every command obeyed before it.
        /// </summary>
        public bool HandleFrame(byte kind, byte[] payload)
        {
            if (kind == ClassWire.Json)
            {
                Dictionary<string, object> m = Json.ParseObject(Encoding.UTF8.GetString(payload));
                if (Json.Str(m, "t", "") == "got") _acks.Enqueue(Json.Str(m, "hash", ""));
                return true;
            }
            if (kind != ClassWire.Command || payload.Length < 2) return false;
            int sigLength = payload[0] | payload[1] << 8;
            if (sigLength <= 0 || 2 + sigLength >= payload.Length) { Refused++; return false; }
            var sig = new byte[sigLength];
            var json = new byte[payload.Length - 2 - sigLength];
            Buffer.BlockCopy(payload, 2, sig, 0, sigLength);
            Buffer.BlockCopy(payload, 2 + sigLength, json, 0, json.Length);
            if (_hostKey == null || !_hostKey.Verify(json, sig)) { Refused++; return false; }
            Dictionary<string, object> c = Json.ParseObject(Encoding.UTF8.GetString(json));
            string to = Json.Str(c, "to", ""), session = Json.Str(c, "session", "");
            long n = (long)Json.Num(c, "n", 0);
            if (session != Session || (to != "*" && to != SeatName)) { Refused++; return false; }
            lock (Seen)
            {
                // By seat as well as by session: several seats may live in one program (simulated seats).
                string key = SeatName + "|" + session;
                Seen.TryGetValue(key, out long last);
                if (n <= last) { Refused++; return false; }
                Seen[key] = n;
            }
            _commands.Enqueue(c);
            return true;
        }

        /// <summary>The next command from the teacher, if any.</summary>
        public bool Poll(out Dictionary<string, object> command) => _commands.TryDequeue(out command);

        /// <summary>The hash of a recording the teacher's PC has confirmed it stored, if any.</summary>
        public bool PollStored(out string replayHash) => _acks.TryDequeue(out replayHash);

        private bool Write(byte kind, byte[] payload)
        {
            if (!_open) return false;
            try
            {
                lock (_writeLock) ClassWire.Write(_stream, kind, payload);
                return true;
            }
            catch (IOException) { _open = false; return false; }
            catch (SocketException) { _open = false; return false; }
            catch (ObjectDisposedException) { _open = false; return false; }
        }

        public bool SendStatus(Dictionary<string, object> status)
        {
            var m = new Dictionary<string, object>(status) { ["t"] = "status" };
            return Write(ClassWire.Json, Encoding.UTF8.GetBytes(Json.Write(m)));
        }

        public bool RaiseHand(bool up) => Write(ClassWire.Json, Encoding.UTF8.GetBytes(Json.Write(new Dictionary<string, object> { ["t"] = "hand", ["up"] = up })));

        /// <summary>A small picture of the simulator's own view (never of the desktop), as an encoded image.</summary>
        public bool SendPicture(byte[] image) => Write(ClassWire.Picture, image);

        /// <summary>A finished attempt: its record and its recording. Keep the copy until <see cref="PollStored"/> names its hash.</summary>
        public bool SendAttempt(string recordJson, byte[] replay) => Write(ClassWire.Attempt, ClassWire.PackAttempt(recordJson, replay));

        public void Dispose()
        {
            _open = false;
            try { _client?.Close(); } catch (SocketException) { }
            _hostKey?.Dispose();
        }
    }

    /// <summary>A class found on the LAN.</summary>
    public sealed class FoundClass
    {
        public string Name = "", Address = "", KeyFingerprint = "";
        public int Port;
        public DateTime Heard;
    }

    /// <summary>Listens for the classes that announce themselves on the LAN, so a seat can join without typing an address.</summary>
    public sealed class ClassFinder : IDisposable
    {
        private readonly UdpClient _udp;
        private volatile bool _open = true;
        private readonly Dictionary<string, FoundClass> _found = new Dictionary<string, FoundClass>();

        public ClassFinder(int beaconPort = ClassWire.DefaultBeaconPort)
        {
            _udp = new UdpClient();
            // Several programs on one PC may listen (a teacher's console and simulated seats beside it).
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, beaconPort));
            new Thread(Listen) { IsBackground = true, Name = "Class link: finder" }.Start();
        }

        private void Listen()
        {
            while (_open)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref from);
                    if (data.Length > 2000) continue;
                    Dictionary<string, object> m = Json.ParseObject(Encoding.UTF8.GetString(data));
                    if (!m.ContainsKey("ushqyr_class")) continue;
                    var c = new FoundClass
                    {
                        Name = Json.Str(m, "ushqyr_class", ""), Address = from.Address.ToString(), Port = (int)Json.Num(m, "port", 0),
                        KeyFingerprint = Json.Str(m, "key", ""), Heard = DateTime.UtcNow,
                    };
                    if (c.Port <= 0 || c.Port > 65535) continue;
                    lock (_found) _found[c.Address + ":" + c.Port] = c;
                }
                catch (SocketException) { if (!_open) return; }
                catch (ObjectDisposedException) { return; }
                catch (FormatException) { /* not ours */ }
            }
        }

        /// <summary>The classes heard in the last few seconds, by name.</summary>
        public List<FoundClass> Classes(double withinSeconds = 5)
        {
            var list = new List<FoundClass>();
            lock (_found)
                foreach (FoundClass c in _found.Values)
                    if ((DateTime.UtcNow - c.Heard).TotalSeconds <= withinSeconds) list.Add(c);
            list.Sort((a, b) => EduStore.CompareNames(a.Name, b.Name));
            return list;
        }

        public void Dispose()
        {
            _open = false;
            try { _udp.Close(); } catch (SocketException) { }
        }
    }
}
