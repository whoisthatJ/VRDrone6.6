using System;
using System.Collections.Generic;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The one database file of a classroom (EDU_PLAN F5): it lives on the teacher's PC, and a seat keeps a smaller copy
    /// of the same shape. Opening it brings an older file up to the current layout, one numbered step at a time; each
    /// step either happens completely or not at all, so a failed upgrade leaves the file as it was.
    /// </summary>
    public sealed class EduDatabase : IDisposable
    {
        /// <summary>
        /// Layout steps in order. Step N turns a file of version N into version N + 1. A released step is never
        /// edited: a change of layout is a new step at the end.
        /// </summary>
        public static readonly string[][] Steps =
        {
            new[]
            {
                "create table users(id integer primary key, name text not null, role text not null default 'student'," +
                " pin_salt text, pin_hash text, language text, created text not null)",
                "create table groups(id integer primary key, name text not null unique, created text not null)",
                "create table group_members(group_id integer not null references groups(id) on delete cascade," +
                " user_id integer not null references users(id) on delete cascade, primary key(group_id, user_id))",
                "create table seats(id integer primary key, name text not null unique, machine text, row integer, col integer)",
                "create table activities(id text primary key, hash text not null, type text not null, title text not null, json text not null)",
                "create table assignments(id integer primary key, activity_id text not null references activities(id)," +
                " group_id integer references groups(id) on delete cascade, user_id integer references users(id) on delete cascade," +
                " settings text, created text not null)",
                "create table replays(id integer primary key, path text not null, hash text not null, bytes integer not null)",
                "create table attempts(id integer primary key, user_id integer references users(id) on delete cascade," +
                " seat_id integer references seats(id) on delete set null, activity_id text not null, activity_hash text not null," +
                " started text not null, duration real not null, score real not null, passed integer not null, stars integer not null," +
                " metrics text, flags text, program text, replay_id integer references replays(id) on delete set null," +
                " manual_score real, comment text, build text)",
                "create index attempts_by_user on attempts(user_id, activity_id)",
                "create table settings(key text primary key, value text)",
            },
            // 2: lesson plans (an ordered list of activities under a name), and finding a student's assignments quickly.
            new[]
            {
                "create table lessons(id integer primary key, name text not null, activities text not null, created text not null)",
                "create index assignments_by_group on assignments(group_id)",
                "create index assignments_by_user on assignments(user_id)",
            },
        };

        private readonly SqliteDb _db;

        /// <summary>The layout version of the open file (equal to the number of steps applied).</summary>
        public int Version { get; private set; }

        public SqliteDb Sql => _db;

        public EduDatabase(string path) : this(path, Steps) { }

        /// <summary>Opens the file with a given list of steps; tests use this to try an upgrade and a failing step.</summary>
        public EduDatabase(string path, string[][] steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            _db = new SqliteDb(path);
            try
            {
                _db.Execute("pragma foreign_keys = on");
                Version = (int)(long)_db.Query("pragma user_version")[0][0];
                if (Version > steps.Length)
                    throw new InvalidOperationException("this database was made by a newer version of the program (layout " +
                                                        Version + ", this program knows " + steps.Length + ")");
                for (int v = Version; v < steps.Length; v++)
                {
                    _db.Execute("begin immediate");
                    try
                    {
                        foreach (string sql in steps[v]) _db.Execute(sql);
                        _db.Execute("pragma user_version = " + (v + 1));
                        _db.Execute("commit");
                        Version = v + 1;
                    }
                    catch (Exception e)
                    {
                        try { _db.Execute("rollback"); }
                        catch (InvalidOperationException) { }
                        throw new InvalidOperationException("the database could not be upgraded from layout " + v + " to " + (v + 1) +
                                                            "; it was left unchanged: " + e.Message, e);
                    }
                }
            }
            catch
            {
                _db.Dispose();
                throw;
            }
        }

        /// <summary>Reads a stored setting, or null.</summary>
        public string Setting(string key)
        {
            List<object[]> rows = _db.Query("select value from settings where key = ?1", key);
            return rows.Count > 0 ? (string)rows[0][0] : null;
        }

        public void SetSetting(string key, string value)
        {
            _db.Execute("insert into settings(key, value) values(?1, ?2) on conflict(key) do update set value = excluded.value", key, value);
        }

        public void Dispose() => _db.Dispose();
    }
}
