using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The TCP side of the bench API: accepts one program at a time, reads its frames on a background thread and hands
    /// decoded messages to the main thread. Everything that arrives is treated as hostile: frames have a size limit,
    /// headers are decoded by a bounded reader, a malformed frame or a flood closes the connection, and nothing here can
    /// throw into the simulation.
    ///
    /// Frame, both directions: u32 length (little-endian, counts the rest) | u8 type | u32 header length |
    /// header (MessagePack map) | payload (raw bytes).
    /// </summary>
    public sealed class BenchApiServer : IDisposable
    {
        public struct Inbound
        {
            public byte Type;
            public Dictionary<string, object> Header;
            /// <summary>Which connection it came from; messages of a closed connection are ignored.</summary>
            public int Connection;
        }

        private const int MaxQueued = 64;

        private readonly TcpListener _listener;
        private readonly Thread _thread;
        private readonly object _queueLock = new object();
        private readonly Queue<Inbound> _queue = new Queue<Inbound>();
        private readonly object _sendLock = new object();
        private readonly byte[] _prefix = new byte[9];
        private volatile bool _stop;
        private volatile Socket _client;
        private volatile int _connection;

        public int Port { get; }
        /// <summary>Increases by one with every accepted connection.</summary>
        public int Connection => _connection;
        public bool Connected => _client != null;
        /// <summary>Why the last connection was closed by the simulator (for the log), or null.</summary>
        public volatile string LastCloseReason;

        public BenchApiServer(int port, bool loopbackOnly)
        {
            _listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start(1);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(Run) { IsBackground = true, Name = "BenchApi" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            try { _listener.Stop(); } catch (Exception) { }
            Close("simulator closed");
        }

        /// <summary>Closes the current connection (if any).</summary>
        public void Close(string reason)
        {
            Socket c = _client;
            _client = null;
            if (c == null) return;
            LastCloseReason = reason;
            try { c.Close(); } catch (Exception) { }
        }

        public bool TryDequeue(out Inbound message)
        {
            lock (_queueLock)
            {
                if (_queue.Count > 0)
                {
                    message = _queue.Dequeue();
                    return true;
                }
            }
            message = default;
            return false;
        }

        /// <summary>Waits up to <paramref name="milliseconds"/> for a message.</summary>
        public bool WaitDequeue(out Inbound message, int milliseconds)
        {
            lock (_queueLock)
            {
                // Monitor.Wait gives the lock up while waiting and is woken by the reader thread's Pulse.
                if (_queue.Count == 0 && milliseconds > 0) Monitor.Wait(_queueLock, milliseconds);
                if (_queue.Count > 0)
                {
                    message = _queue.Dequeue();
                    return true;
                }
            }
            message = default;
            return false;
        }

        /// <summary>
        /// Sends one frame to the program. Returns false (and closes the connection) when it can't be delivered.
        /// </summary>
        public bool Send(int connection, byte type, MsgPackWriter header, byte[] payloadA = null, int lengthA = 0, byte[] payloadB = null, int lengthB = 0)
        {
            Socket c = _client;
            if (c == null || connection != _connection) return false;
            ArraySegment<byte> h = header.Segment;
            lock (_sendLock)
            {
                try
                {
                    uint total = (uint)(5 + h.Count + lengthA + lengthB);
                    WriteU32(_prefix, 0, total);
                    _prefix[4] = type;
                    WriteU32(_prefix, 5, (uint)h.Count);
                    SendAll(c, _prefix, 9);
                    SendAll(c, h.Array, h.Count);
                    if (lengthA > 0) SendAll(c, payloadA, lengthA);
                    if (lengthB > 0) SendAll(c, payloadB, lengthB);
                    return true;
                }
                catch (Exception)
                {
                    if (connection == _connection) Close("send failed");
                    return false;
                }
            }
        }

        private static void SendAll(Socket s, byte[] data, int count)
        {
            int sent = 0;
            while (sent < count) sent += s.Send(data, sent, count - sent, SocketFlags.None);
        }

        private static void WriteU32(byte[] b, int at, uint v)
        {
            b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24);
        }

        private static uint ReadU32(byte[] b, int at) => (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));

        private void Run()
        {
            var buffer = new byte[BenchProtocol.MaxInboundFrame];
            var head = new byte[4];
            while (!_stop)
            {
                Socket s;
                try { s = _listener.AcceptSocket(); }
                catch (Exception) { if (_stop) return; Thread.Sleep(50); continue; }

                // One program at a time: a new connection replaces the old one.
                Close("replaced by a new connection");
                s.NoDelay = true;
                s.SendTimeout = 3000;
                s.SendBufferSize = 4 * 1024 * 1024;
                int id = _connection + 1;
                lock (_queueLock) _queue.Clear();
                _connection = id;
                _client = s;

                string reason = "the program closed the connection";
                try
                {
                    while (!_stop)
                    {
                        if (!ReadAll(s, head, 4)) break;
                        uint length = ReadU32(head, 0);
                        if (length < 5 || length > BenchProtocol.MaxInboundFrame) { reason = "frame size out of range"; break; }
                        if (!ReadAll(s, buffer, (int)length)) break;
                        byte type = buffer[0];
                        uint headerLength = ReadU32(buffer, 1);
                        if (headerLength > length - 5) { reason = "header longer than the frame"; break; }
                        if (!(MsgPackReader.Decode(buffer, 5, (int)headerLength) is Dictionary<string, object> header))
                        {
                            reason = "header is not a map";
                            break;
                        }

                        if (type == BenchProtocol.Ping)
                        {
                            // Answered right here: a transport measurement that doesn't wait for the game loop.
                            // It is refused by the session's rules in competition mode (see PingAllowed).
                            if (!PingAllowed) { reason = "message not allowed"; break; }
                            int size = (int)Math.Clamp(BenchMessages.Long(header, "payload", 0), 0, BenchProtocol.MaxPingPayload);
                            byte[] payload = size > 0 ? new byte[size] : null;
                            var w = new MsgPackWriter();
                            w.Map(1).Key("size").Int(size);
                            Send(id, BenchProtocol.Pong, w, payload, size);
                            continue;
                        }

                        lock (_queueLock)
                        {
                            if (_queue.Count >= MaxQueued) { reason = "too many messages waiting"; break; }
                            _queue.Enqueue(new Inbound { Type = type, Header = header, Connection = id });
                            Monitor.Pulse(_queueLock);
                        }
                    }
                }
                catch (FormatException e) { reason = "malformed message: " + e.Message; }
                catch (Exception) { reason = "connection lost"; }

                if (_connection == id && _client == s) Close(reason);
            }
        }

        /// <summary>Set by the session: pings are a training-mode tool.</summary>
        public volatile bool PingAllowed;

        private static bool ReadAll(Socket s, byte[] into, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Receive(into, got, count - got, SocketFlags.None);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }
    }

    /// <summary>Safe readers for decoded headers: a wrong type gives the fallback, never an exception.</summary>
    public static class BenchMessages
    {
        public static long Long(Dictionary<string, object> h, string key, long fallback)
        {
            if (h != null && h.TryGetValue(key, out object v))
            {
                if (v is long l) return l;
                if (v is double d && !double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < 9e15) return (long)d;
            }
            return fallback;
        }

        public static string Str(Dictionary<string, object> h, string key, string fallback) =>
            h != null && h.TryGetValue(key, out object v) && v is string s ? s : fallback;

        public static bool Bool(Dictionary<string, object> h, string key, bool fallback) =>
            h != null && h.TryGetValue(key, out object v) && v is bool b ? b : fallback;

        /// <summary>
        /// RC channels from a header: a list of numbers. Returns null when the list is missing or holds anything that is
        /// not a finite number (the packet is then rejected as a whole).
        /// </summary>
        public static bool Channels(Dictionary<string, object> h, string key, List<int> into)
        {
            into.Clear();
            if (h == null || !h.TryGetValue(key, out object v) || !(v is List<object> list) || list.Count > 32) return false;
            foreach (object o in list)
            {
                long us;
                if (o is long l) us = l;
                else if (o is double d && !double.IsNaN(d) && !double.IsInfinity(d)) us = (long)Math.Round(Math.Clamp(d, 0.0, 5000.0));
                else return false;
                into.Add(RcLink.Clamp(us));
            }
            return true;
        }
    }
}
