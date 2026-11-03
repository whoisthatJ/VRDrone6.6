using GateRush.Core;
using NUnit.Framework;
using UnityEngine;

namespace GateRush.Tests
{
    public class GraphicsPrefsTests
    {
        [Test]
        public void FirstLaunch_DefaultsToHigh()
        {
            Assert.AreEqual("High", GraphicsQuality.DefaultPreset);
            var first = new GraphicsPrefs().WithPreset(GraphicsQuality.DefaultPreset);
            Assert.AreEqual("High", first.Preset);
            Assert.AreEqual(2, first.Shadows);
            Assert.AreEqual(ScreenDisplay.Fullscreen, first.DisplayMode);
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

        [Test]
        public void Display_DefaultsToFullscreenAtNative_AndSurvivesPresetChanges()
        {
            var p = GraphicsPrefs.ForPreset("High");
            Assert.AreEqual(ScreenDisplay.Fullscreen, p.DisplayMode, "the build opens fullscreen");
            var native = new Vector2Int(2560, 1440);
            Assert.AreEqual(native, ScreenDisplay.TargetSize(p, native));

            p.DisplayMode = ScreenDisplay.Windowed;
            Assert.AreEqual(new Vector2Int(1920, 1080), ScreenDisplay.TargetSize(p, native), "a native window gets 75 %");
            p.ResolutionWidth = 1600;
            p.ResolutionHeight = 900;
            Assert.AreEqual(new Vector2Int(1600, 900), ScreenDisplay.TargetSize(p, native));
            p.DisplayMode = ScreenDisplay.Maximized;
            Assert.AreEqual(native, ScreenDisplay.TargetSize(p, native), "maximized fills the desktop");

            var low = p.WithPreset("Low");
            Assert.AreEqual(ScreenDisplay.Maximized, low.DisplayMode);
            Assert.AreEqual(1600, low.ResolutionWidth);
            Assert.AreEqual(0, low.Shadows, "quality values come from the preset");

            var bad = new GraphicsPrefs { DisplayMode = 7, ResolutionWidth = 100, ResolutionHeight = 50 };
            bad.Clamp();
            Assert.AreEqual(ScreenDisplay.Windowed, bad.DisplayMode);
            Assert.AreEqual(0, bad.ResolutionWidth);
        }
    }
}
