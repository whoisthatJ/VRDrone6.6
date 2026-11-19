using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ushqyr.Bench;
using Ushqyr.Flight;
using UnityEditor;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>AI Bench B1: gate geometry, referee rules, RC link, sensors, protocol headers, track file.</summary>
    public sealed class BenchTests
    {
        // ---------- gate ----------

        [Test]
        public void StandardGate_HasTheDimensionsOfTheTask()
        {
            Assert.AreEqual(2.5f, GateGeometry.Outer, 1e-6f);
            Assert.AreEqual(1.95f, GateGeometry.Opening, 1e-6f);
            Assert.AreEqual(0.275f, GateGeometry.Bar, 1e-6f);
            Assert.AreEqual(1.25f, GateGeometry.CentreHeight, 1e-6f);
        }

        [Test]
        public void GatePrefab_MeasuresWhatTheStandardSays()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Bench/Prefabs/Gate_Standard_250.prefab");
            Assert.IsNotNull(prefab, "run Ushqyr > Bench > Build Everything");
            var outer = new Bounds(new Vector3(0f, 1.25f, 0f), Vector3.zero);
            float openLeft = float.MinValue, openRight = float.MaxValue, openBottom = float.MinValue, openTop = float.MaxValue;
            foreach (var c in prefab.GetComponentsInChildren<BoxCollider>())
            {
                Vector3 centre = c.transform.localPosition, size = c.transform.localScale;
                var b = new Bounds(centre, size);
                outer.Encapsulate(b);
                // Each bar limits the opening from its own side.
                if (c.name == "Left") openLeft = b.max.x;
                if (c.name == "Right") openRight = b.min.x;
                if (c.name == "Bottom") openBottom = b.max.y;
                if (c.name == "Top") openTop = b.min.y;
            }
            Assert.AreEqual(2.5f, outer.size.x, 1e-4f, "outer width");
            Assert.AreEqual(2.5f, outer.size.y, 1e-4f, "outer height");
            Assert.AreEqual(0f, outer.min.y, 1e-4f, "stands on the floor");
            Assert.AreEqual(1.95f, openRight - openLeft, 1e-4f, "opening width");
            Assert.AreEqual(1.95f, openTop - openBottom, 1e-4f, "opening height");
            Assert.AreEqual(0f, (openRight + openLeft) * 0.5f, 1e-4f, "opening centred sideways");
            Assert.AreEqual(1.25f, (openTop + openBottom) * 0.5f, 1e-4f, "opening centred in height");
            Assert.IsNotNull(prefab.GetComponent<BenchGate>());
        }

        // ---------- referee ----------

        private static Referee OneGate(bool frontOnly = false) =>
            new Referee(new[] { RefereeGate.At("A", new Vec3(0f, 1.25f, 0f), Quat.Identity, frontOnly) });

        /// <summary>Flies a straight line in 1 cm physics steps.</summary>
        private static double Fly(Referee r, Vec3 from, Vec3 to, double t0 = 0)
        {
            float length = (to - from).Magnitude;
            int steps = Math.Max(1, (int)(length / 0.01f));
            Vec3 prev = from;
            for (int i = 1; i <= steps; i++)
            {
                Vec3 p = Vec3.Lerp(from, to, (float)i / steps);
                r.Step(prev, p, t0 + (i - 1) * 0.001, t0 + i * 0.001);
                prev = p;
            }
            return t0 + steps * 0.001;
        }

        [Test]
        public void Referee_ThroughTheOpening_Scores()
        {
            var r = OneGate();
            r.Reset(new Vec3(0f, 1.25f, -3f));
            Fly(r, new Vec3(0f, 1.25f, -3f), new Vec3(0.5f, 1.6f, 3f));
            Assert.AreEqual(1, r.Points);
            Assert.AreEqual(GateEventKind.Scored, r.Events[0].Kind);
            Assert.AreEqual(0.300, r.Events[0].Time, 0.002, "time stamp is the moment of crossing the plane");
        }

        [Test]
        public void Referee_FromTheBack_ScoresWhenAnyDirectionIsAllowed_AndNotWhenFrontOnly()
        {
            var any = OneGate();
            any.Reset(new Vec3(0f, 1.25f, 3f));
            Fly(any, new Vec3(0f, 1.25f, 3f), new Vec3(0f, 1.25f, -3f));
            Assert.AreEqual(1, any.Points);

            var front = OneGate(true);
            front.Reset(new Vec3(0f, 1.25f, 3f));
            Fly(front, new Vec3(0f, 1.25f, 3f), new Vec3(0f, 1.25f, -3f));
            Assert.AreEqual(0, front.Points);
            Assert.AreEqual(GateEventKind.WrongDirection, front.Events[0].Kind);
        }

        [Test]
        public void Referee_PokingInAndBackingOut_IsNotAPass()
        {
            var r = OneGate();
            r.Reset(new Vec3(0f, 1.25f, -2f));
            double t = Fly(r, new Vec3(0f, 1.25f, -2f), new Vec3(0f, 1.25f, 0.25f));
            Fly(r, new Vec3(0f, 1.25f, 0.25f), new Vec3(0f, 1.25f, -2f), t);
            Assert.AreEqual(0, r.Points);
            Assert.AreEqual(0, r.Events.Count);
        }

        [Test]
        public void Referee_FlyingAround_IsNotAPass()
        {
            var r = OneGate();
            r.Reset(new Vec3(3f, 1.25f, -2f));
            Fly(r, new Vec3(3f, 1.25f, -2f), new Vec3(3f, 1.25f, 2f));
            Assert.AreEqual(0, r.Points);
            // Over the top as well.
            Fly(r, new Vec3(0f, 3.5f, 2f), new Vec3(0f, 3.5f, -2f));
            Assert.AreEqual(0, r.Points);
        }

        [Test]
        public void Referee_ThroughTheFrameBar_IsNotAPass()
        {
            // 1.0 m to the side of the centre is inside the frame bar (the opening ends at 0.975 m).
            var r = OneGate();
            r.Reset(new Vec3(1.0f, 1.25f, -2f));
            Fly(r, new Vec3(1.0f, 1.25f, -2f), new Vec3(1.0f, 1.25f, 2f));
            Assert.AreEqual(0, r.Points);

            var edge = OneGate();
            edge.Reset(new Vec3(0.97f, 1.25f, -2f));
            Fly(edge, new Vec3(0.97f, 1.25f, -2f), new Vec3(0.97f, 1.25f, 2f));
            Assert.AreEqual(1, edge.Points, "just inside the opening counts");
        }

        [Test]
        public void Referee_AroundThenBackThroughTheOpening_ScoresOnce()
        {
            var r = OneGate();
            r.Reset(new Vec3(3f, 1.25f, -2f));
            double t = Fly(r, new Vec3(3f, 1.25f, -2f), new Vec3(3f, 1.25f, 2f));
            t = Fly(r, new Vec3(3f, 1.25f, 2f), new Vec3(0f, 1.25f, 2f), t);
            Fly(r, new Vec3(0f, 1.25f, 2f), new Vec3(0f, 1.25f, -2f), t);
            Assert.AreEqual(1, r.Points);
        }

        [Test]
        public void Referee_FastPass_IsNotMissed()
        {
            // 30 m/s with 1 ms steps: 3 cm per step; and one giant step across the whole gate.
            var r = OneGate();
            r.Reset(new Vec3(0f, 1.25f, -5f));
            r.Step(new Vec3(0f, 1.25f, -5f), new Vec3(0f, 1.25f, 5f), 0, 0.001);
            Assert.AreEqual(1, r.Points);
        }

        private static Referee SixGates()
        {
            var gates = new List<RefereeGate>();
            for (int i = 0; i < 6; i++) gates.Add(RefereeGate.At("G" + (i + 1), new Vec3(i * 10f, 1.25f, 0f), Quat.Identity));
            return new Referee(gates);
        }

        [Test]
        public void Referee_RepeatScoresOnlyAfterFourOtherGates()
        {
            var r = SixGates();
            r.Reset(new Vec3(0f, 1.25f, -2f));
            double t = 0;
            float side = -1f; // the drone is at z = 2 * side; all six gates share the plane z = 0
            void Through(int gate)
            {
                float x = gate * 10f;
                t = Fly(r, new Vec3(x, 1.25f, 2f * side), new Vec3(x, 1.25f, -2f * side), t + 0.01);
                side = -side;
            }

            Through(0);
            Assert.AreEqual(1, r.Points);
            Through(0);
            Assert.AreEqual(1, r.Points, "straight back through the same gate");
            Assert.AreEqual(GateEventKind.RepeatIgnored, r.Events[1].Kind);
            Assert.AreEqual(0, r.Events[1].OthersSince);

            Through(1); Through(2); Through(3);
            Assert.AreEqual(4, r.Points);
            Through(0);
            Assert.AreEqual(4, r.Points, "only 3 other gates since");
            Assert.AreEqual(3, r.Events[r.Events.Count - 1].OthersSince);
            Through(4);
            Assert.AreEqual(5, r.Points);
            Through(0);
            Assert.AreEqual(6, r.Points, "4 other gates since: counts again");
        }

        [Test]
        public void Referee_TieBreakTimeIsTheLastScoredGate()
        {
            var r = SixGates();
            r.Reset(new Vec3(0f, 1.25f, -2f));
            double t = Fly(r, new Vec3(0f, 1.25f, -2f), new Vec3(0f, 1.25f, 2f), 5.0);
            Assert.AreEqual(5.2, r.LastScoreTime, 0.002);
            // A repeat afterwards doesn't move the time.
            Fly(r, new Vec3(0f, 1.25f, 2f), new Vec3(0f, 1.25f, -2f), t + 3.0);
            Assert.AreEqual(5.2, r.LastScoreTime, 0.002);
        }

        // ---------- RC link ----------

        private static int[] Rc(int roll = 1500, int pitch = 1500, int throttle = 1000, int yaw = 1500, int aux1 = 1000, int aux2 = 1000) =>
            new[] { roll, pitch, throttle, yaw, aux1, aux2 };

        [Test]
        public void RcLink_AppliesTheDelay()
        {
            var link = new RcLink(new RcLinkSettings { RateHz = 100f, Latency = 0.030f, FailsafeTime = 0.5f });
            link.Send(0.0, Rc(roll: 2000));
            Assert.AreEqual(0f, link.Sample(0.029).Roll, 1e-6f, "not there yet");
            Assert.AreEqual(1f, link.Sample(0.030).Roll, 1e-6f);
        }

        [Test]
        public void RcLink_MapsChannelsLikeATransmitter()
        {
            var link = new RcLink(new RcLinkSettings { RateHz = 100f, Latency = 0f, FailsafeTime = 0.5f });
            link.Send(0.0, new[] { 1750, 1250, 1500, 2000, 1000, 2000 });
            PilotInput p = link.Sample(0.0);
            Assert.AreEqual(0.5f, p.Roll, 1e-6f);
            Assert.AreEqual(-0.5f, p.Pitch, 1e-6f);
            Assert.AreEqual(0.5f, p.Throttle, 1e-6f);
            Assert.AreEqual(1f, p.Yaw, 1e-6f);
            Assert.AreEqual(FlightMode.Acro, p.Mode);
            Assert.AreEqual(FlightMode.Angle, RcLink.ModeOf(1000));
            Assert.AreEqual(FlightMode.Horizon, RcLink.ModeOf(1500));
        }

        [Test]
        public void RcLink_ClampsOutOfRangeValues()
        {
            var link = new RcLink(new RcLinkSettings { RateHz = 100f, Latency = 0f, FailsafeTime = 0.5f });
            link.Send(0.0, new[] { 99999, -5, 2500, 0 });
            PilotInput p = link.Sample(0.0);
            Assert.AreEqual(1f, p.Roll, 1e-6f);
            Assert.AreEqual(-1f, p.Pitch, 1e-6f);
            Assert.AreEqual(1f, p.Throttle, 1e-6f);
            Assert.AreEqual(-1f, p.Yaw, 1e-6f);
        }

        [Test]
        public void RcLink_ArmsOnlyWithThrottleLow_OnTheSwitchGoingHigh()
        {
            var link = new RcLink(new RcLinkSettings { RateHz = 100f, Latency = 0f, FailsafeTime = 0.5f });
            link.Send(0.00, Rc(throttle: 1500, aux1: 2000));
            Assert.IsFalse(link.Sample(0.00).Arm, "throttle up: refuses to arm");
            link.Send(0.01, Rc(throttle: 1000, aux1: 2000));
            Assert.IsFalse(link.Sample(0.01).Arm, "switch was already high: needs a new flip");
            link.Send(0.02, Rc(throttle: 1000, aux1: 1000));
            link.Sample(0.02);
            link.Send(0.03, Rc(throttle: 1000, aux1: 2000));
            Assert.IsTrue(link.Sample(0.03).Arm);
            link.Send(0.04, Rc(throttle: 1800, aux1: 2000));
            Assert.IsTrue(link.Sample(0.04).Arm, "stays armed with throttle up");
            link.Send(0.05, Rc(throttle: 1800, aux1: 1000));
            Assert.IsFalse(link.Sample(0.05).Arm);
        }

        [Test]
        public void RcLink_FailsafeCutsTheMotors()
        {
            var link = new RcLink(new RcLinkSettings { RateHz = 100f, Latency = 0f, FailsafeTime = 0.5f });
            link.Send(0.00, Rc());
            link.Sample(0.0);
            link.Send(0.01, Rc(throttle: 1000, aux1: 2000));
            link.Send(0.02, Rc(throttle: 1700, aux1: 2000));
            Assert.IsTrue(link.Sample(0.02).Arm);
            Assert.IsFalse(link.Failsafe);
            PilotInput lost = link.Sample(0.53);
            Assert.IsTrue(link.Failsafe);
            Assert.IsFalse(lost.Arm);
            Assert.AreEqual(0f, lost.Throttle);
            // The link comes back with the switch still high: stays disarmed until it is flipped again.
            link.Send(0.60, Rc(throttle: 1000, aux1: 2000));
            Assert.IsFalse(link.Sample(0.60).Arm);
        }

        [Test]
        public void RcLink_DropsPacketsAboveTheLinkRate()
        {
            var link = new RcLink(new RcLinkSettings { RateHz = 100f, Latency = 0f, FailsafeTime = 0.5f });
            Assert.IsTrue(link.Send(0.000, Rc()));
            Assert.IsFalse(link.Send(0.004, Rc(roll: 2000)));
            Assert.IsTrue(link.Send(0.010, Rc()));
            Assert.AreEqual(1, link.PacketsDropped);
        }

        // ---------- sensors ----------

        [Test]
        public void Imu_AtRest_ReadsGravityUp_InTheBodyFrame()
        {
            var settings = ImuSettings.Default;
            settings.GyroNoise = settings.AccelNoise = settings.GyroBias = settings.AccelBias = 0f;
            var imu = new ImuModel(settings, 1);
            QuadState s = QuadState.AtRest(Vec3.Zero, Quat.Identity);
            ImuSample o = imu.Read(s, s, 0.001f, 9.81f);
            Assert.AreEqual(0f, o.Ax, 1e-4f);
            Assert.AreEqual(0f, o.Ay, 1e-4f);
            Assert.AreEqual(9.81f, o.Az, 1e-4f);
        }

        [Test]
        public void Imu_AxesFollowTheDocumentedFrame()
        {
            var settings = ImuSettings.Default;
            settings.GyroNoise = settings.AccelNoise = settings.GyroBias = settings.AccelBias = 0f;
            var imu = new ImuModel(settings, 1);
            QuadState a = QuadState.AtRest(Vec3.Zero, Quat.Identity);
            QuadState b = a;
            // Simulator axes: X pitch (nose down +), Y yaw (right +), Z roll (left +).
            b.AngularVelocity = new Vec3(1f, 2f, 3f);
            // Accelerating forward (+Z in the simulator) at 2 m/s².
            b.Velocity = new Vec3(0f, 0f, 0.002f);
            ImuSample o = imu.Read(a, b, 0.001f, 9.81f);
            Assert.AreEqual(-3f, o.Gx, 1e-5f, "roll right is positive");
            Assert.AreEqual(1f, o.Gy, 1e-5f, "nose down is positive");
            Assert.AreEqual(-2f, o.Gz, 1e-5f, "yaw left is positive");
            Assert.AreEqual(2f, o.Ax, 1e-3f, "x is forward");
        }

        [Test]
        public void SensorNoise_SameSeedSameNumbers_DifferentSeedDifferentNumbers()
        {
            QuadState s = QuadState.AtRest(Vec3.Zero, Quat.Identity);
            var a = new ImuModel(ImuSettings.Default, 7);
            var b = new ImuModel(ImuSettings.Default, 7);
            var c = new ImuModel(ImuSettings.Default, 8);
            bool differs = false;
            for (int i = 0; i < 50; i++)
            {
                ImuSample x = a.Read(s, s, 0.001f, 9.81f), y = b.Read(s, s, 0.001f, 9.81f), z = c.Read(s, s, 0.001f, 9.81f);
                Assert.AreEqual(x.Gx, y.Gx);
                Assert.AreEqual(x.Az, y.Az);
                differs |= x.Gx != z.Gx;
            }
            Assert.IsTrue(differs);
        }

        [Test]
        public void Tof_ReportsOutOfRange()
        {
            var tof = new TofModel(TofSettings.Default, 1);
            Assert.AreEqual(1.25f, tof.Read(1.25f, out bool ok), 0.08f);
            Assert.IsTrue(ok);
            tof.Read(6f, out ok);
            Assert.IsFalse(ok, "beyond 4 m");
            tof.Read(-1f, out ok);
            Assert.IsFalse(ok, "nothing below");
            tof.Read(0.01f, out ok);
            Assert.IsFalse(ok, "closer than the minimum");
        }

        [Test]
        public void AnalogVideo_FlipsRows_KeepsColour_AndIsRepeatable()
        {
            const int w = 32, h = 8;
            var src = new Unity.Collections.NativeArray<byte>(w * h * 3, Unity.Collections.Allocator.Persistent);
            var clean = new Unity.Collections.NativeArray<byte>(src.Length, Unity.Collections.Allocator.Persistent);
            var a = new Unity.Collections.NativeArray<byte>(src.Length, Unity.Collections.Allocator.Persistent);
            var b = new Unity.Collections.NativeArray<byte>(src.Length, Unity.Collections.Allocator.Persistent);
            var c = new Unity.Collections.NativeArray<byte>(src.Length, Unity.Collections.Allocator.Persistent);
            // Bottom row (first in the source) magenta, everything else grey.
            for (int i = 0; i < src.Length; i += 3)
            {
                bool bottom = i < w * 3;
                src[i] = bottom ? (byte)255 : (byte)90; src[i + 1] = bottom ? (byte)0 : (byte)90; src[i + 2] = bottom ? (byte)200 : (byte)90;
            }
            var map = new Unity.Collections.NativeArray<int>(w * h, Unity.Collections.Allocator.Persistent);
            AnalogVideo.BuildLensMap(map, w, h, w, h, AnalogVideo.LensFocal(w, 120f, 0f), 0f);
            new AnalogVideo(AnalogVideoSettings.Clean, 1).Process(src, clean, map, w, h, 0);
            int last = (h - 1) * w * 3 + 15 * 3;
            Assert.AreEqual(255, clean[last]);
            Assert.AreEqual(0, clean[last + 1]);
            Assert.AreEqual(90, clean[15 * 3], "top row of the output is the grey part");

            new AnalogVideo(AnalogVideoSettings.Default, 5).Process(src, a, map, w, h, 3);
            new AnalogVideo(AnalogVideoSettings.Default, 5).Process(src, b, map, w, h, 3);
            new AnalogVideo(AnalogVideoSettings.Default, 5).Process(src, c, map, w, h, 4);
            CollectionAssert.AreEqual(a.ToArray(), b.ToArray(), "same seed and frame: the same picture");
            CollectionAssert.AreNotEqual(a.ToArray(), c.ToArray(), "the noise changes from frame to frame");
            CollectionAssert.AreNotEqual(a.ToArray(), clean.ToArray());
            // Still clearly magenta in the middle of the magenta row.
            Assert.Greater(a[last], 200);
            Assert.Less(a[last + 1], 60);
            Assert.Greater(a[last + 2], 140);
            src.Dispose(); clean.Dispose(); a.Dispose(); b.Dispose(); c.Dispose(); map.Dispose();
        }

        // ---------- protocol ----------

        [Test]
        public void MsgPack_RoundTrip()
        {
            var w = new MsgPackWriter();
            w.Map(6).Key("a").Int(5).Key("b").Int(-70000).Key("c").Double(1.5).Key("d").Str("сәлем").Key("e").Bool(true)
                .Key("f").Array(3).Int(1000).Int(2000).Nil();
            var seg = w.Segment;
            var map = (Dictionary<string, object>)MsgPackReader.Decode(seg.Array, 0, seg.Count);
            Assert.AreEqual(5L, map["a"]);
            Assert.AreEqual(-70000L, map["b"]);
            Assert.AreEqual(1.5, map["c"]);
            Assert.AreEqual("сәлем", map["d"]);
            Assert.AreEqual(true, map["e"]);
            var list = (List<object>)map["f"];
            Assert.AreEqual(2000L, list[1]);
            Assert.IsNull(list[2]);
        }

        [Test]
        public void MsgPack_RandomBytes_OnlyEverThrowFormatException()
        {
            var rng = new System.Random(12345);
            var buf = new byte[64];
            for (int i = 0; i < 20000; i++)
            {
                int n = rng.Next(1, buf.Length);
                rng.NextBytes(buf);
                try { MsgPackReader.Decode(buf, 0, n); }
                catch (FormatException) { }
            }
            // Deep nesting and huge declared sizes are refused, not followed.
            var deep = new byte[40];
            for (int i = 0; i < deep.Length; i++) deep[i] = 0x91;
            Assert.Throws<FormatException>(() => MsgPackReader.Decode(deep, 0, deep.Length));
            Assert.Throws<FormatException>(() => MsgPackReader.Decode(new byte[] { 0xdd, 0x7f, 0xff, 0xff, 0xff }, 0, 5));
            Assert.Throws<FormatException>(() => MsgPackReader.Decode(new byte[] { 0xdb, 0x7f, 0xff, 0xff, 0xff }, 0, 5));
        }

        [Test]
        public void Channels_RejectAnythingThatIsNotAFiniteNumber()
        {
            var into = new List<int>();
            var ok = new Dictionary<string, object> { ["rc"] = new List<object> { 1500L, 1200.4, 99999L, -3L } };
            Assert.IsTrue(BenchMessages.Channels(ok, "rc", into));
            CollectionAssert.AreEqual(new[] { 1500, 1200, 2000, 1000 }, into);
            Assert.IsFalse(BenchMessages.Channels(new Dictionary<string, object> { ["rc"] = new List<object> { 1500L, double.NaN } }, "rc", into));
            Assert.IsFalse(BenchMessages.Channels(new Dictionary<string, object> { ["rc"] = new List<object> { "1500" } }, "rc", into));
            Assert.IsFalse(BenchMessages.Channels(new Dictionary<string, object> { ["rc"] = "x" }, "rc", into));
            Assert.IsFalse(BenchMessages.Channels(new Dictionary<string, object>(), "rc", into));
        }

        [Test]
        public void CompetitionMode_AllowsOnlyTheFourMessages()
        {
            Assert.IsTrue(BenchProtocol.AllowedInCompetition(BenchProtocol.Hello));
            Assert.IsTrue(BenchProtocol.AllowedInCompetition(BenchProtocol.Start));
            Assert.IsTrue(BenchProtocol.AllowedInCompetition(BenchProtocol.Step));
            Assert.IsTrue(BenchProtocol.AllowedInCompetition(BenchProtocol.Done));
            Assert.IsFalse(BenchProtocol.AllowedInCompetition(BenchProtocol.Reset));
            Assert.IsFalse(BenchProtocol.AllowedInCompetition(BenchProtocol.Ping));
            for (int t = 7; t < 256; t++) Assert.IsFalse(BenchProtocol.AllowedInCompetition((byte)t));
        }

        // ---------- B3: LiDAR, link, lens, venues, validation, generator, oracle ----------

        [Test]
        public void Lidar_DirectionsTurnLeftFromForward()
        {
            Vec3 forward = LidarModel.Direction(0, 360), left = LidarModel.Direction(90, 360), back = LidarModel.Direction(180, 360);
            Assert.AreEqual(1f, forward.z, 1e-5f);
            Assert.AreEqual(-1f, left.x, 1e-5f, "90 degrees is to the left (the simulator's -X)");
            Assert.AreEqual(-1f, back.z, 1e-5f);
        }

        [Test]
        public void Lidar_RangeLimitsDropoutsAndSeed()
        {
            var settings = LidarSettings.Default;
            settings.Dropout = 0f;
            var a = new LidarModel(settings, 3);
            var b = new LidarModel(settings, 3);
            Assert.AreEqual(0f, a.Read(-1f), "nothing hit");
            b.Read(-1f);
            Assert.AreEqual(0f, a.Read(20f), "beyond 12 m");
            b.Read(20f);
            Assert.AreEqual(0f, a.Read(0.05f), "closer than the minimum");
            b.Read(0.05f);
            for (int i = 0; i < 100; i++)
            {
                float x = a.Read(5f), y = b.Read(5f);
                Assert.AreEqual(x, y, "same seed, same reading");
                Assert.AreEqual(5f, x, 0.25f);
            }
            settings.Dropout = 0.5f;
            var c = new LidarModel(settings, 3);
            int zeros = 0;
            for (int i = 0; i < 1000; i++)
                if (c.Read(5f) == 0f) zeros++;
            Assert.That(zeros, Is.InRange(400, 600));
        }

        [Test]
        public void RcLink_LossAndJitter_AreSeeded_AndKeepPacketOrder()
        {
            var settings = new RcLinkSettings { RateHz = 100f, Latency = 0.010f, FailsafeTime = 0.5f, Jitter = 0.030f, Loss = 0.2f };
            var a = new RcLink(settings, 9);
            var b = new RcLink(settings, 9);
            var c = new RcLink(settings, 10);
            bool differs = false;
            float last = -2f;
            for (int i = 0; i < 400; i++)
            {
                double t = i * 0.01;
                // The roll channel carries the packet's number, so order and loss can be read back.
                int[] packet = { 1500 + i, 1500, 1000, 1500, 1000, 1000 };
                a.Send(t, packet); b.Send(t, packet); c.Send(t, packet);
                float ra = a.Sample(t).Roll, rb = b.Sample(t).Roll, rc = c.Sample(t).Roll;
                Assert.AreEqual(ra, rb, "same seed, same link");
                differs |= ra != rc;
                Assert.GreaterOrEqual(ra, last, "packets never arrive out of order");
                last = ra;
            }
            Assert.IsTrue(differs, "another seed, another link");
            Assert.That(a.PacketsLost, Is.InRange(50, 115), "about 20% of 400");
        }

        [Test]
        public void Lens_WithoutDistortion_IsAPlainFlip_AndFocalMatchesTheFieldOfView()
        {
            Assert.AreEqual(320f / Mathf.Tan(60f * Mathf.Deg2Rad), AnalogVideo.LensFocal(640, 120f, 0f), 0.01f);
            var map = new Unity.Collections.NativeArray<int>(4 * 3, Unity.Collections.Allocator.Persistent);
            AnalogVideo.BuildLensMap(map, 4, 3, 4, 3, AnalogVideo.LensFocal(4, 120f, 0f), 0f);
            // Output row 0 (top) is source row 2 (the bottom-up render's last row).
            Assert.AreEqual((2 * 4 + 0) * 3, map[0]);
            Assert.AreEqual((0 * 4 + 3) * 3, map[2 * 4 + 3]);
            map.Dispose();
        }

        [Test]
        public void Lens_WithDistortion_StillSpansTheFieldOfView_AndBendsTheCorners()
        {
            const float k1 = 0.18f;
            float f = AnalogVideo.LensFocal(640, 120f, k1);
            float xd = 320f / f;
            Assert.AreEqual(Mathf.Tan(60f * Mathf.Deg2Rad), xd * (1f + k1 * xd * xd), 1e-3f, "the edge of the picture is still 60 degrees off axis");
            Assert.Greater(f, AnalogVideo.LensFocal(640, 120f, 0f), "the middle of the picture is magnified");
            AnalogVideo.LensSourceExtent(640, 480, f, k1, out float hx, out float hy);
            Assert.Greater(hx, Mathf.Tan(60f * Mathf.Deg2Rad), "the corners look further out than the edge");
            Assert.AreEqual(640f / 480f, hx / hy, 1e-3f);
        }

        [Test]
        public void AnalogVideo_WeakSignal_IsNoisier()
        {
            const int w = 64, h = 32;
            var src = new Unity.Collections.NativeArray<byte>(w * h * 3, Unity.Collections.Allocator.Persistent);
            var good = new Unity.Collections.NativeArray<byte>(src.Length, Unity.Collections.Allocator.Persistent);
            var bad = new Unity.Collections.NativeArray<byte>(src.Length, Unity.Collections.Allocator.Persistent);
            var map = new Unity.Collections.NativeArray<int>(w * h, Unity.Collections.Allocator.Persistent);
            for (int i = 0; i < src.Length; i++) src[i] = 120;
            AnalogVideo.BuildLensMap(map, w, h, w, h, AnalogVideo.LensFocal(w, 120f, 0f), 0f);
            new AnalogVideo(AnalogVideoSettings.Default, 2).Process(src, good, map, w, h, 7, 1f);
            new AnalogVideo(AnalogVideoSettings.Default, 2).Process(src, bad, map, w, h, 7, 0f);
            double eg = 0, eb = 0;
            for (int i = 0; i < src.Length; i++)
            {
                eg += Math.Abs(good[i] - 120);
                eb += Math.Abs(bad[i] - 120);
            }
            Assert.Greater(eb, eg * 2.5, "the picture at the worst signal is far noisier");
            src.Dispose(); good.Dispose(); bad.Dispose(); map.Dispose();
        }

        [Test]
        public void Imu_VibratesWithTheMotors()
        {
            QuadState still = QuadState.AtRest(Vec3.Zero, Quat.Identity), spinning = still;
            spinning.MotorSpeed = new Motor4 { M0 = 1f, M1 = 1f, M2 = 1f, M3 = 1f };
            var a = new ImuModel(ImuSettings.Default, 4);
            var b = new ImuModel(ImuSettings.Default, 4);
            double quiet = 0, loud = 0;
            for (int i = 0; i < 400; i++)
            {
                quiet += Math.Abs(a.Read(still, still, 0.001f, 9.81f).Az - 9.81f);
                loud += Math.Abs(b.Read(spinning, spinning, 0.001f, 9.81f).Az - 9.81f);
            }
            Assert.Greater(loud, quiet * 4.0);
        }

        [Test]
        public void Venues_ParkingHasColumnsAndCars_ThatBlockLinesAndPlaces()
        {
            Venue p = Venue.Parking;
            Assert.AreEqual(28, p.Boxes.FindAll(b => b.Kind == "column").Count);
            Assert.Greater(p.Boxes.FindAll(b => b.Kind == "car").Count, 20);
            Assert.IsFalse(p.Free(8f, 4f, 0.2f), "inside a column");
            Assert.IsTrue(p.Free(4f, 0f, 0.5f), "the middle lane");
            Assert.IsFalse(p.ClearLine(new Vector2(6f, 4f), new Vector2(10f, 4f), 0.1f), "a column stands in between");
            Assert.IsTrue(p.ClearLine(new Vector2(-20f, 0f), new Vector2(20f, 0f), 0.1f), "the middle lane is clear end to end");
            Assert.IsFalse(Venue.Hall.Inside(19.8f, 0f, 0.5f));
        }

        [Test]
        public void PublicTracks_AllValidate_AndMatchThePlan()
        {
            string[] names = { "H1_Oval", "H2_Eight", "H3_Slalom", "X1_Mixed", "P1_Columns", "P2_Maze" };
            int[] gates = { 6, 8, 7, 10, 6, 9 };
            for (int i = 0; i < names.Length; i++)
            {
                string path = $"Assets/StreamingAssets/Bench/Tracks/{names[i]}.benchtrack.json";
                Assert.IsTrue(System.IO.File.Exists(path), names[i]);
                BenchTrack t = BenchTrack.Parse(System.IO.File.ReadAllText(path));
                Assert.AreEqual(gates[i], t.gates.Count, names[i]);
                Assert.AreEqual(names[i].StartsWith("P") ? "parking" : "hall", t.venue);
                var issues = TrackValidator.Validate(t);
                Assert.IsFalse(TrackValidator.HasErrors(issues), names[i] + ": " + string.Join("; ", issues));
            }
            BenchTrack mixed = BenchTrack.Parse(System.IO.File.ReadAllText("Assets/StreamingAssets/Bench/Tracks/X1_Mixed.benchtrack.json"));
            var colours = new HashSet<string>();
            foreach (TrackGate g in mixed.gates) colours.Add(g.led.color);
            Assert.GreaterOrEqual(colours.Count, 4, "different LED colours");
            Assert.GreaterOrEqual(mixed.props.Count, 6, "coloured distractors");
        }

        private static BenchTrack Oval()
        {
            return BenchTrack.Parse(System.IO.File.ReadAllText("Assets/StreamingAssets/Bench/Tracks/H1_Oval.benchtrack.json"));
        }

        [Test]
        public void Validator_CatchesTheUsualMistakes()
        {
            BenchTrack t = Oval();
            Assert.IsFalse(TrackValidator.HasErrors(TrackValidator.Validate(t)));

            t = Oval();
            t.gates.RemoveRange(4, 2);
            StringAssert.Contains("at least 5", string.Join(";", TrackValidator.Validate(t)));

            t = Oval();
            t.gates[0].position = new[] { 19.6f, 0f, 0f };
            StringAssert.Contains("too close to a wall", string.Join(";", TrackValidator.Validate(t)));

            t = Oval();
            t.gates[1].position = new[] { t.gates[0].position[0] + 1f, 0f, t.gates[0].position[2] };
            StringAssert.Contains("too close to G1", string.Join(";", TrackValidator.Validate(t)));

            t = Oval();
            t.start.yaw = 180f; // facing the wall behind the pad
            StringAssert.Contains("no gate is in view", string.Join(";", TrackValidator.Validate(t)));

            t = Oval();
            t.venue = "parking";
            t.gates[2].position = new[] { 8f, 0f, 4f };
            StringAssert.Contains("too close to a column", string.Join(";", TrackValidator.Validate(t)));

            t = Oval();
            t.gates[3].led.color = "pink-ish";
            StringAssert.Contains("#RRGGBB", string.Join(";", TrackValidator.Validate(t)));
        }

        [Test]
        public void Generator_IsRepeatable_Valid_AndHarderMeansMore()
        {
            foreach (string venue in Venue.Ids)
                for (int difficulty = 1; difficulty <= 3; difficulty++)
                    for (long seed = 1; seed <= 6; seed++)
                    {
                        BenchTrack a = TrackGenerator.Generate(venue, seed, difficulty), b = TrackGenerator.Generate(venue, seed, difficulty);
                        Assert.AreEqual(a.ToJson(), b.ToJson(), "same seed, same track");
                        Assert.IsFalse(TrackValidator.HasErrors(TrackValidator.Validate(a)), a.id);
                        Assert.GreaterOrEqual(a.gates.Count, 5 + difficulty);
                        // It survives a round trip through the file format.
                        Assert.AreEqual(a.gates.Count, BenchTrack.Parse(a.ToJson()).gates.Count);
                    }
            Assert.AreNotEqual(TrackGenerator.Generate("hall", 1, 2).ToJson(), TrackGenerator.Generate("hall", 2, 2).ToJson());
            var colours = new HashSet<string>();
            foreach (TrackGate g in TrackGenerator.Generate("hall", 3, 3).gates) colours.Add(g.led.color);
            Assert.Greater(colours.Count, 1, "hard tracks mix LED colours");
        }

        [Test]
        public void Oracle_FliesTheOval_WithTheRealFlightModel()
        {
            // No collision layers here: this checks the pilot, the flight model and the referee together.
            // The venue's walls, columns and gates are checked by Ushqyr > Bench > Check Public Tracks.
            OracleResult r = BenchOracle.Fly(Oval(), 0, 60f);
            Assert.IsTrue(r.Lap, $"no lap: {r.Points} points");
            Assert.AreEqual(6, r.Points);
            Assert.Less(r.LapTime, 40.0);
            Assert.Greater(r.Path.Count, 100);
            OracleResult again = BenchOracle.Fly(Oval(), 0, 60f);
            Assert.AreEqual(r.LapTime, again.LapTime, "the oracle run is repeatable");
        }

        // ---------- results, manifest, leaderboard ----------

        private static string TempFolder()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ushqyr_bench_test_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        private static readonly byte[] Key = System.Text.Encoding.ASCII.GetBytes("a test key of 32 bytes..........");

        [Test]
        public void Manifest_VerifiesUntouchedFolder_AndCatchesEveryKindOfEdit()
        {
            string dir = TempFolder();
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "result.json"), "{\"points\":3}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "events.csv"), "t,type\n1.0,gate\n");
                var result = new AttemptResult { team = "A", track_hash = "abc", seed = 7 };
                AttemptManifest.Write(dir, result, Key);

                Assert.AreEqual(ManifestState.Verified, AttemptManifest.Verify(dir, Key, out _));
                Assert.AreEqual(ManifestState.ChecksumsOnly, AttemptManifest.Verify(dir, null, out _), "no key here: checksums only");
                Assert.AreEqual(ManifestState.Modified, AttemptManifest.Verify(dir, System.Text.Encoding.ASCII.GetBytes("another key of 32 bytes........."), out _));

                // A changed score.
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "result.json"), "{\"points\":9}");
                Assert.AreEqual(ManifestState.Modified, AttemptManifest.Verify(dir, Key, out string detail));
                StringAssert.Contains("result.json", detail);

                // A changed score with the manifest rewritten to match, but without the key: the signature gives it away.
                AttemptManifest.Write(dir, result, null);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, AttemptManifest.SignatureName), new string('0', 64));
                Assert.AreEqual(ManifestState.Modified, AttemptManifest.Verify(dir, Key, out _));

                // An added file, a removed file.
                AttemptManifest.Write(dir, result, Key);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "extra.txt"), "x");
                Assert.AreEqual(ManifestState.Modified, AttemptManifest.Verify(dir, Key, out _));
                System.IO.File.Delete(System.IO.Path.Combine(dir, "extra.txt"));
                System.IO.File.Delete(System.IO.Path.Combine(dir, "events.csv"));
                Assert.AreEqual(ManifestState.Modified, AttemptManifest.Verify(dir, Key, out _));

                // A signed attempt whose signature was simply deleted.
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "events.csv"), "t,type\n1.0,gate\n");
                AttemptManifest.Write(dir, result, Key);
                System.IO.File.Delete(System.IO.Path.Combine(dir, AttemptManifest.SignatureName));
                Assert.AreEqual(ManifestState.Modified, AttemptManifest.Verify(dir, Key, out _));
            }
            finally { System.IO.Directory.Delete(dir, true); }
        }

        [Test]
        public void Manifest_MissingIsReported()
        {
            string dir = TempFolder();
            try { Assert.AreEqual(ManifestState.Missing, AttemptManifest.Verify(dir, Key, out _)); }
            finally { System.IO.Directory.Delete(dir, true); }
        }

        private static AttemptResult Res(string team, string track, int points, double time, string status = "finished", int attempt = 1) =>
            new AttemptResult { team = team, track_id = track, points = points, time = time, status = status, attempt = attempt };

        [Test]
        public void Leaderboard_MorePointsFirst_ThenLessTime()
        {
            var rows = Leaderboard.Rank(new[] { Res("B", "T1", 5, 30.0), Res("A", "T1", 5, 28.5), Res("C", "T1", 6, 80.0) }, "T1");
            Assert.AreEqual("C", rows[0].Team);
            Assert.AreEqual("A", rows[1].Team);
            Assert.AreEqual("B", rows[2].Team);
            Assert.AreEqual(new[] { 1, 2, 3 }, new[] { rows[0].Rank, rows[1].Rank, rows[2].Rank });
        }

        [Test]
        public void Leaderboard_BestAttemptPerTrack_InvalidRunsDontCount_OverallAddsTracks()
        {
            var results = new[]
            {
                Res("A", "T1", 3, 20.0, attempt: 1), Res("A", "T1", 5, 40.0, attempt: 2), Res("A", "T2", 4, 25.0),
                Res("B", "T1", 6, 50.0), Res("B", "T2", 9, 10.0, status: "invalid"), Res("B", "T2", 2, 12.0, attempt: 2),
            };
            var t1 = Leaderboard.Rank(results, "T1");
            Assert.AreEqual("B", t1[0].Team);
            Assert.AreEqual(5, t1[1].Points, "A's better attempt on T1");

            var overall = Leaderboard.Rank(results, null);
            Assert.AreEqual("A", overall[0].Team);
            Assert.AreEqual(9, overall[0].Points);
            Assert.AreEqual(65.0, overall[0].Time, 1e-9);
            Assert.AreEqual(8, overall[1].Points, "the invalid 9-point run is not counted");
        }

        [Test]
        public void Leaderboard_EqualPointsAndTime_ShareARank()
        {
            var rows = Leaderboard.Rank(new[] { Res("A", "T", 4, 10.0), Res("B", "T", 4, 10.0), Res("C", "T", 1, 5.0) }, "T");
            Assert.AreEqual(1, rows[0].Rank);
            Assert.AreEqual(1, rows[1].Rank);
            Assert.AreEqual(3, rows[2].Rank);
        }

        [Test]
        public void Csv_QuotesWhatNeedsQuoting_AndDefusesFormulas()
        {
            var r = Res("Team \"Қыран\", SDU", "H1_Oval", 6, 24.528);
            r.date_utc = "2026-11-19T10:00:00Z";
            string csv = Leaderboard.ToCsv(new[] { r, Res("=cmd()", "T", 1, 1.5) });
            string[] lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(Leaderboard.CsvHeader, lines[0]);
            StringAssert.StartsWith("\"Team \"\"Қыран\"\", SDU\",H1_Oval,1,6,24.528,finished,2026-11-19T10:00:00Z", lines[1]);
            StringAssert.StartsWith("'=cmd(),", lines[2]);

            string semi = Leaderboard.ToCsv(new[] { r }, ';');
            StringAssert.Contains(";6;24,528;finished;", semi);
            StringAssert.StartsWith("team;track;attempt", semi);
        }

        [Test]
        public void RulesHash_IgnoresTokenAndPort_ButNotTheRules()
        {
            BenchRules a = BenchRules.Parse(null), b = BenchRules.Parse("{\"token\":\"secret\",\"port\":6000}"), c = BenchRules.Parse("{\"rc_latency_ms\":40}");
            Assert.AreEqual(a.Hash(), b.Hash());
            Assert.AreNotEqual(a.Hash(), c.Hash());
            Assert.AreEqual("secret", b.token, "hashing must not change the rules");
        }

        [Test]
        public void ScoringReasons_SayWhy()
        {
            var e = new GateEvent { Kind = GateEventKind.RepeatIgnored, OthersSince = 2 };
            Assert.AreEqual("repeat ignored: only 2 other gates since (needs 4)", GateEventText.Reason(e, 4));
            e.OthersSince = 1;
            StringAssert.Contains("1 other gate since", GateEventText.Reason(e, 4));
        }

        // ---------- track file ----------

        [Test]
        public void H1Oval_IsAValidTrack_WithEveryNextGateInView()
        {
            string path = "Assets/StreamingAssets/Bench/Tracks/H1_Oval.benchtrack.json";
            Assert.IsTrue(System.IO.File.Exists(path), "run Ushqyr > Bench > Build Everything");
            BenchTrack t = BenchTrack.Parse(System.IO.File.ReadAllText(path));
            Assert.AreEqual("H1_Oval", t.id);
            Assert.AreEqual("hall", t.venue);
            Assert.AreEqual(6, t.gates.Count);
            Assert.AreEqual(64, t.Hash.Length);
            Assert.GreaterOrEqual(t.gates.Count, t.rules.repeat_window + 1, "fewer gates than the repeat window + 1 would stall scoring");

            for (int i = 0; i < t.gates.Count; i++)
            {
                TrackGate g = t.gates[i], next = t.gates[(i + 1) % t.gates.Count];
                Vector3 p = BenchTrack.V(g.position), q = BenchTrack.V(next.position);
                // Inside the hall with room to the walls.
                Assert.Less(Mathf.Abs(p.x), 20f - 2.5f);
                Assert.Less(Mathf.Abs(p.z), 10f - 1.5f);
                Assert.Greater(Vector3.Distance(p, q), 5f, "gates too close");
                // Leaving gate i along its axis, the next gate is within the camera's half field of view (60°)
                // and is approached within 45° of its own axis.
                Vector3 forward = Quaternion.Euler(0f, g.yaw, 0f) * Vector3.forward;
                Vector3 nextForward = Quaternion.Euler(0f, next.yaw, 0f) * Vector3.forward;
                Assert.Less(Vector3.Angle(forward, q - p), 55f, $"{next.id} not in view from {g.id}");
                Assert.Less(Vector3.Angle(nextForward, q - p), 45f, $"{next.id} approached too obliquely");
            }
        }

        [Test]
        public void TrackFile_RefusesBrokenInput()
        {
            Assert.Throws<FormatException>(() => BenchTrack.Parse("{\"schema\":1,\"gates\":[]}"));
            Assert.Throws<FormatException>(() => BenchTrack.Parse("{\"schema\":99,\"gates\":[{\"id\":\"a\",\"position\":[0,0,0]}]}"));
            Assert.Throws<FormatException>(() => BenchTrack.Parse(
                "{\"schema\":1,\"start\":{\"position\":[0,0,0]},\"gates\":[{\"id\":\"a\",\"position\":[0,0,0]},{\"id\":\"a\",\"position\":[1,0,0]}]}"));
        }

        [Test]
        public void Rules_DefaultsAreTheDocumentedOnes_AndFilesOverrideThem()
        {
            BenchRules d = BenchRules.Parse(null);
            Assert.AreEqual("training", d.mode);
            Assert.AreEqual(640, d.camera_width);
            Assert.AreEqual(480, d.camera_height);
            Assert.AreEqual(30f, d.camera_fps);
            Assert.AreEqual(4, d.repeat_window);
            Assert.AreEqual(120f, d.time_limit);
            Assert.IsFalse(d.Competition);
            BenchRules c = BenchRules.Parse("{\"mode\":\"competition\",\"rc_latency_ms\":40}");
            Assert.IsTrue(c.Competition);
            Assert.AreEqual(40f, c.rc_latency_ms);
            Assert.AreEqual(640, c.camera_width, "untouched fields keep their defaults");
        }
    }
}
