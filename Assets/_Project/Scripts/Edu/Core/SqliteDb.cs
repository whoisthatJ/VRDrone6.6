using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>
    /// A thin wrapper over the SQLite library of the operating system: `winsqlite3.dll`, part of Windows 10 and 11,
    /// and `libsqlite3.so.0` on Linux. Nothing is bundled. One database is one file.
    /// </summary>
    public sealed class SqliteDb : IDisposable
    {
        private IntPtr _db;
        private readonly bool _windows;

        public SqliteDb(string path)
        {
            _windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            byte[] name = Utf8(path);
            int rc = _windows ? Win.sqlite3_open_v2(name, out _db, 6, IntPtr.Zero) : Nix.sqlite3_open_v2(name, out _db, 6, IntPtr.Zero);
            if (rc != 0)
            {
                string why = _db != IntPtr.Zero ? Message() : "code " + rc;
                Dispose();
                throw new InvalidOperationException("cannot open the database '" + path + "': " + why);
            }
        }

        /// <summary>The version of the SQLite library in use.</summary>
        public string LibraryVersion => Marshal.PtrToStringAnsi(_windows ? Win.sqlite3_libversion() : Nix.sqlite3_libversion());

        /// <summary>Runs a statement that returns no rows. Parameters are bound to ?1, ?2, …</summary>
        public void Execute(string sql, params object[] args)
        {
            Query(sql, args);
        }

        /// <summary>Runs a statement and returns its rows; a cell is a long, a double, a string or null.</summary>
        public List<object[]> Query(string sql, params object[] args)
        {
            if (_db == IntPtr.Zero) throw new ObjectDisposedException(nameof(SqliteDb));
            var rows = new List<object[]>();
            IntPtr st;
            Check(_windows ? Win.sqlite3_prepare_v2(_db, Utf8(sql), -1, out st, IntPtr.Zero)
                           : Nix.sqlite3_prepare_v2(_db, Utf8(sql), -1, out st, IntPtr.Zero), sql);
            try
            {
                for (int i = 0; args != null && i < args.Length; i++)
                {
                    object a = args[i];
                    int n = i + 1, rc;
                    if (a == null) rc = _windows ? Win.sqlite3_bind_null(st, n) : Nix.sqlite3_bind_null(st, n);
                    else if (a is double || a is float)
                    {
                        double d = Convert.ToDouble(a);
                        rc = _windows ? Win.sqlite3_bind_double(st, n, d) : Nix.sqlite3_bind_double(st, n, d);
                    }
                    else if (a is int || a is long || a is bool || a is short || a is byte)
                    {
                        long l = Convert.ToInt64(a);
                        rc = _windows ? Win.sqlite3_bind_int64(st, n, l) : Nix.sqlite3_bind_int64(st, n, l);
                    }
                    else
                    {
                        byte[] text = Encoding.UTF8.GetBytes(a.ToString());
                        // -1 as the last argument: SQLite copies the bytes before the call returns.
                        rc = _windows ? Win.sqlite3_bind_text(st, n, text, text.Length, new IntPtr(-1))
                                      : Nix.sqlite3_bind_text(st, n, text, text.Length, new IntPtr(-1));
                    }
                    Check(rc, sql);
                }
                while (true)
                {
                    int rc = _windows ? Win.sqlite3_step(st) : Nix.sqlite3_step(st);
                    if (rc == 101) break; // done
                    if (rc != 100) Check(rc, sql);
                    int cols = _windows ? Win.sqlite3_column_count(st) : Nix.sqlite3_column_count(st);
                    var row = new object[cols];
                    for (int c = 0; c < cols; c++)
                    {
                        int type = _windows ? Win.sqlite3_column_type(st, c) : Nix.sqlite3_column_type(st, c);
                        if (type == 1) row[c] = _windows ? Win.sqlite3_column_int64(st, c) : Nix.sqlite3_column_int64(st, c);
                        else if (type == 2) row[c] = _windows ? Win.sqlite3_column_double(st, c) : Nix.sqlite3_column_double(st, c);
                        else if (type == 5) row[c] = null;
                        else
                        {
                            IntPtr p = _windows ? Win.sqlite3_column_text(st, c) : Nix.sqlite3_column_text(st, c);
                            int len = _windows ? Win.sqlite3_column_bytes(st, c) : Nix.sqlite3_column_bytes(st, c);
                            var bytes = new byte[len];
                            if (len > 0) Marshal.Copy(p, bytes, 0, len);
                            row[c] = Encoding.UTF8.GetString(bytes);
                        }
                    }
                    rows.Add(row);
                }
            }
            finally
            {
                if (_windows) Win.sqlite3_finalize(st); else Nix.sqlite3_finalize(st);
            }
            return rows;
        }

        public void Dispose()
        {
            if (_db == IntPtr.Zero) return;
            if (_windows) Win.sqlite3_close(_db); else Nix.sqlite3_close(_db);
            _db = IntPtr.Zero;
        }

        private void Check(int rc, string sql)
        {
            if (rc != 0) throw new InvalidOperationException("SQLite: " + Message() + " (" + rc + ") in: " + sql);
        }

        private string Message()
        {
            IntPtr p = _windows ? Win.sqlite3_errmsg(_db) : Nix.sqlite3_errmsg(_db);
            if (p == IntPtr.Zero) return "";
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            var bytes = new byte[len];
            Marshal.Copy(p, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }

        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

        private static class Win
        {
            private const string Lib = "winsqlite3";
            private const CallingConvention Cc = CallingConvention.StdCall;
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_open_v2(byte[] name, out IntPtr db, int flags, IntPtr vfs);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_close(IntPtr db);
            [DllImport(Lib, CallingConvention = Cc)] public static extern IntPtr sqlite3_libversion();
            [DllImport(Lib, CallingConvention = Cc)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr st, IntPtr tail);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_step(IntPtr st);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_finalize(IntPtr st);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_bind_null(IntPtr st, int i);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_bind_int64(IntPtr st, int i, long v);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_bind_double(IntPtr st, int i, double v);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_bind_text(IntPtr st, int i, byte[] v, int n, IntPtr free);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_column_count(IntPtr st);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_column_type(IntPtr st, int c);
            [DllImport(Lib, CallingConvention = Cc)] public static extern long sqlite3_column_int64(IntPtr st, int c);
            [DllImport(Lib, CallingConvention = Cc)] public static extern double sqlite3_column_double(IntPtr st, int c);
            [DllImport(Lib, CallingConvention = Cc)] public static extern IntPtr sqlite3_column_text(IntPtr st, int c);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_column_bytes(IntPtr st, int c);
        }

        private static class Nix
        {
            private const string Lib = "libsqlite3.so.0";
            private const CallingConvention Cc = CallingConvention.Cdecl;
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_open_v2(byte[] name, out IntPtr db, int flags, IntPtr vfs);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_close(IntPtr db);
            [DllImport(Lib, CallingConvention = Cc)] public static extern IntPtr sqlite3_libversion();
            [DllImport(Lib, CallingConvention = Cc)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr st, IntPtr tail);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_step(IntPtr st);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_finalize(IntPtr st);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_bind_null(IntPtr st, int i);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_bind_int64(IntPtr st, int i, long v);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_bind_double(IntPtr st, int i, double v);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_bind_text(IntPtr st, int i, byte[] v, int n, IntPtr free);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_column_count(IntPtr st);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_column_type(IntPtr st, int c);
            [DllImport(Lib, CallingConvention = Cc)] public static extern long sqlite3_column_int64(IntPtr st, int c);
            [DllImport(Lib, CallingConvention = Cc)] public static extern double sqlite3_column_double(IntPtr st, int c);
            [DllImport(Lib, CallingConvention = Cc)] public static extern IntPtr sqlite3_column_text(IntPtr st, int c);
            [DllImport(Lib, CallingConvention = Cc)] public static extern int sqlite3_column_bytes(IntPtr st, int c);
        }
    }
}
