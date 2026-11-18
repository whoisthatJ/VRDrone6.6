using System;

namespace Ushqyr.Bench
{
    /// <summary>
    /// Seeded generator for sensor noise (SplitMix64). Every sensor owns one, derived from the attempt seed and the
    /// sensor's name, so the same seed gives the same noise on any machine and sensors don't disturb each other.
    /// </summary>
    public struct BenchRandom
    {
        private ulong _state;
        private bool _hasSpare;
        private float _spare;

        public BenchRandom(ulong seed)
        {
            _state = seed;
            _hasSpare = false;
            _spare = 0f;
        }

        /// <summary>A stream for one sensor: the attempt seed mixed with the sensor's name.</summary>
        public static BenchRandom For(long seed, string stream)
        {
            ulong h = 1469598103934665603UL ^ (ulong)seed;
            for (int i = 0; i < stream.Length; i++) h = (h ^ stream[i]) * 1099511628211UL;
            var r = new BenchRandom(h);
            r.NextU64();
            return r;
        }

        public ulong NextU64()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public uint NextU32() => (uint)(NextU64() >> 32);

        /// <summary>Uniform in [0, 1).</summary>
        public float NextFloat() => (NextU64() >> 40) * (1f / 16777216f);

        /// <summary>Normal distribution, mean 0, standard deviation 1.</summary>
        public float NextGaussian()
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }
            float u, v, s;
            do
            {
                u = NextFloat() * 2f - 1f;
                v = NextFloat() * 2f - 1f;
                s = u * u + v * v;
            } while (s >= 1f || s == 0f);
            float m = MathF.Sqrt(-2f * MathF.Log(s) / s);
            _spare = v * m;
            _hasSpare = true;
            return u * m;
        }
    }
}
