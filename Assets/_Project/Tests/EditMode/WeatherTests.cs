using System;
using NUnit.Framework;
using Ushqyr.Flight;

namespace Ushqyr.Tests
{
    /// <summary>EDU_PLAN F4: wind with gusts from a seed, smooth changes of weather, and the cold-battery effect.</summary>
    public sealed class WeatherTests
    {
        [Test]
        public void Wind_BlowsFromItsDirection_AndCalmIsExactlyZero()
        {
            Assert.AreEqual(Vec3.Zero, Weather.WindAt(WeatherSettings.Calm, 5, 12.0));
            var north = new WeatherSettings { Wind = 6f, WindFromDeg = 0f };
            Vec3 w = Weather.WindAt(north, 5, 12.0);
            Assert.AreEqual(-6f, w.z, 1e-3f, "wind from the north blows towards -Z");
            Assert.AreEqual(0f, w.x, 1e-3f);
            Assert.AreEqual(0f, w.y);
            var east = new WeatherSettings { Wind = 6f, WindFromDeg = 90f };
            Assert.AreEqual(-6f, Weather.WindAt(east, 5, 12.0).x, 1e-3f);
        }

        [Test]
        public void Gusts_AverageToTheSteadyWind_StayInRange_NeverJump_AndComeFromTheSeed()
        {
            var w = new WeatherSettings { Wind = 8f, WindFromDeg = 30f, Gusts = 0.5f };
            double sum = 0;
            float min = 99f, max = 0f, jump = 0f;
            Vec3 previous = Weather.WindAt(w, 3, 0.0);
            int n = 0;
            for (double t = 0.01; t < 600; t += 0.01)
            {
                Vec3 v = Weather.WindAt(w, 3, t);
                float speed = v.Magnitude;
                sum += speed;
                n++;
                min = Math.Min(min, speed);
                max = Math.Max(max, speed);
                jump = Math.Max(jump, (v - previous).Magnitude);
                previous = v;
            }
            Assert.AreEqual(8.0, sum / n, 0.4, "the average is the steady wind");
            Assert.Greater(max, 10.5f, "gusts are felt");
            Assert.LessOrEqual(max, 12.01f, "never more than the wind plus its gust share");
            Assert.GreaterOrEqual(min, 3.99f);
            Assert.Less(jump, 0.2f, "no jump between two moments 10 ms apart");
            Assert.AreEqual(Weather.WindAt(w, 3, 77.7), Weather.WindAt(w, 3, 77.7));
            Assert.AreNotEqual(Weather.WindAt(w, 3, 77.7), Weather.WindAt(w, 4, 77.7), "another seed, other gusts");
        }

        [Test]
        public void Blend_GoesSmoothly_AndTurnsTheWindTheShortWay()
        {
            var a = new WeatherSettings { Wind = 2f, WindFromDeg = 350f, TemperatureC = 20f };
            var b = new WeatherSettings { Wind = 8f, WindFromDeg = 30f, Gusts = 0.4f, Rain = 1f, Visibility = 200f, TemperatureC = -10f };
            WeatherSettings mid = Weather.Blend(a, b, 0.5f);
            Assert.AreEqual(5f, mid.Wind, 1e-4f);
            Assert.AreEqual(10f, mid.WindFromDeg, 1e-3f, "from 350 to 30 through north, not the long way round");
            Assert.AreEqual(0.5f, mid.Rain, 1e-4f);
            Assert.AreEqual(5f, mid.TemperatureC, 1e-4f);
            Assert.Greater(mid.Visibility, 200f, "the fog is still thickening");
            Assert.AreEqual(a.Wind, Weather.Blend(a, b, -1f).Wind);
            Assert.AreEqual(200f, Weather.Blend(a, b, 2f).Visibility);
            Assert.AreEqual(0f, Weather.Blend(b, a, 1f).Visibility, "fully clear at the end");
            Assert.AreEqual(0f, Weather.Blend(a, a, 0.5f).Visibility, "clear stays clear");
        }

        [Test]
        public void ColdBattery_HoldsLessAndSagsMore_WarmIsUnchanged()
        {
            BatterySettings b = BatterySettings.Race4S;
            Assert.AreEqual(b.CapacityMah, Weather.Battery(b, 20f).CapacityMah);
            Assert.AreEqual(b.CapacityMah, Weather.Battery(b, 35f).CapacityMah);
            BatterySettings cold = Weather.Battery(b, -20f);
            Assert.AreEqual(b.CapacityMah * 0.6f, cold.CapacityMah, 0.5f);
            Assert.AreEqual(b.InternalResistance * 2f, cold.InternalResistance, 1e-5f);
            Assert.AreEqual(b.Cells, cold.Cells);
            Assert.AreEqual(Weather.Battery(b, -20f).CapacityMah, Weather.Battery(b, -40f).CapacityMah, "no colder than the model's floor");
        }
    }
}
