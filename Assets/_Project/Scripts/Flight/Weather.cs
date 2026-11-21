using System;

namespace Ushqyr.Flight
{
    /// <summary>The weather of a flight (EDU_PLAN F4). Plain numbers: an activity file sets them and a teacher can change them live.</summary>
    [Serializable]
    public struct WeatherSettings
    {
        /// <summary>Steady wind speed (m/s).</summary>
        public float Wind;
        /// <summary>Where the wind comes from, in degrees: 0 = from +Z (north), 90 = from +X (east).</summary>
        public float WindFromDeg;
        /// <summary>Gust strength as a share of the steady wind: 0 = none, 0.5 = gusts of half the wind speed on top.</summary>
        public float Gusts;
        /// <summary>Rain, 0..1 (pictures and sound only; it does not change the flight).</summary>
        public float Rain;
        /// <summary>How far one can see in fog (m); 0 = clear.</summary>
        public float Visibility;
        /// <summary>Air temperature (°C). Cold batteries hold less and sag more.</summary>
        public float TemperatureC;

        public static WeatherSettings Calm => new WeatherSettings { TemperatureC = 20f };
    }

    public static class Weather
    {
        /// <summary>
        /// The wind at a moment: the steady wind with gusts on top and a slow swing of direction. It is computed from
        /// the seed and the clock alone, so every PC of a class has the same wind and a replay repeats it.
        /// </summary>
        public static Vec3 WindAt(in WeatherSettings w, int seed, double time)
        {
            if (w.Wind <= 0f) return Vec3.Zero;
            float t = (float)(time % 3600.0);
            float p1 = Phase(seed, 1), p2 = Phase(seed, 2), p3 = Phase(seed, 3), p4 = Phase(seed, 4);
            // Three unrelated slow waves: together they do not repeat for a long time and never jump.
            float gust = 0.5f * MathF.Sin(t * 0.61f + p1) + 0.3f * MathF.Sin(t * 1.73f + p2) + 0.2f * MathF.Sin(t * 3.9f + p3);
            float speed = MathF.Max(0f, w.Wind * (1f + w.Gusts * gust));
            float swing = 0.25f * MathF.Min(w.Gusts * 2f, 1f) * MathF.Sin(t * 0.23f + p4);
            float from = w.WindFromDeg * MathF.PI / 180f + swing;
            // Wind FROM the north blows towards the south.
            return new Vec3(-MathF.Sin(from), 0f, -MathF.Cos(from)) * speed;
        }

        private static float Phase(int seed, int stream)
        {
            uint h = (uint)seed * 2654435761u ^ (uint)stream * 2246822519u;
            h ^= h >> 15;
            h *= 2654435761u;
            h ^= h >> 13;
            return (h & 0xFFFFFF) * (2f * MathF.PI / 16777216f);
        }

        /// <summary>
        /// The weather on the way from one setting to another (k from 0 to 1), so that a change made by the teacher
        /// comes in over a few seconds instead of hitting the drone like a wall. The wind turns the short way round.
        /// </summary>
        public static WeatherSettings Blend(in WeatherSettings a, in WeatherSettings b, float k)
        {
            k = k < 0f ? 0f : k > 1f ? 1f : k;
            float turn = (b.WindFromDeg - a.WindFromDeg) % 360f;
            if (turn > 180f) turn -= 360f;
            if (turn < -180f) turn += 360f;
            float visibility;
            if (a.Visibility <= 0f && b.Visibility <= 0f) visibility = 0f;
            else
            {
                // Clear air counts as seeing 5 km, so fog thickens gradually instead of appearing at once.
                float va = a.Visibility > 0f ? a.Visibility : 5000f, vb = b.Visibility > 0f ? b.Visibility : 5000f;
                visibility = k >= 1f && b.Visibility <= 0f ? 0f : va + (vb - va) * k;
            }
            return new WeatherSettings
            {
                Wind = a.Wind + (b.Wind - a.Wind) * k,
                WindFromDeg = ((a.WindFromDeg + turn * k) % 360f + 360f) % 360f,
                Gusts = a.Gusts + (b.Gusts - a.Gusts) * k,
                Rain = a.Rain + (b.Rain - a.Rain) * k,
                Visibility = visibility,
                TemperatureC = a.TemperatureC + (b.TemperatureC - a.TemperatureC) * k,
            };
        }

        /// <summary>
        /// The battery as it behaves at this temperature: a cold LiPo gives up less of its charge and sags more under
        /// load. At 20 °C and above nothing changes; at -20 °C about 60% of the capacity is left and the resistance doubles.
        /// </summary>
        public static BatterySettings Battery(in BatterySettings b, float temperatureC)
        {
            float cold = (20f - temperatureC) / 40f;
            cold = cold < 0f ? 0f : cold > 1f ? 1f : cold;
            BatterySettings o = b;
            o.CapacityMah = b.CapacityMah * (1f - 0.4f * cold);
            o.InternalResistance = b.InternalResistance * (1f + cold);
            return o;
        }
    }
}
