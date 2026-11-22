using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ushqyr.Edu
{
    public sealed class EduUser
    {
        public long Id;
        public string Name, Role, Language;
        public bool HasPin;
    }

    public sealed class EduGroup
    {
        public long Id;
        public string Name;
        public int Members;
    }

    /// <summary>One graded try at an activity, as the journal shows it (EDU_PLAN F1).</summary>
    public sealed class AttemptRecord
    {
        public long Id, UserId;
        public string ActivityId, ActivityHash, Started, Build, Program, Comment;
        public double Duration, Score;
        public double? ManualScore;
        public bool Passed;
        public int Stars;
        /// <summary>Measured values per rubric line, as JSON.</summary>
        public string Metrics;
        /// <summary>What was unusual about the attempt (teacher changed the weather, a failure was injected, paused), as JSON.</summary>
        public string Flags;
        /// <summary>The recording file, relative to the replay folder; empty when none was kept.</summary>
        public string ReplayPath;
        public string ReplayHash;
        public long ReplayBytes;

        /// <summary>The grade that counts: the teacher's, when one was given.</summary>
        public double Final => ManualScore ?? Score;
    }

    /// <summary>
    /// Students, groups and attempts on top of <see cref="EduDatabase"/>. Everything stays in the one local file and the
    /// replay folder next to it; nothing here opens a network connection.
    /// </summary>
    public sealed class EduStore
    {
        private readonly EduDatabase _db;
        private readonly string _replayFolder;

        public EduStore(EduDatabase db, string replayFolder)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _replayFolder = replayFolder ?? throw new ArgumentNullException(nameof(replayFolder));
        }

        /// <summary>The Kazakh Cyrillic alphabet in its own order; it contains the Russian one.</summary>
        private const string Alphabet = "АӘБВГҒДЕЁЖЗИЙКҚЛМНҢОӨПРСТУҰҮФХҺЦЧШЩЪЫІЬЭЮЯ";

        /// <summary>
        /// Alphabetical order for names in Kazakh, Russian and English: digits and Latin letters first, then Cyrillic
        /// in the order of the Kazakh alphabet (Ә after А, Қ after К, …), case ignored. It is spelled out here
        /// because neither SQLite (character codes) nor the runtime's own comparison (it differs between .NET and
        /// Unity) puts the Kazakh letters in their places.
        /// </summary>
        public static int CompareNames(string a, string b)
        {
            a = a ?? "";
            b = b ?? "";
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int ra = Rank(a[i]), rb = Rank(b[i]);
                if (ra != rb) return ra < rb ? -1 : 1;
            }
            return a.Length.CompareTo(b.Length);
        }

        private static int Rank(char c)
        {
            c = char.ToUpperInvariant(c);
            int at = Alphabet.IndexOf(c);
            return at >= 0 ? 0x400 + at : c;
        }

        private static int ByName(string a, long aId, string b, long bId)
        {
            int c = CompareNames(a, b);
            return c != 0 ? c : aId.CompareTo(bId);
        }

        private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        private long LastId() => (long)_db.Sql.Query("select last_insert_rowid()")[0][0];

        // ------------------------------------------------------------ users

        public long AddUser(string name, string role = "student", string pin = null)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException("a name is needed");
            if (name.Length > 80) throw new ArgumentException("the name is longer than 80 characters");
            if (role != "student" && role != "teacher" && role != "seat") throw new ArgumentException("the role must be student, teacher or seat");
            _db.Sql.Execute("insert into users(name, role, created) values(?1, ?2, ?3)", name, role, Now());
            long id = LastId();
            if (!string.IsNullOrEmpty(pin)) SetPin(id, pin);
            return id;
        }

        /// <summary>
        /// The profile of a seat (EDU_PLAN F5, "seat name only"): results are kept under the name of the place, such as
        /// "PC-07", and no person is named at all. Made the first time it is asked for.
        /// </summary>
        public long SeatUser(string seatName)
        {
            seatName = (seatName ?? "").Trim();
            if (seatName.Length == 0) seatName = "Seat";
            if (seatName.Length > 80) seatName = seatName.Substring(0, 80);
            List<object[]> rows = _db.Sql.Query("select id, name from users where role = 'seat' order by id");
            foreach (object[] r in rows)
                if (string.Equals((string)r[1], seatName, StringComparison.Ordinal)) return (long)r[0];
            return AddUser(seatName, "seat");
        }

        /// <summary>One user, or null when there is none with this id (deleted, or a database from another PC).</summary>
        public EduUser User(long id)
        {
            List<object[]> rows = _db.Sql.Query("select id, name, role, language, pin_hash is not null from users where id = ?1", id);
            if (rows.Count == 0) return null;
            object[] r = rows[0];
            return new EduUser { Id = (long)r[0], Name = (string)r[1], Role = (string)r[2], Language = (string)r[3], HasPin = (long)r[4] != 0 };
        }

        public List<EduUser> Users(long? groupId = null)
        {
            List<object[]> rows = groupId == null
                ? _db.Sql.Query("select id, name, role, language, pin_hash is not null from users order by id")
                : _db.Sql.Query("select u.id, u.name, u.role, u.language, u.pin_hash is not null from users u join group_members m on m.user_id = u.id" +
                                " where m.group_id = ?1 order by u.id", groupId.Value);
            var list = new List<EduUser>();
            foreach (object[] r in rows)
                list.Add(new EduUser { Id = (long)r[0], Name = (string)r[1], Role = (string)r[2], Language = (string)r[3], HasPin = (long)r[4] != 0 });
            list.Sort((a, b) => ByName(a.Name, a.Id, b.Name, b.Id));
            return list;
        }

        /// <summary>
        /// Sets or clears (null) the PIN. Only a salted, slowed hash is stored. A PIN keeps classmates out of each
        /// other's profiles; it is not strong protection, and the teacher can always reset it.
        /// </summary>
        public void SetPin(long userId, string pin)
        {
            if (string.IsNullOrEmpty(pin))
            {
                _db.Sql.Execute("update users set pin_salt = null, pin_hash = null where id = ?1", userId);
                return;
            }
            if (pin.Length < 4 || pin.Length > 12) throw new ArgumentException("a PIN has 4 to 12 characters");
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            _db.Sql.Execute("update users set pin_salt = ?2, pin_hash = ?3 where id = ?1", userId, Convert.ToBase64String(salt), PinHash(pin, salt));
        }

        /// <summary>True when the PIN is right, or when the user has none.</summary>
        public bool CheckPin(long userId, string pin)
        {
            List<object[]> rows = _db.Sql.Query("select pin_salt, pin_hash from users where id = ?1", userId);
            if (rows.Count == 0) return false;
            if (rows[0][1] == null) return true;
            string expected = (string)rows[0][1], got = PinHash(pin ?? "", Convert.FromBase64String((string)rows[0][0]));
            int diff = expected.Length ^ got.Length;
            for (int i = 0; i < expected.Length && i < got.Length; i++) diff |= expected[i] ^ got[i];
            return diff == 0;
        }

        private static string PinHash(string pin, byte[] salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(pin), salt, 20000, HashAlgorithmName.SHA256))
                return Convert.ToBase64String(kdf.GetBytes(32));
        }

        // ------------------------------------------------------------ the teacher

        /// <summary>True when a teacher's password has been set on this PC.</summary>
        public bool HasTeacherPassword => !string.IsNullOrEmpty(_db.Setting("teacher_hash"));

        /// <summary>
        /// Sets the password of the teacher's console (4 to 64 characters). Like a PIN, only a salted, slowed hash is
        /// kept. It keeps students out of the journal on a shared PC; whoever can replace the database file can still
        /// get round it, and the school's own Windows accounts are the real protection.
        /// </summary>
        public void SetTeacherPassword(string password)
        {
            if (password == null || password.Length < 4 || password.Length > 64) throw new ArgumentException("the teacher's password has 4 to 64 characters");
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            _db.SetSetting("teacher_salt", Convert.ToBase64String(salt));
            _db.SetSetting("teacher_hash", PinHash(password, salt));
        }

        /// <summary>True when the password is the teacher's. False when none has been set.</summary>
        public bool CheckTeacherPassword(string password)
        {
            string expected = _db.Setting("teacher_hash"), salt = _db.Setting("teacher_salt");
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(salt)) return false;
            string got = PinHash(password ?? "", Convert.FromBase64String(salt));
            int diff = expected.Length ^ got.Length;
            for (int i = 0; i < expected.Length && i < got.Length; i++) diff |= expected[i] ^ got[i];
            return diff == 0;
        }

        /// <summary>The school's name, printed at the top of the reports; empty when not set.</summary>
        public string SchoolName
        {
            get => _db.Setting("school_name") ?? "";
            set => _db.SetSetting("school_name", (value ?? "").Trim());
        }

        // ------------------------------------------------------------ groups

        public long AddGroup(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException("a group name is needed");
            // SQLite folds case for Latin letters only, so "7Б" and "7б" are compared here.
            foreach (EduGroup g in Groups())
                if (string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("a group with this name already exists");
            _db.Sql.Execute("insert into groups(name, created) values(?1, ?2)", name, Now());
            return LastId();
        }

        public void AddToGroup(long groupId, long userId) =>
            _db.Sql.Execute("insert or ignore into group_members(group_id, user_id) values(?1, ?2)", groupId, userId);

        public void RemoveFromGroup(long groupId, long userId) =>
            _db.Sql.Execute("delete from group_members where group_id = ?1 and user_id = ?2", groupId, userId);

        public List<EduGroup> Groups()
        {
            var list = new List<EduGroup>();
            foreach (object[] r in _db.Sql.Query("select g.id, g.name, (select count(*) from group_members m where m.group_id = g.id) from groups g order by g.id"))
                list.Add(new EduGroup { Id = (long)r[0], Name = (string)r[1], Members = (int)(long)r[2] });
            list.Sort((a, b) => ByName(a.Name, a.Id, b.Name, b.Id));
            return list;
        }

        /// <summary>
        /// Adds a class list: one student per line; a line may be a row of a CSV file, in which case its first cell is
        /// the name. Names already in the group are skipped. Returns how many students were added.
        /// </summary>
        public int ImportList(long groupId, string text)
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EduUser u in Users(groupId)) have.Add(u.Name);
            int added = 0;
            _db.Sql.Execute("begin");
            try
            {
                foreach (string raw in (text ?? "").Split('\n'))
                {
                    string line = raw.Trim().TrimStart('﻿');
                    if (line.Length == 0) continue;
                    string name = FirstCell(line).Trim();
                    if (name.Length == 0 || name.Length > 80 || !have.Add(name)) continue;
                    AddToGroup(groupId, AddUser(name));
                    added++;
                }
                _db.Sql.Execute("commit");
            }
            catch
            {
                _db.Sql.Execute("rollback");
                throw;
            }
            return added;
        }

        private static string FirstCell(string line)
        {
            if (line[0] == '"')
            {
                var sb = new StringBuilder();
                for (int i = 1; i < line.Length; i++)
                {
                    if (line[i] != '"') sb.Append(line[i]);
                    else if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else break;
                }
                return sb.ToString();
            }
            int cut = line.IndexOfAny(new[] { ';', ',', '\t' });
            return cut < 0 ? line : line.Substring(0, cut);
        }

        // ------------------------------------------------------------ attempts

        public long SaveAttempt(AttemptRecord a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (string.IsNullOrEmpty(a.ActivityId) || string.IsNullOrEmpty(a.ActivityHash)) throw new ArgumentException("an attempt names its activity and the activity's hash");
            _db.Sql.Execute("begin");
            try
            {
                object replay = null;
                if (!string.IsNullOrEmpty(a.ReplayPath))
                {
                    _db.Sql.Execute("insert into replays(path, hash, bytes) values(?1, ?2, ?3)", a.ReplayPath, a.ReplayHash ?? "", a.ReplayBytes);
                    replay = LastId();
                }
                _db.Sql.Execute(
                    "insert into attempts(user_id, activity_id, activity_hash, started, duration, score, passed, stars, metrics, flags, program, replay_id, build)" +
                    " values(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13)",
                    a.UserId, a.ActivityId, a.ActivityHash, a.Started ?? Now(), a.Duration, a.Score, a.Passed, a.Stars, a.Metrics, a.Flags, a.Program, replay, a.Build);
                a.Id = LastId();
                _db.Sql.Execute("commit");
            }
            catch
            {
                _db.Sql.Execute("rollback");
                throw;
            }
            return a.Id;
        }

        /// <summary>Attempts of a student, newest first; of one activity when <paramref name="activityId"/> is given.</summary>
        public List<AttemptRecord> Attempts(long userId, string activityId = null)
        {
            const string cols = "select a.id, a.user_id, a.activity_id, a.activity_hash, a.started, a.duration, a.score, a.passed, a.stars, a.metrics, a.flags," +
                                " a.program, a.manual_score, a.comment, a.build, r.path, r.hash, r.bytes from attempts a left join replays r on r.id = a.replay_id ";
            List<object[]> rows = activityId == null
                ? _db.Sql.Query(cols + "where a.user_id = ?1 order by a.id desc", userId)
                : _db.Sql.Query(cols + "where a.user_id = ?1 and a.activity_id = ?2 order by a.id desc", userId, activityId);
            var list = new List<AttemptRecord>();
            foreach (object[] r in rows)
                list.Add(new AttemptRecord
                {
                    Id = (long)r[0], UserId = (long)r[1], ActivityId = (string)r[2], ActivityHash = (string)r[3], Started = (string)r[4],
                    Duration = Convert.ToDouble(r[5]), Score = Convert.ToDouble(r[6]), Passed = (long)r[7] != 0, Stars = (int)(long)r[8],
                    Metrics = (string)r[9], Flags = (string)r[10], Program = (string)r[11],
                    ManualScore = r[12] == null ? (double?)null : Convert.ToDouble(r[12]), Comment = (string)r[13], Build = (string)r[14],
                    ReplayPath = (string)r[15], ReplayHash = (string)r[16], ReplayBytes = r[17] == null ? 0 : (long)r[17],
                });
            return list;
        }

        /// <summary>The best grade that counts for this student and activity, or null when there is no attempt.</summary>
        public double? Best(long userId, string activityId)
        {
            object v = _db.Sql.Query("select max(coalesce(manual_score, score)) from attempts where user_id = ?1 and activity_id = ?2", userId, activityId)[0][0];
            return v == null ? (double?)null : Convert.ToDouble(v);
        }

        /// <summary>The teacher's own grade and comment for an attempt; null clears the grade.</summary>
        public void Grade(long attemptId, double? manualScore, string comment)
        {
            if (manualScore != null && (manualScore < 0 || manualScore > 100)) throw new ArgumentException("a grade is between 0 and 100");
            _db.Sql.Execute("update attempts set manual_score = ?2, comment = ?3 where id = ?1", attemptId, manualScore == null ? null : (object)manualScore.Value, comment);
        }

        // ------------------------------------------------------------ privacy, backup

        /// <summary>
        /// Deletes a student and everything recorded about them: the profile, group memberships, attempts with the
        /// programs they wrote, and the recording files. Returns the number of recording files removed.
        /// </summary>
        public int DeleteUser(long userId)
        {
            List<object[]> files = _db.Sql.Query("select r.id, r.path from replays r join attempts a on a.replay_id = r.id where a.user_id = ?1", userId);
            _db.Sql.Execute("begin");
            try
            {
                foreach (object[] f in files) _db.Sql.Execute("delete from replays where id = ?1", (long)f[0]);
                _db.Sql.Execute("delete from users where id = ?1", userId); // attempts and memberships go with it
                _db.Sql.Execute("commit");
            }
            catch
            {
                _db.Sql.Execute("rollback");
                throw;
            }
            int removed = 0;
            foreach (object[] f in files)
            {
                string path = ReplayFile((string)f[1]);
                if (path == null || !File.Exists(path)) continue;
                File.Delete(path);
                removed++;
            }
            return removed;
        }

        /// <summary>The full path of a recording; null when the stored name would point outside the replay folder.</summary>
        public string ReplayFile(string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) return null;
            string root = Path.GetFullPath(_replayFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, relative));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        /// <summary>Writes a complete copy of the database into a new file. The copy is consistent even while the class is flying.</summary>
        public void Backup(string toFile)
        {
            if (File.Exists(toFile)) throw new IOException("the backup file already exists: " + toFile);
            _db.Sql.Execute("vacuum into ?1", toFile);
        }

        /// <summary>
        /// Checks that a file is a class database this program can read, before it replaces the current one. The swap
        /// itself is a file copy done by the caller with the database closed.
        /// </summary>
        public static void CheckBackup(string file)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("no such backup file", file);
            using (var probe = new SqliteDb(file))
            {
                List<object[]> ok;
                try { ok = probe.Query("pragma integrity_check"); }
                catch (InvalidOperationException) { throw new InvalidDataException("this file is not a class database"); }
                if (ok.Count != 1 || (string)ok[0][0] != "ok") throw new InvalidDataException("the backup file is damaged");
                long version = (long)probe.Query("pragma user_version")[0][0];
                if (version < 1) throw new InvalidDataException("this file is not a class database");
                if (version > EduDatabase.Steps.Length) throw new InvalidDataException("the backup was made by a newer version of the program");
                if (probe.Query("select 1 from sqlite_master where type = 'table' and name = 'attempts'").Count == 0)
                    throw new InvalidDataException("this file is not a class database");
            }
        }
    }
}
