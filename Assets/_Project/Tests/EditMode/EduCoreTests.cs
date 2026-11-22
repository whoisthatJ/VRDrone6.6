using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Ushqyr.Edu;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>
    /// Education Suite foundations (EDU_PLAN F1, F2, F5 and the ED0 spikes): the database and its upgrades, activity
    /// files, scoring from a recorded flight, the recording file, and the XLSX, PDF, licence and MAVLink building blocks.
    /// </summary>
    public sealed class EduCoreTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ushqyr-edu-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); }
            catch (IOException) { }
        }

        /// <summary>
        /// The sample exercise "take off, hover, land" as these tests know it. It is kept here, word for word, so that
        /// the tests do not change when the shipped exercise is reworded or laid out differently.
        /// </summary>
        private const string HoverJson = @"{
  ""format"": 1, ""id"": ""a02-hover-land"", ""type"": ""exercise"",
  ""title"": ""Take off, hover, land"",
  ""briefing"": ""Take off and climb into the blue box, 2 m up. Stay inside it for 20 seconds. Then come down and land on the pad you started from."",
  ""pass"": 60, ""time_limit"": 90,
  ""setup"": {
    ""place"": ""field"", ""drone"": ""edu-camera900"", ""assist"": ""position"", ""view"": ""chase"",
    ""objects"": [
      {""type"": ""box"", ""id"": ""hover"", ""x"": 0, ""y"": 2, ""z"": 0, ""size"": 2, ""height"": 1},
      {""type"": ""pad"", ""id"": ""home"", ""x"": 0, ""y"": 0, ""z"": 0, ""size"": 1.5}
    ]
  },
  ""rubric"": [
    {""id"": ""hold"", ""metric"": ""hold_time"", ""label"": ""Time inside the box"", ""tip"": ""Climb until the drone is inside the blue box, then let go of the right stick and only correct small drifts."", ""weight"": 40, ""best"": 20, ""worst"": 0, ""required"": true,
     ""params"": {""x"": 0, ""z"": 0, ""radius"": 1, ""y"": 2, ""height_tolerance"": 0.5}},
    {""id"": ""height"", ""metric"": ""height_error"", ""label"": ""Keeping the height"", ""tip"": ""Watch the height while you hover: small, slow moves of the left stick."", ""weight"": 15, ""best"": 0.1, ""worst"": 0.8,
     ""params"": {""y"": 2, ""from"": 4, ""to"": 24}},
    {""id"": ""landing"", ""metric"": ""landing_distance"", ""label"": ""Landing on the pad"", ""tip"": ""Stop above the pad first, then come straight down."", ""weight"": 25, ""best"": 0.2, ""worst"": 1.5, ""required"": true,
     ""params"": {""x"": 0, ""z"": 0}},
    {""id"": ""touch"", ""metric"": ""touchdown_speed"", ""label"": ""A soft landing"", ""tip"": ""Slow the descent in the last metre."", ""weight"": 10, ""best"": 0.5, ""worst"": 2.5},
    {""id"": ""smooth"", ""metric"": ""smoothness"", ""label"": ""Smooth sticks"", ""tip"": ""Move the sticks slowly and a little at a time."", ""weight"": 10, ""best"": 1.5, ""worst"": 8}
  ]
}";

        /// <summary>A made-up flight of "take off, hover at 2 m, land": how much it wobbles, drifts, how fast it comes down, how nervous the sticks are.</summary>
        private static FlightRecording Hover(double wobble, double drift, double sink, double jitter, bool land = true)
        {
            var r = new FlightRecording();
            var rnd = new System.Random(3);
            double y = 0, x = 0;
            for (int i = 0; i < 3200; i++)
            {
                double t = i * 0.01;
                double target = t < 1 ? 0 : t < 26 ? 2 : 0;
                double rate = t < 26 ? 1.0 : sink;
                double vy = Math.Sign(target - y) * Math.Min(rate, Math.Abs(target - y) * 3);
                if (!land && t >= 26) vy = 0;
                y = Math.Max(0, y + vy * 0.01);
                if (t > 3 && t < 26) x = wobble * Math.Sin(t * 1.3) + drift * (t - 3) / 23;
                r.Samples.Add(new FlightSample
                {
                    T = (float)t,
                    X = (float)x,
                    Y = (float)(y + (t > 3 && t < 26 ? wobble * 0.3 * Math.Sin(t * 2.1) : 0)),
                    Vy = (float)(y <= 0 ? 0 : vy),
                    Qw = 1,
                    Roll = (float)(jitter * (rnd.NextDouble() - 0.5) + 0.1 * Math.Sin(t)),
                    Throttle = 0.4f,
                    Armed = true,
                    Battery = 0.9f,
                });
            }
            return r;
        }

        // ---------------------------------------------------------------- database

        [Test]
        public void Database_FreshFile_HasTheCurrentLayout_AndKeepsKazakhText()
        {
            string file = Path.Combine(_dir, "сынып.db");
            using (var db = new EduDatabase(file))
            {
                Assert.AreEqual(EduDatabase.Steps.Length, db.Version);
                db.Sql.Execute("insert into users(name, created) values(?1, ?2)", "Әлібек Қуанышұлы", "2026-11-21");
                db.SetSetting("school", "№1 мектеп");
                db.SetSetting("school", "№2 мектеп");
                Assert.AreEqual("№2 мектеп", db.Setting("school"));
                Assert.IsNull(db.Setting("missing"));
            }
            using (var db = new EduDatabase(file))
                Assert.AreEqual("Әлібек Қуанышұлы", db.Sql.Query("select name from users")[0][0]);
        }

        [Test]
        public void Database_Upgrade_AppliesNewSteps_AndAFailedStepLeavesTheFileAlone()
        {
            string file = Path.Combine(_dir, "class.db");
            using (var db = new EduDatabase(file)) db.Sql.Execute("insert into users(name, created) values('a', 't')");
            string[][] bad = EduDatabase.Steps.Concat(new[] { new[] { "create table half(x)", "this is not sql" } }).ToArray();
            var e = Assert.Throws<InvalidOperationException>(() => new EduDatabase(file, bad).Dispose());
            StringAssert.Contains("left unchanged", e.Message);
            using (var db = new EduDatabase(file))
            {
                Assert.AreEqual(EduDatabase.Steps.Length, db.Version);
                Assert.AreEqual(0, db.Sql.Query("select name from sqlite_master where name = 'half'").Count, "the half-made table is gone");
                Assert.AreEqual(1L, db.Sql.Query("select count(*) from users")[0][0]);
            }
            string[][] more = EduDatabase.Steps.Concat(new[] { new[] { "alter table users add column note text" } }).ToArray();
            using (var db = new EduDatabase(file, more))
            {
                Assert.AreEqual(more.Length, db.Version);
                Assert.AreEqual(1, db.Sql.Query("select note from users").Count);
            }
            var newer = Assert.Throws<InvalidOperationException>(() => new EduDatabase(file).Dispose());
            StringAssert.Contains("newer version", newer.Message);
        }

        [Test]
        public void Database_DeletingAStudent_DeletesTheirAttempts()
        {
            using (var db = new EduDatabase(Path.Combine(_dir, "class.db")))
            {
                db.Sql.Execute("insert into users(name, created) values('a', 't')");
                db.Sql.Execute("insert into attempts(user_id, activity_id, activity_hash, started, duration, score, passed, stars) values(1, 'x', 'h', 't', 1, 90, 1, 3)");
                db.Sql.Execute("delete from users where id = 1");
                Assert.AreEqual(0L, db.Sql.Query("select count(*) from attempts")[0][0]);
            }
        }

        // ---------------------------------------------------------------- students, groups, attempts

        [Test]
        public void Store_ClassList_PinsAndNamesInThreeAlphabets()
        {
            using (var db = new EduDatabase(Path.Combine(_dir, "class.db")))
            {
                var s = new EduStore(db, _dir);
                long g = s.AddGroup("7Б");
                Assert.Throws<ArgumentException>(() => s.AddGroup("7б"), "the same name in another case");
                Assert.AreEqual(3, s.ImportList(g, "\uFEFFӘлібек Қуанышұлы;7Б\r\n\"Иванов, Пётр\",7Б\n\nАйгерім\nӘлібек Қуанышұлы\n"));
                Assert.AreEqual(0, s.ImportList(g, "айгерім"), "already in the group");
                List<EduUser> users = s.Users(g);
                CollectionAssert.AreEqual(new[] { "Айгерім", "Әлібек Қуанышұлы", "Иванов, Пётр" }, users.Select(u => u.Name).ToArray(),
                    "Kazakh Ә sorts next to А, not after the Russian alphabet");
                Assert.AreEqual(3, s.Groups()[0].Members);
                string[] sorted = { "Қайрат", "Яна", "anna", "Zoe", "Ұлан", "Кирилл", "Әсел", "7Б", "Уля", "Үміт", "Bob", "Алия" };
                Array.Sort(sorted, EduStore.CompareNames);
                CollectionAssert.AreEqual(new[] { "7Б", "anna", "Bob", "Zoe", "Алия", "Әсел", "Кирилл", "Қайрат", "Уля", "Ұлан", "Үміт", "Яна" }, sorted,
                    "digits, Latin, then the Kazakh alphabet; case does not matter");

                long id = users[0].Id;
                Assert.IsTrue(s.CheckPin(id, null), "no PIN set");
                s.SetPin(id, "2468");
                Assert.IsTrue(s.CheckPin(id, "2468"));
                Assert.IsFalse(s.CheckPin(id, "2469"));
                Assert.IsFalse(s.CheckPin(id, null));
                object[] stored = db.Sql.Query("select pin_hash, pin_salt from users where id = ?1", id)[0];
                Assert.IsFalse(stored.Any(v => ((string)v).Contains("2468")), "the PIN itself is not stored");
                Assert.Throws<ArgumentException>(() => s.SetPin(id, "12"));
                s.SetPin(id, null);
                Assert.IsTrue(s.CheckPin(id, "anything"));
                Assert.IsFalse(s.CheckPin(999, null), "unknown user");
            }
        }

        [Test]
        public void Store_Attempts_Grades_DeleteAndBackup()
        {
            string file = Path.Combine(_dir, "class.db"), copy = Path.Combine(_dir, "backup.db"), replays = Path.Combine(_dir, "replays");
            Directory.CreateDirectory(replays);
            File.WriteAllText(Path.Combine(replays, "f1.grreplay"), "x");
            File.WriteAllText(Path.Combine(replays, "f2.grreplay"), "y");
            using (var db = new EduDatabase(file))
            {
                var s = new EduStore(db, replays);
                long a = s.AddUser("Айгерім"), b = s.AddUser("Bolat");
                s.SaveAttempt(new AttemptRecord { UserId = a, ActivityId = "a02", ActivityHash = "h", Duration = 30, Score = 72.5, Passed = true, Stars = 1, ReplayPath = "f1.grreplay", ReplayBytes = 1 });
                long second = s.SaveAttempt(new AttemptRecord { UserId = a, ActivityId = "a02", ActivityHash = "h", Duration = 28, Score = 91, Passed = true, Stars = 2, Program = "drone.takeoff(2)" });
                s.SaveAttempt(new AttemptRecord { UserId = b, ActivityId = "a02", ActivityHash = "h", Duration = 40, Score = 55, ReplayPath = "f2.grreplay", ReplayBytes = 1 });

                List<AttemptRecord> list = s.Attempts(a, "a02");
                Assert.AreEqual(2, list.Count);
                Assert.AreEqual(91, list[0].Score, "newest first");
                Assert.AreEqual("drone.takeoff(2)", list[0].Program);
                Assert.AreEqual("f1.grreplay", list[1].ReplayPath);
                Assert.AreEqual(91, s.Best(a, "a02"));
                Assert.IsNull(s.Best(a, "none"));

                s.Grade(second, 60, "Қайта тапсыру");
                Assert.AreEqual(60, s.Attempts(a)[0].Final, "the teacher's grade counts");
                Assert.AreEqual(72.5, s.Best(a, "a02"));
                s.Grade(second, null, null);
                Assert.AreEqual(91, s.Best(a, "a02"));
                Assert.Throws<ArgumentException>(() => s.Grade(second, 101, ""));
                Assert.Throws<ArgumentException>(() => s.SaveAttempt(new AttemptRecord { UserId = a }));

                Assert.IsNull(s.ReplayFile(@"..\class.db"), "a stored name cannot point outside the replay folder");
                Assert.IsNull(s.ReplayFile(@"C:\Windows\win.ini"));
                Assert.IsNotNull(s.ReplayFile("f1.grreplay"));

                s.Backup(copy);
                Assert.Throws<IOException>(() => s.Backup(copy), "a backup never overwrites a file");

                Assert.AreEqual(1, s.DeleteUser(a));
                Assert.IsFalse(File.Exists(Path.Combine(replays, "f1.grreplay")), "the student's recording is gone");
                Assert.IsTrue(File.Exists(Path.Combine(replays, "f2.grreplay")));
                Assert.AreEqual(0, s.Attempts(a).Count);
                Assert.AreEqual(1, s.Attempts(b).Count, "the other student is untouched");
                Assert.AreEqual(1L, db.Sql.Query("select count(*) from replays")[0][0]);
            }
            EduStore.CheckBackup(copy);
            using (var db = new EduDatabase(copy)) Assert.AreEqual(2, new EduStore(db, replays).Users().Count, "the backup is from before the deletion");
            File.WriteAllText(Path.Combine(_dir, "notes.txt"), "hello hello hello hello");
            Assert.Throws<InvalidDataException>(() => EduStore.CheckBackup(Path.Combine(_dir, "notes.txt")));
            Assert.Throws<FileNotFoundException>(() => EduStore.CheckBackup(Path.Combine(_dir, "none.db")));
        }

        // ---------------------------------------------------------------- activities and scoring

        [Test]
        public void Activity_Parses_AndItsHashIgnoresSpacingAndKeyOrder()
        {
            ActivityDefinition a = ActivityDefinition.Parse(HoverJson);
            Assert.AreEqual("a02-hover-land", a.Id);
            Assert.AreEqual(5, a.Criteria.Count);
            string shuffled = HoverJson.Replace("\r", "").Replace("\n", " ").Replace("\"format\": 1, \"id\": \"a02-hover-land\",", "\"id\":\"a02-hover-land\",\"format\":1,");
            Assert.AreEqual(a.Hash, ActivityDefinition.Parse(shuffled).Hash);
            Assert.AreNotEqual(a.Hash, ActivityDefinition.Parse(HoverJson.Replace("\"pass\": 60", "\"pass\": 70")).Hash);
        }

        [TestCase("\"format\": 1,", "", "format")]
        [TestCase("a02-hover-land", "A02 Hover", "id")]
        [TestCase("hold_time", "hold_tme", "unknown metric")]
        [TestCase("\"best\": 20, \"worst\": 0", "\"best\": 5, \"worst\": 5", "best")]
        [TestCase("\"id\": \"height\"", "\"id\": \"hold\"", "repeats")]
        [TestCase("\"rubric\": [", "\"rubric\": ", "JSON")]
        [TestCase("\"pass\": 60,", "\"pass\": 60, \"pass\": 70,", "twice")]
        [TestCase("\"exercise\"", "\"game\"", "type")]
        public void Activity_WithAFault_IsRefusedWithAReason(string find, string replace, string reason)
        {
            Assert.IsTrue(HoverJson.Contains(find), "the sample still holds: " + find);
            var e = Assert.Throws<FormatException>(() => ActivityDefinition.Parse(HoverJson.Replace(find, replace)));
            StringAssert.Contains(reason, e.Message);
        }

        [Test]
        public void EveryShippedActivity_LoadsAndUsesKnownMetrics()
        {
            string[] files = Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "Edu/Activities"), "*.json");
            Assert.Greater(files.Length, 0);
            foreach (string file in files)
            {
                ActivityDefinition a = ActivityDefinition.Parse(File.ReadAllText(file));
                Assert.AreEqual(Path.GetFileNameWithoutExtension(file), a.Id, "the file is named after the activity");
            }
        }

        [Test]
        public void Scoring_ReferenceFlightScores90OrMore_AndABadFlightFails()
        {
            ActivityDefinition a = ActivityDefinition.Parse(HoverJson);
            ScoreResult good = Rubric.Score(a, Hover(0.08, 0.1, 0.4, 0.02));
            Assert.GreaterOrEqual(good.Score, 90);
            Assert.IsTrue(good.Passed);
            Assert.GreaterOrEqual(good.Stars, 2);

            ScoreResult bad = Rubric.Score(a, Hover(0.9, 3.0, 2.4, 0.5));
            Assert.IsFalse(bad.Passed);
            Assert.AreEqual(0, bad.Stars);
            Assert.AreEqual(2, bad.Tips.Count, "tips come from the two weakest lines");
            Assert.AreEqual(5, bad.Lines.Count, "a breakdown line per criterion");
        }

        [Test]
        public void Scoring_ARequiredLineAtZero_FailsWhateverTheTotal()
        {
            ActivityDefinition a = ActivityDefinition.Parse(HoverJson);
            ScoreResult hoveredButNeverLanded = Rubric.Score(a, Hover(0.08, 0.1, 0.4, 0.02, land: false));
            Assert.GreaterOrEqual(hoveredButNeverLanded.Score, a.PassMark, "the points alone would pass");
            Assert.IsFalse(hoveredButNeverLanded.Passed);
        }

        [Test]
        public void Scoring_IsRepeatable_AndAnEmptyRecordingScoresZero()
        {
            ActivityDefinition a = ActivityDefinition.Parse(HoverJson);
            Assert.AreEqual(Rubric.Score(a, Hover(0.3, 0.5, 1, 0.1)).Score, Rubric.Score(a, Hover(0.3, 0.5, 1, 0.1)).Score);
            ScoreResult empty = Rubric.Score(a, new FlightRecording());
            Assert.AreEqual(0, empty.Score);
            Assert.IsFalse(empty.Passed);
        }

        [Test]
        public void Course_OpensBlocksInOrder_AndKeepsTheBestResult()
        {
            ActivityDefinition Make(string id) => ActivityDefinition.Parse(HoverJson.Replace("a02-hover-land", id));
            var all = new List<ActivityDefinition> { Make("b07-square"), Make("a02-hover-land"), Make("a01-controls"), Make("c13-position"), Make("x-other") };
            Assert.AreEqual("A", all[1].Block);
            Assert.AreEqual(2, all[1].Number);
            Assert.AreEqual("", all[4].Block, "an id without a number is in no block");

            using (var db = new EduDatabase(Path.Combine(_dir, "class.db")))
            {
                var store = new EduStore(db, Path.Combine(_dir, "replays"));
                long student = store.AddUser("Айгерім");
                var fresh = new Course(all, store, student);
                Assert.AreEqual("a01-controls", fresh.In("A")[0].Activity.Id, "in order of their numbers");
                Assert.AreEqual(2, fresh.In("A").Count);
                Assert.IsTrue(fresh.Open("A"));
                Assert.IsFalse(fresh.Open("B"), "block B waits for block A");
                Assert.IsTrue(fresh.Open("B", everythingUnlocked: true), "unless the teacher opened everything");
                Assert.IsFalse(fresh.Open("Z"));
                Assert.IsNull(fresh.In("A")[1].Best);

                string replays = Path.Combine(_dir, "replays");
                ActivityAttempt.Finish(store, replays, all[1], Hover(1.4, 0.1, 0.4, 0.02), student, "1.5");  // a poor flight first
                ActivityAttempt.Finish(store, replays, all[1], Hover(0.08, 0.1, 0.4, 0.02), student, "1.5"); // then a good one
                var half = new Course(all, store, student);
                CourseEntry hover = half.In("A")[1];
                Assert.AreEqual(2, hover.Attempts);
                Assert.IsTrue(hover.Passed);
                Assert.GreaterOrEqual(hover.Best.Value, 90);
                Assert.GreaterOrEqual(hover.Stars, 2);
                Assert.IsFalse(half.Open("B"), "one exercise of block A is still open");

                ActivityAttempt.Finish(store, replays, all[2], Hover(0.08, 0.1, 0.4, 0.02), student, "1.5");
                var done = new Course(all, store, student);
                Assert.IsTrue(done.Done("A"));
                Assert.IsTrue(done.Open("B"));
                Assert.IsFalse(done.Open("C"));
                done.Progress(out int passed, out int total);
                Assert.AreEqual(2, passed);
                Assert.AreEqual(4, total, "the activity outside the course is not counted");

                Assert.IsFalse(new Course(all, store, store.AddUser("Тимур")).In("A")[1].Passed, "another student starts from nothing");
            }
        }

        [Test]
        public void Certificate_IsEarnedByTheCheckRide_AndPrintsInEveryLanguage()
        {
            var all = new List<ActivityDefinition>();
            foreach (string file in Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "Edu/Activities"), "*.json"))
                all.Add(ActivityDefinition.Parse(File.ReadAllText(file)));
            var course = new Course(all);
            Assert.IsNotNull(course.Find(Certificate.CheckRideId), "the course has its check ride");
            Assert.IsFalse(Certificate.Earned(course));
            Assert.IsNull(Certificate.From(course, "Айгерім", DateTime.Now), "no certificate before the check ride is passed");

            // Everything flown at 80 with two stars, the check ride at 91.
            int count = 0;
            foreach (string block in Course.Blocks)
                foreach (CourseEntry e in course.In(block))
                {
                    e.Passed = true;
                    e.Best = e.Activity.Id == Certificate.CheckRideId ? 91 : 80;
                    e.Stars = 2;
                    count++;
                }
            Assert.IsTrue(Certificate.Earned(course));
            Certificate c = Certificate.From(course, "Әлішер Қожахметұлы", new DateTime(2026, 11, 22));
            Assert.AreEqual(count, c.Passed);
            Assert.AreEqual(count, c.Total);
            Assert.AreEqual(2 * count, c.Stars);
            Assert.AreEqual(3 * count, c.MaxStars);
            Assert.AreEqual(91, c.CheckRide, 1e-9);
            Assert.AreEqual((80.0 * (count - 1) + 91) / count, c.Average, 1e-9);

            // The page's texts are in the language sheet, and the font prints each of them in all three languages.
            var sheet = new Dictionary<string, string[]>();
            foreach (string path in Directory.GetFiles(Path.Combine(Application.dataPath, "_Project/Localization/Text"), "*.tsv"))
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] cells = line.Split('\t');
                    if (cells.Length == 3 && !line.StartsWith("#")) sheet[cells[0]] = cells;
                }
            byte[] font = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "Edu/LiberationSans-Regular.ttf"));
            foreach (string key in Certificate.Texts) Assert.IsTrue(sheet.ContainsKey(key), "not in the language sheets: " + key);
            for (int language = 0; language < 3; language++)
            {
                int column = language;
                Func<string, string> text = key => sheet[key][column];
                Assert.AreEqual("", c.Missing(font, text));
                var ms = new MemoryStream();
                c.Write(font, text).Save(ms);
                StringAssert.StartsWith("%PDF-1.7", Encoding.ASCII.GetString(ms.ToArray(), 0, 8));
                Assert.Greater(ms.Length, 20000, "the font is inside the file");
            }
            // A seat is not a person: the name line is left empty to be written by hand.
            Assert.AreEqual(1, Certificate.From(course, "", DateTime.Now).Write(font, key => key).PageCount);
        }

        /// <summary>The language sheets: English key → the three columns.</summary>
        private static Dictionary<string, string[]> Sheets()
        {
            var sheet = new Dictionary<string, string[]>();
            foreach (string path in Directory.GetFiles(Path.Combine(Application.dataPath, "_Project/Localization/Text"), "*.tsv"))
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] cells = line.Split('\t');
                    if (cells.Length == 3 && !line.StartsWith("#")) sheet[cells[0]] = cells;
                }
            return sheet;
        }

        [Test]
        public void Journal_Gradebook_AndStudentReport()
        {
            ActivityDefinition Make(string id) => ActivityDefinition.Parse(HoverJson.Replace("a02-hover-land", id));
            var all = new List<ActivityDefinition> { Make("b07-square"), Make("a02-hover-land"), Make("a01-controls") };
            using (var db = new EduDatabase(Path.Combine(_dir, "class.db")))
            {
                var store = new EduStore(db, Path.Combine(_dir, "replays"));
                string replays = Path.Combine(_dir, "replays");
                long group = store.AddGroup("7Ә");
                long aliya = store.AddUser("Әлия"), bolat = store.AddUser("Болат"), odd = store.AddUser("=Хакер;\"x\""), other = store.AddUser("Зере");
                foreach (long id in new[] { aliya, bolat, odd }) store.AddToGroup(group, id);
                long seat = store.SeatUser("PC-07");
                ActivityAttempt.Finish(store, replays, all[1], Hover(1.4, 0.1, 0.4, 0.02), aliya, "1.5");
                ActivityAttempt.Finish(store, replays, all[1], Hover(0.08, 0.1, 0.4, 0.02), aliya, "1.5");
                ActivityAttempt.Finish(store, replays, all[2], Hover(0.08, 0.1, 0.4, 0.02), bolat, "1.5");
                ActivityAttempt.Finish(store, replays, all[2], Hover(0.08, 0.1, 0.4, 0.02), seat, "1.5");
                ActivityAttempt.Finish(store, replays, all[0], Hover(0.08, 0.1, 0.4, 0.02), other, "1.5");

                Journal j = Journal.Build(store, all, group);
                Assert.AreEqual("7Ә", j.Group);
                Assert.AreEqual(new[] { "a01-controls", "a02-hover-land", "b07-square" }, j.Activities.ConvertAll(x => x.Id).ToArray(), "in the order of the course");
                Assert.AreEqual(3, j.Rows.Count, "the group's students only");
                JournalRow a = j.Rows.Find(r => r.User.Id == aliya), b = j.Rows.Find(r => r.User.Id == bolat);
                Assert.AreEqual(2, a.Cells[1].Attempts);
                Assert.IsTrue(a.Cells[1].Passed);
                Assert.GreaterOrEqual(a.Cells[1].Best.Value, 90);
                Assert.AreEqual(2, a.Cells[1].All.Count);
                Assert.AreEqual(1, a.Passed);
                Assert.AreEqual(1, a.Flown);
                Assert.AreEqual(a.Cells[1].Best.Value, a.Average, 1e-9);
                Assert.Greater(a.Seconds, 0);
                Assert.IsNull(a.Cells[0].Best);
                ActivityStats hover = j.Stats(1);
                Assert.AreEqual(1, hover.Flown);
                Assert.AreEqual(2, hover.AverageAttempts, 1e-9);
                Assert.AreEqual(0, j.Stats(2).Flown);

                // The teacher's own mark replaces the computed one, and decides the pass.
                Assert.IsTrue(b.Cells[0].Passed);
                store.Grade(store.Attempts(bolat)[0].Id, 40, "landed beside the pad");
                b = Journal.Build(store, all, group).Rows.Find(r => r.User.Id == bolat);
                Assert.AreEqual(40, b.Cells[0].Best.Value, 1e-9);
                Assert.IsFalse(b.Cells[0].Passed);

                // Everyone: the fourth student too, and the seat after the students because something was flown on it.
                Journal everyone = Journal.Build(store, all);
                Assert.AreEqual(5, everyone.Rows.Count);
                Assert.AreEqual("seat", everyone.Rows[4].User.Role);
                Assert.AreEqual("", everyone.Group);

                // The reports, in each language.
                Dictionary<string, string[]> sheet = Sheets();
                foreach (string key in Reports.Texts) Assert.IsTrue(sheet.ContainsKey(key), "not in the language sheets: " + key);
                byte[] font = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "Edu/LiberationSans-Regular.ttf"));
                j = Journal.Build(store, all, group);
                for (int language = 0; language < 3; language++)
                {
                    int column = language;
                    Func<string, string> text = key => sheet.TryGetValue(key, out string[] cells) ? cells[column] : key;
                    var xlsx = new MemoryStream();
                    Reports.Gradebook(j, text, "№ 12 мектеп", new DateTime(2026, 11, 22)).Save(xlsx);
                    byte[] zip = xlsx.ToArray();
                    Assert.AreEqual((byte)'P', zip[0]);
                    Assert.AreEqual((byte)'K', zip[1]);
                    Assert.Greater(zip.Length, 1500);

                    string csv = Reports.GradebookCsv(j, text);
                    string[] lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    Assert.AreEqual(4, lines.Length, "a heading and three students");
                    StringAssert.StartsWith(text("Student") + ";", lines[0]);
                    StringAssert.Contains("\"'=Хакер;\"\"x\"\"\"", csv, "a name that looks like a formula is given as text, and quoted");
                    string hers = Array.Find(lines, l => l.StartsWith("Әлия;"));
                    Assert.AreEqual(lines[0].Split(';').Length, hers.Split(';').Length, "as many cells as headings");
                    StringAssert.DoesNotContain(".", hers, "numbers are written with a comma");
                    StringAssert.Contains(",", Reports.GradebookCsv(j, text, false).Split('\n')[0]);

                    PdfWriter pdf = Reports.StudentProgress(j, j.Rows.Find(r => r.User.Id == aliya), font, text, "№ 12 мектеп", new DateTime(2026, 11, 22));
                    Assert.AreEqual(1, pdf.PageCount);
                    var bytes = new MemoryStream();
                    pdf.Save(bytes);
                    Assert.Greater(bytes.Length, 20000);
                    string every = "";
                    foreach (string key in Reports.Texts) every += text(key);
                    Assert.AreEqual("", pdf.Missing(every + "—…№"));
                }
                // A long course runs onto a second page.
                var many = new List<ActivityDefinition>();
                for (int i = 1; i <= 60; i++) many.Add(Make("a" + i.ToString("00") + "-x"));
                Journal big = Journal.Build(store, many, group);
                Assert.AreEqual(2, Reports.StudentProgress(big, big.Rows[0], font, key => key, "", DateTime.Now).PageCount);

                // The teacher's password and the school's name.
                Assert.IsFalse(store.HasTeacherPassword);
                Assert.IsFalse(store.CheckTeacherPassword(""), "no password set: nothing opens the console");
                Assert.Throws<ArgumentException>(() => store.SetTeacherPassword("abc"));
                store.SetTeacherPassword("ұстаз2026");
                Assert.IsTrue(store.HasTeacherPassword);
                Assert.IsTrue(store.CheckTeacherPassword("ұстаз2026"));
                Assert.IsFalse(store.CheckTeacherPassword("ұстаз2027"));
                Assert.IsFalse(store.CheckTeacherPassword(null));
                Assert.AreEqual("", store.SchoolName);
                store.SchoolName = "  № 12 мектеп ";
                Assert.AreEqual("№ 12 мектеп", store.SchoolName);
            }
        }

        [Test]
        public void SeatProfile_NamesNoPerson_AndIsMadeOnce()
        {
            using (var db = new EduDatabase(Path.Combine(_dir, "class.db")))
            {
                var store = new EduStore(db, Path.Combine(_dir, "replays"));
                long student = store.AddUser("Айгерім", "student", "1234");
                long seat = store.SeatUser("PC-07");
                Assert.AreEqual(seat, store.SeatUser("PC-07"), "asked twice, made once");
                Assert.AreEqual(seat, store.SeatUser("  PC-07 "));
                Assert.AreNotEqual(seat, store.SeatUser("PC-08"));
                Assert.AreNotEqual(seat, store.SeatUser(""), "an empty name still gives a seat");

                EduUser user = store.User(seat);
                Assert.AreEqual("seat", user.Role);
                Assert.AreEqual("PC-07", user.Name);
                Assert.IsFalse(user.HasPin);
                Assert.IsTrue(store.User(student).HasPin);
                Assert.IsNull(store.User(987654), "no such user");

                // A student who happens to be called like the seat is not the seat.
                long namesake = store.AddUser("PC-07");
                Assert.AreNotEqual(namesake, store.SeatUser("PC-07"));
                Assert.Throws<ArgumentException>(() => store.AddUser("x", "admin"));
            }
        }

        [Test]
        public void Attempt_IsGradedStoredWithItsRecording_AndCanBeGradedAgain()
        {
            string replays = Path.Combine(_dir, "replays");
            ActivityDefinition a = ActivityDefinition.Parse(HoverJson);
            using (var db = new EduDatabase(Path.Combine(_dir, "class.db")))
            {
                var store = new EduStore(db, replays);
                long student = store.AddUser("Айгерім");
                var flags = new Dictionary<string, object> { ["weather_changed"] = true };
                AttemptRecord saved = ActivityAttempt.Finish(store, replays, a, Hover(0.08, 0.1, 0.4, 0.02), student, "1.5", flags);
                Assert.GreaterOrEqual(saved.Score, 90);
                Assert.IsTrue(saved.Passed);
                Assert.IsTrue(File.Exists(Path.Combine(replays, saved.ReplayPath)));
                StringAssert.StartsWith("a02-hover-land-", saved.ReplayPath, "named by activity and content, not by student");

                AttemptRecord read = store.Attempts(student, "a02-hover-land")[0];
                Assert.AreEqual(saved.Score, read.Score);
                Assert.AreEqual(a.Hash, read.ActivityHash);
                Assert.AreEqual("1.5", read.Build);
                StringAssert.Contains("weather_changed", read.Flags);
                var metrics = Json.ParseObject(read.Metrics);
                var hold = (Dictionary<string, object>)((Dictionary<string, object>)metrics["lines"])["hold"];
                Assert.Greater((double)hold["value"], 19.0, "the measured value is kept, not only the points");

                Assert.AreEqual(saved.Score, ActivityAttempt.Regrade(store, read, a).Score, "the same grade from the stored recording");
                ActivityDefinition stricter = ActivityDefinition.Parse(HoverJson.Replace("\"best\": 0.5, \"worst\": 2.5", "\"best\": 0.1, \"worst\": 0.3"));
                Assert.Less(ActivityAttempt.Regrade(store, read, stricter).Score, saved.Score, "a corrected rubric re-grades the old flight");

                AttemptRecord never = ActivityAttempt.Finish(store, replays, a, Hover(0.08, 0.1, 0.4, 0.02, land: false), student, "1.5");
                StringAssert.Contains("\"value\":null", never.Metrics, "a measurement with no value is written as null");

                byte[] bytes = File.ReadAllBytes(Path.Combine(replays, read.ReplayPath));
                File.WriteAllBytes(Path.Combine(replays, read.ReplayPath), bytes.Take(bytes.Length - 40).ToArray());
                Assert.Throws<InvalidDataException>(() => ActivityAttempt.Regrade(store, read, a), "a damaged recording is not graded");
                File.Delete(Path.Combine(replays, read.ReplayPath));
                Assert.Throws<FileNotFoundException>(() => ActivityAttempt.Regrade(store, read, a));
            }
        }

        [Test]
        public void Metrics_GatesCountOnlyInOrder_AndEventsAreCountedByType()
        {
            var r = new FlightRecording();
            r.Event(1, "gate", "0");
            r.Event(2, "gate", "2");
            r.Event(3, "gate", "1");
            r.Event(4, "crash", "wall");
            Assert.AreEqual(2, Metrics.Measure("gates_in_order", null, r));
            Assert.AreEqual(1, Metrics.Measure("event_count", new Dictionary<string, object> { ["type"] = "crash" }, r));
            Assert.AreEqual(0, Metrics.Measure("event_count", new Dictionary<string, object> { ["type"] = "breach" }, r));
        }

        [Test]
        public void Metrics_PathErrorAndFence()
        {
            var r = new FlightRecording();
            for (int i = 0; i < 1000; i++)
                r.Samples.Add(new FlightSample { T = i * 0.01f, X = i * 0.01f, Y = 2, Z = 0.5f, Vx = 1, Qw = 1, Armed = true });
            var path = new Dictionary<string, object>
            {
                ["points"] = new List<object> { new List<object> { 0.0, 2.0, 0.0 }, new List<object> { 10.0, 2.0, 0.0 } },
            };
            Assert.AreEqual(0.5, Metrics.Measure("path_error", path, r), 1e-3);
            Assert.AreEqual(5.0, Metrics.Measure("outside_fence", new Dictionary<string, object> { ["radius"] = 5.0 }, r), 0.1);
            Assert.AreEqual(1.0, Metrics.Measure("speed_in_band", new Dictionary<string, object> { ["min"] = 0.5, ["max"] = 1.5 }, r), 1e-6);
            Assert.AreEqual(90, new FlightSample { Qy = 0.70710678f, Qw = 0.70710678f }.Heading, 0.01, "a quarter turn to the right");
        }

        // ---------------------------------------------------------------- recording file

        [Test]
        public void Recording_RoundTrip_AndScoreFromTheFileIsTheSame()
        {
            FlightRecording rec = Hover(0.08, 0.1, 0.4, 0.02);
            rec.Notes["drone"] = "camera900";
            rec.Event(7f, "crash", "стена");
            string file = Path.Combine(_dir, "flight.grreplay");
            rec.Save(file);
            FlightRecording back = FlightRecording.Load(file);
            Assert.AreEqual(rec.Samples.Count, back.Samples.Count);
            Assert.AreEqual(rec.Samples[1500].X, back.Samples[1500].X);
            Assert.AreEqual("стена", back.Events[0].Data);
            Assert.AreEqual("camera900", back.Notes["drone"]);
            Assert.AreEqual(rec.Hash(), back.Hash());
            ActivityDefinition a = ActivityDefinition.Parse(HoverJson);
            Assert.AreEqual(Rubric.Score(a, rec).Score, Rubric.Score(a, back).Score);
            Assert.Less(new FileInfo(file).Length, 200 * 1024, "32 s of flight stays small");
        }

        [Test]
        public void Recording_DamagedCutOrEdited_IsRefused()
        {
            var ms = new MemoryStream();
            Hover(0.1, 0.1, 0.4, 0.02).Save(ms);
            byte[] good = ms.ToArray();
            byte[] cut = good.Take(good.Length / 2).ToArray();
            byte[] body = (byte[])good.Clone();
            body[body.Length / 2] ^= 4;
            byte[] hash = (byte[])good.Clone();
            hash[10] ^= 1;
            foreach (byte[] data in new[] { cut, body, hash, new byte[100], new byte[3] })
                Assert.Throws<InvalidDataException>(() => FlightRecording.Load(new MemoryStream(data)));
        }

        [Test]
        public void Recorder_GivesAnEvenRateWhateverTheFrameRate_AndKeepsOnlyTheLastSecondsWhenAsked()
        {
            foreach (double fps in new[] { 30.0, 60.0, 144.0, 250.0 })
            {
                var rec = new FlightRecorderCore(100f);
                for (int i = 0; i * (1.0 / fps) <= 10.0 + 1e-9; i++)
                {
                    double t = i / fps;
                    var s = new Ushqyr.Flight.QuadState { Position = new Ushqyr.Flight.Vec3((float)t * 2f, 1f, 0f), Orientation = Ushqyr.Flight.Quat.Identity, Armed = true };
                    rec.Push(5.0 + t, s, new Ushqyr.Flight.PilotInput { Throttle = 0.4f, Pitch = 0.1f }, 0.8f);
                }
                FlightRecording r = rec.Recording;
                Assert.AreEqual(1001, r.Samples.Count, 1, fps + " fps: 100 samples a second for 10 s");
                Assert.AreEqual(0f, r.Samples[0].T, 1e-6f, "time starts at zero");
                Assert.AreEqual(0.01f, r.Samples[1].T - r.Samples[0].T, 1e-5f);
                FlightSample mid = r.Samples[500];
                Assert.AreEqual(mid.T * 2f, mid.X, 2e-3f, fps + " fps: positions between frames are interpolated");
                Assert.AreEqual(0.4f, mid.Throttle);
                Assert.AreEqual(1f, mid.Qw, 1e-5f);
            }
            var rolling = new FlightRecorderCore(100f, 20f);
            for (int i = 0; i < 6000; i++)
            {
                if (i == 100) rolling.Event("crash");
                if (i == 5900) rolling.Event("gate", "0");
                rolling.Push(i * 0.01, new Ushqyr.Flight.QuadState { Orientation = Ushqyr.Flight.Quat.Identity }, default, 1f);
            }
            Assert.LessOrEqual(rolling.Recording.Samples.Count, 2500, "about the last 20 s of a 60 s flight (cut in blocks)");
            Assert.GreaterOrEqual(rolling.Recording.Samples.Count, 2000);
            Assert.AreEqual(1, rolling.Recording.Events.Count, "the old event went with its samples");
            Assert.AreEqual("gate", rolling.Recording.Events[0].Type);
            rolling.Restart();
            Assert.AreEqual(0, rolling.Recording.Samples.Count);
        }

        // ---------------------------------------------------------------- JSON

        [Test]
        public void Json_RoundTrip_KeepsKazakhAndEscapes_AndRefusesBadText()
        {
            Dictionary<string, object> o = Json.ParseObject("{\"a\":\"Ұшқыр \\\"x\\\"\\n\",\"b\":[1,2.5,true,null],\"c\":{}}");
            Assert.AreEqual("Ұшқыр \"x\"\n", Json.ParseObject(Json.Write(o))["a"]);
            Assert.AreEqual(2.5, ((List<object>)o["b"])[1]);
            foreach (string bad in new[] { "", "{", "{\"a\":}", "[1,]", "{\"a\":1} x", "{\"a\":\"line\nbreak\"}", "{'a':1}", "nul" })
                Assert.Throws<FormatException>(() => Json.Parse(bad), bad);
            Assert.Throws<FormatException>(() => Json.Parse(new string('[', 200)), "deep nesting is refused, not a stack overflow");
        }

        // ---------------------------------------------------------------- reports

        [Test]
        public void Xlsx_HoldsKazakhText_Numbers_AndEscapedMarkup()
        {
            var x = new XlsxWriter();
            x.AddSheet("Журнал / Журнал");
            x.AddRow("Оқушы", "Балл");
            x.AddRow("Өмірзақ <Үміт> & Co", 60.5);
            var ms = new MemoryStream();
            x.Save(ms);
            ms.Position = 0;
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (string part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/styles.xml", "xl/worksheets/sheet1.xml" })
                    Assert.IsNotNull(zip.GetEntry(part), part);
                string sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml").Open(), Encoding.UTF8).ReadToEnd();
                StringAssert.Contains("Өмірзақ &lt;Үміт&gt; &amp; Co", sheet);
                StringAssert.Contains("<v>60.5</v>", sheet);
                string book = new StreamReader(zip.GetEntry("xl/workbook.xml").Open(), Encoding.UTF8).ReadToEnd();
                StringAssert.Contains("name=\"Журнал   Журнал\"", book, "characters Excel forbids in a sheet name are replaced");
            }
            Assert.AreEqual("A", XlsxWriter.ColumnName(0));
            Assert.AreEqual("AA", XlsxWriter.ColumnName(26));
        }

        [Test]
        public void Pdf_EmbedsTheFont_AndHasAGlyphForEveryKazakhLetter()
        {
            byte[] font = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "Edu/LiberationSans-Regular.ttf"));
            var pdf = new PdfWriter(font, "LiberationSans");
            const string kazakh = "ӘҒҚҢӨҰҮҺІ әғқңөұүһі";
            Assert.AreEqual("", pdf.Missing(kazakh + " Ёё Русский English 0123"));
            Assert.AreEqual("\u4e2d", pdf.Missing("\u4e2d"), "a letter the font lacks is reported, not printed as a box");
            pdf.AddPage();
            pdf.Text(50, 60, 12, kazakh);
            pdf.AddPage();
            Assert.Greater(pdf.TextWidth(kazakh, 12), pdf.TextWidth(kazakh, 6));
            var ms = new MemoryStream();
            pdf.Save(ms);
            string text = Encoding.ASCII.GetString(ms.ToArray());
            StringAssert.StartsWith("%PDF-1.7", text);
            StringAssert.Contains("/Count 2", text);
            StringAssert.Contains("/FontFile2", text);
            StringAssert.Contains("%%EOF", text);
            Assert.Greater(ms.Length, 50000, "the font is inside the file");
        }

        // ---------------------------------------------------------------- licence

        [Test]
        public void Licence_GenuineAccepted_EditedOrForeignKeyRefused()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"customer\":\"№1 мектеп\"}");
            using (RSA vendor = RSA.Create())
            using (RSA other = RSA.Create())
            {
                vendor.KeySize = 2048;
                other.KeySize = 2048;
                byte[] signature = vendor.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                RSAParameters pub = vendor.ExportParameters(false), foreign = other.ExportParameters(false);
                Assert.IsTrue(LicenseCheck.SignatureValid(payload, signature, pub.Modulus, pub.Exponent));
                Assert.IsFalse(LicenseCheck.SignatureValid(payload, signature, foreign.Modulus, foreign.Exponent));
                payload[3] ^= 1;
                Assert.IsFalse(LicenseCheck.SignatureValid(payload, signature, pub.Modulus, pub.Exponent));
                Assert.IsFalse(LicenseCheck.SignatureValid(payload, new byte[5], pub.Modulus, pub.Exponent));
                Assert.IsFalse(LicenseCheck.SignatureValid(null, signature, pub.Modulus, pub.Exponent));
            }
        }

        [Test]
        public void Fingerprint_HidesTheIdentifiers_AndToleratesOneChangedPart()
        {
            var before = LicenseCheck.Fingerprint(new Dictionary<string, string> { ["board"] = "ABC-123", ["disk"] = "111", ["mac"] = "aa", ["empty"] = "" });
            var after = LicenseCheck.Fingerprint(new Dictionary<string, string> { ["board"] = " abc-123", ["disk"] = "999", ["mac"] = "aa" });
            Assert.AreEqual(3, before.Count, "an identifier that could not be read is left out");
            Assert.IsFalse(before.Values.Any(v => v.Contains("ABC") || v.Contains("abc-123")));
            Assert.AreEqual(2, LicenseCheck.Matches(before, after), "case and spaces do not matter; the new disk does not match");
            Assert.AreEqual(0, LicenseCheck.Matches(before, null));
        }

        // ---------------------------------------------------------------- MAVLink

        [Test]
        public void Mavlink_ChecksumMatchesTheReferenceValue()
        {
            // The checksum MAVLink uses (CRC-16/MCRF4XX) of the ASCII text "123456789" is 0x6F91, its published check value.
            byte[] data = Encoding.ASCII.GetBytes("12345678");
            Assert.AreEqual(0x6F91, MavlinkEndpoint.Crc(data, 0, 8, (byte)'9'));
        }

        [Test]
        public void Mavlink_TestVehicle_FliesAMissionAndDisarms()
        {
            var v = new MavTestVehicle(51.0893, 71.4161, 350);
            Assert.IsFalse(v.Takeoff(5), "not armed");
            Assert.IsTrue(v.SetMode(4));
            Assert.IsTrue(v.Arm(true));
            Assert.IsTrue(v.Takeoff(5));
            for (int i = 0; i < 400; i++) v.Step(0.02);
            Assert.AreEqual(5, v.AltRel, 0.15);
            Assert.IsFalse(v.Arm(false), "no disarming in the air");
            Assert.IsFalse(v.SetMode(3), "no mission loaded");
            Assert.IsTrue(v.SetMode(6));
            for (int i = 0; i < 2000 && v.Armed; i++) v.Step(0.02);
            Assert.IsFalse(v.Armed, "return to launch ends on the ground, disarmed");
            Assert.IsTrue(v.Landed);
        }
    }
}
