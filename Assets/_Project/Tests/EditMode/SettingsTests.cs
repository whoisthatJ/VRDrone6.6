using System.IO;
using GateRush.Core;
using GateRush.Flight;
using NUnit.Framework;

namespace GateRush.Tests
{
    public class SettingsTests
    {
        [Test]
        public void Json_RoundTrip_KeepsEveryGroup()
        {
            var s = new GameSettings
            {
                PilotName = "Ace",
                QuadId = "whoop65",
                TrackId = "hangar",
                UseCustomRates = true,
                Rates = RateProfile.ActualDefault,
            };
            s.Camera.TiltDegrees = 40f;
            s.Camera.ViewMode = 2;
            s.Input.TouchThrottle = 2;
            s.Comfort.RefreshRate = 90f;
            s.Physics.CrashSpeed = 12f;

            var copy = GameSettings.FromJson(s.ToJson());
            Assert.AreEqual("Ace", copy.PilotName);
            Assert.AreEqual("whoop65", copy.QuadId);
            Assert.AreEqual("hangar", copy.TrackId);
            Assert.IsTrue(copy.UseCustomRates);
            Assert.AreEqual(RatesType.Actual, copy.Rates.Type);
            Assert.AreEqual(670f, copy.Rates.Roll.Rate);
            Assert.AreEqual(40f, copy.Camera.TiltDegrees);
            Assert.AreEqual(2, copy.Camera.ViewMode);
            Assert.AreEqual(2, copy.Input.TouchThrottle);
            Assert.AreEqual(90f, copy.Comfort.RefreshRate);
            Assert.AreEqual(12f, copy.Physics.CrashSpeed);
        }

        [Test]
        public void FromJson_MissingFieldsKeepDefaults()
        {
            var s = GameSettings.FromJson("{\"PilotName\":\"Old\"}");
            Assert.AreEqual("Old", s.PilotName);
            Assert.AreEqual(25f, s.Camera.TiltDegrees);
            Assert.AreEqual(72f, s.Comfort.RefreshRate);
            Assert.AreEqual("race5", s.QuadId);
        }

        [Test]
        public void FromJson_EmptyOrGarbage_GivesDefaults()
        {
            Assert.AreEqual("Pilot", GameSettings.FromJson(null).PilotName);
            Assert.AreEqual("Pilot", GameSettings.FromJson("").PilotName);
        }

        [Test]
        public void Clamp_FixesOutOfRangeValues()
        {
            var s = new GameSettings { PilotName = "  " };
            s.Camera.TiltDegrees = 99f;
            s.Camera.HorizontalFov = 10f;
            s.Comfort.RefreshRate = 85f;
            s.Physics.DragScale = 9f;
            s.Rates.Roll = new AxisRates(5f, 2f, 3f);
            s.Clamp();
            Assert.AreEqual("Pilot", s.PilotName);
            Assert.AreEqual(60f, s.Camera.TiltDegrees);
            Assert.AreEqual(90f, s.Camera.HorizontalFov);
            Assert.AreEqual(90f, s.Comfort.RefreshRate);
            Assert.AreEqual(2f, s.Physics.DragScale);
            Assert.AreEqual(2.55f, s.Rates.Roll.RcRate);
            Assert.AreEqual(0.99f, s.Rates.Roll.Rate);
            Assert.AreEqual(1f, s.Rates.Roll.Expo);
        }

        [Test]
        public void SaveAndLoad_FileRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "gaterush-settings-test.json");
            try
            {
                var s = new GameSettings { PilotName = "Disk" };
                SaveSystem.Save(s, path);
                SaveSystem.Save(s, path); // second save goes through File.Replace
                Assert.AreEqual("Disk", SaveSystem.Load(path).PilotName);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
