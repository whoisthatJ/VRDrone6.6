using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Ushqyr.Flight;

namespace Ushqyr.Bench
{
    /// <summary>One IMU reading in the drone's body frame: x forward, y left, z up (right-handed, as in ROS).</summary>
    public struct ImuSample
    {
        /// <summary>Simulator time (s).</summary>
        public float Time;
        /// <summary>Angular velocity (rad/s): x roll right +, y nose down +, z yaw left +.</summary>
        public float Gx, Gy, Gz;
        /// <summary>Specific force (m/s²): what an accelerometer reads. At rest and level it is (0, 0, +9.81).</summary>
        public float Ax, Ay, Az;
    }

    [Serializable]
    public struct ImuSettings
    {
        public float RateHz;
        /// <summary>White noise, one standard deviation per sample.</summary>
        public float GyroNoise, AccelNoise;
        /// <summary>Constant offset drawn once per attempt, one standard deviation.</summary>
        public float GyroBias, AccelBias;
        /// <summary>Sensor range: readings saturate here.</summary>
        public float GyroLimit, AccelLimit;

        public static ImuSettings Default => new ImuSettings
        {
            RateHz = 500f,
            GyroNoise = 0.004f, AccelNoise = 0.08f,
            GyroBias = 0.003f, AccelBias = 0.04f,
            GyroLimit = 34.9f /* 2000 deg/s */, AccelLimit = 156.9f /* 16 g */,
        };
    }

    /// <summary>
    /// Gyro and accelerometer derived from the flight state, with seeded noise and a per-attempt bias.
    /// The only things it reads are the body rate, the change of velocity over a physics step and the attitude.
    /// </summary>
    public struct ImuModel
    {
        private ImuSettings _settings;
        private BenchRandom _rng;
        private Vec3 _gyroBias, _accelBias;

        public ImuModel(ImuSettings settings, long seed)
        {
            _settings = settings;
            _rng = BenchRandom.For(seed, "imu");
            _gyroBias = new Vec3(_rng.NextGaussian(), _rng.NextGaussian(), _rng.NextGaussian()) * settings.GyroBias;
            _accelBias = new Vec3(_rng.NextGaussian(), _rng.NextGaussian(), _rng.NextGaussian()) * settings.AccelBias;
        }

        /// <summary>
        /// A reading for the physics step that took the drone from <paramref name="previous"/> to <paramref name="s"/>.
        /// </summary>
        public ImuSample Read(in QuadState previous, in QuadState s, float dt, float gravity)
        {
            // Simulator body axes are X right, Y up, Z forward with left-handed rotations; see ImuSample for the output frame.
            Vec3 w = s.AngularVelocity;
            Vec3 accelWorld = (s.Velocity - previous.Velocity) / dt + new Vec3(0f, gravity, 0f);
            Vec3 f = s.Orientation.Conjugate * accelWorld;

            var o = new ImuSample { Time = (float)s.Time };
            o.Gx = Limit(-w.z + _gyroBias.x + _rng.NextGaussian() * _settings.GyroNoise, _settings.GyroLimit);
            o.Gy = Limit(w.x + _gyroBias.y + _rng.NextGaussian() * _settings.GyroNoise, _settings.GyroLimit);
            o.Gz = Limit(-w.y + _gyroBias.z + _rng.NextGaussian() * _settings.GyroNoise, _settings.GyroLimit);
            o.Ax = Limit(f.z + _accelBias.x + _rng.NextGaussian() * _settings.AccelNoise, _settings.AccelLimit);
            o.Ay = Limit(-f.x + _accelBias.y + _rng.NextGaussian() * _settings.AccelNoise, _settings.AccelLimit);
            o.Az = Limit(f.y + _accelBias.z + _rng.NextGaussian() * _settings.AccelNoise, _settings.AccelLimit);
            return o;
        }

        private static float Limit(float v, float limit) => v > limit ? limit : v < -limit ? -limit : v;
    }

    [Serializable]
    public struct TofSettings
    {
        public float RateHz;
        public float MinRange, MaxRange;
        /// <summary>Noise: a fixed part (m) plus a part that grows with distance (fraction of the range).</summary>
        public float NoiseFloor, NoisePerMetre;

        public static TofSettings Default => new TofSettings { RateHz = 50f, MinRange = 0.05f, MaxRange = 4f, NoiseFloor = 0.005f, NoisePerMetre = 0.01f };
    }

    /// <summary>Noise and range limits of the downward distance sensor; the ray itself is cast by the session.</summary>
    public struct TofModel
    {
        private TofSettings _settings;
        private BenchRandom _rng;

        public TofModel(TofSettings settings, long seed)
        {
            _settings = settings;
            _rng = BenchRandom.For(seed, "tof");
        }

        /// <summary>
        /// Turns the true distance along the sensor's ray (negative = nothing hit) into a reading.
        /// Out of range gives valid = false and a range of 0.
        /// </summary>
        public float Read(float trueDistance, out bool valid)
        {
            float noise = _rng.NextGaussian();
            if (trueDistance < 0f || trueDistance > _settings.MaxRange || trueDistance < _settings.MinRange)
            {
                valid = false;
                return 0f;
            }
            float r = trueDistance + noise * (_settings.NoiseFloor + _settings.NoisePerMetre * trueDistance);
            valid = true;
            return r < _settings.MinRange ? _settings.MinRange : r > _settings.MaxRange ? _settings.MaxRange : r;
        }
    }

    [Serializable]
    public struct AnalogVideoSettings
    {
        /// <summary>Brightness noise, one standard deviation in 0..255 units.</summary>
        public float LumaNoise;
        /// <summary>Colour noise, one standard deviation in 0..255 units.</summary>
        public float ChromaNoise;
        /// <summary>0..1: how much of the previous pixel's colour bleeds into the next one along a line (colour smear).</summary>
        public float ChromaSmear;
        /// <summary>0..1: the same for brightness (loss of fine detail).</summary>
        public float LumaSmear;
        /// <summary>Chance per line of a sideways jump.</summary>
        public float LineJitterChance;
        /// <summary>Largest sideways jump (pixels).</summary>
        public int LineJitterPixels;

        public static AnalogVideoSettings Default => new AnalogVideoSettings
        {
            LumaNoise = 5f, ChromaNoise = 4f, ChromaSmear = 0.55f, LumaSmear = 0.25f, LineJitterChance = 0.02f, LineJitterPixels = 3,
        };

        public static AnalogVideoSettings Clean => default;
    }

    /// <summary>
    /// The basic analog-video look of the FPV feed (v0): soft detail, smeared colour, noise and the odd jumping line.
    /// Runs on the CPU in whole numbers with a seeded generator per frame and per line, so a frame is identical on every
    /// computer and graphics card for the same input; the lines are processed in parallel by a Burst job.
    /// </summary>
    public struct AnalogVideo
    {
        private AnalogVideoSettings _settings;
        private ulong _seed;

        public AnalogVideo(AnalogVideoSettings settings, long seed)
        {
            _settings = settings;
            _seed = BenchRandom.For(seed, "video").NextU64();
        }

        /// <summary>
        /// Converts a bottom-up RGB24 image (as read back from the graphics card) into a top-down RGB24 image
        /// with the analog effects applied. <paramref name="frame"/> is the frame's number in the attempt.
        /// </summary>
        public void Process(NativeArray<byte> sourceBottomUp, NativeArray<byte> destinationTopDown, int width, int height, long frame)
        {
            var job = new Lines
            {
                Source = sourceBottomUp, Destination = destinationTopDown, Width = width, Height = height,
                LumaNoise = (int)(_settings.LumaNoise * 256f),
                ChromaNoise = (int)(_settings.ChromaNoise * 256f),
                ChromaKeep = (int)(Math.Clamp(_settings.ChromaSmear, 0f, 0.95f) * 256f),
                LumaKeep = (int)(Math.Clamp(_settings.LumaSmear, 0f, 0.95f) * 256f),
                JitterChance = _settings.LineJitterChance,
                JitterPixels = _settings.LineJitterPixels,
                FrameSeed = _seed + (ulong)frame * 0xD6E8FEB86659FD93UL,
            };
            job.Schedule(height, 16).Complete();
        }

        [BurstCompile]
        private struct Lines : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> Source;
            [NativeDisableParallelForRestriction] public NativeArray<byte> Destination;
            public int Width, Height, LumaNoise, ChromaNoise, ChromaKeep, LumaKeep, JitterPixels;
            public float JitterChance;
            public ulong FrameSeed;

            public void Execute(int row)
            {
                int stride = Width * 3;
                int src = (Height - 1 - row) * stride;
                int dst = row * stride;
                if (LumaNoise <= 0 && ChromaNoise <= 0 && ChromaKeep <= 0 && LumaKeep <= 0 && JitterChance <= 0f)
                {
                    for (int i = 0; i < stride; i++) Destination[dst + i] = Source[src + i];
                    return;
                }

                var rng = new BenchRandom(FrameSeed ^ ((ulong)(row + 1) * 0xA24BAED4963EE407UL));
                rng.NextU64();
                int shift = 0;
                if (JitterPixels > 0 && rng.NextFloat() < JitterChance)
                    shift = (int)(rng.NextU32() % (uint)(2 * JitterPixels + 1)) - JitterPixels;

                // Fixed-point YCbCr (8 fractional bits); the smear runs left to right like the scan of an analog line.
                int yPrev = 0, cbPrev = 0, crPrev = 0;
                ulong bits = 0;
                int bitsLeft = 0;
                for (int x = 0; x < Width; x++)
                {
                    int sx = x + shift;
                    if (sx < 0) sx = 0; else if (sx >= Width) sx = Width - 1;
                    int i = src + sx * 3;
                    int r = Source[i], g = Source[i + 1], b = Source[i + 2];
                    int y = 77 * r + 150 * g + 29 * b;
                    int cb = -43 * r - 85 * g + 128 * b;
                    int cr = 128 * r - 107 * g - 21 * b;
                    if (x > 0)
                    {
                        y = (y * (256 - LumaKeep) + yPrev * LumaKeep) >> 8;
                        cb = (cb * (256 - ChromaKeep) + cbPrev * ChromaKeep) >> 8;
                        cr = (cr * (256 - ChromaKeep) + crPrev * ChromaKeep) >> 8;
                    }
                    yPrev = y; cbPrev = cb; crPrev = cr;

                    // Noise: the sum of four uniform values is close enough to a bell curve and far cheaper.
                    if (bitsLeft < 36) { bits = rng.NextU64(); bitsLeft = 64; }
                    int n1 = (int)(bits & 7) + (int)((bits >> 3) & 7) + (int)((bits >> 6) & 7) + (int)((bits >> 9) & 7) - 14;
                    int n2 = (int)((bits >> 12) & 7) + (int)((bits >> 15) & 7) + (int)((bits >> 18) & 7) + (int)((bits >> 21) & 7) - 14;
                    int n3 = (int)((bits >> 24) & 7) + (int)((bits >> 27) & 7) + (int)((bits >> 30) & 7) + (int)((bits >> 33) & 7) - 14;
                    bits >>= 36; bitsLeft -= 36;
                    // Four values of 0..7 summed have a standard deviation of about 4.58, hence the 56 / 256.
                    int yy = y + n1 * LumaNoise * 56 / 256;
                    int cbb = cb + n2 * ChromaNoise * 56 / 256;
                    int crr = cr + n3 * ChromaNoise * 56 / 256;

                    int ro = (yy + ((359 * crr) >> 8)) >> 8;
                    int go = (yy - ((88 * cbb + 183 * crr) >> 8)) >> 8;
                    int bo = (yy + ((454 * cbb) >> 8)) >> 8;
                    int o = dst + x * 3;
                    Destination[o] = (byte)(ro < 0 ? 0 : ro > 255 ? 255 : ro);
                    Destination[o + 1] = (byte)(go < 0 ? 0 : go > 255 ? 255 : go);
                    Destination[o + 2] = (byte)(bo < 0 ? 0 : bo > 255 ? 255 : bo);
                }
            }
        }
    }
}
