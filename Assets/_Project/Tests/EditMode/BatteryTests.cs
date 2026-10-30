using GateRush.Flight;
using NUnit.Framework;

namespace GateRush.Tests
{
    public class BatteryTests
    {
        [Test]
        public void OpenCircuitVoltage_FollowsLipoCurve()
        {
            Assert.AreEqual(4.2f, BatteryModel.CellOpenCircuit(1f), 1e-4f);
            Assert.AreEqual(BatteryModel.CellEmpty, BatteryModel.CellOpenCircuit(0f), 1e-4f);
            float mid = BatteryModel.CellOpenCircuit(0.5f);
            Assert.That(mid, Is.InRange(3.8f, 3.95f), "flat middle of the curve");
            for (float soc = 0f; soc < 1f; soc += 0.05f)
                Assert.That(BatteryModel.CellOpenCircuit(soc + 0.05f), Is.GreaterThanOrEqualTo(BatteryModel.CellOpenCircuit(soc)), "monotonic");
        }

        [Test]
        public void Current_GrowsWithMotorSpeed_ZeroWhenDisarmed()
        {
            var b = BatterySettings.Race4S;
            var idle = new Motor4 { M0 = 0.05f, M1 = 0.05f, M2 = 0.05f, M3 = 0.05f };
            var full = new Motor4 { M0 = 1f, M1 = 1f, M2 = 1f, M3 = 1f };
            Assert.AreEqual(0f, BatteryModel.Current(b, full, armed: false));
            Assert.AreEqual(b.IdleCurrent + b.MaxCurrent, BatteryModel.Current(b, full, true), 1e-3f);
            Assert.That(BatteryModel.Current(b, idle, true), Is.LessThan(1f));
        }

        [Test]
        public void Step_DrainsCapacity_AndSagsUnderLoad()
        {
            var b = BatterySettings.Race4S;
            float used = 0f;
            float rest = BatteryModel.Step(b, ref used, 0f, 0.001f);
            float loaded = BatteryModel.Step(b, ref used, 100f, 0.001f);
            Assert.AreEqual(16.8f, rest, 1e-3f);
            Assert.AreEqual(100f * b.InternalResistance, rest - loaded, 0.01f, "I*R sag");

            // 60 s at 40 A = 667 mAh used.
            used = 0f;
            for (int i = 0; i < 60000; i++) BatteryModel.Step(b, ref used, 40f, 0.001f);
            Assert.AreEqual(666.7f, used, 1f);
        }

        [Test]
        public void ThrustScale_IsOneOnAFullRestingPack()
        {
            var b = BatterySettings.Race4S;
            Assert.AreEqual(1f, BatteryModel.ThrustScale(b, 16.8f), 1e-4f);
            Assert.That(BatteryModel.ThrustScale(b, 14f), Is.InRange(0.65f, 0.75f));
        }

        [Test]
        public void Simulator_WithSag_LosesClimbOnASaggingPack_AndTracksVoltage()
        {
            var p = QuadProfile.Race5Inch;
            var noSag = PhysicsSettings.Default;
            var sag = PhysicsSettings.Default;
            sag.BatterySag = true;

            QuadState Fly(in PhysicsSettings env)
            {
                var s = QuadState.AtRest(new Vec3(0f, 10f, 0f), Quat.Identity);
                var input = PilotInput.Armed(1f);
                for (int i = 0; i < 1000; i++) QuadSimulator.Step(ref s, input, p, env, 0.001f);
                return s;
            }

            var a = Fly(noSag);
            var b = Fly(sag);
            Assert.That(b.Position.y, Is.LessThan(a.Position.y), "full-throttle punch is weaker with sag");
            Assert.That(b.BatteryVoltage, Is.InRange(12f, 16.5f));
            Assert.That(b.BatteryCurrent, Is.GreaterThan(50f));
            Assert.That(b.BatteryUsedMah, Is.GreaterThan(10f));
        }
    }
}
