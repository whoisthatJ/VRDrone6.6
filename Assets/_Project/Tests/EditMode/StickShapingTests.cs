using GateRush.Input;
using NUnit.Framework;

namespace GateRush.Tests
{
    public class StickShapingTests
    {
        [Test]
        public void Deadband_ZeroesCentre_AndStillReachesFullScale()
        {
            var s = new StickShaping { Deadband = 0.1f, Expo = 0f };
            Assert.AreEqual(0f, s.Apply(0.05f));
            Assert.AreEqual(0f, s.Apply(-0.1f));
            Assert.AreEqual(1f, s.Apply(1f), 1e-6f);
            Assert.AreEqual(-1f, s.Apply(-1f), 1e-6f);
            Assert.AreEqual(0.5f, s.Apply(0.55f), 1e-5f);
        }

        [Test]
        public void Expo_SoftensCentre_KeepsEndpoints()
        {
            var s = new StickShaping { Deadband = 0f, Expo = 0.5f };
            // 0.5^3 * 0.5 + 0.5 * 0.5 = 0.3125
            Assert.AreEqual(0.3125f, s.Apply(0.5f), 1e-5f);
            Assert.AreEqual(-0.3125f, s.Apply(-0.5f), 1e-5f);
            Assert.AreEqual(1f, s.Apply(1f), 1e-6f);
        }

        [Test]
        public void Apply_ClampsOutOfRangeInput()
        {
            Assert.AreEqual(1f, StickShaping.Default.Apply(1.7f), 1e-6f);
        }

        [Test]
        public void ThrottleMapping_Direct()
        {
            Assert.AreEqual(0f, StickShaping.MapThrottle(ThrottleMapping.Direct, -1f, 0.3f));
            Assert.AreEqual(0.5f, StickShaping.MapThrottle(ThrottleMapping.Direct, 0f, 0.3f));
            Assert.AreEqual(1f, StickShaping.MapThrottle(ThrottleMapping.Direct, 1f, 0.3f));
        }

        [Test]
        public void ThrottleMapping_SpringCentre_CentreIsHover()
        {
            Assert.AreEqual(0.3f, StickShaping.MapThrottle(ThrottleMapping.SpringCenterHover, 0f, 0.3f), 1e-6f);
            Assert.AreEqual(0f, StickShaping.MapThrottle(ThrottleMapping.SpringCenterHover, -1f, 0.3f), 1e-6f);
            Assert.AreEqual(1f, StickShaping.MapThrottle(ThrottleMapping.SpringCenterHover, 1f, 0.3f), 1e-6f);
            Assert.AreEqual(0.65f, StickShaping.MapThrottle(ThrottleMapping.SpringCenterHover, 0.5f, 0.3f), 1e-6f);
            Assert.AreEqual(0.15f, StickShaping.MapThrottle(ThrottleMapping.SpringCenterHover, -0.5f, 0.3f), 1e-6f);
        }

        [Test]
        public void ThrottleMapping_Trigger()
        {
            Assert.AreEqual(0.4f, StickShaping.MapThrottle(ThrottleMapping.Trigger, 0.4f, 0.3f));
            Assert.AreEqual(0f, StickShaping.MapThrottle(ThrottleMapping.Trigger, -0.2f, 0.3f));
        }
    }
}
