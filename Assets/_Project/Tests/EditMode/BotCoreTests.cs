using System.Linq;
using Ushqyr.AI;
using Ushqyr.Flight;
using NUnit.Framework;
using UnityEngine;

namespace Ushqyr.Tests
{
    /// <summary>P4 bot core: inverse rate curves, stick synthesis, flight envelope, racing-line baker.</summary>
    public sealed class BotCoreTests
    {
        private static readonly AxisRates[] BetaflightCurves =
        {
            new AxisRates(1.0f, 0.7f, 0f), new AxisRates(1.0f, 0.7f, 0.3f), new AxisRates(0.8f, 0.5f, 0.6f), new AxisRates(1.5f, 0.75f, 0.2f),
        };

        private static readonly AxisRates[] ActualCurves =
        {
            new AxisRates(70f, 670f, 0.54f), new AxisRates(200f, 800f, 0f), new AxisRates(120f, 1000f, 0.8f),
        };

        [Test]
        public void InverseRates_RoundTripWithinHalfAPercent([Values(RatesType.Betaflight, RatesType.Actual)] RatesType type)
        {
            var curves = type == RatesType.Betaflight ? BetaflightCurves : ActualCurves;
            foreach (var c in curves)
            {
                float full = Rates.DegPerSec(type, c, 1f);
                for (float stick = -1f; stick <= 1.0001f; stick += 0.01f)
                {
                    float rate = Rates.DegPerSec(type, c, stick);
                    float back = RateInverse.StickFor(type, c, rate);
                    float again = Rates.DegPerSec(type, c, back);
                    // Relative to the asked rate (with a floor of 0.5 % of full scale near the centre).
                    float err = Mathf.Abs(again - rate) / Mathf.Max(Mathf.Abs(rate), full * 0.01f);
                    Assert.Less(err, 0.005f, $"{type} {c.RcRate}/{c.Rate}/{c.Expo} stick {stick:F2}: {rate:F1} -> {back:F4} -> {again:F1}");
                }
            }
        }

        [Test]
        public void InverseRates_SaturateBeyondFullStick()
        {
            var c = BetaflightCurves[0];
            Assert.AreEqual(1f, RateInverse.StickFor(RatesType.Betaflight, c, 5000f));
            Assert.AreEqual(-1f, RateInverse.StickFor(RatesType.Betaflight, c, -5000f));
            Assert.AreEqual(0f, RateInverse.StickFor(RatesType.Betaflight, c, 0f));
        }

        [Test]
        public void SticksFor_ReproduceTheAcroSetpoint()
        {
            var p = QuadProfile.Race5Inch;
            var want = new Vec3(3f, -2f, 5f);
            RateInverse.SticksFor(p.Rates, want, out float pitch, out float yaw, out float roll);
            var input = new PilotInput { Pitch = pitch, Yaw = yaw, Roll = roll, Arm = true, Mode = FlightMode.Acro };
            var sp = QuadSimulator.ComputeSetpoint(QuadState.AtRest(Vec3.Zero, Quat.Identity), input, p);
            Assert.AreEqual(want.x, sp.x, 0.02f);
            Assert.AreEqual(want.y, sp.y, 0.02f);
            Assert.AreEqual(want.z, sp.z, 0.02f);
        }

        [Test]
        public void ThrottleFor_HoverThrustGivesTheHoverCommand()
        {
            var p = QuadProfile.Race5Inch;
            float weight = p.Mass * 9.81f;
            float thr = RateInverse.ThrottleFor(p.Motors, weight, 0f);
            Assert.AreEqual(MotorModel.HoverCommand(p.Motors, weight), thr, 1e-3f);
        }

        [Test]
        public void Envelope_IsPhysical()
        {
            var env = new FlightEnvelope(QuadProfile.Race5Inch);
            Assert.Greater(env.ThrustToWeight, 2f);
            Assert.That(env.TopSpeed, Is.InRange(15f, 55f), "top speed");
            Assert.Greater(env.Accel(0f, 0f), env.Accel(20f, 0f), "less push at speed");
            Assert.Greater(env.Accel(10f, -0.5f), env.Accel(10f, 0.5f), "diving is easier than climbing");
            Assert.Greater(env.Lateral(5f), 9.81f, "can turn harder than 1 g");
            Assert.LessOrEqual(env.Accel(env.TopSpeed + 1f, 0f), 0.1f, "no push beyond top speed");
        }

        [Test]
        public void Baker_RingOfGates_PassesEveryOpeningInsideTheEnvelope()
        {
            // 8 gates on a 30 m circle, flown counter-clockwise, no obstacles.
            const int n = 8;
            const float r = 30f;
            var gates = new GateSpec[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var pos = new Vector3(Mathf.Cos(a) * r, 2f, Mathf.Sin(a) * r);
                var fwd = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                gates[i] = new GateSpec { Centre = pos, Normal = fwd, Radius = 0.5f };
            }
            var guide = RacingLineBaker.FromGates(gates, gates[0].Centre - gates[0].Normal * 5f, 1f);
            var env = new FlightEnvelope(QuadProfile.Race5Inch);
            var line = RacingLineBaker.Bake(guide, gates, env);

            Assert.IsTrue(line.IsValid);
            Assert.AreEqual(n, line.GateSamples.Length);
            for (int k = 0; k < n; k++)
            {
                var q = line.Points[line.GateSamples[k]] - gates[k].Centre;
                var radial = q - gates[k].Normal * Vector3.Dot(q, gates[k].Normal);
                Assert.LessOrEqual(radial.magnitude, gates[k].Radius + 0.05f, $"gate {k}");
                Assert.Greater(Vector3.Dot(line.Tangents[line.GateSamples[k]], gates[k].Normal), 0.9f, $"gate {k} direction");
            }
            // Gate order goes forward round the loop.
            for (int k = 1; k < n; k++) Assert.Greater(line.GateSamples[k], line.GateSamples[k - 1]);
            float maxLat = line.Points.Select((_, i) => line.Speed[i] * line.Speed[i] * line.Curvature[i].magnitude).Max();
            Assert.LessOrEqual(maxLat, RacingLineBaker.Grip * env.Lateral(0f) + 1f, "centripetal within the grip");
            Assert.LessOrEqual(line.Speed.Max(), env.TopSpeed);
            Assert.Greater(line.LapTime, 2f * Mathf.PI * r / env.TopSpeed);
        }

        [Test]
        public void Difficulty_LevelsAreOrdered()
        {
            float last = 0f;
            foreach (var level in new[] { BotLevel.Rookie, BotLevel.Amateur, BotLevel.Pilot, BotLevel.Pro, BotLevel.Elite })
            {
                var p = BotDifficultyProfile.Default(level);
                Assert.Greater(p.Pace, last, level.ToString());
                last = p.Pace;
            }
            Assert.LessOrEqual(BotDifficultyProfile.Default(BotLevel.Elite).MistakeChance, 0f);
        }
    }
}
