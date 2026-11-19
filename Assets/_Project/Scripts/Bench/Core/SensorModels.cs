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
        /// <summary>Motor vibration: extra noise at full motor speed (it grows with the square of the speed).</summary>
        public float GyroVibration, AccelVibration;

        public static ImuSettings Default => new ImuSettings
        {
            RateHz = 500f,
            GyroNoise = 0.004f, AccelNoise = 0.08f,
            GyroBias = 0.003f, AccelBias = 0.04f,
            GyroLimit = 34.9f /* 2000 deg/s */, AccelLimit = 156.9f /* 16 g */,
            GyroVibration = 0.03f, AccelVibration = 1.2f,
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

            // The frame shakes with the motors: the faster they spin, the noisier both sensors get.
            float spin = (s.MotorSpeed.M0 + s.MotorSpeed.M1 + s.MotorSpeed.M2 + s.MotorSpeed.M3) * 0.25f;
            float gn = _settings.GyroNoise + _settings.GyroVibration * spin * spin;
            float an = _settings.AccelNoise + _settings.AccelVibration * spin * spin;

            var o = new ImuSample { Time = (float)s.Time };
            o.Gx = Limit(-w.z + _gyroBias.x + _rng.NextGaussian() * gn, _settings.GyroLimit);
            o.Gy = Limit(w.x + _gyroBias.y + _rng.NextGaussian() * gn, _settings.GyroLimit);
            o.Gz = Limit(-w.y + _gyroBias.z + _rng.NextGaussian() * gn, _settings.GyroLimit);
            o.Ax = Limit(f.z + _accelBias.x + _rng.NextGaussian() * an, _settings.AccelLimit);
            o.Ay = Limit(-f.x + _accelBias.y + _rng.NextGaussian() * an, _settings.AccelLimit);
            o.Az = Limit(f.y + _accelBias.z + _rng.NextGaussian() * an, _settings.AccelLimit);
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
    public struct LidarSettings
    {
        /// <summary>Full turns per second.</summary>
        public float RateHz;
        /// <summary>Rays per turn (360 = one per degree).</summary>
        public int Rays;
        public float MinRange, MaxRange;
        /// <summary>Noise: a fixed part (m) plus a part that grows with distance (fraction of the range).</summary>
        public float NoiseFloor, NoisePerMetre;
        /// <summary>Chance that a ray returns nothing.</summary>
        public float Dropout;

        public static LidarSettings Default => new LidarSettings
        {
            RateHz = 10f, Rays = 360, MinRange = 0.15f, MaxRange = 12f, NoiseFloor = 0.01f, NoisePerMetre = 0.005f, Dropout = 0.005f,
        };
    }

    /// <summary>
    /// Noise, dropouts and range limits of the 2D scanner. The rays are cast by the session a slice at a time while the
    /// drone moves, which is what bends a scan taken in flight. A reading of 0 means "nothing measured".
    /// Ray k points at angle k * 360 / Rays from the drone's forward axis, turning to the left (anticlockwise seen from
    /// above), in the drone's own horizontal plane.
    /// </summary>
    public struct LidarModel
    {
        private LidarSettings _settings;
        private BenchRandom _rng;

        public LidarModel(LidarSettings settings, long seed)
        {
            _settings = settings;
            _rng = BenchRandom.For(seed, "lidar");
        }

        /// <summary>Direction of ray <paramref name="k"/> in the simulator's body axes (X right, Y up, Z forward).</summary>
        public static Vec3 Direction(int k, int rays)
        {
            float a = k * (2f * MathF.PI / rays);
            return new Vec3(-MathF.Sin(a), 0f, MathF.Cos(a));
        }

        /// <summary>Turns the true distance along a ray (negative = nothing hit) into a reading.</summary>
        public float Read(float trueDistance)
        {
            float noise = _rng.NextGaussian();
            bool dropped = _rng.NextFloat() < _settings.Dropout;
            if (dropped || trueDistance < _settings.MinRange || trueDistance > _settings.MaxRange) return 0f;
            float r = trueDistance + noise * (_settings.NoiseFloor + _settings.NoisePerMetre * trueDistance);
            return r < _settings.MinRange ? _settings.MinRange : r > _settings.MaxRange ? _settings.MaxRange : r;
        }
    }

    [Serializable]
    public struct AnalogVideoSettings
    {
        /// <summary>Brightness noise, one standard deviation in 0..255 units, with a perfect signal.</summary>
        public float LumaNoise;
        /// <summary>Colour noise, one standard deviation in 0..255 units, with a perfect signal.</summary>
        public float ChromaNoise;
        /// <summary>0..1: how much of the previous pixel's colour bleeds into the next one along a line (colour smear).</summary>
        public float ChromaSmear;
        /// <summary>0..1: the same for brightness (loss of fine detail).</summary>
        public float LumaSmear;
        /// <summary>Chance per line of a sideways jump.</summary>
        public float LineJitterChance;
        /// <summary>Largest sideways jump (pixels).</summary>
        public int LineJitterPixels;
        /// <summary>How many times stronger the noise gets when the signal is at its worst.</summary>
        public float WeakSignalNoise;
        /// <summary>Chance per frame of a torn band (lines shifted together), with a perfect signal; rises as it weakens.</summary>
        public float TearChance;
        /// <summary>Largest swing of the colour phase (degrees): colours drift a little from frame to frame.</summary>
        public float HueDriftDegrees;
        /// <summary>Chance per frame, at the worst signal, that a band of the picture breaks up.</summary>
        public float BreakupChance;

        public static AnalogVideoSettings Default => new AnalogVideoSettings
        {
            LumaNoise = 5f, ChromaNoise = 4f, ChromaSmear = 0.55f, LumaSmear = 0.25f, LineJitterChance = 0.02f, LineJitterPixels = 3,
            WeakSignalNoise = 6f, TearChance = 0.01f, HueDriftDegrees = 4f, BreakupChance = 0.5f,
        };

        public static AnalogVideoSettings Clean => default;

        /// <summary>The same link with all its effects stronger or weaker by <paramref name="factor"/>.</summary>
        public AnalogVideoSettings Scaled(float factor)
        {
            AnalogVideoSettings s = this;
            s.LumaNoise *= factor;
            s.ChromaNoise *= factor;
            s.LineJitterChance *= factor;
            s.TearChance *= factor;
            s.HueDriftDegrees *= Math.Min(2f, factor);
            return s;
        }
    }

    /// <summary>
    /// The analog video link of the FPV feed: a wide lens (barrel distortion), exposure gain, soft detail, smeared
    /// colour, colour drift, noise that grows as the signal weakens, jumping lines, torn bands and, at a poor signal,
    /// bands that break up. Runs on the CPU in whole numbers with a seeded generator per frame and per line, so a frame
    /// is identical on every computer and graphics card for the same input; the lines are processed in parallel by a
    /// Burst job.
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
        /// Focal length (pixels) of the output image for a lens with radial distortion <paramref name="k1"/> whose
        /// picture spans <paramref name="hfovDegrees"/> from edge to edge. The lens model:
        /// x_u = x_d * (1 + k1 * r_d²), with x_d = (u - cx) / fx the picture's coordinates and x_u the tangent of
        /// the true angle.
        /// </summary>
        public static float LensFocal(int width, float hfovDegrees, float k1)
        {
            float edge = MathF.Tan(hfovDegrees * 0.5f * MathF.PI / 180f);
            float x = edge;
            for (int i = 0; i < 30; i++) x -= (x * (1f + k1 * x * x) - edge) / (1f + 3f * k1 * x * x);
            return width * 0.5f / x;
        }

        /// <summary>Half-extent (tangent) the undistorted source render must cover, horizontally and vertically.</summary>
        public static void LensSourceExtent(int width, int height, float focal, float k1, out float halfX, out float halfY)
        {
            float xd = width * 0.5f / focal, yd = height * 0.5f / focal;
            float scale = 1f + k1 * (xd * xd + yd * yd);
            halfX = xd * scale;
            halfY = yd * scale;
        }

        /// <summary>
        /// For every output pixel (top row first) the byte offset of its source pixel in a bottom-up RGB24 render of
        /// <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> that covers the half-extent from
        /// <see cref="LensSourceExtent"/>.
        /// </summary>
        public static void BuildLensMap(NativeArray<int> map, int width, int height, int sourceWidth, int sourceHeight, float focal, float k1)
        {
            LensSourceExtent(width, height, focal, k1, out float halfX, out float halfY);
            float fsx = sourceWidth * 0.5f / halfX, fsy = sourceHeight * 0.5f / halfY;
            for (int v = 0; v < height; v++)
            {
                float yd = (v + 0.5f - height * 0.5f) / focal;
                for (int u = 0; u < width; u++)
                {
                    float xd = (u + 0.5f - width * 0.5f) / focal;
                    float scale = 1f + k1 * (xd * xd + yd * yd);
                    int sx = (int)MathF.Floor(xd * scale * fsx + sourceWidth * 0.5f);
                    int syTop = (int)MathF.Floor(yd * scale * fsy + sourceHeight * 0.5f);
                    sx = sx < 0 ? 0 : sx >= sourceWidth ? sourceWidth - 1 : sx;
                    syTop = syTop < 0 ? 0 : syTop >= sourceHeight ? sourceHeight - 1 : syTop;
                    map[v * width + u] = ((sourceHeight - 1 - syTop) * sourceWidth + sx) * 3;
                }
            }
        }

        /// <summary>
        /// Turns a bottom-up RGB24 render into the top-down RGB24 frame the program receives.
        /// <paramref name="map"/> comes from <see cref="BuildLensMap"/>; <paramref name="frame"/> is the frame's number in
        /// the attempt; <paramref name="signal"/> is the link quality (1 = perfect, 0 = worst); <paramref name="gain"/>
        /// is the exposure gain (1 = unchanged).
        /// </summary>
        public void Process(NativeArray<byte> source, NativeArray<byte> destinationTopDown, NativeArray<int> map, int width, int height,
            long frame, float signal = 1f, float gain = 1f)
        {
            signal = signal < 0f ? 0f : signal > 1f ? 1f : signal;
            float weak = (1f - signal) * (1f - signal);
            float noiseScale = 1f + (_settings.WeakSignalNoise - 1f) * weak;
            if (_settings.WeakSignalNoise <= 0f) noiseScale = 1f;
            ulong frameSeed = _seed + (ulong)frame * 0xD6E8FEB86659FD93UL;

            // Per-frame events, drawn on the main thread from the frame's own stream.
            var rng = new BenchRandom(frameSeed ^ 0x5851F42D4C957F2DUL);
            rng.NextU64();
            float hue = (rng.NextFloat() * 2f - 1f) * _settings.HueDriftDegrees * (1f + 2f * weak) * MathF.PI / 180f;
            bool tear = rng.NextFloat() < _settings.TearChance * (1f + 20f * weak);
            int tearRow = (int)(rng.NextFloat() * height), tearRows = 4 + (int)(rng.NextFloat() * height * 0.12f);
            int tearShift = (int)((rng.NextFloat() * 2f - 1f) * width * 0.06f);
            bool breakup = rng.NextFloat() < _settings.BreakupChance * weak;
            int breakRow = (int)(rng.NextFloat() * height), breakRows = 6 + (int)(rng.NextFloat() * height * 0.2f);

            var job = new Lines
            {
                Source = source, Destination = destinationTopDown, Map = map, Width = width, Height = height,
                LumaNoise = (int)(_settings.LumaNoise * noiseScale * 256f),
                ChromaNoise = (int)(_settings.ChromaNoise * noiseScale * 256f),
                ChromaKeep = (int)(Math.Clamp(_settings.ChromaSmear, 0f, 0.95f) * 256f),
                LumaKeep = (int)(Math.Clamp(_settings.LumaSmear, 0f, 0.95f) * 256f),
                JitterChance = _settings.LineJitterChance * (1f + 6f * weak),
                JitterPixels = _settings.LineJitterPixels,
                Gain = (int)(Math.Clamp(gain, 0.25f, 4f) * 256f),
                HueCos = (int)(MathF.Cos(hue) * 256f), HueSin = (int)(MathF.Sin(hue) * 256f),
                TearFrom = tear ? tearRow : -1, TearTo = tear ? tearRow + tearRows : -1, TearShift = tearShift,
                BreakFrom = breakup ? breakRow : -1, BreakTo = breakup ? breakRow + breakRows : -1,
                FrameSeed = frameSeed,
            };
            job.Schedule(height, 16).Complete();
        }

        [BurstCompile]
        private struct Lines : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> Source;
            [ReadOnly] public NativeArray<int> Map;
            [NativeDisableParallelForRestriction] public NativeArray<byte> Destination;
            public int Width, Height, LumaNoise, ChromaNoise, ChromaKeep, LumaKeep, JitterPixels, Gain, HueCos, HueSin;
            public int TearFrom, TearTo, TearShift, BreakFrom, BreakTo;
            public float JitterChance;
            public ulong FrameSeed;

            public void Execute(int row)
            {
                int dst = row * Width * 3;
                int mapRow = row * Width;
                bool plain = LumaNoise <= 0 && ChromaNoise <= 0 && ChromaKeep <= 0 && LumaKeep <= 0 && JitterChance <= 0f
                             && Gain == 256 && HueSin == 0 && TearFrom < 0 && BreakFrom < 0;
                if (plain)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        int i = Map[mapRow + x], o = dst + x * 3;
                        Destination[o] = Source[i]; Destination[o + 1] = Source[i + 1]; Destination[o + 2] = Source[i + 2];
                    }
                    return;
                }

                var rng = new BenchRandom(FrameSeed ^ ((ulong)(row + 1) * 0xA24BAED4963EE407UL));
                rng.NextU64();
                int shift = 0;
                if (JitterPixels > 0 && rng.NextFloat() < JitterChance)
                    shift = (int)(rng.NextU32() % (uint)(2 * JitterPixels + 1)) - JitterPixels;
                if (row >= TearFrom && row < TearTo) shift += TearShift;
                bool broken = row >= BreakFrom && row < BreakTo;
                if (broken) shift += (int)(rng.NextU32() % (uint)(Width / 3 + 1)) - Width / 6;
                int lumaNoise = broken ? LumaNoise * 6 + 9000 : LumaNoise, chromaNoise = broken ? ChromaNoise * 6 + 9000 : ChromaNoise;

                // Fixed-point YCbCr (8 fractional bits); the smear runs left to right like the scan of an analog line.
                int yPrev = 0, cbPrev = 0, crPrev = 0;
                ulong bits = 0;
                int bitsLeft = 0;
                for (int x = 0; x < Width; x++)
                {
                    int sx = x + shift;
                    if (sx < 0) sx = 0; else if (sx >= Width) sx = Width - 1;
                    int i = Map[mapRow + sx];
                    int r = (Source[i] * Gain) >> 8, g = (Source[i + 1] * Gain) >> 8, b = (Source[i + 2] * Gain) >> 8;
                    if (r > 255) r = 255;
                    if (g > 255) g = 255;
                    if (b > 255) b = 255;
                    int y = 77 * r + 150 * g + 29 * b;
                    int cb0 = -43 * r - 85 * g + 128 * b;
                    int cr0 = 128 * r - 107 * g - 21 * b;
                    // Colour phase error of the link: a small rotation of the colour vector.
                    int cb = (cb0 * HueCos - cr0 * HueSin) >> 8;
                    int cr = (cb0 * HueSin + cr0 * HueCos) >> 8;
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
                    int yy = y + n1 * lumaNoise * 56 / 256;
                    int cbb = cb + n2 * chromaNoise * 56 / 256;
                    int crr = cr + n3 * chromaNoise * 56 / 256;

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
