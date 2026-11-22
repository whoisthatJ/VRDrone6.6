using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Ushqyr.Edu;

namespace Ushqyr.Tests
{
    /// <summary>The class link (EDU_PLAN F6, §9) on this PC's own loopback: joining, signed commands, what seats send back.</summary>
    public class ClassLinkTests
    {
        private static ClassKey _key, _otherKey;

        [OneTimeSetUp]
        public void Keys()
        {
            // Making a key takes a moment: two are made once for all the tests.
            _key = ClassKey.Create();
            _otherKey = ClassKey.Create();
        }

        [OneTimeTearDown]
        public void DropKeys()
        {
            _key.Dispose();
            _otherKey.Dispose();
        }

        private static ClassHost Host(ClassKey key = null, string code = "482913", int beaconPort = 0)
        {
            var host = new ClassHost("7Ә сыныбы", code, key ?? _key);
            host.Start(0, beaconPort, true);
            return host;
        }

        private static ClassSeat Seat(ClassHost host, string name, string code = "482913", string pinned = null)
        {
            var seat = new ClassSeat();
            seat.Connect("127.0.0.1", host.Port, name, code, "", pinned);
            return seat;
        }

        private static bool Wait(Func<bool> done, int ms = 3000)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms)
            {
                if (done()) return true;
                Thread.Sleep(5);
            }
            return done();
        }

        private static ClassEvent Next(ClassHost host, string kind, int ms = 3000)
        {
            ClassEvent found = null;
            Wait(() =>
            {
                while (host.Poll(out ClassEvent e))
                    if (e.Kind == kind) { found = e; return true; }
                return false;
            }, ms);
            return found;
        }

        [Test]
        public void Key_SignsAndIsRecognisedByItsPublicHalf()
        {
            byte[] text = Encoding.UTF8.GetBytes("ұшуды бастау");
            byte[] signature = _key.Sign(text);
            using (ClassKey seen = ClassKey.Import(_key.ExportPublic()))
            using (ClassKey whole = ClassKey.Import(_key.ExportPrivate()))
            {
                Assert.IsFalse(seen.CanSign);
                Assert.IsTrue(whole.CanSign);
                Assert.IsTrue(seen.Verify(text, signature));
                Assert.IsTrue(seen.Verify(text, whole.Sign(text)), "the key read back from the class database signs the same way");
                Assert.IsFalse(seen.Verify(Encoding.UTF8.GetBytes("ұшуды тоқтату"), signature));
                Assert.AreEqual(_key.Fingerprint, seen.Fingerprint);
                Assert.AreEqual(16, seen.Fingerprint.Length);
            }
            Assert.AreNotEqual(_key.Fingerprint, _otherKey.Fingerprint);
            Assert.Throws<FormatException>(() => ClassKey.Import("not a key"));
        }

        [Test]
        public void Seat_JoinsWithTheClassCode_AndNotWithout()
        {
            using (ClassHost host = Host())
            {
                using (ClassSeat seat = Seat(host, "PC-07"))
                {
                    Assert.AreEqual("7Ә сыныбы", seat.ClassName);
                    Assert.AreEqual(_key.Fingerprint, seat.KeyFingerprint);
                    Assert.AreEqual("PC-07", Next(host, "joined").Seat);
                    Assert.IsTrue(host.Seats()[0].Connected);
                }
                Assert.AreEqual("PC-07", Next(host, "left").Seat);
                Assert.IsFalse(host.Seats()[0].Connected, "a seat that left stays on the list, marked as gone");

                var wrong = new ClassSeat();
                var refused = Assert.Throws<UnauthorizedAccessException>(() => wrong.Connect("127.0.0.1", host.Port, "PC-08", "000000"));
                Assert.AreEqual("code", refused.Message);
                wrong.Dispose();
                Assert.AreEqual(1, host.Seats().Count, "a seat without the code is not let in");

                // The seat comes back: the same line on the teacher's list, connected again.
                using (ClassSeat again = Seat(host, "PC-07", "482913", _key.Fingerprint))
                {
                    Assert.IsNotNull(Next(host, "joined"));
                    Assert.AreEqual(1, host.Seats().Count);
                    Assert.IsTrue(host.Seats()[0].Connected);
                }
            }
        }

        [Test]
        public void Seat_RefusesAClassWithAnotherKey()
        {
            // Somebody else opens a "class" with the same code (a student who knows it): the seat remembers the
            // teacher's key and will not join.
            using (ClassHost impostor = Host(_otherKey))
            {
                var seat = new ClassSeat();
                var refused = Assert.Throws<UnauthorizedAccessException>(() => seat.Connect("127.0.0.1", impostor.Port, "PC-07", "482913", "", _key.Fingerprint));
                Assert.AreEqual("key", refused.Message);
                seat.Dispose();
            }
        }

        [Test]
        public void Commands_AreObeyedOnlyWhenTheTeacherSignedThem()
        {
            using (ClassHost host = Host())
            using (ClassHost impostor = Host(_otherKey))
            using (ClassSeat seat = Seat(host, "PC-07"))
            using (ClassSeat neighbour = Seat(host, "PC-08"))
            {
                Assert.AreEqual(2, host.Broadcast(new Dictionary<string, object> { ["do"] = "start", ["activity"] = "a02-hover-land" }));
                Dictionary<string, object> got = null;
                Assert.IsTrue(Wait(() => seat.Poll(out got)));
                Assert.AreEqual("start", Json.Str(got, "do", ""));
                Assert.AreEqual("a02-hover-land", Json.Str(got, "activity", ""));
                Assert.IsTrue(Wait(() => neighbour.Poll(out _)));

                // To one seat only.
                Assert.IsTrue(host.Send("PC-08", new Dictionary<string, object> { ["do"] = "pause" }));
                Assert.IsTrue(Wait(() => neighbour.Poll(out got)));
                Assert.AreEqual("pause", Json.Str(got, "do", ""));
                Assert.IsFalse(Wait(() => seat.Poll(out _), 200), "the other seat hears nothing");
                Assert.IsFalse(host.Send("PC-99", new Dictionary<string, object> { ["do"] = "pause" }));

                // A command signed with another key, one for another seat, and one sent a second time are refused.
                Assert.IsFalse(seat.HandleFrame(ClassWire.Command, impostor.BuildCommand(new Dictionary<string, object> { ["do"] = "attention" })));
                Assert.IsFalse(seat.HandleFrame(ClassWire.Command, host.BuildCommand(new Dictionary<string, object> { ["do"] = "pause" }, "PC-08")));
                byte[] once = host.BuildCommand(new Dictionary<string, object> { ["do"] = "resume" });
                Assert.IsTrue(seat.HandleFrame(ClassWire.Command, once));
                Assert.IsFalse(seat.HandleFrame(ClassWire.Command, once), "the same command again");
                byte[] changed = host.BuildCommand(new Dictionary<string, object> { ["do"] = "resume" });
                changed[changed.Length - 3] ^= 1;
                Assert.IsFalse(seat.HandleFrame(ClassWire.Command, changed), "a command altered on the way");
                Assert.IsFalse(seat.HandleFrame(ClassWire.Command, new byte[] { 1, 0, 7 }));
                Assert.AreEqual(5, seat.Refused);
                Assert.IsTrue(seat.Poll(out got));
                Assert.AreEqual("resume", Json.Str(got, "do", ""));
                Assert.IsFalse(seat.Poll(out _), "nothing refused was handed on");
            }
        }

        [Test]
        public void Seat_SendsStatusPicturesAndAttempts_AndHearsThatAnAttemptWasStored()
        {
            using (ClassHost host = Host())
            using (ClassSeat seat = Seat(host, "PC-07"))
            {
                Assert.IsNotNull(Next(host, "joined"));
                Assert.IsTrue(seat.SendStatus(new Dictionary<string, object> { ["state"] = "flying", ["user"] = "Әлия", ["score"] = 87.0 }));
                ClassEvent status = Next(host, "status");
                Assert.AreEqual("flying", Json.Str(status.Data, "state", ""));
                Assert.AreEqual("Әлия", host.Seats()[0].User);

                var image = new byte[5000];
                new Random(3).NextBytes(image);
                Assert.IsTrue(seat.SendPicture(image));
                ClassEvent picture = Next(host, "picture");
                Assert.AreEqual(image, picture.Bytes);
                Assert.AreEqual(1, host.Seats()[0].Pictures);

                Assert.IsTrue(seat.RaiseHand(true));
                Assert.IsTrue(Json.Flag(Next(host, "hand").Data, "up"));

                var replay = new byte[300000];
                new Random(4).NextBytes(replay);
                Assert.IsTrue(seat.SendAttempt("{\"activity\":\"a02-hover-land\",\"user\":\"Әлия\",\"score\":91.5}", replay));
                ClassEvent attempt = Next(host, "attempt");
                Assert.AreEqual(replay, attempt.Bytes);
                Assert.AreEqual(91.5, Json.Num(attempt.Data, "score", 0), 1e-9);
                string stored = null;
                Assert.IsTrue(Wait(() => seat.PollStored(out stored)));
                Assert.AreEqual(ClassWire.Sha256(replay), stored, "the seat is told which recording arrived");
            }
        }

        [Test]
        public void ThirtySeats_AllHearACommandWithinFiveSeconds()
        {
            using (ClassHost host = Host())
            {
                var seats = new List<ClassSeat>();
                try
                {
                    for (int i = 1; i <= 30; i++) seats.Add(Seat(host, "SIM-" + i.ToString("00")));
                    Assert.IsTrue(Wait(() => host.Seats().FindAll(s => s.Connected).Count == 30));
                    var clock = Stopwatch.StartNew();
                    Assert.AreEqual(30, host.Broadcast(new Dictionary<string, object> { ["do"] = "start", ["activity"] = "a02-hover-land" }));
                    var heard = new bool[30];
                    Assert.IsTrue(Wait(() =>
                    {
                        bool all = true;
                        for (int i = 0; i < 30; i++)
                        {
                            if (!heard[i] && seats[i].Poll(out _)) heard[i] = true;
                            all &= heard[i];
                        }
                        return all;
                    }, 5000), "every seat heard the command");
                    Assert.Less(clock.ElapsedMilliseconds, 5000);
                    UnityEngine.Debug.Log("[Edu] 30 seats heard a command in " + clock.ElapsedMilliseconds + " ms");
                }
                finally
                {
                    foreach (ClassSeat s in seats) s.Dispose();
                }
            }
        }

        [Test]
        public void AClassAnnouncesItself_AndASeatFindsIt()
        {
            int port;
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
            using (var finder = new ClassFinder(port))
            using (ClassHost host = Host(null, "482913", port))
            {
                Assert.IsTrue(Wait(() => finder.Classes().Count > 0, 4000), "the class was heard");
                FoundClass found = finder.Classes()[0];
                Assert.AreEqual("7Ә сыныбы", found.Name);
                Assert.AreEqual(host.Port, found.Port);
                Assert.AreEqual(_key.Fingerprint, found.KeyFingerprint);
                using (ClassSeat seat = Seat(host, "PC-07", "482913", found.KeyFingerprint)) Assert.IsTrue(seat.Connected);
            }
        }

        [Test]
        public void Host_NeedsAKeyThatSigns_AndACode()
        {
            using (ClassKey seen = ClassKey.Import(_key.ExportPublic()))
                Assert.Throws<ArgumentException>(() => new ClassHost("x", "482913", seen));
            Assert.Throws<ArgumentException>(() => new ClassHost("x", "12", _key));
        }
    }
}
