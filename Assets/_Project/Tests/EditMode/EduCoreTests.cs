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

        private static string HoverJson => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Edu/Activities/a02-hover-land.json"));

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
