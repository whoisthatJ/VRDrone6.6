using GateRush.Core;
using NUnit.Framework;

namespace GateRush.Tests
{
    public class GraphicsPrefsTests
    {
        [TestCase(2048, "Low")]    // GTX 1050 class
        [TestCase(4096, "Medium")] // GTX 1650 Ti class laptop
        [TestCase(6144, "High")]   // RTX 2060
        [TestCase(8192, "High")]   // RTX 3070
        [TestCase(12288, "Ultra")] // RTX 3060 12 GB / 4070
        public void DetectPreset_UsesVideoMemory(int vramMb, string expected)
        {
            Assert.AreEqual(expected, GraphicsQuality.DetectPreset(vramMb));
        }

        [Test]
        public void Presets_ScaleUpFromLowToUltra()
        {
            var low = GraphicsPrefs.ForPreset("Low");
            var medium = GraphicsPrefs.ForPreset("Medium");
            var high = GraphicsPrefs.ForPreset("High");
            var ultra = GraphicsPrefs.ForPreset("Ultra");
            Assert.IsFalse(low.Ssao || medium.Ssao);
            Assert.Less(low.Shadows, medium.Shadows);
            Assert.Less(low.RenderScale, medium.RenderScale);
            Assert.Less(low.Vegetation, medium.Vegetation);
            Assert.IsTrue(high.Ssao && ultra.Ssao);
            Assert.Less(medium.Shadows, high.Shadows);
            Assert.Less(high.Shadows, ultra.Shadows);
            Assert.Less(medium.Vegetation, ultra.Vegetation);
            Assert.AreEqual("High", GraphicsPrefs.ForPreset("nonsense").Preset);
        }

        [Test]
        public void Clamp_KeepsValuesInRange()
        {
            var p = new GraphicsPrefs { Preset = "Extreme", RenderScale = 3f, AntiAliasing = 9, Shadows = -2, Vegetation = 4f };
            p.Clamp();
            Assert.AreEqual("High", p.Preset);
            Assert.AreEqual(1f, p.RenderScale);
            Assert.AreEqual(3, p.AntiAliasing);
            Assert.AreEqual(0, p.Shadows);
            Assert.AreEqual(1f, p.Vegetation);
        }

        [Test]
        public void GameSettings_ClampCreatesGraphicsPrefsForOldSaves()
        {
            var s = new GameSettings { Graphics = null };
            s.Clamp();
            Assert.IsNotNull(s.Graphics);
        }
    }
}
