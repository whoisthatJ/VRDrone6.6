using System;
using System.Collections.Generic;
using System.Text;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The small part of MessagePack the bench protocol uses for message headers: nil, bool, integers, float32/64,
    /// strings, arrays and string-keyed maps. Values decode to object: null, bool, long, double, string,
    /// List&lt;object&gt;, Dictionary&lt;string, object&gt;. Input is untrusted: depth, sizes and bounds are checked and
    /// a malformed buffer throws <see cref="FormatException"/>, never anything else.
    /// </summary>
    public sealed class MsgPackWriter
    {
        private byte[] _buf = new byte[256];
        private int _len;

        public int Length => _len;
        public void Reset() => _len = 0;
        public ArraySegment<byte> Segment => new ArraySegment<byte>(_buf, 0, _len);

        private void Need(int n)
        {
            if (_len + n <= _buf.Length) return;
            int size = _buf.Length * 2;
            while (size < _len + n) size *= 2;
            System.Array.Resize(ref _buf, size);
        }

        private void Byte(byte b) { Need(1); _buf[_len++] = b; }

        private void BigEndian(ulong v, int bytes)
        {
            Need(bytes);
            for (int i = bytes - 1; i >= 0; i--) _buf[_len++] = (byte)(v >> (8 * i));
        }

        public MsgPackWriter Nil() { Byte(0xc0); return this; }
        public MsgPackWriter Bool(bool v) { Byte(v ? (byte)0xc3 : (byte)0xc2); return this; }

        public MsgPackWriter Int(long v)
        {
            if (v >= 0 && v <= 127) Byte((byte)v);
            else if (v < 0 && v >= -32) Byte((byte)(0xe0 | (v + 32)));
            else if (v >= int.MinValue && v <= int.MaxValue) { Byte(0xd2); BigEndian((uint)(int)v, 4); }
            else { Byte(0xd3); BigEndian((ulong)v, 8); }
            return this;
        }

        public MsgPackWriter Double(double v)
        {
            Byte(0xcb);
            BigEndian((ulong)BitConverter.DoubleToInt64Bits(v), 8);
            return this;
        }

        public MsgPackWriter Str(string s)
        {
            if (s == null) return Nil();
            int n = Encoding.UTF8.GetByteCount(s);
            if (n <= 31) Byte((byte)(0xa0 | n));
            else if (n <= 255) { Byte(0xd9); Byte((byte)n); }
            else if (n <= 65535) { Byte(0xda); BigEndian((ulong)n, 2); }
            else { Byte(0xdb); BigEndian((ulong)n, 4); }
            Need(n);
            Encoding.UTF8.GetBytes(s, 0, s.Length, _buf, _len);
            _len += n;
            return this;
        }

        public MsgPackWriter Array(int count)
        {
            if (count <= 15) Byte((byte)(0x90 | count));
            else { Byte(0xdc); BigEndian((ulong)count, 2); }
            return this;
        }

        public MsgPackWriter Map(int count)
        {
            if (count <= 15) Byte((byte)(0x80 | count));
            else { Byte(0xde); BigEndian((ulong)count, 2); }
            return this;
        }

        public MsgPackWriter Key(string k) => Str(k);
    }

    public static class MsgPackReader
    {
        public const int MaxDepth = 8;
        public const int MaxItems = 256;
        public const int MaxString = 4096;

        public static object Decode(byte[] buf, int offset, int count)
        {
            int pos = offset, end = offset + count;
            object v = Read(buf, ref pos, end, 0);
            if (pos != end) throw new FormatException("trailing bytes after the header");
            return v;
        }

        private static ulong Big(byte[] b, ref int pos, int end, int bytes)
        {
            if (end - pos < bytes) throw new FormatException("truncated value");
            ulong v = 0;
            for (int i = 0; i < bytes; i++) v = (v << 8) | b[pos++];
            return v;
        }

        private static object Read(byte[] b, ref int pos, int end, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("nesting too deep");
            if (pos >= end) throw new FormatException("truncated value");
            byte t = b[pos++];
            if (t <= 0x7f) return (long)t;
            if (t >= 0xe0) return (long)(sbyte)t;
            if (t >= 0xa0 && t <= 0xbf) return Str(b, ref pos, end, t & 0x1f);
            if (t >= 0x90 && t <= 0x9f) return Arr(b, ref pos, end, t & 0x0f, depth);
            if (t >= 0x80 && t <= 0x8f) return Map(b, ref pos, end, t & 0x0f, depth);
            switch (t)
            {
                case 0xc0: return null;
                case 0xc2: return false;
                case 0xc3: return true;
                case 0xca: return (double)BitConverter.Int32BitsToSingle((int)Big(b, ref pos, end, 4));
                case 0xcb: return BitConverter.Int64BitsToDouble((long)Big(b, ref pos, end, 8));
                case 0xcc: return (long)Big(b, ref pos, end, 1);
                case 0xcd: return (long)Big(b, ref pos, end, 2);
                case 0xce: return (long)Big(b, ref pos, end, 4);
                case 0xcf:
                {
                    ulong u = Big(b, ref pos, end, 8);
                    if (u > long.MaxValue) throw new FormatException("integer out of range");
                    return (long)u;
                }
                case 0xd0: return (long)(sbyte)Big(b, ref pos, end, 1);
                case 0xd1: return (long)(short)Big(b, ref pos, end, 2);
                case 0xd2: return (long)(int)Big(b, ref pos, end, 4);
                case 0xd3: return (long)Big(b, ref pos, end, 8);
                case 0xd9: return Str(b, ref pos, end, (long)Big(b, ref pos, end, 1));
                case 0xda: return Str(b, ref pos, end, (long)Big(b, ref pos, end, 2));
                case 0xdb: return Str(b, ref pos, end, (long)Big(b, ref pos, end, 4));
                case 0xdc: return Arr(b, ref pos, end, (long)Big(b, ref pos, end, 2), depth);
                case 0xdd: return Arr(b, ref pos, end, (long)Big(b, ref pos, end, 4), depth);
                case 0xde: return Map(b, ref pos, end, (long)Big(b, ref pos, end, 2), depth);
                case 0xdf: return Map(b, ref pos, end, (long)Big(b, ref pos, end, 4), depth);
                default: throw new FormatException("unsupported type 0x" + t.ToString("x2"));
            }
        }

        private static string Str(byte[] b, ref int pos, int end, long n)
        {
            if (n > MaxString) throw new FormatException("string too long");
            if (end - pos < n) throw new FormatException("truncated string");
            string s;
            try { s = new UTF8Encoding(false, true).GetString(b, pos, (int)n); }
            catch (ArgumentException) { throw new FormatException("invalid UTF-8"); }
            pos += (int)n;
            return s;
        }

        private static object Arr(byte[] b, ref int pos, int end, long n, int depth)
        {
            if (n > MaxItems) throw new FormatException("array too long");
            var list = new List<object>((int)n);
            for (long i = 0; i < n; i++) list.Add(Read(b, ref pos, end, depth + 1));
            return list;
        }

        private static object Map(byte[] b, ref int pos, int end, long n, int depth)
        {
            if (n > MaxItems) throw new FormatException("map too long");
            var map = new Dictionary<string, object>((int)n);
            for (long i = 0; i < n; i++)
            {
                if (!(Read(b, ref pos, end, depth + 1) is string key)) throw new FormatException("map key is not a string");
                map[key] = Read(b, ref pos, end, depth + 1);
            }
            return map;
        }
    }
}
