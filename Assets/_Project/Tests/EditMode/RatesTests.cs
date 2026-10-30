using GateRush.Flight;
using NUnit.Framework;

namespace GateRush.Tests
{
    public class RatesTests
    {
        private const float Tol = 0.05f;

        [Test]
        public void Betaflight_FullStick_Rc1_Super07_Expo0_Is667()
        {
            var r = new AxisRates(1.0f, 0.7f, 0f);
            Assert.AreEqual(666.67f, Rates.Betaflight(r, 1f), Tol);
            Assert.AreEqual(-666.67f, Rates.Betaflight(r, -1f), Tol);
        }

        [Test]
        public void Betaflight_HalfStick_Rc1_Super07()
        {
            // 200 * 0.5 / (1 - 0.5 * 0.7) = 153.85
            Assert.AreEqual(153.85f, Rates.Betaflight(new AxisRates(1.0f, 0.7f, 0f), 0.5f), Tol);
        }

        [Test]
        public void Betaflight_NoSuper_IsLinear200xRcRate()
        {
            var r = new AxisRates(1.2f, 0f, 0f);
            Assert.AreEqual(240f, Rates.Betaflight(r, 1f), Tol);
            Assert.AreEqual(120f, Rates.Betaflight(r, 0.5f), Tol);
        }

        [Test]
        public void Betaflight_Expo_ShapesCentreButNotEndpoint()
        {
            var r = new AxisRates(1.0f, 0.7f, 0.5f);
            // cmd = 0.5*0.125*0.5 + 0.5*0.5 = 0.28125 -> 200*0.28125/0.65 = 86.54
            Assert.AreEqual(86.54f, Rates.Betaflight(r, 0.5f), Tol);
            Assert.AreEqual(666.67f, Rates.Betaflight(r, 1f), Tol);
        }

        [Test]
        public void Betaflight_RcRateAbove2_IsBoosted()
        {
            // rcRate 2.5 -> 2.5 + 14.54*0.5 = 9.77 -> 200 * 9.77 = 1954
            Assert.AreEqual(1954f, Rates.Betaflight(new AxisRates(2.5f, 0f, 0f), 1f), 0.5f);
        }

        [Test]
        public void Rates_AreClampedTo1998()
        {
            Assert.AreEqual(1998f, Rates.DegPerSec(RatesType.Betaflight, new AxisRates(2.55f, 0.9f, 0f), 1f), Tol);
        }

        [Test]
        public void Actual_FullStickIsMaxRate_CentreSlopeIsCentreSensitivity()
        {
            var r = new AxisRates(70f, 670f, 0.54f);
            Assert.AreEqual(670f, Rates.Actual(r, 1f), Tol);
            Assert.AreEqual(-670f, Rates.Actual(r, -1f), Tol);
            // Near centre the curve's slope is the centre sensitivity (the expo term is ~|x|·x, negligible here).
            Assert.AreEqual(70f * 0.001f, Rates.Actual(r, 0.001f), 0.0005f);
        }

        [Test]
        public void Actual_HalfStick_MatchesBetaflightFormula()
        {
            // expof = 0.5 * (0.5^5 * 0.54 + 0.5 * 0.46) = 0.123438; rate = 0.5*70 + 600*0.123438 = 109.06
            Assert.AreEqual(109.06f, Rates.Actual(new AxisRates(70f, 670f, 0.54f), 0.5f), Tol);
        }

        [Test]
        public void ZeroStick_IsZeroRate()
        {
            Assert.AreEqual(0f, Rates.DegPerSec(RatesType.Betaflight, new AxisRates(1f, 0.7f, 0.3f), 0f));
            Assert.AreEqual(0f, Rates.DegPerSec(RatesType.Actual, new AxisRates(70f, 670f, 0.5f), 0f));
        }
    }
}
